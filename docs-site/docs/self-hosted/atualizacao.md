# Atualização

As versões seguem [SemVer](https://semver.org/lang/pt-BR/): `1.17.0` → `1.17.1` corrige; `1.18.0`
acrescenta; `2.0.0` muda algo que pede atenção na atualização (as notas da versão avisam).

## Subir uma versão

```bash
cd /opt/nexus
sudo nexus-backup                                   # 1. dump antes
docker load < nexus-web-1.18.0.tar.gz                # 2. a imagem nova
NEXUS_VERSION=1.18.0 docker compose up -d --wait web # 3. troca; as migrations rodam sozinhas
curl -s https://nexus.suaempresa.com.br/version     # 4. confere
```

O `--wait` espera o container ficar saudável (`/health/live`). Se não ficar, o compose falha e o
container antigo já foi substituído: use o rollback abaixo.

## Rollback

```bash
NEXUS_VERSION=1.17.0 docker compose up -d --wait web
```

Funciona enquanto a versão anterior não tiver uma migration que a versão nova desfez. As notas
da versão dizem quando uma atualização é só para frente; nesse caso, o caminho de volta é
restaurar o dump feito antes.

## Mais de uma instância

Para rodar duas ou mais instâncias do app atrás de um balanceador, configure
`Redis__ConnectionString` em todas: o chat, a presença nos docs e os limites de uso passam a ser
compartilhados. Sem o Redis, cada instância só enxerga as próprias mensagens.

## Notas de versão

Cada versão publicada tem notas no GitHub:
<https://github.com/gentilpedro/Nexus/releases>.
