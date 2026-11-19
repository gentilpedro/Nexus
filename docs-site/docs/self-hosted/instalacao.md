---
hide:
  - toc
---

# Instalação

A instalação de referência é a mesma que a Aamora usa em produção: `docker compose` com o app e o
banco, um proxy reverso no host com HTTPS e um certificado que cifra as chaves de sessão.
Preencha o que for da sua instalação nos cartões abaixo e o `docker-compose.yml`, os comandos e o
bloco do proxy são montados na hora, no topo da página, só com o que você preencheu. Os campos
vazios ficam com o exemplo. Nada sai do seu navegador, e nenhuma senha é digitada aqui.

## 1. Monte a sua instalação

<div class="nx-instalador" hidden>
<div class="nx-instalador__saida">
<div class="nx-instalador__abas" role="tablist">
<button type="button" role="tab" data-aba="compose" aria-selected="true">docker-compose.yml</button>
<button type="button" role="tab" data-aba="comandos" aria-selected="false">Comandos</button>
<button type="button" role="tab" data-aba="proxy" aria-selected="false">Proxy</button>
<div class="nx-instalador__ferramentas">
<button type="button" data-copiar="compose">Copiar</button>
<button type="button" data-baixar="compose">Baixar</button>
</div>
</div>
<div class="nx-instalador__painel" data-painel="compose"><pre><code data-saida="compose"></code></pre></div>
<div class="nx-instalador__painel" data-painel="comandos" hidden><pre><code data-saida="comandos"></code></pre></div>
<div class="nx-instalador__painel" data-painel="proxy" hidden><p class="nx-instalador__painel-titulo" data-saida="proxy-titulo"></p><pre><code data-saida="proxy"></code></pre></div>
</div>
<form autocomplete="off">
<section class="nx-instalador__cartao">
<h3>Instalação<small>Versão, endereço e como a imagem chega ao servidor.</small></h3>
<div class="nx-instalador__campos nx-instalador__campos--3">
<label class="nx-instalador__campo"><span>Versão do Nexus</span><input type="text" name="versao" placeholder="1.24.0"><small>A que a Aamora indicou com a chave.</small></label>
<label class="nx-instalador__campo"><span>Endereço público</span><input type="text" name="endereco" placeholder="nexus.suaempresa.com.br"><small>O nome DNS que a equipe vai usar.</small></label>
<label class="nx-instalador__campo"><span>Porta local do app</span><input type="text" name="porta" placeholder="5100"><small>Onde o proxy encontra o app no host.</small></label>
</div>
<label class="nx-instalador__caixa"><input type="checkbox" name="redeFechada"><span>Rede fechada: recebi a imagem como arquivo <code>.tar.gz</code> <small>(sem token nem <code>docker pull</code>)</small></span></label>
</section>
<section class="nx-instalador__cartao">
<h3>Proxy reverso<small>Quem termina o HTTPS na frente do app. A aba Proxy traz o bloco pronto.</small></h3>
<div class="nx-instalador__opcoes">
<label><input type="radio" name="proxy" value="nginx" checked>Nginx no host</label>
<label><input type="radio" name="proxy" value="caddy">Caddy no host</label>
<label><input type="radio" name="proxy" value="outro">Outro proxy</label>
<label><input type="radio" name="proxy" value="nenhum">Nenhum (só teste)</label>
</div>
</section>
<section class="nx-instalador__cartao">
<h3>Banco de dados<small>PostgreSQL 16 ou mais novo. A senha vai no <code>.env</code>, não aqui.</small></h3>
<label class="nx-instalador__caixa"><input type="checkbox" name="dbContainer" checked><span>Subir o PostgreSQL no próprio compose <small>(desmarque para usar um servidor seu)</small></span></label>
<div class="nx-instalador__campos nx-instalador__campos--4" data-quando="dbContainer=false" hidden>
<label class="nx-instalador__campo"><span>Host</span><input type="text" name="dbHost" placeholder="postgres.suaempresa.local"></label>
<label class="nx-instalador__campo"><span>Porta</span><input type="text" name="dbPorta" placeholder="5432"></label>
<label class="nx-instalador__campo"><span>Banco</span><input type="text" name="dbNome" placeholder="Nexus"></label>
<label class="nx-instalador__campo"><span>Usuário</span><input type="text" name="dbUsuario" placeholder="nexus"></label>
</div>
</section>
<section class="nx-instalador__cartao">
<h3>Administração e licença<small>A instalação sobe vazia; a conta criada com este e-mail é a administradora.</small></h3>
<div class="nx-instalador__campos">
<label class="nx-instalador__campo"><span>E-mail de quem administra a instalação</span><input type="email" name="admin" placeholder="ti@suaempresa.com.br"><small>Vê Licença da instalação e Active Directory no menu da conta.</small></label>
<label class="nx-instalador__caixa"><input type="checkbox" name="temChave" checked><span>Tenho a chave de licença (<code>NXL-...</code>) <small>(desmarcada: 30 dias de avaliação)</small></span></label>
</div>
</section>
<section class="nx-instalador__cartao">
<h3>E-mail (SMTP)<small>O seu servidor de e-mail. Host vazio: o Nexus não envia nada por enquanto. Detalhes na página <a href="../email/">E-mail</a>.</small></h3>
<div class="nx-instalador__campos nx-instalador__campos--3">
<label class="nx-instalador__campo"><span>Host do SMTP</span><input type="text" name="smtpHost" placeholder="smtp.suaempresa.com.br"><small>Ex.: smtp.office365.com, smtp.gmail.com, smtp-relay.brevo.com, relay interno.</small></label>
<label class="nx-instalador__campo"><span>Porta</span><input type="text" name="smtpPorta" placeholder="587"><small>587 com STARTTLS; 25 em relay interno.</small></label>
<label class="nx-instalador__campo"><span>Usuário</span><input type="text" name="smtpUsuario" placeholder="nexus@suaempresa.com.br"><small>Vazio: sem autenticação (relay que confia no IP).</small></label>
<label class="nx-instalador__campo"><span>Remetente</span><input type="email" name="smtpRemetente" placeholder="nexus@suaempresa.com.br"></label>
<label class="nx-instalador__campo"><span>Nome do remetente</span><input type="text" name="smtpNome" placeholder="Nexus"></label>
</div>
</section>
<section class="nx-instalador__cartao">
<h3>Redis<small>Só para rodar mais de uma instância do app atrás de um balanceador.</small></h3>
<label class="nx-instalador__caixa"><input type="checkbox" name="redisContainer"><span>Subir um Redis no próprio compose</span></label>
<div class="nx-instalador__campos" data-quando="redisContainer=false">
<label class="nx-instalador__campo"><span>Endereço do seu Redis</span><input type="text" name="redisEndereco" placeholder="redis.suaempresa.local:6379"><small>Vazio: uma instância só, sem Redis.</small></label>
</div>
</section>
<section class="nx-instalador__cartao">
<h3>Active Directory<small>O AD é configurado pela tela depois de subir. Só a CA interna entra por aqui.</small></h3>
<div class="nx-instalador__campos">
<label class="nx-instalador__campo"><span>Arquivo PEM da CA interna</span><input type="text" name="ca" placeholder="/etc/ssl/certs/empresa-ca.pem"><small>Vazio: não monta. Use se o certificado do controlador de domínio é de uma CA da empresa e você não quer marcar "aceitar autoassinado".</small></label>
</div>
</section>
<section class="nx-instalador__cartao">
<h3>Variáveis opcionais<small>Marque o que quiser mudar; vazio usa o padrão da imagem. Todas estão descritas em <a href="../configuracao/">Configuração</a>.</small></h3>
<div data-variaveis></div>
</section>
<div class="nx-instalador__acoes"><button type="button" data-acao="limpar">Limpar tudo</button></div>
</form>
</div>

