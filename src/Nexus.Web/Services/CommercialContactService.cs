using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;

namespace Nexus.Web.Services;

/// <summary>
/// Bound from the "Commercial" configuration section: the inbox that receives Business-plan
/// contact requests. Falls back to the support inbox when empty.
/// </summary>
public sealed class CommercialOptions
{
    public string? Email { get; set; }
}

/// <summary>What the public "talk to us" form submits (#56).</summary>
public sealed record CommercialContactRequest(
    string Name,
    string Company,
    string Email,
    string TeamSize,
    string? Message,
    string? Website);

public enum CommercialContactResult
{
    /// <summary>Stored and forwarded to the commercial inbox.</summary>
    Received,

    /// <summary>Stored, but the e-mail to the team could not be delivered.</summary>
    ReceivedWithoutEmail,

    /// <summary>The hidden honeypot field was filled in: a bot. Nothing stored, nothing sent.</summary>
    Ignored,
}

/// <summary>
/// Records a Business-plan contact request and e-mails it to the commercial inbox. The request is
/// stored before the e-mail goes out, so a delivery failure never loses a lead.
/// </summary>
public class CommercialContactService(
    IDbContextFactory<AppDbContext> dbFactory,
    Mailer mailer,
    IOptions<CommercialOptions> commercial,
    IOptions<SupportOptions> support,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public static readonly IReadOnlyList<string> TeamSizes = ["Até 10 pessoas", "11 a 50 pessoas", "51 a 200 pessoas", "Mais de 200 pessoas"];

    public async Task<CommercialContactResult> SubmitAsync(CommercialContactRequest request, CancellationToken cancellationToken = default)
    {
        // Real people never see the honeypot field (it is hidden and skipped by the keyboard);
        // form-filling bots fill every input they find. Answering "received" keeps the bot from
        // learning which field gave it away.
        if (!string.IsNullOrWhiteSpace(request.Website))
        {
            return CommercialContactResult.Ignored;
        }

        var contact = new CommercialContact
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Company = request.Company.Trim(),
            Email = request.Email.Trim(),
            TeamSize = request.TeamSize.Trim(),
            Message = string.IsNullOrWhiteSpace(request.Message) ? null : request.Message.Trim(),
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
        };

        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            db.CommercialContacts.Add(contact);
            await db.SaveChangesAsync(cancellationToken);
        }

        var inbox = new[] { commercial.Value.Email, support.Value.Email }
            .Select(e => e?.Trim())
            .FirstOrDefault(e => !string.IsNullOrEmpty(e));
        if (string.IsNullOrEmpty(inbox))
        {
            return CommercialContactResult.ReceivedWithoutEmail;
        }

        var content = EmailTemplate.Render(
            title: "Novo contato sobre o plano Business",
            preview: $"{contact.Name}, da {contact.Company}, quer falar sobre o plano Business.",
            paragraphs:
            [
                "Alguém preencheu o formulário de contato comercial do Nexus. Responda direto para o e-mail abaixo.",
                .. contact.Message is null ? Array.Empty<string>() : [contact.Message],
            ],
            details:
            [
                new EmailDetail("Nome", contact.Name),
                new EmailDetail("Empresa", contact.Company),
                new EmailDetail("E-mail", contact.Email),
                new EmailDetail("Tamanho do time", contact.TeamSize),
            ]);

        var sent = await mailer.SendAsync(inbox, $"Contato comercial: {contact.Company}", content.Html, content.Text);

        return sent ? CommercialContactResult.Received : CommercialContactResult.ReceivedWithoutEmail;
    }
}
