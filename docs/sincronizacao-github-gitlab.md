# Sincronização entre o GitHub e o GitLab

O Nexus vive em dois repositórios ao mesmo tempo:

| | |
|---|---|
| [GitHub](https://github.com/gentilpedro/Nexus) | De onde sai o deploy para produção (FTPS → IIS) e onde as tags/releases são criadas. |
| [GitLab](https://gitlab.com/senac-projetos-de-desenvolvimento/2025-pedro-gentil/tcc_2/nexus) | Repositório exigido pelo TCC — precisa refletir o mesmo código e o mesmo histórico. |

Manter os dois iguais "na mão" não sobrevive a um dia corrido: basta esquecer um `git push` para o
site estar rodando um código que não está no repositório da faculdade, ou o contrário. Esta página
descreve como isso é automático.

## A ideia central: um histórico só

Os dois repositórios **compartilham o mesmo histórico de commits**. O commit `abc1234` é o mesmo
objeto nos dois lados, com o mesmo SHA, o mesmo autor e a mesma data.

É isso que torna o resto simples. Com históricos diferentes (que foi como os dois repositórios
nasceram), sincronizar exigiria converter commits em patches e reaplicá-los do outro lado — um
processo que quebra em qualquer conflito e que faz as datas e os autores divergirem. Com um
histórico só, sincronizar é um `git push` comum.

O preço é uma regra: **ninguém reescreve o histórico da main** (nada de `--force`, `rebase` ou
`amend` em commit que já foi publicado). Reescrever de um lado faz o outro lado divergir na hora.

## Os dois sentidos

```
                     ┌──────────────────────────────────────────┐
                     │              GitHub (main)               │
                     │   deploy FTPS → IIS + tag + release      │
                     └──────────────────────────────────────────┘
                          ▲                             │
       Pull Request para  │                             │  git push automático
       aprovação manual   │                             │  (branches + tags)
                          │                             ▼
                     ┌──────────────────────────────────────────┐
                     │              GitLab (main)               │
                     │   quality gate + release do TCC          │
                     └──────────────────────────────────────────┘
```

### GitHub → GitLab: espelhamento direto

`.github/workflows/sync-gitlab.yml` roda a cada push (em qualquer branch) e ao fim da workflow
`Deploy Nexus`, e empurra branches e tags para o GitLab.

- A `main` é empurrada **sem `--force`**. Se o push falhar, é porque a main do GitLab tem commit
  que não existe no GitHub — e nesse caso a workflow falha com um aviso em vez de apagar o
  trabalho. O caminho certo é mergear o PR de sincronização (abaixo) e rodar de novo.
- As outras branches vão junto para que o GitLab mostre o trabalho como ele aconteceu, não só a
  main. Branches `sync/*` ficam de fora — são artefato da sincronização, não trabalho.
- O segundo gatilho (`workflow_run` de `Deploy Nexus`) existe porque a tag da release é criada
  pelo `GITHUB_TOKEN`, e o GitHub de propósito não dispara workflows para refs criadas com esse
  token. Sem ele, a tag ficaria só no GitHub.

### GitLab → GitHub: Pull Request, nunca push na main

O job `sync-github` do `.gitlab-ci.yml` roda na branch default, **depois do `verify`**, e:

1. Compara o commit com a main do GitHub. Se ele já estiver lá (chegou pelo espelhamento), para
   por aqui — é esta verificação que impede o laço infinito de um lado empurrando para o outro
   para sempre.
2. Se for commit novo, empurra para a branch `sync/gitlab` no GitHub e abre um Pull Request para
   a `main`.

O PR **não é mergeado automaticamente**. Ele espera aprovação, como qualquer outra mudança — o
merge é o que dispara o deploy em produção, e isso não acontece sem alguém olhar. Se já houver um
PR de sincronização aberto, a branch é atualizada e nenhum PR novo é criado.

## Releases e tags

A tag é criada **uma única vez, no GitHub**, depois do deploy — ela significa "esta versão está no
ar". O espelhamento leva a tag para o GitLab, e a chegada dela dispara o job `release` de lá, que
cria a Release correspondente no GitLab com as mesmas notas (apontando para os commits do GitLab).

Por isso o pipeline do GitLab não calcula mais a própria versão para lançar: duas numerações
independentes acabariam com tags de mesmo nome apontando para commits diferentes, e o espelhamento
passaria a falhar.

## O fluxo de trabalho do dia a dia

Tanto faz onde você trabalha, desde que a main nunca receba push direto:

- **Trabalhando pelo GitHub** (caminho normal): branch → PR → merge na main → deploy → o
  espelhamento leva tudo para o GitLab sozinho.
- **Trabalhando pelo GitLab** (ex.: pela interface web, durante a apresentação do TCC): branch →
  Merge Request → merge na main do GitLab → o pipeline abre um PR no GitHub → você aprova e
  mergeia → o deploy roda e o espelhamento fecha o ciclo.

O segundo caminho é mais longo de propósito. O GitHub é a origem da verdade do que está em
produção; entrar por ele é sempre mais curto.

## Configuração (uma vez só)

### 1. Token do GitLab, guardado no GitHub

1. No GitLab: **User settings → Access tokens** → novo token com escopo `write_repository` e papel
   **Maintainer**. (Um Project Access Token do projeto também serve.)
2. No GitHub, em `gentilpedro/Nexus` → **Settings → Secrets and variables → Actions → New
   repository secret**:
   - Nome: `GITLAB_SYNC_TOKEN`
   - Valor: o token gerado.

### 2. Token do GitHub, guardado no GitLab

1. No GitHub: **Settings → Developer settings → Personal access tokens → Fine-grained tokens** →
   novo token com acesso ao repositório `gentilpedro/Nexus` e as permissões:
   - `Contents`: Read and write (empurrar a branch `sync/gitlab`)
   - `Pull requests`: Read and write (abrir o PR)
2. No GitLab, no projeto → **Settings → CI/CD → Variables → Add variable**:
   - Chave: `GITHUB_SYNC_TOKEN`
   - Valor: o token gerado
   - Marcar **Masked** e **Protected** (a branch default é protegida).

### 3. Branch protegida no GitLab

A main do GitLab é protegida contra push direto de pessoas, mas o espelhamento precisa conseguir
escrever nela. Em **Settings → Repository → Protected branches**, a main deve ter *Allowed to
push and merge* incluindo o usuário dono do token do passo 1 (ou o Project Access Token, se foi
esse o caminho escolhido).

### 4. Alinhamento inicial dos históricos

Feito uma única vez, quando esta mudança entrou. Os dois repositórios tinham históricos sem
nenhum commit em comum (o GitLab era uma cópia achatada em um "Initial commit"), e o alinhamento
substituiu o histórico do GitLab pelo do GitHub — 152 commits com autor, data e mensagem
originais, em vez de três.

```bash
git clone --bare https://github.com/gentilpedro/Nexus.git nexus.git
cd nexus.git
git push --force <url-do-gitlab> 'refs/heads/main:refs/heads/main'
git push --force <url-do-gitlab> 'refs/tags/*:refs/tags/*'
git push        <url-do-gitlab> 'refs/heads/*:refs/heads/*'
```

Depois disso, `--force` nunca mais.

## Quando algo dá errado

**A workflow `Sincronizar com o GitLab` falhou dizendo que a main divergiu.**
Alguém commitou direto na main do GitLab. Procure o PR aberto pelo job `sync-github` no GitHub,
mergeie, e rode a workflow de novo (`Actions → Sincronizar com o GitLab → Run workflow`). Não
resolva com `--force`: isso apagaria o commit feito no GitLab.

**O job `sync-github` falhou dizendo que não há histórico em comum.**
O alinhamento inicial (passo 4) não foi feito, ou o histórico de um dos lados foi reescrito.

**O job `sync-github` falhou por falta de `GITHUB_SYNC_TOKEN` (ou a workflow por falta de
`GITLAB_SYNC_TOKEN`).**
Tokens têm validade. Gere de novo e atualize a variável correspondente.

**Os minutos de CI do GitLab estão acabando.**
Cada commit vindo do GitHub roda o build completo de novo lá. Se isso virar problema, trocar o
push da main na workflow de espelhamento por `git push -o ci.skip` faz o GitLab receber o código
sem criar pipeline — ao custo de não ter mais o pipeline do GitLab como evidência no TCC.