<div class="nx-instalador__estatico" markdown>

Exemplo com as respostas de referência (Nginx no host, PostgreSQL no compose, SMTP da empresa).
Com JavaScript ligado, o montador acima gera estes três blocos com os seus valores.

### docker-compose.yml

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
    # Só o proxy reverso do host fala com o app.
    ports: ["127.0.0.1:5100:8080"]
    env_file: .env
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ConnectionStrings__DefaultConnection: "Host=db;Port=5432;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"
      APPLY_MIGRATIONS_ON_STARTUP: "true"
      # Endereço público: links de e-mail, campo url da API, MCP e iCal.
      PUBLIC_BASE_URL: https://nexus.suaempresa.com.br
      AllowedHosts: "nexus.suaempresa.com.br;localhost"
      # IP do proxy de quem o app aceita X-Forwarded-For: o gateway da rede abaixo.
      KNOWN_PROXIES: 172.30.0.1
      DataProtection__CertificatePath: /app/dp/dataprotection.pfx
      DataProtection__CertificatePassword: ${DP_CERT_PASSWORD}
      # E-mail pelo seu SMTP; valores no .env.
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
      # Mais de uma instância do app: chat, docs e limites passam pelo Redis.
      Redis__ConnectionString: "redis:6379"
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

### Comandos

```bash
sudo mkdir -p /opt/nexus && cd /opt/nexus
# 1. Salve o docker-compose.yml (aba ao lado) nesta pasta.

# 2. Segredos: as senhas geradas aqui ficam só no .env. Guarde-o.
cat > .env <<EOF
POSTGRES_DB=Nexus
POSTGRES_USER=postgres
POSTGRES_PASSWORD=$(openssl rand -base64 32)
DP_CERT_PASSWORD=$(openssl rand -base64 32)
NEXUS_LICENSE_KEY=NXL-...         # a chave recebida da Aamora
NEXUS_REGISTRY_TOKEN=ghp_...      # o token de leitura do registro de imagens, recebido com a chave
SMTP_HOST=smtp.suaempresa.com.br
SMTP_PORT=587
SMTP_USER=nexus@suaempresa.com.br
SMTP_PASSWORD=...                 # senha ou chave SMTP desse usuário
SMTP_SENDER_EMAIL=nexus@suaempresa.com.br
SMTP_SENDER_NAME=Nexus
EOF
chmod 600 .env

# 3. Certificado que cifra as chaves de sessão (sem ele o app não sobe).
source .env
openssl req -x509 -newkey rsa:2048 -days 3650 -subj "/CN=nexus-dataprotection" \
  -keyout dp.key -out dp.crt -nodes
openssl pkcs12 -export -out dataprotection.pfx -inkey dp.key -in dp.crt -passout "pass:$DP_CERT_PASSWORD"
rm dp.key dp.crt
chmod 600 dataprotection.pfx

# 4. A imagem.
export NEXUS_VERSION=1.24.0
echo "$NEXUS_REGISTRY_TOKEN" | docker login ghcr.io -u nexus --password-stdin
docker pull ghcr.io/aamora-tec/nexus-web:$NEXUS_VERSION

# 5. Subir. As migrations do banco rodam sozinhas.
docker compose up -d --wait
curl -s http://127.0.0.1:5100/health/ready    # Healthy
```

