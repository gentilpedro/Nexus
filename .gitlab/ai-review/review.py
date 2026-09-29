"""Revisor automático de MRs (issue #43).

Roda no job `ai-review` da pipeline de MR:

1. pula se este commit já foi revisado (marcador no comentário-resumo);
2. monta o diff do MR e, se houver revisão anterior, o diff só do que mudou desde ela;
3. roda o Claude Code só com ferramentas de leitura e com um ambiente sem nenhum segredo além da
   chave da própria API;
4. posta o resumo e os comentários nas linhas, depois de remover qualquer segredo da resposta,
   e marca o bot como revisor.

O token do GitLab fica só com este script. O modelo lê conteúdo controlado por quem abriu o MR,
então parte do princípio de que ele pode ser manipulado: sem shell, sem escrita, sem rede, sem o
token no ambiente — e mesmo assim a saída passa por uma redação antes de virar comentário.
"""

import json
import os
import re
import shutil
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request

MARKER = re.compile(r"<!-- ai-review sha=([0-9a-f]{40}) -->")
WORKDIR = "ai-review"
MAX_INLINE = 10
CLAUDE_TIMEOUT_SECONDS = 15 * 60

API = os.environ["CI_API_V4_URL"]
PROJECT = os.environ["CI_PROJECT_ID"]
MR_IID = os.environ["CI_MERGE_REQUEST_IID"]
HEAD_SHA = os.environ["CI_COMMIT_SHA"]
TARGET = os.environ["CI_MERGE_REQUEST_TARGET_BRANCH_NAME"]
TOKEN = os.environ["GITLAB_REVIEW_TOKEN"]
MODEL = os.environ.get("AI_REVIEW_MODEL") or "sonnet"
# Ensaio: roda tudo, mas imprime a revisão em vez de postar. Para testar o prompt localmente.
DRY_RUN = os.environ.get("AI_REVIEW_DRY_RUN") == "1"
HERE = os.path.dirname(os.path.abspath(__file__))


def api(method, path, data=None):
    if DRY_RUN and method != "GET":
        print(f"\n[ensaio] {method} {path}\n{(data or {}).get('body', data)}\n")
        return {}
    body = urllib.parse.urlencode(data, doseq=True).encode() if data is not None else None
    request = urllib.request.Request(f"{API}/projects/{PROJECT}{path}", data=body, method=method,
                                     headers={"PRIVATE-TOKEN": TOKEN})
    with urllib.request.urlopen(request, timeout=60) as response:
        return json.load(response)


def api_all(path):
    items, page = [], 1
    while True:
        sep = "&" if "?" in path else "?"
        batch = api("GET", f"{path}{sep}per_page=100&page={page}")
        items.extend(batch)
        if len(batch) < 100:
            return items
        page += 1


def git(*args):
    return subprocess.run(["git", *args], check=True, capture_output=True, text=True, encoding="utf-8").stdout


# --------------------------------------------------------------------------------------------
# Segredos: nada que se pareça com um token sai deste job como comentário.
# --------------------------------------------------------------------------------------------

SECRET_PATTERNS = [
    re.compile(r"glpat-[\w-]{16,}"),
    re.compile(r"gh[pousr]_[A-Za-z0-9]{30,}"),
    re.compile(r"github_pat_[A-Za-z0-9_]{30,}"),
    re.compile(r"sk-ant-[\w-]{20,}"),
]
SECRET_VALUES = [v for k, v in os.environ.items()
                 if v and len(v) >= 12 and (k.endswith("TOKEN") or k.endswith("KEY") or k.endswith("PASSWORD") or k.endswith("SECRET"))]


def redact(text):
    for value in SECRET_VALUES:
        text = text.replace(value, "[removido]")
    for pattern in SECRET_PATTERNS:
        text = pattern.sub("[removido]", text)
    return text


# --------------------------------------------------------------------------------------------
# Revisão
# --------------------------------------------------------------------------------------------

