namespace Nexus.Web.Services.ActiveDirectory;

/// <summary>Uma pessoa como o AD a descreve. <see cref="Disabled"/> vem de userAccountControl.</summary>
public sealed record DirectoryUser(string DistinguishedName, string? ObjectId, string? DisplayName, string? Email, string? UserName, bool Disabled);

public sealed record LdapConnectionTest(bool Ok, string Message);

/// <summary>
/// O que o Nexus pede ao LDAP, e só isso: achar uma pessoa pelo e-mail com a conta de serviço e
/// conferir a senha dela com um bind. A implementação real usa System.DirectoryServices.Protocols;
/// os testes usam uma falsa.
/// </summary>
public interface ILdapDirectory
{
    Task<LdapConnectionTest> TestAsync(DirectorySettings settings, string bindPassword, CancellationToken ct = default);

    Task<DirectoryUser?> FindUserAsync(DirectorySettings settings, string bindPassword, string email, CancellationToken ct = default);

    /// <summary>Bind com o DN da pessoa e a senha digitada. Falso para senha errada ou DN inexistente.</summary>
    Task<bool> AuthenticateAsync(DirectorySettings settings, string distinguishedName, string password, CancellationToken ct = default);
}

public static class LdapFilter
{
    /// <summary>Escapa um valor para dentro de um filtro LDAP (RFC 4515), para o e-mail digitado não virar filtro.</summary>
    public static string Escape(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
        {
            sb.Append(c switch
            {
                '\\' => "\\5c",
                '*' => "\\2a",
                '(' => "\\28",
                ')' => "\\29",
                '\0' => "\\00",
                _ => c.ToString(),
            });
        }

        return sb.ToString();
    }

    public static string Build(DirectorySettings settings, string email) =>
        settings.UserFilter.Replace("{0}", Escape(email.Trim()), StringComparison.Ordinal);
}
