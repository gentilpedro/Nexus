"""Espelha as issues do GitLab (o repositório do TCC) neste repositório do GitHub.

Chamado pelo workflow .github/workflows/sync-gitlab-issues.yml. Sincronização completa e
idempotente, do GitLab para o GitHub:

- issue que ainda não existe aqui é criada;
- título, descrição, rótulos e estado (aberta, concluída, não planejada) são atualizados;
- comentários novos são copiados, editados lá são editados aqui, apagados lá são apagados aqui.

Cada issue e cada comentário espelhados carregam um marcador HTML com o id de origem, que é como o
script reconhece o que já existe. Rodar de novo sem nada ter mudado não altera nada.

Não usa o conteúdo do webhook que dispara o workflow: lê tudo da API pública do GitLab. Um evento
forjado, no máximo, provoca uma sincronização a mais.

Variáveis: GITHUB_TOKEN, GITHUB_REPOSITORY (o Actions define as duas), GITLAB_PROJECT (caminho do
projeto), GITLAB_READ_TOKEN (token do GitLab só com read_api; sem ele as issues são espelhadas,
mas os comentários não — a API do GitLab exige autenticação para ler comentários mesmo em
projeto público) e, opcional, SYNC_DRY_RUN=1 para só imprimir o que faria.
"""

import json
import os
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timedelta, timezone

GITLAB_PROJECT = os.environ.get("GITLAB_PROJECT", "senac-projetos-de-desenvolvimento/2025-pedro-gentil/tcc_2/nexus")
GITLAB_API = f"https://gitlab.com/api/v4/projects/{urllib.parse.quote(GITLAB_PROJECT, safe='')}"
GITLAB_WEB = f"https://gitlab.com/{GITLAB_PROJECT}"
GITHUB_API = f"https://api.github.com/repos/{os.environ.get('GITHUB_REPOSITORY', 'gentilpedro/Nexus')}"
DRY_RUN = os.environ.get("SYNC_DRY_RUN") == "1"
GITLAB_TOKEN = os.environ.get("GITLAB_READ_TOKEN") or None

ISSUE_MARKER = re.compile(r"<!-- gitlab-issue:(\d+) -->")
NOTE_MARKER = re.compile(r"<!-- gitlab-note:(\d+) -->")
# Horário de Brasília nas datas escritas nos cabeçalhos.
BRT = timezone(timedelta(hours=-3))
# Conta do GitHub que recebe as issues atribuídas a este usuário do GitLab.
ASSIGNEES = {"gentilpedro": "gentilpedro"}


# ------------------------------------------------------------------------------------------------
# APIs
# ------------------------------------------------------------------------------------------------

def gitlab_all(path):
    """Lista paginada da API do GitLab; com GITLAB_READ_TOKEN, autenticada (só leitura)."""
    items, page = [], 1
    headers = {"PRIVATE-TOKEN": GITLAB_TOKEN} if GITLAB_TOKEN else {}
    while True:
        sep = "&" if "?" in path else "?"
        request = urllib.request.Request(f"{GITLAB_API}{path}{sep}per_page=100&page={page}", headers=headers)
        with urllib.request.urlopen(request, timeout=30) as r:
            batch = json.load(r)
        items.extend(batch)
        if len(batch) < 100:
            return items
        page += 1


def github(method, path, data=None):
    if DRY_RUN and method != "GET":
        print(f"  [ensaio] {method} {path} {json.dumps(data, ensure_ascii=False)[:160] if data else ''}")
        return {"number": 0}
    request = urllib.request.Request(
        path if path.startswith("https://") else GITHUB_API + path,
        data=json.dumps(data).encode() if data is not None else None,
        method=method,
        headers={
            "Authorization": f"Bearer {os.environ['GITHUB_TOKEN']}",
            "Accept": "application/vnd.github+json",
            "X-GitHub-Api-Version": "2022-11-28",
            "Content-Type": "application/json",
        },
    )
    for attempt in range(5):
        try:
            with urllib.request.urlopen(request, timeout=30) as r:
                raw = r.read()
                return json.loads(raw) if raw else None
        except urllib.error.HTTPError as error:
            # Limite secundário do GitHub para criação de conteúdo em sequência.
            if error.code in (403, 429) and attempt < 4 and (error.headers.get("Retry-After") or "rate limit" in error.read().decode(errors="replace").lower()):
                wait = int(error.headers.get("Retry-After") or 60)
                print(f"  limite do GitHub; esperando {wait}s", flush=True)
                time.sleep(wait)
                continue
            raise


