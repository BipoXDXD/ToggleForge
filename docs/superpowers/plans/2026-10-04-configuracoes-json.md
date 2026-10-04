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

## Mapa das fatias 4 a 7

Cada uma ganha a sua seção detalhada neste documento quando começar.

### Fatia 4 — domínio, banco e API

- **Primeiro commit: o contrato.** `docs/api/openapi.yaml`:
  - `FlagCreation` com `type` (`boolean` | `json`, opcional, padrão `boolean`) e valores de qualquer JSON;
  - `FlagModification` sem `type`;
  - `Flag`, `SnapshotFlag` e `SimulationResult` com `type`;
  - 409 `limit-reached` também para os 128 KiB;
  - 413 nas rotas de flag.
  - `npm run lint --prefix tools/api-lint` verde.
- **Domínio (`src/ToggleForge.Domain/Authoring`):**
  - `JsonFlagValue` (System.Text.Json, `AllowDuplicateProperties = false`, `MaxDepth` 8, número fora do `double`
    recusado, compactação preservando a ordem, 4 KiB);
  - `FlagValues` fechado (`BooleanValues`, `JsonValues`);
  - `FlagSettings` e `FlagChange` com `FlagValues`;
  - `PublishedFlag.ToFlag()` devolvendo `Flag` do núcleo;
  - o teto de 128 KiB em `ProjectEnvironment`;
  - falhas novas em `AuthoringFailures`.
- **Infraestrutura:** mapeamento de `type`/`on_value`/`off_value` em `AuthoringDbContext` e `PublicationFlagRow`;
  migration nova gerada com `dotnet ef migrations add JsonFlags --context AuthoringDbContext ...`, com a
  conversão `USING to_json(...)` e os CHECKs.
- **API:** DTOs, limite de 32 KiB por corpo nas rotas de flag, `SimulationService` com `JsonFlag.Evaluate` e
  `json => json`, snapshot com os valores JSON crus.
- **Testes:** `Domain.Tests` (partições do `JsonFlagValue` e o teto) e `Api.Tests` (a lista da Compliance da
  ADR 0011, incluindo as criações concorrentes).

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
