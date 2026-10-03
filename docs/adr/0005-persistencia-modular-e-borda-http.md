# 0005 — Persistência modular no Postgres e borda HTTP do tracer bullet

- **Status:** Aceita
- **Data:** 2026-10-03
- **Emenda:** ADR 0004 (validação de entrada e sessão)

## Contexto

O tracer bullet do Marco 2 atravessa o sistema inteiro: criar conta, criar projeto, criar flag,
publicar, emitir a credencial e baixar o snapshot no jogo. Ele precisa fixar como os módulos de
`ToggleForge.md` (seção 5) viram código, como o Postgres guarda e protege cada invariante e como a borda
HTTP cumpre o contrato `docs/api/openapi.yaml`. A ADR 0001 adiou esses projetos até existir regra de
persistência; agora ela existe.

Alternativas consideradas:

1. **Corte dos projetos.**
   - **Um projeto por camada (`Domain`, `Application`, `Infrastructure`, `Api`), módulos como
     namespaces:** o compilador garante a regra da dependência entre camadas; quatro projetos. Contra: a
     fronteira entre módulos dentro da mesma camada não é vista pelo compilador.
   - **Um projeto por módulo e camada:** o compilador vê tudo. Contra: doze projetos para três módulos
     pequenos, a maioria com poucos arquivos.
   - **Vertical slices num projeto só:** menos cerimônia. Contra: o domínio passa a poder referenciar EF e
     ASP.NET Core, e só disciplina segura isso.
2. **Banco.**
   - **Um `DbContext` e um schema por módulo** (`access`, `authoring`, `delivery`), cada um com seu
     histórico de migrations: o dono de cada tabela é óbvio e nenhum módulo faz JOIN no outro. Contra:
     três migrations e nenhuma FK entre módulos.
   - **Um `DbContext` único:** uma migration só e FKs em todo lugar. Contra: qualquer módulo alcança
     qualquer tabela, o oposto da fronteira que o monólito modular promete.
3. **Invariantes de contagem e escrita concorrente no rascunho.**
   - **`SERIALIZABLE` com retry:** correto para qualquer invariante. Contra: retry da transação inteira
     em todo caso de uso e falhas `40001` difíceis de testar.
   - **Locks explícitos onde há disputa:** lock consultivo de transação para "no máximo N" e lock de
     linha no ambiente para quem escreve no rascunho, com `xmin` como row version. Contra: cada caso de
     uso precisa lembrar do lock certo.
   - **Só concorrência otimista com retry:** sem lock. Contra: o teste com quatro escritores simultâneos
     esgotou as tentativas e respondeu 500.
4. **Validação da entrada na borda.**
   - **`AddValidation` (ADR 0004):** atributos nos DTOs. Contra: o formato de erro dele não é o `errors[]`
     do contrato, e o resultado é um "válido/inválido" que joga fora o tipo convertido.
   - **Parse no endpoint:** cada campo vira o value object do domínio (`ProjectKey`, `DisplayName`,
     `RolloutPercentage`...) ou um erro em `errors[]`, todos de uma vez. Contra: código de parse escrito à
     mão em cada endpoint.

## Decisão

**Projetos por camada, módulos por namespace.** `Domain` só referencia `Evaluation`; `Application`
referencia `Domain`; `Infrastructure` referencia `Application`; `Api` é o composition root. Os módulos
Access, Authoring e Delivery aparecem como namespaces em cada camada. Um módulo só usa outro pela API
pública dele: `IMembershipDirectory` e `MemberAccess` (Access) e `IEnvironmentDirectory`,
`IPublishedSnapshots`, `EnvironmentLocator` e `SnapshotView` (Authoring). Testes de arquitetura cobrem o
que o compilador não vê.

**Um `DbContext` e um schema por módulo**, com snake_case (inclusive as tabelas do Identity) e
`application_name=toggleforge-<schema>` na conexão. Referências entre módulos são só ids, sem FK:
`authoring.publications.published_by` aponta para um membro de `access.users` sem constraint. Dentro do
módulo, toda relação é FK e as regras dos value objects se repetem em CHECK constraints.

**Concorrência por lock explícito:**

