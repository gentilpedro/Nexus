using Microsoft.EntityFrameworkCore;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;

namespace Nexus.Web.Services;

/// <summary>
/// Creates and maintains chat conversations. Every method re-validates workspace membership
/// server-side: the ids it receives are whatever the browser sent over the circuit.
/// </summary>
public class ChatConversationService(IDbContextFactory<AppDbContext> dbFactory)
{
    public static ChatConversation NewWorkspaceConversation(Guid workspaceId, DateTime nowUtc) => new()
    {
        Id = Guid.NewGuid(),
        WorkspaceId = workspaceId,
        Type = ChatConversationType.Workspace,
        CreatedAtUtc = nowUtc
    };

    /// <summary>The workspace-wide conversation, created on demand if the workspace somehow lacks one.</summary>
    public async Task<Guid> EnsureWorkspaceConversationAsync(Guid workspaceId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var existing = await db.ChatConversations
            .Where(c => c.WorkspaceId == workspaceId && c.Type == ChatConversationType.Workspace)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync();

        if (existing is not null)
        {
            return existing.Value;
        }

        var conversation = NewWorkspaceConversation(workspaceId, DateTime.UtcNow);
        db.ChatConversations.Add(conversation);

        try
        {
            await db.SaveChangesAsync();
            return conversation.Id;
        }
        catch (DbUpdateException)
        {
            // Lost the race to another request creating the same row; the filtered unique index
            // guarantees exactly one exists now.
            return await db.ChatConversations
                .Where(c => c.WorkspaceId == workspaceId && c.Type == ChatConversationType.Workspace)
                .Select(c => c.Id)
                .FirstAsync();
        }
    }

    /// <summary>
    /// The direct conversation between two workspace members, created if they never talked before.
    /// Null when either of them is not (or no longer) a member of the workspace.
    /// </summary>
    public async Task<Guid?> GetOrCreateDirectAsync(Guid workspaceId, string userId, string otherUserId)
    {
        if (userId == otherUserId)
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync();

        var memberCount = await db.WorkspaceMembers
            .CountAsync(m => m.WorkspaceId == workspaceId && (m.UserId == userId || m.UserId == otherUserId));
        if (memberCount != 2)
        {
            return null;
        }

        var directKey = ChatConversation.BuildDirectKey(userId, otherUserId);

        var existing = await FindDirectAsync(db, workspaceId, directKey);
        if (existing is not null)
        {
            return existing;
        }

        var now = DateTime.UtcNow;
        var conversation = new ChatConversation
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Type = ChatConversationType.Direct,
            DirectKey = directKey,
            CreatedByUserId = userId,
            CreatedAtUtc = now
        };
        conversation.Members.Add(new ChatConversationMember { Id = Guid.NewGuid(), ConversationId = conversation.Id, UserId = userId, JoinedAtUtc = now });
        conversation.Members.Add(new ChatConversationMember { Id = Guid.NewGuid(), ConversationId = conversation.Id, UserId = otherUserId, JoinedAtUtc = now });
        db.ChatConversations.Add(conversation);

