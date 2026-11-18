using Microsoft.AspNetCore.Identity;
using Nexus.Domain.Entities;

namespace Nexus.Web.Services.ActiveDirectory;

public enum DirectoryLoginStatus
{
    /// <summary>O e-mail não é de um domínio do AD (ou o AD está desligado): segue o login normal.</summary>
    NotHandled,
    Success,
    /// <summary>Pessoa não encontrada ou senha errada. Uma mensagem só, para não revelar qual.</summary>
    InvalidCredentials,
    /// <summary>Conta desabilitada no AD: não entra nem com a senha certa.</summary>
    Disabled,
    /// <summary>O AD aceitou, mas o Nexus não conseguiu criar a conta (ex.: faixa da licença cheia).</summary>
    AccountRefused,
    /// <summary>Servidor fora, configuração incompleta: a pessoa vê "tente mais tarde", o log diz o porquê.</summary>
    Unavailable,
}

public sealed record DirectoryLoginResult(DirectoryLoginStatus Status, ApplicationUser? User = null, string? Detail = null, bool Created = false);

/// <summary>
/// Login pelo Active Directory (#70, fase 1). Quem tem e-mail de um domínio configurado é
/// autenticado por bind LDAP com a senha do AD; a senha nunca é guardada. Na primeira entrada a
/// conta do Nexus nasce com nome e e-mail do AD, marcada como gerenciada; se já existe conta com
/// o mesmo e-mail, ela é vinculada. É o único lugar que cria ou vincula contas do diretório.
/// </summary>
public sealed class DirectoryLoginService(
    DirectorySettingsStore store,
    ILdapDirectory ldap,
    UserManager<ApplicationUser> userManager,
    AuditLogService audit,
    ILogger<DirectoryLoginService> logger)
{
    public async Task<bool> HandlesAsync(string? email, CancellationToken ct = default) =>
        (await store.GetAsync(ct)).HandlesEmail(email);

    public async Task<DirectoryLoginResult> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var settings = await store.GetAsync(ct);
        if (!settings.HandlesEmail(email))
        {
            return new(DirectoryLoginStatus.NotHandled);
        }

        var bindPassword = store.UnprotectBindPassword(settings);
        var problem = settings.Validate();
        if (problem is not null || bindPassword is null)
        {
            logger.LogError("Login pelo AD recusado: configuração incompleta ({Problem}).", problem ?? "sem senha da conta de serviço");
            return new(DirectoryLoginStatus.Unavailable, Detail: problem);
        }

        DirectoryUser? person;
        bool authenticated;
        try
        {
            person = await ldap.FindUserAsync(settings, bindPassword, email, ct);
            if (person is null)
            {
                return new(DirectoryLoginStatus.InvalidCredentials);
            }

            if (person.Disabled)
            {
                logger.LogInformation("Login pelo AD recusado para {Email}: conta desabilitada no diretório.", email);
                return new(DirectoryLoginStatus.Disabled);
            }

            authenticated = await ldap.AuthenticateAsync(settings, person.DistinguishedName, password, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Login pelo AD indisponível para {Email}.", email);
            return new(DirectoryLoginStatus.Unavailable, Detail: ex.Message);
        }

        if (!authenticated)
        {
            return new(DirectoryLoginStatus.InvalidCredentials);
        }

        return await ProvisionAsync(person, email, ct);
    }

    /// <summary>O mesmo caminho do login, parando antes de criar ou vincular conta: para a tela de teste.</summary>
    public async Task<DirectoryLoginResult> ProbeAsync(string email, string password, CancellationToken ct = default)
    {
        var settings = await store.GetAsync(ct);
        if (!settings.HandlesEmail(email))
        {
            return new(DirectoryLoginStatus.NotHandled);
        }

        var bindPassword = store.UnprotectBindPassword(settings);
        var problem = settings.Validate();
        if (problem is not null || bindPassword is null)
        {
            return new(DirectoryLoginStatus.Unavailable, Detail: problem ?? "sem senha da conta de serviço");
        }

        try
        {
            var person = await ldap.FindUserAsync(settings, bindPassword, email, ct);
            if (person is null)
            {
                return new(DirectoryLoginStatus.InvalidCredentials);
            }

            if (person.Disabled)
            {
                return new(DirectoryLoginStatus.Disabled);
            }

            var ok = await ldap.AuthenticateAsync(settings, person.DistinguishedName, password, ct);
            return ok
                ? new(DirectoryLoginStatus.Success, Detail: $"{person.DisplayName ?? person.UserName} ({person.DistinguishedName})")
                : new(DirectoryLoginStatus.InvalidCredentials);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(DirectoryLoginStatus.Unavailable, Detail: ex.Message);
        }
    }

    /// <summary>Acha a conta pelo id do AD, depois pelo e-mail; cria se não houver. Sempre deixa marcada como gerenciada.</summary>
    private async Task<DirectoryLoginResult> ProvisionAsync(DirectoryUser person, string typedEmail, CancellationToken ct)
    {
        var email = string.IsNullOrWhiteSpace(person.Email) ? typedEmail.Trim() : person.Email.Trim();
        ApplicationUser? user = null;
        if (person.ObjectId is not null)
        {
            user = userManager.Users.FirstOrDefault(u => u.DirectoryIdentifier == person.ObjectId);
        }

        user ??= await userManager.FindByEmailAsync(email);
        var created = false;
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = DisplayNameFor(person, email),
                DirectoryManaged = true,
                DirectoryIdentifier = person.ObjectId,
                DirectoryLastSyncAtUtc = DateTime.UtcNow,
            };
            var result = await userManager.CreateAsync(user);
            if (!result.Succeeded)
            {
                var why = string.Join(" ", result.Errors.Select(e => e.Description));
                logger.LogWarning("O AD autenticou {Email}, mas a conta não foi criada: {Why}", email, why);
                return new(DirectoryLoginStatus.AccountRefused, Detail: why);
            }

            created = true;
            await audit.LogAsync(user.Id, AuditAction.DirectoryAccountProvisioned, user.Id, $"{email} criada no primeiro login pelo Active Directory", ct);
        }
        else
        {
            var changed = !user.DirectoryManaged || user.DirectoryIdentifier != person.ObjectId || !user.EmailConfirmed;
            var newName = DisplayNameFor(person, email);
            if (!string.IsNullOrWhiteSpace(person.DisplayName) && user.DisplayName != newName)
            {
                user.DisplayName = newName;
                changed = true;
            }

            var wasManaged = user.DirectoryManaged;
            user.DirectoryManaged = true;
            user.DirectoryIdentifier = person.ObjectId ?? user.DirectoryIdentifier;
            user.EmailConfirmed = true;
            user.DirectoryLastSyncAtUtc = DateTime.UtcNow;
            if (changed)
            {
                await userManager.UpdateAsync(user);
            }

            if (!wasManaged)
            {
                await audit.LogAsync(user.Id, AuditAction.DirectoryAccountProvisioned, user.Id, $"{email} vinculada ao Active Directory no login", ct);
            }
        }

        return new(DirectoryLoginStatus.Success, user, Created: created);
    }

    private static string DisplayNameFor(DirectoryUser person, string email) =>
        !string.IsNullOrWhiteSpace(person.DisplayName) ? person.DisplayName.Trim()
        : !string.IsNullOrWhiteSpace(person.UserName) ? person.UserName.Trim()
        : email[..email.IndexOf('@')];
}
