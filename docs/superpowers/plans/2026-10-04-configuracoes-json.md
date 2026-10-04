# Configurações JSON — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Flags do tipo JSON, do núcleo de avaliação ao console, seguindo a ADR 0011.

**Architecture:** As fatias 2 a 7 da ADR 0011 viram uma branch e um PR cada. Este documento detalha, tarefa
por tarefa, as fatias 2 (SDK tolerante) e 3 (núcleo). As fatias 4 a 7 aparecem como mapa, com arquivos,
interfaces e testes; cada uma ganha a sua seção detalhada aqui no começo dela, escrita sobre o código que as
anteriores deixarem.

**Tech Stack:** .NET 10 (API, console, testes), netstandard2.1 com C# 9 (`ToggleForge.Evaluation` e
`ToggleForge.Sdk`), xUnit v3 no Microsoft.Testing.Platform, Newtonsoft.Json no SDK, System.Text.Json no
domínio, EF Core + PostgreSQL 18, Blazor WebAssembly + Tailwind, Playwright.

**Spec:** `docs/adr/0011-configuracoes-json.md` e `docs/api/configuracoes-json.md`.

## Global Constraints

- `ToggleForge.Evaluation` e `ToggleForge.Sdk`: netstandard2.1, C# 9. Nada de namespace de arquivo, record,
  `required` nem collection expression nesses projetos; namespaces com chaves, como o código existente.
- `ToggleForge.Evaluation` não tem `PackageReference` nem `ProjectReference` (ADR 0001).
- Nenhum tipo do Newtonsoft na API pública do SDK (ADR 0010, `SdkBoundaryTests`).
- A avaliação nunca lança exceção por dado ruim: devolve o `defaultValue` com um `EvaluationReason`.
- Motivos novos entram no **fim** de `EvaluationReason`, para não renumerar os existentes.
- O algoritmo de rollout não muda: `rollout-v1.json` não é tocado, e o tipo da flag não entra no hash.
- Limites: 4 KiB por valor JSON (UTF-8, compactado), 8 níveis, 128 KiB por ambiente, 32 KiB por corpo de
  rota de flag.
- `TreatWarningsAsErrors` com `AnalysisLevel` `latest-recommended`; `dotnet format` sem mudanças.
- Comentário só com o porquê, o contrato, unidades ou premissas (CLAUDE.md do projeto).
- Refactoring e mudança de comportamento em commits separados; mensagens de commit em inglês, com
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` no fim.
- Fluxo por fatia: branch a partir do `main` atualizado, testes vistos falhando, sabotagem manual de cada regra
  nova, revisão Opus só de leitura, PR com resumo/decisões/testes/checklist, CI verde, merge só com a palavra
  do usuário.

## Review Focus

1. Um valor JSON `null` (válido) configurado na flag: o jogo recebe o que o parser devolver para `"null"`, não o
   `defaultValue`. Teste na Tarefa 3.
2. Um parser que falha com qualquer exceção, não só `FormatException`: sempre `InvalidValue`. Teste na Tarefa 3.
3. Caminhos de fallback (sem snapshot, chave inválida, flag ausente, tipo errado, jogador sem ID) nunca chamam
   o parser, que pode ser caro ou ter efeito colateral. Testes nas Tarefas 3 e 4.
4. Uma flag de tipo desconhecido com chave inválida ou repetida: recusa o snapshot, porque a chave é comum a
   todos os tipos. Teste na Tarefa 1.
5. `"type"` com valor que não é texto (`null`, número, objeto) recusa o snapshot; um tipo com outra caixa
   (`"Boolean"`) é desconhecido e é ignorado, não lido como booleano. Testes na Tarefa 1.

---

## Fatia 2 — SDK tolerante a tipos de flag desconhecidos

Branch: `fix/sdk-skip-unknown-flag-types`.

### Tarefa 1: o leitor ignora flag de tipo desconhecido

**Files:**
- Modify: `src/ToggleForge.Sdk/SnapshotReader.cs` (`ReadFlags`, `ReadFlag`, novo `KindOf` e enum `FlagKind`)
- Modify: `docs/adr/0010-sdk-sincronizacao-e-cache.md` (seção "Leitura do snapshot")
- Test: `tests/ToggleForge.Sdk.Tests/SnapshotReaderTests.cs`

**Interfaces:**
- Consumes: nada novo.
- Produces: `SnapshotReader.Read(string)` com o mesmo contrato público, agora aceitando `"type"` em cada flag:
  ausente ou `"boolean"` → booleana; outro texto → ignorada; não texto → snapshot recusado. A Fatia 5 acrescenta
  o caso `"json"` em `KindOf`.

- [ ] **Step 1: Escrever os testes que falham**

Acrescente ao fim de `SnapshotReaderTests` (antes da chave que fecha a classe):

```csharp
    // Um tipo que nenhum SDK conhece hoje, com campos que uma flag booleana recusaria.
    private const string NewerTypeFlag =
        """{"key":"shop-color","type":"color","enabled":true,"onValue":"#ff0000","offValue":{"r":0},"seed":"s2"}""";

    private const string BooleanFlagJson =
        """{"key":"new-shop","enabled":true,"onValue":true,"offValue":false,"rolloutBasisPoints":10000,"seed":"s1"}""";

    [Fact]
    public void A_flag_of_an_unknown_type_is_skipped_and_the_rest_still_applies()
    {
        var snapshot = SnapshotReader.Read(Snapshots.Json(flags: $"[{NewerTypeFlag},{BooleanFlagJson}]"));

        Assert.NotNull(snapshot);
        Assert.True(snapshot.TryGetFlag(FlagKey.Parse("new-shop"), out _));
        Assert.False(snapshot.TryGetFlag(FlagKey.Parse("shop-color"), out _));
    }

    [Fact]
    public void A_flag_may_state_that_it_is_boolean()
    {
        var typed = BooleanFlagJson.Replace("\"enabled\"", "\"type\":\"boolean\",\"enabled\"", StringComparison.Ordinal);

        var snapshot = SnapshotReader.Read(Snapshots.Json(flags: $"[{typed}]"));

        Assert.NotNull(snapshot);
        Assert.True(snapshot.TryGetFlag(FlagKey.Parse("new-shop"), out _));
    }

    [Fact]
    public void A_type_is_matched_exactly_so_another_casing_is_an_unknown_type()
    {
        var shouting = BooleanFlagJson.Replace("\"enabled\"", "\"type\":\"Boolean\",\"enabled\"", StringComparison.Ordinal);

        var snapshot = SnapshotReader.Read(Snapshots.Json(flags: $"[{shouting}]"));

        Assert.NotNull(snapshot);
        Assert.False(snapshot.TryGetFlag(FlagKey.Parse("new-shop"), out _));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("7")]
    [InlineData("true")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void A_type_that_is_not_text_refuses_the_snapshot(string type)
    {
        var flag = BooleanFlagJson.Replace("\"enabled\"", $"\"type\":{type},\"enabled\"", StringComparison.Ordinal);

        Assert.Null(SnapshotReader.Read(Snapshots.Json(flags: $"[{flag}]")));
    }

    [Fact]
    public void A_key_repeated_by_a_flag_of_an_unknown_type_refuses_the_snapshot()
    {
        var clash = NewerTypeFlag.Replace("shop-color", "new-shop", StringComparison.Ordinal);

        Assert.Null(SnapshotReader.Read(Snapshots.Json(flags: $"[{clash},{BooleanFlagJson}]")));
    }

    [Theory]
    [InlineData("\"key\":\"Shop Color\",")]
    [InlineData("\"key\":7,")]
    [InlineData("")]
    public void A_flag_of_an_unknown_type_still_needs_a_valid_key(string key)
    {
        var flag = NewerTypeFlag.Replace("\"key\":\"shop-color\",", key, StringComparison.Ordinal);

        Assert.Null(SnapshotReader.Read(Snapshots.Json(flags: $"[{flag},{BooleanFlagJson}]")));
    }
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test --project tests/ToggleForge.Sdk.Tests -c Release --filter-class "*SnapshotReaderTests"`
Expected: FAIL em `A_flag_of_an_unknown_type_is_skipped_and_the_rest_still_applies` (o snapshot volta nulo,
porque `onValue` texto invalida a flag) e em `A_type_is_matched_exactly_so_another_casing_is_an_unknown_type`
(hoje `type` é ignorado e a flag é lida). Os demais passam já hoje, por motivos diferentes; eles protegem a
implementação nova.

- [ ] **Step 3: Implementar**

Em `SnapshotReader.cs`, substitua o comentário da classe, `ReadFlags` e `ReadFlag`, e acrescente `KindOf`,
`BooleanType` e `FlagKind`:

```csharp
    /// <summary>
    /// Lê o JSON de <c>GET /sdk/v1/snapshot</c> à mão, sem desserialização por reflection, que o stripping do
    /// IL2CPP quebraria (ADR 0010). Campo desconhecido é ignorado, e flag de um tipo que este SDK não conhece
    /// também (ADR 0011), porque builds antigos do jogo continuam no ar quando o contrato cresce; qualquer campo
    /// usado que esteja errado recusa o snapshot inteiro.
    /// </summary>
    internal static class SnapshotReader
    {
        internal const string Algorithm = "toggleforge.rollout.v1";

        private const string BooleanType = "boolean";
```

```csharp
        /// <returns>
        /// Nulo se alguma flag for inválida ou se uma chave se repetir, mesmo numa flag de tipo que este SDK não
        /// conhece: a chave é comum a todos os tipos.
        /// </returns>
        private static List<BooleanFlag>? ReadFlags(JArray items)
        {
            var flags = new List<BooleanFlag>(items.Count);
            var keys = new HashSet<FlagKey>();
            foreach (var item in items)
            {
                if (item is not JObject entry || !FlagKey.TryParse(Text(entry, "key"), out var key) || !keys.Add(key))
                {
                    return null;
                }

                switch (KindOf(entry))
                {
                    case FlagKind.Boolean when ReadBooleanFlag(key, entry) is { } flag:
                        flags.Add(flag);
                        break;
                    case FlagKind.Newer:
                        // Publicada depois deste SDK: só ela fica de fora, e o jogo recebe o próprio valor padrão
                        // para ela.
                        break;
                    default:
                        return null;
                }
            }

            return flags;
        }

        /// <remarks>Sem <c>type</c> a flag é booleana: os snapshots anteriores à ADR 0011 não o tinham.</remarks>
        private static FlagKind KindOf(JObject flag) => flag["type"] switch
        {
            null => FlagKind.Boolean,
            JValue { Type: JTokenType.String, Value: BooleanType } => FlagKind.Boolean,
            JValue { Type: JTokenType.String } => FlagKind.Newer,
            _ => FlagKind.Malformed,
        };

        private static BooleanFlag? ReadBooleanFlag(FlagKey key, JObject flag)
        {
            if (Boolean(flag, "enabled") is not { } enabled
                || Boolean(flag, "onValue") is not { } onValue
                || Boolean(flag, "offValue") is not { } offValue
                || Integer(flag, "rolloutBasisPoints") is not { } basisPoints
                || basisPoints is < int.MinValue or > int.MaxValue
                || !RolloutPercentage.TryFromBasisPoints((int)basisPoints, out var percentage)
                || !RolloutSeed.TryParse(Text(flag, "seed"), out var seed))
            {
                return null;
            }

            return new BooleanFlag(key, enabled, onValue, offValue, new PercentageRollout(percentage, seed));
        }
```

E, antes da chave que fecha a classe:

```csharp
        private enum FlagKind
        {
            Boolean,

            /// <summary>Um tipo que este SDK não conhece, publicado depois dele.</summary>
            Newer,

            Malformed,
        }
```

Atenção: `flag["type"]` é `null` em C# só quando o campo falta; `"type": null` chega como `JValue` do tipo
`JTokenType.Null` e cai em `Malformed`.

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test --project tests/ToggleForge.Sdk.Tests -c Release`
Expected: PASS em todos, inclusive os antigos de `SnapshotReaderTests` e `ToggleForgeClientTests`.

- [ ] **Step 5: Anotar a ADR 0010**

Em `docs/adr/0010-sdk-sincronizacao-e-cache.md`, no item "**O snapshot inteiro é recusado** se o `algorithm`
for desconhecido, ...", acrescente ao fim do parágrafo:

```markdown
  Uma flag de um tipo que este SDK não conhece não é inválida: ela é ignorada, e o resto do snapshot vale
  (ADR 0011).
```

- [ ] **Step 6: Sabotar e conferir**

Cada mutação abaixo, aplicada sozinha, precisa derrubar pelo menos um teste novo; desfaça depois de cada uma:
- `case FlagKind.Newer:` → `case FlagKind.Newer: return null;` (cai `A_flag_of_an_unknown_type_is_skipped...`);
- `JValue { Type: JTokenType.String } => FlagKind.Newer` → `=> FlagKind.Boolean` (cai o de caixa diferente);
- `_ => FlagKind.Malformed` → `_ => FlagKind.Boolean` (a flag passa a ser lida como booleana: cai
  `A_type_that_is_not_text_refuses_the_snapshot` em todos os casos);
- remover `|| !keys.Add(key)` (cai `A_key_repeated_by_a_flag_of_an_unknown_type...`).

- [ ] **Step 7: Format, build e commit**

```bash
dotnet format ToggleForge.slnx --verify-no-changes
dotnet build ToggleForge.slnx -c Release
git add src/ToggleForge.Sdk/SnapshotReader.cs tests/ToggleForge.Sdk.Tests/SnapshotReaderTests.cs docs/adr/0010-sdk-sincronizacao-e-cache.md
git commit -m "fix(sdk): skip flags of an unknown type instead of refusing the snapshot

A flag type published after this SDK would otherwise stop old game builds from
receiving every flag. The key is still required and unique across types.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 8: Revisão e PR**

Revisão Opus só de leitura do diff; corrigir o que for confirmado; PR "fix(sdk): ignora flag de tipo
desconhecido"; esperar CI e a palavra do usuário.

---

## Fatia 3 — Núcleo: `Flag`, `JsonFlag` e `EvaluateJson`

Branch: `feat/json-flag-evaluation`.

### Tarefa 2: refactoring — base `Flag` com a escolha comum

Comportamento igual; nenhum teste novo. Os testes existentes provam que nada mudou.

**Files:**
- Create: `src/ToggleForge.Evaluation/Flag.cs`
- Create: `src/ToggleForge.Evaluation/FlagChoice.cs`
- Modify: `src/ToggleForge.Evaluation/BooleanFlag.cs`
- Modify: `src/ToggleForge.Evaluation/ConfigurationSnapshot.cs`
- Modify: `src/ToggleForge.Evaluation/FlagEvaluator.cs`
- Modify: `tests/ToggleForge.Sdk.Tests/SnapshotReaderTests.cs:14-15`
- Modify: `tests/ToggleForge.Evaluation.Tests/FlagEvaluatorTests.cs` (`SnapshotWith`)
- Modify: `tests/ToggleForge.Evaluation.Tests/ConfigurationSnapshotTests.cs` (`SnapshotWith`)

**Interfaces:**
- Produces:
  - `public abstract class Flag` com `FlagKey Key`, `bool IsEnabled`, `ITargetingRule Targeting`, construtor
    `private protected Flag(FlagKey key, bool isEnabled, ITargetingRule targeting)`,
    `internal FlagChoice Choose(FlagIdentity identity, EvaluationContext context)` e
    `private protected FlagIdentity IdentityIn(ProjectKey projectKey, EnvironmentKey environmentKey)`.
  - `internal readonly struct FlagChoice` com `EvaluationReason Reason`, `int? Bucket`, `bool UsesDefault` e
    `T Pick<T>(T onValue, T offValue)`.
  - `ConfigurationSnapshot(ProjectKey, EnvironmentKey, long, IEnumerable<Flag>)` e
    `bool TryGetFlag(FlagKey key, out Flag? flag)`.

- [ ] **Step 1: Criar `FlagChoice.cs`**

```csharp
using System;

namespace ToggleForge.Evaluation
{
    /// <summary>
    /// Qual valor da flag vale para um jogador, e por quê. Cada tipo de flag aplica a escolha aos próprios
    /// valores, então a regra (inativa → desligado; senão o targeting decide) existe num lugar só.
    /// </summary>
    internal readonly struct FlagChoice
    {
        private readonly Variant _variant;

        private FlagChoice(Variant variant, EvaluationReason reason, int? bucket)
        {
            _variant = variant;
            Reason = reason;
            Bucket = bucket;
        }

        public EvaluationReason Reason { get; }

        public int? Bucket { get; }

        /// <summary>Nenhum valor da flag vale: o jogo recebe o próprio valor padrão.</summary>
        public bool UsesDefault => _variant == Variant.Default;

        public static FlagChoice On(int? bucket) => new FlagChoice(Variant.On, EvaluationReason.Included, bucket);

        public static FlagChoice Off(EvaluationReason reason, int? bucket) => new FlagChoice(Variant.Off, reason, bucket);

        public static FlagChoice Default(EvaluationReason reason) => new FlagChoice(Variant.Default, reason, bucket: null);

        /// <exception cref="InvalidOperationException">A escolha é o valor padrão; confira <see cref="UsesDefault"/> antes.</exception>
        public T Pick<T>(T onValue, T offValue) => _variant switch
        {
            Variant.On => onValue,
            Variant.Off => offValue,
            _ => throw new InvalidOperationException("A default choice has no flag value to pick."),
        };

        private enum Variant
        {
            On,
            Off,
            Default,
        }
    }
}
```

- [ ] **Step 2: Criar `Flag.cs`**

```csharp
using System;

namespace ToggleForge.Evaluation
{
    /// <summary>
    /// O que toda flag publicada tem: chave, se está ativa e a regra de targeting. Inativa, a flag entrega o valor
    /// desligado; ativa, a regra decide entre ligado e desligado. Só este assembly cria tipos de flag, então quem
    /// avalia conhece todos eles (ADR 0011).
    /// </summary>
    public abstract class Flag
    {
        private protected Flag(FlagKey key, bool isEnabled, ITargetingRule targeting)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Targeting = targeting ?? throw new ArgumentNullException(nameof(targeting));
            IsEnabled = isEnabled;
        }

        public FlagKey Key { get; }

        public bool IsEnabled { get; }

        public ITargetingRule Targeting { get; }

        internal FlagChoice Choose(FlagIdentity identity, EvaluationContext context)
        {
            if (!IsEnabled)
            {
                return FlagChoice.Off(EvaluationReason.Disabled, bucket: null);
            }

            var outcome = Targeting.Decide(identity, context);
            return outcome.Decision switch
            {
                TargetingDecision.Included => FlagChoice.On(outcome.Bucket),
                TargetingDecision.Excluded => FlagChoice.Off(EvaluationReason.Excluded, outcome.Bucket),
                TargetingDecision.PlayerIdRequired => FlagChoice.Default(EvaluationReason.MissingPlayerId),
                var unknown => throw new InvalidOperationException($"Unknown targeting decision '{unknown}'."),
            };
        }

        /// <summary>A identidade desta flag no projeto e no ambiente dados, que entram no hash do rollout.</summary>
        private protected FlagIdentity IdentityIn(ProjectKey projectKey, EnvironmentKey environmentKey)
        {
            if (projectKey is null)
            {
                throw new ArgumentNullException(nameof(projectKey));
            }

            if (environmentKey is null)
            {
                throw new ArgumentNullException(nameof(environmentKey));
            }

            return new FlagIdentity(projectKey, environmentKey, Key);
        }
    }
}
```

- [ ] **Step 3: Reescrever `BooleanFlag.cs`**

```csharp
using System;

namespace ToggleForge.Evaluation
{
    /// <summary>
    /// Flag booleana publicada. Inativa, devolve <see cref="OffValue"/>; ativa, a regra de targeting decide
    /// entre <see cref="OnValue"/> e <see cref="OffValue"/>.
    /// </summary>
    public sealed class BooleanFlag : Flag
    {
        public BooleanFlag(FlagKey key, bool isEnabled, bool onValue, bool offValue, ITargetingRule targeting)
            : base(key, isEnabled, targeting)
        {
            OnValue = onValue;
            OffValue = offValue;
        }

        public bool OnValue { get; }

        public bool OffValue { get; }

        /// <summary>
        /// Avalia a flag fora de um snapshot, no projeto e ambiente dados, que entram no hash do rollout. É o
        /// que o <see cref="FlagEvaluator"/> faz depois de achar a flag; o resultado sai sem versão.
        /// </summary>
        public EvaluationResult<bool> Evaluate(
            ProjectKey projectKey, EnvironmentKey environmentKey, EvaluationContext context, bool defaultValue)
        {
            var identity = IdentityIn(projectKey, environmentKey);
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            return Evaluate(identity, context, defaultValue, snapshotVersion: null);
        }

        internal EvaluationResult<bool> Evaluate(
            FlagIdentity identity, EvaluationContext context, bool defaultValue, long? snapshotVersion)
        {
            var choice = Choose(identity, context);
            var value = choice.UsesDefault ? defaultValue : choice.Pick(OnValue, OffValue);
            return new EvaluationResult<bool>(value, choice.Reason, snapshotVersion, choice.Bucket);
        }
    }
}
```

- [ ] **Step 4: `ConfigurationSnapshot` guarda `Flag`**

Em `ConfigurationSnapshot.cs`, troque toda ocorrência de `BooleanFlag` por `Flag` (o campo `_flags`, o
parâmetro `flags` do construtor, `TryGetFlag` e `IndexByKey`). Documente `TryGetFlag`:

```csharp
        /// <summary>A flag com a chave, de qualquer tipo; quem avalia confere o tipo.</summary>
        public bool TryGetFlag(FlagKey key, [NotNullWhen(true)] out Flag? flag) =>
            _flags.TryGetValue(key, out flag);
```

- [ ] **Step 5: `FlagEvaluator` acha a flag e confere o tipo**

Substitua o corpo da classe `FlagEvaluator`:

```csharp
    /// <summary>Avaliador puro, sem estado: o mesmo snapshot e contexto produzem sempre o mesmo resultado.</summary>
    public sealed class FlagEvaluator : IFlagEvaluator
    {
        public EvaluationResult<bool> EvaluateBoolean(
            ConfigurationSnapshot? snapshot, string? flagKey, EvaluationContext context, bool defaultValue)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (snapshot is null)
            {
                return Fallback(defaultValue, EvaluationReason.SnapshotMissing, snapshotVersion: null);
            }

            var flag = Find(snapshot, flagKey, out var missing);
            if (flag is null)
            {
                return Fallback(defaultValue, missing, snapshot.Version);
            }

            if (flag is not BooleanFlag boolean)
            {
                throw new InvalidOperationException($"Unknown flag type '{flag.GetType().Name}'.");
            }

            return boolean.Evaluate(IdentityOf(snapshot, boolean), context, defaultValue, snapshot.Version);
        }

        /// <returns>Nula quando a chave é inválida ou não está no snapshot; <paramref name="missing"/> diz qual dos dois.</returns>
        private static Flag? Find(ConfigurationSnapshot snapshot, string? flagKey, out EvaluationReason missing)
        {
            if (!FlagKey.TryParse(flagKey, out var key))
            {
                missing = EvaluationReason.InvalidFlagKey;
                return null;
            }

            if (!snapshot.TryGetFlag(key, out var flag))
            {
                missing = EvaluationReason.FlagNotFound;
                return null;
            }

            missing = default;
            return flag;
        }

        private static FlagIdentity IdentityOf(ConfigurationSnapshot snapshot, Flag flag) =>
            new FlagIdentity(snapshot.ProjectKey, snapshot.EnvironmentKey, flag.Key);

        private static EvaluationResult<T> Fallback<T>(T defaultValue, EvaluationReason reason, long? snapshotVersion) =>
            new EvaluationResult<T>(defaultValue, reason, snapshotVersion, bucket: null);
    }
