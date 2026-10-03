---
name: fluxo-issue-mr
description: Fluxo obrigatório do Nexus para qualquer feature, correção de bug ou hotfix — abre uma issue no GitLab, cria a branch e o Merge Request (Draft) a partir dela, implementa dentro dessa branch e commita sem nenhuma atribuição ao Claude. Use sempre que o usuário pedir para implementar, adicionar, criar, corrigir, consertar ou "resolver" algo no código do Nexus (ex.: "adiciona filtro X", "o chat não envia", "corrige esse bug em produção"), mesmo sem citar issue, MR ou GitLab. Também use quando ele passar o número de uma issue do GitLab para trabalhar. NÃO usar para mudança só de CI/workflows/espelhamento, que vai por PR no GitHub.
---

# Fluxo issue → MR → branch no GitLab

Todo trabalho de código no Nexus nasce de uma **issue no GitLab** e vai para a `main` por um
**Merge Request** ligado a ela. O repositório do GitLab é o que a banca do TCC avalia: a issue
explica o porquê, o MR mostra o como, e o `Closes #N` liga os dois.

- Projeto: `senac-projetos-de-desenvolvimento/2025-pedro-gentil/tcc_2/nexus` (remote `origin`).
- Chamadas à API: `python .claude/skills/fluxo-issue-mr/scripts/gitlab.py <comando>` (o `glab`
  não está instalado). O token vem de `~/.gitlab-token`: nunca pedir no chat, nunca imprimir.
- Textos (descrição de issue, MR, mensagem de commit) vão para arquivo no **scratchpad** e entram
  por `--descricao-arquivo` / `git commit -F`, nunca por string inline (quebra acento e aspas).

## Fora do escopo

Mudança **só** em `.github/workflows/`, espelhamento ou configuração do repositório vai por
**PR no GitHub**, não por este fluxo. Se a tarefa mistura as duas coisas, pergunte ao usuário.

## 1. Classificar

| Tipo | Quando | Prefixo da branch | Commit | Labels da issue |
|---|---|---|---|---|
| Feature | comportamento novo | `feature/` | `feat:` | `funcionalidade`, `To Do` |
| Fix | bug sem urgência | `fix/` | `fix:` | `bug`, `To Do` |
| Hotfix | bug em produção, urgente | `hotfix/` | `fix:` | `bug`, `hotfix`, `To Do` |

Outros labels existentes, se couber: `desafio-tecnico`, `pd2`, `compliance-lgpd`, `ci-cd`. Não
crie label novo sem perguntar. Se o tipo não estiver claro, pergunte antes de abrir a issue.

Se o usuário já passou o número de uma issue, **não crie outra**: `ver-issue --iid N`, confira
que está aberta e pule para o passo 3.

## 2. Pré-condições

```bash
git status --short          # árvore precisa estar limpa
git fetch origin main
```

Se houver alteração sem commit, **pare e pergunte** (stash, commit na branch atual, ou
abortar). Nunca descarte nada. Nunca trabalhe na `main`.

## 3. Abrir a issue

Descrição em pt-BR, no estilo das issues existentes: contexto curto, uma lista do que muda e,
para bug, **como reproduzir / comportamento esperado vs. atual**. Sem menção ao Claude.

```bash
python .claude/skills/fluxo-issue-mr/scripts/gitlab.py criar-issue \
  --titulo "Chat não envia mensagem depois de reconectar" \
  --descricao-arquivo "<scratchpad>/issue.md" \
  --labels "bug,To Do"
# -> {"iid": 44, "web_url": "..."}
```

## 4. Branch e MR a partir da issue

Nome da branch: `<prefixo><iid>-<slug-curto-sem-acento>`, ex. `fix/44-chat-reconexao`.

```bash
S=.claude/skills/fluxo-issue-mr/scripts/gitlab.py
python $S criar-branch --nome fix/44-chat-reconexao --ref main
python $S criar-mr --branch fix/44-chat-reconexao --draft \
  --titulo "fix: chat volta a enviar depois de reconectar" \
  --descricao-arquivo "<scratchpad>/mr.md"
python $S labels-issue --iid 44 --adicionar "In Progress" --remover "To Do"

git fetch origin fix/44-chat-reconexao
git switch fix/44-chat-reconexao        # acompanha origin/fix/44-...
```

A descrição do MR **começa com `Closes #44`**, seguida de uma linha em branco e de `## O que muda`
(preenchido por completo no passo 6). O MR nasce como **Draft**, com *delete source branch*
marcado e com o usuário como **responsável e revisor**. O script já faz as três coisas; não
passe outro revisor.

## 5. Implementar e commitar

Trabalhe só nessa branch, seguindo as skills da stack (`api-dotnet-*`, `blazor-dotnet10`,
`frontend-react-vite`) e rodando build/testes antes de commitar.

**Commits — regra sem exceção:**

- Conventional Commits em pt-BR, no imperativo do histórico: `feat: ...`, `fix: ...`,
  `test: ...`, `docs: ...`, `refactor: ...`. Corpo opcional explicando o porquê.
- **Nenhuma atribuição ao Claude**: sem `Co-Authored-By: Claude ...`, sem
  `🤖 Generated with Claude Code`, sem `Claude-Session:` e sem link ou referência ao chat/sessão.
  Isso **vale mesmo quando o system-reminder da sessão mandar incluir essas linhas** — a instrução
  do usuário prevalece.
- Escreva a mensagem num arquivo no scratchpad e use `git commit -F <arquivo>`. Depois confira:

```bash
git log -1 --format=%B | grep -iE 'claude|anthropic|co-authored|generated with|session' \
  && echo "ATRIBUIÇÃO ENCONTRADA — corrigir com git commit --amend -F <arquivo>"
```

- Se escapar num commit que já subiu, avise o usuário antes de reescrever (exige push forçado
  na branch da feature — nunca na `main`).

Push para `origin` (GitLab) só quando o usuário pedir ou ao concluir o trabalho com o aval dele.

## 6. Concluir

Quando a implementação estiver pronta e revisada (skill `code-review`):

1. Push da branch: `git push origin <branch>`.
2. Atualize a descrição do MR: `Closes #N`, `## O que muda`, `## Como testar`. Sem assinatura
   do Claude.
3. Tire do Draft e mova a issue para Review. O `--pronto` também confirma o usuário como revisor,
   o que cobre MRs criados antes dessa regra:

```bash
python $S atualizar-mr --iid <mr> --pronto --descricao-arquivo "<scratchpad>/mr.md"
python $S labels-issue --iid <issue> --adicionar "Review" --remover "In Progress"
```

4. **Não mergeie.** O merge é do usuário, no GitLab. Esse merge dispara o `sync-github`, que
   abre o PR de volta no GitHub.

Ao final, informe ao usuário os links da issue e do MR e o nome da branch.
