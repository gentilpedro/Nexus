# Versionamento e releases

Toda entrega em produção vira uma versão rastreável. O número sai dos próprios commits, a release
é publicada automaticamente depois do deploy, e o app diz em que versão está rodando.

## Como o número da versão é decidido

O Nexus segue [SemVer](https://semver.org/lang/pt-BR/) e deriva o incremento das mensagens de
commit, na convenção [Conventional Commits](https://www.conventionalcommits.org/pt-br/) que o
repositório já usa (`feat:`, `fix:`, `style:`, ...).

| O que apareceu nos commits desde a última release | Incremento | Exemplo |
|---|---|---|
| `BREAKING CHANGE` no corpo, ou `!` depois do tipo (`feat!:`) | **major** | 1.4.2 → 2.0.0 |
| Pelo menos um `feat:` | **minor** | 1.4.2 → 1.5.0 |
| Qualquer outra coisa (`fix:`, `docs:`, `chore:`, fora da convenção) | **patch** | 1.4.2 → 1.4.3 |

Vale o maior incremento encontrado: um `feat:` junto de três `fix:` resulta em minor.

A primeira release é a `v1.0.0` — o Nexus já está em produção com usuários reais, então começar em
`0.x` diria o contrário.

> **Consequência prática:** a mensagem de commit deixou de ser só documentação. Ela decide o número
> da versão e é o texto que aparece nas notas da release. Um commit `fix: ajustes` gera uma nota
> inútil; um commit com título claro e corpo explicando o porquê gera uma nota que se lê meses
> depois.

## O que acontece a cada evento

**Ao abrir ou atualizar um PR para a `main`:**

1. `version` — testa os scripts de release e calcula qual versão aquele merge produziria (aparece
   no resumo da execução).
2. `verify` — restore, build, testes, checagem de dependências vulneráveis, análise estática e
   publish.
3. `deploy` e `release` **não rodam**.

**Ao entrar na `main`:**

1. `version` e `verify`, iguais aos de cima.
2. `deploy` — publica por FTPS no IIS.
3. `release` — cria a tag `vX.Y.Z` e a GitHub Release com as notas.

A tag é criada **depois** do deploy, de propósito: ela significa "esta versão está no ar", não
"esta versão compilou". Se o deploy falhar, nenhuma tag é criada e o mesmo número é recalculado na
próxima tentativa.

## As notas da release

São geradas por [`.github/scripts/release-notes.sh`](../.github/scripts/release-notes.sh) a partir
dos commits entre a tag anterior e a atual, agrupados por tipo (Novidades, Correções, Segurança,
Performance, ...). Cada item traz o título do commit, o corpo completo — que é onde mora a
explicação do *porquê* — e o link para o commit. No fim vão os PRs incluídos e o link de
comparação entre as duas versões.

Linhas de `Co-Authored-By:` e afins são removidas. Corpos com mais de 30 linhas são truncados, com
link para o commit completo.

## Como saber qual versão está no ar

**Pelo endpoint:**

```
GET https://usenexus.runasp.net/version

{
  "version": "1.4.0",
  "commit": "a3f9c21d4e5b6a7c8d9e0f1a2b3c4d5e6f708192",
  "builtAt": "2026-08-09T14:22:10Z"
}
```

Anônimo e sem rate limit — os valores são constantes lidas na inicialização, então responder custa
menos que servir o favicon.

**Pela interface:** o rodapé mostra `Nexus 1.4.0`; passar o mouse revela o commit e a data da build.

Uma build feita na máquina de alguém (sem passar pela CI) se identifica como **`0.0.0-local`**. Se
`/version` responder isso em produção, o que está no ar não veio da pipeline.

## Como a versão chega no binário

A CI passa três propriedades ao MSBuild:

```
-p:Version=1.4.0
-p:SourceRevisionId=<sha completo>
-p:BuildTimestamp=<ISO 8601 UTC>
```

O SDK junta as duas primeiras em `AssemblyInformationalVersion` (`1.4.0+<sha>`); a terceira vira um
`AssemblyMetadata`. [`AppVersion`](../src/Nexus.Web/Services/AppVersion.cs) lê os dois na
inicialização.

O step *Conferir a versão carimbada* falha a build se o carimbo não chegou ao binário publicado —
sem ele, um erro nas propriedades passaria despercebido e produção se identificaria como
`0.0.0-local`.

## Mexer nos scripts

```bash
.github/scripts/test-release-scripts.sh
```

Monta repositórios git descartáveis e confere cada regra de incremento e a formatação das notas. A
CI roda esse mesmo teste no job `version`, antes de usar os scripts — a lógica decide o número que
vai para produção, então um erro silencioso aqui só apareceria depois de publicado.

Para ver as notas que sairiam agora, sem publicar nada:

```bash
.github/scripts/release-notes.sh "$(git tag --list 'v[0-9]*' --sort=-v:refname | head -n1)" \
  "$(.github/scripts/next-version.sh | sed -n 's/^version=//p')" gentilpedro/Nexus
```

## Situações incomuns

**Push na main sem commits novos desde a última tag.** `has_changes=false` e o job `release` é
pulado. O deploy acontece normalmente.

**Precisa forçar um major sem mudança incompatível de verdade.** Um commit vazio resolve:
`git commit --allow-empty -m "feat!: marco da versão 2" -m "BREAKING CHANGE: ..."`.

**A release saiu com o número errado.** Apague a release e a tag (`gh release delete vX.Y.Z
--cleanup-tag`) e refaça o push. O cálculo parte sempre da maior tag existente.
