using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Nexus.Domain.Entities;

namespace Nexus.Web.Services.Licensing;

/// <summary>
/// O SignInManager de sempre, mais o carimbo de último login (#81). Senha, 2FA, código de
/// recuperação, passkey e login externo terminam todos em <see cref="SignInWithClaimsAsync"/>,
/// então é aqui, e só aqui, que a conta vira "ativa" para a faixa da licença.
/// </summary>
public sealed class LicenseAwareSignInManager(
    UserManager<ApplicationUser> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<ApplicationUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<ApplicationUser> confirmation,
    LicenseService license)
    : SignInManager<ApplicationUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    /// <summary>
    /// Entrada de quem o Active Directory já autenticou (#70): o mesmo caminho do login por senha
    /// depois da senha conferida, então o 2FA do Nexus continua valendo se a pessoa o ativou.
    /// </summary>
    public Task<SignInResult> DirectorySignInAsync(ApplicationUser user, bool isPersistent) =>
        SignInOrTwoFactorAsync(user, isPersistent, loginProvider: null, bypassTwoFactor: false);

    public override async Task SignInWithClaimsAsync(ApplicationUser user, AuthenticationProperties? authenticationProperties, IEnumerable<Claim> additionalClaims)
    {
        await base.SignInWithClaimsAsync(user, authenticationProperties, additionalClaims);
        try
        {
            await license.RecordLoginAsync(user.Id);
        }
        catch (Exception ex)
        {
            // O login já aconteceu; o carimbo é estatística da licença, não pode derrubar a entrada.
            Logger.LogWarning(ex, "Não consegui gravar o último login de {UserId}.", user.Id);
        }
    }
}
