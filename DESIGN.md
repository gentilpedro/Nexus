---
name: Nexus
description: Gestão de projetos em português para times brasileiros pequenos e médios; tarefas, docs e conversa num lugar só.
colors:
  primary: "#5B5CEB"
  primary-hover: "#4B4CD8"
  primary-active: "#3F40BF"
  primary-soft: "#EEF0FF"
  primary-soft-dark: "#2A3060"
  accent-text: "#4B4CD8"
  accent-text-dark: "#A5A8FF"
  focus: "#5B5CEB"
  focus-dark: "#8B8FFF"
  bg: "#F7F8FC"
  bg-dark: "#111827"
  surface: "#FFFFFF"
  surface-dark: "#1A2235"
  surface-raised-dark: "#202A3C"
  surface-sunken: "#F1F3F8"
  surface-sunken-dark: "#161E2F"
  hover-dark: "#293548"
  selected-dark: "#323F63"
  text: "#111827"
  text-dark: "#F9FAFB"
  text-secondary: "#4B5563"
  text-secondary-dark: "#D1D5DB"
  text-muted: "#6B7280"
  text-muted-dark: "#9CA3AF"
  text-disabled: "#9CA3AF"
  text-on-accent: "#FFFFFF"
  border: "#E5E7EB"
  border-dark: "#334155"
  border-strong: "#D1D5DB"
  border-strong-dark: "#475569"
  divider: "#EEF2F7"
  success: "#22C55E"
  success-text: "#15803D"
  success-text-dark: "#86EFAC"
  warning: "#F59E0B"
  warning-text: "#92400E"
  warning-text-dark: "#FCD34D"
  danger: "#EF4444"
  danger-fill: "#DC2626"
  danger-fill-hover: "#B91C1C"
  danger-text: "#B91C1C"
  danger-text-dark: "#FCA5A5"
  info: "#3B82F6"
  info-text: "#1D4ED8"
  info-text-dark: "#93C5FD"
  status-todo: "#94A3B8"
  status-review: "#A855F7"
  priority-low: "#10B981"
  priority-medium: "#FBBF24"
  priority-high: "#F97316"
  workspace-blue: "#2563EB"
  workspace-petroleum: "#0F766E"
  workspace-green: "#15803D"
  workspace-orange: "#C2410C"
  workspace-magenta: "#BE185D"
  workspace-purple: "#7C3AED"
typography:
  display:
    fontFamily: "Inter, system-ui, -apple-system, Segoe UI, Roboto, Helvetica Neue, Arial, sans-serif"
    fontSize: "clamp(2.125rem, 1.2rem + 2.6vw, 3.375rem)"
    fontWeight: 700
    lineHeight: 1.08
    letterSpacing: "-0.03em"
  headline:
    fontFamily: "Inter, system-ui, -apple-system, Segoe UI, Roboto, Helvetica Neue, Arial, sans-serif"
    fontSize: "clamp(1.5rem, 1.15rem + 1.2vw, 2.25rem)"
    fontWeight: 700
    lineHeight: 1.15
    letterSpacing: "-0.025em"
  page-title:
    fontFamily: "Inter, system-ui, -apple-system, Segoe UI, Roboto, Helvetica Neue, Arial, sans-serif"
    fontSize: "1.5rem"
    fontWeight: 600
    lineHeight: 1.25
    letterSpacing: "-0.018em"
  title:
    fontFamily: "Inter, system-ui, -apple-system, Segoe UI, Roboto, Helvetica Neue, Arial, sans-serif"
    fontSize: "1.25rem"
    fontWeight: 600
    lineHeight: 1.25
    letterSpacing: "-0.014em"
  title-sm:
    fontFamily: "Inter, system-ui, -apple-system, Segoe UI, Roboto, Helvetica Neue, Arial, sans-serif"
    fontSize: "0.9375rem"
    fontWeight: 600
    lineHeight: 1.25
    letterSpacing: "-0.011em"
  body:
    fontFamily: "Inter, system-ui, -apple-system, Segoe UI, Roboto, Helvetica Neue, Arial, sans-serif"
    fontSize: "0.875rem"
    fontWeight: 400
    lineHeight: 1.55
    fontFeature: "\"cv11\", \"ss01\""
  body-lg:
    fontFamily: "Inter, system-ui, -apple-system, Segoe UI, Roboto, Helvetica Neue, Arial, sans-serif"
    fontSize: "1rem"
    fontWeight: 400
    lineHeight: 1.55
  label:
    fontFamily: "Inter, system-ui, -apple-system, Segoe UI, Roboto, Helvetica Neue, Arial, sans-serif"
    fontSize: "0.8125rem"
    fontWeight: 500
    lineHeight: 1.4
  caption:
    fontFamily: "Inter, system-ui, -apple-system, Segoe UI, Roboto, Helvetica Neue, Arial, sans-serif"
    fontSize: "0.75rem"
    fontWeight: 500
    lineHeight: 1
  mono:
    fontFamily: "ui-monospace, Cascadia Mono, SF Mono, Menlo, Consolas, monospace"
    fontSize: "0.92em"