        try
        {
            await db.SaveChangesAsync();
            return conversation.Id;
        }
        catch (DbUpdateException)
        {
            // The other person opened the same conversation at the same moment; the unique index
            // on (WorkspaceId, DirectKey) kept one of the two, so use that one.
            return await FindDirectAsync(db, workspaceId, directKey);
        }
    }

    private static Task<Guid?> FindDirectAsync(AppDbContext db, Guid workspaceId, string directKey) =>
        db.ChatConversations
            .Where(c => c.WorkspaceId == workspaceId && c.DirectKey == directKey)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync();

    /// <summary>
    /// A named group with the creator plus <paramref name="memberIds"/>. Null when the name is
    /// empty or any listed person is not a member of the workspace.
    /// </summary>
    public async Task<Guid?> CreateGroupAsync(Guid workspaceId, string creatorId, string name, IReadOnlyCollection<string> memberIds)
    {
        name = name.Trim();
        if (name.Length == 0 || name.Length > ChatConversation.MaxNameLength)
        {
            return null;
        }

        var allIds = memberIds.Append(creatorId).Distinct().ToList();

        await using var db = await dbFactory.CreateDbContextAsync();

        if (!await AllWorkspaceMembersAsync(db, workspaceId, allIds))
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var conversation = new ChatConversation
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Type = ChatConversationType.Group,
            Name = name,
            CreatedByUserId = creatorId,
            CreatedAtUtc = now
        };
        foreach (var id in allIds)
        {
            conversation.Members.Add(new ChatConversationMember { Id = Guid.NewGuid(), ConversationId = conversation.Id, UserId = id, JoinedAtUtc = now });
        }

        db.ChatConversations.Add(conversation);
        await db.SaveChangesAsync();
        return conversation.Id;
    }

    /// <summary>
    /// Adds workspace members to a group. Any current member of the group can do this, the way
    /// anyone in a Slack private channel can invite. False when the actor is not in the group, the
    /// conversation is not a group, or someone listed is not in the workspace.
    /// </summary>
    public async Task<bool> AddMembersAsync(Guid conversationId, string actorId, IReadOnlyCollection<string> memberIds)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var conversation = await db.ChatConversations
            .Include(c => c.Members)
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.Type == ChatConversationType.Group);

        if (conversation is null || conversation.Members.All(m => m.UserId != actorId))
        {
            return false;
        }

        var newIds = memberIds
            .Distinct()
            .Where(id => conversation.Members.All(m => m.UserId != id))
            .ToList();

        if (newIds.Count == 0)
        {
            return true;
        }

        if (!await AllWorkspaceMembersAsync(db, conversation.WorkspaceId, newIds))
        {
            return false;
        }

        var now = DateTime.UtcNow;
        foreach (var id in newIds)
        {
            db.ChatConversationMembers.Add(new ChatConversationMember { Id = Guid.NewGuid(), ConversationId = conversationId, UserId = id, JoinedAtUtc = now });
        }

        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>Removes the user from a group. The group and its history stay for whoever remains.</summary>
    public async Task<bool> LeaveGroupAsync(Guid conversationId, string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var removed = await db.ChatConversationMembers
            .Where(m => m.ConversationId == conversationId && m.UserId == userId && m.Conversation.Type == ChatConversationType.Group)
            .ExecuteDeleteAsync();

        return removed > 0;
    }

    /// <summary>
    /// Records that the user has seen the conversation up to <paramref name="readAtUtc"/>. For the
    /// workspace conversation the marker row is created on first read, since membership there is
    /// implied by the workspace and not stored per conversation.
    /// </summary>
    public async Task MarkReadAsync(Guid conversationId, string userId, DateTime readAtUtc)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var updated = await db.ChatConversationMembers
            .Where(m => m.ConversationId == conversationId && m.UserId == userId && (m.LastReadAtUtc == null || m.LastReadAtUtc < readAtUtc))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.LastReadAtUtc, readAtUtc));

        if (updated > 0)
        {
            return;
        }

        var hasRow = await db.ChatConversationMembers.AnyAsync(m => m.ConversationId == conversationId && m.UserId == userId);
        if (hasRow)
        {
            return;
        }

        var isWorkspaceConversation = await db.ChatConversations
            .AnyAsync(c => c.Id == conversationId && c.Type == ChatConversationType.Workspace);
        if (!isWorkspaceConversation)
        {
            return;
        }

        db.ChatConversationMembers.Add(new ChatConversationMember
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            UserId = userId,
            JoinedAtUtc = readAtUtc,
            LastReadAtUtc = readAtUtc
        });

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Two tabs opened the same conversation at once and both tried to create the marker;
            // the unique index kept one, which is all that is needed.
        }
    }

    private static async Task<bool> AllWorkspaceMembersAsync(AppDbContext db, Guid workspaceId, IReadOnlyCollection<string> userIds)
    {
        var found = await db.WorkspaceMembers
            .CountAsync(m => m.WorkspaceId == workspaceId && userIds.Contains(m.UserId));
        return found == userIds.Count;
    }
}
