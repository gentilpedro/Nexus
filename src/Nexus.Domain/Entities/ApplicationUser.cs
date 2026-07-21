using Microsoft.AspNetCore.Identity;

namespace Nexus.Domain.Entities;

public class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = "";

    // Relative path under the uploads root, same convention as WorkItemAttachment.StoragePath.
    public string? AvatarStoragePath { get; set; }
    public string? AvatarContentType { get; set; }
}
