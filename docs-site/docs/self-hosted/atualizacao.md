# Atualização

As versões seguem [SemVer](https://semver.org/lang/pt-BR/): `1.17.0` → `1.17.1` corrige; `1.18.0`
acrescenta; `2.0.0` muda algo que pede atenção na atualização (as notas da versão avisam).

## Subir uma versão

```bash
cd /opt/nexus
sudo nexus-backup                                   # 1. dump antes
docker pull ghcr.io/aamora-tec/nexus-web:1.18.0     # 2. a imagem nova (docker login feito na instalação; refaça se o token foi renovado)
NEXUS_VERSION=1.18.0 docker compose up -d --wait web # 3. troca; as migrations rodam sozinhas
curl -s https://nexus.suaempresa.com.br/version     # 4. confere
```

Antes do passo 2, confira em `/admin/licenca` a data limite de atualizações da sua
[licença](licenca.md): uma imagem publicada depois dela sobe só com a página de bloqueio, sem
tocar no banco, e o passo 3 falha no `--wait`. Nesse caso, renove a licença ou use o rollback.

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

Para receber as notas de cada versão, escreva para suportenexus@aamora.com.br.
