# Espelhamento das issues do GitLab no GitHub

As issues do projeto são abertas e trabalhadas no **GitLab**, o repositório do TCC. O GitHub
mantém uma cópia delas, atualizada sozinha, do mesmo jeito que o código já é espelhado entre os
dois (ver [sincronizacao-github-gitlab.md](sincronizacao-github-gitlab.md)).

## Como funciona

1. Um **webhook do projeto no GitLab** dispara a cada evento de issue ou de comentário e chama a
   API de `repository_dispatch` do GitHub (`event_type: gitlab-issue`).
2. O workflow [`sync-gitlab-issues.yml`](../.github/workflows/sync-gitlab-issues.yml) roda
   [`sync-gitlab-issues.py`](../.github/scripts/sync-gitlab-issues.py), que sincroniza tudo:
   cria as issues que faltam e atualiza título, descrição, rótulos, estado e comentários
   (inclusive editados e apagados).
3. Uma vez por dia o workflow roda sozinho, para pegar qualquer evento que o webhook tenha perdido.
   Também dá para rodar à mão em **Actions → Espelhar issues do GitLab → Run workflow**.

Cada issue e cada comentário espelhados levam um marcador invisível com o id de origem
(`<!-- gitlab-issue:N -->`, `<!-- gitlab-note:N -->`). É assim que o script sabe o que já existe:
rodar de novo sem mudança nenhuma não altera nada.

O workflow não usa o conteúdo do evento que o disparou: lê tudo da API do GitLab. Quem
descobrisse o endereço e forjasse um evento, no máximo, provocaria uma sincronização a mais.

## Regras do espelho

- **Uma via só.** Editar ou comentar a issue no GitHub não volta para o GitLab, e a próxima
  sincronização sobrescreve. O cabeçalho de cada issue avisa isso.
- **Numeração diferente.** No GitHub issues e PRs dividem a mesma sequência, então a GitLab #N não
  é a GitHub #N. Referências como `#32` ou `!11` nos textos viram links para o GitLab; cores
  (`#5B5CEB`) e trechos de código ficam como estão.
- **Autor e data.** Tudo aparece criado pela conta do workflow na data da sincronização; a data
  original vai no topo da issue e de cada comentário.
- **Fechamento.** Issue fechada no GitLab fecha como *concluída*, ou como *não planejada* se tiver o
  rótulo `wontfix`. Depois de fechada, o motivo pode ser ajustado à mão aqui sem ser desfeito.
- **Issues confidenciais** não são espelhadas.

## Configuração

| Onde | O quê |
|---|---|
| GitHub → Settings → Secrets → Actions | `GITLAB_READ_TOKEN`: token pessoal do GitLab **só com `read_api`**. Opcional — sem ele as issues são espelhadas, mas os comentários não, porque a API do GitLab exige autenticação para lê-los mesmo em projeto público. |
| GitLab → Settings → Webhooks | URL `https://api.github.com/repos/gentilpedro/Nexus/dispatches`, eventos **Issues** e **Comments**, cabeçalhos `Authorization: Bearer <token>` e `Accept: application/vnd.github+json`, e o corpo personalizado abaixo. |
| GitHub → Settings → Developer settings → Fine-grained tokens | O `<token>` do webhook: acesso só ao repositório Nexus, permissão **Contents: Read and write** (é o que a API de `repository_dispatch` exige). |

Corpo personalizado do webhook (*Custom webhook template*):

```json
{"event_type": "gitlab-issue", "client_payload": {"kind": "{{object_kind}}"}}
```

O token do webhook fica visível para quem administra o projeto no GitLab. Por isso ele é
*fine-grained* e restrito ao repositório: com a branch `main` protegida pelo ruleset, o que ele
permite de fato é disparar este workflow e empurrar branches.

## Custo

Cada evento gera uma execução de menos de um minuto do Actions (conta como um minuto), mais uma
por dia. É uma fração pequena da cota gratuita de repositório privado.