def github_all(path):
    items, page = [], 1
    while True:
        sep = "&" if "?" in path else "?"
        batch = github("GET", f"{path}{sep}per_page=100&page={page}")
        items.extend(batch)
        if len(batch) < 100:
            return items
        page += 1


# ------------------------------------------------------------------------------------------------
# Conteúdo
# ------------------------------------------------------------------------------------------------

def when(iso):
    return datetime.fromisoformat(iso.replace("Z", "+00:00")).astimezone(BRT).strftime("%d/%m/%Y %H:%M")


def convert(text, max_issue, max_mr):
    """Reescreve #N e !N (referências do GitLab) como links para o GitLab.

    Aqui, #N apontaria para a issue ou o PR de número N deste repositório, que é outra coisa: a
    numeração do GitHub é compartilhada com os PRs e não bate com a do GitLab. Trechos de código
    ficam intactos, e números além do maior id existente (cores como #111827) também.
    """
    if not text:
        return ""
    parts = re.split(r"(```.*?```|`[^`\n]*`)", text, flags=re.S)
    for i in range(0, len(parts), 2):
        part = re.sub(
            r"(?<![\w/&#\[])#(\d+)\b",
            lambda m: f"[GitLab #{m.group(1)}]({GITLAB_WEB}/-/issues/{m.group(1)})" if int(m.group(1)) <= max_issue else m.group(0),
            parts[i])
        parts[i] = re.sub(
            r"(?<![\w!\]\[])!(\d+)\b",
            lambda m: f"[GitLab !{m.group(1)}]({GITLAB_WEB}/-/merge_requests/{m.group(1)})" if int(m.group(1)) <= max_mr else m.group(0),
            part)
    return "".join(parts)


def issue_body(issue, max_issue, max_mr):
    header = (f"> Espelho da issue [#{issue['iid']}]({issue['web_url']}) do GitLab · aberta em {when(issue['created_at'])}"
              + (f" · fechada em {when(issue['closed_at'])}" if issue.get("closed_at") else "")
              + "\n> Edite e comente no GitLab: o que for alterado aqui é sobrescrito na próxima sincronização.\n\n")
    return header + convert(issue.get("description"), max_issue, max_mr) + f"\n\n<!-- gitlab-issue:{issue['iid']} -->"


def note_text(note, max_issue, max_mr):
    """Comentário como a importação inicial escreveu, sem o marcador."""
    return f"> Comentário de {when(note['created_at'])}\n\n" + convert(note["body"], max_issue, max_mr)


def note_body(note, max_issue, max_mr):
    return note_text(note, max_issue, max_mr) + f"\n\n<!-- gitlab-note:{note['id']} -->"


def desired_state(issue):
    if issue["state"] != "closed":
        return "open", None
    # O GitLab não diferencia "concluída" de "não planejada"; o rótulo wontfix, se usado, decide.
    return "closed", "not_planned" if "wontfix" in issue["labels"] else "completed"


# ------------------------------------------------------------------------------------------------
# Sincronização
# ------------------------------------------------------------------------------------------------

def sync_labels(issues, gitlab_labels):
    github_labels = {label["name"] for label in github_all("/labels")}
    for name in sorted({name for issue in issues for name in issue["labels"]} - github_labels):
        label = gitlab_labels.get(name, {})
        print(f"rótulo novo: {name}")
        github("POST", "/labels", {
            "name": name,
            "color": (label.get("color") or "#ededed").lstrip("#"),
            "description": (label.get("description") or "")[:100],
        })


def sync_comments(number, notes, max_issue, max_mr):
    comments = github_all(f"/issues/{number}/comments")
    by_note = {}
    unmarked = []
    for comment in comments:
        match = NOTE_MARKER.search(comment.get("body") or "")
        if match:
            by_note[int(match.group(1))] = comment
        else:
            unmarked.append(comment)

    changes = 0
    for note in notes:
        wanted = note_body(note, max_issue, max_mr)
        comment = by_note.pop(note["id"], None)
        if comment is None:
            # Comentário da importação inicial, feito antes dos marcadores: adota em vez de duplicar.
            text = note_text(note, max_issue, max_mr).strip()
            comment = next((c for c in unmarked if (c.get("body") or "").strip() == text), None)
            if comment is not None:
                unmarked.remove(comment)
        if comment is None:
            github("POST", f"/issues/{number}/comments", {"body": wanted})
            changes += 1
            time.sleep(1)
        elif comment["body"] != wanted:
            github("PATCH", comment["url"], {"body": wanted})
            changes += 1
            time.sleep(0.5)

    # Sobrou comentário com marcador de uma nota que não existe mais no GitLab: foi apagada lá.
    for comment in by_note.values():
        github("DELETE", comment["url"])
        changes += 1
    return changes


