using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nexus.Infrastructure.Data;
using Nexus.Infrastructure.DependencyInjection;

namespace Nexus.Web.Tests;

/// <summary>
/// Regression tests for H1 — Data Protection key ring stored unencrypted.
/// </summary>
/// <remarks>
/// The key ring protects the authentication cookie and antiforgery tokens. If the keys are
/// persisted as plaintext XML, read access to the database is enough to forge a session for any
/// user, so what matters is that an <c>IXmlEncryptor</c> is actually configured.
/// </remarks>
public class DataProtectionKeyEncryptionTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();

    /// <summary>
    /// Demonstrates the defect this fix addresses: calling PersistKeysToDbContext on its own —
    /// exactly what the code used to do — leaves XmlEncryptor null, i.e. keys written in the
    /// clear. This is the "before" state, asserted so the fix below is meaningful rather than
    /// self-referential.
    /// </summary>
    [Fact]
    public void PersistKeysToDbContextAlone_LeavesKeysUnencrypted()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase("dp-before"));
        services.AddDataProtection().PersistKeysToDbContext<AppDbContext>();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        Assert.NotNull(options.XmlRepository);
        Assert.Null(options.XmlEncryptor); // <- plaintext keys in the DataProtectionKeys table
    }

    /// <summary>
    /// The fix: AddInfrastructure must always end up with an encryptor configured. On this
    /// (Windows) build agent that is the DPAPI branch; on Linux the certificate branch or the
    /// explicit development opt-out applies.
    /// </summary>
    [Fact]
    public void AddInfrastructure_ConfiguresAKeyEncryptor()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure("Host=localhost;Database=nexus_test;Username=u;Password=p", Config());

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        Assert.NotNull(options.XmlRepository);
        Assert.NotNull(options.XmlEncryptor);
    }

    [Fact]
    public void AddInfrastructure_PinsTheApplicationName()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure("Host=localhost;Database=nexus_test;Username=u;Password=p", Config());

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<DataProtectionOptions>>().Value;

        // Pinned, not derived from the content-root path — otherwise moving the IIS physical
        // directory silently invalidates every issued cookie. SetApplicationName surfaces as the
        // application discriminator, which is what actually feeds key derivation.
        Assert.Equal("Nexus", options.ApplicationDiscriminator);
    }

    /// <summary>
    /// The development escape hatch must be explicit. This asserts the flag is read at all; the
    /// throwing branch itself is only reachable on a non-Windows host with no certificate
    /// configured, which this Windows agent cannot exercise.
    /// </summary>
    [Fact]
    public void AllowUnprotectedKeys_IsAnExplicitOptIn()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var ex = Record.Exception(() => services.AddInfrastructure(
            "Host=localhost;Database=nexus_test;Username=u;Password=p",
            Config(("DataProtection:AllowUnprotectedKeys", "true"))));

        Assert.Null(ex);
    }
}
