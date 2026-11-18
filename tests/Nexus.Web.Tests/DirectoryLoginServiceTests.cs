using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Domain.Entities;
using Nexus.Web.Services;
using Nexus.Web.Services.ActiveDirectory;

namespace Nexus.Web.Tests;

public class DirectoryLoginServiceTests
{
    // Um AD de mentira: quem está no dicionário existe; a senha certa é "<usuário>-senha".
    private sealed class FakeLdap : ILdapDirectory
    {
        public Dictionary<string, DirectoryUser> People { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool Down { get; set; }
        public int Binds { get; private set; }

        public Task<LdapConnectionTest> TestAsync(DirectorySettings settings, string bindPassword, CancellationToken ct = default) =>
            Task.FromResult(new LdapConnectionTest(!Down, Down ? "fora" : "ok"));

        public Task<DirectoryUser?> FindUserAsync(DirectorySettings settings, string bindPassword, string email, CancellationToken ct = default)
        {
            if (Down) throw new InvalidOperationException("servidor fora");
            Assert.Equal("segredo-do-svc", bindPassword);
            return Task.FromResult(People.GetValueOrDefault(email));
        }

        public Task<bool> AuthenticateAsync(DirectorySettings settings, string distinguishedName, string password, CancellationToken ct = default)
        {
            Binds++;
            var person = People.Values.FirstOrDefault(p => p.DistinguishedName == distinguishedName);
            return Task.FromResult(person is not null && password == person.UserName + "-senha");
        }
    }

    private static readonly DirectorySettings Settings = new()
    {
        Enabled = true, Host = "dc01.acme.local", BaseDn = "DC=acme,DC=local", BindDn = "svc-nexus@acme.local",
        EmailDomains = ["acme.com.br"],
    };

    private static async Task<(DirectoryLoginService Service, FakeLdap Ldap, TestIdentity.Setup Identity, DirectorySettingsStore Store)> BuildAsync(bool enabled = true)
    {
        var identity = TestIdentity.Create();
        var store = new DirectorySettingsStore(identity.Factory, new EphemeralDataProtectionProvider());
        await store.SaveAsync(Settings with { Enabled = enabled }, "segredo-do-svc");
        var ldap = new FakeLdap();
        ldap.People["ana@acme.com.br"] = new DirectoryUser("CN=Ana,OU=TI,DC=acme,DC=local", "guid-ana", "Ana Lima", "ana@acme.com.br", "ana", Disabled: false);
        ldap.People["bob@acme.com.br"] = new DirectoryUser("CN=Bob,OU=TI,DC=acme,DC=local", "guid-bob", "Bob Reis", "bob@acme.com.br", "bob", Disabled: true);
        var service = new DirectoryLoginService(store, ldap, identity.UserManager, new AuditLogService(identity.Factory), NullLogger<DirectoryLoginService>.Instance);
        return (service, ldap, identity, store);
    }

    [Fact]
    public async Task FirstLogin_CreatesAManagedAccount_WithNameAndEmailFromTheDirectory()
    {
        var (service, ldap, identity, _) = await BuildAsync();
        var ct = TestContext.Current.CancellationToken;

        var result = await service.LoginAsync("ana@acme.com.br", "ana-senha", ct);

        Assert.Equal(DirectoryLoginStatus.Success, result.Status);
        Assert.True(result.Created);
        var user = await identity.UserManager.FindByEmailAsync("ana@acme.com.br");
        Assert.NotNull(user);
        Assert.Equal("Ana Lima", user.DisplayName);
        Assert.True(user.DirectoryManaged);
        Assert.Equal("guid-ana", user.DirectoryIdentifier);
        Assert.True(user.EmailConfirmed);
        Assert.False(await identity.UserManager.HasPasswordAsync(user));
        Assert.Equal(1, ldap.Binds);
        await using var db = identity.Factory.CreateDbContext();
        Assert.Equal(1, await db.AuditLogEntries.CountAsync(a => a.Action == AuditAction.DirectoryAccountProvisioned, ct));
    }

    [Fact]
    public async Task ExistingAccountWithSameEmail_IsLinked_NotDuplicated()
    {
        var (service, _, identity, _) = await BuildAsync();
        var ct = TestContext.Current.CancellationToken;
        var existing = new ApplicationUser { UserName = "ana@acme.com.br", Email = "ana@acme.com.br", DisplayName = "Ana", EmailConfirmed = false };
        Assert.True((await identity.UserManager.CreateAsync(existing, "Senha-forte-123!")).Succeeded);

        var result = await service.LoginAsync("ana@acme.com.br", "ana-senha", ct);

        Assert.Equal(DirectoryLoginStatus.Success, result.Status);
        Assert.False(result.Created);
        Assert.Equal(existing.Id, result.User!.Id);
        var user = await identity.UserManager.FindByIdAsync(existing.Id);
        Assert.True(user!.DirectoryManaged);
        Assert.Equal("guid-ana", user.DirectoryIdentifier);
        Assert.Equal("Ana Lima", user.DisplayName);
        Assert.True(user.EmailConfirmed);
        Assert.Equal(1, identity.UserManager.Users.Count());
    }

