# Webhooks de Saída

!!! abstract ""
    O Nexus chama a URL do seu sistema quando uma tarefa é criada, muda, troca de status ou de
    responsável, ou recebe um comentário. É o caminho de volta da [API de Integração](api.md):
    o ERP cria a tarefa pela API e fica sabendo pelo webhook quando a equipe a conclui, sem perguntar
    de tempos em tempos. Issue [#75](https://gitlab.com/senac-projetos-de-desenvolvimento/2025-pedro-gentil/tcc_2/nexus/-/issues/75).

## Em um minuto

| | |
|---|---|
| Onde cadastrar | Configurações do workspace → Integrações → Webhooks de saída (Owner ou Admin) |
| O que informar | Nome, URL `https` pública e os eventos que interessam |
| Segredo | Gerado pelo Nexus (`whsec_...`), mostrado uma vez; assina cada chamada |
| Entrega | `POST` com JSON; `2xx` conta como entregue |
| Tentativas | 1, 5 e 30 minutos depois de cada falha; a quarta falha encerra |
| Proteção | 50 falhas definitivas seguidas pausam o webhook e avisam os administradores |
| Histórico | Últimas 20 entregas na tela, com "reenviar"; guardadas por 30 dias |
| Planos | Os mesmos da API: Pro e Business |

## Eventos

| `type` | Quando | Traz em `previous` |
|---|---|---|
| `task.created` | Tarefa criada (tela, API ou MCP) | nada |
| `task.status_changed` | Mudou de status (painel da tarefa, arrastar no quadro, API) | `status` anterior |
| `task.assignee_changed` | Mudou o responsável | `assignee` anterior (ou `null`) |
| `task.updated` | Mudou título, descrição, tipo, prioridade, datas ou referência externa | só os campos que mudaram, com o valor anterior |
| `comment.created` | Comentário novo na tarefa | nada; o comentário vem em `comment` |
| `ping` | Botão "Enviar teste" | nada |

Uma mesma edição pode gerar mais de um evento: mover para "Feito" e trocar o responsável na
mesma tela gera `task.status_changed` e `task.assignee_changed`, cada um com o próprio `id`.

## Corpo

```json
{
  "id": "0d4b...",
  "type": "task.status_changed",
  "occurredAtUtc": "2026-11-17T14:03:11Z",
  "workspaceId": "7c1e...",
  "actor": { "id": "...", "name": "Ana Souza", "email": "ana@acme.com.br" },
  "task": {
    "id": "9b2e...",
    "code": "TASK-12",
    "title": "Separar pedido 1001",
    "status": { "id": "...", "name": "Feito", "category": "Done", "isDefault": false },
    "assignee": { "id": "...", "name": "Ana Souza", "email": "ana@acme.com.br" },
    "externalReference": "PEDIDO-1001",
    "url": "https://nexus.aamora.com.br/lists/4f1c.../list"
  },
  "previous": { "status": { "id": "...", "name": "Fazendo", "category": "InProgress" } },
  "comment": null
}
```

- `task` é a mesma tarefa da [Referência da API](api-referencia.md#tarefa). Use
  `externalReference` para achar o registro no seu sistema.
- `actor` é quem fez a mudança. Vem `null` quando a mudança veio de um arrasto no quadro, e com o
  nome da integração quando veio pela API ("Integração \"ERP\"").
- `id` é o id do evento. Se o Nexus reenviar (tentativa ou "reenviar" na tela), o `id` é o mesmo:
  use-o para não processar duas vezes.

## Cabeçalhos

| Cabeçalho | Conteúdo |
|---|---|
| `X-Nexus-Event` | O `type` do evento |
| `X-Nexus-Delivery` | Id desta entrega (muda a cada tentativa; o `id` do corpo não) |
| `X-Nexus-Signature` | `sha256=<HMAC-SHA256 do corpo, em hexadecimal, com o segredo>` |
| `User-Agent` | `Nexus-Webhooks/1.0` |
| `Content-Type` | `application/json; charset=utf-8` |

## Validar a assinatura

Calcule o HMAC-SHA256 do **corpo bruto** (os bytes exatamente como chegaram, antes de qualquer
parse) com o segredo, e compare com o cabeçalho em tempo constante. Rejeite com `401` quando não
bater.

### C# (ASP.NET Core)

```csharp
app.MapPost("/nexus/webhook", async (HttpRequest request) =>
{
    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync();

    var secret = Environment.GetEnvironmentVariable("NEXUS_WEBHOOK_SECRET")!;
    var expected = "sha256=" + Convert.ToHexStringLower(
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body)));
    var received = request.Headers["X-Nexus-Signature"].ToString();
    if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(received)))
    {
        return Results.Unauthorized();
    }

    var evt = JsonSerializer.Deserialize<JsonElement>(body);
    if (evt.GetProperty("type").GetString() == "task.status_changed"
        && evt.GetProperty("task").GetProperty("status").GetProperty("category").GetString() == "Done")
    {
        var os = evt.GetProperty("task").GetProperty("externalReference").GetString();
        // fechar a OS no ERP
    }

    return Results.NoContent();   // responda rápido; faça o trabalho pesado numa fila
});
```

### Python (Flask)

```python
import hmac, hashlib, os
from flask import Flask, request, abort

app = Flask(__name__)
SECRET = os.environ["NEXUS_WEBHOOK_SECRET"].encode()

@app.post("/nexus/webhook")
def nexus_webhook():
    body = request.get_data()                      # bytes crus, antes do JSON
    expected = "sha256=" + hmac.new(SECRET, body, hashlib.sha256).hexdigest()
    if not hmac.compare_digest(expected, request.headers.get("X-Nexus-Signature", "")):
        abort(401)

    event = request.get_json()
    if event["type"] == "task.status_changed" and event["task"]["status"]["category"] == "Done":
        erp.fechar_os(event["task"]["externalReference"])
    return "", 204
```

## Boas práticas do receptor

- **Responda em menos de 10 segundos**, de preferência `204` na hora, e processe depois. Passou de
  10 s, o Nexus conta como falha e tenta de novo.
- **Trate como "pelo menos uma vez".** Uma tentativa pode ter chegado e a resposta se perdido;
  guarde o `id` do evento e ignore repetidos.
- **Não confie na ordem.** Dois eventos próximos podem chegar trocados; use `occurredAtUtc`
  quando a ordem importar.
- **URL pública e `https`.** O Nexus recusa `http`, `localhost` e endereços de rede interna. Em
  desenvolvimento, o [webhook.site](https://webhook.site) serve para ver o que chega; para testar o
  seu próprio código local, use um túnel (ngrok, Cloudflare Tunnel).
- **Trocar o segredo**: exclua o webhook e cadastre de novo. O novo segredo aparece uma vez.

## Quando algo dá errado

| Na tela | O que quer dizer |
|---|---|
| "Tentando de novo (2 de 4)" | Falhou duas vezes; a próxima tentativa já está agendada |
| "Falhou (4 tentativas)" | Desistiu desta entrega; "Reenviar" cria uma nova |
| "Pausado · 50 falhas seguidas" | O Nexus parou de chamar; conserte a URL e clique em "Reativar" (zera a contagem) |
| Resposta "Sem resposta em 10 segundos" | O seu endpoint demorou; responda antes e processe depois |
