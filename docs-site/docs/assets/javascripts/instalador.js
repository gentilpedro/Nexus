/* Montador de instalação do Nexus (Self-hosted → Instalação), issues #90 e #92.
 *
 * A página mostra o docker-compose.yml, os comandos e o bloco do proxy no topo e, abaixo, os
 * cartões com os campos. Os campos começam vazios, com o exemplo como placeholder; o compose só
 * ganha as seções do que foi preenchido (e-mail, banco próprio, Redis, CA interna, variáveis
 * opcionais). Tudo acontece no navegador: nada sai da página, e senha nenhuma é digitada aqui
 * (as geradas saem como $(openssl rand ...); as do cliente, como "...").
 *
 * O mesmo arquivo roda em Node (docs-site/tests/instalador.test.mjs): cada combinação gera um
 * compose que o docker compose aceita, e o exemplo estático da página é o que o montador gera
 * com o EXEMPLO.
 */
(function (root) {
  'use strict';

  var IMAGEM = 'ghcr.io/aamora-tec/nexus-web';

  // Valores usados quando o campo fica vazio (os obrigatórios) e no exemplo estático da página.
  var EXEMPLO = {
    versao: '1.24.0',
    endereco: 'nexus.suaempresa.com.br',
    porta: '5100',
    proxy: 'nginx',            // nginx | caddy | outro | nenhum
    redeFechada: false,
    dbContainer: true,
    dbHost: 'postgres.suaempresa.local',
    dbPorta: '5432',
    dbNome: 'Nexus',
    dbUsuario: 'nexus',
    admin: 'ti@suaempresa.com.br',
    temChave: true,
    smtpHost: 'smtp.suaempresa.com.br',
    smtpPorta: '587',
    smtpUsuario: 'nexus@suaempresa.com.br',
    smtpRemetente: 'nexus@suaempresa.com.br',
    smtpNome: 'Nexus',
    redisContainer: false,
    redisEndereco: 'redis:6379',
    ca: '',
    vars: {}
  };

  // Variáveis opcionais, em grupos recolhíveis. Marcada = entra no compose com o valor digitado
  // ou, vazio, com o padrão. Os padrões são os da imagem; ver a página Configuração.
  var VARIAVEIS = [
    { grupo: 'Licença e planos', itens: [
      { nome: 'License__Enforce', padrao: 'true', descricao: 'Exige licença. Só a oferta em nuvem da Aamora desliga.' },
      { nome: 'License__Administrators__1', padrao: 'outra.pessoa@suaempresa.com.br', descricao: 'Segundo administrador da instalação (o primeiro é o do cartão acima).' },
      { nome: 'Plans__UnlimitedAccounts__0', padrao: 'ti@suaempresa.com.br', descricao: 'Conta nunca limitada por plano, mesmo com Plans__EnforceLimits ligado.' }
    ] },
    { grupo: 'Operação', itens: [
      { nome: 'UPLOADS_PATH', padrao: 'App_Data/uploads', descricao: 'Pasta dos anexos dentro do container (o volume uploads aponta para ela).' },
      { nome: 'AUDIT_LOG_RETENTION_DAYS', padrao: '730', descricao: 'Dias de retenção do log de auditoria.' },
      { nome: 'NOTIFICATION_RETENTION_DAYS', padrao: '180', descricao: 'Dias de retenção das notificações.' },
      { nome: 'WEBHOOK_DELIVERY_RETENTION_DAYS', padrao: '30', descricao: 'Dias de retenção das entregas de webhook.' },
      { nome: 'COMMERCIAL_CONTACT_RETENTION_DAYS', padrao: '365', descricao: 'Dias de retenção dos contatos do formulário comercial.' },
      { nome: 'Logging__LogLevel__Default', padrao: 'Warning', descricao: 'Nível de log no stdout; Information para investigar.' }
    ] },
    { grupo: 'Integrações e contatos', itens: [
      { nome: 'Webhooks__AllowInsecureUrls', padrao: 'false', descricao: 'Só em laboratório: aceita webhooks http e endereços internos.' },
      { nome: 'Docs__SiteUrl', padrao: 'https://docs-nexus.aamora.com.br', descricao: 'Origem liberada no CORS da especificação OpenAPI (só GET).' },
      { nome: 'Support__Email', padrao: 'suportenexus@aamora.com.br', descricao: 'E-mail de suporte mostrado nas telas.' },
      { nome: 'Support__WhatsAppNumber', padrao: '', descricao: 'WhatsApp de suporte mostrado nas telas (vazio = não mostra).' },
      { nome: 'Commercial__Email', padrao: 'comercialnexus@aamora.com.br', descricao: 'E-mail comercial mostrado nas telas.' }
    ] }
  ];

  function texto(v) { return String(v === undefined || v === null ? '' : v).trim(); }
  function bool(v) { return v === true || v === 'true' || v === 'on'; }
  function limparEndereco(valor) { return texto(valor).replace(/^https?:\/\//i, '').replace(/\/.*$/, ''); }

  function normalizar(entrada) {
    var e = entrada || {};
    var o = {};
    o.versao = texto(e.versao).replace(/^v/, '') || EXEMPLO.versao;
    o.endereco = limparEndereco(e.endereco) || EXEMPLO.endereco;
    o.porta = texto(e.porta).replace(/\D/g, '') || EXEMPLO.porta;
    o.proxy = ['nginx', 'caddy', 'outro', 'nenhum'].indexOf(e.proxy) >= 0 ? e.proxy : EXEMPLO.proxy;
    o.redeFechada = bool(e.redeFechada);
    o.dbContainer = e.dbContainer === undefined ? EXEMPLO.dbContainer : bool(e.dbContainer);
    o.dbHost = texto(e.dbHost) || EXEMPLO.dbHost;
    o.dbPorta = texto(e.dbPorta) || EXEMPLO.dbPorta;
    o.dbNome = texto(e.dbNome) || EXEMPLO.dbNome;
    o.dbUsuario = texto(e.dbUsuario) || EXEMPLO.dbUsuario;
    o.admin = texto(e.admin) || EXEMPLO.admin;
    o.temChave = e.temChave === undefined ? EXEMPLO.temChave : bool(e.temChave);
    o.smtpHost = texto(e.smtpHost);
    o.smtpPorta = texto(e.smtpPorta) || EXEMPLO.smtpPorta;
    o.smtpUsuario = texto(e.smtpUsuario);
    o.smtpRemetente = texto(e.smtpRemetente) || ('nexus@' + o.endereco.replace(/^[^.]+\./, ''));
    o.smtpNome = texto(e.smtpNome) || EXEMPLO.smtpNome;
    o.redisContainer = bool(e.redisContainer);
    o.redisEndereco = o.redisContainer ? EXEMPLO.redisEndereco : texto(e.redisEndereco);
    o.ca = texto(e.ca);
    o.vars = {};
    var vars = e.vars || {};
    VARIAVEIS.forEach(function (g) {
      g.itens.forEach(function (v) {
        if (v.nome in vars) { o.vars[v.nome] = texto(vars[v.nome]) || v.padrao; }
      });
    });
    return o;
  }

  function yamlString(v) { return '"' + String(v).replace(/\\/g, '\\\\').replace(/"/g, '\\"') + '"'; }

  function gerarCompose(o) {
    var comProxy = o.proxy !== 'nenhum';
    var comSmtp = !!o.smtpHost;
    var comRedis = !!o.redisEndereco;
    var l = [];
    l.push('name: nexus');
    l.push('services:');
    if (o.dbContainer) {
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
    if (o.redisContainer) {
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
      l.push('    ports: ["127.0.0.1:' + o.porta + ':8080"]');
    } else {
      l.push('    # Sem proxy: o app responde direto em http://' + o.endereco + ':' + o.porta + ' (só para teste).');
      l.push('    ports: ["' + o.porta + ':8080"]');
    }
    l.push('    env_file: .env');
    l.push('    environment:');
    l.push('      ASPNETCORE_ENVIRONMENT: Production');
    if (o.dbContainer) {
      l.push('      ConnectionStrings__DefaultConnection: "Host=db;Port=5432;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"');
    } else {
      l.push('      ConnectionStrings__DefaultConnection: "Host=' + o.dbHost + ';Port=' + o.dbPorta + ';Database=' + o.dbNome + ';Username=' + o.dbUsuario + ';Password=${DB_PASSWORD}"');
    }
    l.push('      APPLY_MIGRATIONS_ON_STARTUP: "true"');
    l.push('      # Endereço público: links de e-mail, campo url da API, MCP e iCal.');
    l.push('      PUBLIC_BASE_URL: ' + (comProxy ? 'https://' + o.endereco : 'http://' + o.endereco + ':' + o.porta));
    l.push('      AllowedHosts: "' + o.endereco + ';localhost"');
    if (comProxy) {
      l.push('      # IP do proxy de quem o app aceita X-Forwarded-For: o gateway da rede abaixo.');
      l.push('      KNOWN_PROXIES: 172.30.0.1');
    }
    l.push('      DataProtection__CertificatePath: /app/dp/dataprotection.pfx');
    l.push('      DataProtection__CertificatePassword: ${DP_CERT_PASSWORD}');
    if (comSmtp) {
      l.push('      # E-mail pelo seu SMTP; valores no .env.');
      l.push('      Smtp__Host: ${SMTP_HOST}');
      l.push('      Smtp__Port: ${SMTP_PORT}');
      if (o.smtpUsuario) {
        l.push('      Smtp__UserName: ${SMTP_USER}');
        l.push('      Smtp__Password: ${SMTP_PASSWORD}');
      }
      l.push('      Smtp__SenderEmail: ${SMTP_SENDER_EMAIL}');
      l.push('      Smtp__SenderName: ${SMTP_SENDER_NAME}');
    } else {
      l.push('      # E-mail desligado: nada é enviado. Preencha o SMTP na página para ligar.');
    }
    if (o.temChave) {
      l.push('      License__Key: ${NEXUS_LICENSE_KEY}');
    } else {
      l.push('      # Sem chave: 30 dias de avaliação. Depois, cole a chave em /admin/licenca ou em License__Key.');
    }
    l.push('      # Quem administra a instalação (licença e Active Directory): crie a primeira conta com este e-mail.');
    l.push('      License__Administrators__0: ' + o.admin);
    l.push('      # Business: nada é limitado por plano.');
    l.push('      Plans__EnforceLimits: "false"');
    if (comRedis) {
      l.push('      # Mais de uma instância do app: chat, docs e limites passam pelo Redis.');
      l.push('      Redis__ConnectionString: ' + yamlString(o.redisEndereco));
    }
    if (o.ca) {
      l.push('      # CA interna que assinou o certificado do controlador de domínio (LDAPS).');
      l.push('      LDAPTLS_CACERT: /app/ca/empresa-ca.pem');
    }
    var nomes = Object.keys(o.vars);
    if (nomes.length) {
      l.push('      # Opcionais (ver Configuração).');
      nomes.forEach(function (n) { l.push('      ' + n + ': ' + yamlString(o.vars[n])); });
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
    if (o.dbContainer) { deps.push('      db: { condition: service_healthy }'); }
    if (o.redisContainer) { deps.push('      redis: { condition: service_started }'); }
    if (deps.length) { l.push('    depends_on:'); l = l.concat(deps); }
    l.push('');
    l.push('volumes:');
    if (o.dbContainer) { l.push('  pgdata:'); }
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
    var l = [];
    if (o.dbContainer) {
      l.push('POSTGRES_DB=Nexus');
      l.push('POSTGRES_USER=postgres');
      l.push('POSTGRES_PASSWORD=$(openssl rand -base64 32)');
    } else {
      l.push('DB_PASSWORD=...                   # senha do usuário ' + o.dbUsuario + ' no seu PostgreSQL');
    }
    l.push('DP_CERT_PASSWORD=$(openssl rand -base64 32)');
    if (o.temChave) { l.push('NEXUS_LICENSE_KEY=NXL-...         # a chave recebida da Aamora'); }
    if (!o.redeFechada) { l.push('NEXUS_REGISTRY_TOKEN=ghp_...      # o token de leitura do registro de imagens, recebido com a chave'); }
    if (o.smtpHost) {
      l.push('SMTP_HOST=' + o.smtpHost);
      l.push('SMTP_PORT=' + o.smtpPorta);
      if (o.smtpUsuario) {
        l.push('SMTP_USER=' + o.smtpUsuario);
        l.push('SMTP_PASSWORD=...                 # senha ou chave SMTP desse usuário');
      }
      l.push('SMTP_SENDER_EMAIL=' + o.smtpRemetente);
      l.push('SMTP_SENDER_NAME=' + o.smtpNome);
    }
    return l.join('\n') + '\n';
  }

  function gerarComandos(o) {
    var n = 1;
    var l = [];
    l.push('sudo mkdir -p /opt/nexus && cd /opt/nexus');
    l.push('# ' + (n++) + '. Salve o docker-compose.yml (aba ao lado) nesta pasta.');
    l.push('');
    l.push('# ' + (n++) + '. Segredos: as senhas geradas aqui ficam só no .env. Guarde-o.');
    l.push('cat > .env <<EOF');
    l.push(gerarEnv(o).replace(/\n$/, ''));
    l.push('EOF');
    l.push('chmod 600 .env');
    l.push('');
    l.push('# ' + (n++) + '. Certificado que cifra as chaves de sessão (sem ele o app não sobe).');
    l.push('source .env');
    l.push('openssl req -x509 -newkey rsa:2048 -days 3650 -subj "/CN=nexus-dataprotection" \\');
    l.push('  -keyout dp.key -out dp.crt -nodes');
    l.push('openssl pkcs12 -export -out dataprotection.pfx -inkey dp.key -in dp.crt -passout "pass:$DP_CERT_PASSWORD"');
    l.push('rm dp.key dp.crt');
    l.push('chmod 600 dataprotection.pfx');
    if (o.ca) {
      l.push('');
      l.push('# ' + (n++) + '. CA interna do Active Directory (PEM), para o LDAPS.');
      l.push('cp ' + o.ca + ' empresa-ca.pem');
    }
    l.push('');
    l.push('# ' + (n++) + '. A imagem.');
    l.push('export NEXUS_VERSION=' + o.versao);
    if (o.redeFechada) {
      l.push('docker load < nexus-web-' + o.versao + '.tar.gz    # o arquivo recebido da Aamora');
    } else {
      l.push('echo "$NEXUS_REGISTRY_TOKEN" | docker login ghcr.io -u nexus --password-stdin');
      l.push('docker pull ' + IMAGEM + ':$NEXUS_VERSION');
    }
    l.push('');
    l.push('# ' + (n++) + '. Subir. As migrations do banco rodam sozinhas.');
    l.push('docker compose up -d --wait');
    l.push('curl -s http://127.0.0.1:' + o.porta + '/health/ready    # Healthy');
    return l.join('\n') + '\n';
  }

  function gerarProxy(o) {
    var e = o.endereco;
    var p = o.porta;
    if (o.proxy === 'nginx') {
      return { titulo: 'Nginx no host', texto: [
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
        '        proxy_pass http://127.0.0.1:' + p + ';',
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
      ].join('\n') };
    }
    if (o.proxy === 'caddy') {
      return { titulo: 'Caddy no host (Caddyfile)', texto: [
        '# O Caddy emite o certificado TLS sozinho, repassa o WebSocket e envia',
        '# X-Forwarded-For e X-Forwarded-Proto sem configuração extra.',
        e + ' {',
        '    reverse_proxy 127.0.0.1:' + p,
        '    request_body {',
        '        max_size 12MB',
        '    }',
        '}',
        ''
      ].join('\n') };
    }
    if (o.proxy === 'outro') {
      return { titulo: 'Outro proxy no host', texto: [
        'Qualquer proxy serve, desde que:',
        '',
        '- termine o TLS em https://' + e + ' e repasse para http://127.0.0.1:' + p + ';',
        '- repasse o WebSocket (cabeçalhos Upgrade e Connection; o Nexus mantém uma conexão por usuário);',
        '- envie Host, X-Forwarded-For (o IP de quem chamou) e X-Forwarded-Proto: https;',
        '- aceite corpo de até 12 MB (anexos) e não corte conexões longas (1 h).',
        ''
      ].join('\n') };
    }
    return { titulo: 'Sem proxy (só para teste)', texto: [
      'O app responde direto em http://' + e + ':' + p + ', sem TLS. Serve para conhecer o Nexus',
      'numa rede fechada; para a equipe usar de verdade, coloque um proxy com HTTPS na frente',
      'e gere o compose de novo com ele.',
      ''
    ].join('\n') };
  }

  function gerar(entrada) {
    var o = normalizar(entrada);
    return { opcoes: o, compose: gerarCompose(o), env: gerarEnv(o), comandos: gerarComandos(o), proxy: gerarProxy(o) };
  }

  var api = { EXEMPLO: EXEMPLO, VARIAVEIS: VARIAVEIS, gerar: gerar, normalizar: normalizar };
  root.NexusInstalador = api;
  if (typeof module !== 'undefined' && module.exports) { module.exports = api; }

  /* ---------- Página ---------- */

  if (typeof document === 'undefined') { return; }

  var CHAVE = 'nexus.instalador.v2';

  function el(tag, attrs, filhos) {
    var n = document.createElement(tag);
    Object.keys(attrs || {}).forEach(function (k) {
      if (k === 'text') { n.textContent = attrs[k]; } else { n.setAttribute(k, attrs[k]); }
    });
    (filhos || []).forEach(function (f) { n.appendChild(f); });
    return n;
  }

  // Monta os grupos de variáveis opcionais (caixa + nome + descrição + valor).
  function montarVariaveis(alvo) {
    VARIAVEIS.forEach(function (g) {
      var corpo = el('div', { 'class': 'nx-instalador__vars' });
      g.itens.forEach(function (v) {
        var caixa = el('input', { type: 'checkbox', name: 'var:' + v.nome, id: 'nx-var-' + v.nome });
        var valor = el('input', { type: 'text', name: 'val:' + v.nome, placeholder: v.padrao || '(vazio)', 'aria-label': 'Valor de ' + v.nome });
        corpo.appendChild(el('div', { 'class': 'nx-instalador__var' }, [
          el('label', { 'class': 'nx-instalador__var-nome', 'for': 'nx-var-' + v.nome }, [caixa, el('code', { text: v.nome })]),
          el('span', { 'class': 'nx-instalador__var-desc', text: v.descricao }),
          valor
        ]));
      });
      var det = el('details', { 'class': 'nx-instalador__grupo' }, [
        el('summary', {}, [el('span', { text: g.grupo }), el('span', { 'class': 'nx-instalador__grupo-n', text: g.itens.length + ' variáveis' })]),
        corpo
      ]);
      alvo.appendChild(det);
    });
  }

  function ler(form) {
    var o = { vars: {} };
    Array.prototype.forEach.call(form.querySelectorAll('[name]'), function (c) {
      var nome = c.name;
      if (nome.indexOf('var:') === 0) {
        if (c.checked) {
          var val = form.querySelector('[name="val:' + nome.slice(4) + '"]');
          o.vars[nome.slice(4)] = val ? val.value : '';
        }
        return;
      }
      if (nome.indexOf('val:') === 0) { return; }
      if (c.type === 'checkbox') { o[nome] = c.checked; }
      else if (c.type === 'radio') { if (c.checked) { o[nome] = c.value; } }
      else { o[nome] = c.value; }
    });
    return o;
  }

  function escrever(form, o) {
    Array.prototype.forEach.call(form.querySelectorAll('[name]'), function (c) {
      var nome = c.name;
      if (nome.indexOf('var:') === 0) { c.checked = !!(o.vars && nome.slice(4) in o.vars); return; }
      if (nome.indexOf('val:') === 0) { c.value = (o.vars && o.vars[nome.slice(4)]) || ''; return; }
      if (!(nome in o)) { return; }
      if (c.type === 'checkbox') { c.checked = !!o[nome]; }
      else if (c.type === 'radio') { c.checked = c.value === String(o[nome]); }
      else { c.value = o[nome]; }
    });
  }

  function aplicarEstado(form, o) {
    // Campos que dependem de uma caixa: data-quando="dbContainer=false".
    Array.prototype.forEach.call(form.querySelectorAll('[data-quando]'), function (c) {
      var partes = c.getAttribute('data-quando').split('=');
      var valores = partes[1].split('|');
      c.hidden = valores.indexOf(String(o[partes[0]])) < 0;
    });
    // Placeholders que seguem o endereço.
    var remetente = form.querySelector('[name="smtpRemetente"]');
    if (remetente) { remetente.placeholder = 'nexus@' + o.endereco.replace(/^[^.]+\./, ''); }
  }

  function baixar(nome, conteudo) {
    var blob = new Blob([conteudo], { type: 'text/plain;charset=utf-8' });
    var url = URL.createObjectURL(blob);
    var a = el('a', { href: url, download: nome });
    document.body.appendChild(a);
    a.click();
    setTimeout(function () { document.body.removeChild(a); URL.revokeObjectURL(url); }, 0);
  }

  function iniciar() {
    var raiz = document.querySelector('.nx-instalador');
    if (!raiz || raiz.dataset.pronto) { return; }
    raiz.dataset.pronto = '1';
    var form = raiz.querySelector('form');
    var varsAlvo = raiz.querySelector('[data-variaveis]');
    if (varsAlvo) { montarVariaveis(varsAlvo); }
    var saidas = {
      compose: raiz.querySelector('[data-saida="compose"]'),
      comandos: raiz.querySelector('[data-saida="comandos"]'),
      proxy: raiz.querySelector('[data-saida="proxy"]'),
      proxyTitulo: raiz.querySelector('[data-saida="proxy-titulo"]')
    };
    var estatico = document.querySelector('.nx-instalador__estatico');
    if (estatico) { estatico.hidden = true; }
    raiz.hidden = false;

    try {
      var salvo = localStorage.getItem(CHAVE);
      if (salvo) { escrever(form, JSON.parse(salvo)); }
    } catch (e) { /* sem localStorage: segue vazio */ }

    var ultimo = null;
    function atualizar() {
      var entrada = ler(form);
      ultimo = gerar(entrada);
      aplicarEstado(form, ultimo.opcoes);
      saidas.compose.textContent = ultimo.compose;
      saidas.comandos.textContent = ultimo.comandos;
      saidas.proxy.textContent = ultimo.proxy.texto;
      saidas.proxyTitulo.textContent = ultimo.proxy.titulo;
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
        atualizar();
      });
    }

    var abas = raiz.querySelectorAll('[data-aba]');
    var paineis = raiz.querySelectorAll('[data-painel]');
    Array.prototype.forEach.call(abas, function (aba) {
      aba.addEventListener('click', function () {
        Array.prototype.forEach.call(abas, function (a) { a.setAttribute('aria-selected', a === aba ? 'true' : 'false'); });
        Array.prototype.forEach.call(paineis, function (p) { p.hidden = p.getAttribute('data-painel') !== aba.getAttribute('data-aba'); });
      });
    });

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

    Array.prototype.forEach.call(raiz.querySelectorAll('[data-baixar]'), function (botao) {
      botao.addEventListener('click', function () {
        if (!ultimo) { return; }
        var qual = botao.getAttribute('data-baixar');
        if (qual === 'compose') { baixar('docker-compose.yml', ultimo.compose); }
        else if (qual === 'comandos') { baixar('instalar-nexus.sh', '#!/bin/bash\nset -euo pipefail\n' + ultimo.comandos); }
      });
    });

    atualizar();
  }

  if (document.readyState === 'loading') { document.addEventListener('DOMContentLoaded', iniciar); }
  else { iniciar(); }
  if (root.document$ && typeof root.document$.subscribe === 'function') { root.document$.subscribe(iniciar); }
})(typeof window !== 'undefined' ? window : globalThis);
