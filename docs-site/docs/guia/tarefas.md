# Tarefas, quadro e backlog

## A tarefa

Toda tarefa tem um **código** sequencial no workspace (`TASK-12`), um título, uma descrição e:

| Campo | Valores |
|---|---|
| Tipo | Tarefa, Bug, História, Épico |
| Prioridade | Mais baixa, Baixa, Média, Alta, Mais alta |
| Status | As colunas da lista (ex.: A fazer, Fazendo, Feito) |
| Responsável | Um membro do workspace |
| Datas | Início e prazo |
| Épico | Uma tarefa do tipo Épico da mesma lista, para agrupar |
| Sprint | A sprint em que está planejada (planos Pro e Business) |
| Etiquetas | Rótulos coloridos definidos na lista |
| Campos personalizados | Os que a lista definiu (texto, número, data, lista de opções…) |

Ao abrir uma tarefa, o painel lateral mostra também os **comentários** e os **anexos**.

!!! tip "Responsável recebe aviso"
    Quem é atribuído a uma tarefa recebe uma notificação no Nexus e um e-mail, com o link para a
    lista. Vale para atribuições feitas pela tela e pela API.

## Visões

- **Lista**: tabela com ordenação e filtros; boa para revisar muitas tarefas.
- **Quadro**: colunas por status, arrastar e soltar para mudar. Respeita o fluxo da lista.
- **Backlog**: o que ainda não está em sprint; dali as tarefas entram no planejamento.
- **Calendário** e **Gantt**: tarefas no tempo, pelas datas de início e prazo. Veja
  [Sprints, Gantt e calendário](planejamento.md).

## Fluxo de status

Cada lista pode restringir as transições em **Configurações → Fluxo de trabalho**: por exemplo,
só sair de "Em revisão" para "Feito" ou de volta para "Fazendo". Sem regras configuradas, qualquer
mudança é permitida. As regras valem para o quadro, para o painel da tarefa e para a API.

## Campos personalizados e etiquetas

Em cada lista, Admin e Owner definem campos extras (obrigatórios ou não) e etiquetas. Os campos
aparecem no painel da tarefa; as etiquetas, nos cartões.

## Busca

A busca global (ícone de lupa) encontra tarefas pelo código, título e descrição, docs pelo
conteúdo e mensagens do chat, em todos os seus workspaces. Não diferencia acentos.