```

O `throw` do tipo desconhecido é o "impossível" desta tarefa: só existe `BooleanFlag`. A Tarefa 3 o troca por
`TypeMismatch`.

- [ ] **Step 6: Ajustar os testes que leem membros de `BooleanFlag`**

Em `tests/ToggleForge.Sdk.Tests/SnapshotReaderTests.cs`, no primeiro teste:

```csharp
        Assert.True(snapshot.TryGetFlag(FlagKey.Parse("new-shop"), out var found));
        var flag = Assert.IsType<BooleanFlag>(found);
        Assert.Equal((true, true, false), (flag.IsEnabled, flag.OnValue, flag.OffValue));
```

Em `FlagEvaluatorTests` e `ConfigurationSnapshotTests`, troque `params BooleanFlag[] flags` por
`params Flag[] flags` em `SnapshotWith`. Em `ConfigurationSnapshotTests`, se `new List<BooleanFlag>` não
compilar como argumento, deixe como está: `IEnumerable<T>` é covariante.

- [ ] **Step 7: Rodar tudo**

Run: `dotnet build ToggleForge.slnx -c Release && dotnet test --solution ToggleForge.slnx -c Release`
Expected: build sem avisos, todos os testes PASS (os de API precisam do Docker rodando).

- [ ] **Step 8: Commit do refactoring**

```bash
dotnet format ToggleForge.slnx --verify-no-changes
git add src/ToggleForge.Evaluation tests/ToggleForge.Sdk.Tests/SnapshotReaderTests.cs tests/ToggleForge.Evaluation.Tests
git commit -m "refactor(evaluation): extract the choice every flag type shares

Flag holds the key, the enabled state and the targeting rule; FlagChoice says
which value applies and why. BooleanFlag keeps its behavior.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Tarefa 3: `JsonFlag`, `TypeMismatch` e `InvalidValue`

**Files:**
- Create: `src/ToggleForge.Evaluation/JsonFlag.cs`
- Modify: `src/ToggleForge.Evaluation/EvaluationReason.cs`
- Modify: `src/ToggleForge.Evaluation/EvaluationResult.cs` (`IsFallback`)
- Modify: `src/ToggleForge.Evaluation/FlagEvaluator.cs` (`EvaluateBoolean`)
- Create: `tests/ToggleForge.Evaluation.Tests/JsonFlagTests.cs`
- Modify: `tests/ToggleForge.Evaluation.Tests/FlagEvaluatorTests.cs`
- Modify: `tests/ToggleForge.Evaluation.Tests/ConfigurationSnapshotTests.cs`
- Modify: `tests/ToggleForge.Evaluation.Tests/NullArgumentTests.cs`

**Interfaces:**
- Consumes: `Flag`, `FlagChoice`, `IdentityIn` (Tarefa 2).
- Produces:
  - `EvaluationReason.TypeMismatch` e `EvaluationReason.InvalidValue`, nesta ordem, depois de `SnapshotExpired`;
    os dois contam em `IsFallback`.
  - `public sealed class JsonFlag : Flag` com
    `JsonFlag(FlagKey key, bool isEnabled, string onValue, string offValue, ITargetingRule targeting)`,
    `string OnValue`, `string OffValue`,
    `EvaluationResult<T> Evaluate<T>(ProjectKey projectKey, EnvironmentKey environmentKey, EvaluationContext context, T defaultValue, Func<string, T> parse)`
    (usado pela simulação da API na Fatia 4) e
    `internal EvaluationResult<T> Evaluate<T>(FlagIdentity identity, EvaluationContext context, T defaultValue, Func<string, T> parse, long? snapshotVersion)`.

- [ ] **Step 1: Escrever os testes que falham**

Crie `tests/ToggleForge.Evaluation.Tests/JsonFlagTests.cs`:

