---
hide:
  - navigation
  - toc
---

<div class="nx-home" markdown>

<div class="nx-home__top" markdown>

<div class="nx-home__intro" markdown>

# Documentação do Nexus

<p class="nx-home__lead">O Nexus reúne num produto só o que equipes costumam espalhar em várias
ferramentas: quadros e sprints, docs e planilhas, chat, tudo organizado em <strong>workspaces</strong>
independentes. Esta documentação é para três leitores diferentes. Escolha o seu.</p>

<div class="nx-home__actions" markdown>
[Abrir o Nexus](https://nexus.aamora.com.br){ .md-button .md-button--primary }
[Guia do usuário](guia/index.md){ .md-button }
</div>

<div class="nx-home__first-call" markdown>

Primeira chamada à API

```bash
curl https://nexus.aamora.com.br/api/v1/workspace \
  -H "Authorization: Bearer nxk_sua_chave"
```

</div>

</div>

<div class="nx-paths" markdown>

-   :material-book-open-variant: [Guia do usuário](guia/index.md)

    Como usar o Nexus no dia a dia: workspaces, tarefas, quadro, sprints, docs, chat, conta e planos.

-   :material-api: [Integrações](integracoes/index.md)

    Para programadores: chaves de API, rotas de tarefas, webhooks de saída, receitas em C#, Python
    e curl, e o que o Nexus conecta (Stripe, e-mail, Redis, GitHub e GitLab).

-   :material-format-list-bulleted-type: [Referência da API](integracoes/api-referencia.md)

    Cada rota de `/api/v1` com parâmetros, corpo, respostas e exemplo em curl.

-   :material-file-code-outline: [Especificação OpenAPI](api/index.md)

    A especificação lida direto da versão em produção, navegável, com exemplos de chamada em várias linguagens.

-   :material-server-outline: [Self-hosted](self-hosted/index.md)

    Para a TI do cliente do plano Business: instalar a imagem Docker na própria rede, configurar,
    fazer backup e atualizar.

</div>

</div>

## Endereços

| | |
|---|---|
| Aplicação | <https://nexus.aamora.com.br> |
| API | `https://nexus.aamora.com.br/api/v1` |
| Especificação OpenAPI | <https://nexus.aamora.com.br/openapi/v1.json> |
| Código | [GitHub](https://github.com/gentilpedro/Nexus) · [GitLab](https://gitlab.com/senac-projetos-de-desenvolvimento/2025-pedro-gentil/tcc_2/nexus) |
| Suporte | suportenexus@aamora.com.br |

## Versão

A documentação acompanha a `main` do repositório: o que muda no código muda aqui no mesmo
*merge request*. A versão em produção aparece no rodapé do Nexus e em
`https://nexus.aamora.com.br/version`.

</div>
