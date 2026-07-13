using Microsoft.AspNetCore.Identity;

namespace Nexus.Domain.Entities;

public class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = "";
}
