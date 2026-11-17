using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Nexus.Licensing;

namespace Nexus.Web.Services.Licensing;

public sealed class LicenseOptions
{
    /// <summary>
    /// Chave pública (Ed25519, base64) que valida as chaves emitidas pela Aamora. Embutida aqui de
    /// propósito: a instalação self-hosted não liga para casa. Só os testes trocam.
    /// </summary>
    public const string AamoraPublicKey = "qZCPWkShwQI5s65Y3Ssii4a+0WIvaui5Oied+fy2euU=";

    /// <summary>
    /// Se a instância exige licença. True por padrão, porque a imagem publicada é a do plano
    /// Business; a oferta em nuvem da Aamora e o ambiente de desenvolvimento desligam no compose
    /// ou no appsettings.Development.json.
    /// </summary>
    public bool Enforce { get; set; } = true;

    /// <summary>A chave de licença (NXL-...). A chave colada na tela tem precedência.</summary>
    public string? Key { get; set; }

    /// <summary>Caminho de um arquivo com a chave, para quem prefere montar um secret.</summary>
    public string? KeyFile { get; set; }

    public string PublicKey { get; set; } = AamoraPublicKey;

    /// <summary>Dias de uso sem chave, contados da primeira subida da instância.</summary>
    public int TrialDays { get; set; } = 30;

    /// <summary>Com quantos dias de antecedência a faixa "a licença vence em N dias" aparece.</summary>
    public int WarningDays { get; set; } = 30;

    /// <summary>Janela do "usuário ativo": quem entrou nesse número de dias conta na faixa.</summary>
    public int ActiveUserWindowDays { get; set; } = 30;

    /// <summary>
    /// E-mails de quem administra a instância (vê a tela de licença e as faixas de aviso). Vazio =
    /// as contas de Plans:UnlimitedAccounts.
    /// </summary>
    public List<string> Administrators { get; set; } = [];
}

public enum LicenseState
{
    /// <summary>Instância sem licença por configuração (nuvem da Aamora, desenvolvimento).</summary>
    NotEnforced,
    /// <summary>Sem chave, dentro do período de avaliação.</summary>
    Trial,
    Valid,
    ExpiringSoon,
    /// <summary>Licença (ou avaliação) vencida: tudo continua, só atualizações travam.</summary>
    Expired,
    /// <summary>Há uma chave, mas ela não verifica. Vale a regra da avaliação por baixo.</summary>
    Invalid,
}

/// <param name="Source">De onde veio a chave: "tela", "License:Key", "License:KeyFile" ou "nenhuma".</param>
public sealed record LicenseStatus(
    LicenseState State,
    LicenseClaims? Claims,
    string? Error,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? UpdatesUntil,
    bool BlocksThisBuild,
    int ActiveUsers,
    int? MaxActiveUsers,
    DateTimeOffset EvaluatedAt,
    string Source)
{
    public static readonly LicenseStatus NotEnforced = new(LicenseState.NotEnforced, null, null, null, null, false, 0, null, DateTimeOffset.MinValue, "nenhuma");

    public bool IsEnforced => State != LicenseState.NotEnforced;
    public bool IsTrial => State is LicenseState.Trial || (State is LicenseState.Expired && Claims is null);
    public int? DaysUntilExpiry => ExpiresAt is null ? null : (int)Math.Ceiling((ExpiresAt.Value - EvaluatedAt).TotalDays);
    public bool AtUserLimit => MaxActiveUsers is not null && ActiveUsers >= MaxActiveUsers;
    public bool OverUserLimit => MaxActiveUsers is not null && ActiveUsers > MaxActiveUsers;

    /// <summary>Verdadeiro quando um administrador precisa ver uma faixa no topo.</summary>
    public bool NeedsAttention => State is LicenseState.ExpiringSoon or LicenseState.Expired or LicenseState.Invalid
        || (State is LicenseState.Trial && DaysUntilExpiry is <= 14)
        || OverUserLimit;
}

