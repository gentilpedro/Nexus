// Testes do montador de instalação (docs/assets/javascripts/instalador.js), issues #90 e #92.
//
//   node --test "docs-site/tests/*.test.mjs"
//
// 1. Cada combinação de respostas gera um docker-compose.yml que o `docker compose config` aceita.
// 2. O exemplo estático da página Instalação (o que aparece sem JavaScript) é exatamente o que o
//    montador gera com o EXEMPLO, então os dois nunca divergem.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, writeFileSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const aqui = dirname(fileURLToPath(import.meta.url));
const require = createRequire(import.meta.url);
const { gerar, EXEMPLO, VARIAVEIS } = require('../docs/assets/javascripts/instalador.js');

// O .env que os comandos criam, com os $(...) trocados por valores fixos: o compose config só
// precisa que as variáveis existam.
function envDeTeste(resultado) {
  return resultado.env
    .replace(/\$\(openssl rand -base64 32\)/g, 'segredo')
    .replace(/=\.\.\.\s*#.*$/gm, '=segredo')
    .replace(/=NXL-\.\.\.\s*#.*$/gm, '=NXL-chave')
    .replace(/=ghp_\.\.\.\s*#.*$/gm, '=ghp_token');
}

function composeConfig(resultado) {
  const pasta = mkdtempSync(join(tmpdir(), 'nexus-instalador-'));
  try {
    writeFileSync(join(pasta, 'docker-compose.yml'), resultado.compose);
    writeFileSync(join(pasta, '.env'), envDeTeste(resultado));
    writeFileSync(join(pasta, 'dataprotection.pfx'), '');
    writeFileSync(join(pasta, 'empresa-ca.pem'), '');
    return execFileSync('docker', ['compose', '-f', join(pasta, 'docker-compose.yml'), 'config'], {
      cwd: pasta,
      env: { ...process.env, NEXUS_VERSION: '1.24.0' },
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'pipe'],
    });
  } finally {
    rmSync(pasta, { recursive: true, force: true });
  }
}

const todasVars = {};
for (const g of VARIAVEIS) for (const v of g.itens) todasVars[v.nome] = '';

const combinacoes = {
  'vazio (tudo no exemplo)': {},
  exemplo: EXEMPLO,
  'sem proxy': { proxy: 'nenhum' },
  caddy: { proxy: 'caddy' },
  'outro proxy': { proxy: 'outro' },
  'porta diferente': { porta: '8090' },
  'banco externo': { dbContainer: false, dbHost: 'pg.acme.local', dbPorta: '5433', dbNome: 'nexus', dbUsuario: 'nexus' },
  avaliacao: { temChave: false },
  'rede fechada': { redeFechada: true },
  'smtp com usuário': { smtpHost: 'smtp.office365.com', smtpPorta: '587', smtpUsuario: 'nexus@acme.com' },
  'smtp relay sem usuário': { smtpHost: 'smtp.acme.local', smtpPorta: '25' },
  'redis no compose': { redisContainer: true },
  'redis externo': { redisEndereco: 'redis.acme.local:6379' },
  'ca interna': { ca: '/etc/ssl/certs/acme-ca.pem' },
  'todas as variáveis opcionais': { vars: todasVars },
  tudo: { proxy: 'caddy', dbContainer: false, temChave: false, redeFechada: true, smtpHost: 'smtp.acme.local', redisContainer: true, ca: '/tmp/ca.pem', vars: { Logging__LogLevel__Default: 'Information', Support__WhatsAppNumber: '+55 11 99999-0000' } },
};

for (const [nome, opcoes] of Object.entries(combinacoes)) {
  test(`compose válido: ${nome}`, () => {
    const r = gerar(opcoes);
    const saida = composeConfig(r);
    assert.match(saida, /ghcr\.io\/aamora-tec\/nexus-web:1\.24\.0/);
    if (r.opcoes.admin) assert.match(saida, /License__Administrators__0: ti@suaempresa\.com\.br/);
  });
}

test('vazio: obrigatórios caem no exemplo, opcionais ficam de fora', () => {
  const r = gerar({});
  assert.equal(r.opcoes.endereco, 'nexus.suaempresa.com.br');
  assert.equal(r.opcoes.admin, '', 'administrador é opcional: vazio, a primeira pessoa que entra administra');
  assert.doesNotMatch(r.compose, /License__Administrators__0/);
  assert.match(r.compose, /primeira pessoa que entrar/);
  assert.doesNotMatch(r.compose, /Smtp__Host/);
  assert.doesNotMatch(r.compose, /Redis__ConnectionString|LDAPTLS_CACERT|# Opcionais/);
  assert.doesNotMatch(r.env, /SMTP_/);
});

test('sem proxy: http direto, porta aberta, sem KNOWN_PROXIES', () => {
  const r = gerar({ proxy: 'nenhum', porta: '8090' });
  assert.match(r.compose, /PUBLIC_BASE_URL: http:\/\/nexus\.suaempresa\.com\.br:8090/);
  assert.match(r.compose, /ports: \["8090:8080"\]/);
  assert.doesNotMatch(r.compose, /KNOWN_PROXIES/);
  assert.match(r.proxy.texto, /:8090/);
});

test('banco externo: sem serviço db, senha em DB_PASSWORD', () => {
  const r = gerar({ dbContainer: false, dbHost: 'pg.acme.local' });
  assert.doesNotMatch(r.compose, /^\s+db:$/m);
  assert.match(r.compose, /Host=pg\.acme\.local;Port=5432;Database=Nexus;Username=nexus;Password=\$\{DB_PASSWORD\}/);
  assert.match(r.env, /^DB_PASSWORD=/m);
  assert.doesNotMatch(r.env, /POSTGRES_/);
});

test('SMTP: com usuário leva credenciais; sem usuário, não; remetente segue o endereço', () => {
  const com = gerar({ smtpHost: 'smtp.office365.com', smtpUsuario: 'nexus@acme.com', endereco: 'nexus.acme.com.br' });
  assert.match(com.compose, /Smtp__UserName/);
  assert.match(com.env, /SMTP_SENDER_EMAIL=nexus@acme\.com\.br/);
  const sem = gerar({ smtpHost: 'smtp.acme.local', smtpPorta: '25' });
  assert.doesNotMatch(sem.compose, /Smtp__UserName/);
  assert.match(sem.env, /SMTP_HOST=smtp\.acme\.local\nSMTP_PORT=25/);
});

test('Redis: no compose sobe o serviço; externo só a connection string', () => {
  const c = gerar({ redisContainer: true });
  assert.match(c.compose, /^\s+redis:$/m);
  assert.match(c.compose, /Redis__ConnectionString: "redis:6379"/);
  const e = gerar({ redisEndereco: 'redis.acme.local:6379' });
  assert.doesNotMatch(e.compose, /^\s+redis:$/m);
  assert.match(e.compose, /Redis__ConnectionString: "redis\.acme\.local:6379"/);
});

test('CA interna: variável, volume e cópia nos comandos', () => {
  const r = gerar({ ca: '/etc/ssl/certs/acme-ca.pem' });
  assert.match(r.compose, /LDAPTLS_CACERT: \/app\/ca\/empresa-ca\.pem/);
  assert.match(r.compose, /\.\/empresa-ca\.pem:\/app\/ca\/empresa-ca\.pem:ro/);
  assert.match(r.comandos, /cp \/etc\/ssl\/certs\/acme-ca\.pem empresa-ca\.pem/);
});

test('variáveis opcionais: marcada entra com o valor ou com o padrão; desconhecida é ignorada', () => {
  const r = gerar({ vars: { Logging__LogLevel__Default: 'Information', AUDIT_LOG_RETENTION_DAYS: '', INVENTADA: 'x' } });
  assert.match(r.compose, /Logging__LogLevel__Default: "Information"/);
  assert.match(r.compose, /AUDIT_LOG_RETENTION_DAYS: "730"/);
  assert.doesNotMatch(r.compose, /INVENTADA/);
});

test('endereço aceita URL colada e limpa', () => {
  const r = gerar({ endereco: 'https://nexus.acme.com.br/' });
  assert.equal(r.opcoes.endereco, 'nexus.acme.com.br');
  assert.match(r.compose, /AllowedHosts: "nexus\.acme\.com\.br;localhost"/);
});

test('rede fechada: docker load no lugar do pull, sem token no .env', () => {
  const r = gerar({ redeFechada: true });
  assert.match(r.comandos, /docker load < nexus-web-1\.24\.0\.tar\.gz/);
  assert.doesNotMatch(r.comandos, /docker login/);
  assert.doesNotMatch(r.env, /NEXUS_REGISTRY_TOKEN/);
});

// Blocos de código dentro do <div class="nx-instalador__estatico" markdown> da página, na ordem.
function blocosEstaticos() {
  const md = readFileSync(join(aqui, '..', 'docs', 'self-hosted', 'instalacao.md'), 'utf8');
  const inicio = md.indexOf('nx-instalador__estatico');
  assert.ok(inicio > 0, 'a página precisa ter o bloco estático');
  const fim = md.indexOf('</div>', inicio);
  const trecho = md.slice(inicio, fim);
  const blocos = {};
  for (const m of trecho.matchAll(/```(\w+)\n([\s\S]*?)```/g)) { blocos[m[1]] = m[2]; }
  return blocos;
}

test('o exemplo estático da página é o que o montador gera com o EXEMPLO', () => {
  const r = gerar(EXEMPLO);
  const blocos = blocosEstaticos();
  assert.equal(blocos.yaml, r.compose, 'docker-compose.yml estático difere do gerado');
  assert.equal(blocos.bash, r.comandos, 'comandos estáticos diferem dos gerados');
  assert.equal(blocos.nginx, r.proxy.texto, 'bloco do Nginx estático difere do gerado');
});

test('todo campo do formulário da página existe no montador', () => {
  const md = readFileSync(join(aqui, '..', 'docs', 'self-hosted', 'instalacao.md'), 'utf8');
  const nomes = new Set([...md.matchAll(/name="([a-zA-Z]+)"/g)].map(m => m[1]));
  const conhecidos = new Set(Object.keys(EXEMPLO));
  for (const n of nomes) assert.ok(conhecidos.has(n), `campo ${n} do formulário não existe no montador`);
});
