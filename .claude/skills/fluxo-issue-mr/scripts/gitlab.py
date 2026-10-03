"""Chamadas à API do GitLab usadas pela skill fluxo-issue-mr.

O token é lido de ~/.gitlab-token (nunca de argumento, variável de ambiente ou do chat) e nunca é
impresso. Textos longos (descrição de issue/MR) entram por arquivo, para não brigar com aspas e
acentos no shell do Windows.

Uso:
  python gitlab.py criar-issue  --titulo T --descricao-arquivo F [--labels a,b]
  python gitlab.py criar-branch --nome N [--ref main]
  python gitlab.py criar-mr     --branch N --titulo T --descricao-arquivo F [--labels a,b] [--draft]
  python gitlab.py atualizar-mr --iid N [--titulo T] [--descricao-arquivo F] [--pronto] [--labels a,b]

Todo MR criado ou marcado como pronto fica com o dono do token como responsável e revisor.
  python gitlab.py labels-issue --iid N --adicionar a,b [--remover c,d]
  python gitlab.py ver-issue    --iid N
"""

import argparse
import functools
import json
import pathlib
import sys
import urllib.error
import urllib.parse
import urllib.request

PROJETO = "senac-projetos-de-desenvolvimento/2025-pedro-gentil/tcc_2/nexus"
API = "https://gitlab.com/api/v4/projects/" + urllib.parse.quote(PROJETO, safe="")
ARQUIVO_TOKEN = pathlib.Path.home() / ".gitlab-token"


def token() -> str:
    if not ARQUIVO_TOKEN.exists():
        sys.exit(f"Token não encontrado em {ARQUIVO_TOKEN}. Peça ao usuário para salvá-lo lá "
                 "(num terminal próprio, fora do chat).")
    # Remove BOM e CRLF que o Notepad deixa no arquivo.
    return ARQUIVO_TOKEN.read_text(encoding="utf-8-sig").strip()


def chamar(metodo: str, caminho: str, corpo: dict | None = None) -> dict:
    dados = json.dumps(corpo).encode("utf-8") if corpo is not None else None
    req = urllib.request.Request(API + caminho, data=dados, method=metodo)
    req.add_header("PRIVATE-TOKEN", token())
    req.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(req) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as erro:
        sys.exit(f"GitLab respondeu {erro.code} em {metodo} {caminho}: "
                 f"{erro.read().decode('utf-8', 'replace')}")


@functools.cache
def usuario_id() -> int:
    req = urllib.request.Request("https://gitlab.com/api/v4/user")
    req.add_header("PRIVATE-TOKEN", token())
    with urllib.request.urlopen(req) as resp:
        return json.loads(resp.read().decode("utf-8"))["id"]


def ler(arquivo: str | None) -> str | None:
    return pathlib.Path(arquivo).read_text(encoding="utf-8") if arquivo else None


def main() -> None:
    sys.stdout.reconfigure(encoding="utf-8")
    p = argparse.ArgumentParser()
    sub = p.add_subparsers(dest="cmd", required=True)

    s = sub.add_parser("criar-issue")
    s.add_argument("--titulo", required=True)
    s.add_argument("--descricao-arquivo", required=True)
    s.add_argument("--labels", default="")

    s = sub.add_parser("criar-branch")
    s.add_argument("--nome", required=True)
    s.add_argument("--ref", default="main")

    s = sub.add_parser("criar-mr")
    s.add_argument("--branch", required=True)
    s.add_argument("--titulo", required=True)
    s.add_argument("--descricao-arquivo", required=True)
    s.add_argument("--labels", default="")
    s.add_argument("--draft", action="store_true")

    s = sub.add_parser("atualizar-mr")
    s.add_argument("--iid", required=True, type=int)
    s.add_argument("--titulo")
    s.add_argument("--descricao-arquivo")
    s.add_argument("--labels")
    s.add_argument("--pronto", action="store_true", help="tira o Draft do título")

    s = sub.add_parser("labels-issue")
    s.add_argument("--iid", required=True, type=int)
    s.add_argument("--adicionar", default="")
    s.add_argument("--remover", default="")

    s = sub.add_parser("ver-issue")
    s.add_argument("--iid", required=True, type=int)

    a = p.parse_args()

    if a.cmd == "criar-issue":
        r = chamar("POST", "/issues", {
            "title": a.titulo,
            "description": ler(a.descricao_arquivo),
            "labels": a.labels,
            "assignee_ids": [usuario_id()],
        })
        print(json.dumps({"iid": r["iid"], "web_url": r["web_url"]}, ensure_ascii=False))

    elif a.cmd == "criar-branch":
        r = chamar("POST", "/repository/branches?" + urllib.parse.urlencode(
            {"branch": a.nome, "ref": a.ref}))
        print(json.dumps({"branch": r["name"], "commit": r["commit"]["short_id"]}))

    elif a.cmd == "criar-mr":
        titulo = ("Draft: " + a.titulo) if a.draft else a.titulo
        r = chamar("POST", "/merge_requests", {
            "source_branch": a.branch,
            "target_branch": "main",
            "title": titulo,
            "description": ler(a.descricao_arquivo),
            "labels": a.labels,
            "assignee_ids": [usuario_id()],
            "reviewer_ids": [usuario_id()],
            "remove_source_branch": True,
            "squash": False,
        })
        print(json.dumps({"iid": r["iid"], "web_url": r["web_url"]}, ensure_ascii=False))

    elif a.cmd == "atualizar-mr":
        corpo: dict = {}
        titulo = a.titulo
        if a.pronto:
            atual = chamar("GET", f"/merge_requests/{a.iid}")["title"]
            titulo = (titulo or atual).removeprefix("Draft: ").removeprefix("Draft:").strip()
            # Garante o revisor também em MR criado antes dessa regra.
            corpo["reviewer_ids"] = [usuario_id()]
        if titulo:
            corpo["title"] = titulo
        if a.descricao_arquivo:
            corpo["description"] = ler(a.descricao_arquivo)
        if a.labels is not None:
            corpo["labels"] = a.labels
        r = chamar("PUT", f"/merge_requests/{a.iid}", corpo)
        print(json.dumps({"iid": r["iid"], "title": r["title"],
                          "reviewers": [u["username"] for u in r.get("reviewers", [])],
                          "web_url": r["web_url"]}, ensure_ascii=False))

    elif a.cmd == "labels-issue":
        r = chamar("PUT", f"/issues/{a.iid}", {
            "add_labels": a.adicionar,
            "remove_labels": a.remover,
        })
        print(json.dumps({"iid": r["iid"], "labels": r["labels"]}, ensure_ascii=False))

    elif a.cmd == "ver-issue":
        r = chamar("GET", f"/issues/{a.iid}")
        print(json.dumps({k: r[k] for k in ("iid", "title", "state", "labels", "description",
                                            "web_url")}, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
