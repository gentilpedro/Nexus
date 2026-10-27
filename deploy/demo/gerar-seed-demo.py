"""
Gera deploy/demo/seed-demo.sql: o workspace de demonstração "Aurora Digital" usado no vídeo e na
banca do TCC. TEMPORÁRIO — apagar depois da banca final (ver docs/dados-demonstracao.md).

    python deploy/demo/gerar-seed-demo.py > deploy/demo/seed-demo.sql

Tudo o que o script cria é identificável para a remoção (deploy/demo/remover-demo.sql):
- ids com o prefixo d3e70000- (workspace, contas falsas e todo o resto);
- contas falsas com e-mail @example.com (domínio reservado: nenhum e-mail sai de verdade).

O dono do workspace é a conta real do Pedro (gentil.pedro21@gmail.com), que é sem limites em
produção (Plans:UnlimitedAccounts) — por isso tudo fica liberado. As datas são relativas a now(),
então o seed pode ser rodado em qualquer dia. A senha das contas falsas entra como variável do
psql (-v senha_hash=...), nunca no repositório.
"""

import uuid

PREFIX = "d3e70000"
_seq = {}


def gid(kind: str, n: int) -> str:
    """Id determinístico e reconhecível: d3e70000-<tipo>-4000-8000-<n>."""
    kinds = {"ws": "0001", "user": "0002", "space": "0003", "list": "0004", "status": "0005",
             "label": "0006", "sprint": "0007", "item": "0008", "comment": "0009", "doc": "000a",
             "chat": "000b", "mention": "000c", "notif": "000d", "field": "000e", "option": "000f",
             "value": "0010", "itemlabel": "0011", "snap": "0012", "member": "0013"}
    return f"{PREFIX}-{kinds[kind]}-4000-8000-{n:012d}"


def nid(kind: str) -> str:
    _seq[kind] = _seq.get(kind, 0) + 1
    return gid(kind, _seq[kind])


def q(s):
    if s is None:
        return "NULL"
    return "'" + str(s).replace("'", "''") + "'"


def ago(days: float, hours: float = 0) -> str:
    """Timestamp relativo: dias atrás (negativo = no futuro)."""
    total = days * 24 + hours
    return f"(now() - interval '{total} hours')"


def day(offset: int) -> str:
    """Meia-noite UTC de hoje + offset dias (datas de início/entrega e sprints)."""
    return f"((current_date + {offset})::timestamp AT TIME ZONE 'UTC')"


out = []
emit = out.append

WS = gid("ws", 1)
PEDRO = "(SELECT \"Id\" FROM \"AspNetUsers\" WHERE \"NormalizedEmail\" = 'GENTIL.PEDRO21@GMAIL.COM')"

# ---------------------------------------------------------------- pessoas
people = [
    # chave, nome, e-mail, papel (0 Owner, 1 Admin, 2 Member)
    ("mariana", "Mariana Costa", "mariana.costa@example.com", 1),
    ("lucas", "Lucas Almeida", "lucas.almeida@example.com", 1),
    ("ana", "Ana Beatriz Souza", "ana.souza@example.com", 2),
    ("carlos", "Carlos Eduardo Lima", "carlos.lima@example.com", 2),
    ("rafael", "Rafael Santos", "rafael.santos@example.com", 2),
    ("juliana", "Juliana Ferreira", "juliana.ferreira@example.com", 2),
    ("thiago", "Thiago Martins", "thiago.martins@example.com", 2),
    ("camila", "Camila Rocha", "camila.rocha@example.com", 2),
]
U = {"pedro": PEDRO}
NAME = {"pedro": "Pedro Rodrigues"}
for i, (key, name, email, _) in enumerate(people, start=1):
    U[key] = q(gid("user", i))
    NAME[key] = name

emit("-- GERADO por deploy/demo/gerar-seed-demo.py. Não edite à mão.")
emit("-- Dados FALSOS de demonstração (vídeo e banca do TCC). APAGAR depois da banca:")
emit("-- deploy/demo/remover-demo.sql e docs/dados-demonstracao.md.")
emit("\\set ON_ERROR_STOP on")
emit("BEGIN;")
emit("DO $$ BEGIN")
emit(f"  IF {PEDRO} IS NULL THEN RAISE EXCEPTION 'Conta gentil.pedro21@gmail.com não encontrada'; END IF;")
emit(f"  IF EXISTS (SELECT 1 FROM \"Workspaces\" WHERE \"Id\" = '{WS}') THEN RAISE EXCEPTION 'Workspace de demonstração já existe: rode remover-demo.sql antes'; END IF;")
emit("END $$;")

