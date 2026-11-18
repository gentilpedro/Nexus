---
hide:
  - navigation
  - toc
---

# Especificação OpenAPI

A especificação abaixo é lida direto do Nexus em produção (`/openapi/v1.json`), então reflete
exatamente a versão no ar. Para o passo a passo e as regras de cada campo, veja a
[Referência da API](../integracoes/api-referencia.md) e as [Receitas](../integracoes/api-receitas.md).

<div id="nx-openapi">
  <div class="nx-openapi__state" role="status">
    <span class="nx-openapi__spinner" aria-hidden="true"></span>
    <p>Carregando a especificação da versão em produção…</p>
  </div>
</div>

<script src="https://cdn.jsdelivr.net/npm/@scalar/api-reference@1.80.0"></script>
<script>
  (function () {
    // A especificação vem do app (CORS liberado só para este site, ver Docs:SiteUrl no Program.cs).
    // Buscar antes de montar o Scalar permite mostrar um erro legível quando ela não carrega.
    var specUrl = 'https://nexus.aamora.com.br/openapi/v1.json';
    var host = document.getElementById('nx-openapi');

    // Cores do Nexus (DESIGN.md) dentro do Scalar, nos dois temas.
    var customCss = [
      '.scalar-app, .scalar-api-reference { --scalar-font: "Inter", system-ui, sans-serif; --scalar-font-code: ui-monospace, "Cascadia Mono", Menlo, Consolas, monospace; --scalar-radius: 6px; --scalar-radius-lg: 8px; --scalar-radius-xl: 12px; --scalar-custom-header-height: 48px; }',
      '.light-mode { --scalar-background-1: #FFFFFF; --scalar-background-2: #F7F8FC; --scalar-background-3: #F1F3F8; --scalar-background-accent: #EEF0FF; --scalar-color-1: #111827; --scalar-color-2: #4B5563; --scalar-color-3: #6B7280; --scalar-color-accent: #4B4CD8; --scalar-border-color: #E5E7EB; --scalar-button-1: #5B5CEB; --scalar-button-1-hover: #4B4CD8; --scalar-button-1-color: #FFFFFF; --scalar-color-green: #15803D; --scalar-color-blue: #1D4ED8; --scalar-color-orange: #C2410C; --scalar-color-yellow: #92400E; --scalar-color-red: #B91C1C; --scalar-color-purple: #7E22CE; }',
      '.dark-mode { --scalar-background-1: #1A2235; --scalar-background-2: #161E2F; --scalar-background-3: #202A3C; --scalar-background-accent: #2A3060; --scalar-color-1: #F9FAFB; --scalar-color-2: #D1D5DB; --scalar-color-3: #9CA3AF; --scalar-color-accent: #A5A8FF; --scalar-border-color: #334155; --scalar-button-1: #5B5CEB; --scalar-button-1-hover: #4B4CD8; --scalar-button-1-color: #FFFFFF; --scalar-color-green: #86EFAC; --scalar-color-blue: #93C5FD; --scalar-color-orange: #FDBA74; --scalar-color-yellow: #FCD34D; --scalar-color-red: #FCA5A5; --scalar-color-purple: #D8B4FE; }'
    ].join('\n');

    function scheme() {
      return document.body.getAttribute('data-md-color-scheme') === 'slate' ? 'dark' : 'light';
    }

    function showError() {
      host.innerHTML =
        '<div class="nx-openapi__state" role="alert">' +
        '<strong>Não foi possível carregar a especificação.</strong>' +
        '<p>O Nexus em produção não respondeu ou bloqueou a leitura por este site. ' +
        'Tente de novo em alguns minutos, abra o <a href="' + specUrl + '">arquivo JSON</a> direto ' +
        'ou consulte a <a href="../integracoes/api-referencia/">Referência da API</a>.</p>' +
        '</div>';
    }

    fetch(specUrl, { headers: { Accept: 'application/json' } })
      .then(function (response) {
        if (!response.ok) { throw new Error('HTTP ' + response.status); }
        return response.json();
      })
      .then(function (spec) {
        host.innerHTML = '';
        var base = {
          content: spec,
          theme: 'default',
          withDefaultFonts: false,
          hideDarkModeToggle: true,
          forceDarkModeState: scheme(),
          hideDownloadButton: false,
          hideClientButton: false,
          customCss: customCss,
          servers: [{ url: 'https://nexus.aamora.com.br', description: 'Produção' }],
          authentication: { preferredSecurityScheme: 'Bearer' }
        };
        var reference = Scalar.createApiReference('#nx-openapi', base);

        // O seletor de tema do site (sol/lua no topo) vale também para a referência.
        new MutationObserver(function () {
          base.forceDarkModeState = scheme();
          reference.updateConfiguration(Object.assign({}, base));
        }).observe(document.body, { attributes: true, attributeFilter: ['data-md-color-scheme'] });
      })
      .catch(showError);
  })();
</script>
