using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;

namespace Nexus.Web.Services;

/// <param name="Url">A URL de assinatura, com o token. Só o dono da conta vê.</param>
public sealed record CalendarFeedInfo(string Url, DateTime CreatedAtUtc);

// Feed iCalendar (#68): as tarefas datadas em que a pessoa é responsável, em todos os workspaces
// dela, como calendário de assinatura no Google, Outlook e Apple. A URL carrega um token: quem
// tem a URL lê o feed, por isso ele é gerado pelo Nexus, pode ser trocado a qualquer hora e fica
// guardado em hash (para achar a conta) e cifrado (para mostrar a URL de novo em Minha conta).
public class CalendarFeedService(
    IDbContextFactory<AppDbContext> dbFactory,
    IDataProtectionProvider dataProtection,
    TimeProvider? timeProvider = null)
{
    public const string TokenPrefix = "nxc_";
    public const string RoutePrefix = "/calendar/";
    public const int MaxDescriptionLength = 400;

    // Tarefas concluídas somem do feed depois deste prazo; as abertas ficam enquanto tiverem data.
    public static readonly TimeSpan DoneRetention = TimeSpan.FromDays(30);

    private readonly IDataProtector protector = dataProtection.CreateProtector("Nexus.CalendarFeed.Token.v1");
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public static string NewToken() => TokenPrefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static string FeedUrl(string baseUrl, string token) => $"{baseUrl.TrimEnd('/')}{RoutePrefix}{token}.ics";

    public async Task<CalendarFeedInfo?> GetAsync(string userId, string baseUrl, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.CalendarFeedTokenProtected, u.CalendarFeedCreatedAtUtc })
            .FirstOrDefaultAsync(ct);
        if (user?.CalendarFeedTokenProtected is null || user.CalendarFeedCreatedAtUtc is null)
        {
            return null;
        }

        return new CalendarFeedInfo(FeedUrl(baseUrl, protector.Unprotect(user.CalendarFeedTokenProtected)), user.CalendarFeedCreatedAtUtc.Value);
    }

    /// <summary>Gera um token novo. O anterior, se havia, para de funcionar na hora.</summary>
    public async Task<CalendarFeedInfo> RegenerateAsync(string userId, string baseUrl, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
        var token = NewToken();
        var now = clock.GetUtcNow().UtcDateTime;
        user.CalendarFeedTokenHash = Hash(token);
        user.CalendarFeedTokenProtected = protector.Protect(token);
        user.CalendarFeedCreatedAtUtc = now;
        await db.SaveChangesAsync(ct);
        return new CalendarFeedInfo(FeedUrl(baseUrl, token), now);
    }

    public async Task DisableAsync(string userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
        user.CalendarFeedTokenHash = null;
        user.CalendarFeedTokenProtected = null;
        user.CalendarFeedCreatedAtUtc = null;
        await db.SaveChangesAsync(ct);
    }

    /// <returns>O calendário em texto iCalendar, ou null quando o token não é de ninguém.</returns>
    public async Task<string?> BuildAsync(string token, string baseUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128 || !token.StartsWith(TokenPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var hash = Hash(token);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var userId = await db.Users.AsNoTracking()
            .Where(u => u.CalendarFeedTokenHash == hash)
            .Select(u => u.Id)
            .FirstOrDefaultAsync(ct);
        if (userId is null)
        {
            return null;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var doneCutoff = now - DoneRetention;
        var items = await db.WorkItems.AsNoTracking()
            .Where(w => w.AssigneeId == userId
                        && (w.DueDateUtc != null || w.StartDateUtc != null)
                        && (w.Status.Category != StatusCategory.Done || (w.UpdatedAtUtc ?? w.CreatedAtUtc) >= doneCutoff))
            .Select(w => new FeedItem(
                w.Id, w.Number, w.Title, w.Description, w.StartDateUtc, w.DueDateUtc, w.Status.Name, w.Status.Category,
                w.TaskListId, w.TaskList.Name, w.TaskList.Space.Workspace.Name, w.UpdatedAtUtc ?? w.CreatedAtUtc))
            .ToListAsync(ct);

        return Render(items, baseUrl, now);
    }

    public sealed record FeedItem(
        Guid Id, int Number, string Title, string? Description, DateTime? StartDateUtc, DateTime? DueDateUtc,
        string StatusName, StatusCategory StatusCategory, Guid TaskListId, string ListName, string WorkspaceName, DateTime UpdatedAtUtc);

    // iCalendar (RFC 5545): eventos de dia inteiro, DTEND exclusivo (o dia seguinte ao prazo).
    public static string Render(IEnumerable<FeedItem> items, string baseUrl, DateTime nowUtc)
    {
        var host = Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ? uri.Host : "nexus";
        var sb = new StringBuilder();
        Line(sb, "BEGIN:VCALENDAR");
        Line(sb, "VERSION:2.0");
        Line(sb, "PRODID:-//Aamora//Nexus//PT-BR");
        Line(sb, "CALSCALE:GREGORIAN");
        Line(sb, "METHOD:PUBLISH");
        Line(sb, "X-WR-CALNAME:Nexus · Minhas tarefas");
        Line(sb, "REFRESH-INTERVAL;VALUE=DURATION:PT1H");
        Line(sb, "X-PUBLISHED-TTL:PT1H");

        foreach (var item in items.OrderBy(i => i.DueDateUtc ?? i.StartDateUtc))
        {
            var start = (item.StartDateUtc ?? item.DueDateUtc)!.Value.Date;
            var end = (item.DueDateUtc ?? item.StartDateUtc)!.Value.Date;
            if (end < start)
            {
                end = start;
            }

            var code = WorkItem.FormatCode(item.Number);
            var summary = item.StatusCategory == StatusCategory.Done ? $"✓ {code} {item.Title}" : $"{code} {item.Title}";
            var description = $"{item.WorkspaceName} · {item.ListName} · {item.StatusName}";
            if (!string.IsNullOrWhiteSpace(item.Description))
            {
                var text = item.Description.Trim();
                description += "\n\n" + (text.Length > MaxDescriptionLength ? text[..MaxDescriptionLength] + "…" : text);
            }

            Line(sb, "BEGIN:VEVENT");
            Line(sb, $"UID:{item.Id:D}@{host}");
            Line(sb, $"DTSTAMP:{nowUtc:yyyyMMdd'T'HHmmss'Z'}");
            Line(sb, $"LAST-MODIFIED:{item.UpdatedAtUtc:yyyyMMdd'T'HHmmss'Z'}");
            Line(sb, $"DTSTART;VALUE=DATE:{start:yyyyMMdd}");
            Line(sb, $"DTEND;VALUE=DATE:{end.AddDays(1):yyyyMMdd}");
            Line(sb, "SUMMARY:" + Escape(summary));
            Line(sb, "DESCRIPTION:" + Escape(description));
            Line(sb, "URL:" + $"{baseUrl.TrimEnd('/')}/lists/{item.TaskListId}/list");
            Line(sb, "CATEGORIES:" + Escape(item.WorkspaceName));
            Line(sb, item.StatusCategory == StatusCategory.Done ? "STATUS:CONFIRMED" : "STATUS:TENTATIVE");
            Line(sb, "TRANSP:TRANSPARENT");
            Line(sb, "END:VEVENT");
        }

        Line(sb, "END:VCALENDAR");
        return sb.ToString();
    }

    private static string Escape(string value) => value
        .Replace("\\", "\\\\")
        .Replace(";", "\\;")
        .Replace(",", "\\,")
        .Replace("\r\n", "\n")
        .Replace("\n", "\\n");

    // Linhas de no máximo 75 octetos, continuadas com espaço (RFC 5545 §3.1); CRLF obrigatório.
    private static void Line(StringBuilder sb, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        if (bytes.Length <= 75)
        {
            sb.Append(content).Append("\r\n");
            return;
        }

        var position = 0;
        var first = true;
        while (position < content.Length)
        {
            var limit = first ? 75 : 74;
            var length = 0;
            var octets = 0;
            while (position + length < content.Length)
            {
                var c = content[position + length];
                var size = char.IsSurrogate(c) ? 4 : Encoding.UTF8.GetByteCount([c]);
                var step = char.IsHighSurrogate(c) ? 2 : 1;
                if (octets + size > limit)
                {
                    break;
                }

                octets += size;
                length += step;
            }

            sb.Append(first ? "" : " ").Append(content, position, length).Append("\r\n");
            position += length;
            first = false;
        }
    }
}
