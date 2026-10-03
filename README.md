# ToggleForge

[![CI](https://github.com/BipoXDXD/ToggleForge/actions/workflows/ci.yml/badge.svg)](https://github.com/BipoXDXD/ToggleForge/actions/workflows/ci.yml)

Feature flags e configuração remota para estúdios de jogos em Unity e C#. Crie uma flag, simule
jogadores, publique para 10%, amplie a distribuição e reverta se necessário. O jogo já tem a
funcionalidade; o ToggleForge controla quando e para quem ela fica ativa.

O plano de produto está em [`ToggleForge.md`](ToggleForge.md), e as decisões de arquitetura em
[`docs/adr/`](docs/adr/).

## Status

| Marco | Entrega | Situação |
|---|---|---|
| 1 — Núcleo | Avaliação booleana, contexto, rollout percentual e fallback | ✅ Concluído |
| 2 — API persistente | Rascunho, publicação, rollback, autenticação e isolamento | Próximo |
| 3 — Console | Editor, simulador, publicação e histórico (Blazor + Tailwind) | Planejado |
| 4 — SDK e configurações | Sincronização, cache, JSON e app demonstrativo | Planejado |
| 5 — Unity e segmentação | Adaptador, jogo simples e regras por atributos | Planejado |

## Como funciona o rollout

Cada jogador cai num bucket de 0 a 9999, calculado com SHA-256 sobre projeto, ambiente, flag, seed e ID
do jogador. Um rollout de `p` basis points inclui quem tem `bucket < p`. Por isso:

- o mesmo jogador recebe sempre o mesmo resultado;
- ampliar de 10% para 20% nunca remove quem já estava incluído;
- qualquer linguagem consegue reproduzir o cálculo: a especificação está em
  [`docs/rollout-algorithm.md`](docs/rollout-algorithm.md) e os vetores de referência são gerados por
  um oráculo Python independente.

Quando falta configuração, a chave é inválida ou o rollout parcial não recebe o ID do jogador, a
avaliação devolve o valor padrão informado pelo jogo junto com o motivo. Ela nunca lança exceção por
causa de dado ruim.

```csharp
var evaluator = new FlagEvaluator();
var context = EvaluationContext.ForPlayer(PlayerId.Parse("player-42"));

var result = evaluator.EvaluateBoolean(snapshot, "new-shop", context, defaultValue: false);
// result.Value, result.Reason (Included, Excluded, Disabled, FlagNotFound...), result.SnapshotVersion
```

## Demonstração

```bash
dotnet run --project samples/ToggleForge.RolloutDemo
```

```
Versão  Rollout  Incluídos  Taxa real  Perdidos desde a versão anterior
v1      10%          9.954      9,95%  —
v2      20%         19.954     19,95%  0
v3      50%         50.250     50,25%  0
```

## Desenvolvimento

Requisitos: .NET SDK 10.0.400 e Python 3.9+ (só para o oráculo de vetores).

```bash
dotnet build ToggleForge.slnx -c Release
dotnet test --solution ToggleForge.slnx -c Release
dotnet format ToggleForge.slnx --verify-no-changes
python3 tools/rollout-vectors/generate.py --check
```

| Pasta | Conteúdo |
|---|---|
| `src/ToggleForge.Evaluation` | Núcleo de avaliação: netstandard2.1, C# 9, sem dependências (compatível com Unity) |
| `tests/ToggleForge.Evaluation.Tests` | xUnit v3 no Microsoft.Testing.Platform, com vetores de referência |
| `samples/ToggleForge.RolloutDemo` | Simulação de rollout em etapas |
| `tools/rollout-vectors` | Implementação de referência do algoritmo em Python |
| `docs/` | ADRs e especificação do rollout |

O CI roda formatação, build com avisos tratados como erro, testes, checagem dos vetores, smoke test da
demo e scan de segredos (gitleaks).
