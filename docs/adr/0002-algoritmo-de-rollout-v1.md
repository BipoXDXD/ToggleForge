# 0002 — Algoritmo de rollout v1: SHA-256 truncado em basis points

- **Status:** Aceita
- **Data:** 2026-10-03

## Contexto

O rollout percentual precisa ser determinístico (o mesmo jogador recebe sempre o mesmo resultado),
preservar os incluídos quando o percentual aumenta e produzir o mesmo bucket no simulador do console,
no SDK C# e em qualquer SDK futuro em outra linguagem. O algoritmo vira contrato público: mudá-lo
redistribui jogadores em produção.

Alternativas consideradas:

| Opção | Prós | Contras |
|---|---|---|
| **SHA-256 truncado** | Está na BCL de toda plataforma (netstandard2.1, Unity, IL2CPP) e em toda linguagem; vetores verificáveis por ferramenta independente; distribuição uniforme mesmo com IDs sequenciais | ~10× mais lento que um hash não criptográfico (irrelevante numa avaliação local) |
| MurmurHash3 32-bit | Padrão em ferramentas de flags (Unleash); rápido | Implementação própria em cada linguagem, com risco de divergência sutil; 32 bits dão menos margem contra viés |
| FNV-1a 64-bit | Cabe em ~10 linhas | Distribuição pior com entradas parecidas, o que pode enviesar o percentual |

## Decisão

Adotamos o **SHA-256 truncado**, com esta especificação (detalhada em `docs/rollout-algorithm.md`):

1. Entrada canônica: a tag `toggleforge.rollout.v1`, seguida de `projectKey`, `environmentKey`,
   `flagKey`, `seed` e `playerId`, nessa ordem. Cada campo é codificado em UTF-8 e prefixado com o
   tamanho em 4 bytes big-endian. O prefixo impede colisões como `"ab"+"c"` × `"a"+"bc"`.
2. `bucket = BigEndianUInt64(SHA256(entrada)[0..8]) mod 10000`.
3. O percentual é guardado em **basis points** (0 a 10000, granularidade de 0,01%). O jogador está
   incluído se `bucket < basisPoints`.
4. A versão da publicação **não** entra no hash; a seed é fixa por flag.

A regra do item 3 é monotônica: ao subir de `p` para `q > p`, todo bucket menor que `p` também é menor
que `q`, então quem estava incluído continua incluído. O viés de módulo de 2^64 por 10000 é da ordem
de 10^-15 e pode ser ignorado.

## Consequências

- O ambiente entra no hash: o mesmo jogador pode estar incluído em `staging` e excluído em `prod`.
  É o comportamento esperado (ambientes independentes).
- Trocar a seed redistribui todos os jogadores da flag; o console deve tratar isso como ação explícita.
- A regra de targeting devolve o bucket que decidiu (`TargetingOutcome`), e o `EvaluationResult` o
  expõe; é nulo quando o resultado não depende do jogador (flag inativa, 0%, 100%, sem ID). O simulador
  do console e o debug do SDK mostram o mesmo número, sem recalcular o hash por fora.
- Uma mudança no algoritmo exige uma tag nova (`toggleforge.rollout.v2`), um campo de versão no
  snapshot e a coexistência das duas versões até todos os SDKs migrarem.

## Compliance

- `tools/rollout-vectors/generate.py` implementa a especificação em Python (oráculo independente) e
  gera `rollout-v1.json`. Os testes C# conferem cada vetor, e o CI roda `generate.py --check` para
  garantir que o arquivo commitado bate com o oráculo.
- Testes de propriedade: 0% e 100% nos extremos, preservação ao ampliar e distribuição aproximada.