```csharp
namespace ToggleForge.Evaluation.Tests;

/// <summary>A flag JSON avaliada direto, como a simulação do rascunho faz.</summary>
public sealed class JsonFlagTests
{
    private const string OnJson = """{"discount":0.2}""";
    private const string OffJson = """{"discount":0}""";
    private const string DefaultValue = "game-default";

    [Theory]
    [InlineData(NewShopFlag.ReferencePlayerBucket + 1, OnJson, EvaluationReason.Included)]
    [InlineData(NewShopFlag.ReferencePlayerBucket, OffJson, EvaluationReason.Excluded)]
    public void A_partial_rollout_hands_the_chosen_json_to_the_parser_with_the_bucket_of_the_rollout_vectors(
        int basisPoints, string expectedJson, EvaluationReason expectedReason)
    {
        var flag = ShopConfig(enabled: true, RolloutPercentage.FromBasisPoints(basisPoints));

        var result = flag.Evaluate(NewShopFlag.Project, NewShopFlag.Environment, ReferencePlayer(), DefaultValue, json => json);

        Assert.Equal(expectedJson, result.Value);
        Assert.Equal(expectedReason, result.Reason);
        Assert.Equal(NewShopFlag.ReferencePlayerBucket, result.Bucket);
        Assert.Null(result.SnapshotVersion);
        Assert.False(result.IsFallback);
    }

    [Fact]
    public void A_disabled_flag_hands_the_off_json_to_the_parser()
    {
        var flag = ShopConfig(enabled: false, RolloutPercentage.All);

        var result = flag.Evaluate(NewShopFlag.Project, NewShopFlag.Environment, ReferencePlayer(), DefaultValue, json => json);

        Assert.Equal((OffJson, EvaluationReason.Disabled, (int?)null), (result.Value, result.Reason, result.Bucket));
    }

    [Fact]
    public void A_json_null_value_reaches_the_parser_like_any_other()
    {
        var flag = new JsonFlag(NewShopFlag.Key, isEnabled: true, onValue: "null", offValue: OffJson, Rollout(RolloutPercentage.All));
        string? received = null;

        var result = flag.Evaluate(
            NewShopFlag.Project, NewShopFlag.Environment, ReferencePlayer(), DefaultValue, json => received = json);

        Assert.Equal("null", received);
        Assert.Equal(("null", EvaluationReason.Included), (result.Value, result.Reason));
    }

    [Fact]
    public void Without_a_player_id_a_partial_rollout_returns_the_default_without_calling_the_parser()
    {
        var flag = ShopConfig(enabled: true, RolloutPercentage.FromPercent(50m));
        var calls = 0;

        var result = flag.Evaluate(
            NewShopFlag.Project, NewShopFlag.Environment, EvaluationContext.Anonymous, DefaultValue, json => { calls++; return json; });

        Assert.Equal((DefaultValue, EvaluationReason.MissingPlayerId), (result.Value, result.Reason));
        Assert.True(result.IsFallback);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("format")]
    [InlineData("state")]
    [InlineData("argument")]
    public void A_parser_that_throws_falls_back_with_InvalidValue(string failure)
    {
        Exception exception = failure switch
        {
            "format" => new FormatException("not a number"),
            "state" => new InvalidOperationException("missing field"),
            _ => new ArgumentException("bad shape"),
        };
        var flag = ShopConfig(enabled: true, RolloutPercentage.All);

        var result = flag.Evaluate<string>(
            NewShopFlag.Project, NewShopFlag.Environment, ReferencePlayer(), DefaultValue, _ => throw exception);

        Assert.Equal((DefaultValue, EvaluationReason.InvalidValue, (int?)null), (result.Value, result.Reason, result.Bucket));
        Assert.True(result.IsFallback);
    }

    private static JsonFlag ShopConfig(bool enabled, RolloutPercentage percentage) =>
        new(NewShopFlag.Key, enabled, OnJson, OffJson, Rollout(percentage));

    private static PercentageRollout Rollout(RolloutPercentage percentage) => new(percentage, NewShopFlag.Seed);

    private static EvaluationContext ReferencePlayer() =>
        EvaluationContext.ForPlayer(PlayerId.Parse(NewShopFlag.ReferencePlayer));
}
```

O primeiro teste usa a mesma chave e semente de `NewShopFlag`, cujo bucket (7.370) vem dos vetores v1: provar
o mesmo bucket numa `JsonFlag` prova que o tipo não entra no hash.

Em `FlagEvaluatorTests`, acrescente:

```csharp
    [Fact]
    public void A_json_flag_asked_as_boolean_falls_back_with_TypeMismatch()
    {
        var config = new JsonFlag(
            FlagKey.Parse("shop-config"), isEnabled: true, onValue: "{}", offValue: "{}",
            new PercentageRollout(RolloutPercentage.All, NewShopFlag.Seed));

        var result = _evaluator.EvaluateBoolean(SnapshotWith(config), "shop-config", ReferencePlayer(), defaultValue: true);

        AssertResult(result, true, EvaluationReason.TypeMismatch, SnapshotVersion);
        Assert.True(result.IsFallback);
        Assert.Null(result.Bucket);
    }
```

Em `ConfigurationSnapshotTests`, acrescente:

```csharp
    [Fact]
    public void Rejects_the_same_key_in_flags_of_different_types()
    {
        var config = new JsonFlag(
            FlagKey.Parse("new-shop"), true, "{}", "{}", new PercentageRollout(RolloutPercentage.All, NewShopFlag.Seed));

        Assert.Throws<ArgumentException>(() => SnapshotWith(Flag("new-shop"), config));
    }
```

Em `NullArgumentTests`, acrescente:

```csharp
    [Fact]
    public void JsonFlag_rejects_null_key_values_and_targeting()
    {
        AssertNullRejected("key", () => new JsonFlag(null!, true, "{}", "{}", Rollout));
        AssertNullRejected("onValue", () => new JsonFlag(Flag, true, null!, "{}", Rollout));
        AssertNullRejected("offValue", () => new JsonFlag(Flag, true, "{}", null!, Rollout));
        AssertNullRejected("targeting", () => new JsonFlag(Flag, true, "{}", "{}", null!));
    }

    [Fact]
    public void JsonFlag_Evaluate_rejects_null_keys_context_and_parser()
    {
        var flag = new JsonFlag(Flag, isEnabled: true, onValue: "{}", offValue: "{}", Rollout);
        var player = EvaluationContext.ForPlayer(PlayerId.Parse("player"));

        AssertNullRejected("projectKey", () => flag.Evaluate(null!, Environment, player, "", json => json));
        AssertNullRejected("environmentKey", () => flag.Evaluate(Project, null!, player, "", json => json));
        AssertNullRejected("context", () => flag.Evaluate(Project, Environment, null!, "", json => json));
        AssertNullRejected("parse", () => flag.Evaluate(Project, Environment, player, "", null!));
    }
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet build tests/ToggleForge.Evaluation.Tests -c Release`
Expected: FAIL de compilação — `JsonFlag`, `EvaluationReason.TypeMismatch` e `EvaluationReason.InvalidValue` não
existem.

- [ ] **Step 3: Motivos novos e `IsFallback`**

Em `EvaluationReason.cs`, depois de `SnapshotExpired`:

```csharp
        /// <summary>A flag existe, mas é de outro tipo: uma flag JSON pedida como booleana, ou o contrário (ADR 0011).</summary>
        TypeMismatch,

        /// <summary>O parser do jogo lançou exceção ao ler o valor JSON da flag (ADR 0011).</summary>
        InvalidValue,
```

Em `EvaluationResult.cs`, `IsFallback` passa a ser:

```csharp
        public bool IsFallback =>
            Reason is EvaluationReason.SnapshotMissing
                or EvaluationReason.InvalidFlagKey
                or EvaluationReason.FlagNotFound
                or EvaluationReason.MissingPlayerId
                or EvaluationReason.TypeMismatch
                or EvaluationReason.InvalidValue;
```

- [ ] **Step 4: Criar `JsonFlag.cs`**

```csharp
using System;

namespace ToggleForge.Evaluation
{
    /// <summary>
    /// Flag de configuração JSON publicada (ADR 0011). Os valores são texto JSON compacto que o núcleo não lê:
    /// quem criou a flag já o validou, e o parser do jogo o converte para a classe dele.
    /// </summary>
    public sealed class JsonFlag : Flag
    {
        public JsonFlag(FlagKey key, bool isEnabled, string onValue, string offValue, ITargetingRule targeting)
            : base(key, isEnabled, targeting)
        {
            OnValue = onValue ?? throw new ArgumentNullException(nameof(onValue));
            OffValue = offValue ?? throw new ArgumentNullException(nameof(offValue));
        }

        public string OnValue { get; }

        public string OffValue { get; }

        /// <summary>
        /// Avalia a flag fora de um snapshot, como a simulação do rascunho; o resultado sai sem versão.
        /// </summary>
        /// <param name="parse">
        /// Converte o texto JSON escolhido. Não é chamado quando o resultado é o valor padrão; se lançar exceção,
        /// volta <paramref name="defaultValue"/> com <see cref="EvaluationReason.InvalidValue"/>.
        /// </param>
        public EvaluationResult<T> Evaluate<T>(
            ProjectKey projectKey, EnvironmentKey environmentKey, EvaluationContext context, T defaultValue, Func<string, T> parse)
        {
            var identity = IdentityIn(projectKey, environmentKey);
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (parse is null)
            {
                throw new ArgumentNullException(nameof(parse));
            }

            return Evaluate(identity, context, defaultValue, parse, snapshotVersion: null);
        }

        internal EvaluationResult<T> Evaluate<T>(
            FlagIdentity identity, EvaluationContext context, T defaultValue, Func<string, T> parse, long? snapshotVersion)
        {
            var choice = Choose(identity, context);
            if (choice.UsesDefault)
            {
                return new EvaluationResult<T>(defaultValue, choice.Reason, snapshotVersion, choice.Bucket);
            }

            T value;
            try
            {
                value = parse(choice.Pick(OnValue, OffValue));
            }
            catch (Exception)
            {
                // O valor vem de fora do build e o parser é do jogo: uma recusa dele vira fallback, porque a
                // avaliação nunca lança por dado ruim.
                return new EvaluationResult<T>(defaultValue, EvaluationReason.InvalidValue, snapshotVersion, bucket: null);
            }

            return new EvaluationResult<T>(value, choice.Reason, snapshotVersion, choice.Bucket);
        }
    }
}
```

- [ ] **Step 5: `EvaluateBoolean` devolve `TypeMismatch`**

Em `FlagEvaluator.EvaluateBoolean`, troque o bloco do `throw` por:

```csharp
            return flag is BooleanFlag boolean
                ? boolean.Evaluate(IdentityOf(snapshot, boolean), context, defaultValue, snapshot.Version)
                : Fallback(defaultValue, EvaluationReason.TypeMismatch, snapshot.Version);
```

- [ ] **Step 6: Rodar e ver passar**

Run: `dotnet test --project tests/ToggleForge.Evaluation.Tests -c Release`
Expected: PASS em todos.

- [ ] **Step 7: Sabotar**

Cada mutação, sozinha, derruba um teste; desfaça depois:
- tirar `or EvaluationReason.InvalidValue` de `IsFallback`;
- `return new EvaluationResult<T>(defaultValue, EvaluationReason.InvalidValue, ...)` com `choice.Bucket` no lugar
  de `bucket: null`;
- remover o `if (choice.UsesDefault)` de `JsonFlag` (o parser passa a ser chamado sem jogador: cai o teste de
  `calls == 0`, ou o `Pick` lança);
- `: Fallback(defaultValue, EvaluationReason.TypeMismatch, ...)` → `EvaluationReason.FlagNotFound`;
- `choice.Pick(OnValue, OffValue)` → `choice.Pick(OffValue, OnValue)` em `JsonFlag`.

- [ ] **Step 8: Commit**

```bash
dotnet format ToggleForge.slnx --verify-no-changes
git add src/ToggleForge.Evaluation tests/ToggleForge.Evaluation.Tests
git commit -m "feat(evaluation): add json flags with TypeMismatch and InvalidValue

A JsonFlag hands the chosen json text to the game's parser; a parser that throws
falls back with InvalidValue, and asking a flag as the wrong type falls back
with TypeMismatch. The type stays out of the rollout hash.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Tarefa 4: `FlagEvaluator.EvaluateJson`

**Files:**
- Modify: `src/ToggleForge.Evaluation/IFlagEvaluator.cs`
- Modify: `src/ToggleForge.Evaluation/FlagEvaluator.cs`
- Modify: `samples/ToggleForge.RolloutDemo/TallyingFlagEvaluator.cs`
- Modify: `tests/ToggleForge.Evaluation.Tests/FlagEvaluatorTests.cs`
- Modify: `tests/ToggleForge.Evaluation.Tests/NullArgumentTests.cs`

**Interfaces:**
- Consumes: `JsonFlag.Evaluate<T>(FlagIdentity, EvaluationContext, T, Func<string, T>, long?)` (Tarefa 3).
- Produces: `EvaluationResult<T> EvaluateJson<T>(ConfigurationSnapshot? snapshot, string? flagKey, EvaluationContext context, T defaultValue, Func<string, T> parse)`
  em `IFlagEvaluator` e `FlagEvaluator`. A Fatia 5 a chama de `ToggleForgeClient.EvaluateJson<T>`.

- [ ] **Step 1: Escrever os testes que falham**

Em `FlagEvaluatorTests`, acrescente:

```csharp
    private const string ShopConfigJson = """{"discount":0.2}""";

    [Fact]
    public void EvaluateJson_hands_the_published_json_to_the_parser_with_the_snapshot_version()
    {
        var result = _evaluator.EvaluateJson(SnapshotWith(ShopConfig()), "shop-config", ReferencePlayer(), "game-default", json => json);

        Assert.Equal((ShopConfigJson, EvaluationReason.Included, (long?)SnapshotVersion), (result.Value, result.Reason, result.SnapshotVersion));
    }

    [Fact]
    public void EvaluateJson_without_a_snapshot_returns_the_default_without_calling_the_parser()
    {
        var calls = 0;

        var result = _evaluator.EvaluateJson(null, "shop-config", ReferencePlayer(), "game-default", json => { calls++; return json; });

        Assert.Equal(("game-default", EvaluationReason.SnapshotMissing, (long?)null), (result.Value, result.Reason, result.SnapshotVersion));
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("Shop Config", EvaluationReason.InvalidFlagKey)]
    [InlineData("battle-pass", EvaluationReason.FlagNotFound)]
    [InlineData("new-shop", EvaluationReason.TypeMismatch)]
    public void EvaluateJson_falls_back_without_calling_the_parser_when_no_json_flag_answers(
        string flagKey, EvaluationReason expectedReason)
    {
        var snapshot = SnapshotWith(ShopConfig(), Flag("new-shop", enabled: true, RolloutPercentage.All));
        var calls = 0;

        var result = _evaluator.EvaluateJson(snapshot, flagKey, ReferencePlayer(), "game-default", json => { calls++; return json; });

        Assert.Equal(("game-default", expectedReason, (long?)SnapshotVersion), (result.Value, result.Reason, result.SnapshotVersion));
        Assert.True(result.IsFallback);
        Assert.Equal(0, calls);
    }

    private static JsonFlag ShopConfig() =>
        new(FlagKey.Parse("shop-config"), isEnabled: true, ShopConfigJson, offValue: "{}",
            new PercentageRollout(RolloutPercentage.All, NewShopFlag.Seed));
```

Em `NullArgumentTests`, acrescente:

```csharp
    [Fact]
    public void FlagEvaluator_EvaluateJson_rejects_null_context_and_parser()
    {
        var snapshot = new ConfigurationSnapshot(Project, Environment, 1, []);
        var player = EvaluationContext.ForPlayer(PlayerId.Parse("player"));

        AssertNullRejected("context", () => new FlagEvaluator().EvaluateJson(snapshot, "shop-config", null!, "", json => json));
        AssertNullRejected("parse", () => new FlagEvaluator().EvaluateJson<string>(snapshot, "shop-config", player, "", null!));
    }
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet build tests/ToggleForge.Evaluation.Tests -c Release`
Expected: FAIL de compilação — `EvaluateJson` não existe.

- [ ] **Step 3: Implementar**

Em `IFlagEvaluator.cs`, acrescente à interface:

```csharp
        /// <param name="parse">
        /// Converte o texto JSON da flag. Não é chamado quando o resultado é o valor padrão; se lançar exceção,
        /// volta <paramref name="defaultValue"/> com <see cref="EvaluationReason.InvalidValue"/>.
        /// </param>
        EvaluationResult<T> EvaluateJson<T>(
            ConfigurationSnapshot? snapshot, string? flagKey, EvaluationContext context, T defaultValue, Func<string, T> parse);
