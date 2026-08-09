using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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
    private const string ConnectionString = "Host=localhost;Database=nexus_test;Username=u;Password=p";

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();

    /// <summary>
    /// A throwaway self-signed PKCS#12 on disk, so the certificate branch of the guard can be
    /// exercised on any OS. The alternative — relying on the DPAPI branch — only works on a
    /// Windows agent, and CI runs on ubuntu-latest.
    /// </summary>
    private sealed class TemporaryCertificate : IDisposable
    {
        public string PfxPath { get; }
        public string Password => "nexus-test";

        public TemporaryCertificate()
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=Nexus Data Protection Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

            PfxPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"nexus-dp-test-{Guid.NewGuid():N}.pfx");
            File.WriteAllBytes(PfxPath, certificate.Export(X509ContentType.Pkcs12, Password));
        }

        public void Dispose()
        {
            try
            {
                File.Delete(PfxPath);
            }
            catch (IOException)
            {
                // A leftover file in the temp directory is not worth failing a test over.
            }
        }
    }

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
    /// The fix: given a certificate, AddInfrastructure ends up with an encryptor configured.
    /// </summary>
    /// <remarks>
    /// Deliberately exercises the certificate branch rather than DPAPI. DPAPI is Windows-only, so
    /// a test that leaned on it passed locally and threw on the Linux CI agent — which is exactly
    /// how this suite broke. The certificate branch is also the one a non-Windows deployment
    /// would actually use.
    /// </remarks>
    [Fact]
    public void AddInfrastructure_ConfiguresAKeyEncryptor()
    {
        using var certificate = new TemporaryCertificate();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(ConnectionString, Config(
            ("DataProtection:CertificatePath", certificate.PfxPath),
            ("DataProtection:CertificatePassword", certificate.Password)));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        Assert.NotNull(options.XmlRepository);
        Assert.NotNull(options.XmlEncryptor);
    }

    /// <summary>
    /// The guard is a security control, so assert it actually fires. Off Windows, no certificate
    /// and no opt-in must be refused outright; on Windows the DPAPI branch legitimately covers
    /// the same case, so there is nothing to throw.
    /// </summary>
    [Fact]
    public void NoCertificateAndNoOptIn_RefusesToStartWithoutDpapi()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var ex = Record.Exception(() => services.AddInfrastructure(ConnectionString, Config()));

        if (OperatingSystem.IsWindows())
        {
            Assert.Null(ex);
        }
        else
        {
            Assert.IsType<InvalidOperationException>(ex);
        }
    }

    [Fact]
    public void AddInfrastructure_PinsTheApplicationName()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(
            ConnectionString, Config(("DataProtection:AllowUnprotectedKeys", "true")));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<DataProtectionOptions>>().Value;

        // Pinned, not derived from the content-root path — otherwise moving the IIS physical
        // directory silently invalidates every issued cookie. SetApplicationName surfaces as the
        // application discriminator, which is what actually feeds key derivation.
        Assert.Equal("Nexus", options.ApplicationDiscriminator);
    }

    /// <summary>
    /// The development escape hatch must be explicit: setting the flag is what turns the refusal
    /// above into a successful (plaintext-keys) startup.
    /// </summary>
    [Fact]
    public void AllowUnprotectedKeys_IsAnExplicitOptIn()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var ex = Record.Exception(() => services.AddInfrastructure(
            ConnectionString,
            Config(("DataProtection:AllowUnprotectedKeys", "true"))));

        Assert.Null(ex);
    }
}
