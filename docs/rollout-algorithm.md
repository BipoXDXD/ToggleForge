# Algoritmo de rollout v1

Especificação normativa do bucket de rollout. A decisão e as alternativas estão em
`docs/adr/0002-algoritmo-de-rollout-v1.md`. Toda implementação (simulador, SDK C#, SDKs futuros em
outras linguagens) deve reproduzir exatamente os vetores de
`tests/ToggleForge.Evaluation.Tests/Vectors/rollout-v1.json`.

## Entradas

| Campo | Formato |
|---|---|
| `projectKey`, `environmentKey`, `flagKey` | 1 a 64 caracteres em `[a-z0-9._-]`, começando por letra ou dígito |
| `seed` | texto opaco de 1 a 64 code units, fixo durante a vida da flag |
| `playerId` | texto opaco de 1 a 256 code units |

**Texto opaco** (seed e `playerId`): UTF-16 bem formado (sem surrogate solto), nenhum caractere de
controle (categoria Unicode `Cc`) e pelo menos um caractere fora das categorias de separador (`Zs`, `Zl`,
`Zp`). O valor **não** é normalizado nem aparado: `"Ação"` em NFC e em NFD caem em buckets diferentes.
Valor fora dessa regra é inválido: o `PlayerId.TryParse` falha e o jogo (ou o SDK) avalia com
`EvaluationContext.Anonymous`, que num rollout parcial devolve o valor padrão. Os casos
de referência estão em `playerIdCases` e `seedCases` no arquivo de vetores.

## Cálculo

1. Monte a sequência de campos:
   `["toggleforge.rollout.v1", projectKey, environmentKey, flagKey, seed, playerId]`.
2. Para cada campo, nessa ordem, escreva o tamanho dos bytes UTF-8 como inteiro de 4 bytes
   big-endian, seguido dos bytes UTF-8.
3. Calcule o SHA-256 da sequência resultante.
4. Leia os 8 primeiros bytes do hash como inteiro sem sinal big-endian (`uint64`).
5. `bucket = valor mod 10000`.

Um jogador está incluído num rollout de `p` basis points (0 a 10000) se `bucket < p`.

## Exemplo

Para `studio-game`, `prod`, `new-shop`, seed `seed-1` e jogador `player-1`, o bucket é **7370**:
incluído a partir de 73,71% (7371 bp). O resultado pode ser conferido sem código C#:

```bash
python3 tools/rollout-vectors/generate.py --check
```

## Propriedades garantidas

- **Determinismo:** a mesma entrada produz sempre o mesmo bucket.
- **Preservação:** como a inclusão é `bucket < p`, aumentar `p` nunca remove um jogador incluído.
- **Independência:** projeto, ambiente, flag e seed entram no hash, então o mesmo jogador cai em
  buckets independentes em flags ou ambientes diferentes.
- **Fronteiras inequívocas:** o prefixo de tamanho impede que campos deslocados (`"ab"+"c"` ×
  `"a"+"bc"`) produzam a mesma entrada.

## Versionamento

A tag `toggleforge.rollout.v1` faz parte da entrada. Uma mudança de algoritmo cria a tag `v2`, um
arquivo de vetores novo e um campo de versão no snapshot; as duas versões coexistem até todos os SDKs
migrarem.