for i, (key, name, email, _) in enumerate(people, start=1):
    emit(
        'INSERT INTO "AspNetUsers" ("Id","DisplayName","UserName","NormalizedUserName","Email","NormalizedEmail",'
        '"EmailConfirmed","PasswordHash","SecurityStamp","ConcurrencyStamp","PhoneNumberConfirmed","TwoFactorEnabled",'
        '"LockoutEnabled","AccessFailedCount") VALUES '
        f"({U[key]},{q(name)},{q(email)},{q(email.upper())},{q(email)},{q(email.upper())},true,:'senha_hash',"
        f"{q(str(uuid.uuid5(uuid.NAMESPACE_URL, email + '#stamp')).upper().replace('-', ''))},"
        f"{q(str(uuid.uuid5(uuid.NAMESPACE_URL, email + '#concurrency')))},false,false,true,0);")

# ---------------------------------------------------------------- workspace
emit(f'INSERT INTO "Workspaces" ("Id","Name","Slug","Color","IsArchived","CreatedAtUtc") VALUES '
     f"('{WS}','Aurora Digital','aurora-digital-demo','#5B5CEB',false,{ago(40)});")
emit(f'INSERT INTO "WorkspaceSubscriptions" ("WorkspaceId","Plan","Status","Seats","CreatedAtUtc","UpdatedAtUtc") VALUES '
     f"('{WS}',0,0,{len(people) + 1},{ago(40)},{ago(40)});")
emit(f'INSERT INTO "WorkspaceMembers" ("Id","WorkspaceId","UserId","Role","JoinedAtUtc") VALUES '
     f"('{nid('member')}','{WS}',{PEDRO},0,{ago(40)});")
for i, (key, _, _, role) in enumerate(people, start=1):
    emit(f'INSERT INTO "WorkspaceMembers" ("Id","WorkspaceId","UserId","Role","JoinedAtUtc") VALUES '
         f"('{nid('member')}','{WS}',{U[key]},{role},{ago(39 - i)});")

# ---------------------------------------------------------------- spaces, listas, status, labels
STATUS = {}  # (lista, chave) -> id
LABEL = {}


def space(name, color, order):
    sid = nid("space")
    emit(f'INSERT INTO "Spaces" ("Id","WorkspaceId","Name","Color","Icon","SortOrder") VALUES '
         f"('{sid}','{WS}',{q(name)},{q(color)},NULL,{order});")
    return sid


def tasklist(space_id, key, name, order, statuses, labels):
    lid = nid("list")
    emit(f'INSERT INTO "TaskLists" ("Id","SpaceId","Name","SortOrder") VALUES (\'{lid}\',\'{space_id}\',{q(name)},{order});')
    for o, (skey, sname, cat) in enumerate(statuses):
        st = nid("status")
        STATUS[(key, skey)] = st
        emit(f'INSERT INTO "TaskStatusDefinitions" ("Id","TaskListId","Name","Color","SortOrder","Category","IsDefault") VALUES '
             f"('{st}','{lid}',{q(sname)},NULL,{o},{cat},{'true' if o == 0 else 'false'});")
    for o, (lkey, lname, color) in enumerate(labels):
        lb = nid("label")
        LABEL[(key, lkey)] = lb
        emit(f'INSERT INTO "Labels" ("Id","TaskListId","Name","Description","Color","SortOrder") VALUES '
             f"('{lb}','{lid}',{q(lname)},NULL,{q(color)},{o});")
    return lid


DEV_STATUSES = [("todo", "A Fazer", 0), ("doing", "Em Andamento", 1), ("review", "Em Revisão", 1), ("done", "Concluído", 2)]
SIMPLE_STATUSES = [("todo", "A Fazer", 0), ("doing", "Em Andamento", 1), ("done", "Concluído", 2)]

sp_prod = space("Produto", "#5B5CEB", 0)
sp_ops = space("Operações", "#0F766E", 1)
sp_mkt = space("Marketing", "#BE185D", 2)

L_APP = tasklist(sp_prod, "app", "App Sabor Local", 0, DEV_STATUSES, [
    ("backend", "backend", "#2563EB"), ("frontend", "frontend", "#7C3AED"), ("mobile", "mobile", "#0F766E"),
    ("ux", "ux", "#BE185D"), ("bug", "bug", "#DC2626"), ("perf", "performance", "#EA580C")])
L_SITE = tasklist(sp_prod, "site", "Site institucional", 1, SIMPLE_STATUSES, [
    ("conteudo", "conteúdo", "#15803D"), ("seo", "seo", "#C2410C")])
L_SUP = tasklist(sp_ops, "sup", "Suporte e incidentes", 0,
                 [("todo", "Aberto", 0), ("doing", "Investigando", 1), ("done", "Resolvido", 2)], [
    ("p1", "crítico", "#DC2626"), ("cliente", "cliente", "#2563EB")])
