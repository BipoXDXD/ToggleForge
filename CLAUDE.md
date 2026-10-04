# ToggleForge — regras do projeto

Feature flags e configuração remota para estúdios de jogos (Unity/C#). O plano de produto está em
`ToggleForge.md`; as decisões, em `docs/adr/`. Nada aqui é imutável: mudou a regra, registre o motivo
(ADR nova ou atualização do plano) e ajuste os testes afetados.

## Comandos

```bash
dotnet build ToggleForge.slnx -c Release               # avisos são erros
dotnet test --solution ToggleForge.slnx -c Release     # xUnit v3 no Microsoft.Testing.Platform
dotnet format ToggleForge.slnx --verify-no-changes
python3 tools/rollout-vectors/generate.py              # regenera os vetores do rollout
python3 tools/rollout-vectors/generate.py --check      # confere o JSON commitado (roda no CI)
dotnet run --project samples/ToggleForge.RolloutDemo
dotnet run --project samples/ToggleForge.SdkDemo              # roteiro do SDK; --live usa TOGGLEFORGE_API_URL/CREDENTIAL
npm ci --prefix tools/api-lint && npm run lint --prefix tools/api-lint   # Spectral + OWASP na spec
dotnet run --project src/ToggleForge.Api -- --migrate  # aplica as migrations; precisa de ConnectionStrings:ToggleForge
dotnet run --project src/ToggleForge.Api -- --purge-idempotency-records  # job agendado: esquece chaves com mais de 24 h
dotnet run --project src/ToggleForge.Api               # sobe a API
dotnet tool restore && dotnet ef migrations add <Nome> --context <Módulo>DbContext \
  --project src/ToggleForge.Infrastructure --startup-project src/ToggleForge.Api --output-dir <Módulo>/Migrations
```

Os testes da API sobem um Postgres com Testcontainers: o Docker precisa estar rodando. O build do console
roda `npm ci` e o Tailwind, então o Node também precisa estar instalado. A connection string local vai em
User Secrets (`dotnet user-secrets set ConnectionStrings:ToggleForge "..." --project src/ToggleForge.Api`),
nunca no repositório.

## Layout

- `src/ToggleForge.Evaluation`: núcleo de avaliação. netstandard2.1 com C# 9 (compatível com Unity),
  sem `PackageReference` nem `ProjectReference` (ADR 0001).
- `src/ToggleForge.Sdk`: o SDK do jogo (Marco 4), netstandard2.1 com C# 9. Só referencia o `Evaluation` e o
  Newtonsoft.Json, lido à mão, sem reflection, e nenhum tipo do Newtonsoft na API pública (ADR 0010).
- `src/ToggleForge.Console`: o console do Marco 3, Blazor WebAssembly servido pela API na mesma origem
  (ADR 0009). Não referencia nenhum projeto de `src/`: fala com a API só por HTTP, com records próprios.
  O CSS sai do Tailwind via npm no build (ADR 0003); `wwwroot/css/` é gerado.
- `src/ToggleForge.Domain`, `Application`, `Infrastructure` e `Api`: a API do Marco 2, em camadas, com os
  módulos Access, Authoring e Delivery como namespaces; cada módulo tem o próprio `DbContext` e schema
  (ADR 0005). Migrations em `Infrastructure/<Módulo>/Migrations`, geradas e nunca editadas à mão.
- `tests/`: asserts do próprio xUnit.
  - `Evaluation.Tests` e `Domain.Tests`: regras de negócio, sem banco nem mock.
  - `Api.Tests`: integração ponta a ponta com Postgres real, cobrindo `Application`, `Infrastructure` e
    `Api`. Toda resposta é conferida contra `docs/api/openapi.yaml`, e os testes de arquitetura guardam as
    fronteiras entre módulos. Cada teste cria as próprias contas e organizações, sem dados compartilhados.
    As jornadas do console (`ConsoleJourneyTests`) rodam num Chromium headless pelo Playwright, contra a
    API num Kestrel em HTTPS (`BrowserHost`); o Chromium é baixado na primeira execução.
  - `Console.Tests`: componentes e cliente da API do console com bUnit, contra uma API em stub.
  - `Sdk.Tests`: leitura do snapshot, ordem das versões e transporte do SDK, com transporte em stub; a
    integração do SDK com a API real fica em `Api.Tests` (`SdkClientTests`).
- `samples/`: demonstrações executáveis.
- `tools/rollout-vectors`: oráculo Python do algoritmo de rollout (ADR 0002).
- `docs/api`: canvas de capacidades, ameaças e o contrato `openapi.yaml` (ADR 0004).
- `tools/api-lint`: Spectral com o ruleset OWASP; cada regra desligada tem a justificativa ao lado.

## Referências de design

Prioridade: **Adaptive Code 2e** no dia a dia; **Dive Into Design Patterns** quando surgir um problema
de design; **Clean Architecture with .NET** (Dino Esposito) nas decisões de arquitetura.

Arquitetura e domínio:

- **Fundamentals of Software Architecture 2e** (FSA), caps. 2–11, 14–15 e 18–23:
  - pensamento arquitetural, modularidade, características (definir, medir, governar com fitness
    functions) e componentes (caps. 2–9);
  - estilos layered, modular monolith, service-based, event-driven e microservices, e como escolher
    (caps. 10–11, 14–15, 18–19);
  - padrões, ADRs, análise de risco e diagramas (caps. 20–23).
- **Learning Domain-Driven Design** (LDDD), caps. 1–15:
  - subdomínios, linguagem ubíqua e bounded contexts (caps. 1–4);
  - transaction script ou active record para lógica simples, domain model para a complexa, e o tempo
    como dimensão (caps. 5–7);
  - padrões de arquitetura e comunicação, heurísticas, evolução, EventStorming, microservices e EDA
    (caps. 8–15).
- **The Art of PostgreSQL** (AoP), caps. 4, 7–8, 13–18, 22–23, 26–28, 30–32, 35–39, 43 e 45:
  - lógica de negócio em SQL, SQL é código, estratégia de índices (caps. 4, 7–8);
  - consultas: SELECT, ORDER BY sem OFFSET, GROUP BY, NULL, window functions, joins (caps. 13–18);
  - tipos e tipos desnormalizados (caps. 22–23);
  - ORM, modelagem, normalização, anti-patterns, desnormalização e NoSQL (caps. 26–28, 30–32);
  - DML, isolamento e locks, cache em SQL, triggers, LISTEN/NOTIFY, auditoria e trigramas
    (caps. 35–39, 43, 45).
- **Designing Data-Intensive Applications, 2ª ed.** (DDIA), caps. 1–5, 8–10, 12 e 13: os porquês por
  trás do banco e do SDK.
  - Trade-offs, requisitos não funcionais, modelos de dados e armazenamento (caps. 1–4): um Postgres só
    até um problema medido pedir mais.
  - Codificação e evolução (cap. 5): o snapshot e o contrato mudam de forma compatível, porque builds
    antigos do jogo continuam no ar.
  - Transações e isolamento (cap. 8): cada invariante sob disputa diz qual proteção usa (constraint, lock
    ou isolamento) e tem teste com escritores concorrentes.
  - Falhas de rede, relógios e ordenação (caps. 9–10): timeout, retry com idempotência, instantes em UTC
    e ordem pela versão da publicação, nunca pelo relógio.
  - Streams e integridade de ponta a ponta (caps. 12–13): outbox e CDC/LISTEN para propagar publicações,
    idempotência de ponta a ponta e dado derivado reconstruível.
  - Replicação, sharding e batch (caps. 6–7 e 11) ficam de fora enquanto o sistema roda num nó só.
- **Code That Fits in Your Head** (CFH), caps. 2–8, 10–13 e 15:
  - checklists, complexidade, vertical slice, encapsulamento, triangulação, decomposição e design de
    API (caps. 2–8);
  - evoluir código existente, editar testes, troubleshooting, separação de responsabilidades e os
    suspeitos de sempre: performance e segurança (caps. 10–13, 15).

Qualidade de código (base para código bem produzido e com poucos erros):

- **Code Complete 2e**, caps. 5–8 (design, classes, rotinas, programação defensiva), 11 (nomes),
  22–23 (testes do desenvolvedor, depuração) e 32 (código autodocumentado).
- **Refactoring 2e**, caps. 1–4 (smells, testes) e 6–8, 10–11 (catálogo). Nomeie o smell e o
  refactoring pelo nome em inglês; refactoring e mudança de comportamento em commits separados.
- **The Pragmatic Programmer 2e**, caps. 2 (ETC, DRY, ortogonalidade), 4 (contratos, dead programs,
  asserções), 5 (desacoplamento, Tell Don't Ask), 6 (concorrência), 7 (coincidência, testes,
  property-based, nomes) e 9.
- **Unit Testing: Principles, Practices, and Patterns** (Khorikov) é a referência de testes:
  - teste comportamento observável, nunca detalhe de implementação; refactoring não quebra teste;
  - regra de negócio no domínio, testada sem mock (estilo output/state);
  - controllers e serviços de aplicação finos, cobertos por testes de integração do caminho feliz
    mais longo e dos casos de borda que o unit test não alcança;
  - o Postgres é dependência gerenciada: real nos testes (Testcontainers), nunca mock de
    repositório; mock só para dependência não gerenciada (e-mail, API de terceiro), e o relógio entra
    como `TimeProvider`.
- Todo teste novo é visto falhando (ou sabotando o código) antes de contar; código crítico passa por
  mutation testing. O Stryker.NET ainda não suporta o Microsoft.Testing.Platform, então a varredura
  é feita com mutações manuais.

Design da API HTTP (Marco 2 em diante), três livros lidos juntos:

- **The Design of Web APIs, 2ª ed.** (DWA), caps. 2–7 e 12: o processo.
  - Antes de codar uma feature da API, preencha o API Capabilities Canvas em `docs/api/<feature>.md`:
    usuário, caso de uso, passos, entradas, sucesso, falha e operação, sem HTTP (caps. 2–3).
  - Recursos, ações, status e modelos derivados do modelo completo (caps. 4–5).
  - Spec-first: o contrato OpenAPI 3.1 é escrito e revisado antes da implementação (caps. 6–7).
  - Segurança no design: superfície mínima, nada sensível na URL, `ETag`/`If-Match` e
    `Idempotency-Key`, e "quem vê o quê" descrito em cada operação (cap. 12).
- **API Design Patterns** (ADP), caps. 3–9, 21–22, 24 e 26–29: os padrões de cada operação.
  - Nomes, escopo e hierarquia de recursos, tipos e defaults, IDs (caps. 3–6).
  - Métodos padrão, atualização parcial com field mask e métodos customizados como
    `:publish` e `:rollback` (caps. 7–9).
  - Paginação por token opaco com teto de página e filtros com allowlist (caps. 21–22).
  - Política de compatibilidade e versionamento (cap. 24).
  - Deduplicação por `Idempotency-Key`, validação com `validateOnly`, revisões de recurso e
    retry seguro (caps. 26–29). As publicações imutáveis do ToggleForge são as revisões do cap. 28.
- **Secure APIs** (SA), caps. 2–9, 11 e 12: as ameaças e a prova de que foram mitigadas.
  - Modelagem de ameaças e princípios (caps. 2–3): cada feature com dado de outro tenant, credencial
    ou permissão registra ameaça → mitigação → teste.
  - OWASP API Top 10, com ênfase em BOLA, BFLA, BOPLA e consumo de recursos (caps. 4–5).
  - Segurança no design, autenticação e autorização (caps. 6–8), infraestrutura (cap. 9),
    observabilidade (cap. 11) e testes de segurança (cap. 12).
  - Os capítulos conceituais vencem os exemplos do próprio livro: o login do cap. 8 omite PKCE e
    `state`, e o cap. 11 loga headers inteiros. Siga o cap. 7 e nunca logue `Authorization` nem `Cookie`.

Implantação:

- **Kubernetes in Action, 2ª ed.**, caps. 1–9, 11, 13–15 e 18: a referência de Kubernetes. O plano
  começa em containers no Azure Container Apps, e o Kubernetes é uma etapa própria depois
  (`ToggleForge.md`, seção 13). Os capítulos que valem desde já estão marcados abaixo.
  - Containers, API e modelo de objetos (caps. 1–4).
  - Pods, ciclo de vida e health probes, namespaces e labels (caps. 5–7). Desde já: liveness não
    depende do banco, readiness sim, e o SIGTERM encerra as requisições em andamento antes de sair.
  - ConfigMaps, Secrets e volumes (caps. 8–9): configuração e segredos por variável de ambiente ou
    arquivo montado, nunca na imagem.
  - Services e Gateway API (caps. 11 e 13). O Ingress (cap. 12) fica de fora: a API dele está
    congelada e o Gateway API a substitui.
  - ReplicaSets e Deployments com rolling update (caps. 14–15).
  - Jobs e CronJobs (cap. 18): as migrations rodam num Job antes do rollout, e a limpeza dos
    registros de idempotência roda num CronJob.
  - PersistentVolumes, StatefulSets e DaemonSets (caps. 10, 16 e 17) ficam de fora: o Postgres é
    gerenciado, fora do cluster.

Padrões iniciais: Strategy, Adapter e Decorator, aplicados onde houver variação prevista ou borda real:

- Strategy: `ITargetingRule` (rollout percentual hoje; segmentação no Marco 5).
- Decorator: em volta de `IFlagEvaluator` (contagem na demo; logs e métricas no Marco 2).
- Adapter: persistência (Marco 2) e transporte do SDK (Marco 4).

## Regras específicas

- Comentário só quando agrega: o porquê, o contrato (o que exige, garante, quando lança), unidades,
  premissas e o que o código não consegue dizer. Nada que repita o nome ou o código; nome melhor
  vence comentário.
- O algoritmo de rollout é contrato público. Qualquer mudança exige uma versão nova (ADR 0002) e
  vetores novos; nunca edite `rollout-v1.json` à mão.
- A avaliação nunca lança exceção por causa de dado ruim: devolve o `defaultValue` com um `Reason`.
- Frontend (Marco 3): Blazor + Tailwind (ADR 0003).