rounded:
  sm: "6px"
  md: "8px"
  lg: "12px"
  pill: "999px"
spacing:
  "0-5": "2px"
  "1": "4px"
  "1-5": "6px"
  "2": "8px"
  "2-5": "10px"
  "3": "12px"
  "4": "16px"
  "5": "20px"
  "6": "24px"
  "8": "32px"
  "10": "40px"
  "12": "48px"
  "16": "64px"
components:
  button-primary:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.text-on-accent}"
    typography: "{typography.body}"
    rounded: "{rounded.md}"
    padding: "0 16px"
    height: "36px"
  button-primary-hover:
    backgroundColor: "{colors.primary-hover}"
  button-primary-active:
    backgroundColor: "{colors.primary-active}"
  button-secondary:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text}"
    rounded: "{rounded.md}"
    padding: "0 16px"
    height: "36px"
  button-secondary-hover:
    backgroundColor: "{colors.surface-sunken}"
  button-ghost:
    textColor: "{colors.text-secondary}"
    rounded: "{rounded.md}"
    padding: "0 16px"
    height: "36px"
  button-ghost-hover:
    backgroundColor: "{colors.surface-sunken}"
    textColor: "{colors.text}"
  button-danger:
    backgroundColor: "{colors.danger-fill}"
    textColor: "{colors.text-on-accent}"
    rounded: "{rounded.md}"
    padding: "0 16px"
    height: "36px"
  button-danger-hover:
    backgroundColor: "{colors.danger-fill-hover}"
  button-sm:
    typography: "{typography.label}"
    padding: "0 12px"
    height: "28px"
  button-lg:
    typography: "{typography.body-lg}"
    padding: "0 20px"
    height: "40px"
  icon-button:
    textColor: "{colors.text-muted}"
    rounded: "{rounded.md}"
    size: "36px"
  input:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text}"
    typography: "{typography.body}"
    rounded: "{rounded.md}"
    padding: "0 12px"
    height: "36px"
  card:
    backgroundColor: "{colors.surface}"
    rounded: "{rounded.lg}"
    padding: "20px"
  task-card:
    backgroundColor: "{colors.surface}"
    rounded: "{rounded.md}"
    padding: "12px"
  badge:
    backgroundColor: "{colors.surface-sunken}"
    textColor: "{colors.text-secondary}"
    typography: "{typography.caption}"
    rounded: "{rounded.sm}"
    padding: "0 8px"
    height: "20px"
  badge-accent:
    backgroundColor: "{colors.primary-soft}"
    textColor: "{colors.accent-text}"
  nav-item:
    textColor: "{colors.text-secondary}"
    typography: "{typography.body}"
    rounded: "{rounded.sm}"
    padding: "0 8px"
    height: "32px"
  nav-item-active:
    backgroundColor: "{colors.primary-soft}"
    textColor: "{colors.accent-text}"
  tab:
    textColor: "{colors.text-muted}"
    typography: "{typography.body}"
    padding: "0 12px"
    height: "40px"
  tab-active:
    textColor: "{colors.text}"
  dialog:
    backgroundColor: "{colors.surface}"
    rounded: "{rounded.lg}"
    width: "28rem"
  avatar:
    textColor: "{colors.text-on-accent}"
    size: "32px"
---

# Design System: Nexus

## Overview

**Creative North Star: "A Mesa de Trabalho Arrumada"**

O Nexus é uma ferramenta de trabalho diário, e o sistema visual se comporta como uma mesa bem arrumada: fundo levemente frio, superfícies brancas delimitadas por bordas finas, um único índigo que marca onde está a ação e onde você está. A densidade é de produto (corpo em 14px, controles de 36px, linhas de 32px na navegação), pensada para quem passa o dia arrastando tarefas, lendo docs e conversando sem trocar de ferramenta.

