#!/usr/bin/env bash
#
# Testes de next-version.sh e release-notes.sh.
#
# A lógica de versionamento decide o número que vai para produção e para a tag do repositório —
# se ela errar em silêncio, o estrago só aparece depois de publicada. Cada caso abaixo monta um
# repositório git descartável e confere a saída.
#
# Uso: .github/scripts/test-release-scripts.sh

set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly NEXT_VERSION="${script_dir}/next-version.sh"
readonly RELEASE_NOTES="${script_dir}/release-notes.sh"

# Cada caso roda num subshell para isolar o diretório de trabalho, e subshell não devolve
# variável para o pai. Os contadores moram em arquivo justamente por isso — a primeira versão
# deste script contava em variáveis e reportava "0 verificações, 0 falhas" com um teste vermelho
# na tela, saindo com código 0.
readonly TALLY_DIR="$(mktemp -d)"
trap 'rm -rf "$TALLY_DIR"' EXIT
: > "${TALLY_DIR}/checks"
: > "${TALLY_DIR}/failures"

fail() {
  printf '  ✗ %s\n' "$1" >&2
  printf 'x\n' >> "${TALLY_DIR}/failures"
}

pass() {
  printf '  ✓ %s\n' "$1"
}

record_check() {
  printf 'x\n' >> "${TALLY_DIR}/checks"
}

assert_equals() {
  local expected="$1" actual="$2" what="$3"
  record_check
  if [ "$expected" = "$actual" ]; then
    pass "$what"
  else
    fail "$what — esperado '${expected}', obtido '${actual}'"
  fi
}

assert_contains() {
  local haystack="$1" needle="$2" what="$3"
  record_check
  if [[ "$haystack" == *"$needle"* ]]; then
    pass "$what"
  else
    fail "$what — não encontrou '${needle}'"
  fi
}

assert_matches() {
  local value="$1" pattern="$2" what="$3"
  record_check
  if [[ "$value" =~ $pattern ]]; then
    pass "$what"
  else
    fail "$what — '${value}' não casa com /${pattern}/"
  fi
}

# Cria um repositório vazio e entra nele.
new_repo() {
  local dir
  dir="$(mktemp -d)"
  cd "$dir"
  git init --quiet
  git config user.email "teste@nexus.local"
  git config user.name "Teste"
  git config commit.gpgsign false
  git commit --quiet --allow-empty -m "chore: commit inicial"
}

commit() {
  git commit --quiet --allow-empty -m "$1" ${2:+-m "$2"}
}

# Lê uma chave da saída chave=valor de next-version.sh.
value_of() {
  printf '%s\n' "$1" | sed -n "s/^${2}=//p"
}

printf '\nnext-version.sh\n'

(
  new_repo
  out="$(bash "$NEXT_VERSION")"
  assert_equals "1.0.0" "$(value_of "$out" version)" "sem tags começa em 1.0.0"
  assert_equals "initial" "$(value_of "$out" bump)" "sem tags reporta bump=initial"
)

(
  new_repo
  git tag v1.4.2
  commit "fix: corrige overflow no mobile"
  out="$(bash "$NEXT_VERSION")"
  assert_equals "1.4.3" "$(value_of "$out" version)" "fix: sobe patch"
  assert_equals "v1.4.2" "$(value_of "$out" previous_tag)" "reporta a tag anterior"
)

(
  new_repo
  git tag v1.4.2
  commit "feat: exporta board em PDF"
  assert_equals "1.5.0" "$(value_of "$(bash "$NEXT_VERSION")" version)" "feat: sobe minor"
)

(
  new_repo
  git tag v1.4.2
  commit "fix: ajuste pequeno"
  commit "feat: recurso novo"
  commit "docs: atualiza readme"
  assert_equals "1.5.0" "$(value_of "$(bash "$NEXT_VERSION")" version)" "feat junto de fix ainda é minor"
)

(
  new_repo
  git tag v1.4.2
  commit "feat!: remove a API v1"
  assert_equals "2.0.0" "$(value_of "$(bash "$NEXT_VERSION")" version)" "'!' no título sobe major"
)

(
  new_repo
  git tag v1.4.2
  commit "fix(auth): muda o formato do token" "BREAKING CHANGE: sessões antigas serão invalidadas."
  assert_equals "2.0.0" "$(value_of "$(bash "$NEXT_VERSION")" version)" "BREAKING CHANGE no corpo sobe major"
)

(
  new_repo
  git tag v1.9.0
  commit "fix: a"
  git tag v1.10.0
  commit "fix: b"
  out="$(bash "$NEXT_VERSION")"
  assert_equals "v1.10.0" "$(value_of "$out" previous_tag)" "ordena por versão, não alfabeticamente"
  assert_equals "1.10.1" "$(value_of "$out" version)" "continua a partir da maior versão"
)

(
  new_repo
  git tag v1.4.2
  out="$(bash "$NEXT_VERSION")"
  assert_equals "false" "$(value_of "$out" has_changes)" "sem commits novos não gera release"
  assert_equals "1.4.2" "$(value_of "$out" version)" "sem commits novos mantém a versão"
)

(
  new_repo
  git tag v1.4.2
  commit "reestrutura pastas sem seguir a convenção"
  assert_equals "1.4.3" "$(value_of "$(bash "$NEXT_VERSION")" version)" "commit fora da convenção vira patch"
)

printf '\nrelease-notes.sh\n'

