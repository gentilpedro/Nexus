using Microsoft.AspNetCore.Identity;
using Nexus.Domain.Entities;
using Nexus.Web.Services;

namespace Nexus.Web.Components.Account;

// Sends transactional Identity emails (confirmation, password reset) through Brevo — free tier
// covers 300 emails/day, well above what account confirmation and password resets need at this
// stage.
public sealed class BrevoEmailSender(BrevoMailer mailer) : IEmailSender<ApplicationUser>
{
    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
        mailer.SendAsync(email, "Confirme seu cadastro no Nexus", $"<p>Confirme sua conta <a href='{confirmationLink}'>clicando aqui</a>.</p>");

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
        mailer.SendAsync(email, "Redefinir sua senha do Nexus", $"<p>Redefina sua senha <a href='{resetLink}'>clicando aqui</a>.</p>");

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
        mailer.SendAsync(email, "Redefinir sua senha do Nexus", $"<p>Use o código a seguir para redefinir sua senha: <strong>{resetCode}</strong></p>");
}
