#!/usr/bin/env bash
#
# Monta as notas de release em Markdown, descrevendo o que mudou de uma versão para a outra.
#
# Os commits do Nexus têm corpos longos que explicam o *porquê* de cada mudança — é a informação
# mais valiosa que existe no histórico, e é ela que vai para as notas. O título vira o item da
# lista e o corpo vira a explicação logo abaixo, agrupado pelo tipo do Conventional Commit.
#
# Uso: .github/scripts/release-notes.sh <tag_anterior> <versao> <owner/repo>
#      .github/scripts/release-notes.sh "" 1.0.0 gentilpedro/Nexus   # primeira release
#
# Escreve Markdown em stdout.

set -euo pipefail

previous_tag="${1:-}"
version="${2:?versão é obrigatória}"
repo="${3:?owner/repo é obrigatório}"

# Corpos muito longos são cortados para a release não estourar o limite de 125.000 caracteres do
# GitHub. O link do commit fica sempre disponível para o texto completo.
readonly MAX_BODY_LINES=30

# Teto de segurança para o texto inteiro. Acima disso as notas são refeitas só com os títulos dos
# commits — uma nota condensada é melhor que uma release que a API recusa. Sobrescrevível por
# variável de ambiente para o teste conseguir exercitar esse caminho sem forjar 100 commits.
readonly MAX_TOTAL_CHARS="${NEXUS_RELEASE_NOTES_MAX_CHARS:-100000}"

if [ -n "$previous_tag" ]; then
  range="${previous_tag}..HEAD"
else
  # Primeira release: todo o histórico.
  range="HEAD"
fi

# Base URLs são sobrescrevíveis por variável de ambiente — o pipeline do GitHub usa o padrão
# (github.com), o pipeline do GitLab aponta para o próprio projeto (ver .gitlab-ci.yml). Sem a
# variável definida, o comportamento é idêntico ao original.
commit_url="${NEXUS_COMMIT_BASE_URL:-https://github.com/${repo}/commit}"
compare_base_url="${NEXUS_COMPARE_BASE_URL:-https://github.com/${repo}/compare}"

# Ordem intencional: o que o usuário final percebe vem antes do que só interessa a quem mantém o
# código.
declare -A GROUP_TITLES=(
  [feat]="✨ Novidades"
  [fix]="🐛 Correções"
  [security]="🔒 Segurança"
  [perf]="⚡ Performance"
  [style]="💅 Interface e estilo"
  [refactor]="♻️ Refatoração"
  [test]="✅ Testes"
  [docs]="📖 Documentação"
  [build]="🏗️ Build e infraestrutura"
  [ci]="🔁 Integração contínua"
  [chore]="🧹 Manutenção"
  [other]="📦 Outras mudanças"
)
readonly GROUP_ORDER=(feat fix security perf style refactor test docs build ci chore other)

generate_notes() {
  # $1 = "1" para incluir o corpo dos commits, "0" para só os títulos.
  local include_bodies="$1"

declare -A entries
for key in "${GROUP_ORDER[@]}"; do
  entries[$key]=""
done

total_commits=0

while IFS=$'\x1f' read -r -d $'\x1e' sha subject body; do
  # git termina cada registro com uma quebra de linha depois do separador, então do segundo
  # commit em diante o primeiro campo chega com "\n" na frente — o que corromperia o sha curto e
  # o link do commit.
  sha="${sha#$'\n'}"
  subject="${subject%$'\r'}"

  total_commits=$((total_commits + 1))
  short_sha="${sha:0:7}"

  # Separa "tipo(escopo)!: descrição" em tipo e descrição. Commits que não seguem a convenção caem
  # inteiros em "other", sem perder nada.
  if [[ "$subject" =~ ^([a-zA-Z]+)(\([^\)]*\))?(!)?:[[:space:]]*(.*)$ ]]; then
    type="$(echo "${BASH_REMATCH[1]}" | tr '[:upper:]' '[:lower:]')"
    scope="${BASH_REMATCH[2]}"
    breaking="${BASH_REMATCH[3]}"
    description="${BASH_REMATCH[4]}"
  else
    type="other"
    scope=""
    breaking=""
    description="$subject"
  fi

  if [ -z "${GROUP_TITLES[$type]+x}" ]; then
    type="other"
  fi

  # O escopo (o "(auth)" de "fix(auth):") vira prefixo em negrito, para não sumir da nota.
  scope_label=""
  if [ -n "$scope" ]; then
    scope_label="${scope#(}"
    scope_label="**${scope_label%)}:** "
  fi

  breaking_label=""
  if [ -n "$breaking" ] || [[ "$body" == *"BREAKING CHANGE"* ]]; then
    breaking_label="⚠️ **MUDANÇA INCOMPATÍVEL** — "
  fi

  entry="- ${breaking_label}${scope_label}${description} — [\`${short_sha}\`](${commit_url}/${sha})"$'\n'

  # Remove as linhas de rodapé automáticas (co-autoria, assinatura) — são ruído numa nota de
  # release.
  #
  # O `tr -d '\r'` é defensivo: o histórico atual não tem CR nenhum, mas o desenvolvimento é em
  # Windows e um editor configurado para CRLF colocaria CR nas mensagens. Um CR solto sobrescreve
  # o resto da linha na renderização — some texto sem erro nenhum, e o diagnóstico é chato.
  cleaned_body="$(printf '%s\n' "$body" \
    | tr -d '\r' \
    | sed -E '/^(Co-Authored-By|Co-authored-by|Signed-off-by):/d' \
    | sed -E '/^🤖 Generated with/d' \
    | sed -E '/^[[:space:]]*$/{ /./!d }')"

  # Tira linhas em branco do começo e do fim.
  cleaned_body="$(printf '%s' "$cleaned_body" | sed -E '/./,$!d' | tac | sed -E '/./,$!d' | tac)"

  if [ "$include_bodies" = "1" ] && [ -n "$cleaned_body" ]; then
    body_lines="$(printf '%s\n' "$cleaned_body" | wc -l)"
    if [ "$body_lines" -gt "$MAX_BODY_LINES" ]; then
      cleaned_body="$(printf '%s\n' "$cleaned_body" | head -n "$MAX_BODY_LINES")"
      cleaned_body="${cleaned_body}"$'\n'"_(…texto truncado — veja o commit completo)_"
    fi

    # Indentado em dois espaços: em Markdown isso faz o parágrafo pertencer ao item da lista.
    entry+=$'\n'"$(printf '%s\n' "$cleaned_body" | sed -E 's/^/  /')"$'\n'
  fi

  entries[$type]+="${entry}"$'\n'