def previous_review(notes):
    """SHA da última revisão deste bot neste MR, ou None."""
    for note in sorted(notes, key=lambda n: n["created_at"], reverse=True):
        match = MARKER.search(note.get("body") or "")
        if match:
            return match.group(1)
    return None


def build_prompt(incremental_available):
    template = open(os.path.join(HERE, "prompt.md"), encoding="utf-8").read()
    note = ("o diff só do que mudou desde a última revisão deste MR. Aponte achados apenas em "
            "linhas desse diff; o diff completo serve de contexto."
            if incremental_available else "não há (primeira revisão deste MR).")
    return (template
            .replace("{{DIFF_FILE}}", f"{WORKDIR}/mr.diff")
            .replace("{{INCREMENTAL_FILE}}", f"{WORKDIR}/incremental.diff")
            .replace("{{INCREMENTAL_NOTE}}", note)
            .replace("{{TARGET_BRANCH}}", TARGET))


def run_claude(prompt):
    settings = {
        "permissions": {
            # Além de só permitir leitura, nega ler o que está fora do repositório: /proc guarda o
            # ambiente dos outros processos do job, onde os tokens estão.
            "deny": ["Bash", "Edit", "Write", "NotebookEdit", "WebFetch", "WebSearch",
                     "Read(//proc/**)", "Read(//etc/**)", "Read(//root/**)", "Read(//builds/**/.git/**)"],
        }
    }
    command = [
        # O prompt vai pela entrada padrão: como argumento, o cmd.exe do Windows corta na primeira
        # quebra de linha, e prompt longo esbarra no limite de tamanho da linha de comando.
        shutil.which("claude") or "claude", "-p", "Siga as instruções recebidas pela entrada padrão.",
        "--output-format", "json",
        "--model", MODEL,
        "--max-turns", "40",
        "--allowedTools", "Read,Grep,Glob",
        "--settings", json.dumps(settings),
    ]
    # Ambiente mínimo: o processo do Claude não recebe o token do GitLab, o CI_JOB_TOKEN nem
    # nenhuma outra variável da pipeline.
    env = {
        "PATH": os.environ["PATH"],
        "HOME": os.environ.get("HOME", "/root"),
        "LANG": "C.UTF-8",
        "ANTHROPIC_API_KEY": os.environ["ANTHROPIC_API_KEY"],
        "DISABLE_AUTOUPDATER": "1",
    } if not DRY_RUN else {k: v for k, v in os.environ.items() if k != "GITLAB_REVIEW_TOKEN"}
    result = subprocess.run(command, input=prompt, env=env, capture_output=True, text=True,
                            encoding="utf-8", timeout=CLAUDE_TIMEOUT_SECONDS)
    if result.returncode != 0:
        print(redact(result.stderr[-2000:]), file=sys.stderr)
        raise SystemExit("O Claude Code terminou com erro.")
    envelope = json.loads(result.stdout)
    return envelope.get("result") or "", envelope.get("total_cost_usd")


def parse_review(text):
    """Primeiro objeto JSON da resposta, aceitando cerca de código em volta."""
    decoder = json.JSONDecoder()
    for start in (i for i, ch in enumerate(text) if ch == "{"):
        try:
            value, _ = decoder.raw_decode(text[start:])
        except json.JSONDecodeError:
            continue
        if isinstance(value, dict) and "summary" in value:
            return value
    raise SystemExit("A resposta do revisor não trouxe o JSON esperado.")


# --------------------------------------------------------------------------------------------
# Publicação
# --------------------------------------------------------------------------------------------

SEVERITY_ORDER = {"bloqueante": 0, "importante": 1, "sugestão": 2}


def post_inline(finding, diff_refs, old_paths):
    body = redact(f"**[{finding['severity']}] {finding['title']}**\n\n{finding['body']}")
    new_path = finding["file"]
    data = {
        "body": body,
        "position[position_type]": "text",
        "position[base_sha]": diff_refs["base_sha"],
        "position[start_sha]": diff_refs["start_sha"],
        "position[head_sha]": diff_refs["head_sha"],
        "position[new_path]": new_path,
        "position[old_path]": old_paths.get(new_path, new_path),
        "position[new_line]": int(finding["line"]),
    }
    try:
        api("POST", f"/merge_requests/{MR_IID}/discussions", data)
        return True
    except urllib.error.HTTPError:
        return False  # linha fora do diff: o achado vai para o resumo


