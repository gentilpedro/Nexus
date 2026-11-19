# Configuração

Tudo por variável de ambiente do container. Chaves com dois pontos no `appsettings` viram dois
sublinhados na variável (`Smtp:Host` → `Smtp__Host`); listas ganham índice (`__0`, `__1`). Esta
página lista **todas** as variáveis que o app lê e termina com o
[compose completo](#compose-completo), comentado linha a linha.

## Obrigatórias

| Variável | O quê |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production`. É o que liga o certificado de Data Protection obrigatório e o nível de log `Warning` |
| `ConnectionStrings__DefaultConnection` | PostgreSQL: `Host=...;Port=5432;Database=Nexus;Username=...;Password=...` |
| `PUBLIC_BASE_URL` | Endereço público com `https`, usado nos links de e-mail, no campo `url` da API, no feed iCal e no MCP |
| `AllowedHosts` | Os nomes pelos quais o app pode ser chamado, separados por `;`. Inclua `localhost` para o health check da imagem |
| `DataProtection__CertificatePath` e `__CertificatePassword` | Certificado que cifra as chaves de sessão. Sem ele o app não sobe em produção |
| `KNOWN_PROXIES` | IP do proxy reverso de quem o app aceita `X-Forwarded-For` (gateway da rede Docker na instalação de referência). Sem proxy, deixe fora |

## E-mail

| Variável | O quê |
|---|---|
| `Smtp__Host`, `Smtp__Port` | Servidor SMTP, 587 com STARTTLS ou 25 em relay interno. Host vazio desliga o envio (nada falha, só não manda) |
| `Smtp__UserName`, `Smtp__Password` | Credenciais, se o servidor exigir |
| `Smtp__SenderEmail`, `Smtp__SenderName` | Remetente dos e-mails |

Provedores, teste e o que fazer sem SMTP: [E-mail (SMTP)](email.md).

## Licença

| Variável | Padrão | O quê |
|---|---|---|
| `License__Key` | vazio | A chave `NXL-...` recebida da Aamora; vazio = 30 dias de avaliação |
| `License__KeyFile` | vazio | Alternativa: caminho de um arquivo montado no container com a chave |
| `License__Administrators__0`, `__1`... | vazio | Fixa quem administra a instalação (telas de licença e Active Directory). Vazio: a primeira pessoa que entra administra e promove outras pela tela |
| `License__Enforce` | `true` | Exige licença. Só a oferta em nuvem da Aamora desliga |

Detalhes, avaliação e vencimento: [Licença](licenca.md).

## Planos e contatos

| Variável | O quê |
|---|---|
| `Plans__EnforceLimits` | `true` aplica os limites de plano (membros, workspaces, recursos pagos). Numa instalação Business fica `false`: tudo liberado em todos os workspaces |
| `Plans__UnlimitedAccounts__0`, `__1`... | E-mails de contas nunca limitadas, mesmo com `EnforceLimits` ligado |
| `Stripe__SecretKey`, `Stripe__WebhookSecret`, `Stripe__ProPriceId` | Só para a oferta em nuvem; numa instalação self-hosted ficam vazias e a cobrança fica desligada |
| `Support__Email`, `Support__WhatsAppNumber`, `Commercial__Email` | Contatos de suporte e comercial mostrados nas telas. Já vêm na imagem com os da Aamora, que é quem atende; não precisa mudar |

## Integrações

| Variável | Padrão | O quê |
|---|---|---|
| `Webhooks__AllowInsecureUrls` | `false` | Só em laboratório: aceita webhooks `http` e endereços internos |
| `Docs__SiteUrl` | site da Aamora | Origem liberada no CORS para ler a especificação OpenAPI (só `GET`). Já vem na imagem |

API, MCP, webhooks e feed iCal no seu endereço: [Integrações na sua instalação](integracoes.md).

## Active Directory

O Active Directory é configurado **pela tela** (`/admin/diretorio`), não por variável: servidor,
DN base, conta de serviço, domínios e filtro ficam no banco, com a senha cifrada. A única variável
é opcional:

| Variável | O quê |
|---|---|
| `LDAPTLS_CACERT` | Caminho, dentro do container, da CA interna que assinou o certificado do controlador de domínio, se você não quiser marcar "aceitar certificado autoassinado". Monte o arquivo por volume |

Passo a passo: [Active Directory](active-directory.md).

## Operação

| Variável | Padrão | O quê |
|---|---|---|
| `APPLY_MIGRATIONS_ON_STARTUP` | `true` | Aplica as migrations do banco ao subir |
| `UPLOADS_PATH` | `App_Data/uploads` | Pasta dos anexos (monte um volume) |
| `Redis__ConnectionString` | vazio | Com valor, chat, docs e limites passam pelo Redis; necessário com mais de uma instância |
| `AUDIT_LOG_RETENTION_DAYS` | 730 | Retenção do log de auditoria |
| `NOTIFICATION_RETENTION_DAYS` | 180 | Retenção das notificações |
| `WEBHOOK_DELIVERY_RETENTION_DAYS` | 30 | Retenção das entregas de webhook |
| `COMMERCIAL_CONTACT_RETENTION_DAYS` | 365 | Retenção dos contatos do formulário comercial |
| `Logging__LogLevel__Default` | `Warning` | Nível de log; `Information` para investigar |

## Logs

O app escreve no stdout do container (`docker compose logs web`). O nível padrão em produção é
`Warning`; para investigar algo, suba temporariamente `Logging__LogLevel__Default=Information`.

## Compose completo

O `docker-compose.yml` da [Instalação](instalacao.md) com **todas** as variáveis: as ligadas
com o valor da instalação de referência, as opcionais comentadas com o padrão. Segredos e valores
que mudam por instalação vêm do `.env`.

```yaml
name: nexus
services:
  db:
    image: postgres:18-alpine
    restart: unless-stopped
    env_file: .env                      # POSTGRES_DB, POSTGRES_USER, POSTGRES_PASSWORD
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
    ports: ["127.0.0.1:5100:8080"]      # só o proxy reverso do host fala com o app
    env_file: .env
    environment:
      # ----- Obrigatórias -----
      ASPNETCORE_ENVIRONMENT: Production
      ConnectionStrings__DefaultConnection: "Host=db;Port=5432;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"
      # Endereço público: links de e-mail, campo url da API, MCP e iCal.
      PUBLIC_BASE_URL: https://nexus.suaempresa.com.br
      # Nomes pelos quais o app aceita ser chamado (localhost atende o health check da imagem).
      AllowedHosts: "nexus.suaempresa.com.br;localhost"
      # IP do proxy reverso de quem o app aceita X-Forwarded-For: o gateway da rede abaixo.
      KNOWN_PROXIES: 172.30.0.1
      # Certificado que cifra as chaves de sessão; sem ele o app se recusa a subir.
      DataProtection__CertificatePath: /app/dp/dataprotection.pfx
      DataProtection__CertificatePassword: ${DP_CERT_PASSWORD}

      # ----- E-mail (o seu SMTP; valores no .env) -----
      Smtp__Host: ${SMTP_HOST}
      Smtp__Port: ${SMTP_PORT}
      Smtp__UserName: ${SMTP_USER}
      Smtp__Password: ${SMTP_PASSWORD}
      Smtp__SenderEmail: ${SMTP_SENDER_EMAIL}
      Smtp__SenderName: ${SMTP_SENDER_NAME}

      # ----- Licença -----
      License__Key: ${NEXUS_LICENSE_KEY}
      # Alternativa à chave inline: arquivo montado no container.
      # License__KeyFile: /app/license/nexus.key
      # Fixa quem administra a instalação (telas de licença e Active Directory). Mais de um: __1, __2...
      # Vazio ou ausente: a primeira pessoa que entrar administra e promove outras pela tela.
      License__Administrators__0: ti@suaempresa.com.br
      # Padrão true: sem chave válida, 30 dias de avaliação e depois bloqueio de atualização.
      # License__Enforce: "true"

      # ----- Planos -----
      # Business: nada é limitado por plano (membros, workspaces, sprints, Gantt, API, MCP, webhooks).
      Plans__EnforceLimits: "false"
      # Contas nunca limitadas mesmo com EnforceLimits true.
      # Plans__UnlimitedAccounts__0: ti@suaempresa.com.br
      # Cobrança da oferta em nuvem: desligada (já vêm vazias na imagem).
      # Stripe__SecretKey: ""
      # Stripe__WebhookSecret: ""
      # Stripe__ProPriceId: ""

      # ----- Contatos mostrados nas telas (já vêm na imagem com os da Aamora, que atende o suporte) -----
      # Support__Email: suportenexus@aamora.com.br
      # Support__WhatsAppNumber: ""
      # Commercial__Email: comercialnexus@aamora.com.br
      # Origem liberada no CORS da especificação OpenAPI (só GET); já vem na imagem.
      # Docs__SiteUrl: https://docs-nexus.aamora.com.br

      # ----- Active Directory (configurado pela tela; só a CA interna entra por aqui) -----
      # LDAPTLS_CACERT: /app/ca/empresa-ca.pem     # e monte o arquivo em volumes

      # ----- Operação (padrões da imagem) -----
      APPLY_MIGRATIONS_ON_STARTUP: "true"
      # UPLOADS_PATH: App_Data/uploads            # pasta dos anexos (volume uploads abaixo)
      # AUDIT_LOG_RETENTION_DAYS: "730"
      # NOTIFICATION_RETENTION_DAYS: "180"
      # WEBHOOK_DELIVERY_RETENTION_DAYS: "30"
      # COMMERCIAL_CONTACT_RETENTION_DAYS: "365"
      # Webhooks só para https e endereços públicos; true apenas em laboratório.
      # Webhooks__AllowInsecureUrls: "false"
      # Mais de uma instância do app atrás de um balanceador: chat, docs e limites pelo Redis.
      # Redis__ConnectionString: "redis:6379"
      # Log no stdout; Information para investigar.
      # Logging__LogLevel__Default: Warning
    volumes:
      - uploads:/app/App_Data/uploads
      - ./dataprotection.pfx:/app/dp/dataprotection.pfx:ro
      # - ./empresa-ca.pem:/app/ca/empresa-ca.pem:ro   # CA interna para o LDAPS, se usar
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

E o `.env` correspondente (segredos; `chmod 600`):

```bash
POSTGRES_DB=Nexus
POSTGRES_USER=postgres
POSTGRES_PASSWORD=...
DP_CERT_PASSWORD=...
NEXUS_LICENSE_KEY=NXL-...
NEXUS_REGISTRY_TOKEN=ghp_...
SMTP_HOST=smtp.suaempresa.com.br
SMTP_PORT=587
SMTP_USER=nexus@suaempresa.com.br
SMTP_PASSWORD=...
SMTP_SENDER_EMAIL=nexus@suaempresa.com.br
SMTP_SENDER_NAME=Nexus
```

Depois de mudar qualquer variável: `docker compose up -d --wait web` recria o container com os
valores novos; não é preciso derrubar o banco.
