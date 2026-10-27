# Cobrança do plano Pro (Stripe)

A cobrança (#50) está pronta no código e **desligada até as três chaves `Stripe:*` existirem**.
Sem elas, o botão "Assinar o Pro" não aparece e o webhook responde 404 — o produto se comporta
como antes.

## Preço

**R$ 149/mês com até 10 pessoas + R$ 14,90 por pessoa extra.** O Nexus só informa ao Stripe
quantas pessoas o workspace tem; quem faz a conta é o preço em faixas do Stripe. Os textos
mostrados nas telas ficam em `BillingService` (`ProBasePrice`, `ProIncludedSeats`,
`ProExtraSeatPrice`) e na landing — mantenha os dois iguais ao preço do Stripe.

## Como ativar

1. **Criar a conta** em <https://dashboard.stripe.com/register> e completar os dados da empresa
   para receber em reais. Comece em **modo de teste** (chave de teste, cartão `4242 4242 4242 4242`).
2. **Criar o produto e o preço** — *Catálogo de produtos → Adicionar produto*:
   - Nome: `Nexus Pro`.
   - Preço: **recorrente, mensal, BRL**, modelo de cobrança **"Por níveis" (graduated)**:
     - Nível 1: de 1 a 10 unidades — **valor fixo R$ 149,00**, valor por unidade R$ 0,00.
     - Nível 2: de 11 em diante — **R$ 14,90 por unidade**.
   - Copie o id do preço (`price_…`).
3. **Criar o webhook** — *Desenvolvedores → Webhooks → Adicionar endpoint*:
   - URL: `https://nexus.aamora.com.br/stripe/webhook`.
   - Eventos: `checkout.session.completed`, `customer.subscription.created`,
     `customer.subscription.updated`, `customer.subscription.deleted`.
   - Copie o segredo de assinatura (`whsec_…`).
4. **Configurar no servidor** (variáveis de ambiente do container; nunca no repositório):

   ```
   Stripe__SecretKey=sk_test_…        (ou sk_live_… em produção)
   Stripe__WebhookSecret=whsec_…
   Stripe__ProPriceId=price_…
   ```

5. **Atualizar a landing**: tirar o selo "Em breve" do Pro e a nota "A cobrança ainda não está
   ativa" (`Landing.razor`), e ligar `Plans:EnforceLimits` se for a hora de aplicar os limites do
   Starter (#52/#53).

## Como funciona

| Momento | O que acontece |
|---|---|
| Dono clica "Assinar o Pro" (configurações do workspace) | `BillingService.StartCheckoutAsync` cria uma sessão do Stripe Checkout com a quantidade de membros como assentos e manda a pessoa para lá. O Nexus nunca vê o cartão. |
| Pagamento aprovado | Stripe chama o webhook (`checkout.session.completed`, `customer.subscription.*`). `StripeWebhookHandler` confere a assinatura, aplica uma vez só (id do evento em `StripeWebhookEvents`) e ignora evento mais velho que o último aplicado. O workspace vira Pro. |
| Alguém entra ou sai do workspace | `BillingService.SyncSeatsAsync` atualiza a quantidade no Stripe, com proporcional do mês. |
| Pagamento falha | Stripe manda `past_due`: o workspace fica "em atraso" e **continua Pro** enquanto o Stripe tenta de novo. |
| Dono cancela | Fica Pro até o fim do período pago e depois volta ao Starter (`WorkspaceSubscription.EffectivePlan`). Gerenciar e cancelar pela própria plataforma é a #54. |
