# Documentação do Nexus

O Nexus reúne num produto só o que equipes costumam espalhar em várias ferramentas: quadros e
sprints, docs e planilhas, chat, tudo organizado em **workspaces** independentes. Esta
documentação é para três leitores diferentes. Escolha o seu.

<div class="grid cards" markdown>

-   :material-book-open-variant: **Guia do usuário**

    ---

    Como usar o Nexus no dia a dia: workspaces, tarefas, quadro, sprints, docs, chat, conta e
    planos.

    [Começar pelo guia](guia/index.md)

-   :material-api: **Integrações**

    ---

    Para programadores: chaves de API, rotas de tarefas, webhooks de saída, receitas em C#,
    Python e curl, e o que o Nexus conecta (Stripe, e-mail, Redis, GitHub e GitLab).

    [Ver as integrações](integracoes/index.md)

-   :material-server: **Self-hosted**

    ---

    Para a TI do cliente do plano Business: instalar a imagem Docker na própria rede, configurar,
    fazer backup e atualizar.

    [Instalar o Nexus](self-hosted/index.md)

-   :material-file-code: **Referência da API**

    ---

    A especificação OpenAPI lida direto da versão em produção, navegável e testável.

    [Abrir a referência](api/index.md)

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
