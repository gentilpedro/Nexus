# Backup e restauração

## O que precisa de backup

| O quê | Onde | Perder significa |
|---|---|---|
| Banco PostgreSQL | volume `pgdata` | Perder tudo: usuários, tarefas, docs, chat |
| Anexos | volume `uploads` | Perder os arquivos anexados a tarefas, chat e docs |
| `dataprotection.pfx` e `.env` | `/opt/nexus` | Todo mundo deslogado; sem a senha do banco não há restauração |

## Rotina diária

```bash
#!/bin/bash
# /usr/local/bin/nexus-backup — rodar pelo cron, de madrugada
set -euo pipefail
cd /opt/nexus
DIA=$(date +%F)
mkdir -p backups

# Banco: dump lógico completo, formato custom (restaura com pg_restore).
docker compose exec -T db sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" --format=custom --no-owner' \
  > "backups/nexus-$DIA.dump"

# Anexos: os nomes são GUIDs e um arquivo nunca muda, então só os novos entram.
docker run --rm -v nexus_uploads:/u:ro -v "$PWD/backups:/b" alpine \
  sh -c "tar czf /b/uploads-$DIA.tar.gz -C /u ."

# Guarda 7 dias; copie a pasta para fora do servidor.
find backups -name 'nexus-*.dump' -mtime +7 -delete
find backups -name 'uploads-*.tar.gz' -mtime +7 -delete
```

Copie `backups/` para outro lugar (outro servidor, um bucket). Um backup no mesmo disco do
servidor só protege contra erro humano, não contra perder o servidor.

## Antes de atualizar

Faça um dump antes de subir uma versão nova. As migrations rodam sozinhas e não têm desfazer
automático; o dump é o caminho de volta.

## Restaurar o banco

```bash
cd /opt/nexus
docker compose stop web                                  # 1. app fora, sem escrita concorrente
docker compose exec -T db sh -c 'dropdb -U "$POSTGRES_USER" --force "$POSTGRES_DB" && createdb -U "$POSTGRES_USER" "$POSTGRES_DB"'
docker compose exec -T db sh -c 'pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" --no-owner --no-privileges --exit-on-error' \
  < backups/nexus-2026-11-17.dump                        # 2. restaura
docker compose up -d --wait web                          # 3. app de volta
```

Prefira restaurar primeiro num banco **novo** (`createdb Nexus_restore`) e validar antes de
trocar o banco em uso.

## Restaurar os anexos

```bash
docker run --rm -i -v nexus_uploads:/u alpine sh -c 'tar xzf - -C /u' < backups/uploads-2026-11-17.tar.gz
```

## Validar

- Entrar com uma conta e abrir um workspace, uma tarefa com anexo e um doc.
- `SELECT count(*) FROM "AspNetUsers";` bate com o esperado.
- Se o `dataprotection.pfx` não for o mesmo da origem, todo mundo precisa entrar de novo; nenhum
  dado se perde.
