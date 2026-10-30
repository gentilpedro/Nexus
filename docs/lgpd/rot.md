# Registro de Operações de Tratamento (ROT) — Nexus

> Última atualização: 27/08/2026. Mantido junto ao código para ficar sob controle de
> versão e ser atualizado sempre que uma entidade que armazena dado pessoal for
> adicionada/alterada — ver `src/Nexus.Domain/Entities`.

Controlador: Pedro Gentil Roodes Rodrigues, CPF 054.***.***-36, pessoa física
(ver `src/Nexus.Web/Components/Account/Pages/PrivacyPolicy.razor`).

## 1. Cadastro e autenticação de usuário

| | |
|---|---|
| **Dados tratados** | Nome de exibição, e-mail, telefone (opcional), senha (hash), foto de avatar (opcional) |
| **Onde vive** | `ApplicationUser` (`AspNetUsers`) |
| **Finalidade** | Criar/autenticar a conta, viabilizar identificação dentro dos workspaces |
| **Base legal** | Execução de contrato (art. 7º, V) |
| **Compartilhamento** | Provedor de hospedagem (operador, art. 5º, VII); Resend recebe nome e e-mail apenas para entregar e-mail de confirmação de cadastro/redefinição de senha (`IdentityEmailSender.cs`, via relay SMTP próprio na VPS) |
| **Retenção** | Enquanto a conta estiver ativa; anonimizado imediatamente ao pedido de exclusão (`DeletePersonalData.razor`) |
| **Titular pode** | Baixar (`PersonalData.razor`), corrigir (`Account/Manage`), excluir/anonimizar |

## 2. Segurança de sessão

| | |
|---|---|
| **Dados tratados** | Estado de bloqueio de conta, tentativas de login (via ASP.NET Identity), rate limiting por IP |
| **Onde vive** | `AspNetUsers` (LockoutEnd, AccessFailedCount), memória do rate limiter |
| **Finalidade** | Prevenir acesso indevido à conta |
| **Base legal** | Legítimo interesse (art. 7º, IX) |
| **Compartilhamento** | Nenhum |
| **Retenção** | Enquanto a conta existir; rate limiting é em memória, não persistido |

## 3. Conteúdo de workspace (tarefas, comentários, chat, docs, anexos)

| | |
|---|---|
| **Dados tratados** | Texto livre criado pelos usuários — pode conter dados pessoais de terceiros (CPF, telefone, dados bancários de cliente/representante) se o usuário optar por digitá-los |
| **Onde vive** | `WorkItem`, `WorkItemComment`, `ChatMessage`, `DocPage` e anexos associados |
| **Finalidade** | Colaboração da equipe dentro do workspace |
| **Base legal** | Execução de contrato (art. 7º, V) para o conteúdo em si; a base legal de eventual dado pessoal de terceiro inserido em texto livre é de responsabilidade de quem o inseriu |
| **Compartilhamento** | Visível apenas a membros do mesmo workspace (`WorkspaceAuthorizationHandler`) |
| **Retenção** | Enquanto o workspace existir. Workspaces podem ser arquivados (ocultos, dados preservados); ainda não há exclusão definitiva de workspace |
| **Observação** | Não há hoje um canal para um titular terceiro (não usuário do Nexus) mencionado em texto livre solicitar acesso/exclusão diretamente — ver plano de resposta a incidente e política de privacidade, seção 6 |

## 4. Avatar do usuário

| | |
|---|---|
| **Dados tratados** | Arquivo de imagem (foto de perfil) |
| **Onde vive** | Disco (`AttachmentStorageService`), caminho referenciado em `ApplicationUser.AvatarStoragePath` |
| **Finalidade** | Identificação visual dentro dos workspaces |
| **Base legal** | Execução de contrato / consentimento implícito no upload voluntário |
| **Compartilhamento** | Apenas membros que compartilham workspace com o titular (`/avatars/{userId}/download`, `Program.cs`) |
| **Retenção** | Apagado do disco no momento da exclusão de dados pessoais |

## 5. Trilha de auditoria (LGPD art. 46/37)

| | |
|---|---|
| **Dados tratados** | Ator (user id), ação, alvo, timestamp — não é um log de atividade completo, só ações relevantes para um titular ou controle de acesso |
| **Onde vive** | `AuditLogEntry` |
| **Finalidade** | Comprovar cumprimento de solicitações de titular e mudanças de controle de acesso, em caso de disputa |
| **Base legal** | Cumprimento de obrigação legal / legítimo interesse (art. 7º, II e IX) |
| **Compartilhamento** | Nenhum |
| **Retenção** | 730 dias (24 meses), expurgo automático via `DataRetentionHostedService` — configurável em `AUDIT_LOG_RETENTION_DAYS` |

## 6. Notificações no produto

| | |
|---|---|
| **Dados tratados** | Mensagem de notificação (referencia título de tarefa/ação de outro usuário) |
| **Onde vive** | `Notification` |
| **Finalidade** | Avisar o usuário sobre eventos relevantes (prazo, atribuição) |
| **Base legal** | Execução de contrato |
| **Compartilhamento** | Nenhum |
| **Retenção** | 180 dias, expurgo automático via `DataRetentionHostedService` — configurável em `NOTIFICATION_RETENTION_DAYS` |

## 7b. Convite para workspace (pessoa ainda não usuária)