| Invariante | Proteção |
|---|---|
| Até 20 projetos por organização, chave única | `pg_advisory_xact_lock` na organização + índice único `(organization_id, key)` |
| Até 5 credenciais ativas por ambiente | `pg_advisory_xact_lock` no ambiente |
| Rascunho alterado por vários editores ao mesmo tempo | `select ... for no key update` na linha do ambiente antes de ler o rascunho; `xmin` como row version continua valendo |
| Publicação revisada sobre um rascunho que mudou | revisão revisada comparada sob o mesmo lock → 409 `draft-changed` |
| Rollback decidido sobre uma versão que já mudou | versão atual vista comparada sob o mesmo lock → 409 `current-version-changed` |

**Publicações imutáveis normalizadas:** `publications` + `publication_flags`, sem jsonb (AoP cap. 30). O
domínio não conhece essas linhas: o mapeamento passa por `PublicationRow`.

**Idempotência** (ADP cap. 26): `idempotency_records` com PK `(member_id, key)`, o SHA-256 do JSON
canônico do pedido (operação, organização, projeto, ambiente e o corpo) e a versão criada, gravados na
mesma transação da publicação. Publicar e reverter dividem a tabela: a operação no fingerprint impede que
uma chave sirva às duas. A repetição é conferida já com o ambiente travado, então duas requisições
simultâneas com a mesma chave geram uma publicação e a mesma resposta. A mesma chave em dois ambientes ao
mesmo tempo esbarra na PK; a perdedora lê o registro da outra e responde 409 `idempotency-key-reused`.

**Borda HTTP** (emenda a ADR 0004):

- Parse no endpoint, como na alternativa 4; `AddValidation` não é usado.
- Handlers devolvem `Results<..., JsonHttpResult<ProblemBody>>`. O `ProblemBody` é escrito à mão porque
  o writer padrão de Problem Details acrescenta `traceId`, e o schema do contrato não aceita campos
  extras.
- O JSON recusa campo desconhecido, propriedade repetida e número em string (`UnmappedMemberHandling`,
  `AllowDuplicateProperties`, `NumberHandling.Strict`).
- Content-Type errado, rota inexistente e método errado também respondem em Problem Details; o 415 do
  roteamento vira 400, como toda entrada inválida.
- A sessão confere o security stamp em toda requisição sem renovar o cookie. O `SecurityStampValidator`
  do Identity renovaria o cookie a cada conferência, com `Set-Cookie` e `Cache-Control: no-cache,
  no-store` em todas as respostas. A sessão dura 8 horas, sem sliding expiration.

**Migrations** com o `dotnet-ef` do manifesto local, uma pasta por módulo, aplicadas por
`dotnet run --project src/ToggleForge.Api -- --migrate`: um passo explícito do deploy, nunca a cada
boot de instância.

## Consequências

- Duas FKs que um banco único teria (membro da publicação, ambiente da credencial) não existem; a
  integridade entre módulos depende das portas. Apagar membros ou ambientes, quando existir, precisa
  tratar isso.
- Quem escreve no rascunho entra em fila por ambiente. Com 200 flags no máximo e edição humana, a fila é
  curta; se a medição mostrar disputa, a revisão começa por aqui.
- Os registros de idempotência crescem sem limpeza até existir o job de expiração de 24 horas que o
  contrato promete.
- `lastUsedAt` da credencial ainda não é atualizado; atualizar a cada download do SDK seria uma escrita
  por requisição e pede um desenho próprio (amostragem ou buffer).
- O teste de integração cria contas e organizações novas a cada caso, então os testes não dividem dados
  e dispensam limpar o banco (Respawn).

## Compliance

- `ArchitectureTests`: Access não depende de Authoring nem de Delivery; Authoring não depende de
  Delivery; Delivery só usa a API pública de Authoring; nenhum módulo da infraestrutura toca a
  persistência de outro.
- `LimitTests`: corridas pela última vaga de projeto e de credencial, pela mesma chave de projeto e por
  flags criadas ao mesmo tempo.
- `PublishingTests` e `RollbackTests`: repetição, reuso da chave com outro corpo ou noutra operação, a
  mesma chave por outro membro, retries simultâneos, revisão ou versão desatualizada, "nada a publicar" e
  "já em vigor".
- Toda resposta dos testes da API é conferida contra `docs/api/openapi.yaml` (status declarado, corpo e
  headers). `DefaultDenyTests` varre as rotas registradas: cada uma é uma operação do contrato, exige
  autenticação fora da allowlist e, se for mutação, exige o token antiforgery.
