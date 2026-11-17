# Check-in Semanal

!!! abstract ""
    Uma vez por semana, um resumo por workspace que a gestão e o cliente leem sem abrir o quadro:
    uma percepção entre quatro e quatro textos curtos. Issue [#79](https://gitlab.com/senac-projetos-de-desenvolvimento/2025-pedro-gentil/tcc_2/nexus/-/issues/79).

## Em um minuto

| | |
|---|---|
| Onde | Aba **Check-in** do workspace |
| Quem escreve | Owner e Admin |
| Quem lê | Todos os membros (um cliente convidado como Member acompanha) |
| Quando | Um por semana (segunda a domingo); registrar de novo edita o da semana |
| Percepção | ✅ No planejado · ❄️ Congelado · ⚡ Fora do planejado · ☣️ Sob risco |
| Campos | Concluído · Em andamento · Próximos passos · Observações |
| Onde aparece | Histórico na aba; o selo da semana mais recente no cabeçalho do workspace |

## Rascunho a partir do quadro

Ao abrir a aba numa semana ainda sem check-in, o Nexus preenche "Concluído" com as tarefas que
foram para **Feito** na semana e "Em andamento" com as que estão **em andamento**, com código,
título e responsável. É um ponto de partida para editar, não um texto para postar sem ler.

## API

```bash
curl -H "Authorization: Bearer nxk_..." https://nexus.aamora.com.br/api/v1/checkins

curl -X POST -H "Authorization: Bearer nxk_..." -H "Content-Type: application/json" \
  https://nexus.aamora.com.br/api/v1/checkins \
  -d '{ "weekDate": "2026-11-17", "perception": "OnTrack", "finished": "API no ar", "inProgress": "Webhooks", "nextSteps": "Horas", "observations": "" }'
```

`POST` na semana que já tem check-in responde `200` com o atualizado; numa semana nova, `201`.

## Com o assistente de IA

Pelo [servidor MCP](../integracoes/mcp.md), três ferramentas: `list_checkins`, `draft_checkin`
(o rascunho do quadro) e `post_checkin`. Um fluxo que funciona bem: *"faz o rascunho do check-in
desta semana no nexus, me mostra, e só posta depois que eu aprovar"*.

## Por baixo

- `WorkspaceCheckin`: `WeekStartUtc` (segunda-feira, meia-noite UTC), `Perception`, quatro textos
  de até 4000 caracteres, autor. Índice único por workspace e semana.
- `CheckinService.SaveAsync` é o único ponto de escrita (tela, API e MCP), e é ele que decide
  "criar ou editar". Semana futura é recusada.
