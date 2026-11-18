namespace Nexus.Domain.Entities;

public enum AuditAction
{
    PersonalDataDownloaded,
    PersonalDataAnonymized,
    WorkspaceMemberRoleChanged,
    WorkspaceMemberRemoved,
    WorkspaceInviteSent,
    WorkspaceInviteCanceled,
    WorkspaceApiKeyCreated,
    WorkspaceApiKeyRevoked,
    WorkspaceWebhookCreated,
    WorkspaceWebhookDeleted,
    ContractCreated,
    ContractUpdated,
    ContractDeleted,
    TimeEntryDeletedByAdmin,
    DirectorySettingsUpdated,
    DirectoryAccountProvisioned,
}
