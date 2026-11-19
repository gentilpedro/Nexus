# Instalação

A instalação de referência é a mesma que a Aamora usa em produção: `docker compose` com o app e o
banco, um proxy reverso no host com HTTPS e um certificado que cifra as chaves de sessão. Em vez
de copiar um exemplo e editar no meio do texto, responda às perguntas abaixo: o
`docker-compose.yml`, os comandos e o bloco do proxy são gerados na hora, já com os seus valores.
Nada sai do seu navegador, e nenhuma senha é digitada aqui.

## 1. Monte a sua instalação

<div class="nx-instalador" hidden>
<div class="nx-instalador__grid">
<form autocomplete="off">
<fieldset>
<legend>Endereço</legend>
<label>Versão do Nexus<input type="text" name="versao" value="1.24.0"><small>A que a Aamora indicou com a chave.</small></label>
<label>Endereço público<input type="text" name="endereco" value="nexus.suaempresa.com.br" placeholder="nexus.suaempresa.com.br"><small>O nome DNS que a equipe vai usar.</small></label>
<label>Proxy reverso<select name="proxy">
<option value="nginx">Nginx no host</option>
<option value="caddy">Caddy no host</option>
<option value="outro">Outro proxy no host</option>
<option value="nenhum">Nenhum (só para teste, sem HTTPS)</option>
</select></label>
</fieldset>
<fieldset>
<legend>Banco de dados</legend>
<label>PostgreSQL<select name="banco">
<option value="container">Container da instalação (recomendado)</option>
<option value="externo">Servidor PostgreSQL meu</option>
</select></label>
<div data-quando="banco=externo" hidden>
<label>Host<input type="text" name="dbHost" value="postgres.suaempresa.local"></label>
<label>Porta<input type="text" name="dbPorta" value="5432"></label>
<label>Banco<input type="text" name="dbNome" value="Nexus"></label>
<label>Usuário<input type="text" name="dbUsuario" value="nexus"><small>A senha vai no <code>.env</code>, não aqui.</small></label>
</div>
</fieldset>
<fieldset>
<legend>Administração e licença</legend>
<label>E-mail de quem administra a instalação<input type="email" name="admin" value="ti@suaempresa.com.br"><small>A primeira conta é criada com este e-mail; é ela que vê Licença e Active Directory.</small></label>
<label class="nx-instalador__linha"><input type="radio" name="licenca" value="chave" checked>Tenho a chave de licença (NXL-...)</label>
<label class="nx-instalador__linha"><input type="radio" name="licenca" value="avaliacao">Avaliação de 30 dias, sem chave</label>
<label class="nx-instalador__linha"><input type="radio" name="registro" value="pull" checked>Baixar a imagem com o token do registro</label>
<label class="nx-instalador__linha"><input type="radio" name="registro" value="arquivo">Rede fechada: recebi o arquivo .tar.gz</label>
</fieldset>
<fieldset>
<legend>E-mail</legend>
<label>Servidor SMTP<select name="smtp">
<option value="m365">Microsoft 365 / Exchange Online</option>
<option value="google">Google Workspace / Gmail</option>
<option value="brevo">Brevo</option>
<option value="ses">Amazon SES</option>
<option value="resend">Resend</option>
<option value="relay">Relay interno / Exchange</option>
<option value="nenhum">Nenhum por enquanto</option>
</select><small data-saida="dica-smtp"></small></label>
<div data-quando="smtp=relay" hidden>
<label>Host do relay<input type="text" name="smtpHost" value="smtp.suaempresa.local"></label>
<label>Porta<input type="text" name="smtpPorta" value="25"></label>
</div>
<div data-quando="smtp=ses" hidden>
<label>Região do SES<input type="text" name="sesRegiao" value="sa-east-1"></label>
</div>
<div data-quando="smtp=m365|google|brevo|ses" hidden>
<label>Usuário do SMTP<input type="text" name="smtpUsuario" value="nexus@suaempresa.com.br"></label>
</div>
<div data-quando="smtp=m365|google|brevo|ses|resend|relay" hidden>
<label>Remetente<input type="email" name="smtpRemetente" value="nexus@suaempresa.com.br"></label>
<label>Nome do remetente<input type="text" name="smtpNome" value="Nexus"></label>
</div>
</fieldset>
<fieldset>
<legend>Opcionais</legend>
<label class="nx-instalador__linha"><input type="checkbox" name="redis">Redis (vou rodar mais de uma instância do app)</label>
<label class="nx-instalador__linha"><input type="checkbox" name="ca">CA interna para o LDAPS do Active Directory</label>
</fieldset>
<div class="nx-instalador__acoes"><button type="button" data-acao="limpar">Voltar aos padrões</button></div>
</form>
<div class="nx-instalador__saida">
<div class="nx-instalador__abas" role="tablist">
<button type="button" role="tab" data-aba="compose" aria-selected="true">docker-compose.yml</button>
<button type="button" role="tab" data-aba="comandos" aria-selected="false">Comandos</button>
<button type="button" role="tab" data-aba="proxy" aria-selected="false">Proxy</button>
</div>
<div class="nx-instalador__painel" data-painel="compose">
<button type="button" class="nx-instalador__copiar" data-copiar="compose">Copiar</button>
<pre><code data-saida="compose"></code></pre>
</div>
<div class="nx-instalador__painel" data-painel="comandos" hidden>
<button type="button" class="nx-instalador__copiar" data-copiar="comandos">Copiar</button>
<pre><code data-saida="comandos"></code></pre>
</div>
<div class="nx-instalador__painel" data-painel="proxy" hidden>
<p class="nx-instalador__painel-titulo" data-saida="proxy-titulo"></p>
<button type="button" class="nx-instalador__copiar" data-copiar="proxy">Copiar</button>
<pre><code data-saida="proxy"></code></pre>
</div>
</div>
</div>
</div>

<div class="nx-instalador__estatico" markdown>

Exemplo com as respostas padrão (Nginx no host, PostgreSQL no container, Microsoft 365). Com
JavaScript ligado, o montador acima gera estes três blocos com os seus valores.

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
      # E-mail pelo seu SMTP (Microsoft 365 / Exchange Online); valores no .env.
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
SMTP_HOST=smtp.office365.com
SMTP_PORT=587
SMTP_USER=nexus@suaempresa.com.br
SMTP_PASSWORD=...                # a caixa remetente e a senha dela; SMTP AUTH precisa estar habilitado na caixa
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

**`docker-compose.yml`**: o app e, se você escolheu, o banco. Vai em `/opt/nexus`. O compose com
**todas** as variáveis, comentado, está em [Configuração](configuracao.md#compose-completo); o
que o montador gera é o necessário para a sua escolha.

**`.env`**: os segredos. As senhas do banco e do certificado são geradas no servidor pelo
`openssl rand`; a chave de licença, o token do registro e a senha do SMTP você cola no lugar dos
`...`. `chmod 600` deixa o arquivo legível só pelo seu usuário.

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

Para mais de um administrador, acrescente `License__Administrators__1`, `__2`... e recrie o
container (`docker compose up -d --wait web`). Se a primeira conta foi criada com outro e-mail,
faça o mesmo com o e-mail certo.

## 5. Conferir

- `https://nexus.suaempresa.com.br/version` mostra a versão e o estado da licença.
- `/health/live` e `/health/ready` servem para o seu monitoramento, sem autenticação.
- Mande um convite para si mesmo: se o e-mail chegar, o SMTP está certo ([E-mail](email.md)).
- `/admin/licenca` mostra cliente, validade e usuários ativos ([Licença](licenca.md)).