Um só sistema serve dois modos. No app (modo Operate) a escala tipográfica é fixa em rem, os títulos pesam 600 e a cor aparece só como informação: status, prioridade, tipo, workspace. Na landing pública (modo Persuade) o mesmo mundo ganha títulos fluidos em peso 700, respiros de 64px entre seções e uma peça assinatura, a janela do produto, que mostra o próprio Nexus funcionando em vez de ilustração. Nada na landing usa cor, fonte ou forma que o app não tenha.

Os dois temas, claro e escuro, são obrigatórios e cumprem WCAG AA. Cada cor é declarada uma única vez com `light-dark()` em `tokens.css`; o tema vem do sistema operacional ou do `data-theme` gravado quando a pessoa escolhe. A paleta de `paletadecor.md` é compromisso de marca; os únicos afastamentos dela existem por contraste e estão registrados em Colors.

**Key Characteristics:**
- Índigo #5B5CEB como acento único, em ações primárias, item ativo e foco.
- Superfícies planas com borda de 1px; sombra só para o que flutua ou se arrasta.
- Inter auto-hospedada, quatro pesos (400/500/600/700), escala fixa no app e fluida só nos títulos da landing.
- Cantos 6/8/12px por tamanho do objeto; pílula só para contadores e marcadores.
- Tema claro e escuro por `light-dark()`, ambos em AA.
- Ícones de traço 2px em grade de 24px, de um conjunto único.

## Colors

Neutros frios de base cinza-azulada, um índigo de marca e um conjunto semântico que só aparece quando carrega significado. Os valores do frontmatter sem sufixo são do tema claro; os com `-dark` são o lado escuro do mesmo token `light-dark()`.

### Primary
- **Índigo Nexus** (primary): botão primário, sublinhado da aba ativa, marcador do plano em destaque, série 1 dos gráficos, cor padrão de workspace. Escurece para **Índigo Pressionado** no hover (primary-hover) e **Índigo Fundo** no clique (primary-active).
- **Índigo Névoa** (primary-soft / primary-soft-dark): fundo do item selecionado na navegação, contadores em aba ativa, ícone de diálogo neutro, badge de acento.
- **Índigo de Leitura** (accent-text / accent-text-dark): todo texto e ícone índigo sobre superfície ou fundo tingido: links, item ativo da sidebar, ícones de lista na landing. **Desvio do paletadecor por contraste:** o paletadecor usa #5B5CEB para texto ativo e #6E72FF no escuro; o build usa este token porque #5B5CEB sobre #EEF0FF e #6E72FF sobre as superfícies escuras não chegam a 4,5:1.
- **Anel de Foco** (focus / focus-dark): contorno de 2px com afastamento de 2px em todo foco de teclado; nos campos vira borda mais halo de 3px a 24%.

### Secondary
- **Cores de workspace** (workspace-blue, workspace-petroleum, workspace-green, workspace-orange, workspace-magenta, workspace-purple, mais o Índigo Nexus): as sete opções oferecidas ao criar um workspace, usadas no avatar quadrado com a inicial em branco. **Desvio do paletadecor por contraste:** são tons escurecidos (Tailwind 600/700) em vez dos tons de gráfico do paletadecor, porque todas precisam de 4,5:1 com a inicial branca. Cores antigas gravadas continuam exibidas, mas não são oferecidas.

### Tertiary
- **Semânticas** (success, warning, danger, info): preenchimento de ícone, bolinha e barra. Cada uma tem par de texto (`-text`), fundo suave e borda para alertas e badges; texto semântico sempre usa o par `-text`, nunca o tom de preenchimento.
- **Vermelho de Ação** (danger-fill, hover danger-fill-hover): fundo do botão de perigo e do contador de não lidas. **Desvio do paletadecor por contraste:** o paletadecor define o botão de perigo em #EF4444; branco sobre #EF4444 fica em 3,8:1, então o build usa #DC2626.
- **Fluxo e prioridade** (status-todo, info, status-review, success; priority-low, priority-medium, priority-high, danger-fill para urgente): bolinhas de status e barras de prioridade, conforme o paletadecor. Nos badges preenchidos o texto usa os pares escuros de cada família.

