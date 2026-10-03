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
npm ci --prefix tools/api-lint && npm run lint --prefix tools/api-lint   # Spectral + OWASP na spec
```

## Layout

- `src/ToggleForge.Evaluation`: núcleo de avaliação. netstandard2.1 com C# 9 (compatível com Unity),
  sem `PackageReference` nem `ProjectReference` (ADR 0001).
- `tests/`: um projeto de teste por projeto de produção; asserts do próprio xUnit.
- `samples/`: demonstrações executáveis.
- `tools/rollout-vectors`: oráculo Python do algoritmo de rollout (ADR 0002).
- `docs/api`: canvas de capacidades, ameaças e o contrato `openapi.yaml` (ADR 0004).
- `tools/api-lint`: Spectral com o ruleset OWASP; cada regra desligada tem a justificativa ao lado.

## Referências de design

Prioridade: **Adaptive Code 2e** no dia a dia; **Dive Into Design Patterns** quando surgir um problema
de design; **Clean Architecture with .NET** (Dino Esposito) nas decisões de arquitetura.

Qualidade de código (base para código bem produzido e com poucos erros):

- **Code Complete 2e**, caps. 5–8 (design, classes, rotinas, programação defensiva), 11 (nomes),
  22–23 (testes do desenvolvedor, depuração) e 32 (código autodocumentado).
- **Refactoring 2e**, caps. 1–4 (smells, testes) e 6–8, 10–11 (catálogo). Nomeie o smell e o
  refactoring pelo nome em inglês; refactoring e mudança de comportamento em commits separados.
- **The Pragmatic Programmer 2e**, caps. 2 (ETC, DRY, ortogonalidade), 4 (contratos, dead programs,
  asserções), 5 (desacoplamento, Tell Don't Ask), 6 (concorrência), 7 (coincidência, testes,
  property-based, nomes) e 9.
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