```

(acrescente `using System;` no topo de `IFlagEvaluator.cs`).

Em `FlagEvaluator.cs`, depois de `EvaluateBoolean`:

```csharp
        public EvaluationResult<T> EvaluateJson<T>(
            ConfigurationSnapshot? snapshot, string? flagKey, EvaluationContext context, T defaultValue, Func<string, T> parse)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (parse is null)
            {
                throw new ArgumentNullException(nameof(parse));
            }

            if (snapshot is null)
            {
                return Fallback(defaultValue, EvaluationReason.SnapshotMissing, snapshotVersion: null);
            }

            var flag = Find(snapshot, flagKey, out var missing);
            if (flag is null)
            {
                return Fallback(defaultValue, missing, snapshot.Version);
            }

            return flag is JsonFlag json
                ? json.Evaluate(IdentityOf(snapshot, json), context, defaultValue, parse, snapshot.Version)
                : Fallback(defaultValue, EvaluationReason.TypeMismatch, snapshot.Version);
        }
```

Em `samples/ToggleForge.RolloutDemo/TallyingFlagEvaluator.cs`, a classe passa a ser:

```csharp
/// <summary>Não é thread-safe: a demo avalia num único thread.</summary>
internal sealed class TallyingFlagEvaluator(IFlagEvaluator inner) : IFlagEvaluator
{
    private readonly Dictionary<EvaluationReason, int> _countsByReason = [];

    public IReadOnlyDictionary<EvaluationReason, int> CountsByReason => _countsByReason;

    public EvaluationResult<bool> EvaluateBoolean(
        ConfigurationSnapshot? snapshot, string? flagKey, EvaluationContext context, bool defaultValue) =>
        Tally(inner.EvaluateBoolean(snapshot, flagKey, context, defaultValue));

    public EvaluationResult<T> EvaluateJson<T>(
        ConfigurationSnapshot? snapshot, string? flagKey, EvaluationContext context, T defaultValue, Func<string, T> parse) =>
        Tally(inner.EvaluateJson(snapshot, flagKey, context, defaultValue, parse));

