using Microsoft.AspNetCore.Identity;
using Nexus.Domain.Entities;

namespace Nexus.Web.Services.Licensing;

/// <summary>
/// Faixa de usuários da licença (#81): com a faixa cheia, nenhuma conta nova é criada, por
/// nenhum caminho (cadastro, convite, login externo). Entra como validador do Identity porque é
/// o único ponto por onde todo <c>UserManager.CreateAsync</c> passa. Quem já existe não é tocado.
/// </summary>
public sealed class LicenseUserValidator(LicenseService license) : IUserValidator<ApplicationUser>
{
    public const string ErrorCode = "LicenseUserLimit";

    public async Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user)
    {
        if (license.CanCreateAccount)
        {
            return IdentityResult.Success;
        }

        // O validador também roda em UpdateAsync; só a criação é barrada.
        var exists = await manager.FindByIdAsync(user.Id) is not null;
        if (exists)
        {
            return IdentityResult.Success;
        }

        var limit = license.Current.MaxActiveUsers;
        return IdentityResult.Failed(new IdentityError
        {
            Code = ErrorCode,
            Description = $"A licença desta instalação permite {limit} usuários ativos e o limite foi atingido. Fale com o administrador do Nexus.",
        });
    }
}