L_INFRA = tasklist(sp_ops, "infra", "Infraestrutura", 1, SIMPLE_STATUSES, [("devops", "devops", "#0F766E")])
L_MKT = tasklist(sp_mkt, "mkt", "Campanha de lançamento", 0, SIMPLE_STATUSES, [
    ("social", "redes sociais", "#BE185D"), ("email", "e-mail", "#7C3AED")])
LIST_ID = {"app": L_APP, "site": L_SITE, "sup": L_SUP, "infra": L_INFRA, "mkt": L_MKT}

# Campos personalizados na lista principal
F_POINTS = nid("field")
F_ENV = nid("field")
emit(f'INSERT INTO "CustomFieldDefinitions" ("Id","TaskListId","Name","Type","Required","SortOrder") VALUES '
     f"('{F_POINTS}','{L_APP}','Story points',1,false,0),('{F_ENV}','{L_APP}','Plataforma',3,false,1);")
ENV_OPTS = {}
for o, label in enumerate(["Android", "iOS", "Web", "API"]):
    oid = nid("option")
    ENV_OPTS[label] = oid
    emit(f'INSERT INTO "CustomFieldOptions" ("Id","CustomFieldDefinitionId","Label","SortOrder") VALUES (\'{oid}\',\'{F_ENV}\',{q(label)},{o});')

# ---------------------------------------------------------------- sprints
SPRINT = {}
for key, name, goal, start, end, status, started, completed in [
    ("s1", "Sprint 1 — Fundação", "Login, catálogo e estrutura da API no ar.", -34, -20, 2, 34, 20),
    ("s2", "Sprint 2 — Pedido e carrinho", "Cliente monta o carrinho e acompanha o pedido.", -20, -6, 2, 20, 6),
    ("s3", "Sprint 3 — Pagamento", "Pagamento por Pix e cartão, com recibo por e-mail.", -6, 8, 1, 6, None),
    ("s4", "Sprint 4 — Avaliações e cupons", "Avaliar restaurante e aplicar cupom no checkout.", 8, 22, 0, None, None),
]:
    sid = nid("sprint")
    SPRINT[key] = sid
    emit(f'INSERT INTO "Sprints" ("Id","TaskListId","Name","Goal","StartDateUtc","EndDateUtc","Status","CreatedAtUtc","StartedAtUtc","CompletedAtUtc") VALUES '
         f"('{sid}','{L_APP}',{q(name)},{q(goal)},{day(start)},{day(end)},{status},{ago(36)},"
         f"{ago(started) if started is not None else 'NULL'},{ago(completed) if completed is not None else 'NULL'});")

# ---------------------------------------------------------------- itens
ITEM = {}
order_by_list = {}


def item(key, lst, title, *, type_=0, status="todo", prio=2, who=None, by="mariana", start=None, due=None,
         sprint=None, epic=None, labels=(), points=None, plat=None, created=30, desc=None):
    wid = nid("item")
    ITEM[key] = wid
    order_by_list[lst] = order_by_list.get(lst, 0) + 1
    emit('INSERT INTO "WorkItems" ("Id","TaskListId","Title","Description","Type","StatusId","Priority","AssigneeId",'
         '"StartDateUtc","DueDateUtc","SortOrder","CreatedAtUtc","UpdatedAtUtc","ParentEpicId","SprintId","CreatedByUserId") VALUES '
         f"('{wid}','{LIST_ID[lst]}',{q(title)},{q(desc)},{type_},'{STATUS[(lst, status)]}',{prio},"
         f"{U[who] if who else 'NULL'},{day(start) if start is not None else 'NULL'},{day(due) if due is not None else 'NULL'},"
         f"{order_by_list[lst]},{ago(created)},{ago(max(0, created - 3), 5)},"
         f"{q(ITEM[epic]) if epic else 'NULL'},{q(SPRINT[sprint]) if sprint else 'NULL'},{U[by]});")
    for lb in labels:
        emit(f'INSERT INTO "WorkItemLabels" ("Id","WorkItemId","LabelId") VALUES (\'{nid("itemlabel")}\',\'{wid}\',\'{LABEL[(lst, lb)]}\');')
    if points is not None:
        emit(f'INSERT INTO "CustomFieldValues" ("Id","WorkItemId","CustomFieldDefinitionId","Value") VALUES (\'{nid("value")}\',\'{wid}\',\'{F_POINTS}\',\'{points}\');')
    if plat is not None:
        emit(f'INSERT INTO "CustomFieldValues" ("Id","WorkItemId","CustomFieldDefinitionId","Value") VALUES (\'{nid("value")}\',\'{wid}\',\'{F_ENV}\',\'{ENV_OPTS[plat]}\');')


