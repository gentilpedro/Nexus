# Canal de distribuição do plano Business

Como a imagem do Nexus chega ao cliente self-hosted e como a Aamora controla quem baixa. Documento
interno: o lado do cliente está no site, em *Self-hosted → Instalação* e *Atualização*.

## A imagem

- Publicada pelo workflow `deploy-nexus.yml` (job `publish-image`) em todo push na `main`, depois
  do `verify`, como `ghcr.io/aamora-tec/nexus-web:<versão>` e `:latest`. É a mesma pasta
  `publish/` que o `verify` testou e carimbou, montada com `deploy/vps/Dockerfile`; a VPS da
  Aamora continua recebendo a dela por SSH, no job `deploy-vps`.
- O pacote fica **privado** no GitHub Container Registry (conferir em
  https://github.com/orgs/Aamora-Tec/packages → `nexus-web` → *Package settings* → *Danger zone*
  → visibilidade). Ninguém baixa sem token.
- O caminho vem do dono do repositório (`github.repository_owner`, a organização `Aamora-Tec`,
  em minúsculas no nome da imagem). Mudou o dono, muda sozinho; só as docs do site citam o nome.

## Token por cliente

Cada cliente recebe o próprio token, junto com a chave de licença, para poder ser revogado
sozinho.

1. Com a conta do owner da organização: GitHub → *Settings* → *Developer settings* → *Personal
   access tokens* → **Tokens (classic)** → *Generate new token*. Só o classic tem o escopo de
   pacotes; os fine-grained ainda não servem para o GHCR. O token lê os pacotes da organização
   porque a conta é membro dela.
2. Nome: `nexus-business-<cliente>`; expiração: a validade da licença (um ano); escopo:
   **somente `read:packages`**.
3. Mandar ao cliente com a chave `NXL-...`. Guardar os dois no contrato.
4. Cliente não renovou: revogar o token na mesma lista. O que ele já baixou continua rodando,
   como a licença promete; o que não baixou, não baixa.

O `nexus-license issue` lembra de emitir o token ao final.

## O cliente

```bash
echo "$TOKEN" | docker login ghcr.io -u aamora-tec --password-stdin
docker pull ghcr.io/aamora-tec/nexus-web:1.24.0
```

O usuário do `docker login` pode ser qualquer texto; o GHCR só olha o token.

## Rede fechada

Quem não pode sair para a internet recebe o `.tar.gz` à mão:

```bash
docker pull ghcr.io/aamora-tec/nexus-web:1.24.0
docker save ghcr.io/aamora-tec/nexus-web:1.24.0 | gzip > nexus-web-1.24.0.tar.gz
```

e carrega com `docker load < nexus-web-1.24.0.tar.gz`.
