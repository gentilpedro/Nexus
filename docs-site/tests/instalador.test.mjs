// Testes do montador de instalação (docs/assets/javascripts/instalador.js), issue #90.
//
//   node --test "docs-site/tests/*.test.mjs"
//
// 1. Cada combinação de respostas gera um docker-compose.yml que o `docker compose config` aceita.
// 2. O exemplo estático da página Instalação (o que aparece sem JavaScript) é exatamente o que o
//    montador gera com as respostas padrão, então os dois nunca divergem.
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
const { gerar, PADRAO } = require('../docs/assets/javascripts/instalador.js');

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

const combinacoes = {
  padrao: {},
  'sem proxy': { proxy: 'nenhum' },
  caddy: { proxy: 'caddy' },
  'outro proxy': { proxy: 'outro' },
  'banco externo': { banco: 'externo', dbHost: 'pg.acme.local', dbPorta: '5433', dbNome: 'nexus', dbUsuario: 'nexus' },
  avaliacao: { licenca: 'avaliacao' },
  'rede fechada': { registro: 'arquivo' },
  google: { smtp: 'google' },
  brevo: { smtp: 'brevo' },
  ses: { smtp: 'ses', sesRegiao: 'us-east-1' },
  resend: { smtp: 'resend' },
  relay: { smtp: 'relay', smtpHost: 'smtp.acme.local', smtpPorta: '25' },
  'sem e-mail': { smtp: 'nenhum' },
  redis: { redis: true },
  'ca interna': { ca: true },
  tudo: { proxy: 'caddy', banco: 'externo', licenca: 'avaliacao', registro: 'arquivo', smtp: 'relay', redis: true, ca: true },
};

for (const [nome, opcoes] of Object.entries(combinacoes)) {
  test(`compose válido: ${nome}`, () => {
    const r = gerar(opcoes);
    const saida = composeConfig(r);
    assert.match(saida, /ghcr\.io\/aamora-tec\/nexus-web:1\.24\.0/);
    assert.match(saida, /License__Administrators__0: ti@suaempresa\.com\.br/);
  });
}

test('sem proxy: http direto, porta aberta, sem KNOWN_PROXIES', () => {
  const r = gerar({ proxy: 'nenhum' });
  assert.match(r.compose, /PUBLIC_BASE_URL: http:\/\/nexus\.suaempresa\.com\.br:5100/);
  assert.match(r.compose, /ports: \["5100:8080"\]/);
  assert.doesNotMatch(r.compose, /KNOWN_PROXIES/);
});

test('banco externo: sem serviço db, senha em DB_PASSWORD', () => {
  const r = gerar({ banco: 'externo', dbHost: 'pg.acme.local' });
  assert.doesNotMatch(r.compose, /^\s+db:$/m);
  assert.match(r.compose, /Host=pg\.acme\.local;Port=5432;Database=Nexus;Username=nexus;Password=\$\{DB_PASSWORD\}/);
  assert.match(r.env, /^DB_PASSWORD=/m);
  assert.doesNotMatch(r.env, /POSTGRES_/);
});

test('SES monta o host pela região; Resend fixa o usuário; relay sem credenciais', () => {
  assert.match(gerar({ smtp: 'ses', sesRegiao: 'us-east-1' }).env, /SMTP_HOST=email-smtp\.us-east-1\.amazonaws\.com/);
  assert.match(gerar({ smtp: 'resend' }).env, /SMTP_USER=resend/);
  const relay = gerar({ smtp: 'relay', smtpHost: 'smtp.acme.local', smtpPorta: '25' });
  assert.match(relay.env, /SMTP_HOST=smtp\.acme\.local\nSMTP_PORT=25/);
  assert.doesNotMatch(relay.compose, /Smtp__UserName/);
});

test('sem e-mail: nada de Smtp no compose nem no .env', () => {
  const r = gerar({ smtp: 'nenhum' });
  assert.doesNotMatch(r.compose, /Smtp__Host/);
  assert.doesNotMatch(r.env, /SMTP_/);
});

test('endereço aceita URL colada e limpa', () => {
  const r = gerar({ endereco: 'https://nexus.acme.com.br/' });
  assert.equal(r.opcoes.endereco, 'nexus.acme.com.br');
  assert.match(r.compose, /AllowedHosts: "nexus\.acme\.com\.br;localhost"/);
});

test('rede fechada: docker load no lugar do pull, sem token no .env', () => {
  const r = gerar({ registro: 'arquivo' });
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

test('o exemplo estático da página é o que o montador gera por padrão', () => {
  const r = gerar(PADRAO);
  const blocos = blocosEstaticos();
  assert.equal(blocos.yaml, r.compose, 'docker-compose.yml estático difere do gerado');
  assert.equal(blocos.bash, r.comandos, 'comandos estáticos diferem dos gerados');
  assert.equal(blocos.nginx, r.proxy.texto, 'bloco do Nginx estático difere do gerado');
});
