# Backup e Recuperação — Nexus

> Criado em 07/08/2026 durante a auditoria pré-produção. Antes disso não existia nenhuma
> estratégia de backup documentada nem implementada — o único registro era a menção a "perda de
> backup sem cópia recuperável" no [plano de resposta a incidente](lgpd/plano-resposta-incidente.md),
> que pressupunha um backup que não existia.

Um backup só conta se já foi restaurado pelo menos uma vez. As seções abaixo descrevem o que
precisa ser copiado, como copiar e — principalmente — como restaurar.

## 1. O que precisa de backup

O Nexus tem **dois** estados persistentes. Um backup só do banco perde os anexos.

| # | Item | Onde vive | Perda se faltar |
|---|---|---|---|
| 1 | Banco PostgreSQL | Servidor de banco (MonsterASP) | Tudo: contas, workspaces, tarefas, docs, chat, audit log |
| 2 | Arquivos enviados | `wwwroot/App_Data/uploads/` no servidor | Anexos de tarefas/chat e avatares. As linhas no banco continuam, apontando para arquivos inexistentes |
| 3 | Chaves de Data Protection | Tabela `DataProtectionKeys` (item 1) | Todos os cookies de sessão são invalidados; usuários precisam entrar de novo |
| 4 | Segredos de deploy | GitHub Actions Secrets | Impossível fazer deploy até recriar |

> **Atenção:** o item 3 vive dentro do banco, mas é criptografado com DPAPI (chave da máquina
> Windows) ou com o certificado configurado em `DataProtection:CertificatePath`. Restaurar o banco
> em **outra** máquina sem o mesmo certificado torna essas chaves ilegíveis — o app volta a
> funcionar, mas todo mundo é deslogado. Isso é esperado e aceitável; não é perda de dado.

## 2. Rotina de backup

### 2.1 Banco (diário)

```bash
# Dump lógico completo, comprimido, com timestamp.
pg_dump \
  --host="$PGHOST" --port=5432 --username="$PGUSER" --dbname=Nexus \
  --format=custom --compress=9 \
  --file="nexus-$(date +%Y%m%d-%H%M%S).dump"
```

Retenção mínima recomendada: 7 diários + 4 semanais. O audit log tem retenção de 730 dias
(`AUDIT_LOG_RETENTION_DAYS`) por obrigação de LGPD, então backups mais antigos que isso não têm
valor de conformidade.

### 2.2 Anexos (diário)

```bash
# Espelha o diretório de uploads. --ignore-existing porque os nomes são GUIDs: um arquivo
# existente nunca muda de conteúdo.
lftp -u "$FTP_USER,$FTP_PASS" -e \
  "mirror --verbose --ignore-existing /wwwroot/App_Data/uploads ./backup-uploads; quit" \
  ftps://"$FTP_HOST"
```

### 2.3 Antes de cada deploy com migration destrutiva

O Nexus aplica migrations automaticamente no startup (`APPLY_MIGRATIONS_ON_STARTUP`). Não há
rollback automático. Faça o dump da seção 2.1 **antes** de fazer merge na `main` quando a release
contiver `DropColumn`, `DropTable`, `AlterColumn` ou qualquer `migrationBuilder.Sql` que altere
dados.

Para revisar o SQL que uma migration vai gerar, sem aplicá-la:

```bash
dotnet ef migrations script <ÚltimaAplicada> <Nova> \
  --project src/Nexus.Infrastructure --startup-project src/Nexus.Web \
  --output migration.sql
```

## 3. Restauração

### 3.1 Banco

