# Nexus — dados de demonstração

Este arquivo lista as contas e workspaces criados no banco local (Docker)
só para demonstrar o aplicativo. Não é dado real — nomes, e-mails e o
"cliente" (Josapar) são fictícios/de exemplo.

## Como acessar

Antes da primeira vez, copie `.env.example` para `.env` (não versionado)
e ajuste a senha se quiser. Suba o app localmente (`docker compose up db
-d` + `dotnet run --project src/Nexus.Web`) e faça login em
`/Account/Login` com qualquer um dos e-mails abaixo.

> A connection string local (com a senha do Postgres) fica nos User
> Secrets do projeto, não no `appsettings.json` — se clonar em outra
> máquina, rode `dotnet user-secrets set
> "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;
> Database=Nexus;Username=postgres;Password=<a mesma do seu .env>"
> --project src/Nexus.Web` primeiro.

**Senha das contas de demonstração:** não versionada. Defina a sua ao criar os
usuários locais (`dotnet user-secrets`, ou direto pela tela de cadastro) e
guarde-a fora do repositório.

> A senha de demonstração ficava escrita aqui em texto claro. Mesmo valendo só
> para o banco local, uma senha versionada tende a ser reaproveitada — e este
> arquivo também identifica uma conta real. A política de senha exige no mínimo
> 10 caracteres com maiúscula, minúscula, dígito e símbolo.

> Essas contas existem só no banco de desenvolvimento local — não fazem
> parte de nenhuma migration, então não aparecem automaticamente em um
> deploy novo. Se for mostrar o app pra alguém fora da sua máquina,
> troque essa senha ou recrie os usuários com uma senha própria antes.

## Usuários

| Nome | E-mail | Papel no time |
|---|---|---|
| Pedro Rodrigues | pedro.rodrigues@josapar.com.br | Owner dos dois workspaces (conta real) |
| Ana Beatriz Souza | ana.souza@nexusdemo.com | Desenvolvedora (Backend) |
| Carlos Eduardo Lima | carlos.lima@nexusdemo.com | Desenvolvedor (Frontend) |
| Juliana Ferreira | juliana.ferreira@nexusdemo.com | Gerente de projetos |
| Rafael Santos | rafael.santos@nexusdemo.com | QA / Testes |
| Mariana Costa | mariana.costa@nexusdemo.com | Executiva de contas (vendas) |

## Workspaces

### Nexus App - Desenvolvimento
Time construindo um app do zero. Membros: Pedro (Owner), Juliana (Admin),
Ana, Carlos, Rafael.

- **Backend** — modelagem de banco, API de autenticação, pagamentos, CI/CD
- **Frontend** — tela de login, dashboard, responsividade
- **QA & Testes** — testes de integração, homologação da release

Inclui uma página de Docs ("Guia de onboarding do time") e mensagens de
chat de exemplo.

### Venda - Cliente Josapar
Funil de venda com um cliente fictício. Membros: Pedro (Owner), Mariana
(Admin), Juliana.

- **Pré-venda** — levantamento de requisitos, apresentação, agendamento
- **Proposta Comercial** — proposta técnica, precificação, contrato
- **Pós-venda** — onboarding, configuração de ambiente, treinamento

Inclui uma página de Docs ("Resumo da negociação") e mensagens de chat
de exemplo.
