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
```

## Layout

- `src/ToggleForge.Evaluation`: núcleo de avaliação. netstandard2.1 com C# 9 (compatível com Unity),
  sem `PackageReference` nem `ProjectReference` (ADR 0001).
- `tests/`: um projeto de teste por projeto de produção; asserts do próprio xUnit.
- `samples/`: demonstrações executáveis.
- `tools/rollout-vectors`: oráculo Python do algoritmo de rollout (ADR 0002).

## Referências de design

Prioridade: **Adaptive Code 2e** no dia a dia; **Dive Into Design Patterns** quando surgir um problema
de design; **Clean Architecture with .NET** (Dino Esposito) nas decisões de arquitetura.

Design da API HTTP (Marco 2 em diante): **The Design of Web APIs, 2ª ed.**, caps. 2–7 e 12.

- Antes de codar uma feature da API, preencha o API Capabilities Canvas em `docs/api/<feature>.md`:
  usuário, caso de uso, passos, entradas, sucesso, falha e operação, sem HTTP (caps. 2–3).
- Recursos, ações, status e modelos derivados do modelo completo (caps. 4–5).
- Spec-first: o contrato OpenAPI 3.1 é escrito e revisado antes da implementação (caps. 6–7).
- Segurança no design: superfície mínima, nada sensível na URL, `ETag`/`If-Match` e
  `Idempotency-Key`, e "quem vê o quê" descrito em cada operação (cap. 12).

Padrões iniciais: Strategy, Adapter e Decorator, aplicados onde houver variação prevista ou borda real:

- Strategy: `ITargetingRule` (rollout percentual hoje; segmentação no Marco 5).
- Decorator: em volta de `IFlagEvaluator` (contagem na demo; logs e métricas no Marco 2).
- Adapter: persistência (Marco 2) e transporte do SDK (Marco 4).

## Regras específicas

- O algoritmo de rollout é contrato público. Qualquer mudança exige uma versão nova (ADR 0002) e
  vetores novos; nunca edite `rollout-v1.json` à mão.
- A avaliação nunca lança exceção por causa de dado ruim: devolve o `defaultValue` com um `Reason`.
- Frontend (Marco 3): Blazor + Tailwind (ADR 0003).