```bash
# 1. Coloque o app offline (evita escrita concorrente durante a restauração).
curl --ssl-reqd -T app_offline.htm --user "$FTP_USER:$FTP_PASS" \
  "ftp://$FTP_HOST/wwwroot/app_offline.htm"

# 2. Restaure em um banco NOVO primeiro — nunca por cima do banco atual.
createdb -h "$PGHOST" -U "$PGUSER" Nexus_restore
pg_restore -h "$PGHOST" -U "$PGUSER" -d Nexus_restore --no-owner nexus-AAAAMMDD-HHMMSS.dump

# 3. Valide (ver 3.3). Só então aponte a connection string para Nexus_restore,
#    ou renomeie os bancos.

# 4. Traga o app de volta.
curl --ssl-reqd --user "$FTP_USER:$FTP_PASS" -Q "DELE /wwwroot/app_offline.htm" "ftp://$FTP_HOST/"
```

### 3.2 Anexos

```bash
lftp -u "$FTP_USER,$FTP_PASS" -e \
  "mirror --reverse --verbose ./backup-uploads /wwwroot/App_Data/uploads; quit" \
  ftps://"$FTP_HOST"
```

### 3.3 Validação pós-restauração

Não considere a restauração concluída sem estes quatro passos:

1. `GET /health/ready` retorna `Healthy` (prova que o app alcança o banco restaurado).
2. Login com uma conta conhecida funciona.
3. Um workspace abre e lista suas tarefas.
4. O download de um anexo antigo funciona — é o que prova que banco **e** arquivos estão
   consistentes entre si.

## 4. Teste de recuperação (trimestral)

Execute a seção 3 inteira contra um banco descartável e registre a data aqui. Um backup nunca
restaurado deve ser tratado como inexistente.

| Data do teste | Executado por | Resultado |
|---|---|---|
| _(pendente — nenhum teste de restauração executado até agora)_ | | |

## 5. Automação

O backup do banco (seção 2.1) está automatizado em
[`.github/workflows/backup-nexus.yml`](../.github/workflows/backup-nexus.yml): roda todo dia às
03:00 UTC (meia-noite em São Paulo), gera o dump, **verifica que ele é restaurável**
(`pg_restore --list` + checagem de tamanho mínimo) e o publica como artefato com retenção de 30
dias. Também pode ser disparado sob demanda (`workflow_dispatch`).

> **Ele começa desligado, de propósito.** Só executa depois que a variável de repositório
> `BACKUP_ENABLED` for definida como `true`.
>
> O motivo é de proteção de dados, não técnico: um dump contém dado pessoal de todos os usuários
> (nome, e-mail, telefone, hash de senha, avatar e o conteúdo integral dos workspaces). Guardá-lo
> como artefato do GitHub torna o GitHub um **operador adicional** desses dados, o que o
> [ROT](lgpd/rot.md) hoje não declara. Antes de habilitar: decida onde os dumps devem viver e
> atualize o ROT. Um bucket sob seu controle é a resposta melhor para dado pessoal; se optar por
> isso, troque o passo de upload.

Os anexos (seção 2.2) **continuam manuais** — o workflow não os copia, porque eles vivem no
servidor FTP e não no banco.

## 6. Limitações conhecidas

- **Anexos sem automação.** Só o banco é copiado automaticamente; `App_Data/uploads` depende da
  rotina manual da seção 2.2. Um restore de banco sem os arquivos deixa anexos quebrados.
- **Dependência de conectividade.** O workflow assume que o PostgreSQL aceita conexões dos
  runners do GitHub. Hospedagens gerenciadas costumam restringir por IP — nesse caso o job falha
  no passo de verificação de conectividade (de propósito, para não gerar backup vazio) e a rotina
  precisa rodar de uma máquina autorizada.
- **RPO/RTO não definidos.** Com backup diário, a perda máxima é de ~24h de dados; a recuperação
  ainda depende de alguém executar a seção 3 manualmente.
- **Sem réplica.** O PostgreSQL do MonsterASP é uma instância única; a falha do host é um
  outage completo até a restauração.
- **Nenhum teste de restauração executado ainda** (seção 4). O workflow verifica que o dump é
  legível, o que não é a mesma coisa que provar que o sistema volta a funcionar a partir dele.
