# 📚 Referência da API de Integração

!!! abstract ""
    Todas as rotas abaixo ficam em `/api/v1`, exigem `Authorization: Bearer <chave>` e respondem JSON.
    Os erros comuns a todas (`401`, `403`, `429`) estão descritos em [API de Integração](api.md#respostas-de-erro).
    A fonte da verdade é a especificação gerada pelo app em [`/openapi/v1.json`](https://nexus.aamora.com.br/openapi/v1.json).

## Objetos

### Tarefa

```json
{
  "id": "9b2e4c1a-...",
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
  "url": "https://nexus.aamora.com.br/lists/4f1c.../list"
}
```

| Campo | Tipo | Observação |
|---|---|---|
| `id` | GUID | Imutável |
| `code` / `number` | texto / inteiro | Sequencial por workspace (`TASK-1`, `TASK-2`…), nunca reaproveitado |
| `type` | `Task` `Bug` `Story` `Epic` | |
| `priority` | `Lowest` `Low` `Medium` `High` `Highest` | |
| `status` | objeto | `category` é `ToDo`, `InProgress` ou `Done`; `isDefault` marca o status inicial da lista |
| `assignee` | objeto ou `null` | Membro do workspace |
| `startDateUtc`, `dueDateUtc` | data ISO ou `null` | Sempre UTC |
| `externalReference` | texto (até 100) ou `null` | Id da tarefa no sistema de origem; único por workspace |
| `updatedAtUtc` | data ou `null` | `null` enquanto a tarefa nunca foi editada |
| `url` | texto | Lista da tarefa no Nexus |

### Erro de validação (`400`)

`application/problem+json` com `errors`: objeto cujo nome é o campo em camelCase (`title`,
`taskListId`, `statusId`, `assigneeId`, `startDateUtc`, `description`, `externalReference`) e o valor
é uma lista de mensagens em português. Todos os erros vêm de uma vez.

---

## `GET /workspace`

Dados do workspace ao qual a chave dá acesso.

**200**

```json
{ "id": "...", "name": "Acme", "slug": "acme", "isArchived": false }
```

## `GET /lists`

Spaces, listas e os status de cada lista. É aqui que se descobre o `taskListId` para criar tarefas
e os `statusId` para filtrar ou mover.

**200**

```json
[
  {
    "id": "...",
    "name": "Operações",
    "lists": [
      {
        "id": "4f1c...",
        "name": "Pedidos",
        "statuses": [
          { "id": "...", "name": "A fazer",  "category": "ToDo",       "isDefault": true },
          { "id": "...", "name": "Fazendo",  "category": "InProgress", "isDefault": false },
          { "id": "...", "name": "Feito",    "category": "Done",       "isDefault": false }
        ]
      }
    ]
  }
]
```

Os status vêm na ordem das colunas do quadro.

## `GET /members`

Membros do workspace. O `id` é o que vai em `assigneeId`.

**200**

```json
[
  { "id": "...", "name": "Ana Souza", "email": "ana@acme.com.br", "role": "Owner" },
  { "id": "...", "name": "Bruno Lima", "email": "bruno@acme.com.br", "role": "Member" }
]
```

`role` é `Owner`, `Admin` ou `Member`.

## `GET /tasks`

Lista tarefas do workspace, paginada, em ordem crescente de `number`.

| Parâmetro | Tipo | Padrão | O que faz |
|---|---|---|---|
| `listId` | GUID | — | Só tarefas dessa lista |
| `statusId` | GUID | — | Só tarefas nesse status |
| `assigneeId` | texto | — | Só tarefas desse responsável |
| `updatedSince` | data ISO | — | Tarefas criadas ou alteradas a partir desse instante (compara `updatedAtUtc`, ou `createdAtUtc` quando nunca editada) |
| `page` | inteiro | 1 | Página, a partir de 1 |
| `pageSize` | inteiro | 50 | Itens por página, **máximo 100** |

**200**

```json
{ "items": [ { "...": "tarefa" } ], "page": 1, "pageSize": 50, "total": 137 }
```

## `GET /tasks/{idOrCode}`

Uma tarefa pelo `id` ou pelo código (`TASK-12`; só o número, `12`, também vale).

| Código | |
|---|---|
| **200** | A tarefa |
| **404** | Não existe neste workspace (inclusive quando existe em outro) |

## `POST /tasks`

Cria uma tarefa.

**Corpo**

| Campo | Obrigatório | Regras |
|---|---|---|
| `taskListId` | sim | Lista do workspace da chave |
| `title` | sim | Até 500 caracteres; espaços nas pontas são removidos |
| `description` | não | Até 10.000 caracteres |
| `type` | não | Padrão `Task` |
| `priority` | não | Padrão `Medium` |
| `statusId` | não | Status da lista. Sem informar: o status inicial da lista (`isDefault`), ou o primeiro |
| `assigneeId` | não | Membro do workspace |
| `startDateUtc`, `dueDateUtc` | não | Início não pode ser depois do prazo |
| `externalReference` | não | Até 100 caracteres. Se já existir no workspace, responde `409` |

```json
{
  "taskListId": "4f1c...",
  "title": "Separar pedido 1001",
  "priority": "High",
  "assigneeId": "...",
  "dueDateUtc": "2026-11-21",
  "externalReference": "PEDIDO-1001"
}
```

| Código | Corpo |
|---|---|
| **201** | A tarefa criada. `Location: /api/v1/tasks/{id}` |
| **400** | Erro de validação |
| **409** | `{ "message": "...", "task": { ...tarefa existente } }` quando a `externalReference` já existe |

O responsável, se informado, recebe a mesma notificação (e o mesmo e-mail) que receberia se alguém
o atribuísse pela tela. A tarefa fica registrada como criada por quem gerou a chave, e a notificação
diz `Integração "<nome da chave>" atribuiu TASK-n ...`.

## `PATCH /tasks/{idOrCode}`

Atualização parcial. Mande só o que muda.

| Campo | Regras |
|---|---|
| `title` | Mesmas regras do `POST` |
| `description` | `""` limpa |
| `type`, `priority` | |
| `statusId` | Status da mesma lista. Se a lista tem regras de transição, a mudança precisa ser permitida a partir do status atual |
| `assigneeId` | Membro do workspace; `""` tira o responsável |
| `startDateUtc`, `dueDateUtc` | Validados contra o valor que ficar (enviado ou atual) |
| `externalReference` | `""` limpa; se já existir em outra tarefa, `409` |

```json
{ "statusId": "...", "assigneeId": "" }
```

| Código | Corpo |
|---|---|
| **200** | A tarefa atualizada (`updatedAtUtc` preenchido) |
| **400** | Erro de validação, inclusive transição de status proibida (campo `statusId`) |
| **404** | Tarefa não existe neste workspace |
| **409** | Com `task`: outra tarefa já usa a `externalReference`. Sem `task`: a tarefa foi alterada por outra pessoa durante a chamada; leia de novo e repita |

Novo responsável recebe a notificação de atribuição; trocar para o mesmo responsável não notifica.

---

## Limites e números

| | |
|---|---|
| Chamadas por minuto, por chave | 120 (resposta `429`) |
| Chaves ativas por workspace | 20 |
| Itens por página | até 100 |
| Título | 500 caracteres |
| Descrição | 10.000 caracteres |
| `externalReference` | 100 caracteres |
| Nome da chave | 80 caracteres |
