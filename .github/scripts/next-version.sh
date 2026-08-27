#!/usr/bin/env bash
#
# Decide a próxima versão semântica a partir dos commits desde a última tag de release.
#
# A regra segue Conventional Commits, que é a convenção que o repositório já usa nas mensagens
# ("fix:", "feat:", "style:", ...):
#
#   BREAKING CHANGE no corpo, ou "!" depois do tipo  -> major   (1.4.2 -> 2.0.0)
#   pelo menos um "feat:"                            -> minor   (1.4.2 -> 1.5.0)
#   qualquer outra coisa                             -> patch   (1.4.2 -> 1.4.3)
#
# Escreve pares chave=valor em stdout, no formato que o GitHub Actions espera em $GITHUB_OUTPUT:
#
#   previous_tag=v1.4.2   (vazio na primeira release)
#   version=1.5.0
#   bump=minor
#   has_changes=true
#
# Uso: .github/scripts/next-version.sh
# Requer que o checkout tenha o histórico completo e as tags (fetch-depth: 0).

set -euo pipefail

# A primeira release não tem tag anterior de onde partir. 1.0.0 e não 0.1.0 porque o Nexus já está
# em produção servindo usuários reais — começar em 0.x diria o contrário.
readonly FIRST_VERSION="1.0.0"

# --sort=-v:refname ordena por versão (v1.10.0 > v1.9.0), diferente da ordem alfabética que
# colocaria v1.9.0 na frente.
previous_tag="$(git tag --list 'v[0-9]*' --sort=-v:refname | head -n 1)"

if [ -z "$previous_tag" ]; then
  echo "previous_tag="
  echo "version=${FIRST_VERSION}"
  echo "bump=initial"
  echo "has_changes=true"
  exit 0
fi

# --no-merges: num fluxo de merge commits (o deste repo), o commit de merge só repete o título do
# PR. Os commits reais é que carregam o tipo Conventional Commit que decide o bump.
range="${previous_tag}..HEAD"
commit_count="$(git rev-list --no-merges --count "$range")"

if [ "$commit_count" -eq 0 ]; then
  # Push sem commits novos desde a tag (ex.: republicar a mesma árvore). Nada a versionar.
  echo "previous_tag=${previous_tag}"
  echo "version=${previous_tag#v}"
  echo "bump=none"
  echo "has_changes=false"
  exit 0
fi

bump="patch"

# Separadores de campo (0x1f) e de registro (0x1e) em vez de quebra de linha: o corpo do commit é
# multilinha, então qualquer parsing por linha embaralharia os campos.
while IFS=$'\x1f' read -r -d $'\x1e' subject body; do
  # git termina cada registro com uma quebra de linha depois do separador, então do segundo
  # commit em diante o primeiro campo chega com "\n" na frente. Sem remover, o "^feat" abaixo
  # nunca casa e todo release vira patch.
  subject="${subject#$'\n'}"

  # "feat!:" ou "fix(escopo)!:" — o "!" é a marca de breaking change no título.
  if [[ "$subject" =~ ^[a-zA-Z]+(\([^\)]*\))?!: ]] || [[ "$body" == *"BREAKING CHANGE"* ]]; then
    bump="major"
    break
  fi

  if [[ "$subject" =~ ^feat(\([^\)]*\))?: ]]; then
    bump="minor"
  fi
done < <(git log --no-merges --format="%s%x1f%b%x1e" "$range")

IFS='.' read -r major minor patch <<< "${previous_tag#v}"

case "$bump" in
  major) major=$((major + 1)); minor=0; patch=0 ;;
  minor) minor=$((minor + 1)); patch=0 ;;
  patch) patch=$((patch + 1)) ;;
esac

echo "previous_tag=${previous_tag}"
echo "version=${major}.${minor}.${patch}"
echo "bump=${bump}"
echo "has_changes=true"