### Neutral
- **Papel Frio** (bg / bg-dark): fundo da aplicação e do painel da janela de produto.
- **Folha** (surface, surface-dark, surface-raised-dark): topbar, sidebar, cards, menus, diálogos. No claro superfície e superfície elevada são o mesmo branco; no escuro a elevada sobe um degrau (#202A3C).
- **Bandeja** (surface-sunken / surface-sunken-dark): colunas do quadro, trilhas, campos desabilitados, fundo de badges neutros e do segmentado.
- **Tinta** (text, text-secondary, text-muted, text-disabled e pares escuros): quatro níveis; o muted é o menor que ainda passa AA para texto.
- **Traço** (border, border-strong, divider e pares escuros): borda de card, borda de controle (mais forte), divisória entre linhas (mais fraca).

### Named Rules
**A Regra do Acento Único.** O índigo é o único acento. Semânticas, status e prioridade são informação, não decoração; nenhuma segunda cor de marca entra em botões, fundos de seção ou títulos.

**A Regra do Texto de Acento.** Texto índigo sempre usa accent-text. #5B5CEB é preenchimento (botão, sublinhado, marcador), nunca cor de texto sobre fundo tingido ou escuro.

**A Regra da Cor com Nome.** Status é bolinha mais nome; prioridade é barras mais rótulo. A cor nunca é o único portador da informação.

## Typography

**Display Font:** Inter (com system-ui, -apple-system, Segoe UI, Roboto, Helvetica Neue, Arial)
**Body Font:** Inter (mesma pilha)
**Label/Mono Font:** ui-monospace, Cascadia Mono, SF Mono, Menlo, Consolas, só para código

**Character:** Uma família só, auto-hospedada (a CSP aceita fontes apenas do próprio domínio), com as alternativas `cv11` e `ss01` ligadas. A hierarquia vem de tamanho e peso, com tracking negativo crescendo conforme o título aumenta.

### Hierarchy
- **Display** (700, fluido de 34px a 54px, 1.08): só o título principal da landing.
- **Headline** (700, fluido de 24px a 36px, 1.15): títulos de seção da landing e o título da faixa de fechamento.
- **Page title** (600, 24px, 1.25): h1 de toda tela do app, via cabeçalho de página.
- **Title** (600, 20px, 1.25): h2 do app; também os itens de diferencial da landing.
- **Title small** (600, 15px, 1.25): título de card, de estado vazio e de seção dentro de card.
- **Body** (400, 14px, 1.55): corpo de toda a interface. Descrições de página limitadas a 42rem.
- **Body large** (400, 16px, 1.55): prosa longa (docs, descrições) e parágrafos da landing (lá com 1.6 e até 36rem).
- **Label** (500, 13px): texto auxiliar, rótulos, abas da janela de produto, botão pequeno.
- **Caption** (500, 12px): metadados, contadores, badges. Contadores numéricos usam algarismos tabulares.
- **Número de destaque** (700, 32px): preço nos planos e métricas.

### Named Rules
**A Regra da Escala Fixa.** No app os tamanhos são fixos em rem; um h1 não muda com a largura da janela. `clamp()` existe só nos títulos da landing.

**A Regra dos Quatro Pesos.** Só 400, 500, 600 e 700. Títulos do app em 600, títulos da landing e wordmark em 700, controles e rótulos em 500.

## Layout

Base de espaçamento de 4px (tokens de 2px a 64px). O app é uma topbar fixa de 56px, uma sidebar de 15.5rem (árvore workspace, space e lista, com linhas de 32px) e a área de conteúdo. O conteúdo vive em containers centralizados: estreito de 48rem por padrão, largo de 72rem para tabelas e quadros, e total para o Kanban e o Gantt, com respiro de 24px nas laterais e 48px no pé. Cabeçalho de página sempre igual: trilha, título, descrição e ação principal à direita.

A landing usa largura máxima de 74rem, hero em grade 6/13 e 7/13 (texto à esquerda, janela do produto à direita), seções com 64px de respiro vertical e a mesma proporção 5/7 na seção de diferenciais, com a introdução fixa ao rolar.

Breakpoints do sistema inteiro: **1024px** (a sidebar vira gaveta; na landing, grades 5/7 viram uma coluna e os planos empilham em até 28rem) e **640px** (celular: margens de 16px, botões da landing em largura total, toasts ocupam a largura, rótulos de ícone viram só leitor de tela). Em ponteiro grosso, todo alvo de toque sobe para 44px.

## Elevation & Depth

Sistema híbrido, plano por padrão. Superfícies se separam por tom (papel, folha, bandeja) e por borda de 1px; sombra entra só em três situações: controle que se destaca levemente do fundo, objeto em hover que pode ser aberto ou arrastado, e camada que flutua sobre a página. As cores de sombra são `light-dark()` e ficam bem mais densas no escuro. A topbar usa fundo translúcido a 88% com desfoque de 10px.

### Shadow Vocabulary
- **Pouso** (`box-shadow: 0 1px 2px var(--shadow-color)`): botão secundário, card de tarefa em repouso, aba ativa do segmentado.
- **Levantar** (`box-shadow: 0 4px 12px -2px var(--shadow-color-strong), 0 2px 4px -2px var(--shadow-color)`): hover de card clicável e de card de tarefa.
- **Flutuar** (`box-shadow: 0 20px 40px -12px var(--shadow-color-strong), 0 4px 10px -4px var(--shadow-color)`): diálogos, toasts, menus.
- **Halo Índigo** (`box-shadow: 0 30px 60px -24px rgb(63 64 191 / 0.35)` somado ao Flutuar): só a janela de produto da landing; no plano em destaque, anel de 1px índigo com `0 24px 48px -24px rgb(63 64 191 / 0.4)`.

### Named Rules
**A Regra do Plano com Borda.** Em repouso, card é superfície mais borda de 1px, sem sombra (exceto o que se arrasta). Sombra média só como resposta a hover; sombra grande só para camadas que flutuam.

## Shapes

Cantos suaves escalonados pelo tamanho do objeto: 6px para itens de menu, badges, chips e controles pequenos; 8px para botões, campos, alertas, toasts, linhas e avatar de workspace; 12px para cards, painéis, diálogos, tabelas e a janela de produto. A pílula (999px) fica para contadores, marcadores de plano e filtros de rótulo. Pessoas são círculos; workspaces são quadrados de canto 8px. Bordas são sempre de 1px (1.5px só no chip de rótulo alternável); a única linha de 2px é o sublinhado da aba ativa e o anel de foco.

## Components

### Buttons
Contidos e diretos: um primário por região, o resto recua.
- **Shape:** cantos suaves (8px), altura 36px (28px pequeno, 40px grande), ícone de 16px com 8px de espaço.
- **Primary:** Índigo Nexus com texto branco, peso 500, 16px nas laterais.
- **Hover / Focus:** hover escurece para primary-hover em 120ms; clique vai a primary-active e desce 1px; foco é o anel de 2px afastado 2px. Carregando, o texto fica transparente (largura mantida) e aparece um anel girando.
- **Secondary:** folha com borda forte e sombra Pouso; hover em tom de hover.
- **Ghost:** transparente, texto secundário; ações de baixa ênfase em barras e listas.
- **Danger:** Vermelho de Ação com texto branco, só para destruir algo; a versão discreta é texto danger-text sobre fundo suave no hover.
- **Icon button:** 36px quadrado, texto muted, hover em tom de hover; aberto ou pressionado fica em Índigo Névoa com ícone accent-text. Sempre com rótulo acessível.
- **Disabled:** bandeja, borda simples, texto desabilitado.

### Chips
- **Badge:** 20px de altura, canto 6px, caption em 500 sobre bandeja. Variantes suaves: acento, sucesso, aviso, perigo, contorno.
- **Status:** bolinha de 8px da cor do fluxo mais o nome, sobre o fundo suave da família.
- **Prioridade:** três barras de 3px (40/70/100%) coloridas conforme o nível; urgente em vermelho com texto semibold. Versão preenchida em pílula suave no calendário e em legendas.
- **Rótulo:** cor definida pelo time, texto calculado para contraste.

### Cards / Containers
- **Corner Style:** 12px (card de tarefa: 8px).
- **Background:** folha (surface; surface-raised no escuro).
- **Shadow Strategy:** plano com borda; card clicável ganha Levantar no hover; card de tarefa repousa em Pouso e sobe a Levantar.
- **Border:** 1px border; hover passa a border-strong; card de tarefa com foco dentro ganha borda de foco.
- **Internal Padding:** 20px (card), 12px (card de tarefa), 32px por 24px (card de plano). Listas dentro de cards usam divisórias, nunca cards aninhados.

### Inputs / Fields
- **Style:** 36px de altura, 12px nas laterais, borda forte de 1px, canto 8px, fundo folha. Placeholder em texto desabilitado. Select com chevron próprio que troca de cor no tema escuro.
- **Focus:** borda vira cor de foco mais halo de 3px a 24%.
- **Error / Disabled:** inválido tem borda vermelha e halo vermelho; mensagem em danger-text 13px. Desabilitado e somente leitura usam bandeja.

### Navigation
- **Topbar:** 56px, grade de três colunas (marca, busca central até 32rem, ações), fundo translúcido com desfoque e borda inferior. Wordmark em 700, 17px.
- **Sidebar:** 15.5rem, linhas de 32px com canto 6px, texto secundário em 500. Hover em tom de hover; ativo em Índigo Névoa com texto e ícone accent-text. Árvore com chevron que gira 90 graus. Contador de não lidas em pílula Vermelho de Ação. Abaixo de 1024px vira gaveta.
- **Abas de visão:** texto muted em 500 sobre linha de 1px; ativa com texto pleno e sublinhado índigo de 2px. Contador da aba ativa em Índigo Névoa.
- **Segmentado:** trilha de bandeja com 3px de respiro; opção ativa vira folha com sombra Pouso.

### Feedback
- **Alerta:** inline, canto 8px, fundo suave, borda e texto da família semântica.
- **Toast:** canto inferior direito, canto 8px, folha com sombra Flutuar, ícone colorido pela família; entra subindo 8px em 260ms.
- **Diálogo:** `<dialog>` nativo, até 28rem, canto 12px, sombra Flutuar, fundo de sobreposição escurecido; ícone em círculo de 40px (Índigo Névoa ou suave de perigo).
- **Estado vazio:** centralizado, ícone de 22px em quadrado de bandeja de 44px com canto 12px, título small, descrição até 26rem e ações.

### Janela do Produto (assinatura da landing)
A prova da landing é o próprio Nexus em miniatura: card de 12px com Halo Índigo, barra de 44px com caminho (workspace, space, lista) e avatares sobrepostos, abas Kanban, Lista, Calendário, Gantt, Docs e Chat no mesmo desenho das abas do app, painel em Papel Frio com as visões de exemplo e legenda "Tela ilustrativa, com dados de exemplo." Trocar de aba não recarrega; o painel entra subindo 6px em 260ms. O fechamento da landing é uma faixa escura (#111827 no claro, #202A3C no escuro) com texto branco e o índigo só no botão.

### Movimento
Transições de estado em 120ms, abrir e girar em 180ms, entradas de toast e painel em 260ms, sempre com curva de saída `cubic-bezier(0.16, 1, 0.3, 1)`. Com movimento reduzido, mantém cor e opacidade e tira deslocamento e escala.

## Do's and Don'ts

### Do:
- **Do** usar accent-text (#4B4CD8 claro, #A5A8FF escuro) para qualquer texto ou ícone índigo.
- **Do** usar danger-fill (#DC2626) sob texto branco; #EF4444 fica para ícone, bolinha e borda.
- **Do** oferecer só as sete cores de workspace de `WorkspaceColors`, todas com 4,5:1 contra a inicial branca.
- **Do** declarar toda cor nova uma única vez com `light-dark()` em `tokens.css` e conferir AA nos dois temas.
- **Do** manter um botão primário por região e usar secundário ou ghost para o resto.
- **Do** separar superfícies por tom e borda de 1px; reservar sombra para hover, arraste e camadas flutuantes.
- **Do** usar os breakpoints 640px e 1024px e alvos de 44px em ponteiro grosso.
- **Do** desenhar ícones pelo componente Icon: traço 2px, grade 24px, decorativo por padrão e com rótulo quando for o único conteúdo do controle.

### Don't:
- **Don't** usar #5B5CEB como cor de texto sobre fundo tingido ou escuro.
- **Don't** introduzir um segundo acento de marca; cor semântica não vira decoração.
- **Don't** usar `clamp()` ou tamanhos fluidos nas telas do app.
- **Don't** usar pesos fora de 400, 500, 600 e 700 nem outra família além de Inter.
- **Don't** colocar sombra em card em repouso nem aninhar cards dentro de cards.
- **Don't** escrever hex solto em componente quando existe token.
- **Don't** pôr rótulo em caixa alta acima de título; o sistema não usa sobretítulos.
- **Don't** usar emoji ou caracteres como ícone.
