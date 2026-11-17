using System.Net.Mime;
using System.Text.Encodings.Web;

namespace Nexus.Web.Services.Licensing;

/// <summary>
/// O bloqueio de atualização (#81): quando esta imagem foi construída depois de
/// <c>updatesUntil</c> da licença, o container sobe (o compose vê o <c>/health/live</c> saudável
/// e não fica reiniciando), mas toda página responde 503 com a explicação e o comando de voltar
/// para a versão anterior. As migrations não rodam (ver Program.cs), então a volta é limpa.
/// </summary>
public sealed class LicenseGateMiddleware(RequestDelegate next, LicenseService license)
{
    private static readonly string[] AllowedPaths = ["/health/live", "/version"];

    public async Task InvokeAsync(HttpContext context)
    {
        var status = license.Current;
        if (!status.BlocksThisBuild || AllowedPaths.Any(p => context.Request.Path.Equals(p, StringComparison.OrdinalIgnoreCase)))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = MediaTypeNames.Text.Html + "; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsync(BuildPage(status, AppVersion.Current));
    }

    internal static string BuildPage(LicenseStatus status, AppVersionInfo build)
    {
        var h = HtmlEncoder.Default;
        var who = status.Claims is null ? "o período de avaliação" : $"a licença de {h.Encode(status.Claims.Customer)}";
        var until = status.UpdatesUntil?.ToString("dd/MM/yyyy") ?? "?";
        var builtAt = build.BuiltAt?.ToString("dd/MM/yyyy") ?? "?";
        return $$"""
            <!doctype html>
            <html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Nexus · atualização bloqueada pela licença</title>
            <style>
              body{font-family:system-ui,sans-serif;background:#f6f7f9;color:#1b1f24;margin:0;padding:48px 16px}
              main{max-width:640px;margin:0 auto;background:#fff;border:1px solid #e3e6ea;border-radius:12px;padding:32px}
              h1{font-size:22px;margin:0 0 12px}p{line-height:1.5}code,pre{background:#f0f2f5;border-radius:6px;padding:2px 6px}
              pre{padding:12px;overflow:auto}
            </style></head><body><main>
            <h1>Esta versão do Nexus não pode subir com a licença atual</h1>
            <p>A imagem <strong>{{h.Encode(build.Version)}}</strong> foi construída em {{builtAt}}, e {{who}} só cobre atualizações
            publicadas até <strong>{{until}}</strong>. Os dados não foram tocados: nenhuma migration rodou.</p>
            <p>Para voltar ao ar agora, suba a versão anterior:</p>
            <pre>NEXUS_VERSION=&lt;versão anterior&gt; docker compose up -d --wait web</pre>
            <p>Para usar esta versão, renove a licença com a Aamora (comercialnexus@aamora.com.br), instale a chave nova em
            <code>License__Key</code> e suba de novo.</p>
            </main></body></html>
            """;
    }
}