    private EvaluationResult<T> Tally<T>(EvaluationResult<T> result)
    {
        _countsByReason[result.Reason] = _countsByReason.GetValueOrDefault(result.Reason) + 1;
        return result;
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet build ToggleForge.slnx -c Release && dotnet test --project tests/ToggleForge.Evaluation.Tests -c Release && dotnet run --project samples/ToggleForge.RolloutDemo -c Release`
Expected: build sem avisos, testes PASS, a demo de rollout imprime o mesmo resultado de antes.

- [ ] **Step 5: Sabotar**

- no ramo final de `EvaluateJson`, `EvaluationReason.TypeMismatch` → `EvaluationReason.FlagNotFound` (cai o
  caso `"new-shop"` da teoria);
- remover o `if (snapshot is null)` de `EvaluateJson` (lança `NullReferenceException`: cai o teste sem snapshot);
- `snapshot.Version` → `snapshotVersion: null` no ramo `JsonFlag` (cai o teste da versão).

- [ ] **Step 6: Commit**

```bash
dotnet format ToggleForge.slnx --verify-no-changes
git add src/ToggleForge.Evaluation samples/ToggleForge.RolloutDemo tests/ToggleForge.Evaluation.Tests
git commit -m "feat(evaluation): evaluate json flags from a snapshot

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Tarefa 5: revisão e PR da Fatia 3

- [ ] Rodar a solução inteira: `dotnet test --solution ToggleForge.slnx -c Release` (Docker rodando).
- [ ] Revisão Opus só de leitura do diff da branch; sabotagem paralela numa cópia da árvore, se o revisor achar
  lacuna.
- [ ] PR "feat(evaluation): flags JSON no núcleo" com resumo, decisões, testes, sabotagem e checklist; esperar o
  CI e a palavra do usuário.

---

## Fatia 4 — domínio, banco e API

Branch: `feat/json-flags-api`. Detalhada em 2026-10-04 sobre o `main` com as Fatias 2 e 3 (`59d9654`).

**Decisões desta fatia, registradas antes do código:**
- **Migration (escolha do usuário):** a migration `Initial` do módulo Authoring é **regenerada** com as colunas
  novas, em vez de converter `boolean` em `json`. O Postgres não converte `boolean` em `json` sem `USING`, e o
  CLAUDE.md proíbe editar migrations à mão. Como ainda não há produção, todo banco de desenvolvimento existente é
  recriado (`drop` e `--migrate`). A ADR 0011 registra a escolha e que, depois do Marco 6, ela não se repete.
- **Uma forma só de entrada:** a API não sabe o tipo de uma flag num PATCH, e `true` é um valor válido também
  numa flag JSON. Por isso todo valor chega ao domínio como `JsonFlagValue`, e o domínio o interpreta pelo tipo
  da flag. Uma flag booleana aceita só `true`/`false`.
- **Console:** fica como está nesta fatia. Até a Fatia 6, um rascunho com flag JSON criada pela API não abre no
  console, porque os records dele leem `onValue` como `bool`. É uma lacuna conhecida, descrita no PR.
- **Simulação:** sempre tem jogador, e o parser dela é `json => json`, que nunca lança. Então `MissingPlayerId` e
  `InvalidValue` não aparecem nela, e o `ReasonOf` de `Responses.cs` não muda.

Ordem e paralelismo: a Tarefa 6 (`JsonFlagValue`) e a Tarefa 7 (contrato OpenAPI) não compartilham arquivos e
rodam em paralelo, em worktrees separadas. A Tarefa 8 (modelo e persistência) depende da 6. A Tarefa 9 (API)
depende da 7 e da 8.

### Tarefa 6: `JsonFlagValue`

**Files:**
- Create: `src/ToggleForge.Domain/Authoring/JsonFlagValue.cs`
- Create: `src/ToggleForge.Domain/Authoring/JsonValueProblem.cs`
- Test: `tests/ToggleForge.Domain.Tests/Authoring/JsonFlagValueTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record JsonFlagValue` com `const int MaxBytes = 4096`, `const int MaxDepth = 8`,
    `static JsonFlagValue True`, `static JsonFlagValue False`, `string Text`, `int ByteCount`,
    `static bool TryParse(string? json, [NotNullWhen(true)] out JsonFlagValue? value, out JsonValueProblem problem)`,
    `static JsonFlagValue Parse(string json)` (lança `ArgumentException`; só reidratação e testes) e
    `internal bool? AsBoolean`.
  - `public enum JsonValueProblem { NotJson, DuplicateKey, TooDeep, NumberOutOfRange, TooLarge }`.

- [ ] **Step 1: Escrever os testes que falham**

`tests/ToggleForge.Domain.Tests/Authoring/JsonFlagValueTests.cs`:

```csharp
using ToggleForge.Domain.Authoring;

namespace ToggleForge.Domain.Tests.Authoring;

public sealed class JsonFlagValueTests
{
    [Theory]
    [InlineData("3", "3")]
    [InlineData("\"gold\"", "\"gold\"")]
    [InlineData("null", "null")]
    [InlineData("true", "true")]
    [InlineData("[ 1 , 2 ]", "[1,2]")]
    [InlineData("{ \"b\" : 1 ,\n \"a\" : [ 1, { } ] }", "{\"b\":1,\"a\":[1,{}]}")]
    [InlineData("1.50", "1.50")]
    [InlineData("\"ação\"", "\"ação\"")]
    public void Any_json_value_is_kept_compact_with_its_keys_in_order(string json, string expected)
    {
        Assert.True(JsonFlagValue.TryParse(json, out var value, out _));
        Assert.Equal(expected, value.Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("tru")]
    [InlineData("{'a':1}")]
    [InlineData("1 2")]
    [InlineData("{\"a\":1,}")]
    public void Text_that_is_not_one_json_value_is_refused(string? json)
    {
        Assert.False(JsonFlagValue.TryParse(json, out var value, out var problem));
        Assert.Null(value);
        Assert.Equal(JsonValueProblem.NotJson, problem);
    }

    [Theory]
    [InlineData("{\"a\":1,\"a\":2}")]
    [InlineData("{\"x\":{\"a\":1,\"a\":1}}")]
    [InlineData("[{\"a\":1,\"\\u0061\":2}]")]
    public void A_key_repeated_in_an_object_is_refused_at_any_depth(string json)
    {
        Assert.False(JsonFlagValue.TryParse(json, out _, out var problem));
        Assert.Equal(JsonValueProblem.DuplicateKey, problem);
    }

    [Fact]
    public void The_same_key_in_different_objects_is_fine() =>
        Assert.True(JsonFlagValue.TryParse("[{\"a\":1},{\"a\":2}]", out _, out _));

    [Theory]
    [InlineData(8, true)]
    [InlineData(9, false)]
    public void Values_nest_up_to_8_levels(int levels, bool accepted)
    {
        var json = new string('[', levels) + "1" + new string(']', levels);

        Assert.Equal(accepted, JsonFlagValue.TryParse(json, out _, out var problem));
        if (!accepted)
        {
            Assert.Equal(JsonValueProblem.TooDeep, problem);
        }
    }

    [Fact]
    public void Objects_count_as_levels_too()
    {
        var json = string.Concat(Enumerable.Repeat("{\"a\":", 9)) + "1" + new string('}', 9);

        Assert.False(JsonFlagValue.TryParse(json, out _, out var problem));
        Assert.Equal(JsonValueProblem.TooDeep, problem);
    }

    [Theory]
    [InlineData("1e400")]
    [InlineData("-1e400")]
    [InlineData("[1, 1e309]")]
    public void A_number_out_of_double_range_is_rejected(string json)
    {
        Assert.False(JsonFlagValue.TryParse(json, out _, out var problem));
        Assert.Equal(JsonValueProblem.NumberOutOfRange, problem);
    }

    [Theory]
    [InlineData("1e308")]
    [InlineData("1e-400")]
    [InlineData("12345678901234567890")]
    public void A_number_a_double_can_hold_is_kept_as_written(string json)
    {
        Assert.True(JsonFlagValue.TryParse(json, out var value, out _));
        Assert.Equal(json, value.Text);
    }

    [Theory]
    [InlineData('x', 4094, true)]
    [InlineData('x', 4095, false)]
    [InlineData('ç', 2047, true)]
    [InlineData('ç', 2048, false)]
    public void Up_to_4_KiB_in_utf8_after_compaction(char filler, int count, bool accepted)
    {
        var json = "\"" + new string(filler, count) + "\"";

        Assert.Equal(accepted, JsonFlagValue.TryParse(json, out var value, out var problem));
        if (accepted)
        {
            Assert.Equal(4096, value!.ByteCount);
        }
        else
        {
            Assert.Equal(JsonValueProblem.TooLarge, problem);
        }
    }

    [Fact]
    public void Whitespace_does_not_count_toward_the_limit()
    {
        var json = "[" + new string(' ', 5_000) + "1]";

        Assert.True(JsonFlagValue.TryParse(json, out var value, out _));
        Assert.Equal("[1]", value.Text);
    }

    [Fact]
    public void The_same_json_written_differently_is_the_same_value() =>
        Assert.Equal(JsonFlagValue.Parse("{\"a\": [1, 2]}"), JsonFlagValue.Parse("{ \"a\":[1,2] }"));

    [Fact]
    public void True_and_false_are_the_json_literals()
    {
        Assert.Equal(JsonFlagValue.Parse("true"), JsonFlagValue.True);
        Assert.Equal(JsonFlagValue.Parse("false"), JsonFlagValue.False);
    }

    [Fact]
    public void Parse_rejects_invalid_text() =>
        Assert.Throws<ArgumentException>(() => JsonFlagValue.Parse("{"));
}
```

Os valores esperados são literais contados à mão: `"` + 4.094 `x` + `"` = 4.096 bytes, e `ç` ocupa 2 bytes em
UTF-8 (2 + 2 × 2.047 = 4.096).

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet build tests/ToggleForge.Domain.Tests -c Release`
Expected: FAIL de compilação — `JsonFlagValue` e `JsonValueProblem` não existem.

- [ ] **Step 3: Implementar**

`src/ToggleForge.Domain/Authoring/JsonValueProblem.cs`:

```csharp
namespace ToggleForge.Domain.Authoring;

/// <summary>Por que um texto não serve como valor de flag JSON (ADR 0011).</summary>
public enum JsonValueProblem
{
    NotJson,

    /// <summary>Uma chave se repete no mesmo objeto; o Newtonsoft e o JsonUtility a resolveriam diferente.</summary>
    DuplicateKey,

    TooDeep,

    /// <summary>Um número não cabe num <see cref="double"/>, e cada parser o trataria de um jeito.</summary>
    NumberOutOfRange,

    TooLarge,
}
```

`src/ToggleForge.Domain/Authoring/JsonFlagValue.cs`:

```csharp
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ToggleForge.Domain.Authoring;

/// <summary>
/// O valor de uma flag JSON (ADR 0011): um valor JSON qualquer, sem chave repetida, com até <see cref="MaxDepth"/>
/// níveis, números que cabem num <see cref="double"/> e até <see cref="MaxBytes"/> bytes em UTF-8 depois de
/// compactado. Guarda o texto compacto com a ordem das chaves, então espaço em branco não conta como mudança.
/// </summary>
public sealed record JsonFlagValue
{
    public const int MaxBytes = 4 * 1024;

    public const int MaxDepth = 8;

    // As repetições são procuradas depois, para a recusa dizer qual regra quebrou.
    private static readonly JsonDocumentOptions _reading = new() { AllowDuplicateProperties = true };

    // O texto vai para o snapshot e para o console como JSON, nunca embutido em HTML sem codificação; escapar
    // acentos só aumentaria os bytes que contam para o limite.
    private static readonly JsonWriterOptions _compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private JsonFlagValue(string text) => Text = text;

    /// <summary>Os valores de uma flag booleana, na mesma forma dos valores das flags JSON.</summary>
    public static JsonFlagValue True { get; } = new("true");

    public static JsonFlagValue False { get; } = new("false");

    /// <summary>O JSON compacto.</summary>
    public string Text { get; }

    /// <summary>O tamanho de <see cref="Text"/> em UTF-8, que é o que os limites contam.</summary>
    public int ByteCount => Encoding.UTF8.GetByteCount(Text);

    internal bool? AsBoolean => Text switch
    {
        "true" => true,
        "false" => false,
        _ => null,
    };

    /// <param name="problem">Por que o texto foi recusado; só tem sentido quando o retorno é falso.</param>
    public static bool TryParse(string? json, [NotNullWhen(true)] out JsonFlagValue? value, out JsonValueProblem problem)
    {
        value = null;
        problem = JsonValueProblem.NotJson;
        if (json is null || !TryRead(json, out var document))
        {
            return false;
        }

        using (document)
        {
            if (ProblemIn(document.RootElement, depth: 0) is { } found)
            {
                problem = found;
                return false;
            }

            var compact = Compact(document.RootElement);
            if (Encoding.UTF8.GetByteCount(compact) > MaxBytes)
            {
                problem = JsonValueProblem.TooLarge;
                return false;
            }

            value = new JsonFlagValue(compact);
            return true;
        }
    }

    /// <exception cref="ArgumentException">O texto não é um valor de flag JSON válido.</exception>
    public static JsonFlagValue Parse(string json) =>
        TryParse(json, out var value, out var problem)
            ? value
            : throw new ArgumentException($"Not a valid json flag value ({problem}).", nameof(json));

    private static bool TryRead(string json, [NotNullWhen(true)] out JsonDocument? document)
    {
        try
        {
            document = JsonDocument.Parse(json, _reading);
            return true;
        }
        catch (JsonException)
        {
            // Texto que não é JSON é exatamente o que este método recusa; o motivo volta como NotJson.
            document = null;
            return false;
        }
    }

    /// <param name="depth">Quantos objetos e listas envolvem <paramref name="element"/>.</param>
    private static JsonValueProblem? ProblemIn(JsonElement element, int depth) => element.ValueKind switch
    {
        JsonValueKind.Object => ProblemInObject(element, depth + 1),
        JsonValueKind.Array => ProblemInArray(element, depth + 1),
        JsonValueKind.Number when !(element.TryGetDouble(out var number) && double.IsFinite(number)) => JsonValueProblem.NumberOutOfRange,
        _ => null,
    };

    private static JsonValueProblem? ProblemInObject(JsonElement element, int level)
    {
        if (level > MaxDepth)
        {
            return JsonValueProblem.TooDeep;
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!keys.Add(property.Name))
            {
                return JsonValueProblem.DuplicateKey;
            }

            if (ProblemIn(property.Value, level) is { } problem)
            {
                return problem;
            }
        }

        return null;
    }

    private static JsonValueProblem? ProblemInArray(JsonElement element, int level)
    {
        if (level > MaxDepth)
        {
            return JsonValueProblem.TooDeep;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (ProblemIn(item, level) is { } problem)
            {
                return problem;
            }
        }

        return null;
    }

    private static string Compact(JsonElement element)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, _compact))
        {
            element.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
```

Se o analyzer reclamar de `JsonValueProblem.NotJson` como valor inicial de `problem`, mantenha: o contrato do
`out` é "só tem sentido quando o retorno é falso".

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test --project tests/ToggleForge.Domain.Tests -c Release --filter-class "*JsonFlagValueTests"`
Expected: PASS. Se `1e-400` for recusado (alguma versão do .NET devolve falso em `TryGetDouble` para underflow),
pare e reporte: o teste fixa que underflow vira 0 e é aceito.

- [ ] **Step 5: Sabotar**

Cada mutação, sozinha, derruba um teste; desfaça depois:
- `if (level > MaxDepth)` → `if (level > MaxDepth + 1)` nos dois métodos;
- remover o `if (!keys.Add(...))`;
- `double.IsFinite(number)` → `true`;
- `> MaxBytes` → `>= MaxBytes`;
- `_compact` sem o `Encoder` (o teste de `ação` passa a falhar, e o de `ç` também).

- [ ] **Step 6: Commit**

```bash
dotnet format ToggleForge.slnx --verify-no-changes
git add src/ToggleForge.Domain/Authoring/JsonFlagValue.cs src/ToggleForge.Domain/Authoring/JsonValueProblem.cs tests/ToggleForge.Domain.Tests/Authoring/JsonFlagValueTests.cs
git commit -m "feat(domain): add the json flag value with its limits

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Tarefa 7: contrato OpenAPI das flags JSON

Só `docs/api/openapi.yaml`. É o spec-first da fatia: o código da Tarefa 9 se ajusta a ele.

**Files:**
- Modify: `docs/api/openapi.yaml`

**Interfaces:**
- Produces: o contrato que a Tarefa 9 implementa e que `ApiContract` confere em cada resposta dos testes.

- [ ] **Step 1: Schemas**

Em `components/schemas`:

1. Novo `FlagType`:

```yaml
    FlagType:
      type: string
      enum: [boolean, json]
      description: Chosen when the flag is created and never changes (ADR 0011).
```

2. Novo `FlagValue`, sem `type`, porque aceita qualquer valor JSON:

```yaml
    FlagValue:
      description: |
        `true` or `false` for a boolean flag. Any JSON value for a json flag: at most 4 KiB in UTF-8 once
        compacted, at most 8 levels deep, no key repeated in an object and every number within the range of a
        double. The server returns it compacted, with the keys in the order they were sent.
```

3. `FlagCreation`: acrescente `type` (`$ref: '#/components/schemas/FlagType'`, com
   `description: Defaults to boolean.`), fora de `required`. Troque `onValue` e `offValue` por
   `$ref: '#/components/schemas/FlagValue'`.
4. `FlagModification`: `onValue` e `offValue` passam a `$ref: '#/components/schemas/FlagValue'`. `type` **não**
   entra, e `additionalProperties: false` continua recusando-o.
5. `Flag`: acrescente `type` (`$ref: FlagType`) e o ponha em `required`. Os valores passam a `FlagValue`.
6. `SnapshotFlag`: acrescente `type` como string **fora** de `required`, com a descrição
   `Missing means boolean. Clients must skip a flag whose type they do not know.`, e sem `enum`, porque um SDK
   antigo precisa aceitar tipos futuros. Os valores passam a `FlagValue`.
7. `SimulationResult`: acrescente `type` (`$ref: FlagType`, em `required`), e `value` passa a `FlagValue`.

- [ ] **Step 2: Respostas e operações**

1. Nova resposta compartilhada em `components/responses`, no estilo das existentes:

```yaml
    PayloadTooLarge:
      description: The body is over the limit of the operation; nothing was saved.
      content:
        application/problem+json:
          schema:
            $ref: '#/components/schemas/Problem'
```

2. `createFlag`: acrescente `'413': $ref: '#/components/responses/PayloadTooLarge'`. Na descrição, troque
   "409 `limit-reached` at 200 flags" por "409 `limit-reached` at 200 flags or at 128 KiB of json values in the
   draft". Acrescente "Bodies over 32 KiB get 413."
3. `updateFlag`: acrescente `'409': $ref: '#/components/responses/Conflict'` e `'413'`. Na descrição, ponha:
   "409 `limit-reached` when the change would take the draft over 128 KiB of json values. The type of a flag
   never changes: `type` is an unknown field here. Bodies over 32 KiB get 413."

- [ ] **Step 3: Lint**

Run: `npm ci --prefix tools/api-lint && npm run lint --prefix tools/api-lint`
Expected: sem erros nem avisos. Se uma regra OWASP reclamar do `FlagValue` sem `type`, não a desligue no
ruleset geral: use um `overrides` no `.spectral.yaml` restrito ao caminho do `FlagValue`, com a justificativa ao
lado ("qualquer valor JSON; os limites são conferidos no servidor e descritos no schema"), como as regras
desligadas existentes.

- [ ] **Step 4: Commit**

```bash
git add docs/api/openapi.yaml tools/api-lint/.spectral.yaml
git commit -m "docs(api): describe json flags in the contract

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

Os testes de integração vão falhar contra este contrato até a Tarefa 9 (as respostas ainda não trazem `type`).
É esperado, e o PR só sai com a Tarefa 9.

### Tarefa 8: modelo de valores e persistência

**Files:**
- Create: `src/ToggleForge.Domain/Authoring/FlagType.cs`
- Create: `src/ToggleForge.Domain/Authoring/FlagValues.cs` (`FlagValues`, `BooleanValues`, `JsonValues`)
- Modify: `src/ToggleForge.Domain/Authoring/FlagSettings.cs`, `FlagChange.cs`, `DraftFlag.cs`, `PublishedFlag.cs`,
  `ProjectEnvironment.cs`, `AuthoringFailures.cs`
- Modify: `src/ToggleForge.Application/Authoring/SimulationService.cs`, `Views.cs`
- Modify: `src/ToggleForge.Infrastructure/Authoring/AuthoringDbContext.cs`, `PublicationFlagRow.cs`,
  `PublicationRepository.cs`; `src/ToggleForge.Infrastructure/Persistence/ValueConverters.cs`, `SqlChecks.cs`
- Delete and regenerate: `src/ToggleForge.Infrastructure/Authoring/Migrations/*`
- Modify (só para compilar, mesmo comportamento booleano): `src/ToggleForge.Api/Endpoints/DraftEndpoints.cs`,
  `FlagEndpoints.cs`, `src/ToggleForge.Api/Contracts/Responses.cs`
- Modify: `docs/adr/0011-configuracoes-json.md` (seção "Domínio e banco")
- Test: `tests/ToggleForge.Domain.Tests/Authoring/FlagValuesTests.cs` (novo), `JsonBudgetTests.cs` (novo),
  `Flags.cs`, `FlagEditingTests.cs`, `DraftTests.cs`, `RollbackTests.cs`, `DraftComparisonTests.cs` (ajustes);
  `tests/ToggleForge.Api.Tests/FlagStorageTests.cs` (novo)

**Interfaces:**
- Consumes: `JsonFlagValue` (Tarefa 6); `BooleanFlag`, `JsonFlag`, `Flag`, `PercentageRollout` do núcleo.
- Produces:
  - `public enum FlagType { Boolean, Json }`.
  - `public abstract record FlagValues` com `abstract FlagType Type`, `abstract JsonFlagValue OnValue`,
    `abstract JsonFlagValue OffValue`, `static Result<FlagValues> Of(FlagType type, JsonFlagValue onValue, JsonFlagValue offValue)`,
    `static FlagValues Restore(FlagType type, JsonFlagValue onValue, JsonFlagValue offValue)` (lança
    `InvalidDataException`; só reidratação) e `internal Result<FlagValues> With(JsonFlagValue? onValue, JsonFlagValue? offValue)`.
  - `public sealed record BooleanValues(bool On, bool Off) : FlagValues`.
  - `public sealed record JsonValues : FlagValues` com construtor `(JsonFlagValue onValue, JsonFlagValue offValue)`
    e `int ByteCount`.
  - `FlagSettings(bool IsEnabled, FlagValues Values, RolloutPercentage Rollout)` com `FlagType Type`.
  - `FlagChange(bool? IsEnabled = null, JsonFlagValue? OnValue = null, JsonFlagValue? OffValue = null, RolloutPercentage? Rollout = null)`
    com `internal Result<FlagSettings> ApplyTo(FlagSettings current)`.
  - `PublishedFlag.ToFlag()` devolvendo `Flag` (substitui `ToBooleanFlag`).
  - `ProjectEnvironment.MaxJsonBytes = 128 * 1024`.
  - `AuthoringFailures.OnValueNotOfFlagType`, `OffValueNotOfFlagType` (`InvalidValue`,
    `"value-not-of-flag-type"`) e `JsonBudgetReached` (`Conflict`, `"limit-reached"`).
  - `SimulationResultView(FlagKey FlagKey, FlagType Type, string Value, EvaluationReason Reason, int? Bucket)`,
    em que `Value` é texto JSON.

- [ ] **Step 1: Testes de domínio que falham**

`tests/ToggleForge.Domain.Tests/Authoring/FlagValuesTests.cs`:

```csharp
using ToggleForge.Domain.Authoring;

namespace ToggleForge.Domain.Tests.Authoring;

public sealed class FlagValuesTests
{
    [Fact]
    public void A_boolean_flag_reads_true_and_false()
    {
        Assert.True(FlagValues.Of(FlagType.Boolean, JsonFlagValue.True, JsonFlagValue.False).TryGetValue(out var values, out _));
        Assert.Equal(new BooleanValues(On: true, Off: false), values);
    }

    [Theory]
    [InlineData("1", "false", true)]
    [InlineData("\"true\"", "false", true)]
    [InlineData("true", "null", false)]
    [InlineData("true", "{}", false)]
    public void A_boolean_flag_refuses_anything_else_on_the_side_that_has_it(string on, string off, bool onIsWrong)
    {
        Assert.False(FlagValues.Of(FlagType.Boolean, JsonFlagValue.Parse(on), JsonFlagValue.Parse(off)).TryGetValue(out _, out var failure));
        Assert.Equal(onIsWrong ? AuthoringFailures.OnValueNotOfFlagType : AuthoringFailures.OffValueNotOfFlagType, failure);
    }

    [Fact]
    public void A_json_flag_takes_any_json_value_including_true_and_null()
    {
        Assert.True(FlagValues.Of(FlagType.Json, JsonFlagValue.True, JsonFlagValue.Parse("null")).TryGetValue(out var values, out _));
        var json = Assert.IsType<JsonValues>(values);
        Assert.Equal(("true", "null"), (json.OnValue.Text, json.OffValue.Text));
        Assert.Equal(FlagType.Json, json.Type);
    }

    [Fact]
    public void Json_values_count_the_bytes_of_both_sides()
    {
        var values = new JsonValues(JsonFlagValue.Parse("{\"discount\":0.2}"), JsonFlagValue.Parse("{}"));

        Assert.Equal(18, values.ByteCount);
    }

    [Fact]
    public void Boolean_values_are_stored_as_the_json_literals()
    {
        var values = new BooleanValues(On: false, Off: true);

        Assert.Equal((JsonFlagValue.False, JsonFlagValue.True), (values.OnValue, values.OffValue));
    }

    [Fact]
    public void Restore_refuses_a_stored_boolean_flag_with_a_json_value() =>
        Assert.Throws<InvalidDataException>(() => FlagValues.Restore(FlagType.Boolean, JsonFlagValue.Parse("3"), JsonFlagValue.False));
}
```

(`{"discount":0.2}` tem 16 bytes e `{}` tem 2: total 18.)

`tests/ToggleForge.Domain.Tests/Authoring/JsonBudgetTests.cs`. Use os helpers de `Flags.cs`, `NewEnvironment()`
inclusive:

```csharp
using ToggleForge.Domain.Authoring;
using ToggleForge.Evaluation;

namespace ToggleForge.Domain.Tests.Authoring;

/// <summary>O teto de 128 KiB de valores JSON por rascunho (ADR 0011), que protege cada download do snapshot.</summary>
public sealed class JsonBudgetTests
{
    // 16 flags com dois valores de 4 KiB somam exatamente 128 KiB.
    private const int FlagsThatFillTheBudget = 16;

    [Fact]
    public void A_draft_holds_exactly_128_KiB_of_json_values()
    {
        var environment = Flags.NewEnvironment();

        for (var flag = 1; flag <= FlagsThatFillTheBudget; flag++)
        {
            Assert.True(environment.AddFlag(FlagKey.Parse($"config-{flag}"), FullJsonSettings(), RolloutSeed.Parse("s1")).TryGetValue(out _, out _));
        }
    }

    [Fact]
    public void One_more_byte_of_json_is_refused_and_the_revision_stays()
    {
        var environment = FullEnvironment();
        var revision = environment.DraftRevision;

        var added = environment.AddFlag(FlagKey.Parse("one-more"), JsonSettings("0", "0"), RolloutSeed.Parse("s1"));

        Assert.False(added.TryGetValue(out _, out var failure));
        Assert.Equal(AuthoringFailures.JsonBudgetReached, failure);
        Assert.Equal(revision, environment.DraftRevision);
    }

    [Fact]
    public void Boolean_flags_do_not_count_toward_the_budget() =>
        Assert.True(FullEnvironment().AddFlag(FlagKey.Parse("new-shop"), Flags.Settings(), RolloutSeed.Parse("s1")).TryGetValue(out _, out _));

    [Fact]
    public void A_change_that_grows_a_value_past_the_budget_is_refused()
    {
        var environment = Flags.NewEnvironment();
        for (var flag = 1; flag < FlagsThatFillTheBudget; flag++)
        {
            environment.AddFlag(FlagKey.Parse($"config-{flag}"), FullJsonSettings(), RolloutSeed.Parse("s1"));
        }

        environment.AddFlag(FlagKey.Parse("growing"), JsonSettings(Full, "0"), RolloutSeed.Parse("s1"));
        environment.AddFlag(FlagKey.Parse("tiny"), JsonSettings("0", "0"), RolloutSeed.Parse("s1"));
        // 15 × 8.192 + 4.097 + 2 = 126.979 bytes: sobram 4.093, e trocar "0" por 4.096 bytes soma 4.095.
        var growing = FlagKey.Parse("growing");
        var revision = environment.Flags.Single(flag => flag.Key == growing).ChangedAtRevision;

        var changed = environment.ChangeFlag(growing, revision, new FlagChange(OffValue: JsonFlagValue.Parse(Full)));

        Assert.False(changed.TryGetValue(out _, out var failure));
        Assert.Equal(AuthoringFailures.JsonBudgetReached, failure);
    }

    [Fact]
    public void A_change_that_shrinks_a_value_is_accepted_when_the_draft_is_full()
    {
        var environment = FullEnvironment();
        var key = FlagKey.Parse("config-1");
        var revision = environment.Flags.Single(flag => flag.Key == key).ChangedAtRevision;

        Assert.True(environment.ChangeFlag(key, revision, new FlagChange(OnValue: JsonFlagValue.Parse("0"))).TryGetValue(out _, out _));
    }

    private static ProjectEnvironment FullEnvironment()
    {
        var environment = Flags.NewEnvironment();
        for (var flag = 1; flag <= FlagsThatFillTheBudget; flag++)
        {
            environment.AddFlag(FlagKey.Parse($"config-{flag}"), FullJsonSettings(), RolloutSeed.Parse("s1"));
        }

        return environment;
    }

    // Um texto JSON de 4.096 bytes: aspas mais 4.094 caracteres.
    private static readonly string Full = "\"" + new string('x', 4094) + "\"";

    private static FlagSettings FullJsonSettings() => JsonSettings(Full, Full);

    private static FlagSettings JsonSettings(string on, string off) =>
        new(IsEnabled: true, new JsonValues(JsonFlagValue.Parse(on), JsonFlagValue.Parse(off)), RolloutPercentage.All);
}
```

Em `FlagEditingTests` acrescente:
- `Changing_a_boolean_flag_to_a_value_that_is_not_true_or_false_is_refused_and_the_revision_stays`
  (`new FlagChange(OnValue: JsonFlagValue.Parse("\"yes\""))` → `OnValueNotOfFlagType`, `DraftRevision` igual);
- `A_json_flag_takes_a_new_value_of_any_json_type` (flag JSON com `{}`, muda `OnValue` para `3` → aceito, o
  `Settings.Values` passa a ser `JsonValues` com `OnValue.Text == "3"`).

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet build tests/ToggleForge.Domain.Tests -c Release`
Expected: FAIL de compilação (os tipos novos não existem).

- [ ] **Step 3: Domínio**

`FlagType.cs`:

```csharp
namespace ToggleForge.Domain.Authoring;

/// <summary>Escolhido na criação da flag e imutável: um build publicado lê a flag com uma classe dele (ADR 0011).</summary>
public enum FlagType
{
    Boolean,
    Json,
}
```

`FlagValues.cs`:

```csharp
namespace ToggleForge.Domain.Authoring;

/// <summary>
/// Os valores ligado e desligado de uma flag, sempre do mesmo tipo. Todo valor chega como <see cref="JsonFlagValue"/>,
/// porque <c>true</c> também é um valor válido numa flag JSON: é o tipo da flag que diz como lê-lo.
/// </summary>
public abstract record FlagValues
{
    private protected FlagValues()
    {
    }

    public abstract FlagType Type { get; }

    public abstract JsonFlagValue OnValue { get; }

    public abstract JsonFlagValue OffValue { get; }

    public static Result<FlagValues> Of(FlagType type, JsonFlagValue onValue, JsonFlagValue offValue)
    {
        ArgumentNullException.ThrowIfNull(onValue);
        ArgumentNullException.ThrowIfNull(offValue);
        return type switch
        {
            FlagType.Boolean => BooleanValues.From(onValue, offValue),
            FlagType.Json => new JsonValues(onValue, offValue),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown flag type."),
        };
    }

    /// <exception cref="InvalidDataException">O que foi guardado não forma um par válido do tipo.</exception>
    public static FlagValues Restore(FlagType type, JsonFlagValue onValue, JsonFlagValue offValue) =>
        Of(type, onValue, offValue).TryGetValue(out var values, out var failure)
            ? values
            : throw new InvalidDataException($"Stored {type} flag values are invalid: {failure.Detail}");

    /// <summary>O par com os lados dados trocados; nulo mantém o atual, e o tipo nunca muda.</summary>
    internal Result<FlagValues> With(JsonFlagValue? onValue, JsonFlagValue? offValue) =>
        Of(Type, onValue ?? OnValue, offValue ?? OffValue);
}

public sealed record BooleanValues(bool On, bool Off) : FlagValues
{
    public override FlagType Type => FlagType.Boolean;

    public override JsonFlagValue OnValue => On ? JsonFlagValue.True : JsonFlagValue.False;

    public override JsonFlagValue OffValue => Off ? JsonFlagValue.True : JsonFlagValue.False;

    internal static Result<FlagValues> From(JsonFlagValue onValue, JsonFlagValue offValue)
    {
        if (onValue.AsBoolean is not { } on)
        {
            return AuthoringFailures.OnValueNotOfFlagType;
        }

        if (offValue.AsBoolean is not { } off)
        {
            return AuthoringFailures.OffValueNotOfFlagType;
        }

        return new BooleanValues(on, off);
    }
}

public sealed record JsonValues : FlagValues
{
    public JsonValues(JsonFlagValue onValue, JsonFlagValue offValue)
    {
        OnValue = onValue ?? throw new ArgumentNullException(nameof(onValue));
        OffValue = offValue ?? throw new ArgumentNullException(nameof(offValue));
    }

    public override FlagType Type => FlagType.Json;

    public override JsonFlagValue OnValue { get; }

    public override JsonFlagValue OffValue { get; }

    /// <summary>Os bytes dos dois valores, que contam para o teto do rascunho.</summary>
    public int ByteCount => OnValue.ByteCount + OffValue.ByteCount;
}
```

`FlagSettings.cs`:

```csharp
namespace ToggleForge.Domain.Authoring;

/// <summary>O que o Editor configura numa flag; a chave e a seed ficam de fora porque não mudam.</summary>
public sealed record FlagSettings(bool IsEnabled, FlagValues Values, RolloutPercentage Rollout)
{
    public FlagType Type => Values.Type;
}
```

(Mantenha os `using` que o arquivo já tem; `RolloutPercentage` vem de `ToggleForge.Evaluation`.)

`FlagChange.cs`:

```csharp
/// <summary>O que o Editor muda numa flag. Nulo mantém o valor atual; a chave, a seed e o tipo nunca mudam.</summary>
public sealed record FlagChange(
    bool? IsEnabled = null, JsonFlagValue? OnValue = null, JsonFlagValue? OffValue = null, RolloutPercentage? Rollout = null)
{
    internal Result<FlagSettings> ApplyTo(FlagSettings current)
    {
        if (!current.Values.With(OnValue, OffValue).TryGetValue(out var values, out var failure))
        {
            return failure;
        }

        return new FlagSettings(IsEnabled ?? current.IsEnabled, values, Rollout ?? current.Rollout);
    }
}
```

`DraftFlag.cs`: troque os campos `bool OnValue`/`bool OffValue` por `FlagType Type`, `JsonFlagValue OnValue` e
`JsonFlagValue OffValue` (todos com `private set`, exceto `Type`, que é só leitura). O construtor privado ganha
`FlagType type` depois de `isEnabled`, e os parâmetros `onValue`/`offValue` passam a `JsonFlagValue`. Os nomes
dos parâmetros continuam iguais aos das propriedades, porque é com esse construtor que o EF materializa a
entidade. Ficam assim:

```csharp
    public FlagSettings Settings => new(IsEnabled, FlagValues.Restore(Type, OnValue, OffValue), Rollout);

    internal static DraftFlag Create(FlagKey key, FlagSettings settings, RolloutSeed seed, long revision) =>
        new(key, settings.IsEnabled, settings.Type, settings.Values.OnValue, settings.Values.OffValue, settings.Rollout, seed, revision);

    internal void Change(FlagSettings settings, long revision)
    {
        if (settings.Type != Type)
        {
            throw new InvalidOperationException("The type of a flag never changes.");
        }

        IsEnabled = settings.IsEnabled;
        OnValue = settings.Values.OnValue;
        OffValue = settings.Values.OffValue;
        Rollout = settings.Rollout;
        ChangedAtRevision = revision;
    }
```

`PublishedFlag.cs`:

```csharp
public sealed record PublishedFlag(FlagKey Key, FlagSettings Settings, RolloutSeed Seed)
{
    public Flag ToFlag()
    {
        var rollout = new PercentageRollout(Settings.Rollout, Seed);
        return Settings.Values switch
        {
            BooleanValues values => new BooleanFlag(Key, Settings.IsEnabled, values.On, values.Off, rollout),
            JsonValues values => new JsonFlag(Key, Settings.IsEnabled, values.OnValue.Text, values.OffValue.Text, rollout),
            var unknown => throw new InvalidOperationException($"Unknown flag values '{unknown.GetType().Name}'."),
        };
    }
}
```

`AuthoringFailures.cs`, junto das falhas de flag:

```csharp
    public static Failure OnValueNotOfFlagType { get; } =
        new(FailureKind.InvalidValue, "value-not-of-flag-type", "The on value of a boolean flag must be true or false.");

    public static Failure OffValueNotOfFlagType { get; } =
        new(FailureKind.InvalidValue, "value-not-of-flag-type", "The off value of a boolean flag must be true or false.");

    public static Failure JsonBudgetReached { get; } =
        new(FailureKind.Conflict, "limit-reached", $"A draft can hold at most {ProjectEnvironment.MaxJsonBytes / 1024} KiB of json values.");
```

O endpoint compara a falha recebida com cada uma para apontar `/onValue` ou `/offValue`; os `Detail` diferentes
as mantêm distintas pela igualdade de record.

`ProjectEnvironment.cs`:
- `public const int MaxJsonBytes = 128 * 1024;`, ao lado de `MaxFlags`.
- Em `AddFlag`, depois da checagem de `MaxFlags` e antes de `DraftRevision++`:
  `if (JsonBytes + JsonBytesOf(settings) > MaxJsonBytes) return AuthoringFailures.JsonBudgetReached;`.
- Em `ChangeFlag`, troque `var settings = change.ApplyTo(flag.Settings);` por:

```csharp
        if (!change.ApplyTo(flag.Settings).TryGetValue(out var settings, out var invalid))
        {
            return invalid;
        }

        if (settings == flag.Settings)
        {
            return flag;
        }

        if (JsonBytes - JsonBytesOf(flag.Settings) + JsonBytesOf(settings) > MaxJsonBytes)
        {
            return AuthoringFailures.JsonBudgetReached;
        }
```

- Novos membros privados:

```csharp
    /// <summary>Os bytes de valores JSON do rascunho; o lock do ambiente na edição impede que dois editores passem juntos do teto.</summary>
    private int JsonBytes => _flags.Sum(flag => JsonBytesOf(flag.Settings));

    private static int JsonBytesOf(FlagSettings settings) => settings.Values is JsonValues json ? json.ByteCount : 0;
```

`ReplaceDraft` e o rollback não checam o teto: o conteúdo restaurado já coube quando foi publicado.

- [ ] **Step 4: Ajustar os testes de domínio existentes**

- `Flags.Settings(...)` passa a `new FlagSettings(isEnabled, new BooleanValues(On: true, Off: false), percentage)`.
- Todo `new FlagChange(OnValue: true)` passa a `OnValue: JsonFlagValue.True`; `false` passa a `JsonFlagValue.False`.
- Leituras de `flag.OnValue` como `bool` passam a `Assert.IsType<BooleanValues>(flag.Settings.Values).On`.

Run: `dotnet test --project tests/ToggleForge.Domain.Tests -c Release`
Expected: PASS.

- [ ] **Step 5: Application**

`Views.cs`: `SimulationResultView(FlagKey FlagKey, FlagType Type, string Value, EvaluationReason Reason, int? Bucket)`,
com `Value` em texto JSON. Remova o `Of(FlagKey, EvaluationResult<bool>)`. Em `SimulationService`, troque o
`Evaluate` atual por:

```csharp
    private static SimulationResultView Evaluate(PublishedFlag flag, EnvironmentLocator locator, EvaluationContext player) =>
        flag.ToFlag() switch
        {
            BooleanFlag boolean => View(flag, boolean.Evaluate(locator.ProjectKey, locator.EnvironmentKey, player, defaultValue: false), value => value ? "true" : "false"),
            // A simulação sempre tem jogador e o parser não lança: o valor padrão nunca sai daqui.
            JsonFlag json => View(flag, json.Evaluate(locator.ProjectKey, locator.EnvironmentKey, player, defaultValue: "null", text => text), text => text),
            var unknown => throw new InvalidOperationException($"Unknown flag '{unknown.GetType().Name}'."),
        };

    private static SimulationResultView View<T>(PublishedFlag flag, EvaluationResult<T> result, Func<T, string> json) =>
        new(flag.Key, flag.Settings.Type, json(result.Value), result.Reason, result.Bucket);
```

Ajuste quem chamava `SimulationResultView.Of`.

- [ ] **Step 6: Infraestrutura e migration regenerada**

`ValueConverters.cs`, no padrão dos existentes (parse inválido lança `InvalidDataException`):

```csharp
    public static ValueConverter<FlagType, string> FlagType { get; } = new(type => FlagTypeText(type), text => ParseFlagType(text));

    public static ValueConverter<JsonFlagValue, string> JsonFlagValue { get; } = new(value => value.Text, text => ParseJsonFlagValue(text));
```

com `FlagTypeText`: `Boolean → "boolean"` e `Json → "json"` (switch com `default` que lança), `ParseFlagType`
inverso, e `ParseJsonFlagValue` via `Domain.Authoring.JsonFlagValue.TryParse`. Use os nomes totalmente
qualificados se as propriedades estáticas sombrearem os tipos.

`SqlChecks.cs`:

```csharp
    public static string FlagType(string column) => $"{column} in ('boolean', 'json')";

    /// <summary>Uma flag booleana só guarda os literais true e false (ADR 0011).</summary>
    public static string BooleanValues(string type, string onValue, string offValue) =>
        $"{type} <> 'boolean' or (json_typeof({onValue}) = 'boolean' and json_typeof({offValue}) = 'boolean')";
```

`AuthoringDbContext.cs`, em `draft_flags`:
- CHECKs novos `ck_draft_flags_type` (`SqlChecks.FlagType("type")`) e `ck_draft_flags_boolean_values`
  (`SqlChecks.BooleanValues("type", "on_value", "off_value")`);
- `flag.Property(f => f.Type).HasConversion(ValueConverters.FlagType);`;
- `flag.Property(f => f.OnValue).HasConversion(ValueConverters.JsonFlagValue).HasColumnType("json");`, e o
  mesmo para `OffValue`.

Em `publication_flags`, os mesmos dois CHECKs (`ck_publication_flags_type`, `ck_publication_flags_boolean_values`)
e as mesmas conversões, aplicadas a `PublicationFlagRow`. A row troca `bool OnValue/OffValue` por
`required FlagType Type`, `required JsonFlagValue OnValue` e `required JsonFlagValue OffValue`. Em
`PublicationRepository`:
- `ToRow` grava `Type = flag.Settings.Type`, `OnValue = flag.Settings.Values.OnValue` e
  `OffValue = flag.Settings.Values.OffValue`;
- `ToDomain` lê com `FlagValues.Restore(row.Type, row.OnValue, row.OffValue)`.

Regenere a migration (escolha do usuário, ver as decisões da fatia):

```bash
git rm -q src/ToggleForge.Infrastructure/Authoring/Migrations/*
dotnet tool restore
dotnet ef migrations add Initial --context AuthoringDbContext \
  --project src/ToggleForge.Infrastructure --startup-project src/ToggleForge.Api --output-dir Authoring/Migrations
```

Confira no arquivo gerado que:
- `on_value`/`off_value` são `json`, `type` é `text`;
- os quatro CHECKs novos estão lá, e os antigos continuam;
- nada mais mudou em relação ao `Initial` anterior além disso: compare com `git diff --stat` e leia o diff da
  migration.

Não edite o arquivo gerado.

- [ ] **Step 7: API compila com o mesmo comportamento booleano**

O contrato novo entra na Tarefa 9. Aqui a API só passa a falar a forma nova do domínio:
- `DraftEndpoints.Settings`: `new FlagSettings(enabled, new BooleanValues(onValue, offValue), rollout)`;
- `FlagEndpoints.Change`: `request.OnValue.IsPresent ? (request.OnValue.Value ? JsonFlagValue.True : JsonFlagValue.False) : null`,
  e o mesmo para `OffValue`;
- `FlagResponse.Of` e `SnapshotFlagResponse`: `settings.Values` é sempre `BooleanValues` aqui. Leia
  `((BooleanValues)settings.Values).On`. É temporário: a Tarefa 9 troca por JSON. Uma flag JSON ainda não tem
  como ser criada pela API;
- `SimulationResultResponse.Of`: `bool.Parse(view.Value)` (temporário, pelo mesmo motivo).
- Se `ChangeFlag` devolver `OnValueNotOfFlagType`, que não acontece com bool, `Problems.From` lança:
  aceitável até a Tarefa 9.

- [ ] **Step 8: Teste de persistência com Postgres real**

`tests/ToggleForge.Api.Tests/FlagStorageTests.cs`. Siga o padrão de SQL direto de `ApiFactory.AddMembershipAsync`.
Se não houver um helper que execute SQL e devolva o erro, crie um em `ApiFactory`
(`ExecuteSqlAsync(string sql)`) no mesmo estilo:

```csharp
public sealed class FlagStorageTests(ApiFactory api)
{
    [Fact]
    public async Task The_database_refuses_a_boolean_flag_that_stores_a_json_value()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");
        await owner.CreateFlagAsync("space-race", "new-shop");

        var error = await Assert.ThrowsAnyAsync<Npgsql.PostgresException>(() =>
            api.ExecuteSqlAsync(UpdateNewShop(owner, "on_value = '3'")));

        Assert.Equal("ck_draft_flags_boolean_values", error.ConstraintName);
    }

    [Fact]
    public async Task The_database_refuses_an_unknown_flag_type()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");
        await owner.CreateFlagAsync("space-race", "new-shop");

        var error = await Assert.ThrowsAnyAsync<Npgsql.PostgresException>(() =>
            api.ExecuteSqlAsync(UpdateNewShop(owner, "type = 'color'")));

        Assert.Equal("ck_draft_flags_type", error.ConstraintName);
    }

    // Outras organizações também têm new-shop, e os testes rodam em paralelo: só a flag deste teste muda.
    private static string UpdateNewShop(ConsoleClient owner, string assignment) =>
        $"""
        update authoring.draft_flags flag set {assignment}
        from authoring.environments environment join authoring.projects project on project.id = environment.project_id
        where flag.environment_id = environment.id and project.organization_id = '{owner.OrganizationId}' and flag.key = 'new-shop'
        """;
}
```

Confira os nomes de coluna (`environment_id`, `project_id`, `organization_id`) no `Initial` regenerado.
Ajuste os parâmetros de `CreateFlagAsync` à assinatura real em `ConsoleClient`.

- [ ] **Step 9: ADR 0011**

Na seção "Domínio e banco", troque o item **Banco**, que fala em `USING to_json(...)`, por:

```markdown
- **Banco:** `type text not null` com CHECK `in ('boolean', 'json')`, `on_value`/`off_value` do tipo `json` (com
  `true`/`false` nas flags booleanas) e um CHECK de que uma flag `boolean` só guarda `true`/`false`. O Postgres não
  converte `boolean` em `json` sem `USING`, e as migrations são geradas, nunca editadas à mão: por escolha do
  usuário, a migration `Initial` do módulo Authoring foi **regenerada**, e todo banco de desenvolvimento existente
  é recriado (`drop` e `--migrate`). Só é aceitável porque ainda não há produção; depois do Marco 6, mudanças de
  esquema seguem expand/contract.
```

Atualize também a linha do plano das fatias, se citar a conversão.

- [ ] **Step 10: Rodar tudo**

Run: `dotnet build ToggleForge.slnx -c Release && dotnet test --solution ToggleForge.slnx -c Release`
Expected: build sem avisos. Testes: tudo PASS, **exceto** as validações de contrato de `Api.Tests` que esperam
`type` nas respostas, se a Tarefa 7 já estiver nesta branch. Nesse caso, liste no relatório exatamente quais
falham e por quê. A Tarefa 9 as fecha.

- [ ] **Step 11: Sabotar**

- Remover a checagem de teto em `AddFlag` (cai `One_more_byte_of_json_is_refused...`).
- Em `ChangeFlag`, trocar `- JsonBytesOf(flag.Settings)` por nada (cai o teste do encolhimento).
- Em `BooleanValues.From`, inverter as duas falhas (cai a teoria de lado errado).
- No `SqlChecks.BooleanValues`, trocar `<> 'boolean'` por `= 'json' or true` (cai o teste de persistência).

- [ ] **Step 12: Commits**

Dois commits, nesta ordem:
1. `feat(domain): model flag values as a closed boolean or json pair` (Domain, Domain.Tests, Application, ajustes
   de compilação da API);
2. `feat(infrastructure): store flag values as json with type checks` (Infrastructure, migration regenerada,
   FlagStorageTests, ADR 0011).

Se o primeiro commit não compilar sem a infraestrutura, junte os dois num só,
`feat: model flag values as a boolean or json pair and store them as json`, e explique no relatório.

### Tarefa 9: API das flags JSON

**Files:**
- Create: `src/ToggleForge.Api/Http/FlagTypes.cs`
- Modify: `src/ToggleForge.Api/Contracts/Requests.cs`, `Responses.cs`
- Modify: `src/ToggleForge.Api/Http/FlagInput.cs`, `UnexpectedErrors.cs`, `Problems.cs`
- Modify: `src/ToggleForge.Api/Endpoints/DraftEndpoints.cs`, `FlagEndpoints.cs`
- Test: `tests/ToggleForge.Api.Tests/JsonFlagTests.cs` (novo), `tests/ToggleForge.Api.Tests/LimitTests.cs`,
  `tests/ToggleForge.Api.Tests/Support/ConsoleClient.cs`

**Interfaces:**
- Consumes: o contrato da Tarefa 7; `JsonFlagValue`, `JsonValueProblem`, `FlagValues.Of`, `FlagType`, as falhas novas
  e `SimulationResultView` com `Type`/`Value` (Tarefas 6 e 8).
- Produces: o contrato HTTP que o SDK (Fatia 5) e o console (Fatia 6) consomem.

- [ ] **Step 1: Testes de integração que falham**

`tests/ToggleForge.Api.Tests/JsonFlagTests.cs`. Use `ConsoleClient.SendRawAsync` para corpos montados à mão e
acrescente ao `ConsoleClient` um `CreateJsonFlagAsync(projectKey, flagKey, string onValueJson, string offValueJson, int rolloutBasisPoints = 10_000)`.
Cada teste cria a própria conta. Os testes, com o que cada um afirma:

1. `A_json_flag_is_created_with_its_values_compacted`: POST com `"type":"json"`, `onValue` =
   `{ "discount" : 0.2, "items" : ["sword"] }` → 201; corpo com `type == "json"`; `onValue` re-serializado
   compacto igual a `{"discount":0.2,"items":["sword"]}`; `offValue` igual ao enviado.
2. `A_flag_without_a_type_is_boolean`: o corpo de hoje (sem `type`) → 201 com `type == "boolean"`.
3. `A_json_flag_takes_true_null_and_scalars_as_values`: `onValue: true`, `offValue: null` numa flag `json` → 201,
   com os valores devolvidos iguais.
4. `A_boolean_flag_refuses_a_value_that_is_not_true_or_false` (Theory: `1`, `"true"`, `null`, `{}`) → 400 com
   ponteiro `/onValue` em `ErrorSources()` e rascunho vazio.
5. `An_invalid_json_value_is_rejected_on_its_field` (Theory com valores de 9 níveis, número `1e400` e uma string de
   4.095 caracteres) → 400 com ponteiro `/onValue`, rascunho vazio.
6. `A_duplicated_key_is_rejected_at_any_depth`: `onValue` `{"a":{"b":1,"b":2}}` → 400, rascunho vazio. Se o
   binding do System.Text.Json já recusar (`AllowDuplicateProperties = false` global) antes do domínio, a
   resposta é 400 `INVALID_BODY`. Afirme 400 e um ponteiro que começa com `/onValue`, e registre no relatório qual
   camada recusou.
7. `An_unknown_type_is_rejected`: `"type":"color"` → 400 com ponteiro `/type`.
8. `Changing_the_type_of_a_flag_is_rejected_and_nothing_is_saved` (canvas T4): PATCH `{"type":"json"}` numa flag
   booleana → 400 `INVALID_BODY`, ETag e corpo da flag iguais.
9. `A_json_flag_takes_a_new_value_through_a_patch`: PATCH `{"onValue":[1,2]}` com `If-Match` → 200, valor novo,
   ETag nova.
10. `A_patch_cannot_set_the_json_null_and_changes_nothing`: PATCH `{"offValue":null}` numa flag JSON → 400, ETag e
    corpo iguais. No merge patch, `null` quer dizer apagar o campo, e nenhum campo de flag pode ser apagado; uma
    flag JSON só recebe o JSON `null` na criação (decisão do coordenador, escrita no contrato).
11. `A_published_json_flag_reaches_the_snapshot_as_json`: cria, publica, credencial e `GameClient` → o snapshot
    traz `type: "json"` e os valores como JSON (não string), e a flag booleana traz `type: "boolean"`.
12. `The_simulation_returns_the_json_value_the_player_gets`: flag JSON com rollout 100% → `value` igual ao
    `onValue` (JSON) e `type: "json"`.
13. `Rolling_back_restores_the_json_value`: v1 com `{"a":1}`, v2 com `{"a":2}`, rollback para v1 → snapshot e
    rascunho com `{"a":1}`.
14. `A_draft_holds_at_most_128_KiB_of_json_values`: 16 flags com valores de 4.096 bytes → todas 201; a 17ª (valores
    `0`) → 409 `/problems/limit-reached`.
15. `A_change_past_the_json_budget_gets_409_and_changes_nothing`: rascunho cheio, PATCH que aumenta um valor →
    409 `limit-reached`, ETag igual.
16. `A_body_over_32_KiB_gets_413` (canvas D3): POST com corpo de 32 KiB + 1 byte (um JSON válido acolchoado com
    espaços) → 413, com corpo `application/problem+json`; rascunho vazio. E
    `A_body_of_exactly_32_KiB_is_read`: 32.768 bytes → não é 413 (201).

Em `LimitTests`, acrescente `Concurrent_creations_cannot_exceed_the_environment_json_budget` (canvas D4) no padrão
das corridas da classe:
- encha o rascunho até sobrar espaço para uma só flag de 8 KiB;
- dispare `Racers` criações simultâneas de flags de 8 KiB;
- afirme uma 201 e todas as outras 409 `/problems/limit-reached`.

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test --project tests/ToggleForge.Api.Tests -c Release --filter-class "*JsonFlagTests"`
Expected: FAIL (a API ainda é só booleana, e as respostas não batem com o contrato da Tarefa 7).

- [ ] **Step 3: Tipos e leitura na borda**

`Http/FlagTypes.cs`, o único lugar da API que traduz o tipo:

```csharp
internal static class FlagTypes
{
    public static string Name(FlagType type) => type switch
    {
        FlagType.Boolean => "boolean",
        FlagType.Json => "json",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown flag type."),
    };

    public static bool TryParse(string? name, out FlagType type)
    {
        switch (name)
        {
            case "boolean":
                type = FlagType.Boolean;
                return true;
            case "json":
                type = FlagType.Json;
                return true;
            default:
                type = default;
                return false;
        }
    }
}
```

`Contracts/Requests.cs`:

```csharp
internal sealed record FlagCreationRequest(
    string? Key, string? Type, bool? Enabled, JsonElement OnValue, JsonElement OffValue, int? RolloutBasisPoints);

internal sealed record FlagModificationRequest(
    PatchField<bool> Enabled, PatchField<JsonElement> OnValue, PatchField<JsonElement> OffValue, PatchField<int> RolloutBasisPoints)
```

Na criação, `JsonElement` não anulável distingue ausente (`ValueKind.Undefined`) de `null` (`ValueKind.Null`), que é
um valor válido numa flag JSON. No PATCH, `null` continua recusado em todos os campos (semântica do merge patch):
como `JsonElement` lê `null` como `ValueKind.Null` sem lançar, o endpoint precisa recusar `OnValue`/`OffValue`
presentes com `ValueKind.Null` como 400 no campo (ou o conversor, se for mais simples), e o teste 10 prova isso.
`An_invalid_patch_is_rejected_and_changes_nothing` já cobre `null` em `bool` e `int`.

`Http/FlagInput.cs`, ao lado de `Rollout`:

```csharp
    /// <returns>Nulo quando o valor falta ou é inválido; o erro já foi apontado no campo.</returns>
    public static JsonFlagValue? Value(JsonElement element, string pointer, InputErrors errors)
    {
        if (element.ValueKind == JsonValueKind.Undefined)
        {
            errors.MissingInBody(pointer);
            return null;
        }

        if (JsonFlagValue.TryParse(element.GetRawText(), out var value, out var problem))
        {
            return value;
        }

        errors.InBody(pointer, Detail(problem));
        return null;
    }
```

com `Detail` dando uma frase por `JsonValueProblem`, nos limites literais: "is not a JSON value", "repeats a key
in an object", "nests deeper than 8 levels", "has a number out of the range of a double", "is over 4 KiB once
compacted". Ajuste `MissingInBody` à assinatura real.

- [ ] **Step 4: Endpoints**

- `DraftEndpoints` (criação):
  - `type` ausente vira `FlagType.Boolean`; inválido → `errors.InBody("/type", "Use boolean or json.")`;
  - os valores passam por `FlagInput.Value`, depois por `FlagValues.Of(type, on, off)`;
  - `OnValueNotOfFlagType` → `errors.InBody("/onValue", failure.Detail)`, e `OffValueNotOfFlagType` → `/offValue`.
- `FlagEndpoints.Change`: os valores presentes passam por `FlagInput.Value`. O resultado de `ChangeFlag` com
  `OnValueNotOfFlagType`/`OffValueNotOfFlagType` vira 400 no campo, e as outras falhas seguem por `Problems.From`.
- Limite de corpo: `.WithMetadata(new RequestSizeLimitAttribute(FlagBodyLimitBytes))` em `createFlag` e
  `updateFlag`, com `private const int FlagBodyLimitBytes = 32 * 1024;` num lugar só, perto de `FlagInput`. Confira
  com o teste 16 que o minimal API respeita o metadata. Se não respeitar, use um endpoint filter que ajuste
  `IHttpMaxRequestBodySizeFeature.MaxRequestBodySize` antes da leitura do corpo.
- `UnexpectedErrors.BadRequest`: uma `BadHttpRequestException` com `StatusCode == 413` vira um problem 413
  (`type "/problems/payload-too-large"`, título "Payload too large"), não o 400 `INVALID_BODY` de hoje. Crie
  `Problems.PayloadTooLarge(int limitBytes)` com o detail "The body is over the limit of {limitBytes} bytes.".

- [ ] **Step 5: Respostas**

- `FlagResponse(string Key, string Type, bool Enabled, JsonElement OnValue, JsonElement OffValue, int RolloutBasisPoints)`,
  com `Of` lendo `FlagTypes.Name(settings.Type)` e os valores por um helper
  `JsonOf(JsonFlagValue value) => JsonSerializer.Deserialize<JsonElement>(value.Text)`. O mesmo helper serve
  para o snapshot e a simulação.
- `SnapshotFlagResponse(string Key, string Type, bool Enabled, JsonElement OnValue, JsonElement OffValue, int RolloutBasisPoints, string Seed)`.
- `SimulationResultResponse(string FlagKey, string Type, JsonElement Value, string Reason, int? Bucket)`.
- Remova os casts temporários da Tarefa 8.

- [ ] **Step 6: Rodar tudo**

Run: `dotnet build ToggleForge.slnx -c Release && dotnet test --solution ToggleForge.slnx -c Release`
Expected: tudo PASS, inclusive os testes antigos de flag booleana (os valores continuam `true`/`false`), as
jornadas do console (que só criam flags booleanas) e `SdkClientTests` (o SDK lê `type: "boolean"` e ignoraria
uma flag JSON até a Fatia 5).

- [ ] **Step 7: Sabotar**

- `FlagInput.Value` aceitando qualquer texto (pule o `TryParse`): cai o teste 5;
- tirar o `RequestSizeLimit`: cai o teste 16;
- `UnexpectedErrors` sem o caso 413: cai o teste 16;
- `FlagResponse` mandando o valor como string (`JsonOf` → `JsonSerializer.SerializeToElement(value.Text)`): cai o 1
  pela validação de contrato ou pelo assert;
- trocar `/onValue` por `/offValue` no mapeamento da falha: cai o 4.

- [ ] **Step 8: Commit**

```bash
dotnet format ToggleForge.slnx --verify-no-changes
git add src/ToggleForge.Api tests/ToggleForge.Api.Tests
git commit -m "feat(api): create, change, publish and simulate json flags

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

#### Adendo à Tarefa 9 (spike e revisão de segurança, 2026-10-04)

Fontes: o spike descartável `task9-spike.md` e a revisão de segurança `slice4-security-review.md`, os dois no
workspace do plano. Este adendo **prevalece** sobre o texto acima quando os dois divergirem.

**O que o spike mediu e muda o texto acima:**
- **Limite de corpo:** `RequestSizeLimitAttribute` como metadata é respeitado pelo **Kestrel**, mas o `TestServer`
  do `ApiFactory` o ignora (só loga um aviso). Além disso, o `RequestDelegateFactory` engole a exceção da leitura
  mesmo com `ThrowOnBadRequest`, e o 413 chega **vazio** ao `UseStatusCodePages`, nunca ao `UnexpectedErrors`.
  - O endpoint filter **não serve**, porque roda depois do binding.
  - **Decisão:** usar o limite nativo (`RequestSizeLimit` no endpoint) e mapear o 413 vazio para o problem
    `/problems/payload-too-large` no handler de `UseStatusCodePages` (`ApiPipeline`), ao lado do tratamento do 415.
  - Os testes de 413 rodam num host **Kestrel** (como o `BrowserHost`, ou um host Kestrel sem navegador em
    `Support/`), não no `TestServer`.
- **Chave repetida:** o binding (`AllowDuplicateProperties = false` global) recusa antes do domínio, com 400
  `INVALID_BODY`. No POST o ponteiro é `/onValue`; no PATCH o `Path` vem `$` e o ponteiro sai vazio. O teste 6 afirma
  400 `INVALID_BODY` com ponteiro `/onValue` no POST, sem o "se". O `DuplicateKey` do domínio fica como defesa em
  profundidade.
- **`null` no PATCH:** `PatchField<JsonElement>` entrega `IsPresent = true` e `ValueKind.Null`, sem lançar.
  - A recusa é no endpoint, com ponteiro `/onValue` ou `/offValue`; no conversor o ponteiro sairia vazio.
  - Corrija os comentários de `PatchField.cs`, que hoje afirmam que `null` "já falha aqui".
  - Sabotagem: tirar a recusa derruba o teste 10.
- **Saída:** o `JsonElement` vindo de `JsonSerializer.Deserialize<JsonElement>(text)` sai como JSON cru, com a ordem
  das chaves e o texto dos números preservados, e `ç` sai sem escape. Os asserts comparam o JSON re-serializado ou
  usam `JsonElement.DeepEquals`.

**Testes e correções a mais (revisão de segurança):**
- **I2:** `A_boolean_flag_refuses_a_patch_with_a_value_that_is_not_true_or_false`. É uma teoria com o campo
  (`onValue`/`offValue`) × o valor (`1`, `"true"`, `{}`). Afirma 400 `/problems/invalid-request`, ponteiro igual ao
  campo enviado e ETag e corpo inalterados. Estenda o teste 4 com casos em `offValue`. Sabotagem: tirar o mapeamento
  de `OnValueNotOfFlagType` no PATCH derruba o teste com 500.
- **I3:** testes num host Kestrel:
  - `A_body_over_32_KiB_gets_413`;
  - `A_chunked_body_over_32_KiB_gets_413` (sem `Content-Length`);
  - `A_body_of_exactly_32_KiB_is_read`;
  - `The_413_problem_says_only_the_limit`: as chaves são exatamente `type`, `title`, `status` e `detail`, e o corpo
    não traz `Exception`, `Kestrel` nem `at `.
  - Em todos: rascunho vazio e nenhum 500 nos logs.
- **I4:**
  - Em `IsolationTests`, o dono ganha uma flag `json` publicada com `{"secret":"CANARY-<guid>"}`, e toda operação da
    teoria afirma que o canário não aparece no corpo do intruso.
  - Casos novos:
    - `A_member_of_another_organization_gets_404_for_a_json_value_that_does_not_fit_the_flag`: PATCH
      `{"onValue":{}}` na flag booleana de A → 404, não 400;
    - `A_member_of_another_organization_gets_404_even_when_the_owner_draft_is_full`: 404, não 409.
  - Em `RoleTests`: `A_viewer_gets_403_for_a_json_value_that_does_not_fit_the_flag` (403, não 400), e viewer criando
    flag `json` → 403.
- **M3:**
  - `Unusual_but_valid_json_values_round_trip_unchanged`, uma teoria com `"\u0000"`, `"a\nb"`, `"😀"`, `" "`, `-0`,
    `1.7976931348623157e308`, `1e-320`, 8 níveis, `{"":1}` e chave com `/` e `~`. O valor faz POST → GET → publicação
    → snapshot e continua o mesmo (`JsonElement.DeepEquals`), sem nenhum 500.
  - `A_value_nested_past_the_reader_depth_gets_400`: 65 níveis → 400.
  - `A_body_that_is_not_utf8_gets_400`.
- **M4:** chame `FlagValues.Of` só com o tipo lido **e** os dois valores não nulos. Testes:
  - `A_flag_with_an_unknown_type_reports_only_the_type`: só `/type`;
  - `Every_invalid_field_of_a_creation_is_reported_together`: `/onValue` e `/offValue`, nunca 500.
- **M5:** `"type": null` → 400 `/type`. Leia `type` como `JsonElement` para distinguir ausente de `null`. Teste
  `A_null_type_is_rejected`.
- **M6 (decisão):** `UnexpectedErrors.ToPointer` corta o ponteiro no membro de topo do corpo (`/onValue`), para não
  refletir chaves escolhidas pelo cliente. Os DTOs da API são planos, então o membro de topo basta para achar o campo.
  Teste `An_invalid_body_inside_a_value_points_at_the_field`: `{"onValue":{"<b>x</b>":1,"<b>x</b>":2}}` → ponteiro
  exatamente `/onValue`, e o corpo sem `<b>`.
- **M7 (decisão):** o spike mostrou que a saída com `JsonElement` não reescapa `ç`. O download fica no tamanho
  contado, e não há conversor próprio.
- **M10:** em `LimitTests`, `Concurrent_changes_cannot_exceed_the_environment_json_budget`. São `Racers` PATCHes
  simultâneos em flags **diferentes**, com folga para um só aumento: um 200, os demais 409 `limit-reached`, e a soma
  final ≤ 131.072 bytes.
- **M11:** comentários de `PatchField.cs` corrigidos (ver o spike acima).
- **M13:** `A_patch_that_only_reformats_a_json_value_keeps_the_etag`: PATCH com `{ "a" : 1 }` numa flag com `{"a":1}`
  → 200, mesma ETag e `hasUnpublishedChanges` igual.

### Tarefa 10: revisão final e PR da Fatia 4

- [ ] Rebase sobre o `main`, suíte inteira (`dotnet test --solution ToggleForge.slnx -c Release`) e lint da spec.
- [ ] Revisão final da branch inteira (Opus), com a triagem dos minors adiados; uma rodada de correções e uma
  re-revisão restrita.
- [ ] PR "feat: flags JSON no domínio, no banco e na API". A descrição traz:
  - a escolha da migration regenerada e o aviso de recriar bancos de desenvolvimento;
  - a lacuna conhecida do console até a Fatia 6;
  - o mapa ameaça → teste do canvas (T4, T5, D3, D4).
- [ ] Esperar o CI e a palavra do usuário.

---

## Mapa das fatias 5 a 7

Cada uma ganha a sua seção detalhada neste documento quando começar.

### Fatia 5 — SDK

- `SnapshotReader`: caso `"json"` em `KindOf`, valores relidos como `JToken` e regravados com
  `Formatting.None`.
- `ToggleForgeClient.EvaluateJson<T>(string flagKey, EvaluationContext context, T defaultValue, Func<string, T> parse)`
  devolvendo `FlagResult<T>`, com o mesmo tratamento de cache expirado do `EvaluateBoolean`.
- `samples/ToggleForge.SdkDemo`: passo que publica `shop-config` e lê um record tipado; `--json` no modo ao vivo.
- Testes em `Sdk.Tests` e `SdkClientTests` (API real).

### Fatia 6 — console

- `FlagEditor.razor`: escolha do tipo na criação, áreas de texto JSON, aviso de que o valor chega a todo jogador.
- `EnvironmentDraft.razor`, `PublicationDetail.razor` e `Simulator.razor`: selo do tipo e prévia do JSON.
- `Console.Tests` (bUnit, incluindo valor com `<script>` exibido como texto) e uma jornada nova em
  `ConsoleJourneyTests`, com captura de tela descartável para conferir o visual.

### Fatia 7 — fechar o Marco 4

- `README.md` (SDK com `EvaluateJson<T>`), status do Marco 4 em `ToggleForge.md` e CLAUDE.md, se o layout mudar.