def main():
    os.makedirs(WORKDIR, exist_ok=True)
    notes = api_all(f"/merge_requests/{MR_IID}/notes?sort=desc")
    last = previous_review(notes)
    if last == HEAD_SHA:
        print(f"O commit {HEAD_SHA[:7]} já foi revisado. Nada a fazer.")
        return

    git("fetch", "--quiet", "origin", TARGET)
    with open(f"{WORKDIR}/mr.diff", "w", encoding="utf-8") as f:
        f.write(git("diff", f"origin/{TARGET}...HEAD"))

    incremental = False
    if last:
        ancestor = subprocess.run(["git", "merge-base", "--is-ancestor", last, "HEAD"], capture_output=True).returncode == 0
        if ancestor:
            with open(f"{WORKDIR}/incremental.diff", "w", encoding="utf-8") as f:
                f.write(git("diff", f"{last}..HEAD"))
            incremental = True

    text, cost = run_claude(build_prompt(incremental))
    review = parse_review(text)

    mr = api("GET", f"/merge_requests/{MR_IID}")
    diff_refs = mr["diff_refs"]
    old_paths = {d["new_path"]: d["old_path"] for d in api_all(f"/merge_requests/{MR_IID}/diffs")}

    findings = sorted(review.get("findings") or [], key=lambda f: SEVERITY_ORDER.get(f.get("severity"), 3))[:MAX_INLINE]
    leftovers = []
    for finding in findings:
        if not all(finding.get(k) for k in ("file", "severity", "title", "body")):
            continue
        if finding.get("line") and post_inline(finding, diff_refs, old_paths):
            continue
        leftovers.append(finding)

    counts = {s: sum(1 for f in findings if f.get("severity") == s) for s in SEVERITY_ORDER}
    lines = ["### Revisão automática", "", review["summary"], ""]
    if findings:
        lines.append("**Achados:** " + ", ".join(f"{n} {s}" for s, n in counts.items() if n) +
                     (" — comentados nas linhas do diff." if len(leftovers) < len(findings) else "."))
    else:
        lines.append("Nenhum achado.")
    if leftovers:
        lines += ["", "Sem linha específica no diff:"]
        lines += [f"- **[{f['severity']}] {f['title']}** (`{f['file']}`) — {f['body']}" for f in leftovers]
    scope = f"mudanças desde `{last[:7]}`" if incremental else "MR completo"
    footer = f"Revisor IA · commit `{HEAD_SHA[:7]}` · {scope}"
    if cost is not None:
        footer += f" · US$ {cost:.2f}"
    footer += " · só aconselha: não aprova nem bloqueia o merge."
    lines += ["", f"<sub>{footer}</sub>", f"<!-- ai-review sha={HEAD_SHA} -->"]
    api("POST", f"/merge_requests/{MR_IID}/notes", {"body": redact("\n".join(lines))})

    # O bot aparece como revisor do MR, sem tirar os revisores que já estavam lá.
    try:
        me = json.load(urllib.request.urlopen(urllib.request.Request(f"{API}/user", headers={"PRIVATE-TOKEN": TOKEN}), timeout=30))
        reviewers = {r["id"] for r in mr.get("reviewers", [])}
        if me["id"] not in reviewers:
            api("PUT", f"/merge_requests/{MR_IID}", {"reviewer_ids[]": sorted(reviewers | {me["id"]})})
    except urllib.error.HTTPError as error:
        print(f"Não foi possível marcar o bot como revisor ({error.code}); a revisão foi publicada.")

    print(f"Revisão publicada: {len(findings)} achado(s), {len(findings) - len(leftovers)} nas linhas.")


if __name__ == "__main__":
    main()
