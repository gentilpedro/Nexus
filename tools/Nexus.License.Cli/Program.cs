using System.Globalization;
using System.Text.Json;
using Nexus.Licensing;

// nexus-license (#81): emite e lê chaves de licença do plano Business self-hosted.
//
//   nexus-license keygen
//       Gera um par de chaves. Guarde a privada fora do repositório (ex.: ~/.nexus-license-key);
//       a pública vai em LicenseOptions.AamoraPublicKey.
//   nexus-license issue --customer "Acme" --license-id ACME-2027 --expires 2027-11-30
//                       [--max-users 50 | --unlimited] [--grace-days 7] [--plan Business]
//                       [--private-key-file ~/.nexus-license-key | NEXUS_LICENSE_PRIVATE_KEY]
//       Imprime a chave NXL-... para mandar ao cliente.
//   nexus-license inspect NXL-... [--public-key <base64>]
//       Verifica a assinatura (com a chave pública embutida, ou outra) e mostra o conteúdo.

return args.Length == 0 ? Usage() : args[0] switch
{
    "keygen" => KeyGen(),
    "issue" => Issue(args[1..]),
    "inspect" => Inspect(args[1..]),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("uso: nexus-license keygen | issue --customer <nome> --license-id <id> --expires <aaaa-mm-dd> [--max-users N | --unlimited] [--grace-days 7] [--plan Business] [--private-key-file <arquivo>] | inspect <chave> [--public-key <base64>]");
    return 2;
}

static int KeyGen()
{
    var (priv, pub) = LicenseKeyCodec.GenerateKeyPair();
    Console.WriteLine("chave privada (guarde fora do repositório, nunca na imagem):");
    Console.WriteLine(priv);
    Console.WriteLine();
    Console.WriteLine("chave pública (LicenseOptions.AamoraPublicKey):");
    Console.WriteLine(pub);
    return 0;
}

static int Issue(string[] args)
{
    var o = ParseOptions(args);
    var customer = Required(o, "customer");
    var licenseId = Required(o, "license-id");
    var expires = ParseDate(Required(o, "expires"));
    var plan = o.GetValueOrDefault("plan") ?? LicenseClaims.BusinessPlan;
    var graceDays = int.Parse(o.GetValueOrDefault("grace-days") ?? LicenseClaims.DefaultUpdateGrace.TotalDays.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    int? maxUsers = o.ContainsKey("unlimited") ? null
        : int.Parse(o.GetValueOrDefault("max-users") ?? throw new ArgumentException("Informe --max-users N ou --unlimited."), CultureInfo.InvariantCulture);
    if (maxUsers is < 1)
    {
        throw new ArgumentException("--max-users precisa ser 1 ou mais.");
    }

    var privateKey = Environment.GetEnvironmentVariable("NEXUS_LICENSE_PRIVATE_KEY");
    if (o.TryGetValue("private-key-file", out var file))
    {
        privateKey = File.ReadAllText(Expand(file)).Trim();
    }
    else if (string.IsNullOrWhiteSpace(privateKey))
    {
        var home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nexus-license-key");
        if (File.Exists(home))
        {
            privateKey = File.ReadAllText(home).Trim();
        }
    }

    if (string.IsNullOrWhiteSpace(privateKey))
    {
        throw new ArgumentException("Chave privada não encontrada: use --private-key-file, NEXUS_LICENSE_PRIVATE_KEY ou ~/.nexus-license-key.");
    }

    var now = DateTimeOffset.UtcNow;
    var claims = new LicenseClaims(licenseId, customer, plan, maxUsers, now, expires, expires.AddDays(graceDays));
    Console.WriteLine(LicenseKeyCodec.Encode(claims, privateKey));
    Console.Error.WriteLine("emitida: " + LicenseKeyCodec.Describe(claims));
    Console.Error.WriteLine("lembrete: emita também o token de leitura do registro de imagens para este cliente (docs/canal-business.md).");
    return 0;
}

static int Inspect(string[] args)
{
    var key = args.FirstOrDefault(a => a.StartsWith(LicenseKeyCodec.Prefix, StringComparison.Ordinal))
        ?? throw new ArgumentException("Informe a chave NXL-...");
    var o = ParseOptions(args.Where(a => !a.StartsWith(LicenseKeyCodec.Prefix, StringComparison.Ordinal)).ToArray());
    var publicKey = o.GetValueOrDefault("public-key") ?? EmbeddedPublicKey();
    if (!LicenseKeyCodec.TryDecode(key, publicKey, out var claims, out var error))
    {
        Console.Error.WriteLine("inválida: " + error);
        return 1;
    }

    Console.WriteLine(JsonSerializer.Serialize(claims, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    return 0;
}

// A chave pública embutida no app fica em Nexus.Web; para não arrastar o ASP.NET inteiro para a
// ferramenta, ela é lida do fonte quando o repositório está por perto, senão exige --public-key.
static string EmbeddedPublicKey()
{
    var dir = AppContext.BaseDirectory;
    for (var i = 0; i < 8 && dir is not null; i++, dir = Path.GetDirectoryName(dir))
    {
        var source = Path.Combine(dir, "src", "Nexus.Web", "Services", "Licensing", "LicenseService.cs");
        if (!File.Exists(source))
        {
            continue;
        }

        foreach (var line in File.ReadLines(source))
        {
            const string marker = "AamoraPublicKey = \"";
            var at = line.IndexOf(marker, StringComparison.Ordinal);
            if (at >= 0)
            {
                return line[(at + marker.Length)..line.LastIndexOf('"')];
            }
        }
    }

    throw new ArgumentException("Não achei a chave pública embutida; passe --public-key.");
}

static Dictionary<string, string> ParseOptions(string[] args)
{
    var o = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException("Argumento inesperado: " + args[i]);
        }

        var name = args[i][2..];
        var hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
        o[name] = hasValue ? args[++i] : "";
    }

    return o;
}

static string Required(Dictionary<string, string> o, string name) =>
    o.TryGetValue(name, out var v) && v.Length > 0 ? v : throw new ArgumentException($"Falta --{name}.");

static DateTimeOffset ParseDate(string s) =>
    DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
        ? new DateTimeOffset(d.ToDateTime(new TimeOnly(23, 59, 59)), TimeSpan.Zero)
        : throw new ArgumentException("Data no formato aaaa-mm-dd: " + s);

static string Expand(string path) =>
    path.StartsWith('~') ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.TrimStart('~', '/', '\\')) : path;
