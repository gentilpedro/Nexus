# E-mail (SMTP)

O Nexus envia e-mail pelo **seu** servidor SMTP: o da empresa (Microsoft 365, Google Workspace,
Exchange), um serviço transacional (Brevo, Amazon SES, Resend, SendGrid) ou um relay interno.
Nada passa pela Aamora.

## O que o Nexus envia

| E-mail | Quando |
|---|---|
| Confirmação de conta | Cadastro pela tela de registro (contas criadas pelo [Active Directory](active-directory.md) já nascem confirmadas) |
| Redefinição de senha | "Esqueci a senha" |
| Convite para o workspace | Owner ou Admin convida alguém |
| Atribuição de tarefa | Pela tela, pela [API](../integracoes/api.md) ou por um assistente de IA |
| Prazo se aproximando | Lembrete diário das tarefas que vencem |
| Contato comercial | Formulário da página pública, se ela estiver no ar |

Um e-mail que falha **nunca derruba a ação** que o disparou: o convite fica pendente, a tarefa é
atribuída, a conta é criada; o log registra o motivo.

## Variáveis

| Variável | O quê |
|---|---|
| `Smtp__Host` | Servidor SMTP. **Vazio desliga o envio** (nada falha, só não manda) |
| `Smtp__Port` | `587` com STARTTLS é o caso comum; `25` para relay interno sem TLS. A porta `465` (TLS implícito) não é suportada: use a 587 |
| `Smtp__UserName`, `Smtp__Password` | Credenciais, se o servidor exigir. Vazios = sem autenticação (relay que confia no IP) |
| `Smtp__SenderEmail` | Remetente. Precisa ser um endereço que o servidor aceite enviar (domínio com SPF/DKIM no provedor). Vazio também desliga o envio |
| `Smtp__SenderName` | Nome exibido; padrão `Nexus` |

O Nexus usa STARTTLS quando o servidor oferece e fala texto puro quando não oferece (relay na
rede interna). O envio tem 15 segundos para acontecer.

Na instalação de referência os valores ficam no `.env` (`SMTP_HOST`, `SMTP_PORT`, `SMTP_USER`,
`SMTP_PASSWORD`, `SMTP_SENDER_EMAIL`, `SMTP_SENDER_NAME`) e o compose os repassa; ver
[Configuração](configuracao.md#compose-completo).

## Exemplos por provedor

| Provedor | `Smtp__Host` | Porta | Usuário e senha | Observação |
|---|---|---|---|---|
| Microsoft 365 / Exchange Online | `smtp.office365.com` | 587 | a caixa remetente e a senha dela | A caixa precisa de **SMTP AUTH** habilitado no admin do Microsoft 365; o remetente é a própria caixa |
| Google Workspace / Gmail | `smtp.gmail.com` | 587 | o e-mail e uma **senha de app** (exige verificação em duas etapas) | Limite diário do Google por conta |
| Brevo | `smtp-relay.brevo.com` | 587 | o login da conta e a **chave SMTP** (não a senha do site) | Remetente precisa estar verificado no Brevo |
| Amazon SES | `email-smtp.<região>.amazonaws.com` | 587 | as **credenciais SMTP** geradas no SES | Fora do sandbox do SES para enviar a qualquer endereço |
| Resend | `smtp.resend.com` | 587 | `resend` e a chave de API | Domínio verificado no Resend |
| Exchange ou relay interno | `smtp.acme.local` | 25 | vazios | Libere o IP do servidor do Nexus no relay |

!!! tip "Porta 25 bloqueada"
    Provedores de nuvem costumam bloquear a porta 25 de saída. Use um dos serviços acima na 587, ou
    um relay dentro da sua rede.

## Testar

1. Suba o app e mande um convite para o seu próprio e-mail (Configurações do workspace → Membros),
   ou use "Esqueci a senha" na tela de login.
2. Se não chegou, veja o log:

```bash
docker compose logs web | grep -i -E "smtp|email"
```

| Mensagem no log | Significa |
|---|---|
| `Skipped sending email ... SMTP is not configured` | `Smtp__Host` ou `Smtp__SenderEmail` vazio |
| `Authentication failed` | usuário ou senha errados, SMTP AUTH desligado no provedor, ou senha comum onde o provedor exige senha de app ou chave SMTP |
| `timed out` | o container não alcança o servidor: firewall, porta bloqueada ou nome errado |
| `Sender address rejected` / `not verified` | o provedor não aceita o remetente: verifique o domínio ou use a própria caixa |

## Sem SMTP

Dá para subir sem e-mail (por exemplo, numa instalação de homologação). Nada falha, mas:

- a conta criada pela tela de registro não recebe o link de confirmação e não consegue entrar.
  Confirme-a direto no banco e entre em seguida:

```bash
docker compose exec -T db psql -U postgres -d Nexus \
  -c "UPDATE \"AspNetUsers\" SET \"EmailConfirmed\" = true WHERE \"NormalizedEmail\" = upper('ti@suaempresa.com.br');"
```

- convites ficam pendentes sem a pessoa saber: avise por outro canal, ela se cadastra com o mesmo
  e-mail e o convite é aplicado.
- contas vindas do [Active Directory](active-directory.md) não dependem de e-mail.
