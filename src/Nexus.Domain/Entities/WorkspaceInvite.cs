namespace Nexus.Domain.Entities;

// Created when someone invites an email that has no account yet (WorkspaceCreate/WorkspaceMembers).
// The invited person accepts it by registering (or logging in, if they created an account in the
// meantime through some other path) at /convite/{Token}, which adds the WorkspaceMember row.
public class WorkspaceInvite
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }
    public Workspace Workspace { get; set; } = null!;

    public string Email { get; set; } = "";
    public WorkspaceRole Role { get; set; }

    public string Token { get; set; } = "";

    // Nullable + SetNull (see WorkspaceInviteConfiguration): the invite record should stay valid
    // even if the inviter's account is later anonymized.
    public string? InvitedByUserId { get; set; }
    public ApplicationUser? InvitedByUser { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
}
