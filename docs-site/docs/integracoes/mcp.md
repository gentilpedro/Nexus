# Assistentes de IA (MCP)

!!! abstract ""
    O Nexus expõe um servidor **MCP** (Model Context Protocol) em `https://nexus.aamora.com.br/mcp`.
    Com ele, o Claude, o ChatGPT, o Copilot ou qualquer cliente MCP opera as tarefas de um workspace:
    "cria uma tarefa na lista Pedidos com prazo sexta", "o que está atrasado?", "move a TASK-12 para
    Feito". Issue #67.

## Em um minuto

| | |
|---|---|
| Endereço | `https://nexus.aamora.com.br/mcp` (transporte Streamable HTTP, sem sessão) |
| Autenticação | A mesma chave da [API de Integração](api.md): `Authorization: Bearer nxk_...` |
| Escopo | O workspace da chave. Chave só de leitura: só as ferramentas de consulta |
| Limite | O mesmo da API: 120 chamadas por minuto por chave, contando ferramentas e rotas juntas |
| Planos | Pro e Business |

Tarefas criadas pelo assistente ficam registradas em nome de quem gerou a chave, e a notificação de
atribuição diz `Assistente via "<nome da chave>" atribuiu TASK-n ...`. Os [webhooks de saída](webhooks.md)
disparam normalmente.

## Ferramentas

| Ferramenta | Faz | Precisa de escrita |
|---|---|---|
| `get_workspace` | Nome, slug e situação do workspace da chave | não |
| `list_lists` | Spaces, listas e status (os ids que `create_task` e `update_task` usam) | não |
| `list_members` | Membros, com o id que vai em `assigneeId` | não |
| `list_tasks` | Tarefas paginadas, filtros por lista, status, responsável e `updatedSince` | não |
| `get_task` | Uma tarefa pelo id ou pelo código (`TASK-12`) | não |
| `create_task` | Cria uma tarefa; `externalReference` evita duplicar | sim |
| `update_task` | Atualiza campos; mudança de status respeita o fluxo da lista | sim |

As regras são as da API: lista do workspace, status da lista, responsável membro, datas em ordem,
transição permitida. Um erro vira uma mensagem para o modelo ("assigneeId: O responsável precisa
ser membro do workspace."), que costuma corrigir sozinho.

## Conectar o Claude Desktop

Em *Configurações → Conectores → Adicionar conector personalizado* (ou no arquivo de
configuração, conforme a versão):

```json
{
  "mcpServers": {
    "nexus": {
      "url": "https://nexus.aamora.com.br/mcp",
      "headers": { "Authorization": "Bearer nxk_sua_chave" }
    }
  }
}
```

## Conectar o Claude Code

```bash
claude mcp add --transport http nexus https://nexus.aamora.com.br/mcp \
  --header "Authorization: Bearer nxk_sua_chave"
```

Depois, no chat: *"usa o nexus para listar as tarefas atrasadas da lista Pedidos"*.

## Outros clientes

Qualquer cliente que fale Streamable HTTP funciona: informe a URL e o cabeçalho `Authorization`.
Como o servidor é sem sessão, cada chamada é independente; não há `Mcp-Session-Id` para guardar.

## Boas práticas

- **Uma chave por assistente**, com nome ("Claude da Ana", "Copilot do suporte"), para o log de
  auditoria e as notificações dizerem quem fez o quê. Revogue quando a pessoa sair.
- **Só leitura para quem só consulta.** Um assistente que faz relatórios não precisa criar tarefa.
- **Peça `externalReference`** quando o assistente criar tarefas a partir de outro sistema, para
  reenvios não duplicarem.
- O assistente vê o workspace inteiro da chave, como qualquer membro. Não dê a ele uma chave de um
  workspace que a pessoa não deveria ver.
