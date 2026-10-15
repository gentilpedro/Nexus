# Deploy na VPS (nexus.aamora.com.br)

O Nexus roda numa VPS Locaweb (Rocky Linux 10) que também hospeda o aamora. Por enquanto em
paralelo com o MonsterASP: todo push na `main` publica nos dois lugares, cada um com o próprio
banco, até a virada (seção 4).

## 1. Como a requisição chega

```
navegador → Cloudflare (proxy, WAF) → Nginx do host :443 → 127.0.0.1:5100 → container web :8080
                                                                              └→ container db (Postgres 18)
```

- O firewall (firewalld) só aceita 80/443 vindos dos IPs do Cloudflare; o resto bate em `return 444`.
- TLS na origem usa o certificado de origem do Cloudflare `*.aamora.com.br` (`snippets/aamora-ssl.conf`).
- O Nginx entrega o IP real em `X-Forwarded-For` (vem do `CF-Connecting-IP`) e o app confia só no
  gateway da rede Docker (`KNOWN_PROXIES=172.30.0.1`).

## 2. O que fica no servidor

| Caminho | O que é |
|---|---|
| `/opt/nexus/docker-compose.yml` | Cópia de `deploy/vps/docker-compose.yml` |
| `/opt/nexus/.env` | Senha do Postgres e do certificado de Data Protection. Gerado no servidor, **não está em lugar nenhum além dele** |
| `/opt/nexus/dataprotection.pfx` | Cifra as chaves de Data Protection em repouso. Perder = todo mundo deslogado |
| `/opt/nexus/brevo.env` | Escrito a cada deploy a partir dos secrets do GitHub |
| `/opt/nexus/backups/` | `nexus-backup` diário (03:45): dump do banco + anexos, 7 dias |
| `/etc/nginx/conf.d/nexus.conf` | Cópia de `deploy/vps/nexus.conf` |
| `/usr/local/bin/nexus-deploy` | Único comando que o usuário `deploy` (chave do Actions) pode rodar |

Os arquivos de `deploy/vps/` não são copiados automaticamente: mudou um deles, copie para o servidor.

## 3. O deploy

Job `deploy-vps` em `.github/workflows/deploy-nexus.yml`:

1. Empacota a pasta `publish/` do job `verify` com `deploy/vps/Dockerfile` (não recompila).
2. `docker save | gzip` → SSH → `nexus-deploy load-image <versão>`.
3. `nexus-deploy up <versão>` espera o container ficar saudável; se não ficar, volta para a versão
   anterior e falha o job com os logs.
4. Confere `https://nexus.aamora.com.br/health/live` de fora.

Secrets do repositório: `VPS_HOST`, `VPS_SSH_KEY`, `VPS_KNOWN_HOSTS` (+ os da Brevo, já existentes).

Rollback manual (o servidor guarda as 3 últimas imagens):

```bash
docker images nexus-web
sudo /usr/local/bin/nexus-deploy up 1.4.2
```

## 4. Virada do MonsterASP para a VPS

1. Tirar snapshot da VPS no painel da Locaweb.
2. Colocar o MonsterASP em manutenção (`app_offline.htm`) para não entrar dado novo no Neon.
3. Copiar o banco: `pg_dump --format=custom --no-owner` do Neon → `pg_restore --clean --no-owner`
   no container `nexus-db-1`.
4. Copiar `App_Data/uploads` do MonsterASP (FTP) para o volume `nexus_uploads`.
5. Remover o job `deploy` (FTP) e trocar `release` para depender de `deploy-vps`; apontar o
   `backup-nexus.yml` para o backup da VPS ou removê-lo.
6. Restringir o environment `vps` à branch `main`.

As chaves de Data Protection do MonsterASP são DPAPI (Windows) e não abrem no Linux: depois da
virada, todos os usuários precisam entrar de novo. Nenhum dado se perde.
