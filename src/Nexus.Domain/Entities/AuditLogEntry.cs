namespace Nexus.Domain.Entities;

// Minimal LGPD-oriented audit trail (art. 46/37): records who did what, to which record,
// and when, for actions that touch someone's personal data or workspace access level.
// Deliberately not a full activity log for every domain action — just the subset relevant
// to a data-subject or access-control investigation.
public class AuditLogEntry
{
    public Guid Id { get; set; }

    // Who performed the action. Nullable because it's SetNull on user removal — the audit
    // trail must survive even if the actor's account is later anonymized.
    public string? ActorUserId { get; set; }
    public ApplicationUser? ActorUser { get; set; }

    public AuditAction Action { get; set; }

    // Id of the affected record (e.g. the user id whose data was downloaded, the workspace
    // member id whose role changed). Plain string, not a FK, so the entry outlives the
    // record it describes.
    public string? TargetId { get; set; }

    public string? Detail { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