T, BUG, STORY, EPIC = 0, 1, 2, 3

# Épicos (Gantt)
item("e_auth", "app", "Conta e login", type_=EPIC, status="done", prio=3, who="lucas", start=-34, due=-20, created=36,
     desc="<p>Cadastro, login social e recuperação de senha.</p>")
item("e_order", "app", "Pedido e carrinho", type_=EPIC, status="done", prio=3, who="mariana", start=-20, due=-6, created=36,
     desc="<p>Do cardápio ao pedido confirmado, com acompanhamento em tempo real.</p>")
item("e_pay", "app", "Pagamento", type_=EPIC, status="doing", prio=4, who="ana", start=-6, due=8, created=30,
     desc="<p>Pix e cartão via gateway, com recibo por e-mail e estorno pelo suporte.</p>")
item("e_review", "app", "Avaliações e cupons", type_=EPIC, status="todo", prio=2, who="juliana", start=8, due=22, created=25,
     desc="<p>Avaliar o restaurante depois da entrega e aplicar cupons no checkout.</p>")
item("e_launch", "app", "Lançamento nas lojas", type_=EPIC, status="todo", prio=3, who="thiago", start=18, due=30, created=20,
     desc="<p>Publicação na Play Store e na App Store, com testes beta antes.</p>")

# Sprint 1 — concluída
s1 = [
    ("Cadastro com e-mail e senha", STORY, "ana", ["backend"], 5, "API"),
    ("Login com Google", STORY, "ana", ["backend"], 3, "API"),
    ("Tela de login no app", STORY, "thiago", ["mobile", "ux"], 3, "Android"),
    ("Recuperação de senha por e-mail", T, "ana", ["backend"], 2, "API"),
    ("Catálogo de restaurantes (API)", STORY, "lucas", ["backend"], 8, "API"),
    ("Listagem de restaurantes no app", STORY, "thiago", ["mobile"], 5, "Android"),
    ("Protótipo navegável no Figma", T, "juliana", ["ux"], 5, None),
    ("Pipeline de CI com testes", T, "lucas", ["backend"], 3, None),
]
for n, (title, tp, who, labels, pts, plat) in enumerate(s1):
    item(f"s1_{n}", "app", title, type_=tp, status="done", who=who, start=-34 + n, due=-22 + (n % 3), sprint="s1",
         epic="e_auth", labels=labels, points=pts, plat=plat, created=35 - n)

# Sprint 2 — concluída
s2 = [
    ("Cardápio do restaurante com categorias", STORY, "carlos", ["frontend"], 5, "Web"),
    ("Adicionar e remover itens do carrinho", STORY, "thiago", ["mobile"], 5, "Android"),
    ("Cálculo de frete por distância", STORY, "ana", ["backend"], 8, "API"),
    ("Confirmação do pedido pelo restaurante", STORY, "lucas", ["backend"], 5, "API"),
    ("Acompanhamento do pedido em tempo real", STORY, "carlos", ["frontend"], 8, "Web"),
    ("Carrinho perde itens ao trocar de aba", BUG, "thiago", ["mobile", "bug"], 2, "iOS"),
    ("Layout do carrinho no tablet", T, "juliana", ["ux"], 2, None),
]
for n, (title, tp, who, labels, pts, plat) in enumerate(s2):
    item(f"s2_{n}", "app", title, type_=tp, status="done", who=who, start=-20 + n, due=-8 + (n % 3), sprint="s2",
         epic="e_order", labels=labels, points=pts, plat=plat, created=22 - n)

# Sprint 3 — ativa (o burndown mostra o progresso)
s3 = [
    ("Pagamento por Pix com QR Code", STORY, "ana", ["backend"], 8, "API", "done", 4),
    ("Tela de pagamento no app", STORY, "thiago", ["mobile", "ux"], 5, "Android", "done", 3),
    ("Pagamento com cartão de crédito", STORY, "ana", ["backend"], 8, "API", "review", 4),
    ("Recibo do pedido por e-mail", T, "lucas", ["backend"], 3, "API", "doing", 3),
    ("Webhook de confirmação do gateway", T, "lucas", ["backend"], 5, "API", "doing", 4),
    ("Tratar pagamento recusado", STORY, "carlos", ["frontend"], 3, "Web", "todo", 3),
    ("Pix expira e o pedido fica pendente", BUG, "rafael", ["bug", "backend"], 3, "API", "doing", 4),
    ("Testes de ponta a ponta do checkout", T, "rafael", [], 5, None, "todo", 2),
    ("Ícones e estados de carregamento do pagamento", T, "juliana", ["ux"], 2, "Android", "review", 1),
    ("Tempo de resposta do checkout acima de 2s", BUG, "lucas", ["perf", "bug"], 3, "API", "todo", 3),
]
for n, (title, tp, who, labels, pts, plat, st, prio) in enumerate(s3):
    item(f"s3_{n}", "app", title, type_=tp, status=st, prio=prio, who=who, start=-6 + (n % 4), due=2 + n,
         sprint="s3", epic="e_pay", labels=labels, points=pts, plat=plat, created=8 - (n % 5),
         desc="<p>Critério de aceite combinado na planning. Ver o documento <strong>Especificação do checkout</strong>.</p>" if n < 3 else None)

