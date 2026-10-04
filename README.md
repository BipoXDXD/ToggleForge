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
| 2 — API persistente | Rascunho, publicação, rollback, autenticação e isolamento | ✅ Concluído |
| 3 — Console | Editor, simulador, publicação e histórico (Blazor + Tailwind) | Em andamento |
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
// result.Value, result.Reason (Included, Excluded, Disabled, FlagNotFound...), result.SnapshotVersion,
// result.Bucket (o bucket do jogador quando ele decidiu o resultado)
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

## API (Marco 2)

O contrato é [`docs/api/openapi.yaml`](docs/api/openapi.yaml) (OpenAPI 3.1), escrito antes do código. O
tracer bullet cobre o caminho inteiro: criar conta, criar projeto, criar flag no rascunho, publicar com
`Idempotency-Key`, emitir a credencial do ambiente e baixar o snapshot no jogo, com `ETag` e 304. Depois
dele vieram a edição de flags com `If-Match`, o rollback, o histórico paginado de publicações e a
revogação de credenciais e a simulação, que avalia o rascunho ou uma publicação para um jogador com o
mesmo código do SDK e mostra o bucket que decidiu cada flag. O console
usa cookie de sessão com token antiforgery; o jogo, `Authorization: Bearer tfsdk_...`. Um rate limit
por minuto (membro pela conta, jogo pela credencial e IP, o resto pelo IP) responde 429 com
`Retry-After`; os tetos ficam em `RateLimits:*` (ADR 0006). As chaves que protegem a sessão ficam no
Postgres, cifradas pelo certificado PEM de `DataProtection:CertificatePath` e `DataProtection:KeyPath`;
fora do Development, a API não sobe sem ele (ADR 0008). Atrás de um proxy, declare a rede dele em
`ForwardedHeaders:KnownNetworks` (CIDR) para a API enxergar o IP e o HTTPS do cliente.

```bash
dotnet user-secrets set ConnectionStrings:ToggleForge "Host=localhost;Database=toggleforge;Username=..." \
  --project src/ToggleForge.Api
dotnet run --project src/ToggleForge.Api -- --migrate
dotnet run --project src/ToggleForge.Api -- --purge-idempotency-records   # job agendado, a cada hora
dotnet run --project src/ToggleForge.Api
```

## Console (Marco 3)

Blazor WebAssembly com Tailwind, servido pela própria API na mesma origem (ADR 0009): o console é só mais
um cliente de `/api/v1`, com o cookie de sessão e o token antiforgery. Com a API rodando, ele abre na raiz
(`https://localhost:<porta>/`). Já dá para entrar, listar e criar projetos, adicionar flags ao rascunho de um
ambiente, editá-las ou removê-las e publicar o rascunho. A edição vai com `If-Match`: se outra pessoa mudou a
flag enquanto ela estava aberta, nada é gravado e o console mostra os valores atuais para a mudança ser refeita.
A publicação mostra as mudanças desde a versão no ar e só libera o botão com a chave do ambiente digitada. Se
a resposta não chega, o console só oferece conferir o resultado, reenviando o mesmo pedido com o mesmo
`Idempotency-Key`, o que nunca publica duas vezes.

## Desenvolvimento

Requisitos: .NET SDK 10.0.400, Docker (os testes da API sobem um Postgres com Testcontainers), Node 24
(o build do console gera o CSS com o Tailwind) e Python 3.9+ (só para o oráculo de vetores). As jornadas do
console baixam um Chromium do Playwright na primeira execução.

```bash
dotnet build ToggleForge.slnx -c Release
dotnet test --solution ToggleForge.slnx -c Release
dotnet format ToggleForge.slnx --verify-no-changes
python3 tools/rollout-vectors/generate.py --check
```

| Pasta | Conteúdo |
|---|---|
| `src/ToggleForge.Evaluation` | Núcleo de avaliação: netstandard2.1, C# 9, sem dependências (compatível com Unity) |
| `src/ToggleForge.Domain` | Regras de Access, Authoring e Delivery, sem framework |
| `src/ToggleForge.Application` | Casos de uso e portas de persistência |
| `src/ToggleForge.Infrastructure` | EF Core e Npgsql, um `DbContext` e um schema por módulo, Identity |
| `src/ToggleForge.Api` | Minimal APIs, autenticação e Problem Details; serve o console |
| `src/ToggleForge.Console` | Console Blazor WebAssembly com Tailwind |
| `tests/ToggleForge.Evaluation.Tests` | xUnit v3 no Microsoft.Testing.Platform, com vetores de referência |
| `tests/ToggleForge.Domain.Tests` | Regras de negócio, sem banco |
| `tests/ToggleForge.Api.Tests` | Integração com Postgres real, contrato, segurança, arquitetura e jornadas do console no navegador |
| `tests/ToggleForge.Console.Tests` | Componentes e cliente do console com bUnit |
| `samples/ToggleForge.RolloutDemo` | Simulação de rollout em etapas |
| `tools/rollout-vectors` | Implementação de referência do algoritmo em Python |
| `docs/` | ADRs, especificação do rollout, contrato e ameaças da API |
| `tools/api-lint` | Spectral com o ruleset OWASP sobre o contrato |

O CI roda formatação, build com avisos tratados como erro, testes (inclusive os da API, com Postgres em
container), checagem dos vetores, smoke test da demo, lint do contrato e scan de segredos (gitleaks).
