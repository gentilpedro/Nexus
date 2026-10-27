# Nexus — dados de demonstração

> ## ⚠️ PRODUÇÃO TEM DADOS FALSOS — APAGAR DEPOIS DA BANCA FINAL DO TCC
>
> Em 26/10/2026 entrou em **produção** o workspace de demonstração **Aurora Digital**, com 8
> contas falsas (`@example.com`), para gravar o vídeo e apresentar o TCC. Ele precisa ser
> removido depois da banca final: issue **#59**, passo a passo na seção
> [Produção: workspace Aurora Digital](#produção-workspace-aurora-digital-temporário).

Este arquivo lista as contas e workspaces do banco local (Docker) usados
só para demonstrar o aplicativo. Não é dado real — nomes, e-mails e o
"cliente" (Josapar) são fictícios/de exemplo.

## Como acessar

Suba o app localmente seguindo [Rodando localmente](../CONTRIBUTING.md#rodando-localmente)
e faça login em `/Account/Login` com qualquer um dos e-mails abaixo.

**Senha das contas de demonstração:** não versionada. Defina a sua ao criar os
usuários locais (direto pela tela de cadastro) e guarde-a fora do repositório.
A política de senha exige no mínimo 10 caracteres com maiúscula, minúscula,
dígito e símbolo.

> Essas contas existem só no banco de desenvolvimento local — não fazem
> parte de nenhuma migration, então não aparecem automaticamente em um
> deploy novo. Se for mostrar o app pra alguém fora da sua máquina,
> use uma senha própria, que não seja reaproveitada em nenhum outro lugar.

## Usuários

| Nome | E-mail | Papel no time |
|---|---|---|
| Pedro Rodrigues | pedro@nexusdemo.com | Owner dos dois workspaces |
| Juliana Ferreira | juliana@nexusdemo.com | Gerente de projetos |
| Ana Beatriz Souza | ana@nexusdemo.com | Desenvolvedora (Backend) |
| Carlos Eduardo Lima | carlos@nexusdemo.com | Desenvolvedor (Frontend) |
| Rafael Santos | rafael@nexusdemo.com | QA / Testes |

## Workspaces

### Agência Norte
Membros: Pedro (Owner), Juliana (Admin), Ana, Carlos, Rafael.

- Espaços **Clientes** e **Produto**
- Docs: "Guia de onboarding do time", "Orçamento do lançamento" e
  "Checklist de publicação do site"

### Venda - Cliente Josapar
Funil de venda com um cliente fictício. Membros: Pedro (Owner), Juliana.

- Espaço **Pré-venda**

## Produção: workspace Aurora Digital (temporário)

Criado para o vídeo e a banca do TCC. **Não é dado real**: pessoas, empresa (Aurora Digital) e
produto (app de delivery "Sabor Local") são fictícios.

### O que tem

- **Dono:** a conta real do Pedro (`gentil.pedro21@gmail.com`). Ela não tem limites em produção
  (`Plans:UnlimitedAccounts`), então o workspace tem tudo liberado: sprints, Gantt, burndown e
  membros sem limite.
- **8 contas falsas**, todas com a mesma senha. A senha **não está no repositório**: o Pedro
  recebeu por fora.

  | Nome | E-mail | Papel |
  |---|---|---|
  | Mariana Costa | mariana.costa@example.com | Admin — produto |
  | Lucas Almeida | lucas.almeida@example.com | Admin — tech lead |
  | Ana Beatriz Souza | ana.souza@example.com | Membro — backend |
  | Carlos Eduardo Lima | carlos.lima@example.com | Membro — frontend |
  | Rafael Santos | rafael.santos@example.com | Membro — QA |
  | Juliana Ferreira | juliana.ferreira@example.com | Membro — design |
  | Thiago Martins | thiago.martins@example.com | Membro — mobile |
  | Camila Rocha | camila.rocha@example.com | Membro — dados e marketing |

- **Spaces:**
  - **Produto:** listas "App Sabor Local" e "Site institucional";
  - **Operações:** listas "Suporte e incidentes" e "Infraestrutura";
  - **Marketing:** lista "Campanha de lançamento".
- **Lista "App Sabor Local":**
  - 5 épicos com datas, para o Gantt;
  - 4 sprints: duas concluídas, uma ativa com burndown e uma planejada;
  - backlog, etiquetas e campos personalizados (Story points, Plataforma).
- **Conteúdo:**
  - 63 tarefas e 19 comentários;
  - 5 documentos de texto e 2 planilhas;
  - 14 mensagens no chat, com menções;
  - notificações para o Pedro.

`@example.com` é um domínio reservado: nenhum e-mail chega a ninguém de verdade.

### Como foi criado

`deploy/demo/gerar-seed-demo.py` gera `deploy/demo/seed-demo.sql`. As datas são relativas ao dia
em que o script roda, e ele recusa rodar se o workspace já existir. Antes de rodar, faça o backup
(`nexus-backup`). Para rodar:

```bash
# na VPS; <hash> é o hash Identity (v3, PBKDF2-SHA512) da senha, gerado fora do repositório
docker exec -i nexus-db-1 sh -c 'psql -v ON_ERROR_STOP=1 -v senha_hash=<hash> -U $POSTGRES_USER -d $POSTGRES_DB' < seed-demo.sql
```

Tudo o que ele cria usa ids com o prefixo `d3e70000-`. É por esse prefixo que a remoção encontra
os dados.

### Como remover (depois da banca)

1. Backup: `/usr/local/bin/nexus-backup` na VPS.
2. Copie `deploy/demo/remover-demo.sql` para a VPS e rode:

   ```bash
   docker exec -i nexus-db-1 sh -c 'psql -At -v ON_ERROR_STOP=1 -U $POSTGRES_USER -d $POSTGRES_DB' < remover-demo.sql
   ```

   O script roda numa transação. Ele apaga o workspace inteiro, inclusive o que foi criado
   durante a gravação, as 8 contas, as notificações e os registros de auditoria ligados a eles.
   No fim, precisa mostrar `restam workspaces demo: 0` e `restam contas demo: 0`.
3. Se ele listar linhas `ARQUIVO:` ou `AVATAR:` (anexos enviados durante a gravação), apague cada
   uma do volume de uploads:
   `docker exec nexus-web-1 rm /app/App_Data/uploads/<caminho>`.
4. Feche a issue #59 e apague esta seção e a pasta `deploy/demo/`.

