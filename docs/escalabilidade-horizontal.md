# Escalabilidade horizontal do tempo real

O Nexus roda hoje em **uma instância**. Esta página descreve o que foi preciso mudar para que
rodar **várias** seja possível, e o que ainda precisa ser providenciado do lado da infraestrutura.

## O problema

Blazor Server mantém um circuito SignalR por usuário, e o estado do tempo real vivia na memória
desse processo. Com duas instâncias atrás de um balanceador, dois usuários do mesmo workspace
podem cair em servidores diferentes — e aí:

- **Chat:** `WorkspaceChatBroadcaster` era um `event Action<ChatMessage>` do C#. Um evento só
  alcança os assinantes do próprio processo, então a mensagem postada no servidor A nunca chegava
  a quem estava no B. O usuário só a via ao recarregar a página.
- **Rate limiting:** `CircuitActionRateLimiter` contava numa `ConcurrentDictionary` local. Com N
  instâncias, o limite efetivo vira N × 20 mensagens por minuto — e basta uma reconexão cair em
  outro servidor para o usuário ganhar uma cota nova. O limite que existia para conter abuso
  passava a não conter nada.

## A solução

Um backplane em Redis, em `src/Nexus.Web/Services/Backplane/`.

### Chat: pub/sub com recarga no destino

```
usuário → instância A ──► evento local (quem está em A vê na hora)
                      └─► Redis: publica {instanceId, workspaceId, messageId}
                                   │
                        instância B ◄┘ descarta se instanceId for o próprio,
                                      senão recarrega do banco e dispara o evento local
```

Três decisões que valem explicação:

**Só identificadores cruzam a rede.** `ChatMessage` é entidade do EF com autor, anexos, menções e
página de Docs referenciada — tudo que a tela renderiza. Serializar esse grafo significaria ou
mandar um objeto pela metade (a mensagem apareceria sem autor), ou manter um DTO em sincronia com
cada mudança futura da entidade. A instância que recebe recarrega do banco, com exatamente os
`Include` que a página usa. O banco é compartilhado de qualquer forma.

**O evento local é disparado primeiro e sempre.** A entrega entre instâncias é best-effort em
cima dele. Redis fora do ar degrada o app para o comportamento de instância única — não quebra o
chat de todo mundo.

**O `instanceId` mata o eco.** Redis entrega a publicação a todos os assinantes do canal,
inclusive quem publicou. Sem descartar a própria, a mensagem apareceria duas vezes na tela de
quem enviou. E `RaiseFromRemote` existe separado de `BroadcastAsync` justamente para que uma
mensagem vinda do backplane não seja republicada nele — senão A ouviria o eco de B, republicaria,
B ouviria o de A, e as duas ficariam passando a mesma mensagem de volta para sempre.

### Rate limiting: contador em Redis

`INCR` + `PEXPIRE` dentro de um script Lua, para as duas operações serem indivisíveis. Separadas,
um processo que morre entre elas deixa um contador **sem expiração** — e o usuário dono daquela
chave fica limitado para sempre, sem se recuperar sozinho.

A expiração só é definida quando o `INCR` retorna 1, isto é, na primeira requisição da janela.
Renovar a cada acesso transformaria a janela fixa numa janela deslizante que nunca expira
enquanto o usuário continuar tentando — o oposto da intenção.

Se o Redis estiver indisponível, cai para o limitador em memória. **Não** libera a ação: limitar
por instância é mais fraco que limitar globalmente, mas é a mesma garantia que o app tinha antes,
e uma queda do Redis é um momento plausível para o tráfego estar anormal.

## Como ligar

Configuração única, `Redis:ConnectionString` (ou `Redis__ConnectionString` como variável de
ambiente). **Ausente significa instância única** — as implementações em memória, exatamente o
comportamento anterior. Multi-instância é opt-in: quem não provisionou Redis não recebe uma falha
de inicialização falando de infraestrutura que não tem.

A conexão usa `AbortOnConnectFail = false`. Sem isso, o app inteiro se recusa a subir quando o
Redis não responde no boot — transformando uma queda de cache em queda total.

## Como ver funcionando

```bash
# 1. descomente REDIS_CONNECTION no .env
docker compose --profile escala up
```

Sobe Redis, a instância em `:8080` e uma segunda em `:8081`. Abra as duas em navegadores
diferentes, entre no mesmo workspace e poste no chat: a mensagem aparece do outro lado sem
recarregar. Suba sem o profile e as duas portas deixam de se enxergar — que é a limitação que
isto resolve.

## O que ainda falta, do lado da infraestrutura

O código está pronto para várias instâncias. Colocar em produção exige três coisas que **não** são
código:

1. **Sticky sessions no balanceador.** O circuito Blazor é uma conexão WebSocket de longa duração
   entre o navegador e um servidor específico. Sem afinidade de sessão, a reconexão cai em outro
   servidor, que não conhece aquele circuito, e o usuário leva um "Attempting to reconnect"
   seguido de recarga de página.

2. **Chaves de Data Protection compartilhadas — e cifradas com certificado.** O chaveiro já é
   persistido no banco (`PersistKeysToDbContext`), que é compartilhado, então essa metade está
   resolvida. Mas o **encriptador** precisa ser o mesmo nas duas instâncias: com
   `ProtectKeysWithDpapi()` a chave é amarrada à máquina Windows, e a instância B não consegue
   decifrar o que a A escreveu — cookie de autenticação emitido num servidor é rejeitado no
   outro. Multi-instância exige `DataProtection:CertificatePath`, não DPAPI.

3. **Hospedagem que permita mais de uma instância.** O deploy atual é FTPS → IIS em hospedagem
   compartilhada, com uma instância e sem Redis. Rodar de fato em várias significa trocar esse
   modelo (containers com balanceador, ou App Service com scale-out) e provisionar um Redis.

## O que este backplane não resolve

Edição colaborativa de Docs continua last-write-wins: `DocPage` não tem token de concorrência —
ao contrário de `WorkItem`, que tem `RowVersion`. Dois usuários editando o mesmo documento, o
último salva por cima do outro em silêncio. Resolver isso exige reconciliação distribuída
(CRDT ou OT) e é assunto de outra frente de trabalho, que se apoia neste transporte.