    [Fact]
    public async Task WrongPassword_UnknownPerson_AndDisabledAccount_AreRefused()
    {
        var (service, ldap, identity, _) = await BuildAsync();
        var ct = TestContext.Current.CancellationToken;

        var wrong = await service.LoginAsync("ana@acme.com.br", "errada", ct);
        var unknown = await service.LoginAsync("zed@acme.com.br", "zed-senha", ct);
        var disabled = await service.LoginAsync("bob@acme.com.br", "bob-senha", ct);

        Assert.Equal(DirectoryLoginStatus.InvalidCredentials, wrong.Status);
        Assert.Equal(DirectoryLoginStatus.InvalidCredentials, unknown.Status);
        Assert.Equal(DirectoryLoginStatus.Disabled, disabled.Status);
        // Conta desabilitada nem chega ao bind, e ninguém ganhou conta no Nexus.
        Assert.Equal(1, ldap.Binds);
        Assert.Equal(0, identity.UserManager.Users.Count());
    }

    [Fact]
    public async Task OtherDomains_AndDisabledIntegration_AreNotHandled()
    {
        var (service, _, _, _) = await BuildAsync();
        var (off, _, _, _) = await BuildAsync(enabled: false);
        var ct = TestContext.Current.CancellationToken;

        Assert.False(await service.HandlesAsync("ana@gmail.com", ct));
        Assert.True(await service.HandlesAsync("ANA@ACME.COM.BR", ct));
        Assert.Equal(DirectoryLoginStatus.NotHandled, (await service.LoginAsync("ana@gmail.com", "x", ct)).Status);
        Assert.False(await off.HandlesAsync("ana@acme.com.br", ct));
    }

    [Fact]
    public async Task ServerDown_IsUnavailable_NotInvalidCredentials()
    {
        var (service, ldap, _, _) = await BuildAsync();
        ldap.Down = true;

        var result = await service.LoginAsync("ana@acme.com.br", "ana-senha", TestContext.Current.CancellationToken);

        Assert.Equal(DirectoryLoginStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task Probe_ChecksTheDirectory_WithoutCreatingAnAccount()
    {
        var (service, _, identity, _) = await BuildAsync();
        var ct = TestContext.Current.CancellationToken;

        var ok = await service.ProbeAsync("ana@acme.com.br", "ana-senha", ct);
        var bad = await service.ProbeAsync("ana@acme.com.br", "x", ct);

        Assert.Equal(DirectoryLoginStatus.Success, ok.Status);
        Assert.Contains("Ana Lima", ok.Detail);
        Assert.Equal(DirectoryLoginStatus.InvalidCredentials, bad.Status);
        Assert.Equal(0, identity.UserManager.Users.Count());
    }

    [Fact]
    public async Task SettingsStore_KeepsThePasswordProtected_AndNormalizesDomains()
    {
        var identity = TestIdentity.Create();
        var store = new DirectorySettingsStore(identity.Factory, new EphemeralDataProtectionProvider());
        var ct = TestContext.Current.CancellationToken;

        var saved = await store.SaveAsync(Settings with { EmailDomains = [" @Acme.com.br ", "acme.local", "acme.local"] }, "segredo", ct);
        var kept = await store.SaveAsync(saved with { Host = "dc02.acme.local" }, bindPassword: null, ct);

        Assert.Equal(["acme.com.br", "acme.local"], saved.EmailDomains);
        Assert.NotEqual("segredo", saved.BindPasswordProtected);
        Assert.Equal("segredo", store.UnprotectBindPassword(kept));
        Assert.Equal("dc02.acme.local", kept.Host);
        await using var db = identity.Factory.CreateDbContext();
        var row = await db.InstanceSettings.SingleAsync(s => s.Key == InstanceSettingKeys.DirectoryLdap, ct);
        Assert.DoesNotContain("segredo", row.Value);
        store.Invalidate();
        Assert.Equal("dc02.acme.local", (await store.GetAsync(ct)).Host);
    }

    [Theory]
    [InlineData("ana@acme.com.br", "(&(objectClass=user)(|(mail=ana@acme.com.br)(userPrincipalName=ana@acme.com.br)))")]
    [InlineData("a*)(cn=*", "(&(objectClass=user)(|(mail=a\\2a\\29\\28cn=\\2a)(userPrincipalName=a\\2a\\29\\28cn=\\2a)))")]
    public void Filter_EscapesWhatTheUserTyped(string email, string expected)
    {
        Assert.Equal(expected, LdapFilter.Build(new DirectorySettings(), email));
    }

    [Fact]
    public void Validate_NamesTheMissingPiece()
    {
        Assert.Equal("Informe o servidor.", new DirectorySettings().Validate());
        Assert.Equal("Informe a senha da conta de serviço.", (Settings with { BindPasswordProtected = null }).Validate());
        Assert.Null((Settings with { BindPasswordProtected = "x" }).Validate());
        Assert.Equal("Informe ao menos um domínio de e-mail.", (Settings with { BindPasswordProtected = "x", EmailDomains = [] }).Validate());
    }
}
