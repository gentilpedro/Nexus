# Configuração

Tudo por variável de ambiente do container. Chaves com dois pontos no `appsettings` viram dois
sublinhados na variável (`Smtp:Host` → `Smtp__Host`).

## Obrigatórias

| Variável | O quê |
|---|---|
| `ConnectionStrings__DefaultConnection` | PostgreSQL: `Host=...;Port=5432;Database=Nexus;Username=...;Password=...` |
| `PUBLIC_BASE_URL` | Endereço público, usado nos links de e-mail e no campo `url` da API |
| `AllowedHosts` | Os nomes pelos quais o app pode ser chamado, separados por `;` |
| `DataProtection__CertificatePath` e `__CertificatePassword` | Certificado que cifra as chaves de sessão. Sem ele o app não sobe em produção |
| `KNOWN_PROXIES` | IP do proxy reverso de quem o app aceita `X-Forwarded-For` (gateway da rede Docker na instalação de referência) |

## E-mail

| Variável | O quê |
|---|---|
| `Smtp__Host`, `Smtp__Port` | Servidor SMTP. Host vazio desliga o envio (nada falha, só não manda) |
| `Smtp__UserName`, `Smtp__Password` | Credenciais, se o servidor exigir |
| `Smtp__SenderEmail`, `Smtp__SenderName` | Remetente dos e-mails |

## Operação

| Variável | Padrão | O quê |
|---|---|---|
| `APPLY_MIGRATIONS_ON_STARTUP` | `true` | Aplica as migrations do banco ao subir |
| `UPLOADS_PATH` | `App_Data/uploads` | Pasta dos anexos (monte um volume) |
| `Redis__ConnectionString` | vazio | Com valor, chat, docs e limites passam pelo Redis; necessário com mais de uma instância |
| `AUDIT_LOG_RETENTION_DAYS` | 730 | Retenção do log de auditoria |
| `NOTIFICATION_RETENTION_DAYS` | 180 | Retenção das notificações |
| `WEBHOOK_DELIVERY_RETENTION_DAYS` | 30 | Retenção das entregas de webhook |
| `Webhooks__AllowInsecureUrls` | `false` | Só em desenvolvimento: aceita webhooks `http` e endereços internos |

## Planos e contatos

| Variável | O quê |
|---|---|
| `Plans__EnforceLimits` | `true` aplica os limites de plano (membros, workspaces, recursos pagos). Numa instalação Business costuma ficar `false`: tudo liberado |
| `Plans__UnlimitedAccounts` | E-mails de contas sem limite |
| `Support__Email`, `Support__WhatsAppNumber`, `Commercial__Email` | Contatos mostrados nas telas |
| `Stripe__*` | Só para a oferta em nuvem; numa instalação self-hosted ficam vazias e a cobrança fica desligada |

## Logs

O app escreve no stdout do container (`docker compose logs web`). O nível padrão em produção é
`Warning`; para investigar algo, suba temporariamente `Logging__LogLevel__Default=Information`.
