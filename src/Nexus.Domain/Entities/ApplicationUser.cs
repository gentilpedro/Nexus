using Microsoft.AspNetCore.Identity;

namespace Nexus.Domain.Entities;

public class ApplicationUser : IdentityUser
{
    // Marked [PersonalData] so it's included in the Identity "download my data" export
    // alongside the base UserName/Email/PhoneNumber (LGPD art. 18, VI - portabilidade).
    [PersonalData]
    public string DisplayName { get; set; } = "";

    // Relative path under the uploads root, same convention as WorkItemAttachment.StoragePath.
    public string? AvatarStoragePath { get; set; }
    public string? AvatarContentType { get; set; }

    // Feed iCalendar (#68). O token vai na URL de assinatura: o hash acha a conta na requisição
    // anônima do calendário, a cópia cifrada deixa "Minha conta" mostrar a URL de novo. Null = sem
    // feed. Não é [PersonalData]: é uma credencial, não um dado da pessoa.
    public string? CalendarFeedTokenHash { get; set; }
    public string? CalendarFeedTokenProtected { get; set; }
    public DateTime? CalendarFeedCreatedAtUtc { get; set; }
}