# Sprint 4 — planejada
for n, (title, tp, who, labels, pts) in enumerate([
    ("Avaliar restaurante com estrelas e comentário", STORY, "carlos", ["frontend"], 5),
    ("Moderação de avaliações", STORY, "ana", ["backend"], 5),
    ("Cupom de desconto no checkout", STORY, "lucas", ["backend"], 8),
    ("Tela de cupons no app", STORY, "thiago", ["mobile"], 3),
]):
    item(f"s4_{n}", "app", title, type_=tp, status="todo", who=who, start=8 + n, due=18 + n, sprint="s4",
         epic="e_review", labels=labels, points=pts, created=6)

# Backlog
for n, (title, tp, prio, labels) in enumerate([
    ("Modo escuro no app", STORY, 1, ["mobile", "ux"]),
    ("Programa de fidelidade", STORY, 2, []),
    ("Exportar relatório de vendas para o restaurante", STORY, 2, ["backend"]),
    ("Notificação push de promoção", STORY, 1, ["mobile"]),
    ("Acessibilidade: leitor de tela no cardápio", T, 3, ["ux", "mobile"]),
    ("Beta fechado com 50 usuários", T, 3, []),
    ("Publicar na Play Store", T, 3, ["mobile"]),
    ("Publicar na App Store", T, 3, ["mobile"]),
]):
    item(f"bk_{n}", "app", title, type_=tp, prio=prio, labels=labels,
         epic="e_launch" if n >= 5 else None, start=18 + n if n >= 5 else None, due=26 + n if n >= 5 else None,
         who="thiago" if n >= 6 else None, created=15 - n)

# Site institucional
for n, (title, st, who, labels, start, due) in enumerate([
    ("Página inicial com chamada para baixar o app", "done", "juliana", ["conteudo"], -15, -9),
    ("Página para restaurantes parceiros", "doing", "carlos", ["conteudo"], -5, 3),
    ("Formulário de cadastro de restaurante", "doing", "carlos", [], -3, 5),
    ("Meta tags e sitemap", "todo", "camila", ["seo"], 2, 6),
    ("Blog com 3 posts de lançamento", "todo", "mariana", ["conteudo", "seo"], 4, 14),
    ("Política de privacidade e termos", "done", "mariana", ["conteudo"], -12, -8),
]):
    item(f"site_{n}", "site", title, status=st, who=who, labels=labels, start=start, due=due, created=16 - n)

# Suporte e incidentes
for n, (title, tp, st, prio, who, labels, created) in enumerate([
    ("Cliente não recebe o código de verificação", BUG, "doing", 4, "rafael", ["p1", "cliente"], 1),
    ("Restaurante não vê pedidos novos no painel", BUG, "todo", 4, "lucas", ["p1", "cliente"], 0.3),
    ("Endereço com complemento cortado no recibo", BUG, "done", 2, "ana", ["cliente"], 6),
    ("Dúvida: como alterar o horário de funcionamento", T, "done", 1, "mariana", ["cliente"], 4),
    ("Lentidão ao abrir o cardápio às 19h", BUG, "doing", 3, "lucas", [], 2),
]):
    item(f"sup_{n}", "sup", title, type_=tp, status=st, prio=prio, who=who, labels=labels, created=created, due=1 if st != "done" else None)

# Infraestrutura
for n, (title, st, who, start, due) in enumerate([
    ("Backup diário do banco com teste de restauração", "done", "lucas", -25, -18),
    ("Monitoramento e alertas de erro", "doing", "lucas", -4, 4),
    ("Ambiente de homologação", "done", "camila", -18, -12),
    ("Escalar a API para dois servidores", "todo", "lucas", 6, 15),
]):
    item(f"infra_{n}", "infra", title, status=st, who=who, labels=["devops"], start=start, due=due, created=26 - n * 4)

