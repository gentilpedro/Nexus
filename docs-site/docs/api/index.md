---
hide:
  - toc
---

# Referência da API (OpenAPI)

A especificação abaixo é lida direto do Nexus em produção (`/openapi/v1.json`), então reflete
exatamente a versão no ar. Para o passo a passo e as regras de cada campo, veja a
[Referência da API](../integracoes/api-referencia.md) e as [Receitas](../integracoes/api-receitas.md).

<div id="app"></div>
<script src="https://cdn.jsdelivr.net/npm/@scalar/api-reference@1.80.0"></script>
<script>
  Scalar.createApiReference('#app', {
    url: 'https://nexus.aamora.com.br/openapi/v1.json',
    theme: 'default',
    hideDownloadButton: false,
    authentication: { preferredSecurityScheme: 'Bearer' },
  })
</script>
