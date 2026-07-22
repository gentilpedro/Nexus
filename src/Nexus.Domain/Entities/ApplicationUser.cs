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
}
