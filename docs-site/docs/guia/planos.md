# Planos

| | Starter | Pro | Business |
|---|---|---|---|
| Preço | Grátis | R$ 149/mês com até 10 pessoas, R$ 14,90 por pessoa a mais | A partir de R$ 5.900/ano (faixas abaixo) |
| Membros por workspace | até 5 | sem limite | sem limite |
| Workspaces próprios | 1 | sem limite | sem limite |
| Quadro, lista, backlog, calendário | sim | sim | sim |
| Sprints, burndown e Gantt | não | sim | sim |
| Docs, planilhas e chat | sim | sim | sim |
| API de integração, webhooks e assistentes de IA (MCP) | não | sim | sim |
| Horas, contratos e check-in semanal | sim | sim | sim |
| Suporte prioritário | não | sim | sim |
| Instalação na sua rede (self-hosted) | não | não | sim |
| Login pelo Active Directory | não | não | sim |

O plano é do **workspace**, não da conta: dois workspaces seus podem estar em planos diferentes.

## Assinar e gerenciar

Em **Configurações → Plano e cobrança** (só o Owner). O pagamento é pelo Stripe, com cartão; o
Nexus não vê os dados do cartão. Pelo mesmo lugar dá para trocar o cartão, baixar faturas e
cancelar. O cancelamento vale no fim do período pago: até lá tudo continua funcionando, e depois
o workspace volta ao Starter sem perder nada.

## Business self-hosted

O Business é instalado na infraestrutura do cliente, como imagem Docker, com assinatura anual
pré-paga por faixa de **usuários ativos** (quem entrou no Nexus nos últimos 30 dias).

| Usuários ativos | Por ano | Equivale por pessoa/mês |
|---|---|---|
| Até 25 | R$ 5.900 | R$ 19,70 |
| Até 50 | R$ 9.900 | R$ 16,50 |
| Até 100 | R$ 16.900 | R$ 14,10 |
| Até 250 | R$ 34.900 | R$ 11,60 |
| Ilimitado | R$ 59.900 | |

- Passou da faixa: a instalação continua; só contas novas esperam a faixa seguinte. A troca de
  faixa no meio do ano cobra a diferença proporcional.
- Licença vencida continua funcionando; o que para são as atualizações (ver
  [Licença](../self-hosted/licenca.md)).
- Implantação (Active Directory, migração de dados) é um serviço à parte, por hora.

Veja [Self-hosted](../self-hosted/index.md) para instalar.
