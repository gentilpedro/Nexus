# Registro de Operações de Tratamento (ROT) — Nexus

> Última atualização: 22/07/2026. Mantido junto ao código para ficar sob controle de
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
| **Compartilhamento** | Nenhum terceiro além do provedor de hospedagem (operador, art. 5º, VII) |
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

## 7. Chaves de proteção de dados (Data Protection)

| | |
|---|---|
| **Dados tratados** | Chaves criptográficas usadas para proteger cookies de autenticação e tokens antiforgery — não é dado pessoal do titular, mas sensível operacionalmente |
| **Onde vive** | Tabela de Data Protection Keys no banco (`PersistKeysToDbContext`) |
| **Finalidade** | Manter sessões válidas entre reinícios do container |
| **Retenção** | Enquanto a aplicação estiver em produção |

---

## Pendências conhecidas (ver auditoria LGPD de 22/07/2026)

- Confirmar com o provedor de hospedagem se há criptografia de disco/TDE no SQL Server.
- Revisar `TrustServerCertificate=True` na connection string à luz da topologia real de rede.
- Definir processo para pedidos de titulares terceiros mencionados em conteúdo de texto livre.
- Finalizar e obter revisão jurídica da Política de Privacidade antes de remover o aviso de rascunho.
