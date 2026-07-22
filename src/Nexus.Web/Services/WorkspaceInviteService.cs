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

// Handles the two shapes an invite can take: the invited email already has a Nexus account (added
// as a member right away, just gets a notification email) or it doesn't (a WorkspaceInvite row is
// created and the person accepts it by registering/logging in at /convite/{Token}).
public class WorkspaceInviteService(
    IDbContextFactory<AppDbContext> dbFactory,
    BrevoMailer mailer,
    NavigationManager navigationManager)
{
    private static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(7);

    public async Task NotifyAddedAsync(string toEmail, string workspaceName, Guid workspaceId)
    {
        var workspaceUrl = navigationManager.ToAbsoluteUri($"/workspaces/{workspaceId}").ToString();
        await mailer.SendAsync(
            toEmail,
            $"Você foi adicionado ao workspace \"{workspaceName}\" no Nexus",
            $"<p>Você agora faz parte do workspace <strong>{workspaceName}</strong> no Nexus.</p><p><a href='{workspaceUrl}'>Clique aqui para acessar</a>.</p>");
    }

    public async Task CreateAndSendAsync(Guid workspaceId, string workspaceName, string email, WorkspaceRole role, string? invitedByUserId)
    {
        var token = Guid.NewGuid().ToString("N");

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.WorkspaceInvites.Add(new WorkspaceInvite
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                Email = email.Trim(),
                Role = role,
                Token = token,
                InvitedByUserId = invitedByUserId,
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.Add(InviteLifetime),
            });
            await db.SaveChangesAsync();
        }

        var acceptUrl = navigationManager.ToAbsoluteUri($"/convite/{token}").ToString();
        await mailer.SendAsync(
            email,
            $"Você foi convidado para o workspace \"{workspaceName}\" no Nexus",
            $"<p>Você foi convidado para participar do workspace <strong>{workspaceName}</strong> no Nexus.</p>" +
            $"<p><a href='{acceptUrl}'>Clique aqui para aceitar o convite</a> — se ainda não tiver conta, você poderá criar uma na mesma página.</p>" +
            "<p>Este convite expira em 7 dias.</p>");
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

        if (invite.AcceptedAtUtc is null && invite.ExpiresAtUtc <= DateTime.UtcNow)
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
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                Id = Guid.NewGuid(),
                WorkspaceId = invite.WorkspaceId,
                UserId = userId,
                Role = invite.Role,
                JoinedAtUtc = DateTime.UtcNow,
            });
        }

        invite.AcceptedAtUtc ??= DateTime.UtcNow;
        await db.SaveChangesAsync();

        return InviteAcceptResult.Accepted;
    }
}