# Marketing
for n, (title, st, who, labels, start, due) in enumerate([
    ("Calendário de posts do lançamento", "done", "camila", ["social"], -10, -5),
    ("Vídeo de 30s para o Instagram", "doing", "juliana", ["social"], -3, 4),
    ("E-mail de boas-vindas para a lista de espera", "doing", "camila", ["email"], -2, 3),
    ("Parceria com 10 restaurantes do bairro", "todo", "mariana", [], 1, 12),
    ("Cupom de primeira compra", "todo", "camila", ["email", "social"], 5, 10),
]):
    item(f"mkt_{n}", "mkt", title, status=st, who=who, labels=labels, start=start, due=due, created=12 - n)

# ---------------------------------------------------------------- comentários
for key, who, text, hours in [
    ("s3_0", "ana", "Integração com o gateway pronta em homologação. QR Code expira em 15 minutos.", 70),
    ("s3_0", "rafael", "Testei com o app do banco e caiu certinho. Pode fechar!", 60),
    ("s3_2", "ana", "Falta tratar o 3DS de alguns bancos. Abri o PR, @Lucas Almeida pode revisar?", 30),
    ("s3_2", "lucas", "Revisei, deixei dois comentários sobre o retry. Fora isso, aprovado.", 20),
    ("s3_4", "lucas", "O gateway reenvia o evento se a gente não responder em 10s, então a idempotência é obrigatória.", 26),
    ("s3_6", "rafael", "Reproduzi: se o Pix expira, o pedido fica pendente para sempre. Prints no anexo do ticket.", 40),
    ("s3_6", "ana", "Vou colocar um job que cancela o pedido 20 minutos depois da expiração.", 30),
    ("s3_6", "mariana", "Combinado. Precisamos avisar o cliente por push também.", 28),
    ("s3_8", "juliana", "Subi os estados no Figma: carregando, aprovado, recusado e expirado.", 15),
    ("s3_9", "lucas", "A consulta de frete está sem índice. Com o índice caiu para 300ms em homologação.", 8),
    ("s2_4", "carlos", "Usei SignalR para o status do pedido; atualiza sem recarregar.", 200),
    ("s2_5", "thiago", "Era o estado do carrinho sendo recriado ao voltar para a aba. Corrigido.", 240),
    ("sup_0", "rafael", "Afeta só a operadora X. Abri chamado com o provedor de SMS.", 10),
    ("sup_0", "mariana", "Enquanto isso, vamos liberar a verificação por e-mail para esses casos.", 8),
    ("sup_1", "lucas", "Investigando — parece a fila de notificações parada desde as 18h.", 5),
    ("site_1", "juliana", "Textos aprovados pela Mariana, falta só a foto de capa.", 30),
    ("mkt_1", "juliana", "Primeira versão no Drive. Feedbacks até sexta, por favor!", 20),
    ("mkt_1", "camila", "Ficou ótimo! Só aumentaria a logo no final.", 12),
    ("e_pay", "mariana", "Prioridade máxima da sprint: sem pagamento não tem lançamento.", 140),
]:
    emit(f'INSERT INTO "WorkItemComments" ("Id","WorkItemId","UserId","Content","CreatedAtUtc") VALUES '
         f"('{nid('comment')}','{ITEM[key]}',{U[who]},{q(text)},{ago(0, hours)});")

# ---------------------------------------------------------------- burndown (sprints 1, 2 e a ativa)
def burndown(sprint_key, start, end, total, remaining_by_day):
    for d in range(start, end + 1):
        idx = d - start
        if idx >= len(remaining_by_day):
            break
        emit(f'INSERT INTO "SprintBurndownSnapshots" ("Id","SprintId","SnapshotDateUtc","TotalCount","RemainingCount") VALUES '
             f"('{nid('snap')}','{SPRINT[sprint_key]}',{day(d)},{total},{remaining_by_day[idx]});")


burndown("s1", -34, -20, 8, [8, 8, 8, 7, 7, 6, 6, 5, 4, 4, 3, 2, 1, 1, 0])
burndown("s2", -20, -6, 7, [7, 7, 7, 6, 6, 6, 5, 4, 4, 3, 2, 2, 1, 0, 0])
# Ativa: de -6 até ontem; o SprintSnapshotHostedService grava o de hoje sozinho.
burndown("s3", -6, -1, 10, [10, 10, 9, 9, 8, 8])

# ---------------------------------------------------------------- documentos
DOC = {}


def doc(key, title, html, who, created, updated_by=None, updated=None):
    did = nid("doc")
    DOC[key] = did
    emit('INSERT INTO "DocPages" ("Id","WorkspaceId","Title","Type","ContentHtml","CreatedByUserId","CreatedAtUtc",'
         '"UpdatedByUserId","UpdatedAtUtc","HtmlRevision","Revision") VALUES '
         f"('{did}','{WS}',{q(title)},0,{q(html)},{U[who]},{ago(created)},"
         f"{U[updated_by] if updated_by else 'NULL'},{ago(updated) if updated is not None else 'NULL'},0,0);")