def main():
    # with_labels_details traz cor e descrição de cada rótulo; o endpoint /labels exige token, e
    # este script roda sem nenhum token do GitLab.
    issues = sorted(gitlab_all("/issues?scope=all&with_labels_details=true"), key=lambda i: i["iid"])
    gitlab_labels = {label["name"]: label for issue in issues for label in issue["labels"]}
    for issue in issues:
        issue["labels"] = [label["name"] for label in issue["labels"]]
    if not issues:
        print("Nenhuma issue no GitLab.")
        return
    max_issue = max(i["iid"] for i in issues)
    merge_requests = gitlab_all("/merge_requests?scope=all&state=all")
    max_mr = max((m["iid"] for m in merge_requests), default=0)

    sync_labels(issues, gitlab_labels)

    mirrors = {}
    for item in github_all("/issues?state=all"):
        match = ISSUE_MARKER.search(item.get("body") or "")
        if match and "pull_request" not in item:
            mirrors[int(match.group(1))] = item

    if not GITLAB_TOKEN:
        print("::warning::GITLAB_READ_TOKEN não configurado: espelhando só as issues, sem os comentários.")

    created = updated = 0
    for issue in issues:
        notes = (sorted((n for n in gitlab_all(f"/issues/{issue['iid']}/notes") if not n["system"]), key=lambda n: n["created_at"])
                 if GITLAB_TOKEN else None)
        body = issue_body(issue, max_issue, max_mr)
        state, reason = desired_state(issue)
        assignees = sorted({ASSIGNEES[a["username"]] for a in issue.get("assignees", []) if a["username"] in ASSIGNEES})
        mirror = mirrors.get(issue["iid"])

        if mirror is None:
            number = github("POST", "/issues", {"title": issue["title"], "body": body, "labels": issue["labels"], "assignees": assignees})["number"]
            if notes and not DRY_RUN:
                sync_comments(number, notes, max_issue, max_mr)
            if state == "closed":
                github("PATCH", f"/issues/{number}", {"state": "closed", "state_reason": reason})
            print(f"GitLab #{issue['iid']} → GitHub #{number} (nova)", flush=True)
            created += 1
            time.sleep(1)
            continue

        patch = {}
        if mirror["title"] != issue["title"]:
            patch["title"] = issue["title"]
        if (mirror.get("body") or "") != body:
            patch["body"] = body
        if sorted(label["name"] for label in mirror["labels"]) != sorted(issue["labels"]):
            patch["labels"] = issue["labels"]
        if sorted(a["login"] for a in mirror.get("assignees", [])) != assignees:
            patch["assignees"] = assignees
        # O motivo (concluída ou não planejada) só é definido na hora de fechar: quem ajustar o motivo
        # à mão aqui não tem o ajuste desfeito a cada sincronização.
        if mirror["state"] != state:
            patch["state"] = state
            if state == "closed":
                patch["state_reason"] = reason
        if patch:
            github("PATCH", f"/issues/{mirror['number']}", patch)
            time.sleep(0.5)

        # Sem token (notes é None) os comentários ficam como estão: não dá para saber o que mudou.
        comment_changes = sync_comments(mirror["number"], notes, max_issue, max_mr) if notes is not None and (notes or mirror["comments"]) else 0
        if patch or comment_changes:
            print(f"GitLab #{issue['iid']} → GitHub #{mirror['number']}: {', '.join(patch) or 'comentários'}", flush=True)
            updated += 1

    print(f"Pronto: {created} criada(s), {updated} atualizada(s), {len(issues) - created - updated} sem mudança.")


if __name__ == "__main__":
    try:
        main()
    except urllib.error.HTTPError as error:
        sys.exit(f"Falha na API ({error.code}): {error.url}")