done < <(git log --no-merges --reverse --format="%H%x1f%s%x1f%b%x1e" "$range")

# ---------------------------------------------------------------------------
# Cabeçalho com o resumo quantitativo da diferença entre as duas versões.
# ---------------------------------------------------------------------------

printf '## Nexus %s\n\n' "$version"

if [ -n "$previous_tag" ]; then
  shortstat="$(git diff --shortstat "${previous_tag}..HEAD" | sed -E 's/^[[:space:]]+//')"
  printf 'Mudanças desde **%s** — %s commit(s)' "$previous_tag" "$total_commits"
  [ -n "$shortstat" ] && printf ', %s' "$shortstat"
  printf '.\n\n'
else
  printf 'Primeira release versionada do Nexus — %s commit(s) de histórico.\n\n' "$total_commits"
  printf 'Esta é a linha de base: em vez do detalhamento completo de cada commit, vai o resumo do\n'
  printf 'que existe hoje. A partir da próxima versão as notas trazem a explicação inteira de cada\n'
  printf 'mudança.\n\n'
fi

if [ "$include_bodies" = "0" ] && [ -n "$previous_tag" ]; then
  printf '> Notas condensadas aos títulos: o detalhamento completo passaria do limite de tamanho\n'
  printf '> de uma release no GitHub. Abra os commits para o texto integral.\n\n'
fi

for key in "${GROUP_ORDER[@]}"; do
  if [ -n "${entries[$key]}" ]; then
    printf '### %s\n\n' "${GROUP_TITLES[$key]}"
    printf '%s' "${entries[$key]}"
  fi
done

# ---------------------------------------------------------------------------
# Pull requests incluídos. Num fluxo de merge commits o número do PR só existe no commit de merge,
# então ele é lido de lá; o padrão "(#12)" cobre também merges por squash.
# ---------------------------------------------------------------------------

pr_numbers="$( { git log --merges --format='%s' "$range" \
                   | sed -n 's/^Merge pull request #\([0-9]\{1,\}\).*/\1/p'
                 git log --no-merges --format='%s' "$range" \
                   | sed -n 's/.*(#\([0-9]\{1,\}\)).*/\1/p'
               } | sort -un || true)"

if [ -n "$pr_numbers" ]; then
  printf '### 🔗 Pull requests nesta versão\n\n'
  while read -r pr; do
    [ -n "$pr" ] && printf -- '- [#%s](https://github.com/%s/pull/%s)\n' "$pr" "$repo" "$pr"
  done <<< "$pr_numbers"
  printf '\n'
fi

# ---------------------------------------------------------------------------
# Rodapé: como conferir o que está realmente rodando em produção.
# ---------------------------------------------------------------------------

printf -- '---\n\n'

if [ -n "$previous_tag" ]; then
  printf '**Diferença completa:** %s/%s...v%s\n\n' \
    "$compare_base_url" "$previous_tag" "$version"
fi

printf 'Para confirmar qual versão está no ar: `GET https://usenexus.runasp.net/version`\n'
}

# A primeira release não é uma diferença entre versões, é o inventário de tudo que já existe —
# detalhar 100 commits ali gera um texto que ninguém lê. Da segunda em diante cada release cobre
# poucos commits e o detalhamento completo é justamente o que se quer.
if [ -n "$previous_tag" ]; then
  notes="$(generate_notes 1)"

  # Rede de segurança: se mesmo assim passar do teto, refaz só com os títulos.
  if [ "${#notes}" -gt "$MAX_TOTAL_CHARS" ]; then
    notes="$(generate_notes 0)"
  fi
else
  notes="$(generate_notes 0)"
fi

printf '%s\n' "$notes"
