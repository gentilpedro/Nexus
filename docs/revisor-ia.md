# Revisor automático de MRs

Todo Merge Request no GitLab recebe uma revisão do Claude Code, feita pelo job `ai-review` da
pipeline de MR (issue #43). O revisor comenta como uma pessoa comentaria: um resumo no MR e os
achados nas linhas do diff. Ele **só aconselha** — não aprova, não faz merge e não reprova a
pipeline (`allow_failure`). Quem barra o merge continua sendo o `verify`.

## Como funciona

1. A cada push num MR, o job monta o diff contra a branch de destino. Se o MR já tinha sido
   revisado, monta também o diff só do que mudou desde aquela revisão, e os achados ficam
   restritos a ele. O mesmo commit nunca é revisado duas vezes (o resumo leva um marcador com o
   SHA).
2. `review.py` roda o Claude Code com o prompt de [`.gitlab/ai-review/prompt.md`](../.gitlab/ai-review/prompt.md),
   que traz as regras do projeto: revalidação de acesso no servidor, `ExecuteUpdate` × token de
   concorrência, `TestContext.Current.CancellationToken` nos testes (a CI trata warning como
   erro), tokens do `DESIGN.md` nas telas, Conventional Commits.
3. A resposta vem em JSON; o script posta o resumo, os comentários nas linhas e marca o bot como
   revisor do MR. Achado sem linha válida no diff entra no resumo.

## Segurança

O revisor lê conteúdo controlado por quem abriu o MR, e esse conteúdo pode trazer instruções
escondidas para a IA. O desenho parte do princípio de que o modelo pode ser manipulado:

- **Só leitura.** Ferramentas permitidas: `Read`, `Grep`, `Glob`. Shell, edição e rede estão
  negados, assim como ler `/proc` (ambiente dos processos do job), `/etc` e o `.git` (onde o
  runner guarda o `CI_JOB_TOKEN` na URL do remote).
- **Sem segredos no ambiente do modelo.** O processo do Claude recebe só `PATH`, `HOME`, `LANG` e
  a `ANTHROPIC_API_KEY`. O token do GitLab fica com o script que posta.
- **Redação antes de postar.** Qualquer valor de variável terminada em `TOKEN`, `KEY`,
  `PASSWORD` ou `SECRET`, e qualquer coisa com cara de token (`glpat-`, `ghp_`, `github_pat_`,
  `sk-ant-`), vira `[removido]` antes de virar comentário.
- **Fora de fork e de rascunho.** O projeto é público: MR vindo de fork não roda o job, para
  ninguém usar a chave da API do dono. MR em rascunho (`Draft:`) também não.
- O prompt instrui o revisor a tratar pedidos embutidos no diff como achado de segurança, não como
  instrução.

## Configuração

Duas variáveis de CI/CD do projeto (**Settings → CI/CD → Variables**), ambas **mascaradas** e
**não protegidas** (as branches de MR não são protegidas). Sem as duas, o job simplesmente não
aparece na pipeline.

| Variável | O que é |
|---|---|
| `ANTHROPIC_API_KEY` | Chave da API da Anthropic, do dono do projeto. |
| `GITLAB_REVIEW_TOKEN` | Token pessoal (escopo `api`) da conta que assina as revisões. |

Opcional: `AI_REVIEW_MODEL` (padrão `sonnet`).

**Conta do revisor.** No plano gratuito do GitLab.com não dá para criar token de projeto ou de
grupo (bot). O recomendado é uma conta separada — por exemplo `nexus-revisor` — adicionada ao
projeto como **Developer** (o mínimo que permite se marcar como revisor), com um token pessoal
dela em `GITLAB_REVIEW_TOKEN`. Usar o token da própria conta do dono também funciona, mas aí as
revisões aparecem como se fossem dele, e o token dá acesso a tudo que a conta acessa.

## Testar localmente

Com o Claude Code instalado e logado, o modo ensaio roda tudo e imprime o que seria postado:

```bash
AI_REVIEW_DRY_RUN=1 \
CI_API_V4_URL=https://gitlab.com/api/v4 CI_PROJECT_ID=86472224 \
CI_MERGE_REQUEST_IID=<número do MR> CI_MERGE_REQUEST_TARGET_BRANCH_NAME=main \
CI_COMMIT_SHA=$(git rev-parse HEAD) \
GITLAB_REVIEW_TOKEN=$(cat ~/.gitlab-token) \
python .gitlab/ai-review/review.py
```

## Custo

Cada push num MR aberto gera uma revisão (o rodapé do resumo mostra o custo em dólares).
Rascunhos não são revisados, então abrir o MR como `Draft:` enquanto ele ainda está em andamento
e tirar do rascunho quando estiver pronto evita pagar por revisões intermediárias.
