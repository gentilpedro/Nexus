using Microsoft.AspNetCore.Identity;
using Nexus.Domain.Entities;
using Nexus.Web.Services;

namespace Nexus.Web.Components.Account;

// Sends transactional Identity emails (confirmation, password reset) through Brevo — free tier
// covers 300 emails/day, well above what account confirmation and password resets need at this
// stage. Layout comes from EmailTemplate so these match the invite e-mail.
public sealed class BrevoEmailSender(BrevoMailer mailer) : IEmailSender<ApplicationUser>
{
    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
    {
        var content = EmailTemplate.Render(
            title: "Confirme seu cadastro",
            preview: "Falta um passo para começar a usar o Nexus.",
            paragraphs:
            [
                "Sua conta no Nexus foi criada. Confirme este endereço de e-mail para poder entrar.",
            ],
            button: new EmailButton("Confirmar meu e-mail", confirmationLink),
            footnote: "Se você não criou esta conta, ignore este e-mail — nada será ativado.");

        return mailer.SendAsync(email, "Confirme seu cadastro no Nexus", content.Html, content.Text);
    }

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
    {
        var content = EmailTemplate.Render(
            title: "Redefinir sua senha",
            preview: "Link para criar uma nova senha no Nexus.",
            paragraphs:
            [
                "Recebemos um pedido para redefinir a senha da sua conta no Nexus.",
            ],
            button: new EmailButton("Criar nova senha", resetLink),
            footnote: "Se não foi você quem pediu, ignore este e-mail: sua senha atual continua valendo.");

        return mailer.SendAsync(email, "Redefinir sua senha do Nexus", content.Html, content.Text);
    }

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
    {
        var content = EmailTemplate.Render(
            title: "Redefinir sua senha",
            preview: "Seu código para redefinir a senha no Nexus.",
            paragraphs:
            [
                "Use o código abaixo para concluir a redefinição da sua senha.",
            ],
            details: [new EmailDetail("Código", resetCode)],
            footnote: "Se não foi você quem pediu, ignore este e-mail: sua senha atual continua valendo.");

        return mailer.SendAsync(email, "Redefinir sua senha do Nexus", content.Html, content.Text);
    }
}
