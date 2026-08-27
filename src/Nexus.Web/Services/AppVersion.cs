using System.Reflection;
using System.Globalization;

namespace Nexus.Web.Services;

/// <summary>
/// A versão da build que está rodando, lida dos atributos gravados no assembly durante a
/// compilação.
/// </summary>
/// <param name="Version">Versão semântica, ex.: <c>1.4.0</c>.</param>
/// <param name="Commit">SHA completo do commit que originou a build, quando conhecido.</param>
/// <param name="BuiltAt">Momento da compilação em UTC, quando conhecido.</param>
public sealed record AppVersionInfo(string Version, string? Commit, DateTimeOffset? BuiltAt)
{
    /// <summary>Os 7 primeiros caracteres do SHA — o formato curto que o GitHub usa.</summary>
    public string? ShortCommit => Commit is { Length: >= 7 } ? Commit[..7] : Commit;

    /// <summary>
    /// Linha única para exibição, ex.: <c>1.4.0 (a3f9c21, 09/08/2026 14:22 UTC)</c>. Usada no
    /// atributo title do rodapé, onde cabe o detalhe completo sem poluir a interface.
    /// </summary>
    public string DetailedLabel
    {
        get
        {
            var parts = new List<string>(2);
            if (ShortCommit is not null)
            {
                parts.Add($"commit {ShortCommit}");
            }

            if (BuiltAt is { } builtAt)
            {
                parts.Add($"build de {builtAt.UtcDateTime.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)} UTC");
            }

            return parts.Count == 0 ? Version : $"{Version} — {string.Join(", ", parts)}";
        }
    }
}

/// <summary>
/// Descobre qual versão do Nexus está no ar.
/// </summary>
/// <remarks>
/// A CI carimba a build com <c>-p:Version</c> e <c>-p:SourceRevisionId</c>, e o SDK combina os
/// dois em <c>AssemblyInformationalVersion</c> no formato <c>1.4.0+&lt;sha&gt;</c>. O horário da
/// compilação vem separado, num <see cref="AssemblyMetadataAttribute"/>, porque não existe campo
/// padrão para ele.
/// <para>
/// Uma build local não passa nenhuma dessas propriedades e por isso se identifica como
/// <c>0.0.0-local</c> — é importante que não se disfarce de release.
/// </para>
/// </remarks>
public static class AppVersion
{
    private const string LocalBuildVersion = "0.0.0-local";
    internal const string BuildTimestampKey = "BuildTimestamp";

    /// <summary>A versão desta instância em execução.</summary>
    public static AppVersionInfo Current { get; } = FromAssembly(typeof(AppVersion).Assembly);

    internal static AppVersionInfo FromAssembly(Assembly assembly)
    {
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        var buildTimestamp = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == BuildTimestampKey)?.Value;

        return Parse(informational, buildTimestamp);
    }

    /// <summary>
    /// Separa a versão informacional em versão e commit e interpreta o horário da build.
    /// Mantido puro (sem tocar em assembly) para poder ser testado diretamente.
    /// </summary>
    internal static AppVersionInfo Parse(string? informationalVersion, string? buildTimestamp)
    {
        var version = LocalBuildVersion;
        string? commit = null;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            // "1.4.0+a3f9c21..." — o '+' é o separador de metadados de build do semver. Um build
            // local sem SourceRevisionId não tem essa parte.
            var separator = informationalVersion.IndexOf('+');
            if (separator >= 0)
            {
                version = informationalVersion[..separator];
                var sha = informationalVersion[(separator + 1)..];
                commit = string.IsNullOrWhiteSpace(sha) ? null : sha;
            }
            else
            {
                version = informationalVersion;
            }
        }

        DateTimeOffset? builtAt = null;
        if (!string.IsNullOrWhiteSpace(buildTimestamp)
            && DateTimeOffset.TryParse(
                buildTimestamp,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            builtAt = parsed;
        }

        return new AppVersionInfo(version, commit, builtAt);
    }
}
