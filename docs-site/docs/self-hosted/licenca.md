# Licença

A instalação self-hosted é licenciada por assinatura anual, como o GitLab self-managed. A licença
é uma **chave de texto assinada** (começa com `NXL-`) que a própria imagem verifica: a instância
não liga para a Aamora, não precisa de internet e funciona em rede fechada.

## Como funciona

| Situação | O que acontece |
|---|---|
| Sem chave | A instância roda em **avaliação por 30 dias**, contados da primeira subida. |
| Chave válida | Tudo liberado até a data de validade. |
| 30 dias antes de vencer | Faixa no topo para os administradores: "A licença vence em N dias". |
| Vencida (ou avaliação encerrada) | **A instalação continua funcionando para todo mundo.** O que trava é subir uma imagem construída depois da data de atualizações da licença (validade + 7 dias de carência). |
| Faixa de usuários cheia | Contas novas não são criadas; quem já existe continua entrando. |

Uma licença vencida nunca derruba a instalação. Se você subir uma versão nova que a licença não
cobre, o container sobe só com uma página explicando o bloqueio, **sem tocar no banco**, e o
caminho é voltar à versão anterior ou instalar a chave renovada.

## O que a chave contém

Cliente, identificador da licença, plano, **faixa de usuários ativos** (ou ilimitada), data de
emissão, validade e data limite de atualizações. "Usuário ativo" é uma conta que entrou no Nexus
nos últimos 30 dias; contas criadas e nunca usadas, ou de quem saiu da empresa, não contam.

## Instalar a chave

Três formas, da mais simples para a mais controlada. A chave colada na tela tem precedência sobre
as outras duas.

1. **Pela tela**: entre com uma conta administradora, abra o menu da conta → **Licença da
   instalação** (`/admin/licenca`), cole a chave e confirme. Ela é verificada antes de ser guardada.
2. **Variável de ambiente** `License__Key` no compose.
3. **Arquivo** montado no container e apontado por `License__KeyFile` (para quem usa secrets).

Quem administra a instância é quem está em `License__Administrators` (lista de e-mails) ou, se
vazio, em `Plans__UnlimitedAccounts`.

## Renovar

Peça a renovação para comercialnexus@aamora.com.br informando o identificador da licença (está na
tela). A chave nova substitui a antiga pela tela ou pela variável; não precisa reiniciar quando a
troca é pela tela.

## Conferir

- `/admin/licenca` mostra cliente, validade, usuários ativos e a versão da imagem em uso.
- `/version` devolve o estado (`Valid`, `ExpiringSoon`, `Expired`, `Trial`, `Invalid`), sem dados
  do cliente: serve para o seu monitoramento.

## Variáveis

| Variável | Padrão | O quê |
|---|---|---|
| `License__Enforce` | `true` | Exige licença. Só a oferta em nuvem da Aamora desliga. |
| `License__Key` | vazio | A chave `NXL-...` |
| `License__KeyFile` | vazio | Caminho de um arquivo com a chave |
| `License__Administrators` | vazio | E-mails que veem a tela e as faixas; vazio = `Plans__UnlimitedAccounts` |
| `License__TrialDays` | 30 | Dias de avaliação sem chave |
| `License__WarningDays` | 30 | Antecedência do aviso de vencimento |
