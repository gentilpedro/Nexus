# Referência da API de Integração

Todas as rotas abaixo ficam em `https://nexus.aamora.com.br/api/v1`, exigem
`Authorization: Bearer <chave>` e respondem JSON. Os erros comuns a todas (`401`, `403`, `429`)
estão em [API de Integração](api.md#respostas-de-erro). A fonte da verdade é a especificação gerada
pelo app em [`/openapi/v1.json`](https://nexus.aamora.com.br/openapi/v1.json), que também pode ser
navegada em [Especificação OpenAPI](../api/index.md).

Os exemplos usam duas variáveis de shell, como nas [Receitas](api-receitas.md):

```bash
NEXUS=https://nexus.aamora.com.br
KEY=nxk_sua_chave
```

## Rotas

| Método | Rota | O que faz |
|---|---|---|
| <span class="nx-verb nx-verb--get">GET</span> | [`/workspace`](#get-workspace) | Dados do workspace da chave |
| <span class="nx-verb nx-verb--get">GET</span> | [`/lists`](#get-lists) | Spaces, listas e os status de cada lista |
| <span class="nx-verb nx-verb--get">GET</span> | [`/members`](#get-members) | Membros do workspace |
| <span class="nx-verb nx-verb--get">GET</span> | [`/tasks`](#get-tasks) | Lista tarefas, paginada, com filtros |
| <span class="nx-verb nx-verb--get">GET</span> | [`/tasks/{idOrCode}`](#get-task) | Uma tarefa pelo id ou pelo código |
| <span class="nx-verb nx-verb--post">POST</span> | [`/tasks`](#post-tasks) | Cria uma tarefa |
| <span class="nx-verb nx-verb--patch">PATCH</span> | [`/tasks/{idOrCode}`](#patch-task) | Atualiza parte de uma tarefa |
| <span class="nx-verb nx-verb--get">GET</span> | [`/time-entries`](#get-time-entries) | Lista lançamentos de horas |
| <span class="nx-verb nx-verb--post">POST</span> | [`/time-entries`](#post-time-entries) | Registra horas de um membro |
| <span class="nx-verb nx-verb--get">GET</span> | [`/checkins`](#get-checkins) | Lista os check-ins semanais |
| <span class="nx-verb nx-verb--post">POST</span> | [`/checkins`](#post-checkins) | Registra ou atualiza o check-in de uma semana |

As rotas `POST` e `PATCH` exigem uma chave de **leitura e escrita**. Com uma chave só de leitura,
respondem `403`.

## Workspace, listas e membros

### <span class="nx-verb nx-verb--get">GET</span> `/workspace` { #get-workspace }

Dados do workspace ao qual a chave dá acesso.

=== "Requisição"

    ```bash
    curl -s $NEXUS/api/v1/workspace -H "Authorization: Bearer $KEY"
    ```

=== "Resposta 200"

    ```json
    { "id": "...", "name": "Acme", "slug": "acme", "isArchived": false }
    ```

### <span class="nx-verb nx-verb--get">GET</span> `/lists` { #get-lists }

Spaces, listas e os status de cada lista. É aqui que se descobre o `taskListId` para criar tarefas
e os `statusId` para filtrar ou mover. Os status vêm na ordem das colunas do quadro.

=== "Requisição"

    ```bash
    curl -s $NEXUS/api/v1/lists -H "Authorization: Bearer $KEY"
    ```

=== "Resposta 200"

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

### <span class="nx-verb nx-verb--get">GET</span> `/members` { #get-members }

Membros do workspace. O `id` é o que vai em `assigneeId` (tarefas) e em `userId` (horas).
`role` é `Owner`, `Admin` ou `Member`.

=== "Requisição"

    ```bash
    curl -s $NEXUS/api/v1/members -H "Authorization: Bearer $KEY"
    ```

=== "Resposta 200"

    ```json
    [
      { "id": "...", "name": "Ana Souza", "email": "ana@acme.com.br", "role": "Owner" },
      { "id": "...", "name": "Bruno Lima", "email": "bruno@acme.com.br", "role": "Member" }
    ]
    ```

## Tarefas

### <span class="nx-verb nx-verb--get">GET</span> `/tasks` { #get-tasks }

Lista tarefas do workspace, paginada, em ordem crescente de `number`.

| Parâmetro | Tipo | Padrão | O que faz |
|---|---|---|---|
| `listId` | GUID | — | Só tarefas dessa lista |
| `statusId` | GUID | — | Só tarefas nesse status |
| `assigneeId` | texto | — | Só tarefas desse responsável |
| `updatedSince` | data ISO | — | Tarefas criadas ou alteradas a partir desse instante (compara `updatedAtUtc`, ou `createdAtUtc` quando nunca editada) |
| `page` | inteiro | 1 | Página, a partir de 1 |
| `pageSize` | inteiro | 50 | Itens por página, **máximo 100** |

=== "Requisição"

    ```bash
    curl -s "$NEXUS/api/v1/tasks?listId=4f1c...&pageSize=100" \
      -H "Authorization: Bearer $KEY"
    ```

=== "Resposta 200"

    ```json
    { "items": [ { "...": "tarefa" } ], "page": 1, "pageSize": 50, "total": 137 }
    ```

    Cada item tem o formato do objeto [Tarefa](#tarefa).

### <span class="nx-verb nx-verb--get">GET</span> `/tasks/{idOrCode}` { #get-task }

Uma tarefa pelo `id` ou pelo código (`TASK-12`; só o número, `12`, também vale).

=== "Requisição"

    ```bash
    curl -s $NEXUS/api/v1/tasks/TASK-12 -H "Authorization: Bearer $KEY"
    ```

=== "Resposta 200"

    O objeto [Tarefa](#tarefa).

| Resposta | |
|---|---|
| <span class="nx-status nx-status--ok">200</span> | A tarefa |
| <span class="nx-status nx-status--client">404</span> | Não existe neste workspace (inclusive quando existe em outro) |

### <span class="nx-verb nx-verb--post">POST</span> `/tasks` { #post-tasks }

Cria uma tarefa.

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

=== "Requisição"

    ```bash
    curl -s -X POST $NEXUS/api/v1/tasks \
      -H "Authorization: Bearer $KEY" -H "Content-Type: application/json" \
      -d '{
        "taskListId": "4f1c...",
        "title": "Separar pedido 1001",
        "priority": "High",
        "assigneeId": "...",
        "dueDateUtc": "2026-11-21",
        "externalReference": "PEDIDO-1001"
      }'
    ```

=== "Resposta 201"

    O objeto [Tarefa](#tarefa) criado, com `Location: /api/v1/tasks/{id}`.

=== "Resposta 409"

    ```json
    { "message": "...", "task": { "...": "tarefa existente" } }
    ```

| Resposta | |
|---|---|
| <span class="nx-status nx-status--ok">201</span> | A tarefa criada. `Location: /api/v1/tasks/{id}` |
| <span class="nx-status nx-status--client">400</span> | [Erro de validação](#erro-de-validacao) |
| <span class="nx-status nx-status--client">409</span> | A `externalReference` já existe; `task` traz a tarefa existente |

O responsável, se informado, recebe a mesma notificação (e o mesmo e-mail) que receberia se alguém
o atribuísse pela tela. A tarefa fica registrada como criada por quem gerou a chave, e a notificação
diz `Integração "<nome da chave>" atribuiu TASK-n ...`.

### <span class="nx-verb nx-verb--patch">PATCH</span> `/tasks/{idOrCode}` { #patch-task }

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

=== "Requisição"

    ```bash
    curl -s -X PATCH $NEXUS/api/v1/tasks/TASK-12 \
      -H "Authorization: Bearer $KEY" -H "Content-Type: application/json" \
      -d '{ "statusId": "...", "assigneeId": "" }'
    ```

=== "Resposta 200"

    O objeto [Tarefa](#tarefa) atualizado, com `updatedAtUtc` preenchido.

| Resposta | |
|---|---|
| <span class="nx-status nx-status--ok">200</span> | A tarefa atualizada |
| <span class="nx-status nx-status--client">400</span> | Erro de validação, inclusive transição de status proibida (campo `statusId`) |
| <span class="nx-status nx-status--client">404</span> | Tarefa não existe neste workspace |
| <span class="nx-status nx-status--client">409</span> | Com `task`: outra tarefa já usa a `externalReference`. Sem `task`: a tarefa foi alterada por outra pessoa durante a chamada; leia de novo e repita |

Novo responsável recebe a notificação de atribuição; trocar para o mesmo responsável não notifica.

## Horas

### <span class="nx-verb nx-verb--get">GET</span> `/time-entries` { #get-time-entries }

Lançamentos de horas do workspace, do mais recente para o mais antigo.

| Parâmetro | Tipo | Padrão | O que faz |
|---|---|---|---|
| `from` | data | 1º dia do mês atual | Início do período (inclusive) |
| `to` | data | Último dia do mês de `from` | Fim do período (inclusive) |
| `userId` | texto | — | Só as horas dessa pessoa |
| `taskId` | GUID | — | Só as horas dessa tarefa |
| `contractId` | GUID | — | Só as horas desse contrato |

=== "Requisição"

    ```bash
    curl -s "$NEXUS/api/v1/time-entries?from=2026-11-01&to=2026-11-30" \
      -H "Authorization: Bearer $KEY"
    ```

=== "Resposta 200"

    Uma lista de [Lançamento de horas](#lancamento-de-horas).

### <span class="nx-verb nx-verb--post">POST</span> `/time-entries` { #post-time-entries }

Registra horas de um membro. A chave é do workspace, não de uma pessoa, por isso `userId` é
obrigatório.

| Campo | Obrigatório | Regras |
|---|---|---|
| `userId` | sim | Membro do workspace (veja [`GET /members`](#get-members)) |
| `date` | sim | Entre um ano atrás e amanhã |
| `hours` | sim | Mais que zero e até 24 por lançamento; arredondado em duas casas |
| `taskId` | não | Tarefa do workspace |
| `activityId` | não | Atividade do workspace (Desenvolvimento, Reunião…) |
| `contractId` | não | Contrato que recebe as horas. Sem informar: com um contrato ativo, vincula a ele; com dois ou mais, responde `400` pedindo a escolha; sem contrato, fica sem vínculo |
| `comment` | não | Até 500 caracteres |

=== "Requisição"

    ```bash
    curl -s -X POST $NEXUS/api/v1/time-entries \
      -H "Authorization: Bearer $KEY" -H "Content-Type: application/json" \
      -d '{
        "userId": "...",
        "date": "2026-11-16",
        "hours": 1.5,
        "taskId": "9b2e4c1a-...",
        "comment": "Separação do pedido 1001"
      }'
    ```

=== "Resposta 201"

    O [Lançamento de horas](#lancamento-de-horas) criado, com
    `Location: /api/v1/time-entries/{id}`.

| Resposta | |
|---|---|
| <span class="nx-status nx-status--ok">201</span> | O lançamento criado |
| <span class="nx-status nx-status--client">400</span> | Sem `userId`, horas ou data fora da regra, tarefa, atividade ou contrato de outro workspace, contrato travado ou encerrado, ou contrato não escolhido quando há mais de um ativo. A mensagem vem em `errors[""]` |
| <span class="nx-status nx-status--client">403</span> | A pessoa não é membro do workspace |

## Check-in semanal

### <span class="nx-verb nx-verb--get">GET</span> `/checkins` { #get-checkins }

Os check-ins do workspace, do mais recente para o mais antigo, até 26 semanas.

=== "Requisição"

    ```bash
    curl -s $NEXUS/api/v1/checkins -H "Authorization: Bearer $KEY"
    ```

=== "Resposta 200"

    Uma lista de [Check-in](#check-in).

### <span class="nx-verb nx-verb--post">POST</span> `/checkins` { #post-checkins }

Registra o check-in da semana da data informada (segunda a domingo). Se a semana já tem check-in,
ele é atualizado, como na tela.

| Campo | Obrigatório | Regras |
|---|---|---|
| `weekDate` | sim | Qualquer dia da semana; não vale semana que ainda não começou |
| `perception` | sim | `OnTrack`, `Frozen`, `OffTrack` ou `AtRisk` |
| `finished`, `inProgress`, `nextSteps` | pelo menos um | O que foi concluído, o que está em andamento e os próximos passos. Até 4.000 caracteres cada |
| `observations` | não | Até 4.000 caracteres |

=== "Requisição"

    ```bash
    curl -s -X POST $NEXUS/api/v1/checkins \
      -H "Authorization: Bearer $KEY" -H "Content-Type: application/json" \
      -d '{
        "weekDate": "2026-11-17",
        "perception": "OnTrack",
        "finished": "Integração do pedido com o ERP.",
        "nextSteps": "Homologar com o financeiro."
      }'
    ```

=== "Resposta 201"

    O [Check-in](#check-in) criado. Quando a semana já tinha check-in, a resposta é `200` com o
    check-in atualizado.

| Resposta | |
|---|---|
| <span class="nx-status nx-status--ok">201</span> | Check-in criado para a semana |
| <span class="nx-status nx-status--ok">200</span> | Check-in da semana atualizado |
| <span class="nx-status nx-status--client">400</span> | Semana futura, percepção inválida, texto acima do limite ou nenhum dos três campos principais preenchido. A mensagem vem em `errors[""]` |

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

### Lançamento de horas

```json
{
  "id": "...",
  "userId": "...",
  "userName": "Ana Souza",
  "taskId": "9b2e4c1a-...",
  "taskCode": "TASK-12",
  "contractId": "...",
  "contractName": "Suporte 2026",
  "activityId": "...",
  "activityName": "Desenvolvimento",
  "date": "2026-11-16T00:00:00Z",
  "hours": 1.5,
  "comment": "Separação do pedido 1001",
  "source": "Manual",
  "createdAtUtc": "2026-11-16T18:00:00Z"
}
```

| Campo | Tipo | Observação |
|---|---|---|
| `taskId`, `contractId`, `activityId` | GUID ou `null` | Com o código ou o nome ao lado (`taskCode`, `contractName`, `activityName`) |
| `date` | data | O dia trabalhado, sem hora |
| `hours` | número | Até duas casas decimais |
| `source` | `Manual` `Timer` | `Timer` quando veio do cronômetro da tela |

### Check-in

```json
{
  "id": "...",
  "weekStartUtc": "2026-11-16T00:00:00Z",
  "perception": "OnTrack",
  "finished": "Integração do pedido com o ERP.",
  "inProgress": "",
  "nextSteps": "Homologar com o financeiro.",
  "observations": "",
  "authorName": "Ana Souza",
  "createdAtUtc": "2026-11-17T12:00:00Z",
  "updatedAtUtc": null
}
```

| Campo | Tipo | Observação |
|---|---|---|
| `weekStartUtc` | data | Segunda-feira da semana |
| `perception` | `OnTrack` `Frozen` `OffTrack` `AtRisk` | Na tela: No planejado, Congelado, Fora do planejado, Sob risco |
| `authorName` | texto ou `null` | Quem registrou; pela API, quem gerou a chave |

### Erro de validação (`400`) { #erro-de-validacao }

`application/problem+json` com `errors`: objeto cujo nome é o campo em camelCase (`title`,
`taskListId`, `statusId`, `assigneeId`, `startDateUtc`, `description`, `externalReference`) e o valor
é uma lista de mensagens em português. Todos os erros vêm de uma vez. Nas rotas de horas e
check-in, a regra violada vem com o nome vazio (`errors[""]`).

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
| Horas por lançamento | até 24 |
| Texto de cada campo do check-in | 4.000 caracteres |
