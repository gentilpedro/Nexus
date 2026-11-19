using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Nexus.Licensing;
using Nexus.Web.Services;
using Nexus.Web.Services.Licensing;

namespace Nexus.Web.Tests;

public class LicenseServiceTests
{
    private static readonly (string Private, string Public) Keys = LicenseKeyCodec.GenerateKeyPair();
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 15, 0, 0, TimeSpan.Zero);
    private static readonly AppVersionInfo BuildToday = new("2.0.0", "abc", Now);

    private static LicenseOptions Options(bool enforce = true, string? key = null) => new() { Enforce = enforce, PublicKey = Keys.Public, Key = key, Administrators = ["admin@acme.com.br"] };

    private static LicenseClaims Claims(DateTimeOffset expiresAt, int? maxUsers = 50) =>
        new("ACME-2027", "Acme", LicenseClaims.BusinessPlan, maxUsers, Now.AddYears(-1), expiresAt, expiresAt + LicenseClaims.DefaultUpdateGrace);

    private static LicenseService Service(IDbContextFactory<AppDbContext> factory, LicenseOptions? options = null, AppVersionInfo? build = null) =>
        new(factory, Microsoft.Extensions.Options.Options.Create(options ?? Options()), timeProvider: new FakeTimeProvider(Now), build: build ?? BuildToday);

    // --- As regras puras: os cinco estados, mais o "não exige".

    [Fact]
    public void Evaluate_Valid_ExpiringSoon_Expired()
    {
        var opt = Options();

        var valid = LicenseService.Evaluate(Claims(Now.AddDays(200)), null, Now, Now.AddDays(-100), Now, 3, opt);
        var soon = LicenseService.Evaluate(Claims(Now.AddDays(10)), null, Now, Now.AddDays(-100), Now, 3, opt);
        var expired = LicenseService.Evaluate(Claims(Now.AddDays(-1)), null, Now, Now.AddDays(-100), Now, 3, opt);

        Assert.Equal(LicenseState.Valid, valid.State);
        Assert.False(valid.NeedsAttention);
        Assert.Equal(LicenseState.ExpiringSoon, soon.State);
        Assert.Equal(10, soon.DaysUntilExpiry);
        Assert.True(soon.NeedsAttention);
        Assert.Equal(LicenseState.Expired, expired.State);
        Assert.Equal("A licença venceu.", expired.Error);
        // Vencida há um dia ainda está na carência de 7 dias: esta build sobe.
        Assert.False(expired.BlocksThisBuild);
    }

    [Fact]
    public void Evaluate_Trial_CountsFromFirstStart_AndThenExpires()
    {
        var opt = Options();

        var trial = LicenseService.Evaluate(null, null, Now, Now.AddDays(-20), Now, 0, opt);
        var over = LicenseService.Evaluate(null, null, Now, Now.AddDays(-31), Now, 0, opt);

        Assert.Equal(LicenseState.Trial, trial.State);
        Assert.Equal(10, trial.DaysUntilExpiry);
        Assert.True(trial.IsTrial);
        Assert.True(trial.NeedsAttention);
        Assert.Equal(LicenseState.Expired, over.State);
        Assert.Null(over.Claims);
        Assert.Contains("avaliação", over.Error);
    }

    [Fact]
    public void Evaluate_InvalidKey_FallsBackToTrialRules()
    {
        var opt = Options();

        var invalid = LicenseService.Evaluate(null, "Assinatura inválida.", Now, Now.AddDays(-5), Now, 0, opt);
        var invalidAndOld = LicenseService.Evaluate(null, "Assinatura inválida.", Now, Now.AddDays(-60), Now, 0, opt);

        Assert.Equal(LicenseState.Invalid, invalid.State);
        Assert.False(invalid.BlocksThisBuild);
        Assert.Equal(LicenseState.Invalid, invalidAndOld.State);
        Assert.True(invalidAndOld.BlocksThisBuild);
    }

    [Fact]
    public void Evaluate_BlocksOnlyBuildsNewerThanUpdatesUntil()
    {
        var opt = Options();
        var claims = Claims(Now.AddDays(-30));

        var newer = LicenseService.Evaluate(claims, null, Now, Now, Now, 0, opt);
        var older = LicenseService.Evaluate(claims, null, Now, Now, Now.AddDays(-60), 0, opt);
        var local = LicenseService.Evaluate(claims, null, Now, Now, null, 0, opt);

        Assert.True(newer.BlocksThisBuild);
        Assert.False(older.BlocksThisBuild);
        Assert.False(local.BlocksThisBuild);
    }

    [Fact]
    public void Evaluate_NotEnforced_IgnoresEverything()
    {
        var status = LicenseService.Evaluate(null, "qualquer coisa", Now, Now.AddYears(-2), Now, 999, Options(enforce: false));

        Assert.Equal(LicenseState.NotEnforced, status.State);
        Assert.False(status.IsEnforced);
        Assert.False(status.BlocksThisBuild);
    }

    [Fact]
    public void Evaluate_UserBand()
    {
        var opt = Options();

        var under = LicenseService.Evaluate(Claims(Now.AddYears(1), 50), null, Now, Now, Now, 49, opt);
        var full = LicenseService.Evaluate(Claims(Now.AddYears(1), 50), null, Now, Now, Now, 50, opt);
        var over = LicenseService.Evaluate(Claims(Now.AddYears(1), 50), null, Now, Now, Now, 51, opt);
        var unlimited = LicenseService.Evaluate(Claims(Now.AddYears(1), null), null, Now, Now, Now, 5000, opt);

        Assert.False(under.AtUserLimit);
        Assert.True(full.AtUserLimit);
        Assert.False(full.OverUserLimit);
        Assert.True(over.OverUserLimit);
        Assert.True(over.NeedsAttention);
        Assert.False(unlimited.AtUserLimit);
    }

    // --- O serviço com banco: primeira subida, chave da tela, usuários ativos.

    [Fact]
    public async Task Refresh_RecordsFirstStart_AndStartsTheTrial()
    {
        var factory = TestDb.CreateFactory();
        var ct = TestContext.Current.CancellationToken;
        var service = Service(factory);

        var status = await service.RefreshAsync(ct);

        Assert.Equal(LicenseState.Trial, status.State);
        Assert.Equal(Now.AddDays(30), status.ExpiresAt);
        Assert.Equal("nenhuma", status.Source);
        await using var db = factory.CreateDbContext();
        var first = await db.InstanceSettings.SingleAsync(s => s.Key == InstanceSettingKeys.FirstStartedAtUtc, ct);
        Assert.Equal(Now, DateTimeOffset.Parse(first.Value, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal));
    }

    [Fact]
    public async Task InstallKey_VerifiesBeforeSaving_AndTakesPrecedenceOverConfiguration()
    {
        var factory = TestDb.CreateFactory();
        var ct = TestContext.Current.CancellationToken;
        var configured = LicenseKeyCodec.Encode(Claims(Now.AddDays(5)), Keys.Private);
        var service = Service(factory, Options(key: configured));
        Assert.Equal(LicenseState.ExpiringSoon, (await service.RefreshAsync(ct)).State);
        Assert.Equal("License:Key", service.Current.Source);

        var bad = await service.InstallKeyAsync("NXL-abc.def", ct);
        var good = await service.InstallKeyAsync(LicenseKeyCodec.Encode(Claims(Now.AddYears(1)), Keys.Private), ct);

        Assert.False(bad.Ok);
        Assert.True(good.Ok, good.Message);
        Assert.Contains("Acme", good.Message);
        Assert.Equal(LicenseState.Valid, service.Current.State);
        Assert.Equal("tela", service.Current.Source);
        await using var db = factory.CreateDbContext();
        Assert.Equal(1, await db.InstanceSettings.CountAsync(s => s.Key == InstanceSettingKeys.LicenseKey, ct));
    }

    [Fact]
    public async Task ActiveUsers_AreThoseWhoLoggedInRecently_AndTheBandBlocksNewAccounts()
    {
        var factory = TestDb.CreateFactory();
        var ct = TestContext.Current.CancellationToken;
        await using (var db = factory.CreateDbContext())
        {
            db.Users.AddRange(
                new ApplicationUser { Id = "a", UserName = "a", LastLoginAtUtc = Now.UtcDateTime.AddDays(-1) },
                new ApplicationUser { Id = "b", UserName = "b", LastLoginAtUtc = Now.UtcDateTime.AddDays(-29) },
                new ApplicationUser { Id = "c", UserName = "c", LastLoginAtUtc = Now.UtcDateTime.AddDays(-31) },
                new ApplicationUser { Id = "d", UserName = "d" });
            await db.SaveChangesAsync(ct);
        }

        var service = Service(factory, Options(key: LicenseKeyCodec.Encode(Claims(Now.AddYears(1), 2), Keys.Private)));
        var status = await service.RefreshAsync(ct);

        Assert.Equal(2, status.ActiveUsers);
        Assert.True(status.AtUserLimit);
        Assert.False(service.CanCreateAccount);

        // "c" entra de novo: passa a contar, e a faixa fica estourada (contas antigas continuam entrando).
        await service.RecordLoginAsync("c", ct);
        status = await service.RefreshAsync(ct);
        Assert.Equal(3, status.ActiveUsers);
        Assert.True(status.OverUserLimit);
    }

    [Fact]
    public void Administrators_ComeFromLicenseOptions_OrPlansUnlimitedAccounts()
    {
        var factory = TestDb.CreateFactory();
        var fromLicense = Service(factory);
        var fromPlans = new LicenseService(factory, Microsoft.Extensions.Options.Options.Create(new LicenseOptions { PublicKey = Keys.Public }),
            Microsoft.Extensions.Options.Options.Create(new PlanOptions { UnlimitedAccounts = ["dono@aamora.com.br"] }), new FakeTimeProvider(Now), build: BuildToday);

        Assert.True(fromLicense.IsAdministrator(Principal("admin@acme.com.br")));
        Assert.False(fromLicense.IsAdministrator(Principal("dono@aamora.com.br")));
        Assert.True(fromPlans.IsAdministrator(Principal("Dono@aamora.com.br")));
        Assert.False(fromPlans.IsAdministrator(null));
    }

    // --- Administradores pela tela e a primeira conta que entra (#91).

    private static LicenseService ServiceWithoutConfiguredAdministrators(IDbContextFactory<AppDbContext> factory) =>
        new(factory, Microsoft.Extensions.Options.Options.Create(new LicenseOptions { PublicKey = Keys.Public }), timeProvider: new FakeTimeProvider(Now), build: BuildToday);

    private static async Task AddUsersAsync(IDbContextFactory<AppDbContext> factory, CancellationToken ct, params (string Id, string Email)[] users)
    {
        await using var db = factory.CreateDbContext();
        db.Users.AddRange(users.Select(u => new ApplicationUser { Id = u.Id, UserName = u.Email, Email = u.Email, NormalizedEmail = u.Email.ToUpperInvariant() }));
        await db.SaveChangesAsync(ct);
    }

    [Fact]
    public async Task FirstLogin_BecomesAdministrator_WhenNobodyIsConfigured()
    {
        var factory = TestDb.CreateFactory();
        var ct = TestContext.Current.CancellationToken;
        await AddUsersAsync(factory, ct, ("ana", "ana@acme.com.br"), ("bia", "bia@acme.com.br"));
        var service = ServiceWithoutConfiguredAdministrators(factory);
        await service.RefreshAsync(ct);
        Assert.False(service.HasAdministrators);

        await service.RecordLoginAsync("ana", ct);
        await service.RecordLoginAsync("bia", ct);

        Assert.True(service.IsAdministrator(Principal("Ana@acme.com.br")));
        Assert.False(service.IsAdministrator(Principal("bia@acme.com.br")));
        var admins = await service.GetAdministratorsAsync(ct);
        Assert.Equal([new LicenseService.Administrator("ana@acme.com.br", false)], admins);
        await using var db = factory.CreateDbContext();
        Assert.Equal(1, await db.AuditLogEntries.CountAsync(a => a.Action == AuditAction.InstanceAdministratorAdded && a.TargetId == "ana@acme.com.br", ct));

        // Outro processo (ou a subida seguinte) relê o banco e enxerga a mesma pessoa.
        var again = ServiceWithoutConfiguredAdministrators(factory);
        await again.RefreshAsync(ct);
        Assert.True(again.IsAdministrator(Principal("ana@acme.com.br")));
    }

    [Fact]
    public async Task FirstLogin_DoesNotClaim_WhenAdministratorsAreConfigured()
    {
        var factory = TestDb.CreateFactory();
        var ct = TestContext.Current.CancellationToken;
        await AddUsersAsync(factory, ct, ("ana", "ana@acme.com.br"));
        var service = Service(factory);

        await service.RecordLoginAsync("ana", ct);

        Assert.False(service.IsAdministrator(Principal("ana@acme.com.br")));
        Assert.True(service.IsAdministrator(Principal("admin@acme.com.br")));
        Assert.Equal([new LicenseService.Administrator("admin@acme.com.br", true)], await service.GetAdministratorsAsync(ct));
    }

    [Fact]
    public async Task Administrators_CanBePromotedAndRemoved_ButNeverLeaveNobody()
    {
        var factory = TestDb.CreateFactory();
        var ct = TestContext.Current.CancellationToken;
        await AddUsersAsync(factory, ct, ("ana", "ana@acme.com.br"), ("bia", "bia@acme.com.br"));
        var service = ServiceWithoutConfiguredAdministrators(factory);
        await service.RecordLoginAsync("ana", ct);

        var unknown = await service.AddAdministratorAsync("ninguem@acme.com.br", "ana", ct);
        var twice = await service.AddAdministratorAsync("ANA@acme.com.br", "ana", ct);
        var promoted = await service.AddAdministratorAsync(" bia@acme.com.br ", "ana", ct);

        Assert.False(unknown.Ok);
        Assert.Contains("precisa criar a conta", unknown.Message);
        Assert.False(twice.Ok);
        Assert.True(promoted.Ok, promoted.Message);
        Assert.True(service.IsAdministrator(Principal("bia@acme.com.br")));
        Assert.Equal(2, (await service.GetAdministratorsAsync(ct)).Count);

        var removedAna = await service.RemoveAdministratorAsync("ana@acme.com.br", "bia", ct);
        var last = await service.RemoveAdministratorAsync("bia@acme.com.br", "bia", ct);

        Assert.True(removedAna.Ok, removedAna.Message);
        Assert.False(service.IsAdministrator(Principal("ana@acme.com.br")));
        Assert.False(last.Ok);
        Assert.Contains("pelo menos um administrador", last.Message);
        Assert.True(service.IsAdministrator(Principal("bia@acme.com.br")));
        await using var db = factory.CreateDbContext();
        Assert.Equal(1, await db.AuditLogEntries.CountAsync(a => a.Action == AuditAction.InstanceAdministratorRemoved, ct));
    }

    [Fact]
    public async Task ConfiguredAdministrators_CannotBeRemovedFromTheScreen()
    {
        var factory = TestDb.CreateFactory();
        var ct = TestContext.Current.CancellationToken;
        var service = Service(factory);

        var result = await service.RemoveAdministratorAsync("admin@acme.com.br", null, ct);

        Assert.False(result.Ok);
        Assert.Contains("License__Administrators", result.Message);
        Assert.True(service.IsAdministrator(Principal("admin@acme.com.br")));
    }

    private static System.Security.Claims.ClaimsPrincipal Principal(string email) =>
        new(new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, email)], "test"));
}
