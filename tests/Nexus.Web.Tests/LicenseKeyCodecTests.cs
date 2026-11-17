using Nexus.Licensing;

namespace Nexus.Web.Tests;

public class LicenseKeyCodecTests
{
    private static readonly (string Private, string Public) Keys = LicenseKeyCodec.GenerateKeyPair();

    private static LicenseClaims Claims() => new(
        "ACME-2027", "Acme Ltda", LicenseClaims.BusinessPlan, 50,
        new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2027, 11, 30, 23, 59, 59, TimeSpan.Zero),
        new DateTimeOffset(2027, 12, 7, 23, 59, 59, TimeSpan.Zero));

    [Fact]
    public void Encode_ThenDecode_RoundTrips()
    {
        var key = LicenseKeyCodec.Encode(Claims(), Keys.Private);

        Assert.StartsWith("NXL-", key);
        Assert.True(LicenseKeyCodec.TryDecode(key, Keys.Public, out var decoded, out var error), error);
        Assert.Equal(Claims(), decoded);
        Assert.True(LicenseKeyCodec.TryDecode("  " + key + "\n", Keys.Public, out _, out _));
    }

    [Fact]
    public void OneByteChanged_IsRefused()
    {
        var key = LicenseKeyCodec.Encode(Claims(), Keys.Private);
        var dot = key.IndexOf('.');
        var payloadIndex = dot - 5;
        var tampered = key[..payloadIndex] + (key[payloadIndex] == 'A' ? 'B' : 'A') + key[(payloadIndex + 1)..];

        Assert.False(LicenseKeyCodec.TryDecode(tampered, Keys.Public, out _, out var error));
        Assert.Contains("inválida", error);
    }

    [Fact]
    public void SignedWithAnotherKey_IsRefused()
    {
        var other = LicenseKeyCodec.GenerateKeyPair();
        var key = LicenseKeyCodec.Encode(Claims(), other.PrivateKey);

        Assert.False(LicenseKeyCodec.TryDecode(key, Keys.Public, out _, out _));
    }

    [Theory]
    [InlineData("", "vazia")]
    [InlineData("ABC-123", "NXL-")]
    [InlineData("NXL-semponto", "assinatura")]
    [InlineData("NXL-###.###", "base64")]
    public void Garbage_FailsWithAReadableReason(string text, string expected)
    {
        Assert.False(LicenseKeyCodec.TryDecode(text, Keys.Public, out _, out var error));
        Assert.Contains(expected, error);
    }

    [Fact]
    public void UnlimitedLicense_HasNoUserCap()
    {
        var key = LicenseKeyCodec.Encode(Claims() with { MaxActiveUsers = null }, Keys.Private);

        Assert.True(LicenseKeyCodec.TryDecode(key, Keys.Public, out var decoded, out _));
        Assert.True(decoded.IsUnlimited);
    }
}
