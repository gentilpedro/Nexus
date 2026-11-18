# Instalação

A instalação de referência é a mesma que a Aamora usa em produção: `docker compose` com o banco e
o app, um proxy reverso no host e um certificado para cifrar as chaves de sessão.

## 1. Pasta e segredos

```bash
sudo mkdir -p /opt/nexus && cd /opt/nexus
# Senha do banco e senha do certificado de Data Protection: gere valores longos e guarde-os.
cat > .env <<EOF
POSTGRES_DB=Nexus
POSTGRES_USER=postgres
POSTGRES_PASSWORD=$(openssl rand -base64 32)
DP_CERT_PASSWORD=$(openssl rand -base64 32)
NEXUS_LICENSE_KEY=NXL-...   # a chave recebida da Aamora; vazio = 30 dias de avaliação
NEXUS_REGISTRY_TOKEN=ghp_...  # o token de leitura do registro de imagens, recebido com a chave
SMTP_HOST=smtp.suaempresa.com.br   # o seu servidor de e-mail; ver a página E-mail
SMTP_PORT=587
SMTP_USER=nexus@suaempresa.com.br
SMTP_PASSWORD=...
SMTP_SENDER_EMAIL=nexus@suaempresa.com.br
SMTP_SENDER_NAME=Nexus
EOF
chmod 600 .env
```

## 2. Certificado de Data Protection

O Nexus cifra em repouso as chaves que assinam os cookies de login e os tokens de formulário.
Sem um certificado, o app **se recusa a subir** em produção, de propósito.

```bash
source .env
openssl req -x509 -newkey rsa:2048 -days 3650 -subj "/CN=nexus-dataprotection" \
  -keyout dp.key -out dp.crt -nodes
openssl pkcs12 -export -out dataprotection.pfx -inkey dp.key -in dp.crt -passout "pass:$DP_CERT_PASSWORD"
rm dp.key dp.crt
chmod 600 dataprotection.pfx
```

!!! danger "Faça backup deste arquivo"
    Perder o `dataprotection.pfx` não perde dados, mas desloga todo mundo e invalida links de
    confirmação pendentes. Guarde-o junto com o `.env`.

## 3. `docker-compose.yml`

```yaml
name: nexus
services:
  db:
    image: postgres:18-alpine
    restart: unless-stopped
    env_file: .env
    volumes:
      - pgdata:/var/lib/postgresql
    networks: [nexus]
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U $${POSTGRES_USER} -d $${POSTGRES_DB}"]
      interval: 10s
      retries: 5

  web:
    image: ghcr.io/aamora-tec/nexus-web:${NEXUS_VERSION:?defina NEXUS_VERSION}
    restart: unless-stopped
    ports: ["127.0.0.1:5100:8080"]
    env_file: .env
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ConnectionStrings__DefaultConnection: "Host=db;Port=5432;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"
      APPLY_MIGRATIONS_ON_STARTUP: "true"
      PUBLIC_BASE_URL: https://nexus.suaempresa.com.br
      AllowedHosts: "nexus.suaempresa.com.br;localhost"
      KNOWN_PROXIES: 172.30.0.1
      DataProtection__CertificatePath: /app/dp/dataprotection.pfx
      DataProtection__CertificatePassword: ${DP_CERT_PASSWORD}
      Smtp__Host: ${SMTP_HOST}
      Smtp__Port: ${SMTP_PORT}
      Smtp__UserName: ${SMTP_USER}
      Smtp__Password: ${SMTP_PASSWORD}
      Smtp__SenderEmail: ${SMTP_SENDER_EMAIL}
      Smtp__SenderName: ${SMTP_SENDER_NAME}
      License__Key: ${NEXUS_LICENSE_KEY}
      # Quem administra a instalação (licença e Active Directory): crie a primeira conta com este e-mail.
      License__Administrators__0: ti@suaempresa.com.br
      # Business: nada é limitado por plano.
      Plans__EnforceLimits: "false"
    volumes:
      - uploads:/app/App_Data/uploads
      - ./dataprotection.pfx:/app/dp/dataprotection.pfx:ro
    networks:
      nexus: { gw_priority: 1 }
    depends_on:
      db: { condition: service_healthy }

volumes:
  pgdata:
  uploads:

networks:
  nexus:
    ipam:
      config:
        - subnet: 172.30.0.0/24
          gateway: 172.30.0.1
```

Este é o mínimo; o compose com **todas** as variáveis, comentado, está em
[Configuração](configuracao.md#compose-completo).

`KNOWN_PROXIES` é o gateway da rede Docker: é dele que o app aceita o cabeçalho
`X-Forwarded-For` do proxy. O app expõe só `127.0.0.1:5100`; quem fala com a internet é o proxy.

## 4. Proxy reverso

Exemplo para Nginx. O importante é repassar o WebSocket (o Nexus usa uma conexão persistente por
usuário) e informar o esquema `https`.

```nginx
map $http_upgrade $connection_upgrade { default upgrade; '' close; }

server {
    listen 443 ssl;
    http2 on;
    server_name nexus.suaempresa.com.br;
    ssl_certificate     /etc/ssl/nexus/fullchain.pem;
    ssl_certificate_key /etc/ssl/nexus/privkey.pem;
    client_max_body_size 12m;

    location / {
        proxy_pass http://127.0.0.1:5100;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection $connection_upgrade;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $remote_addr;
        proxy_set_header X-Forwarded-Proto https;
        proxy_read_timeout 1h;
    }
}
```

## 5. Subir

```bash
source .env
echo "$NEXUS_REGISTRY_TOKEN" | docker login ghcr.io -u nexus --password-stdin   # o token que você recebeu com a licença
export NEXUS_VERSION=1.24.0                  # a versão que a Aamora indicou
docker pull ghcr.io/aamora-tec/nexus-web:$NEXUS_VERSION
docker compose up -d --wait
curl -s http://127.0.0.1:5100/health/ready   # Healthy
```

O usuário do `docker login` pode ser qualquer texto; o registro só olha o token. Rede fechada:
peça o `.tar.gz` da versão e carregue com `docker load < nexus-web-1.24.0.tar.gz` no lugar do
`pull`.

O app aplica as migrations do banco sozinho ao subir (`APPLY_MIGRATIONS_ON_STARTUP`).

## 6. Primeira conta

1. Abra `https://nexus.suaempresa.com.br` e crie a conta com o e-mail que está em
   `License__Administrators` (`ti@suaempresa.com.br` no exemplo). É ela que vê **Licença da
   instalação** e **Active Directory** no menu da conta; as demais contas são comuns.
2. Confirme pelo link que chega por e-mail. Se o SMTP ainda não estiver configurado, confirme no
   banco como descrito em [E-mail → Sem SMTP](email.md#sem-smtp).
3. Entre, crie o workspace e convide a equipe. Com o [Active Directory](active-directory.md)
   configurado, a equipe entra com a senha da rede e as contas nascem no primeiro login.

## 7. Conferir

- `https://nexus.suaempresa.com.br/version` mostra a versão.
- `/health/live` e `/health/ready` servem para o seu monitoramento, sem autenticação.
- Mande um convite para si mesmo: se o e-mail chegar, o SMTP está certo ([E-mail](email.md)).
- `/admin/licenca` mostra cliente, validade e usuários ativos ([Licença](licenca.md)).
