# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

Two audiences, weighted equally:

- **Equipes brasileiras pequenas e médias** que tocam projetos no dia a dia: times de desenvolvimento (backend, frontend, QA, gestão de projetos) e times comerciais conduzindo um funil de venda com clientes. Membros entram num workspace com papel de Owner, Admin ou Member, e o trabalho é organizar tarefas, acompanhar sprints, escrever docs e conversar sem sair da ferramenta.
- **A banca do TCC** (Senac, repositório no GitLab), que avalia o Nexus como produto entregue. Para ela, o sistema precisa se apresentar como algo em produção e utilizável, não como protótipo.

Nenhum dos dois públicos pode ser sacrificado pelo outro.

## Product Purpose

O Nexus é uma plataforma de gestão de projetos que reúne num só lugar o que uma equipe pequena ou média precisa: workspaces com spaces e listas, tarefas vistas como lista, quadro Kanban, backlog, calendário e Gantt; sprints com burndown; docs e planilhas; chat por workspace com menções; notificações e busca. Sucesso é o time usar no primeiro dia, sem treinamento, e não precisar de outra ferramenta ao lado.

## Positioning

Frente a ClickUp, Jira e Notion, o Nexus defende quatro coisas juntas:

1. **Português nativo**: vocabulário e jeito de trabalhar do time brasileiro, sem jargão em inglês.
2. **Tudo num lugar só**: tarefas, docs, planilhas e chat integrados, com o chat ligado às tarefas e documentos.
3. **Zero curva de aprendizado**: a equipe usa no primeiro dia.
4. **Suporte de quem constrói**: contato direto com o desenvolvedor (WhatsApp), sem central de atendimento.

## Operating Context

- App web servido por Blazor (.NET) em produção via IIS; tempo real via SignalR, com backplane em Redis entre instâncias.
- Uso diário dentro de workspaces: planejamento de sprint, arrastar tarefas no quadro, comentar, anexar arquivos, conversar no chat, editar docs.
- O projeto vive em dois repositórios sincronizados (GitHub para deploy, GitLab para o TCC); versão em produção exposta em `GET /version`.

## Capabilities and Constraints

- **Entidades centrais:** Workspace → Space → Lista (TaskList) → Tarefa (WorkItem), com status customizáveis e transições, prioridades, tipos, labels, campos customizados, comentários e anexos; Sprints com snapshots de burndown; DocPage (docs e planilhas); ChatMessage com menções e anexos; Notificações; log de auditoria.
- **Papéis:** Owner, Admin, Member por workspace; convites por link.
- **Conta:** Identity do ASP.NET com confirmação de e-mail, 2FA, passkeys e login externo; senha com no mínimo 10 caracteres com maiúscula, minúscula, dígito e símbolo.
- **Idioma:** interface inteiramente em pt-BR.
- **Planos (preços de referência, sujeitos a alteração):** Starter grátis, Pro R$ 29/usuário/mês, Business sob consulta.
- **Em aberto:** edição colaborativa em tempo real nos Docs (CRDT/OT, issue #32); hoje os Docs são last-write-wins.

## Brand Commitments

- Nome **Nexus** e o logo `src/Nexus.Web/wwwroot/IconNexus.png` ficam.
- A paleta de `docs/design/paleta-de-cores.md` é decisão tomada, não ponto de partida: primário `#5B5CEB` (hover `#4B4CD8`, active `#3F40BF`, soft `#EEF0FF`) e os tokens de tema claro e escuro definidos ali.
- Voz: português direto e informal ("pra", "a gente"), sem jargão em inglês, falando com o time e não com o comprador corporativo.
- Textos legais (Política de Privacidade, Termos de Uso, retenção de dados LGPD, plano de resposta a incidente) fazem parte do produto e devem ser preservados.

## Evidence on Hand

- Screenshots e imagens do produto: `src/Nexus.Web/wwwroot/Home.png`, `LoginImage.png`, `foto1Landing.png`, `foto2Landing.png`.
- Dados de demonstração locais (fictícios): workspaces "Agência Norte" e "Venda - Cliente Josapar", descritos em `docs/dados-demonstracao.md`. Josapar é cliente de exemplo, não cliente real.
- Documentação operacional em `docs/` (backup, escalabilidade horizontal, sincronização, releases, LGPD).
- **Não existem**: depoimentos, clientes pagantes, logos de clientes, métricas de uso ou benchmarks. Nenhum trabalho futuro deve inventá-los.
- A Política de Privacidade ainda é um modelo com trechos `[PREENCHER]`; não apresentar como texto jurídico final.

## Product Principles

1. **Primeiro dia sem manual.** Toda tela precisa ser entendida por quem nunca viu o Nexus; se precisa de explicação, está complexa demais.
2. **Um só lugar, sem costura aparente.** Tarefa, doc e conversa se referenciam; o usuário não deve sentir que trocou de ferramenta.
3. **Português de verdade.** Termos e textos soam como o time brasileiro fala, não como tradução.
4. **Honestidade de produto.** Mostrar só o que existe e funciona; nada de prova social, número ou recurso inventado, nem para a banca nem para clientes.
5. **Pronto para produção.** Cada entrega precisa se sustentar diante de uma equipe real usando e de uma banca avaliando.

## Accessibility & Inclusion

- WCAG 2.x nível AA obrigatório.
- Temas claro e escuro obrigatórios, ambos cumprindo AA.