### Nginx

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

</div>

## 2. O que você acabou de gerar

**`docker-compose.yml`**: o app e, se marcou, o banco e o Redis. Vai em `/opt/nexus`. O compose
com **todas** as variáveis, comentado, está em [Configuração](configuracao.md#compose-completo);
o que o montador gera é o necessário para o que você preencheu.

**`.env`**: os segredos, criados pelo bloco de comandos. As senhas do banco e do certificado são
geradas no servidor pelo `openssl rand`; a chave de licença, o token do registro e a senha do
SMTP você cola no lugar dos `...`. `chmod 600` deixa o arquivo legível só pelo seu usuário.

**`dataprotection.pfx`**: o certificado que cifra em repouso as chaves que assinam os cookies de
login e os tokens de formulário. Sem ele o app **se recusa a subir** em produção, de propósito.

!!! danger "Faça backup do `.env` e do `dataprotection.pfx`"
    Perder o certificado não perde dados, mas desloga todo mundo e invalida links de confirmação
    pendentes. Sem a senha do banco não há restauração. Guarde os dois fora do servidor
    ([Backup](backup.md)).

**A imagem**: `docker login` com o token de leitura recebido com a chave (o usuário pode ser
qualquer texto; o registro só olha o token) e `docker pull`. Em rede fechada, a Aamora manda o
`.tar.gz` da versão e o comando vira `docker load`.

**`docker compose up -d --wait`** sobe tudo e espera o app ficar saudável; as migrations do banco
rodam sozinhas (`APPLY_MIGRATIONS_ON_STARTUP`). `curl /health/ready` tem que responder `Healthy`.

## 3. Proxy reverso

O app expõe só `127.0.0.1:5100`; quem fala com a internet é o proxy, que termina o TLS. A aba
**Proxy** traz o bloco pronto para o que você escolheu. O que importa, em qualquer proxy:

- repassar o **WebSocket** (o Nexus mantém uma conexão persistente por usuário);
- enviar `Host`, `X-Forwarded-For` e `X-Forwarded-Proto: https`;
- aceitar corpo de até 12 MB (anexos) e não cortar conexões longas.

`KNOWN_PROXIES` no compose é o gateway da rede Docker (`172.30.0.1`): é dele que o app aceita o
cabeçalho `X-Forwarded-For`. Por isso a rede tem IP fixo.

O certificado TLS do domínio é o seu de sempre (Let's Encrypt, a CA da empresa). O Caddy emite
sozinho; no Nginx, aponte `ssl_certificate` para ele.

## 4. Primeira conta

A instalação sobe sem nenhuma conta. Quem administra a instalação (vê as telas **Licença da
instalação** e **Active Directory**) é a conta cujo e-mail você respondeu em "quem administra a
instalação", gravado em `License__Administrators__0` no compose. Então:

1. Abra o endereço do seu Nexus e clique em **Criar conta**, usando exatamente esse e-mail.
2. Confirme a conta pelo link que chega por e-mail. Sem SMTP configurado, confirme no banco
   ([E-mail → Sem SMTP](email.md#sem-smtp)).
3. Entre. No menu da conta, no canto superior direito, aparecem **Licença da instalação** e
   **Active Directory**. Contas criadas com outros e-mails não têm esses itens.
4. Crie o workspace e convide a equipe. Com o [Active Directory](active-directory.md)
   configurado, a equipe entra com a senha da rede e as contas nascem sozinhas no primeiro login.

Para mais de um administrador, marque `License__Administrators__1` nas variáveis opcionais (ou
acrescente no compose) e recrie o container (`docker compose up -d --wait web`). Se a primeira
conta foi criada com outro e-mail, faça o mesmo com o e-mail certo.

## 5. Conferir

- `https://nexus.suaempresa.com.br/version` mostra a versão e o estado da licença.
- `/health/live` e `/health/ready` servem para o seu monitoramento, sem autenticação.
- Mande um convite para si mesmo: se o e-mail chegar, o SMTP está certo ([E-mail](email.md)).
- `/admin/licenca` mostra cliente, validade e usuários ativos ([Licença](licenca.md)).
