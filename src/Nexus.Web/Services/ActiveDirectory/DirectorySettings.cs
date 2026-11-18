using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;

namespace Nexus.Web.Services.ActiveDirectory;

/// <summary>
/// Configuração do login pelo Active Directory (#70, fase 1), por instância. Guardada em
/// <c>InstanceSettings</c> como JSON; a senha da conta de serviço só existe cifrada pelo Data
/// Protection (<see cref="BindPasswordProtected"/>) e nunca volta para a tela.
/// </summary>
public sealed record DirectorySettings
{
    public const string DefaultUserFilter = "(&(objectClass=user)(|(mail={0})(userPrincipalName={0})))";

    public bool Enabled { get; init; }
    public string Host { get; init; } = "";
    public int Port { get; init; } = 636;
    /// <summary>LDAPS. Desligar só em laboratório: a senha da pessoa viaja no bind.</summary>
    public bool UseSsl { get; init; } = true;
    /// <summary>Aceita certificado autoassinado do controlador de domínio.</summary>
    public bool AllowSelfSignedCertificate { get; init; }
    public string BaseDn { get; init; } = "";
    /// <summary>Conta de serviço usada para procurar a pessoa antes do bind com a senha dela.</summary>
    public string BindDn { get; init; } = "";
    public string? BindPasswordProtected { get; init; }
    /// <summary><c>{0}</c> recebe o e-mail digitado, já escapado conforme a RFC 4515.</summary>
    public string UserFilter { get; init; } = DefaultUserFilter;
    public string DisplayNameAttribute { get; init; } = "displayName";
    public string EmailAttribute { get; init; } = "mail";
    public string UserNameAttribute { get; init; } = "sAMAccountName";
    /// <summary>Domínios de e-mail que entram pelo AD (ex.: acme.com.br). Fora deles, login normal.</summary>
    public List<string> EmailDomains { get; init; } = [];

    public bool HasBindPassword => !string.IsNullOrEmpty(BindPasswordProtected);

    public bool HandlesEmail(string? email)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var at = email.LastIndexOf('@');
        if (at < 0)
        {
            return false;
        }

        var domain = email[(at + 1)..].Trim();
        return EmailDomains.Any(d => string.Equals(d.Trim(), domain, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>O que impede a configuração de funcionar, em texto para a tela; null = pronta.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Host)) return "Informe o servidor.";
        if (Port is < 1 or > 65535) return "Porta inválida.";
        if (string.IsNullOrWhiteSpace(BaseDn)) return "Informe o DN base da busca.";
        if (string.IsNullOrWhiteSpace(BindDn)) return "Informe a conta de serviço.";
        if (!HasBindPassword) return "Informe a senha da conta de serviço.";
        if (!UserFilter.Contains("{0}", StringComparison.Ordinal)) return "O filtro precisa conter {0}, que recebe o e-mail.";
        if (EmailDomains.Count == 0) return "Informe ao menos um domínio de e-mail.";
        return null;
    }
}

/// <summary>
/// Lê e grava a configuração do diretório. Cache em memória: o login consulta a cada tentativa,
/// e a configuração muda uma vez na vida da instância.
/// </summary>
public sealed class DirectorySettingsStore(IDbContextFactory<AppDbContext> dbFactory, IDataProtectionProvider dataProtection, TimeProvider? timeProvider = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IDataProtector protector = dataProtection.CreateProtector("Nexus.Directory.BindPassword.v1");
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim gate = new(1, 1);
    private DirectorySettings? cached;

    public async Task<DirectorySettings> GetAsync(CancellationToken ct = default)
    {
        if (cached is not null)
        {
            return cached;
        }

        await gate.WaitAsync(ct);
        try
        {
            if (cached is not null)
            {
                return cached;
            }

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var row = await db.InstanceSettings.FindAsync([InstanceSettingKeys.DirectoryLdap], ct);
            cached = row is null ? new DirectorySettings() : JsonSerializer.Deserialize<DirectorySettings>(row.Value, Json) ?? new DirectorySettings();
            return cached;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Grava a configuração. Senha nova em <paramref name="bindPassword"/>; null ou vazia mantém a
    /// que já estava cifrada.
    /// </summary>
    public async Task<DirectorySettings> SaveAsync(DirectorySettings settings, string? bindPassword, CancellationToken ct = default)
    {
        var current = await GetAsync(ct);
        var toSave = settings with
        {
            Host = settings.Host.Trim(),
            BaseDn = settings.BaseDn.Trim(),
            BindDn = settings.BindDn.Trim(),
            UserFilter = string.IsNullOrWhiteSpace(settings.UserFilter) ? DirectorySettings.DefaultUserFilter : settings.UserFilter.Trim(),
            DisplayNameAttribute = string.IsNullOrWhiteSpace(settings.DisplayNameAttribute) ? "displayName" : settings.DisplayNameAttribute.Trim(),
            EmailAttribute = string.IsNullOrWhiteSpace(settings.EmailAttribute) ? "mail" : settings.EmailAttribute.Trim(),
            UserNameAttribute = string.IsNullOrWhiteSpace(settings.UserNameAttribute) ? "sAMAccountName" : settings.UserNameAttribute.Trim(),
            EmailDomains = settings.EmailDomains.Select(d => d.Trim().TrimStart('@').ToLowerInvariant()).Where(d => d.Length > 0).Distinct().ToList(),
            BindPasswordProtected = string.IsNullOrEmpty(bindPassword) ? current.BindPasswordProtected : protector.Protect(bindPassword),
        };

        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var json = JsonSerializer.Serialize(toSave, Json);
            var row = await db.InstanceSettings.FindAsync([InstanceSettingKeys.DirectoryLdap], ct);
            if (row is null)
            {
                db.InstanceSettings.Add(new InstanceSetting { Key = InstanceSettingKeys.DirectoryLdap, Value = json, UpdatedAtUtc = now });
            }
            else
            {
                row.Value = json;
                row.UpdatedAtUtc = now;
            }

            await db.SaveChangesAsync(ct);
        }

        cached = toSave;
        return toSave;
    }

    public string? UnprotectBindPassword(DirectorySettings settings) =>
        settings.BindPasswordProtected is null ? null : protector.Unprotect(settings.BindPasswordProtected);

    /// <summary>Esquece o cache (testes e troca de instância).</summary>
    public void Invalidate() => cached = null;
}
