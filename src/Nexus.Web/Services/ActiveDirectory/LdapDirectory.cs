using System.DirectoryServices.Protocols;
using System.Net;

namespace Nexus.Web.Services.ActiveDirectory;

/// <summary>
/// LDAP de verdade, com System.DirectoryServices.Protocols (funciona no container Linux com a
/// libldap instalada; ver Dockerfile). Bind simples sobre LDAPS: o AD recusa bind simples sem TLS
/// por padrão, e a senha da pessoa não pode viajar em claro de qualquer forma.
/// </summary>
public sealed class LdapDirectory(ILogger<LdapDirectory> logger) : ILdapDirectory
{
    private const int AccountDisabledFlag = 0x2;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public Task<LdapConnectionTest> TestAsync(DirectorySettings settings, string bindPassword, CancellationToken ct = default) => Task.Run(() =>
    {
        try
        {
            using var connection = Connect(settings, settings.BindDn, bindPassword);
            var request = new SearchRequest(settings.BaseDn, "(objectClass=*)", SearchScope.Base, "objectClass");
            request.SizeLimit = 1;
            var response = (SearchResponse)connection.SendRequest(request);
            return new LdapConnectionTest(true, $"Conectado a {settings.Host}:{settings.Port} e o DN base respondeu ({response.Entries.Count} entrada).");
        }
        catch (LdapException ex)
        {
            return new LdapConnectionTest(false, Describe(ex));
        }
        catch (Exception ex) when (ex is DirectoryOperationException or InvalidOperationException or PlatformNotSupportedException or System.Net.Sockets.SocketException)
        {
            return new LdapConnectionTest(false, ex.Message);
        }
    }, ct);

    public Task<DirectoryUser?> FindUserAsync(DirectorySettings settings, string bindPassword, string email, CancellationToken ct = default) => Task.Run(() =>
    {
        using var connection = Connect(settings, settings.BindDn, bindPassword);
        var attributes = new[] { settings.DisplayNameAttribute, settings.EmailAttribute, settings.UserNameAttribute, "objectGUID", "userAccountControl" };
        var request = new SearchRequest(settings.BaseDn, LdapFilter.Build(settings, email), SearchScope.Subtree, attributes);
        request.SizeLimit = 2;
        var response = (SearchResponse)connection.SendRequest(request);
        if (response.Entries.Count == 0)
        {
            return null;
        }

        if (response.Entries.Count > 1)
        {
            logger.LogWarning("LDAP: o filtro devolveu mais de uma pessoa para {Email}; recusando por ambiguidade.", email);
            return null;
        }

        var entry = response.Entries[0];
        var uac = int.TryParse(Text(entry, "userAccountControl"), out var flags) ? flags : 0;
        var guidBytes = entry.Attributes["objectGUID"]?.GetValues(typeof(byte[])).OfType<byte[]>().FirstOrDefault();
        return new DirectoryUser(
            entry.DistinguishedName,
            guidBytes is { Length: 16 } ? new Guid(guidBytes).ToString() : null,
            Text(entry, settings.DisplayNameAttribute),
            Text(entry, settings.EmailAttribute),
            Text(entry, settings.UserNameAttribute),
            (uac & AccountDisabledFlag) != 0);
    }, ct);

    public Task<bool> AuthenticateAsync(DirectorySettings settings, string distinguishedName, string password, CancellationToken ct = default) => Task.Run(() =>
    {
        if (string.IsNullOrEmpty(password))
        {
            // Bind com senha vazia é bind anônimo e "passa" em muitos servidores. Nunca.
            return false;
        }

        try
        {
            using var connection = Connect(settings, distinguishedName, password);
            return true;
        }
        catch (LdapException ex) when (ex.ErrorCode == 49)
        {
            // 49 = invalidCredentials: senha errada, conta bloqueada ou expirada no AD.
            return false;
        }
    }, ct);

    private static LdapConnection Connect(DirectorySettings settings, string bindDn, string password)
    {
        var identifier = new LdapDirectoryIdentifier(settings.Host, settings.Port, fullyQualifiedDnsHostName: true, connectionless: false);
        var connection = new LdapConnection(identifier)
        {
            AuthType = AuthType.Basic,
            Credential = new NetworkCredential(bindDn, password),
            Timeout = Timeout,
        };
        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.SecureSocketLayer = settings.UseSsl;
        if (settings.UseSsl && settings.AllowSelfSignedCertificate)
        {
            try
            {
                connection.SessionOptions.VerifyServerCertificate = (_, _) => true;
            }
            catch (PlatformNotSupportedException)
            {
                // Nesta plataforma a validação fica com a libldap (TLS_REQCERT); nada a fazer aqui.
            }
        }

        try
        {
            connection.Bind();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static string? Text(SearchResultEntry entry, string attribute)
    {
        var values = entry.Attributes[attribute];
        if (values is null || values.Count == 0)
        {
            return null;
        }

        return values[0] switch
        {
            string s => s,
            byte[] b => System.Text.Encoding.UTF8.GetString(b),
            var other => other?.ToString(),
        };
    }

    private static string Describe(LdapException ex) => ex.ErrorCode switch
    {
        49 => "Conta de serviço ou senha recusadas pelo servidor (invalidCredentials).",
        81 => "Servidor não respondeu (serverDown): confira host, porta e se é LDAPS.",
        _ => $"Erro LDAP {ex.ErrorCode}: {ex.Message}",
    };
}
