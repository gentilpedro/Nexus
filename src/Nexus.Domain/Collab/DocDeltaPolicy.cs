using System.Text.Json;

namespace Nexus.Domain.Collab;

/// <summary>
/// O que uma alteração num documento colaborativo pode fazer: quais formatos, com quais valores,
/// e dentro de quais limites. É a regra que o servidor aplica antes de aceitar uma operação.
/// </summary>
/// <remarks>
/// <para>
/// A lista de formatos é a mesma que o editor habilita no navegador (opção <c>formats</c> do
/// Quill). Qualquer outro formato só apareceria num Delta montado à mão, então é recusado.
/// </para>
/// <para>
/// Não é a barreira contra XSS: o Delta nunca vira HTML no servidor. Quem desenha o Delta é o
/// Quill, que sanitiza link e imagem na criação, e o HTML do modo leitura passa pelo
/// <c>HtmlContentSanitizer</c>. O papel desta política é manter o documento dentro do formato e do
/// tamanho que o sistema sabe tratar.
/// </para>
/// </remarks>
public static class DocDeltaPolicy
{
    /// <summary>Tamanho máximo do documento, em posições (unidades UTF-16; embed conta 1).</summary>
    public const int MaxDocumentLength = 500_000;

    /// <summary>Tamanho máximo do JSON de uma única alteração (uma colagem grande cabe folgada).</summary>
    public const int MaxChangeJsonLength = 512 * 1024;

    private const int MaxStringValueLength = 2048;

    /// <summary>Formatos permitidos. Precisa bater com <c>formats</c> em <c>doc-collab-interop.js</c>.</summary>
    public static IReadOnlyCollection<string> AllowedFormats => Validators.Keys;

    private static readonly Dictionary<string, Func<AttributeValue, bool>> Validators = new(StringComparer.Ordinal)
    {
        ["bold"] = IsTrue,
        ["italic"] = IsTrue,
        ["underline"] = IsTrue,
        ["strike"] = IsTrue,
        ["code"] = IsTrue,
        ["blockquote"] = IsTrue,
        ["link"] = IsShortString,
        ["color"] = v => IsShortString(v) && v.Text!.Length <= 64,
        ["background"] = v => IsShortString(v) && v.Text!.Length <= 64,
        ["header"] = v => IsIntegerBetween(v, 1, 6),
        ["indent"] = v => IsIntegerBetween(v, 1, 8),
        ["list"] = v => IsOneOf(v, "ordered", "bullet", "checked", "unchecked"),
        ["align"] = v => IsOneOf(v, "center", "right", "justify"),
        ["script"] = v => IsOneOf(v, "sub", "super"),
        // O Quill 2 marca bloco de código com true ou com o nome da linguagem ("plain").
        ["code-block"] = v => v.Kind == JsonValueKind.True || (IsShortString(v) && v.Text!.Length <= 32),
    };

    /// <summary>Documento vazio do Quill: sempre existe a quebra de linha final.</summary>
    public static Delta EmptyDocument() => new Delta().Insert("\n");

    /// <summary>
    /// Aplica <paramref name="change"/> a <paramref name="document"/> se a alteração for válida
    /// e o resultado continuar sendo um documento que o editor aceita.
    /// </summary>
    public static bool TryApply(Delta document, Delta change, out Delta result, out string? error)
    {
        result = document;

        if (change.Ops.Count == 0)
        {
            error = "Alteração vazia.";
            return false;
        }

        // Sem essa checagem, um retain além do fim viraria retain "infinito" no compose e o
        // resultado deixaria de ser um documento.
        if (change.BaseLength() > document.Length())
        {
            error = "A alteração vai além do fim do documento.";
            return false;
        }

        foreach (var op in change.Ops)
        {
            if (!IsAllowed(op, out error))
            {
                return false;
            }
        }

        var composed = document.Compose(change);

        if (!composed.IsDocument())
        {
            error = "O resultado não é um documento.";
            return false;
        }

        // O Quill exige a quebra de linha final; sem ela o editor recriaria uma localmente e o
        // documento do navegador deixaria de ser igual ao do servidor.
        if (composed.Ops.Count == 0 || composed.Ops[^1] is not { IsTextInsert: true } last || !last.Text!.EndsWith('\n'))
        {
            error = "O documento precisa terminar em quebra de linha.";
            return false;
        }

        // Uma posição no meio de um emoji (entre as duas metades do par UTF-16) deixaria meio
        // caractere no documento, que não dá mais para gravar nem mostrar. O editor nunca faz
        // isso, porque o cursor não para ali dentro.
        if (composed.Ops.Any(op => op.Text is not null && HasLoneSurrogate(op.Text)))
        {
            error = "A alteração divide um caractere ao meio.";
            return false;
        }

        if (composed.Length() > MaxDocumentLength)
        {
            error = $"O documento passaria do limite de {MaxDocumentLength:N0} caracteres.";
            return false;
        }

        result = composed;
        error = null;
        return true;
    }

    private static bool IsAllowed(DeltaOp op, out string? error)
    {
        if (op.Embed is { } embed)
        {
            // Só imagem por endereço http(s). Imagem colada vira data: URL de centenas de KB e já
            // era descartada pelo sanitizador do HTML; o editor remove antes de chegar aqui.
            if (embed.Type != "image"
                || embed.Value.Kind != JsonValueKind.String
                || embed.Value.Text!.Length > MaxStringValueLength
                || !(embed.Value.Text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                     || embed.Value.Text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
            {
                error = "Só é permitido embed de imagem por endereço http(s).";
                return false;
            }
        }

        if (op.Attributes is { } attributes)
        {
            foreach (var (key, value) in attributes.Values)
            {
                if (!Validators.TryGetValue(key, out var isValid))
                {
                    error = $"Formato não permitido: {key}.";
                    return false;
                }
                // null remove o formato e vale para qualquer formato permitido.
                if (!value.IsNull && !isValid(value))
                {
                    error = $"Valor inválido para o formato {key}.";
                    return false;
                }
            }
        }

        error = null;
        return true;
    }

    private static bool HasLoneSurrogate(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                {
                    return true;
                }
                i++;
            }
            else if (char.IsLowSurrogate(text[i]))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsTrue(AttributeValue value) => value.Kind == JsonValueKind.True;

    private static bool IsShortString(AttributeValue value) =>
        value.Kind == JsonValueKind.String && value.Text!.Length <= MaxStringValueLength;

    private static bool IsIntegerBetween(AttributeValue value, int min, int max) =>
        value.Kind == JsonValueKind.Number
        && value.Number == Math.Floor(value.Number)
        && value.Number >= min
        && value.Number <= max;

    private static bool IsOneOf(AttributeValue value, params string[] allowed) =>
        value.Kind == JsonValueKind.String && allowed.Contains(value.Text, StringComparer.Ordinal);
}
