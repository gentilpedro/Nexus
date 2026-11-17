using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Nexus.Licensing;

/// <summary>
/// Monta e verifica chaves de licença. Formato: <c>NXL-&lt;payload&gt;.&lt;assinatura&gt;</c>, os dois
/// em base64url; o payload é o JSON de <see cref="LicenseClaims"/> e a assinatura é Ed25519 sobre
/// os bytes exatos do payload. A chave pública fica embutida na imagem; a privada só existe na
/// máquina de quem emite (ver tools/Nexus.License.Cli).
/// </summary>
public static class LicenseKeyCodec
{
    public const string Prefix = "NXL-";
    private const int PublicKeyLength = 32;
    private const int PrivateKeyLength = 32;
    private const int SignatureLength = 64;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    /// <summary>Gera um par de chaves novo. Retorna (privada, pública) em base64 padrão.</summary>
    public static (string PrivateKey, string PublicKey) GenerateKeyPair()
    {
        var seed = RandomNumberGenerator.GetBytes(PrivateKeyLength);
        var priv = new Ed25519PrivateKeyParameters(seed, 0);
        return (Convert.ToBase64String(seed), Convert.ToBase64String(priv.GeneratePublicKey().GetEncoded()));
    }

    /// <summary>Assina as claims com a chave privada (base64) e devolve o texto da chave.</summary>
    public static string Encode(LicenseClaims claims, string privateKeyBase64)
    {
        var seed = Convert.FromBase64String(privateKeyBase64);
        if (seed.Length != PrivateKeyLength)
        {
            throw new ArgumentException("A chave privada precisa ter 32 bytes.", nameof(privateKeyBase64));
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(claims, Json);
        var signer = new Ed25519Signer();
        signer.Init(forSigning: true, new Ed25519PrivateKeyParameters(seed, 0));
        signer.BlockUpdate(payload, 0, payload.Length);
        var signature = signer.GenerateSignature();
        return Prefix + Base64Url(payload) + "." + Base64Url(signature);
    }

    /// <summary>
    /// Lê e verifica uma chave. Falha, com motivo legível, em formato errado, assinatura inválida
    /// ou JSON que não é uma licença. Não olha datas: isso é decisão de quem chama.
    /// </summary>
    public static bool TryDecode(string? text, string publicKeyBase64, [NotNullWhen(true)] out LicenseClaims? claims, [NotNullWhen(false)] out string? error)
    {
        claims = null;
        error = null;
        var key = (text ?? "").Trim();
        if (key.Length == 0)
        {
            error = "Chave vazia.";
            return false;
        }

        if (!key.StartsWith(Prefix, StringComparison.Ordinal))
        {
            error = "A chave precisa começar com NXL-.";
            return false;
        }

        var dot = key.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0 || dot == key.Length - 1)
        {
            error = "Chave incompleta: falta a assinatura.";
            return false;
        }

        byte[] payload, signature, publicKey;
        try
        {
            payload = FromBase64Url(key[Prefix.Length..dot]);
            signature = FromBase64Url(key[(dot + 1)..]);
            publicKey = Convert.FromBase64String(publicKeyBase64);
        }
        catch (FormatException)
        {
            error = "Chave corrompida: não é base64 válido.";
            return false;
        }

        if (publicKey.Length != PublicKeyLength)
        {
            error = "Chave pública da instância inválida (License:PublicKey).";
            return false;
        }

        if (signature.Length != SignatureLength)
        {
            error = "Assinatura com tamanho errado.";
            return false;
        }

        var verifier = new Ed25519Signer();
        verifier.Init(forSigning: false, new Ed25519PublicKeyParameters(publicKey, 0));
        verifier.BlockUpdate(payload, 0, payload.Length);
        if (!verifier.VerifySignature(signature))
        {
            error = "Assinatura inválida: a chave foi alterada ou não foi emitida pela Aamora.";
            return false;
        }

        try
        {
            claims = JsonSerializer.Deserialize<LicenseClaims>(payload, Json);
        }
        catch (JsonException)
        {
            claims = null;
        }

        if (claims is null || string.IsNullOrWhiteSpace(claims.LicenseId) || string.IsNullOrWhiteSpace(claims.Customer))
        {
            claims = null;
            error = "O conteúdo assinado não é uma licença.";
            return false;
        }

        if (claims.MaxActiveUsers is < 1)
        {
            claims = null;
            error = "Limite de usuários inválido.";
            return false;
        }

        return true;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string s)
    {
        var b = s.Replace('-', '+').Replace('_', '/');
        b = (b.Length % 4) switch { 2 => b + "==", 3 => b + "=", 1 => throw new FormatException(), _ => b };
        return Convert.FromBase64String(b);
    }

    /// <summary>Resumo curto para logs: id e cliente, nunca a chave inteira.</summary>
    public static string Describe(LicenseClaims c) =>
        $"{c.LicenseId} ({c.Customer}, {c.Plan}, {(c.IsUnlimited ? "ilimitada" : c.MaxActiveUsers + " usuários")}, vence {c.ExpiresAt:yyyy-MM-dd})";

    internal static string Utf8(byte[] bytes) => Encoding.UTF8.GetString(bytes);
}
