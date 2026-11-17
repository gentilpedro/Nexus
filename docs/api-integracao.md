# API de integração (tarefas)

> Guia completo para quem integra, com referência de cada rota e receitas em C#, Python e curl:
> [Integrações na Wiki do projeto](https://gitlab.com/senac-projetos-de-desenvolvimento/2025-pedro-gentil/tcc_2/nexus/-/wikis/Integracoes).

A API (#66) existe para um sistema externo, normalmente um ERP, criar e acompanhar tarefas de um
workspace sem ninguém digitar nada de novo no Nexus. A primeira versão cobre tarefas, listas,
status e membros. A especificação OpenAPI gerada pelo próprio app fica em `/openapi/v1.json`.

Base: `https://<seu-nexus>/api/v1`. Tudo é JSON, em UTF-8.

## Autenticação

Cada chave pertence a **um** workspace e só enxerga ele. Owner ou Admin gera a chave em
**Configurações do workspace → Integrações**: o valor completo (`nxk_...`) aparece uma única vez;
o Nexus guarda só o hash. Envie a chave em todas as chamadas:

```http
Authorization: Bearer nxk_sua_chave
```

| Situação | Resposta |
|---|---|
| Sem chave, chave inválida ou revogada | `401` com `application/problem+json` |
| Plano do workspace sem a API (Starter, com os limites de plano ligados) | `403` |
| Workspace arquivado | `403` |
| Chave só de leitura em `POST` ou `PATCH` | `403` |
| Mais de 120 chamadas por minuto com a mesma chave | `429` com `Retry-After` em segundos |

Ao gerar a chave, escolha o **escopo**: *leitura e escrita* (para um ERP) ou *só leitura* (para
painéis e relatórios, que não devem conseguir criar tarefa). Revogar uma chave (na mesma tela)
corta o acesso na hora. Criação e revogação ficam no log de auditoria.

A API faz parte dos planos **Pro e Business** (`PlanCapabilities.IncludesApiAccess`). Como os
outros recursos pagos, a regra só é aplicada com `Plans:EnforceLimits` ligado; um workspace que
volta ao Starter recebe `403` em toda chamada e volta a funcionar ao assinar de novo, com as
mesmas chaves.

Toda resposta traz `X-RateLimit-Limit` (120) e `X-RateLimit-Remaining` (quantas chamadas ainda
cabem no minuto atual). No `429`, `Retry-After` diz quantos segundos esperar.

## Rotas

| Método e rota | O que faz |
|---|---|
| `GET /workspace` | Nome, slug e situação do workspace da chave |
| `GET /lists` | Spaces, listas e os status de cada lista (ids para criar tarefas) |
| `GET /members` | Membros do workspace (ids aceitos em `assigneeId`) |
| `GET /tasks` | Lista tarefas, paginada, com filtros |
| `GET /tasks/{id}` | Uma tarefa, pelo id ou pelo código (`TASK-12`) |
| `POST /tasks` | Cria uma tarefa |
| `PATCH /tasks/{id}` | Atualiza campos de uma tarefa |

### Criar uma tarefa

```bash
curl -X POST https://<seu-nexus>/api/v1/tasks \
  -H "Authorization: Bearer nxk_sua_chave" \
  -H "Content-Type: application/json" \
  -d '{
    "taskListId": "4f1c...",
    "title": "Separar pedido 1001",
    "description": "Cliente pediu entrega até sexta.",
    "type": "Task",
    "priority": "High",
    "assigneeId": "id-do-membro",
    "dueDateUtc": "2026-11-21",
    "externalReference": "PEDIDO-1001"
  }'
```

- `taskListId` e `title` são obrigatórios. Os demais são opcionais.
- `type`: `Task`, `Bug`, `Story` ou `Epic`. `priority`: `Lowest`, `Low`, `Medium`, `High`,
  `Highest`. Sem informar, a tarefa nasce como `Task` e `Medium`.
- `statusId` precisa ser um status da lista (veja `GET /lists`). Sem informar, usa o status
  inicial da lista.
- `assigneeId` precisa ser membro do workspace (`GET /members`). O responsável recebe a mesma
  notificação de atribuição que receberia pela tela.
- Datas em ISO 8601. Uma data sem fuso (`2026-11-21`) conta como UTC.
- **`externalReference`** é o id da tarefa no sistema de origem. Repetir a chamada com a mesma
  referência não duplica: a API responde `409` com a tarefa que já existe. Use sempre que o ERP
  puder reenviar.

Resposta `201`:

```json
{
  "id": "9b2e...",
  "code": "TASK-12",
  "number": 12,
  "title": "Separar pedido 1001",
  "description": "Cliente pediu entrega até sexta.",
  "type": "Task",
  "priority": "High",
  "status": { "id": "...", "name": "A fazer", "category": "ToDo", "isDefault": true },
  "list": { "id": "4f1c...", "name": "Pedidos" },
  "assignee": { "id": "...", "name": "Ana Souza", "email": "ana@acme.com.br" },
  "startDateUtc": null,
  "dueDateUtc": "2026-11-21T00:00:00Z",
  "externalReference": "PEDIDO-1001",
  "createdAtUtc": "2026-11-16T12:00:00Z",
  "updatedAtUtc": null,
  "url": "https://<seu-nexus>/lists/4f1c.../list"
}
```

Erro de validação (`400`): `application/problem+json` com `errors` por campo, por exemplo
`{ "errors": { "assigneeId": ["O responsável precisa ser membro do workspace. ..."] } }`.

### Atualizar

```bash
curl -X PATCH https://<seu-nexus>/api/v1/tasks/TASK-12 \
  -H "Authorization: Bearer nxk_sua_chave" \
  -H "Content-Type: application/json" \
  -d '{ "statusId": "id-do-status-feito" }'
```

- Só o que for enviado muda. `""` limpa `description`, `assigneeId` ou `externalReference`.
- Mudar de status respeita as regras de transição da lista: uma transição proibida responde
  `400` com erro em `statusId`.
- Se outra pessoa alterou a tarefa durante a chamada, a resposta é `409` sem tarefa no corpo.
  Leia de novo e repita.

### Listar

```
GET /tasks?listId=...&statusId=...&assigneeId=...&updatedSince=2026-11-15T00:00:00Z&page=1&pageSize=50
```

Todos os filtros são opcionais. `pageSize` vai até 100. `updatedSince` traz só as tarefas criadas
ou alteradas a partir da data: é o jeito de sincronizar sem baixar tudo a cada vez. A resposta
tem `items`, `page`, `pageSize` e `total`.

## Webhooks de saída

O caminho de volta: o Nexus chama a URL do seu sistema quando uma tarefa é criada, muda de
status ou de responsável, tem outros campos alterados ou recebe um comentário. Cadastro em
**Configurações do workspace → Integrações → Webhooks de saída**, com URL `https` pública e os
eventos que interessam. O segredo (`whsec_...`) aparece uma vez e assina cada chamada em
`X-Nexus-Signature: sha256=<HMAC-SHA256 do corpo>`.

Entrega por `POST` com o mesmo JSON de tarefa da API dentro de `task`, mais `type`, `id` do
evento, `actor` e `previous` (o que mudou). `2xx` conta como entregue; senão o Nexus tenta de novo
em 1, 5 e 30 minutos e desiste na quarta falha. 50 falhas definitivas seguidas pausam o webhook e
avisam os administradores. As últimas 20 entregas ficam na tela, com "reenviar" e "enviar teste".

Eventos, corpo completo, validação da assinatura em C# e Python e boas práticas do receptor:
[Webhooks de Saída na Wiki](https://gitlab.com/senac-projetos-de-desenvolvimento/2025-pedro-gentil/tcc_2/nexus/-/wikis/Webhooks-de-Saida).

## Assistentes de IA (servidor MCP)

O mesmo conjunto de operações está disponível como servidor MCP em `/mcp` (Streamable HTTP, sem
sessão), com a mesma chave em `Authorization: Bearer`. Ferramentas: `get_workspace`,
`list_lists`, `list_members`, `list_tasks`, `get_task`, `create_task` e `update_task` (as duas
últimas exigem chave de leitura e escrita). O código fica em `src/Nexus.Web/Mcp/NexusTools.cs`
e reaproveita o `WorkItemService`: o assistente nunca faz nada que a API não deixaria. Conexão do
Claude Desktop e do Claude Code na
[Wiki](https://gitlab.com/senac-projetos-de-desenvolvimento/2025-pedro-gentil/tcc_2/nexus/-/wikis/Assistentes-de-IA-MCP).

## O que a API não faz (ainda)

- Comentários, anexos, etiquetas e campos personalizados.
- Excluir tarefas.

Essas são as próximas issues, junto com o servidor MCP (#67) e o feed de calendário (#68).
