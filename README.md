# Nexus

Plataforma de gestão de projetos em português para equipes brasileiras pequenas e médias. Reúne num só
lugar workspaces com spaces e listas; tarefas em lista, quadro Kanban, backlog, calendário e Gantt; sprints
com burndown; docs com edição colaborativa em tempo real e planilhas; chat por workspace com menções;
notificações e busca.

Projeto de TCC do curso do Senac. A documentação de arquitetura, modelo de dados e funcionalidades fica na
[Wiki](https://gitlab.com/senac-projetos-de-desenvolvimento/2025-pedro-gentil/tcc_2/nexus/-/wikis/home).

## Stack

- **.NET 10** com Blazor (Interactive Server) e ASP.NET Core Identity
- **PostgreSQL** via EF Core, com migrations aplicadas na subida do app
- **Redis** como backplane opcional do tempo real (chat e docs) entre instâncias
- **GitHub Actions** para build, testes, deploy e releases versionadas

## Estrutura

```
src/
  Nexus.Domain/          entidades e regras de domínio
  Nexus.Infrastructure/  EF Core, migrations e configuração de dados
  Nexus.Web/             app Blazor: páginas, serviços, autenticação, wwwroot
tests/                   um projeto de teste por camada, mais testes JS do editor colaborativo
docs/                    backup, LGPD, sincronização GitHub/GitLab, versionamento, dados de demo
```

## Rodando localmente

```bash
cp .env.example .env
docker compose up db -d
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Host=localhost;Port=5432;Database=Nexus;Username=postgres;Password=<a mesma do seu .env>" \
  --project src/Nexus.Web
dotnet run --project src/Nexus.Web
```

Detalhes, incluindo o que fazer se a porta 5432 já estiver ocupada, em
[CONTRIBUTING.md](CONTRIBUTING.md#rodando-localmente). As contas de demonstração estão em
[docs/dados-demonstracao.md](docs/dados-demonstracao.md).

## Contribuindo

Convenção de commits, fluxo de branch e MR/PR, testes e migrations: [CONTRIBUTING.md](CONTRIBUTING.md).
Notas de cada versão: [GitHub Releases](https://github.com/gentilpedro/Nexus/releases).

## Licença

[Apache 2.0](LICENSE).
