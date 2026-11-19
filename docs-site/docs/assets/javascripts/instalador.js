/* Montador de instalação do Nexus (Self-hosted → Instalação), issue #90.
 *
 * A página pergunta o essencial (endereço, proxy, banco, administrador, licença, e-mail, Redis,
 * CA interna) e este arquivo gera o docker-compose.yml, os comandos de instalação e o bloco do
 * proxy a cada mudança. Tudo acontece no navegador: nada é enviado para fora da página, e senha
 * nenhuma é digitada aqui (as geradas saem como $(openssl rand ...); as do cliente, como "...").
 *
 * O mesmo arquivo roda em Node (docs-site/tests/instalador.test.mjs), que confere que cada
 * combinação gera um compose válido e que o exemplo estático da página é o que o montador gera
 * com as respostas padrão.
 */
(function (root) {
  'use strict';

  var IMAGEM = 'ghcr.io/aamora-tec/nexus-web';

  var PADRAO = {
    versao: '1.24.0',
    endereco: 'nexus.suaempresa.com.br',
    proxy: 'nginx',          // nginx | caddy | outro | nenhum
    banco: 'container',      // container | externo
    dbHost: 'postgres.suaempresa.local',
    dbPorta: '5432',
    dbNome: 'Nexus',
    dbUsuario: 'nexus',
    admin: 'ti@suaempresa.com.br',
    licenca: 'chave',        // chave | avaliacao
    registro: 'pull',        // pull | arquivo
    smtp: 'm365',            // m365 | google | brevo | ses | resend | relay | nenhum
    smtpUsuario: 'nexus@suaempresa.com.br',
    smtpRemetente: 'nexus@suaempresa.com.br',
    smtpNome: 'Nexus',
    smtpHost: 'smtp.suaempresa.local',
    smtpPorta: '25',
    sesRegiao: 'sa-east-1',
    redis: false,
    ca: false
  };

  var PROVEDORES = {
    m365:   { nome: 'Microsoft 365 / Exchange Online', host: 'smtp.office365.com', porta: '587', auth: true,
              dica: 'a caixa remetente e a senha dela; SMTP AUTH precisa estar habilitado na caixa' },
    google: { nome: 'Google Workspace / Gmail', host: 'smtp.gmail.com', porta: '587', auth: true,
              dica: 'o e-mail e uma senha de app (exige verificação em duas etapas)' },
    brevo:  { nome: 'Brevo', host: 'smtp-relay.brevo.com', porta: '587', auth: true,
              dica: 'o login da conta Brevo e a chave SMTP (não a senha do site)' },
    ses:    { nome: 'Amazon SES', host: null, porta: '587', auth: true,
              dica: 'as credenciais SMTP geradas no SES' },
    resend: { nome: 'Resend', host: 'smtp.resend.com', porta: '587', auth: true, usuarioFixo: 'resend',
              dica: 'usuário "resend" e a chave de API como senha' },
    relay:  { nome: 'Relay interno / Exchange', host: null, porta: null, auth: false,
              dica: 'sem credenciais: libere o IP do servidor do Nexus no relay' },
    nenhum: { nome: 'Nenhum por enquanto', host: null, porta: null, auth: false,
              dica: 'nada é enviado; confirme a primeira conta no banco (página E-mail)' }
  };

  function limparEndereco(valor) {
    var v = String(valor || '').trim().replace(/^https?:\/\//i, '').replace(/\/.*$/, '');
    return v || PADRAO.endereco;
  }

  function normalizar(entrada) {
    var o = {};
    for (var k in PADRAO) { o[k] = PADRAO[k]; }
    for (var j in (entrada || {})) { if (j in PADRAO && entrada[j] !== undefined && entrada[j] !== '') { o[j] = entrada[j]; } }
    o.endereco = limparEndereco(o.endereco);
    o.versao = String(o.versao).trim().replace(/^v/, '') || PADRAO.versao;
    o.redis = o.redis === true || o.redis === 'true';
    o.ca = o.ca === true || o.ca === 'true';
    if (!PROVEDORES[o.smtp]) { o.smtp = PADRAO.smtp; }
    return o;
  }

  function smtpResolvido(o) {
    var p = PROVEDORES[o.smtp];
    if (o.smtp === 'nenhum') { return null; }
    var host = p.host;
    if (o.smtp === 'ses') { host = 'email-smtp.' + (o.sesRegiao || PADRAO.sesRegiao).trim() + '.amazonaws.com'; }
    if (o.smtp === 'relay') { host = (o.smtpHost || PADRAO.smtpHost).trim(); }
    return {
      host: host,
      porta: o.smtp === 'relay' ? String(o.smtpPorta || PADRAO.smtpPorta).trim() : p.porta,
      auth: p.auth,
      usuario: p.usuarioFixo || (o.smtpUsuario || '').trim(),
      remetente: (o.smtpRemetente || PADRAO.smtpRemetente).trim(),
      nome: (o.smtpNome || PADRAO.smtpNome).trim(),
      dica: p.dica
    };
  }

  function gerarCompose(o) {
    var comProxy = o.proxy !== 'nenhum';
    var smtp = smtpResolvido(o);
    var l = [];
    l.push('name: nexus');
    l.push('services:');
    if (o.banco === 'container') {
      l.push('  db:');
      l.push('    image: postgres:18-alpine');
      l.push('    restart: unless-stopped');
      l.push('    env_file: .env');
      l.push('    volumes:');
      l.push('      - pgdata:/var/lib/postgresql');
      l.push('    networks: [nexus]');
      l.push('    healthcheck:');
      l.push('      test: ["CMD-SHELL", "pg_isready -U $${POSTGRES_USER} -d $${POSTGRES_DB}"]');
      l.push('      interval: 10s');
      l.push('      retries: 5');
      l.push('');
    }
    if (o.redis) {
      l.push('  redis:');
      l.push('    image: redis:7-alpine');
      l.push('    restart: unless-stopped');
      l.push('    networks: [nexus]');
      l.push('');
    }
    l.push('  web:');
    l.push('    image: ' + IMAGEM + ':${NEXUS_VERSION:?defina NEXUS_VERSION}');
    l.push('    restart: unless-stopped');
    if (comProxy) {
      l.push('    # Só o proxy reverso do host fala com o app.');
      l.push('    ports: ["127.0.0.1:5100:8080"]');
    } else {
      l.push('    # Sem proxy: o app responde direto em http://' + o.endereco + ':5100 (só para teste).');
      l.push('    ports: ["5100:8080"]');
    }
    l.push('    env_file: .env');
    l.push('    environment:');
    l.push('      ASPNETCORE_ENVIRONMENT: Production');
    if (o.banco === 'container') {
      l.push('      ConnectionStrings__DefaultConnection: "Host=db;Port=5432;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"');
    } else {
      l.push('      ConnectionStrings__DefaultConnection: "Host=' + o.dbHost.trim() + ';Port=' + String(o.dbPorta).trim() + ';Database=' + o.dbNome.trim() + ';Username=' + o.dbUsuario.trim() + ';Password=${DB_PASSWORD}"');
    }
    l.push('      APPLY_MIGRATIONS_ON_STARTUP: "true"');
    l.push('      # Endereço público: links de e-mail, campo url da API, MCP e iCal.');
    l.push('      PUBLIC_BASE_URL: ' + (comProxy ? 'https://' + o.endereco : 'http://' + o.endereco + ':5100'));
    l.push('      AllowedHosts: "' + o.endereco + ';localhost"');
    if (comProxy) {
      l.push('      # IP do proxy de quem o app aceita X-Forwarded-For: o gateway da rede abaixo.');
      l.push('      KNOWN_PROXIES: 172.30.0.1');
    }
    l.push('      DataProtection__CertificatePath: /app/dp/dataprotection.pfx');
    l.push('      DataProtection__CertificatePassword: ${DP_CERT_PASSWORD}');
    if (smtp) {
      l.push('      # E-mail pelo seu SMTP (' + PROVEDORES[o.smtp].nome + '); valores no .env.');
      l.push('      Smtp__Host: ${SMTP_HOST}');
      l.push('      Smtp__Port: ${SMTP_PORT}');
      if (smtp.auth) {
        l.push('      Smtp__UserName: ${SMTP_USER}');
        l.push('      Smtp__Password: ${SMTP_PASSWORD}');
      }
      l.push('      Smtp__SenderEmail: ${SMTP_SENDER_EMAIL}');
      l.push('      Smtp__SenderName: ${SMTP_SENDER_NAME}');
    } else {
      l.push('      # E-mail desligado por enquanto: nada é enviado. Ver a página E-mail (SMTP).');
    }
    if (o.licenca === 'chave') {
      l.push('      License__Key: ${NEXUS_LICENSE_KEY}');
    } else {
      l.push('      # Sem chave: 30 dias de avaliação. Depois, cole a chave em /admin/licenca ou em License__Key.');
    }
    l.push('      # Quem administra a instalação (licença e Active Directory): crie a primeira conta com este e-mail.');
    l.push('      License__Administrators__0: ' + o.admin.trim());
    l.push('      # Business: nada é limitado por plano.');
    l.push('      Plans__EnforceLimits: "false"');
    if (o.redis) {
      l.push('      # Mais de uma instância do app: chat, docs e limites passam pelo Redis.');
      l.push('      Redis__ConnectionString: "redis:6379"');
    }
    if (o.ca) {
      l.push('      # CA interna que assinou o certificado do controlador de domínio (LDAPS).');
      l.push('      LDAPTLS_CACERT: /app/ca/empresa-ca.pem');
    }
    l.push('    volumes:');
    l.push('      - uploads:/app/App_Data/uploads');
    l.push('      - ./dataprotection.pfx:/app/dp/dataprotection.pfx:ro');
    if (o.ca) { l.push('      - ./empresa-ca.pem:/app/ca/empresa-ca.pem:ro'); }
    if (comProxy) {
      l.push('    networks:');
      l.push('      nexus: { gw_priority: 1 }');
    } else {
      l.push('    networks: [nexus]');
    }
    var deps = [];
    if (o.banco === 'container') { deps.push('      db: { condition: service_healthy }'); }
    if (o.redis) { deps.push('      redis: { condition: service_started }'); }
    if (deps.length) { l.push('    depends_on:'); l = l.concat(deps); }
    l.push('');
    l.push('volumes:');
    if (o.banco === 'container') { l.push('  pgdata:'); }
    l.push('  uploads:');
    l.push('');
    l.push('networks:');
    if (comProxy) {
      l.push('  nexus:');
      l.push('    ipam:');
      l.push('      config:');
      l.push('        - subnet: 172.30.0.0/24');
      l.push('          gateway: 172.30.0.1');
    } else {
      l.push('  nexus: {}');
    }
    return l.join('\n') + '\n';
  }

  function gerarEnv(o) {
    var smtp = smtpResolvido(o);
    var l = [];
    if (o.banco === 'container') {
      l.push('POSTGRES_DB=Nexus');
      l.push('POSTGRES_USER=postgres');
      l.push('POSTGRES_PASSWORD=$(openssl rand -base64 32)');
    } else {
      l.push('DB_PASSWORD=...                 # senha do usuário ' + o.dbUsuario.trim() + ' no seu PostgreSQL');
    }
    l.push('DP_CERT_PASSWORD=$(openssl rand -base64 32)');
    if (o.licenca === 'chave') { l.push('NEXUS_LICENSE_KEY=NXL-...         # a chave recebida da Aamora'); }
    if (o.registro === 'pull') { l.push('NEXUS_REGISTRY_TOKEN=ghp_...      # o token de leitura do registro de imagens, recebido com a chave'); }
    if (smtp) {
      l.push('SMTP_HOST=' + smtp.host);
      l.push('SMTP_PORT=' + smtp.porta);
      if (smtp.auth) {
        l.push('SMTP_USER=' + smtp.usuario);
        l.push('SMTP_PASSWORD=...                # ' + smtp.dica);
      }
      l.push('SMTP_SENDER_EMAIL=' + smtp.remetente);
      l.push('SMTP_SENDER_NAME=' + smtp.nome);
    }
    return l.join('\n') + '\n';
  }

  function gerarComandos(o) {
    var l = [];
    l.push('sudo mkdir -p /opt/nexus && cd /opt/nexus');
    l.push('# 1. Salve o docker-compose.yml (aba ao lado) nesta pasta.');
    l.push('');
    l.push('# 2. Segredos: as senhas geradas aqui ficam só no .env. Guarde-o.');
    l.push('cat > .env <<EOF');
    l.push(gerarEnv(o).replace(/\n$/, ''));
    l.push('EOF');
    l.push('chmod 600 .env');
    l.push('');
    l.push('# 3. Certificado que cifra as chaves de sessão (sem ele o app não sobe).');
    l.push('source .env');
    l.push('openssl req -x509 -newkey rsa:2048 -days 3650 -subj "/CN=nexus-dataprotection" \\');
    l.push('  -keyout dp.key -out dp.crt -nodes');
    l.push('openssl pkcs12 -export -out dataprotection.pfx -inkey dp.key -in dp.crt -passout "pass:$DP_CERT_PASSWORD"');
    l.push('rm dp.key dp.crt');
    l.push('chmod 600 dataprotection.pfx');
    if (o.ca) {
      l.push('');
      l.push('# 4. CA interna do Active Directory (PEM), para o LDAPS.');
      l.push('cp /caminho/da/sua/ca.pem empresa-ca.pem');
    }
    l.push('');
    l.push('# ' + (o.ca ? '5' : '4') + '. A imagem.');
    l.push('export NEXUS_VERSION=' + o.versao);
    if (o.registro === 'pull') {
      l.push('echo "$NEXUS_REGISTRY_TOKEN" | docker login ghcr.io -u nexus --password-stdin');
      l.push('docker pull ' + IMAGEM + ':$NEXUS_VERSION');
    } else {
      l.push('docker load < nexus-web-' + o.versao + '.tar.gz    # o arquivo recebido da Aamora');
    }
    l.push('');
    l.push('# ' + (o.ca ? '6' : '5') + '. Subir. As migrations do banco rodam sozinhas.');
    l.push('docker compose up -d --wait');
    l.push('curl -s http://127.0.0.1:5100/health/ready    # Healthy');
    return l.join('\n') + '\n';
  }

  function gerarProxy(o) {
    var e = o.endereco;
    if (o.proxy === 'nginx') {
      return {
        titulo: 'Nginx no host',
        linguagem: 'nginx',
        texto: [
          "map $http_upgrade $connection_upgrade { default upgrade; '' close; }",
          '',
          'server {',
          '    listen 443 ssl;',
          '    http2 on;',
          '    server_name ' + e + ';',
          '    ssl_certificate     /etc/ssl/nexus/fullchain.pem;',
          '    ssl_certificate_key /etc/ssl/nexus/privkey.pem;',
          '    client_max_body_size 12m;',
          '',
          '    location / {',
          '        proxy_pass http://127.0.0.1:5100;',
          '        proxy_http_version 1.1;',
          '        proxy_set_header Upgrade $http_upgrade;',
          '        proxy_set_header Connection $connection_upgrade;',
          '        proxy_set_header Host $host;',
          '        proxy_set_header X-Forwarded-For $remote_addr;',
          '        proxy_set_header X-Forwarded-Proto https;',
          '        proxy_read_timeout 1h;',
          '    }',
          '}',
          ''
        ].join('\n')
      };
    }
    if (o.proxy === 'caddy') {
      return {
        titulo: 'Caddy no host (Caddyfile)',
        linguagem: 'text',
        texto: [
          '# O Caddy emite o certificado TLS sozinho, repassa o WebSocket e envia',
          '# X-Forwarded-For e X-Forwarded-Proto sem configuração extra.',
          e + ' {',
          '    reverse_proxy 127.0.0.1:5100',
          '    request_body {',
          '        max_size 12MB',
          '    }',
          '}',
          ''
        ].join('\n')
      };
    }
    if (o.proxy === 'outro') {
      return {
        titulo: 'Outro proxy no host',
        linguagem: 'text',
        texto: [
          'Qualquer proxy serve, desde que:',
          '',
          '- termine o TLS em https://' + e + ' e repasse para http://127.0.0.1:5100;',
          '- repasse o WebSocket (cabeçalhos Upgrade e Connection; o Nexus mantém uma conexão por usuário);',
          '- envie Host, X-Forwarded-For (o IP de quem chamou) e X-Forwarded-Proto: https;',
          '- aceite corpo de até 12 MB (anexos) e não corte conexões longas (1 h).',
          ''
        ].join('\n')
      };
    }
    return {
      titulo: 'Sem proxy (só para teste)',
      linguagem: 'text',
      texto: [
        'O app responde direto em http://' + e + ':5100, sem TLS. Serve para conhecer o Nexus',
        'numa rede fechada; para a equipe usar de verdade, coloque um proxy com HTTPS na frente',
        'e gere o compose de novo com ele.',
        ''
      ].join('\n')
    };
  }

  function gerar(entrada) {
    var o = normalizar(entrada);
    return {
      opcoes: o,
      compose: gerarCompose(o),
      env: gerarEnv(o),
      comandos: gerarComandos(o),
      proxy: gerarProxy(o)
    };
  }

  var api = { PADRAO: PADRAO, PROVEDORES: PROVEDORES, gerar: gerar, normalizar: normalizar };
  root.NexusInstalador = api;
  if (typeof module !== 'undefined' && module.exports) { module.exports = api; }

  /* ---------- Página ---------- */

  if (typeof document === 'undefined') { return; }

  var CHAVE = 'nexus.instalador';

  function ler(form) {
    var o = {};
    Array.prototype.forEach.call(form.querySelectorAll('[name]'), function (el) {
      if (el.type === 'checkbox') { o[el.name] = el.checked; }
      else if (el.type === 'radio') { if (el.checked) { o[el.name] = el.value; } }
      else { o[el.name] = el.value; }
    });
    return o;
  }

  function escrever(form, o) {
    Array.prototype.forEach.call(form.querySelectorAll('[name]'), function (el) {
      if (!(el.name in o)) { return; }
      if (el.type === 'checkbox') { el.checked = !!o[el.name]; }
      else if (el.type === 'radio') { el.checked = el.value === String(o[el.name]); }
      else { el.value = o[el.name]; }
    });
  }

  function mostrarCondicionais(form, o) {
    Array.prototype.forEach.call(form.querySelectorAll('[data-quando]'), function (el) {
      // data-quando="smtp=relay|ses" mostra o bloco quando o campo tem um dos valores.
      var partes = el.getAttribute('data-quando').split('=');
      var valores = partes[1].split('|');
      el.hidden = valores.indexOf(String(o[partes[0]])) < 0;
    });
  }

  function iniciar() {
    var raiz = document.querySelector('.nx-instalador');
    if (!raiz || raiz.dataset.pronto) { return; }
    raiz.dataset.pronto = '1';
    var form = raiz.querySelector('form');
    var saidas = {
      compose: raiz.querySelector('[data-saida="compose"]'),
      comandos: raiz.querySelector('[data-saida="comandos"]'),
      proxy: raiz.querySelector('[data-saida="proxy"]'),
      proxyTitulo: raiz.querySelector('[data-saida="proxy-titulo"]'),
      dicaSmtp: raiz.querySelector('[data-saida="dica-smtp"]')
    };
    var estatico = document.querySelector('.nx-instalador__estatico');
    if (estatico) { estatico.hidden = true; }
    raiz.hidden = false;

    try {
      var salvo = localStorage.getItem(CHAVE);
      if (salvo) { escrever(form, JSON.parse(salvo)); }
    } catch (e) { /* sem localStorage: segue com os padrões */ }

    function atualizar() {
      var entrada = ler(form);
      var r = gerar(entrada);
      mostrarCondicionais(form, r.opcoes);
      saidas.compose.textContent = r.compose;
      saidas.comandos.textContent = r.comandos;
      saidas.proxy.textContent = r.proxy.texto;
      saidas.proxyTitulo.textContent = r.proxy.titulo;
      if (saidas.dicaSmtp) {
        var p = PROVEDORES[r.opcoes.smtp];
        saidas.dicaSmtp.textContent = p ? p.dica : '';
      }
      try { localStorage.setItem(CHAVE, JSON.stringify(entrada)); } catch (e) { /* ignora */ }
    }

    form.addEventListener('input', atualizar);
    form.addEventListener('change', atualizar);
    form.addEventListener('submit', function (ev) { ev.preventDefault(); });

    var limpar = raiz.querySelector('[data-acao="limpar"]');
    if (limpar) {
      limpar.addEventListener('click', function () {
        try { localStorage.removeItem(CHAVE); } catch (e) { /* ignora */ }
        form.reset();
        escrever(form, PADRAO);
        atualizar();
      });
    }

    // Abas da saída.
    var abas = raiz.querySelectorAll('[data-aba]');
    var paineis = raiz.querySelectorAll('[data-painel]');
    Array.prototype.forEach.call(abas, function (aba) {
      aba.addEventListener('click', function () {
        Array.prototype.forEach.call(abas, function (a) { a.setAttribute('aria-selected', a === aba ? 'true' : 'false'); });
        Array.prototype.forEach.call(paineis, function (p) { p.hidden = p.getAttribute('data-painel') !== aba.getAttribute('data-aba'); });
      });
    });

    // Copiar.
    Array.prototype.forEach.call(raiz.querySelectorAll('[data-copiar]'), function (botao) {
      botao.addEventListener('click', function () {
        var alvo = raiz.querySelector('[data-saida="' + botao.getAttribute('data-copiar') + '"]');
        if (!alvo || !navigator.clipboard) { return; }
        var rotulo = botao.textContent;
        navigator.clipboard.writeText(alvo.textContent).then(function () {
          botao.textContent = 'Copiado';
          setTimeout(function () { botao.textContent = rotulo; }, 1500);
        });
      });
    });

    atualizar();
  }

  if (document.readyState === 'loading') { document.addEventListener('DOMContentLoaded', iniciar); }
  else { iniciar(); }
  // Navegação instantânea do Material, se um dia for ligada: a página troca sem recarregar.
  if (root.document$ && typeof root.document$.subscribe === 'function') { root.document$.subscribe(iniciar); }
})(typeof window !== 'undefined' ? window : globalThis);
