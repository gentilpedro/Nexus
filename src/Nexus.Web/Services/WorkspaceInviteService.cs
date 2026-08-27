using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;

namespace Nexus.Web.Services;

public enum InviteAcceptResult
{
    Accepted,
    NotFound,
    Expired,
    EmailMismatch,
}

public enum InviteSendStatus
{
    /// <summary>Invite created (or refreshed) and the e-mail went out.</summary>
    Sent,

    /// <summary>Invite is recorded and pending, but the e-mail could not be delivered.</summary>
    SentWithoutEmail,

    /// <summary>An earlier invite for this address was still pending; it was refreshed and re-sent.</summary>
    Resent,

    /// <summary>The address already belongs to someone who is a member of the workspace.</summary>
    AlreadyMember,
}

/// <param name="Status">What happened to the invite.</param>
/// <param name="NotifiedInApp">
/// True when the address matched an existing account, so an in-app notification was created
/// alongside the e-mail.
/// </param>
public readonly record struct InviteSendOutcome(InviteSendStatus Status, bool NotifiedInApp);

// Every invite — whether or not the address already has a Nexus account — is a pending
// WorkspaceInvite row until the person explicitly accepts it at /convite/{Token}. Nobody is added
// to a workspace on someone else's say-so: the WorkspaceMember row is only written by AcceptAsync.
// Addresses that already have an account also get an in-app notification pointing at the same
// accept page.
public class WorkspaceInviteService(
    IDbContextFactory<AppDbContext> dbFactory,
    BrevoMailer mailer,
    PublicUrlBuilder publicUrl)
{
    private static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(7);

    public async Task<InviteSendOutcome> CreateAndSendAsync(Guid workspaceId, string workspaceName, string email, WorkspaceRole role, string? invitedByUserId)
    {
        email = email.Trim();
        var normalizedEmail = email.ToUpperInvariant();

        string token;
        var resent = false;
        ApplicationUser? existingUser;
        string? inviterName = null;

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            existingUser = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);

            // Having an account is not the same as having accepted: only refuse when this person
            // is actually a member already.
            if (existingUser is not null
                && await db.WorkspaceMembers.AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == existingUser.Id))
            {
                return new InviteSendOutcome(InviteSendStatus.AlreadyMember, false);
            }

            // Re-inviting an address that is already pending refreshes the existing row instead of
            // piling up rows: the members panel would otherwise show the same person as pending
            // several times, and every old token would stay live.
            var pending = await db.WorkspaceInvites
                .Where(i => i.WorkspaceId == workspaceId && i.Email.ToUpper() == normalizedEmail && i.AcceptedAtUtc == null)
                .OrderByDescending(i => i.CreatedAtUtc)
                .FirstOrDefaultAsync();

            if (pending is not null)
            {
                resent = true;
                pending.Role = role;
                pending.InvitedByUserId = invitedByUserId;
                pending.CreatedAtUtc = DateTime.UtcNow;
                pending.ExpiresAtUtc = DateTime.UtcNow.Add(InviteLifetime);
                token = pending.Token;
            }
            else
            {
                token = Guid.NewGuid().ToString("N");
                db.WorkspaceInvites.Add(new WorkspaceInvite
                {
                    Id = Guid.NewGuid(),
                    WorkspaceId = workspaceId,
                    Email = email,
                    Role = role,
                    Token = token,
                    InvitedByUserId = invitedByUserId,
                    CreatedAtUtc = DateTime.UtcNow,
                    ExpiresAtUtc = DateTime.UtcNow.Add(InviteLifetime),
                });
            }

            if (existingUser is not null)
            {
                if (invitedByUserId is not null)
                {
                    inviterName = (await db.Users.FirstOrDefaultAsync(u => u.Id == invitedByUserId))?.DisplayName;
                }

                var message = string.IsNullOrWhiteSpace(inviterName)
                    ? $"Você foi convidado para o workspace \"{workspaceName}\". Clique para aceitar."
                    : $"{inviterName} convidou você para o workspace \"{workspaceName}\". Clique para aceitar.";

                db.Notifications.Add(new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = existingUser.Id,
                    Type = NotificationType.WorkspaceInvite,
                    Message = Notification.TruncateMessage(message),
                    // Deliberately not WorkspaceId: that would make Notifications.razor link into a
                    // workspace this person is not a member of yet. The accept page is the target.
                    LinkUrl = $"/convite/{token}",
                    CreatedAtUtc = DateTime.UtcNow,
                });
            }

            await db.SaveChangesAsync();
        }

        // PublicUrlBuilder, not NavigationManager.ToAbsoluteUri — these URLs are e-mailed, so
        // deriving them from the request's Host header would let an attacker point them at a
        // domain they control. See PublicUrlBuilder.
        var acceptUrl = publicUrl.BuildUrl($"convite/{token}", new Dictionary<string, object?>());

        var details = new List<EmailDetail> { new("Workspace", workspaceName) };
        if (!string.IsNullOrWhiteSpace(inviterName))
        {
            details.Add(new EmailDetail("Convidado por", inviterName));
        }

        var content = EmailTemplate.Render(
            title: "Você foi convidado para um workspace",
            preview: $"Entre no workspace {workspaceName} no Nexus.",
            paragraphs:
            [
                existingUser is not null
                    ? "Aceite o convite para passar a fazer parte deste workspace. Entre com esta mesma conta para confirmar."
                    : "Aceite o convite para passar a fazer parte deste workspace. Você poderá criar sua conta na mesma página, com este mesmo e-mail.",
            ],
            button: new EmailButton("Aceitar convite", acceptUrl),
            details: details,
            footnote: "Este convite expira em 7 dias.");

        var sent = await mailer.SendAsync(
            email,
            $"Você foi convidado para o workspace \"{workspaceName}\" no Nexus",
            content.Html,
            content.Text);

        var status = sent
            ? (resent ? InviteSendStatus.Resent : InviteSendStatus.Sent)
            : InviteSendStatus.SentWithoutEmail;

        return new InviteSendOutcome(status, existingUser is not null);
    }

    /// <summary>
    /// Invites for this workspace that nobody has accepted yet — what the members panel shows as
    /// "Pendente". Expired ones are included so an admin can see them and re-send.
    /// </summary>
    public async Task<List<WorkspaceInvite>> GetPendingAsync(Guid workspaceId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.WorkspaceInvites
            .Where(i => i.WorkspaceId == workspaceId && i.AcceptedAtUtc == null)
            .OrderBy(i => i.Email)
            .ToListAsync();
    }

    /// <summary>Withdraws a pending invite. Scoped by workspace so an id alone is not enough.</summary>
    public async Task<bool> CancelAsync(Guid inviteId, Guid workspaceId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        // Load-then-Remove rather than ExecuteDeleteAsync: it's a single row either way, and this
        // works on the in-memory provider the service tests run against.
        var invite = await db.WorkspaceInvites
            .FirstOrDefaultAsync(i => i.Id == inviteId && i.WorkspaceId == workspaceId && i.AcceptedAtUtc == null);
        if (invite is null)
        {
            return false;
        }

        db.WorkspaceInvites.Remove(invite);
        await db.SaveChangesAsync();
        return true;
    }

    // Raw lookup (ignores accepted/expired state) so the accept page can show a specific reason
    // ("already used" vs "expired" vs "not found") instead of a generic "invalid invite".
    public async Task<WorkspaceInvite?> GetAsync(string token)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.WorkspaceInvites
            .Include(i => i.Workspace)
            .FirstOrDefaultAsync(i => i.Token == token);
    }

    public async Task<InviteAcceptResult> AcceptAsync(string token, string userId, string userEmail)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var invite = await db.WorkspaceInvites.FirstOrDefaultAsync(i => i.Token == token);

        if (invite is null)
        {
            return InviteAcceptResult.NotFound;
        }

        // Once accepted, an invite is permanently spent — even if the caller (currently only
        // WorkspaceInviteAccept.razor) already blocks revisiting an accepted invite before ever
        // reaching here, the service shouldn't rely solely on that. Without this, someone removed
        // from the workspace after accepting could theoretically be silently re-added via the
        // same old token.
        if (invite.AcceptedAtUtc is not null)
        {
            return InviteAcceptResult.Expired;
        }

        if (invite.ExpiresAtUtc <= DateTime.UtcNow)
        {
            return InviteAcceptResult.Expired;
        }

        if (!string.Equals(invite.Email, userEmail, StringComparison.OrdinalIgnoreCase))
        {
            return InviteAcceptResult.EmailMismatch;
        }

        var alreadyMember = await db.WorkspaceMembers.AnyAsync(m => m.WorkspaceId == invite.WorkspaceId && m.UserId == userId);
        if (!alreadyMember)
        {
            // Re-check the inviter's standing instead of trusting the role captured at invite
            // creation — an invite can sit unaccepted for up to 7 days, long enough for the
            // inviter to be demoted or removed. Only Admin/Owner can grant Admin, so a stale
            // invite from someone no longer in that position is honored as a Member invite
            // instead of silently granting the privilege they can no longer hand out.
            var grantedRole = invite.Role;
            if (grantedRole == WorkspaceRole.Admin)
            {
                var inviterStillAdmin = invite.InvitedByUserId is not null && await db.WorkspaceMembers.AnyAsync(
                    m => m.WorkspaceId == invite.WorkspaceId
                        && m.UserId == invite.InvitedByUserId
                        && (m.Role == WorkspaceRole.Admin || m.Role == WorkspaceRole.Owner));
                if (!inviterStillAdmin)
                {
                    grantedRole = WorkspaceRole.Member;
                }
            }

            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                Id = Guid.NewGuid(),
                WorkspaceId = invite.WorkspaceId,
                UserId = userId,
                Role = grantedRole,
                JoinedAtUtc = DateTime.UtcNow,
            });
        }

        invite.AcceptedAtUtc ??= DateTime.UtcNow;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException) when (!alreadyMember)
        {
            // Lost a race with a concurrent accept of the same invite (double-click, two open
            // tabs) — the unique (WorkspaceId, UserId) index on WorkspaceMembers rejected our
            // insert because the other request's already landed and committed first. The end
            // state we wanted (user is a member) is already true, so this isn't a real failure;
            // only re-throw if that assumption turns out wrong.
            var stillNotMember = !await db.WorkspaceMembers.AnyAsync(m => m.WorkspaceId == invite.WorkspaceId && m.UserId == userId);
            if (stillNotMember)
            {
                throw;
            }
        }

        return InviteAcceptResult.Accepted;
    }
}