/// <summary>
/// Licença da instalação self-hosted (#81). Um único lugar responde "esta instância está
/// licenciada?": a tela de licença, as faixas de aviso, o bloqueio de atualização na subida e o
/// limite de contas novas leem <see cref="Current"/>. A verificação da assinatura está em
/// <see cref="LicenseKeyCodec"/>; aqui ficam as regras de data, avaliação e faixa de usuários.
/// Regra central: licença vencida nunca derruba a instalação. O que trava é subir uma imagem
/// construída depois de <see cref="LicenseClaims.UpdatesUntil"/>.
/// </summary>
public sealed class LicenseService(
    IDbContextFactory<AppDbContext> dbFactory,
    IOptions<LicenseOptions> options,
    IOptions<PlanOptions>? planOptions = null,
    TimeProvider? timeProvider = null,
    ILogger<LicenseService>? logger = null,
    AppVersionInfo? build = null)
{
    private static readonly TimeSpan LoginStampResolution = TimeSpan.FromHours(1);

    private readonly LicenseOptions opt = options.Value;
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly AppVersionInfo buildInfo = build ?? AppVersion.Current;
    private readonly HashSet<string> administrators = (options.Value.Administrators.Count > 0
            ? options.Value.Administrators
            : planOptions?.Value.UnlimitedAccounts ?? [])
        .Where(e => !string.IsNullOrWhiteSpace(e))
        .Select(e => e.Trim())
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private volatile LicenseStatus current = options.Value.Enforce
        ? new LicenseStatus(LicenseState.Trial, null, null, null, null, false, 0, null, DateTimeOffset.MinValue, "nenhuma")
        : LicenseStatus.NotEnforced;

    public LicenseStatus Current => current;

    public bool IsEnforced => opt.Enforce;

    public bool IsAdministrator(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var email = user.FindFirstValue(ClaimTypes.Email) ?? user.Identity.Name;
        return email is not null && administrators.Contains(email);
    }

    /// <summary>Contas novas só são bloqueadas com a faixa cheia; quem já existe continua entrando.</summary>
    public bool CanCreateAccount => !current.IsEnforced || !current.AtUserLimit;

    /// <summary>
    /// Relê a chave (tela, depois License:Key, depois License:KeyFile), a data da primeira subida e
    /// os usuários ativos, e recalcula o estado. Chamado na subida, de hora em hora e ao instalar
    /// uma chave. Nunca lança: sem banco, a avaliação vale a partir de agora e a chave da
    /// configuração ainda é verificada, para o bloqueio de build funcionar antes das migrations.
    /// </summary>
    public async Task<LicenseStatus> RefreshAsync(CancellationToken ct = default)
    {
        if (!opt.Enforce)
        {
            current = LicenseStatus.NotEnforced;
            return current;
        }

        var now = clock.GetUtcNow();
        string? key = null;
        var source = "nenhuma";
        DateTimeOffset firstStartedAt = now;
        var activeUsers = 0;
        var databaseReady = false;

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var settings = await db.InstanceSettings
                .Where(s => s.Key == InstanceSettingKeys.LicenseKey || s.Key == InstanceSettingKeys.FirstStartedAtUtc)
                .ToListAsync(ct);
            databaseReady = true;

            var stored = settings.FirstOrDefault(s => s.Key == InstanceSettingKeys.LicenseKey)?.Value;
            if (!string.IsNullOrWhiteSpace(stored))
            {
                key = stored;
                source = "tela";
            }

            var first = settings.FirstOrDefault(s => s.Key == InstanceSettingKeys.FirstStartedAtUtc);
            if (first is not null && DateTimeOffset.TryParse(first.Value, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                firstStartedAt = parsed;
            }
            else
            {
                db.InstanceSettings.Add(new InstanceSetting { Key = InstanceSettingKeys.FirstStartedAtUtc, Value = now.ToString("O"), UpdatedAtUtc = now.UtcDateTime });
                await db.SaveChangesAsync(ct);
            }

            var since = now.UtcDateTime.AddDays(-opt.ActiveUserWindowDays);
            activeUsers = await db.Users.CountAsync(u => u.LastLoginAtUtc != null && u.LastLoginAtUtc >= since, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "Licença: banco indisponível ao ler as configurações da instância; usando só a configuração.");
        }

        if (key is null && !string.IsNullOrWhiteSpace(opt.Key))
        {
            key = opt.Key;
            source = "License:Key";
        }

        if (key is null && !string.IsNullOrWhiteSpace(opt.KeyFile))
        {
            try
            {
                key = await File.ReadAllTextAsync(opt.KeyFile, ct);
                source = "License:KeyFile";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger?.LogWarning(ex, "Licença: não consegui ler License:KeyFile em {Path}.", opt.KeyFile);
            }
        }

        LicenseClaims? claims = null;
        string? error = null;
        if (key is not null && !LicenseKeyCodec.TryDecode(key, opt.PublicKey, out claims, out error))
        {
            claims = null;
        }

        var status = Evaluate(claims, error, now, firstStartedAt, buildInfo.BuiltAt, activeUsers, opt) with { Source = key is null ? "nenhuma" : source };
        var previous = current;
        current = status;
        if (previous.State != status.State || previous.BlocksThisBuild != status.BlocksThisBuild)
        {
            logger?.LogInformation("Licença: {State}{Block} ({Detail}; banco {Db}).",
                status.State, status.BlocksThisBuild ? ", build bloqueada" : "",
                claims is null ? status.Error ?? "sem chave" : LicenseKeyCodec.Describe(claims),
                databaseReady ? "ok" : "indisponível");
        }

        return status;
    }

    /// <summary>
    /// As regras, sem banco nem relógio de verdade, para os testes cobrirem os cinco estados.
    /// </summary>
    public static LicenseStatus Evaluate(LicenseClaims? claims, string? keyError, DateTimeOffset now, DateTimeOffset firstStartedAt, DateTimeOffset? builtAt, int activeUsers, LicenseOptions options)
    {
        if (!options.Enforce)
        {
            return LicenseStatus.NotEnforced;
        }

        if (claims is null)
        {
            // Sem chave (ou chave que não verifica): vale a avaliação contada da primeira subida.
            var trialEndsAt = firstStartedAt.AddDays(options.TrialDays);
            var updatesUntil = trialEndsAt + LicenseClaims.DefaultUpdateGrace;
            var state = keyError is not null ? LicenseState.Invalid : now < trialEndsAt ? LicenseState.Trial : LicenseState.Expired;
            var error = keyError ?? (state is LicenseState.Expired ? "O período de avaliação terminou." : null);
            return new LicenseStatus(state, null, error, trialEndsAt, updatesUntil, Blocks(builtAt, updatesUntil), activeUsers, null, now, "nenhuma");
        }

        var licensed = now >= claims.ExpiresAt ? LicenseState.Expired
            : now >= claims.ExpiresAt.AddDays(-options.WarningDays) ? LicenseState.ExpiringSoon
            : LicenseState.Valid;
        return new LicenseStatus(licensed, claims, licensed is LicenseState.Expired ? "A licença venceu." : null,
            claims.ExpiresAt, claims.UpdatesUntil, Blocks(builtAt, claims.UpdatesUntil), activeUsers, claims.MaxActiveUsers, now, "chave");
    }

    // Build local (sem BuildTimestamp) nunca é bloqueada: é o desenvolvedor rodando na máquina.
    private static bool Blocks(DateTimeOffset? builtAt, DateTimeOffset updatesUntil) =>
        builtAt is not null && builtAt.Value > updatesUntil;

    /// <summary>Verifica e guarda a chave colada na tela. Uma chave que não verifica não é guardada.</summary>
    public async Task<(bool Ok, string Message)> InstallKeyAsync(string? key, CancellationToken ct = default)
    {
        var text = (key ?? "").Trim();
        if (!LicenseKeyCodec.TryDecode(text, opt.PublicKey, out var claims, out var error))
        {
            return (false, error);
        }

        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var setting = await db.InstanceSettings.FindAsync([InstanceSettingKeys.LicenseKey], ct);
            if (setting is null)
            {
                db.InstanceSettings.Add(new InstanceSetting { Key = InstanceSettingKeys.LicenseKey, Value = text, UpdatedAtUtc = now });
            }
            else
            {
                setting.Value = text;
                setting.UpdatedAtUtc = now;
            }

            await db.SaveChangesAsync(ct);
        }

        var status = await RefreshAsync(ct);
        logger?.LogInformation("Licença instalada pela tela: {License}.", LicenseKeyCodec.Describe(claims));
        return (true, status.State is LicenseState.Expired
            ? $"Chave de {claims.Customer} instalada, mas ela venceu em {claims.ExpiresAt:dd/MM/yyyy}."
            : $"Licença de {claims.Customer} instalada: válida até {claims.ExpiresAt:dd/MM/yyyy}.");
    }

    /// <summary>
    /// Carimba o último login. Só grava quando o carimbo anterior tem mais de uma hora, para um
    /// usuário que entra e sai o dia inteiro não virar uma escrita por login.
    /// </summary>
    public async Task RecordLoginAsync(string userId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var threshold = now - LoginStampResolution;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var stale = db.Users.Where(u => u.Id == userId && (u.LastLoginAtUtc == null || u.LastLoginAtUtc < threshold));
        if (db.Database.IsRelational())
        {
            await stale.ExecuteUpdateAsync(s => s.SetProperty(u => u.LastLoginAtUtc, now), ct);
            return;
        }

        var user = await stale.FirstOrDefaultAsync(ct);
        if (user is not null)
        {
            user.LastLoginAtUtc = now;
            await db.SaveChangesAsync(ct);
        }
    }
}
