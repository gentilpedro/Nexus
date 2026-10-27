-- Remove o workspace de demonstração "Aurora Digital" e as contas falsas (@example.com) criados
-- por seed-demo.sql. Rodar DEPOIS DA BANCA FINAL DO TCC — ver docs/dados-demonstracao.md.
--
-- Apaga tudo o que estiver dentro do workspace d3e70000-0001-4000-8000-000000000001, inclusive
-- o que for criado durante a gravação (tarefas, comentários, mensagens, documentos), e as contas
-- d3e70000-0002-... com e-mail @example.com. Não toca em nada do Pedro fora desse workspace.
-- Os arquivos enviados ao workspace (anexos e documentos do tipo arquivo) ficam no volume de
-- uploads: a primeira consulta lista os caminhos para apagar depois (passo descrito no .md).

\set ON_ERROR_STOP on
BEGIN;

CREATE TEMP TABLE demo_ws ON COMMIT DROP AS
    SELECT "Id" FROM "Workspaces" WHERE "Id" = 'd3e70000-0001-4000-8000-000000000001';
CREATE TEMP TABLE demo_users ON COMMIT DROP AS
    SELECT "Id" FROM "AspNetUsers"
    WHERE "Id" LIKE 'd3e70000-0002-%' AND "NormalizedEmail" LIKE '%@EXAMPLE.COM';
CREATE TEMP TABLE demo_lists ON COMMIT DROP AS
    SELECT l."Id" FROM "TaskLists" l JOIN "Spaces" s ON s."Id" = l."SpaceId" WHERE s."WorkspaceId" IN (SELECT "Id" FROM demo_ws);
CREATE TEMP TABLE demo_items ON COMMIT DROP AS
    SELECT "Id" FROM "WorkItems" WHERE "TaskListId" IN (SELECT "Id" FROM demo_lists);

-- Arquivos para apagar do volume de uploads depois do COMMIT.
SELECT 'ARQUIVO: ' || "StoragePath" FROM "WorkItemAttachments" WHERE "WorkItemId" IN (SELECT "Id" FROM demo_items)
UNION ALL SELECT 'ARQUIVO: ' || "FileStoragePath" FROM "DocPages" WHERE "WorkspaceId" IN (SELECT "Id" FROM demo_ws) AND "FileStoragePath" IS NOT NULL
UNION ALL SELECT 'ARQUIVO: ' || a."StoragePath" FROM "ChatMessageAttachments" a JOIN "ChatMessages" m ON m."Id" = a."ChatMessageId" WHERE m."WorkspaceId" IN (SELECT "Id" FROM demo_ws)
UNION ALL SELECT 'AVATAR: ' || "AvatarStoragePath" FROM "AspNetUsers" WHERE "Id" IN (SELECT "Id" FROM demo_users) AND "AvatarStoragePath" IS NOT NULL;

-- Notificações do workspace (de qualquer pessoa, inclusive do Pedro) e das contas falsas.
DELETE FROM "Notifications"
WHERE "WorkspaceId" IN (SELECT "Id" FROM demo_ws) OR "WorkItemId" IN (SELECT "Id" FROM demo_items)
   OR "UserId" IN (SELECT "Id" FROM demo_users);

-- Chat (menções e anexos caem em cascata).
DELETE FROM "ChatMessages" WHERE "WorkspaceId" IN (SELECT "Id" FROM demo_ws);

-- Itens: primeiro o que aponta para eles com RESTRICT, depois os próprios itens (filhos antes dos épicos).
DELETE FROM "WorkItemLabels" WHERE "WorkItemId" IN (SELECT "Id" FROM demo_items);
DELETE FROM "CustomFieldValues" WHERE "WorkItemId" IN (SELECT "Id" FROM demo_items);
DELETE FROM "WorkItemComments" WHERE "WorkItemId" IN (SELECT "Id" FROM demo_items);
DELETE FROM "WorkItemAttachments" WHERE "WorkItemId" IN (SELECT "Id" FROM demo_items);
UPDATE "WorkItems" SET "ParentEpicId" = NULL, "SprintId" = NULL WHERE "Id" IN (SELECT "Id" FROM demo_items);
DELETE FROM "WorkItems" WHERE "Id" IN (SELECT "Id" FROM demo_items);

-- Estrutura das listas.
DELETE FROM "StatusTransitions" WHERE "FromStatusId" IN (SELECT "Id" FROM "TaskStatusDefinitions" WHERE "TaskListId" IN (SELECT "Id" FROM demo_lists))
                                   OR "ToStatusId" IN (SELECT "Id" FROM "TaskStatusDefinitions" WHERE "TaskListId" IN (SELECT "Id" FROM demo_lists));
DELETE FROM "CustomFieldDefinitions" WHERE "TaskListId" IN (SELECT "Id" FROM demo_lists);
DELETE FROM "Labels" WHERE "TaskListId" IN (SELECT "Id" FROM demo_lists);
DELETE FROM "Sprints" WHERE "TaskListId" IN (SELECT "Id" FROM demo_lists);
DELETE FROM "TaskStatusDefinitions" WHERE "TaskListId" IN (SELECT "Id" FROM demo_lists);
DELETE FROM "TaskLists" WHERE "Id" IN (SELECT "Id" FROM demo_lists);
DELETE FROM "Spaces" WHERE "WorkspaceId" IN (SELECT "Id" FROM demo_ws);

-- Documentos (operações de edição caem em cascata).
DELETE FROM "DocPages" WHERE "WorkspaceId" IN (SELECT "Id" FROM demo_ws);

-- O workspace (membros, convites e assinatura caem em cascata).
DELETE FROM "Workspaces" WHERE "Id" IN (SELECT "Id" FROM demo_ws);

-- Contas falsas. Se alguma ainda aparecer em outro workspace, o RESTRICT de autoria segura
-- o DELETE e a transação inteira volta — sinal de que alguém usou a conta fora da demo.
DELETE FROM "DocOperations" WHERE "UserId" IN (SELECT "Id" FROM demo_users);
DELETE FROM "AuditLogEntries" WHERE "ActorUserId" IN (SELECT "Id" FROM demo_users)
                                 OR "Detail" ILIKE '%@example.com%'
                                 OR "TargetId" IN (SELECT "Id"::text FROM demo_ws);
DELETE FROM "AspNetUsers" WHERE "Id" IN (SELECT "Id" FROM demo_users);

-- Conferência: tudo zerado.
SELECT 'restam workspaces demo: ' || count(*) FROM "Workspaces" WHERE "Id"::text LIKE 'd3e70000-%';
SELECT 'restam contas demo: ' || count(*) FROM "AspNetUsers" WHERE "Id" LIKE 'd3e70000-%' OR "NormalizedEmail" LIKE '%@EXAMPLE.COM';

COMMIT;