(
  new_repo
  git tag v1.4.2
  commit "feat(board): exporta o quadro em PDF" \
    "Gera o PDF no servidor com QuestPDF em vez de imprimir pelo navegador.

Co-Authored-By: Alguem <a@b.c>"
  commit "fix: corrige overflow do menu no mobile"
  notes="$(bash "$RELEASE_NOTES" v1.4.2 1.5.0 gentilpedro/Nexus)"

  assert_contains "$notes" "## Nexus 1.5.0" "tem o título com a versão"
  assert_contains "$notes" "Mudanças desde **v1.4.2**" "cita a versão anterior"
  assert_contains "$notes" "✨ Novidades" "agrupa os feats em Novidades"
  assert_contains "$notes" "🐛 Correções" "agrupa os fixes em Correções"
  assert_contains "$notes" "**board:** exporta o quadro em PDF" "mostra o escopo do commit"
  assert_contains "$notes" "QuestPDF" "inclui o corpo do commit na nota"
  assert_contains "$notes" "compare/v1.4.2...v1.5.0" "linka a comparação entre as versões"

  record_check
  if [[ "$notes" == *"Co-Authored-By"* ]]; then
    fail "remove as linhas de co-autoria do corpo"
  else
    pass "remove as linhas de co-autoria do corpo"
  fi

  # Regressão: o segundo commit em diante chegava com "\n" no primeiro campo, o que gerava um sha
  # curto quebrado e um link de commit inválido. Confere todos os links, não só o primeiro.
  links="$(printf '%s\n' "$notes" | grep -o 'commit/[0-9a-f]*' || true)"
  link_count="$(printf '%s\n' "$links" | grep -c . || true)"
  assert_equals "2" "$link_count" "gera um link de commit por commit"
  for link in $links; do
    assert_matches "${link#commit/}" '^[0-9a-f]{40}$' "link de commit com sha íntegro (${link})"
  done
  assert_matches "$notes" '\[`[0-9a-f]{7}`\]' "sha curto exibido sem lixo"
)

(
  new_repo
  git tag v2.0.0
  commit "feat!: remove o endpoint legado" "BREAKING CHANGE: integrações antigas param de funcionar."
  notes="$(bash "$RELEASE_NOTES" v2.0.0 3.0.0 gentilpedro/Nexus)"
  assert_contains "$notes" "MUDANÇA INCOMPATÍVEL" "destaca a mudança incompatível"
)

# Defesa preventiva: o histórico atual não tem CR, mas o desenvolvimento é em Windows e um editor
# configurado para CRLF os introduziria. Um CR solto sobrescreve o resto da linha na renderização
# — o texto some sem erro nenhum.
(
  new_repo
  git tag v1.0.0
  # CR no fim de cada linha, como um editor do Windows deixaria.
  git commit --quiet --allow-empty \
    -m "$(printf 'fix: corrige o filtro\r')" \
    -m "$(printf 'Primeira linha do corpo.\r\nSegunda linha do corpo.\r')"
  notes="$(bash "$RELEASE_NOTES" v1.0.0 1.0.1 gentilpedro/Nexus)"

  record_check
  if [[ "$notes" == *$'\r'* ]]; then
    fail "remove os CR herdados das mensagens escritas no Windows"
  else
    pass "remove os CR herdados das mensagens escritas no Windows"
  fi
  assert_contains "$notes" "Segunda linha do corpo." "preserva o texto que vinha depois do CR"
)

(
  new_repo
  commit "feat: primeira funcionalidade" "Explicação detalhada que não deve aparecer na linha de base."
  notes="$(bash "$RELEASE_NOTES" "" 1.0.0 gentilpedro/Nexus)"
  assert_contains "$notes" "Primeira release versionada" "trata a primeira release sem tag anterior"

  # A linha de base cobre todo o histórico do repositório: detalhar cada commit ali produz um
  # texto que ninguém lê (100 commits = 1200 linhas), então ela sai condensada.
  record_check
  if [[ "$notes" == *"Explicação detalhada"* ]]; then
    fail "primeira release sai condensada, sem o corpo dos commits"
  else
    pass "primeira release sai condensada, sem o corpo dos commits"
  fi
)

(
  new_repo
  git tag v1.0.0
  commit "feat: recurso" "Corpo longo o suficiente para estourar o teto artificial deste teste."
  # Teto minúsculo força o caminho de fallback sem precisar forjar centenas de commits.
  notes="$(NEXUS_RELEASE_NOTES_MAX_CHARS=200 bash "$RELEASE_NOTES" v1.0.0 1.1.0 gentilpedro/Nexus)"

  assert_contains "$notes" "Notas condensadas" "avisa quando precisa condensar por tamanho"
  record_check
  if [[ "$notes" == *"Corpo longo o suficiente"* ]]; then
    fail "condensa o corpo dos commits ao estourar o teto"
  else
    pass "condensa o corpo dos commits ao estourar o teto"
  fi
)

(
  new_repo
  git tag v1.0.0
  commit "feat: recurso" "Corpo que cabe folgado no teto padrão."
  notes="$(bash "$RELEASE_NOTES" v1.0.0 1.1.0 gentilpedro/Nexus)"

  assert_contains "$notes" "Corpo que cabe folgado" "mantém o corpo quando cabe no teto"
  record_check
  if [[ "$notes" == *"Notas condensadas"* ]]; then
    fail "não avisa de condensação quando não condensou"
  else
    pass "não avisa de condensação quando não condensou"
  fi
)

checks="$(grep -c . "${TALLY_DIR}/checks" || true)"
failures="$(grep -c . "${TALLY_DIR}/failures" || true)"

printf '\n%s verificações, %s falha(s)\n' "$checks" "$failures"
[ "$failures" -eq 0 ]
