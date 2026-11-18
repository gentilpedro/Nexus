# Integrações na sua instalação

As páginas da seção [Integrações](../integracoes/index.md) descrevem a API, os assistentes de IA
(MCP), os webhooks e o feed iCal usando o endereço da Aamora (`https://nexus.aamora.com.br`). Na
sua instalação, **troque pelo seu endereço**: tudo fica debaixo do `PUBLIC_BASE_URL` que você
configurou.

## Endereços

| O quê | Onde | Página |
|---|---|---|
| API de integração | `https://nexus.suaempresa.com.br/api/v1` | [API](../integracoes/api.md) · [Referência](../integracoes/api-referencia.md) · [Receitas](../integracoes/api-receitas.md) |
| Especificação OpenAPI | `https://nexus.suaempresa.com.br/openapi/v1.json` | gerada pelo próprio app, sempre da versão instalada |
| Servidor MCP (assistentes de IA) | `https://nexus.suaempresa.com.br/mcp` | [Assistentes de IA (MCP)](../integracoes/mcp.md) |
| Webhooks de saída | cadastrados na tela; o Nexus chama a URL do seu sistema | [Webhooks de saída](../integracoes/webhooks.md) |
| Feed iCal | link pessoal gerado na tela, debaixo do seu endereço | [Calendário](../guia/calendario.md) |
| Health checks | `/health/live`, `/health/ready` | sem autenticação, para o seu monitoramento |
| Versão e licença | `/version` | `{"version": "1.24.0", "license": "Valid", ...}` |

`PUBLIC_BASE_URL` precisa ser o endereço que as pessoas e os sistemas usam de fato (com `https`):
ele entra nos links de e-mail, no campo `url` das respostas da API, no feed iCal e nas
notificações.

## Planos e chaves

Numa instalação Business `Plans__EnforceLimits` fica `false`: API, MCP, webhooks, sprints, Gantt
e tudo mais ficam liberados em **todos** os workspaces, sem assinatura nem Stripe. As chaves de
API continuam por workspace, em **Configurações do workspace → Integrações** (Owner ou Admin),
com escopo de leitura ou leitura e escrita; os webhooks ficam na mesma tela. As regras de limite
(120 chamadas por minuto por chave, 20 chaves por workspace) valem igual.

## Assistentes de IA (MCP)

A configuração é a da página de [MCP](../integracoes/mcp.md), com o seu endereço:

```json
{
  "mcpServers": {
    "nexus": {
      "url": "https://nexus.suaempresa.com.br/mcp",
      "headers": { "Authorization": "Bearer nxk_sua_chave" }
    }
  }
}
```

O cliente MCP (Claude Desktop, Claude Code, Copilot...) roda na máquina da pessoa e precisa
**alcançar o endereço**: se o Nexus só responde na rede interna, vale pela VPN; se o assistente
roda na nuvem do fornecedor, o endereço precisa estar publicado.

## Webhooks de saída

O Nexus só chama URLs **`https` públicas** e recusa `http`, `localhost` e endereços de rede
interna (10.x, 192.168.x), para um webhook não virar uma porta para dentro da sua rede. Um
receptor dentro da empresa precisa de nome DNS e certificado válido. Para um ambiente de
laboratório, `Webhooks__AllowInsecureUrls=true` libera `http` e endereços internos; nunca use
isso em produção.

## Monitoramento

- `/health/live` responde `Healthy` enquanto o processo está de pé; `/health/ready` também confere
  o banco. Aponte o seu monitor para o segundo.
- `/version` devolve versão, commit, data do build e o estado da licença (`Valid`,
  `ExpiringSoon`, `Expired`, `Trial`, `Invalid`), sem dados do cliente.
- O log sai no stdout do container: `docker compose logs -f web`.

## O que não se aplica

- **Stripe** (cobrança do plano Pro na nuvem): fica desligado; as variáveis `Stripe__*` ficam
  vazias e `/stripe/webhook` responde `404`.
- **GitHub ↔ GitLab**: é o fluxo de desenvolvimento do próprio Nexus, não da sua instalação.
- `Docs__SiteUrl` libera o site de documentação da Aamora a ler a especificação OpenAPI da
  instância (só `GET`). Já vem na imagem; não precisa mudar.
