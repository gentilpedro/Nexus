namespace Nexus.Domain.Entities;

/// <summary>
/// Chave/valor da instância inteira, para o que não pertence a nenhum workspace: a chave de
/// licença instalada pela tela (#81), a data da primeira subida (que conta o período de
/// avaliação) e, mais adiante, a configuração do Active Directory (#70).
/// </summary>
public class InstanceSetting
{
    public const int MaxKeyLength = 100;
    public const int MaxValueLength = 8000;

    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; }
}

public static class InstanceSettingKeys
{
    /// <summary>Quando esta instância subiu pela primeira vez (ISO 8601, UTC).</summary>
    public const string FirstStartedAtUtc = "instance.first-started-at";

    /// <summary>A chave de licença colada na tela; tem precedência sobre License:Key.</summary>
    public const string LicenseKey = "license.key";

    /// <summary>Configuração do login pelo Active Directory (JSON de DirectorySettings).</summary>
    public const string DirectoryLdap = "directory.ldap";
}