def sheet(key, title, grid, who, created, updated):
    import json
    did = nid("doc")
    DOC[key] = did
    emit('INSERT INTO "DocPages" ("Id","WorkspaceId","Title","Type","GridDataJson","CreatedByUserId","CreatedAtUtc",'
         '"UpdatedByUserId","UpdatedAtUtc","HtmlRevision","Revision") VALUES '
         f"('{did}','{WS}',{q(title)},1,{q(json.dumps(grid, ensure_ascii=False))},{U[who]},{ago(created)},"
         f"{U[who]},{ago(updated)},0,0);")


doc("onboarding", "Guia de onboarding do time", """<h1>Bem-vindo à Aurora Digital</h1>
<p>Este guia reúne o que você precisa nos primeiros dias. Dúvidas? Pergunte no chat do workspace.</p>
<h2>Primeira semana</h2>
<ol><li>Acesso ao repositório, ao Figma e ao ambiente de homologação.</li><li>Ler a <strong>Especificação do checkout</strong> e o <strong>Padrão de código</strong>.</li><li>Pegar uma tarefa marcada como <em>boa primeira tarefa</em> no backlog.</li></ol>
<h2>Rituais</h2>
<ul><li><strong>Daily</strong>: 9h30, 15 minutos.</li><li><strong>Planning</strong>: segunda-feira do início da sprint.</li><li><strong>Review e retrospectiva</strong>: sexta-feira do fim da sprint.</li></ul>
<h2>Contatos</h2>
<p>Produto: Mariana Costa · Tecnologia: Lucas Almeida · Design: Juliana Ferreira.</p>""", "mariana", 38, "lucas", 5)

doc("checkout", "Especificação do checkout", """<h1>Especificação do checkout</h1>
<p><strong>Objetivo:</strong> o cliente paga o pedido em até três toques, com Pix ou cartão.</p>
<h2>Fluxo</h2>
<ol><li>Revisar o carrinho e o endereço.</li><li>Escolher a forma de pagamento.</li><li>Confirmar e acompanhar o pedido.</li></ol>
<h2>Regras</h2>
<ul><li>O QR Code do Pix expira em 15 minutos; depois disso o pedido é cancelado e o cliente é avisado.</li><li>Cartão recusado mostra o motivo e oferece outra forma de pagamento.</li><li>O recibo vai por e-mail assim que o gateway confirma.</li></ul>
<h2>Fora do escopo desta sprint</h2>
<p>Parcelamento, carteira digital e cupons (Sprint 4).</p>""", "ana", 9, "ana", 1)

doc("retro2", "Retrospectiva — Sprint 2", """<h1>Retrospectiva da Sprint 2</h1>
<h2>O que foi bem</h2>
<ul><li>Entregamos 100% do compromisso.</li><li>O acompanhamento em tempo real impressionou na review.</li></ul>
<h2>O que pode melhorar</h2>
<ul><li>Bugs de mobile chegaram tarde ao QA.</li><li>Faltou tempo de revisão de código no fim da sprint.</li></ul>
<h2>Ações</h2>
<ol><li>QA participa do refinamento (responsável: Rafael).</li><li>Limite de 2 PRs abertos por pessoa (responsável: Lucas).</li></ol>""", "mariana", 6)

doc("padrao", "Padrão de código e revisão", """<h1>Padrão de código e revisão</h1>
<ul><li>Toda mudança entra por pull request ligado a uma tarefa do Nexus.</li><li>Pelo menos uma aprovação antes do merge.</li><li>Testes automatizados para regra de negócio nova.</li><li>Mensagens de commit no padrão <code>feat:</code>, <code>fix:</code>, <code>docs:</code>.</li></ul>""", "lucas", 30)

doc("reuniao", "Ata — reunião com restaurantes parceiros", """<h1>Reunião com restaurantes parceiros</h1>
<p><strong>Participantes:</strong> Mariana, Camila e 6 restaurantes do bairro.</p>
<h2>Principais pedidos</h2>
<ul><li>Painel simples para aceitar pedidos pelo celular.</li><li>Repasse semanal dos pagamentos.</li><li>Poder pausar o restaurante em horário de pico.</li></ul>
<h2>Próximos passos</h2>
<p>Camila monta a proposta comercial até sexta; Mariana leva os pedidos para o refinamento.</p>""", "camila", 3)

