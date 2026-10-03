# Contribuindo com o Nexus

Guia rápido para contribuir com o projeto — convenções de commit, fluxo de branch/PR e como rodar os
testes localmente. Para entender o projeto em si (arquitetura, modelo de dados, funcionalidades), veja a
[Wiki](https://gitlab.com/senac-projetos-de-desenvolvimento/2025-pedro-gentil/tcc_2/nexus/-/wikis/home).

## Convenção de commits

O Nexus usa [Conventional Commits](https://www.conventionalcommits.org/pt-br/) (`feat:`, `fix:`, `docs:`,
`chore:`, ...). Isso não é só estilo: a mensagem de commit **decide o incremento de versão** e vira o texto
das notas de release, geradas automaticamente pelo pipeline.

| Tipo de commit | Efeito na versão |
|---|---|
| `feat!:` ou corpo com `BREAKING CHANGE` | major (1.4.2 → 2.0.0) |
| `feat:` | minor (1.4.2 → 1.5.0) |
| `fix:`, `docs:`, `chore:`, outros | patch (1.4.2 → 1.4.3) |

Escreva o corpo do commit explicando o *porquê* da mudança, não só o *o quê* — é esse texto que aparece na
nota de release meses depois. Um commit `fix: ajustes` gera uma nota inútil.

## Fluxo de branch e PR

1. Crie uma branch a partir da `main`.
2. Abra um Pull Request para `main`. Isso dispara os jobs `version` (calcula a versão que o merge geraria)
   e `verify` (build, testes, checagem de dependências vulneráveis, análise estática) — sem publicar nada.
3. Após aprovação e merge na `main`, o pipeline roda `deploy` (FTPS → IIS) e `release` (tag + GitHub
   Release), automaticamente.

Detalhes completos do pipeline na página [CI/CD e Deploy](https://gitlab.com/senac-projetos-de-desenvolvimento/2025-pedro-gentil/tcc_2/nexus/-/wikis/CI-CD-e-Deploy)
da wiki.

## Dois repositórios, um histórico

O projeto vive no [GitHub](https://github.com/gentilpedro/Nexus) (de onde sai o deploy) e no
[GitLab](https://gitlab.com/senac-projetos-de-desenvolvimento/2025-pedro-gentil/tcc_2/nexus) (repositório do
TCC). Os dois são sincronizados automaticamente e **compartilham o mesmo histórico de commits** — o que
impõe duas regras:

- **Nunca reescreva o histórico da `main`** (`--force`, `rebase` ou `amend` em commit já publicado). Isso
  faz o outro repositório divergir na hora e quebra a sincronização.
- **Nunca faça push direto na `main`**, em nenhum dos dois. Mudança entra por PR (GitHub) ou MR (GitLab).

Commit feito no GitHub aparece no GitLab sozinho, poucos minutos depois. Commit feito no GitLab vira um
Pull Request no GitHub, que espera aprovação antes de virar deploy. Como isso funciona, e o que fazer
quando falha: [`docs/sincronizacao-github-gitlab.md`](docs/sincronizacao-github-gitlab.md).

## Rodando localmente

```bash
cp .env.example .env
docker compose up db -d
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Host=localhost;Port=5432;Database=Nexus;Username=postgres;Password=<a mesma do seu .env>" \
  --project src/Nexus.Web
dotnet run --project src/Nexus.Web
```

Se o `dotnet run` falhar com `28P01: autenticação do tipo senha falhou` mesmo com a senha certa,
provavelmente outro Postgres já ocupa a porta 5432 da sua máquina (por exemplo, um PostgreSQL instalado
direto no Windows). Nesse caso, publique o container em outra porta com um `docker-compose.override.yml`
(já ignorado pelo git) e use essa porta nos user-secrets:

```yaml
# docker-compose.override.yml
services:
  db:
    ports:
      - "5433:5432"
```

```bash
docker compose up db -d
dotnet user-secrets set "ConnectionStrings:DefaultConnection"   "Host=localhost;Port=5433;Database=Nexus;Username=postgres;Password=<a mesma do seu .env>"   --project src/Nexus.Web
```

As contas de demonstração estão em [`docs/dados-demonstracao.md`](docs/dados-demonstracao.md).
Passo a passo completo (incluindo dados de demonstração) na página
[Como Rodar Localmente](https://gitlab.com/senac-projetos-de-desenvolvimento/2025-pedro-gentil/tcc_2/nexus/-/wikis/Como-Rodar-Localmente)
da wiki.

## Testes

```bash
dotnet test
```

A solução mantém um projeto de teste por camada — `Nexus.Domain.Tests`, `Nexus.Infrastructure.Tests` e
`Nexus.Web.Tests`. Uma mudança em código de domínio ou infraestrutura deve vir acompanhada de teste na
camada correspondente.

## Migrations de banco

Ao alterar uma entidade em `Nexus.Domain.Entities`, gere a migration correspondente:

```bash
dotnet ef migrations add NomeDaMigration \
  --project src/Nexus.Infrastructure --startup-project src/Nexus.Web
```

Se a migration remover coluna/tabela ou alterar dado (`DropColumn`, `DropTable`,
`migrationBuilder.Sql` que altera dados), faça um dump de segurança antes do merge — ver
`docs/backup-restore.md`.

## Dado pessoal e LGPD

Se a mudança adicionar ou alterar uma entidade que armazena dado pessoal, atualize também o
[Registro de Operações de Tratamento](docs/lgpd/rot.md) na mesma alteração — ele é mantido junto ao código
justamente para não ficar defasado.

## Segurança

Encontrou uma vulnerabilidade? Não abra uma issue pública — veja o
[plano de resposta a incidente](docs/lgpd/plano-resposta-incidente.md) para o canal de contato adequado.
