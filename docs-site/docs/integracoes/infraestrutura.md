# E-mail, Redis e Monitoramento

!!! abstract ""
    O que o Nexus usa por fora para funcionar em produção, e o que a infraestrutura usa para saber se
    ele está de pé. Tudo configurado por variável de ambiente do container; nada disso fica no
    repositório.

## E-mail transacional (SMTP)

Um único componente, `Mailer`,
manda todos os e-mails: confirmação de conta e redefinição de senha (Identity), convites para
workspace, atribuição de tarefa (pela tela e pela [API](api.md)), prazo se aproximando e
o formulário comercial.

| Chave | Produção | Local |
|---|---|---|
| `Smtp__Host` | `smtp` (relay na rede Docker `mail`, que encaminha pelo Resend) | vazio = **não manda nada**, sem erro |
| `Smtp__Port` | 25 | |
| `Smtp__UserName` / `Smtp__Password` | vazios (o relay confia na rede) | |
| `Smtp__SenderEmail` / `Smtp__SenderName` | `nao-responda@aamora.com.br` / `Nexus` | |

Regras que o `Mailer` garante:

- **Um e-mail que falha nunca derruba a tela que o disparou.** O envio devolve `false`, a ação segue
  (o convite fica pendente, a tarefa é atribuída) e o log registra o motivo.
- **Conteúdo de usuário é codificado** antes de entrar no HTML (nome de quem atribuiu, título da
  tarefa). Um título como `<a href=evil>` chega como texto, não como link.
- Por que um relay: a VPS bloqueia a porta 25 de saída. O relay ao lado do app alcança o Resend na
  587. Histórico completo no cabeçalho do próprio `Mailer.cs`.

## Redis (tempo real com mais de uma instância)

O Nexus roda hoje em **uma** instância. Para rodar várias atrás de um balanceador, o chat, a
presença nos Docs e o rate limit do circuito Blazor precisam conversar entre processos. Isso é o
backplane em `src/Nexus.Web/Services/Backplane/`.

| Chave | Efeito |
|---|---|
| `Redis__ConnectionString` | Configurada: pub/sub do chat e dos docs e rate limit passam pelo Redis. **Vazia: implementações locais de instância única**, o comportamento de sempre |

Só identificadores cruzam a rede (`{instanceId, workspaceId, messageId}`): a instância que recebe
recarrega do banco, com os mesmos `Include` que a tela usa.

## Health checks e versão

| Rota | Autenticação | O que responde | Para quê |
|---|---|---|---|
| `GET /health/live` | nenhuma | `Healthy` se o processo responde. Não toca em dependência nenhuma | Liveness do container e do balanceador |
| `GET /health/ready` | nenhuma | `Healthy` só se o banco responde. Limitada a 12 chamadas por minuto por IP | Readiness: só receber tráfego quando o banco está acessível |
| `GET /version` | nenhuma | `{ "version": "1.4.0", "commit": "abc1234", "builtAt": "..." }` | Saber o que está no ar; a mesma informação aparece no rodapé |

As duas rotas de health devolvem **só uma palavra**: nunca o nome da dependência, a string de
conexão ou a exceção. Uma build local se identifica como `0.0.0-local`.

O `HEALTHCHECK` do Docker chama o próprio binário com `--healthcheck`, que faz um `GET` em
`/health/live` e sai com 0 ou 1. A imagem base não tem `curl` nem `wget`, e instalar um deles só
para isso não valia a pena.

## Variáveis de ambiente do app

| Variável | O quê |
|---|---|
| `ConnectionStrings__DefaultConnection` | PostgreSQL (`Host=...;Database=Nexus;Username=...;Password=...`) |
| `APPLY_MIGRATIONS_ON_STARTUP` | `true` em produção: o app aplica as migrations ao subir. Não há rollback automático, então faça [backup](../self-hosted/backup.md) antes de uma migration destrutiva |
| `PUBLIC_BASE_URL` | Endereço público (`https://nexus.aamora.com.br`). Usado nos links de e-mail e no campo `url` da API |
| `UPLOADS_PATH` | Pasta dos anexos (volume persistente) |
| `AUDIT_LOG_RETENTION_DAYS`, `NOTIFICATION_RETENTION_DAYS`, `COMMERCIAL_CONTACT_RETENTION_DAYS` | Retenção aplicada pelo job diário (LGPD) |
| `DataProtection__CertificatePath` / `__CertificatePassword` | Certificado que cifra o anel de chaves (cookies, antiforgery). Sem ele, Windows usa DPAPI; em Linux sem certificado o app **se recusa a subir**, a não ser com `DataProtection__AllowUnprotectedKeys=true` (só local) |
| `Stripe__*` | Ver [Stripe](stripe.md) |
| `Smtp__*`, `Redis__ConnectionString` | Acima |
| `Support__Email`, `Support__WhatsAppNumber`, `Commercial__Email` | Contatos mostrados nas telas e destino do formulário comercial |
| `Plans__EnforceLimits`, `Plans__UnlimitedAccounts` | Limites de plano |