sheet("orcamento", "Orçamento do lançamento", [
    ["Item", "Responsável", "Previsto (R$)", "Realizado (R$)", "Situação"],
    ["Anúncios no Instagram", "Camila", "3.000", "1.250", "Em andamento"],
    ["Vídeo de lançamento", "Juliana", "1.500", "1.500", "Pago"],
    ["Contas de desenvolvedor (lojas)", "Thiago", "650", "650", "Pago"],
    ["Servidores (3 meses)", "Lucas", "900", "300", "Em andamento"],
    ["Brindes para restaurantes", "Mariana", "800", "0", "A fazer"],
    ["Total", "", "6.850", "3.700", ""],
], "camila", 14, 1)

sheet("capacidade", "Capacidade da Sprint 3", [
    ["Pessoa", "Dias disponíveis", "Pontos planejados", "Observação"],
    ["Ana Beatriz", "10", "16", ""],
    ["Lucas", "9", "11", "1 dia de evento"],
    ["Thiago", "10", "5", "Apoia o QA"],
    ["Carlos", "8", "3", "2 dias de folga"],
    ["Juliana", "10", "2", "Foco no vídeo do lançamento"],
    ["Rafael", "10", "8", ""],
], "mariana", 7, 6)

# ---------------------------------------------------------------- chat
chat = [
    ("mariana", "Bom dia, time! Planning da Sprint 3 às 10h. Pauta: pagamento por Pix e cartão.", 6 * 24 + 3, [], None),
    ("lucas", "Subi a branch do webhook do gateway. Quem puder revisar, agradeço!", 5 * 24, [], None),
    ("ana", "@Lucas Almeida reviso depois do almoço.", 5 * 24 - 1, ["lucas"], None),
    ("juliana", "Estados da tela de pagamento prontos no Figma 🎨", 4 * 24, [], None),
    ("thiago", "Tela de pagamento no Android já está na build de homologação.", 3 * 24 + 4, [], None),
    ("rafael", "Achei um bug: Pix expirado deixa o pedido pendente. Abri a tarefa na sprint.", 3 * 24, [], None),
    ("mariana", "Deixei a especificação do checkout atualizada com a regra de expiração.", 2 * 24 + 5, [], "checkout"),
    ("camila", "Orçamento do lançamento atualizado, já gastamos 54% do previsto.", 2 * 24, [], "orcamento"),
    ("carlos", "Alguém sabe se o gateway devolve o motivo da recusa do cartão?", 30, [], None),
    ("ana", "Devolve sim, no campo decline_code. Está na especificação.", 29, [], None),
    ("lucas", "Incidente: restaurantes não estão vendo pedidos novos. Estou olhando.", 6, [], None),
    ("lucas", "Era a fila de notificações parada. Reiniciei e já voltou. Vou colocar alerta.", 4, [], None),
    ("mariana", "@Pedro Rodrigues consegue validar o fluxo de pagamento amanhã antes da review?", 2, ["pedro"], None),
    ("rafael", "Testes de ponta a ponta do checkout começam amanhã cedo ✅", 1, [], None),
]
for who, text, hours, mentions, ref in chat:
    cid = nid("chat")
    emit(f'INSERT INTO "ChatMessages" ("Id","WorkspaceId","UserId","Content","CreatedAtUtc","ReferencedDocPageId") VALUES '
         f"('{cid}','{WS}',{U[who]},{q(text)},{ago(0, hours)},{q(DOC[ref]) if ref else 'NULL'});")
    for m in mentions:
        emit(f'INSERT INTO "ChatMessageMentions" ("Id","ChatMessageId","UserId") VALUES (\'{nid("mention")}\',\'{cid}\',{U[m]});')

# ---------------------------------------------------------------- notificações do Pedro
for type_, msg, item_key, hours, read in [
    (3, "Mariana Costa mencionou você no chat de Aurora Digital.", None, 2, False),
    (0, "Mariana Costa atribuiu \"Validar fluxo de pagamento\" a você.", "val", 3, False),
    (1, "\"Tratar pagamento recusado\" vence amanhã.", "s3_5", 10, True),
    (2, "Nova mensagem de Lucas Almeida em Aurora Digital.", None, 4, True),
]:
    if item_key == "val":
        item("val", "app", "Validar fluxo de pagamento", status="todo", prio=3, who="pedro", by="mariana",
             start=0, due=1, sprint="s3", epic="e_pay", points=1, plat="Android", created=0.2)
    emit(f'INSERT INTO "Notifications" ("Id","UserId","Type","Message","WorkItemId","WorkspaceId","IsRead","CreatedAtUtc","LinkUrl") VALUES '
         f"('{nid('notif')}',{PEDRO},{type_},{q(msg)},{q(ITEM[item_key]) if item_key else 'NULL'},'{WS}',{'true' if read else 'false'},{ago(0, hours)},NULL);")

emit("COMMIT;")
print("\n".join(out))
