# Deploy na VPS (nexus.aamora.com.br)

O Nexus roda numa VPS Locaweb (Rocky Linux 10) que também hospeda o aamora. Todo push na `main`
publica aqui. Até 10/2026 rodava no MonsterASP (IIS, deploy por FTPS) com banco no Neon — a virada
está na seção 4.

## 1. Como a requisição chega

```
navegador → Cloudflare (proxy, WAF) → Nginx do host :443 → 127.0.0.1:5100 → container web :8080
                                                                              ├→ container db (Postgres 18)
                                                                              └→ relay SMTP (smtp:25, rede mail) → Resend :587
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

Secrets do repositório: `VPS_HOST`, `VPS_SSH_KEY`, `VPS_KNOWN_HOSTS`.

E-mail: o app envia para o relay SMTP da Aamora (`/opt/smtp`, container na rede Docker externa
`mail`), que repassa pelo Resend na 587 — a VPS não sai pela porta 25. O remetente é
`nao-responda@aamora.com.br` (`appsettings.Production.json`); nenhum segredo de e-mail passa pelo
deploy. A chave do Resend fica só em `/opt/smtp/.env`.
Os de FTP e `PROD_SQL_CONNECTION_STRING` só servem à migração e podem ser apagados depois dela.

Rollback manual (o servidor guarda as 3 últimas imagens):

```bash
docker images nexus-web
sudo /usr/local/bin/nexus-deploy up 1.4.2
```

## 4. Virada do MonsterASP para a VPS

Feita uma vez, pelo workflow manual `migrar-para-vps.yml`, que usa os secrets do Neon e do FTP já
cadastrados no repositório:

1. Snapshot da VPS no painel da Locaweb.
2. No servidor: `touch /opt/nexus/.allow-import` — sem esse arquivo o `nexus-deploy` recusa os
   `import-*`, então a chave de deploy, no dia a dia, não consegue sobrescrever dados.
3. Rodar o workflow digitando `MIGRAR`. Ele:
   - põe um `app_offline.htm` no MonsterASP com link para o endereço novo (para nada novo ser
     gravado no Neon);
   - faz o `pg_dump` do Neon e baixa `App_Data/uploads` por FTPS;
   - `import-db`: guarda o banco atual da VPS em `backups/pre-import-*.dump`, recria o banco e
     restaura o dump;
   - `import-uploads`: extrai os anexos no volume `nexus_uploads`;
   - confere `/health/ready`.
4. No servidor: `rm /opt/nexus/.allow-import`.

As chaves de Data Protection do MonsterASP são DPAPI (Windows) e não abrem no Linux: depois da
virada, todos os usuários precisam entrar de novo. Nenhum dado se perde.