| | |
|---|---|
| **Dados tratados** | E-mail de quem foi convidado para um workspace, antes de ter conta no Nexus |
| **Onde vive** | `WorkspaceInvite` |
| **Finalidade** | Permitir que a pessoa convidada crie conta (ou entre, se já tiver) e seja automaticamente adicionada ao workspace |
| **Base legal** | Execução de contrato — a pedido do Contratante/membro que fez o convite; o e-mail foi fornecido por um terceiro (quem convidou), não pela própria pessoa |
| **Compartilhamento** | Enviado ao Resend apenas para entrega do e-mail de convite (mesmo operador da seção 1) |
| **Retenção** | Expira em 7 dias se não aceito; não há hoje expurgo automático de convites expirados/aceitos — considerar incluir no `DataRetentionHostedService` |
| **Titular pode** | Se decidir não criar conta, o único dado retido é o e-mail em si — pode solicitar remoção via contato do controlador |

## 7. Chaves de proteção de dados (Data Protection)

| | |
|---|---|
| **Dados tratados** | Chaves criptográficas usadas para proteger cookies de autenticação e tokens antiforgery — não é dado pessoal do titular, mas sensível operacionalmente |
| **Onde vive** | Tabela de Data Protection Keys no banco (`PersistKeysToDbContext`) |
| **Finalidade** | Manter sessões válidas entre reinícios do container |
| **Retenção** | Enquanto a aplicação estiver em produção |

## 8. Cópias de segurança do banco (backup)

| | |
|---|---|
| **Dados tratados** | Cópia integral do banco de produção — contém tudo que está nas seções 1 a 7 (nome, e-mail, telefone, hash de senha, avatares, conteúdo de workspace, trilha de auditoria) |
| **Onde vive** | Desde 10/2026: dump `pg_dump` + anexos gerados por `nexus-backup` na VPS (Locaweb, Brasil), em `/opt/nexus/backups`, acesso só root. Até então: *artifact* privado do GitHub Actions gerado por `backup-nexus.yml` (removido) |
| **Finalidade** | Recuperação de desastre e cumprimento do art. 46 (medidas de segurança); ver `docs/backup-restore.md` |
| **Base legal** | Legítimo interesse (art. 7º, IX) e cumprimento de dever de segurança (art. 46) |
| **Compartilhamento** | Locaweb como operador (art. 5º, VII) — hospeda a VPS; servidores no Brasil, sem transferência internacional. Até a virada: GitHub, Inc. (artifacts, servidores nos EUA) |
| **Retenção** | 7 dias na VPS. Os artifacts antigos do GitHub expiram sozinhos em até 30 dias após a virada |
| **Observação** | Um pedido de exclusão de dados atendido no banco (seção 1) **não** apaga o titular dos dumps já gerados — eles somem sozinhos em até 7 dias. Isso é o prazo máximo de sobrevida do dado pessoal após a exclusão e deve constar da resposta ao titular |

## 9. Contato comercial (plano Business)

| | |
|---|---|
| **Dados tratados** | Nome, empresa, e-mail, tamanho do time e mensagem livre informados no formulário público (pode ser de quem não tem conta) |
| **Onde vive** | `CommercialContact` (`CommercialContacts`); cópia no e-mail enviado à caixa comercial, `Commercial:Email` (`CommercialContactService.cs`) |
| **Finalidade** | Responder ao pedido de contato sobre o plano Business |
| **Base legal** | Procedimentos preliminares a contrato, a pedido do titular (art. 7º, V) |
| **Compartilhamento** | Resend, como operador, entrega o e-mail com os dados à caixa comercial |
| **Retenção** | 12 meses no banco (`COMMERCIAL_CONTACT_RETENTION_DAYS`, apagado por `DataRetentionHostedService`). A cópia na caixa de e-mail segue a política dessa caixa |
| **Titular pode** | Pedir acesso ou exclusão pelo e-mail comercial ou de suporte |

## 10. Cobrança do plano Pro (Stripe)

| | |
|---|---|
| **Dados tratados** | E-mail de quem assina, identificadores do cliente e da assinatura no Stripe, quantidade de assentos, período pago e status. Os dados do cartão são digitados na página do Stripe e **não passam pelo Nexus** |
| **Onde vive** | `WorkspaceSubscription` (`StripeCustomerId`, `StripeSubscriptionId`); ids dos eventos já processados em `StripeWebhookEvents` (sem dado pessoal) |
| **Finalidade** | Cobrar a assinatura do plano Pro e aplicar o plano ao workspace |
| **Base legal** | Execução de contrato (art. 7º, V) |
| **Compartilhamento** | Stripe, como operador (art. 5º, VII), com transferência internacional (art. 33) — servidores do Stripe fora do Brasil |
| **Retenção** | Enquanto o workspace existir; os ids de eventos são apagados em 90 dias (`DataRetentionHostedService`). Os registros fiscais e de pagamento ficam no Stripe, pela política dele |
| **Titular pode** | Pedir acesso ou exclusão pelo e-mail de suporte; os dados de pagamento são geridos no Stripe |

---

## Pendências conhecidas (ver auditoria LGPD de 22/07/2026)

- Confirmar com o provedor de hospedagem se há criptografia de disco/TDE no SQL Server.
- Revisar `TrustServerCertificate=True` na connection string à luz da topologia real de rede.
- Definir processo para pedidos de titulares terceiros mencionados em conteúdo de texto livre.
- Finalizar e obter revisão jurídica da Política de Privacidade antes de remover o aviso de rascunho.
- Avaliar mover os dumps (seção 8) para bucket próprio, evitando o GitHub como operador de cópia integral.
- Refletir na Política de Privacidade a sobrevida de até 30 dias do dado em backup após pedido de exclusão.
