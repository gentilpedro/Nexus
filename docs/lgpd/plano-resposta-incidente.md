# Plano de Resposta a Incidente de Segurança — Nexus

> Última atualização: 22/07/2026. Cobre a obrigação do art. 48 da LGPD de comunicar à ANPD
> e aos titulares afetados um incidente de segurança que possa acarretar risco ou dano
> relevante. Ver também [ROT](rot.md) para o que cada tabela/entidade contém.

## 1. O que conta como incidente

Qualquer acesso, alteração, destruição ou vazamento não autorizado de dado pessoal tratado
pelo Nexus. Exemplos concretos neste sistema:

- Credenciais de banco de dados ou de hospedagem expostas/comprometidas.
- Acesso indevido ao servidor/contêiner que hospeda o Nexus (ex.: exploração de
  vulnerabilidade, credencial vazada).
- Bug de autorização que exponha dados de um workspace a quem não é membro.
- Perda de backup sem cópia recuperável.

## 2. Passos imediatos (primeiras horas)

1. **Conter**: revogar/rotacionar a credencial ou fechar o acesso comprometido
   (senha do SQL Server, chaves de Data Protection, credencial do provedor de hospedagem).
   Se for uma falha de autorização no código, aplicar o patch e fazer deploy assim que possível.
2. **Preservar evidência**: não apagar `AuditLogEntry`, logs de aplicação ou logs do
   provedor de hospedagem antes de entender o escopo — eles são a base para descrever
   o incidente no aviso à ANPD/titulares.
3. **Escopo**: usar `AuditLogEntry` e os relacionamentos de `WorkspaceMember` para
   identificar quais usuários/workspaces foram efetivamente afetados. Não superestimar
   nem subestimar o alcance.

## 3. Avaliação de risco

Classificar o incidente por tipo de dado exposto (ver [ROT](rot.md)):

| Dado exposto | Risco | Aviso obrigatório? |
|---|---|---|
| Nome, e-mail, avatar | Baixo/moderado | Sim, se volume ou contexto indicar risco relevante ao titular |
| Senha (hash) | Baixo isoladamente (não reversível), mas força reset preventivo | Sim, com orientação de troca de senha |
| Conteúdo de workspace (tarefas/chat/docs), incluindo eventual CPF/dado bancário de terceiro digitado em texto livre | Alto — pode incluir dado sensível de terceiro não usuário do sistema | Sim |
| Trilha de auditoria (`AuditLogEntry`) | Baixo (metadados, não é conteúdo em si) | Avaliar caso a caso |

Na dúvida, tratar como "sim, notificar" — o custo de uma notificação desnecessária é
muito menor que o de uma omissão em caso de dado sensível de terceiro exposto.

## 4. Comunicação

**Prazo**: comunicar à ANPD e aos titulares em prazo razoável, tão logo o incidente seja
identificado e o escopo minimamente compreendido — não esperar a investigação completa
terminar para dar o primeiro aviso. Um aviso inicial com atualização posterior é melhor
que atraso.

**Canal para titulares**: e-mail cadastrado do usuário (`ApplicationUser.Email`) e, se
o incidente afetar um workspace específico, aviso aos membros daquele workspace.

**Conteúdo mínimo do comunicado** (art. 48 §1º):

- Descrição da natureza dos dados pessoais afetados.
- Informações sobre os titulares envolvidos (quantidade/categoria, sem expor dados de
  terceiros no próprio aviso).
- Indicação das medidas técnicas e de segurança utilizadas para a proteção dos dados,
  observados os segredos comercial e industrial.
- Os riscos relacionados ao incidente.
- Os motivos da eventual demora na comunicação (se houver).
- As medidas que foram ou serão adotadas para reverter ou mitigar os efeitos do prejuízo.

**Canal para a ANPD**: através do canal oficial de comunicação de incidentes da ANPD
(peticionamento eletrônico) — consultar o site da ANPD para o formulário vigente no
momento do incidente, pois o processo pode mudar.

## 5. Pós-incidente

- Registrar o incidente e as ações tomadas (mesmo que fora do `AuditLogEntry`, já que
  esse é para ações de titular/acesso, não para o incidente em si) — manter um registro
  simples (ex.: neste mesmo diretório, `docs/lgpd/incidentes/AAAA-MM-DD-titulo.md`) com
  linha do tempo, causa raiz e correção aplicada.
- Revisar se o [ROT](rot.md) precisa de atualização (ex.: novo dado passou a ser tratado,
  nova medida de segurança adotada).
- Se a causa raiz for uma falha de autorização/código, adicionar teste de regressão
  cobrindo o cenário explorado.

## 6. Responsável

Enquanto o Nexus for operado por pessoa física, Pedro Gentil Roodes Rodrigues acumula o
papel de controlador e de encarregado (art. 41) para efeitos deste plano — é quem recebe,
avalia e comunica o incidente. Contato: pedro.desenv@outlook.com (mesmo e-mail da
Política de Privacidade).
