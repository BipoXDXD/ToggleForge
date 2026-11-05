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
| 3 — Console | Editor, simulador, publicação e histórico (Blazor + Tailwind) | ✅ Concluído |
| 4 — SDK e configurações | Sincronização, cache, configurações JSON e app demonstrativo | ✅ Concluído |
| 5 — Unity e segmentação | Adaptador, jogo simples e regras por atributos | Planejado |
| 6 — Cloud e piloto | CI, implantação, observabilidade, backups e onboarding | Planejado |
| 7 — Distribuição independente | Eventos duráveis, serviço e armazenamento de leitura próprios | Planejado |

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
fora do Development, a API não sobe sem ele (ADR 0008). Fora do Development, declare também a rede do ingress em
`ForwardedHeaders:KnownNetworks` (CIDR): sem ela a API não sobe, porque o rate limit por IP e o HSTS dependem de
enxergar o IP e o HTTPS do cliente. A API também não sobe com `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, que faria
o host confiar em qualquer remetente. Toda resposta leva `nosniff`, `X-Frame-Options`, `Referrer-Policy` e CSP; o
HSTS só sai em HTTPS, fora do Development, com o `max-age` de `StrictTransportSecurity:MaxAge` (1 dia no
`appsettings.json`, para o primeiro deploy). Um corpo acima de 32 KiB recebe 413 (ADR 0012).

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
(`https://localhost:<porta>/`). A experiência central inteira passa por ele: criar o projeto e a flag,
simular jogadores, publicar para 10%, ampliar a distribuição e reverter. Uma jornada no Chromium
(`A_member_creates_simulates_publishes_widens_and_rolls_back_a_flag_from_the_console`) percorre esse caminho
só pela interface e confere que, depois do rollback, o mesmo jogador volta ao mesmo bucket. Outra
(`A_member_creates_publishes_simulates_and_rolls_back_a_json_flag_from_the_console`) faz o mesmo com uma
configuração JSON.

- **Rascunho e flags:** listar e criar projetos, adicionar flags ao rascunho de um ambiente, editá-las ou
  removê-las. A edição vai com `If-Match`: se outra pessoa mudou a flag enquanto ela estava aberta, nada é
  gravado e o console mostra os valores atuais para a mudança ser refeita.
- **Flags JSON, editor:** o tipo é escolhido na criação, por dois cartões (Boolean e JSON), e fica fixo. O editor
  tem duas áreas, "Value for included players" e "Value for everyone else", cada uma com a audiência calculada do
  rollout digitado ("The 10% of players inside the rollout."), um medidor dos bytes compactados contra os 4 KiB
  ("n of 4,096 bytes") e o erro de sintaxe com linha e coluna enquanto se digita. O botão Format indenta o valor e
  "Copy from included" copia o primeiro valor para o segundo. O console reformata o valor para edição, envia a
  forma compacta e só manda no PATCH o valor que mudou de verdade.
- **Flags JSON, limites e avisos:** o console recusa na hora um valor com mais de 8 níveis. Com alguma flag JSON
  no rascunho, ele mostra quanto dos 128 KiB do ambiente já foi usado ("JSON values: 96.0 of 128 KiB", ou
  "< 0.1" abaixo de 0,1 KiB), e um rascunho cheio recebe a explicação do 409 ("This draft is out of room for JSON
  values."). Uma chave já publicada com outro tipo é marcada no próprio campo da chave (409 `flag-type-locked`). O
  simulador abre o valor indentado com um botão de copiar. O aviso de que todo jogador consegue ler os valores
  fica fixo no editor, e todo valor aparece como texto, nunca como HTML.
- **Simulador:** avalia o rascunho ou uma versão publicada para um ID de jogador e explica cada flag, com o
  valor e o bucket que o decidiu. O ID vai só no corpo do pedido, nunca no endereço.
- **Publicação:** mostra as mudanças desde a versão no ar e só libera o botão com a chave do ambiente
  digitada. Se a resposta não chega, o console só oferece conferir o resultado, reenviando o mesmo pedido com
  o mesmo `Idempotency-Key`, o que nunca publica duas vezes.
- **Histórico e rollback:** o histórico é paginado e marca a versão no ar. O rollback segue as regras da
  publicação e ainda pede o descarte explícito das mudanças não publicadas do rascunho, que ele substitui.

## Configurações JSON (Marco 4)

Uma flag tem um tipo, `boolean` ou `json`, escolhido na criação. O tipo é fixo enquanto a flag existe, e depois da
primeira publicação a **chave** o guarda naquele ambiente: um build já publicado lê a flag com uma classe dele, e
trocar o tipo o quebraria em silêncio (ADR 0011). A flag JSON segue as mesmas regras da booleana (rascunho,
publicação imutável, diff, histórico, rollback, `ETag` e `Idempotency-Key`) e o mesmo rollout: inativa, devolve
`offValue`; ativa, o bucket do jogador decide entre `onValue` e `offValue`. O tipo não entra no hash, então o
algoritmo continua o v1 e os buckets são os mesmos de uma flag booleana com a mesma chave e semente.

Pelo contrato, a criação é `createFlag` em [`docs/api/openapi.yaml`](docs/api/openapi.yaml): exige uma sessão
iniciada e o token antiforgery, e o corpo leva `key`, `type`, `enabled`, `rolloutBasisPoints`, `onValue` e
`offValue`.

- **Qualquer valor JSON vale**, não só objeto: `3`, `"gold"` e `[1, 2]` cobrem os casos simples sem um terceiro
  tipo. A API compacta o valor ao salvar, preservando a ordem das chaves, então espaço em branco não aparece como
  mudança.
- **Limites**, porque cada jogador baixa o snapshot inteiro: **4 KiB** por valor em UTF-8, já compactado; **8
  níveis** de profundidade; **128 KiB** somando os valores JSON de um ambiente (as flags booleanas não contam).
  Passar do teto do ambiente responde 409 `json-budget-reached` (as 200 flags continuam `limit-reached`), e um
  corpo acima de 32 KiB, 413 `payload-too-large`. O banco repete o limite de 4 KiB num `CHECK`.
- **Recusados na entrada:** chave repetida num objeto e número que não cabe num `double` (`1e400`), porque cada
  parser de jogo os leria de um jeito.
- **`null` só na criação.** Uma flag JSON pode nascer com o valor JSON `null`, mas o PATCH recusa `null` no campo:
  no merge patch ele apagaria o valor. Quem precisa voltar a `null` recria a flag com outra chave.
- **O tipo de uma chave publicada não muda.** Depois de publicada num ambiente, a chave mantém o tipo em toda
  publicação seguinte desse ambiente, também depois de apagada do rascunho e também no rollback. Recriar a flag,
  publicar ou reverter com o outro tipo responde 409 `flag-type-locked`, e o detalhe diz a chave e o tipo já
  publicado. Outros ambientes não se afetam, e o PATCH não troca o tipo. Para outro tipo, outra chave
  (`shop-config-v2`). Quem segura a regra é o banco (tabela `published_flag_types` e uma FK), então duas edições
  concorrentes nunca publicam os dois tipos.
- **Nada de segredo num valor.** O valor chega a todo jogador que tem o jogo; o console avisa no editor.

> **Bancos de desenvolvimento criados antes do Marco 4 precisam ser recriados.** As colunas de valor viraram `json` e
> ganhou a tabela `published_flag_types`, e a migration `Initial` do Authoring foi regenerada (agora
> `20261004203641_Initial`) em vez de ganhar uma migration nova, porque ainda não há produção. O `--migrate` sozinho
> **falha** num banco antigo: o EF não reconhece o novo `Initial`, tenta aplicá-lo e a API aborta com `42P07: relation
> "projects" already exists`. O banco antigo fica como estava: a migration roda numa transação e volta atrás. Apague o
> banco e migre; o `--migrate` cria o banco que não existe (o usuário da connection string precisa poder criar bancos):
>
> ```bash
> dropdb --if-exists toggleforge    # o banco da sua ConnectionStrings:ToggleForge
> dotnet run --project src/ToggleForge.Api -- --migrate
> ```
>
> Depois da primeira implantação (Marco 6), mudanças de esquema seguem expand/contract, sem regenerar nada.

## SDK (Marco 4)

O jogo usa o `ToggleForgeClient` com a credencial do ambiente. O SDK baixa o snapshot em
`GET /sdk/v1/snapshot` com `If-None-Match` e avalia as flags localmente, sem esperar rede nem disco. Cada
resultado traz o valor, o motivo, a versão e a origem dos dados (`Server`, `Cache` ou `None`). Uma versão igual ou
anterior à que está em uso é descartada, porque a ordem vem da versão da publicação e nunca do relógio. Um
snapshot inválido ou de algoritmo desconhecido mantém o anterior. O SDK é netstandard2.1 com C# 9 e lê o JSON
com Newtonsoft.Json mapeado à mão, sem reflection, para rodar no IL2CPP da Unity (ADR 0010).

`StartAsync` faz a primeira sincronização e mantém as próximas em segundo plano: a cada 60 s com ±10% de
variação (configurável de 15 s a 1 h), backoff dobrando a cada falha até 10 min e `Retry-After` respeitado até
1 h. Um ambiente nunca publicado volta ao ritmo normal, e uma credencial recusada espera o teto. O jogo pode
desistir de esperar a primeira sincronização sem pará-la. `RefreshAsync` sincroniza na hora, por exemplo quando
o jogo volta do segundo plano, mas devolve `Skipped` sem chamar o servidor se vier antes de 15 s da última
tentativa ou do prazo que o servidor pediu.

Com um `FileSnapshotStore`, o snapshot sobrevive a um reinício do jogo, num arquivo por credencial. Sem rede,
o jogo continua com o snapshot guardado (origem `Cache`) até o servidor confirmá-lo. Passados 7 dias da última
confirmação (configurável), o snapshot expira e o jogo recebe os próprios valores padrão com o motivo
`SnapshotExpired`. Uma jornada contra a API real reinicia o jogo sem rede e prova esse caminho.

```csharp
using var http = new HttpClient { BaseAddress = new Uri("https://api.toggleforge.example/") };
var credential = SdkCredential.Parse(secret);
await using var client = new ToggleForgeClient(
    new HttpSnapshotTransport(http, credential), new FileSnapshotStore(cacheDirectory, credential));
await client.StartAsync();
var newShop = client.EvaluateBoolean("new-shop", EvaluationContext.ForPlayer(PlayerId.Parse(playerId)), defaultValue: false);
```

O app demonstrativo conta essa história com o SDK de verdade. Sem argumentos, ele roda um roteiro autocontido,
contra um servidor simulado: primeira abertura sem rede, publicação a 10%, ampliação a 25% sem perder ninguém,
resposta atrasada descartada, reinício sem rede com o cache, o cache vencido depois de dias offline, a volta da
rede com um 304 e, no passo 8, a configuração JSON `shop-config` publicada na v3 e lida com a classe do jogo
(`ShopConfig`, pelo System.Text.Json). No mesmo passo, um parser que recusa o valor não derruba o jogo: ele recebe
o próprio valor padrão com o motivo `InvalidValue`. Cada passo confere o que promete, e o CI roda o roteiro como
smoke test.

Com `--live`, ele conecta numa API rodando e mostra a flag mudar a cada publicação ou rollback feito pelo console;
fora do localhost, só por HTTPS, para a credencial não trafegar em texto claro. Com `--live --json`, ele lê uma flag
JSON (`shop-config` por padrão) e imprime o texto JSON inteiro do valor, exatamente como chega ao parser do jogo, e
`null` quando cai no valor padrão. É uma ferramenta do desenvolvedor, para ver o que o jogo receberia: um valor de
4 KiB ocupa a tela inteira a cada mudança.

```bash
dotnet run --project samples/ToggleForge.SdkDemo
TOGGLEFORGE_API_URL=http://localhost:5080/ TOGGLEFORGE_CREDENTIAL=tfsdk_... \
  dotnet run --project samples/ToggleForge.SdkDemo -- --live new-shop player-42
TOGGLEFORGE_API_URL=http://localhost:5080/ TOGGLEFORGE_CREDENTIAL=tfsdk_... \
  dotnet run --project samples/ToggleForge.SdkDemo -- --live --json shop-config player-42
```

O SDK só manda a credencial, sem cookie, então em desenvolvimento a API pode ouvir também em HTTP local
(`ASPNETCORE_URLS="https://localhost:7443;http://localhost:5080"`). O HTTPS de desenvolvimento só funciona com
o certificado confiável (`dotnet dev-certs https --trust`).

Uma flag JSON se lê com `EvaluateJson<T>`: o SDK entrega o texto JSON compacto do valor escolhido, e o parser do
jogo o converte para a classe que o jogo usa. O SDK não depende de nenhuma biblioteca de JSON do jogo (JsonUtility,
Newtonsoft, System.Text.Json ou à mão), e a regra de nunca lançar continua valendo: flag ausente, de outro tipo
(`TypeMismatch`), snapshot expirado ou um parser que lança exceção (`InvalidValue`) devolvem o `defaultValue` do jogo
com o motivo.

```csharp
internal sealed record ShopConfig(decimal Discount, IReadOnlyList<string> FeaturedItems)
{
    public static ShopConfig Closed { get; } = new(0m, []);
}

// Campo ausente ou nulo é recusado, em vez de virar 0 ou uma lista nula no jogo.
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
};

var shop = client.EvaluateJson("shop-config", EvaluationContext.ForPlayer(PlayerId.Parse(playerId)), ShopConfig.Closed,
    json => JsonSerializer.Deserialize<ShopConfig>(json, options) ?? throw new JsonException("The shop config is null."));
// shop.Value.Discount, shop.Reason, shop.Source, shop.SnapshotVersion
```

- **Avalie em pontos naturais do jogo** (abrir a loja, carregar a cena), não a cada frame: o parse roda a cada
  chamada.
- **Um valor guardado pelo jogo não vê publicação, rollback nem expiração** até a próxima avaliação. Quem guarda o
  `ShopConfig` reavalia quando quer enxergar a versão nova.
- O `InvalidValue` só cobre o que o parser recusa **lançando exceção**. O `JsonUtility.FromJson` da Unity preenche
  campo ausente com o padrão do tipo e quase nunca lança, então a forma do valor se valida dentro do parser. A
  exceção não chega ao jogo: para diagnosticá-la, registre-a dentro do próprio parser.
- Números com fração passam pelo `double` no SDK: o valor é o `double` mais próximo, e a grafia pode mudar (`1.50`
  chega como `1.5`). Inteiros e datas chegam como saíram da API (ADR 0010).

## Desenvolvimento

Requisitos: .NET SDK 10.0.403, Docker (os testes da API sobem um Postgres com Testcontainers), Node 24
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
| `src/ToggleForge.Sdk` | SDK do jogo: netstandard2.1, C# 9, só Newtonsoft.Json além do núcleo (ADR 0010) |
| `tests/ToggleForge.Evaluation.Tests` | xUnit v3 no Microsoft.Testing.Platform, com vetores de referência |
| `tests/ToggleForge.Domain.Tests` | Regras de negócio, sem banco |
| `tests/ToggleForge.Api.Tests` | Integração com Postgres real, contrato, segurança, arquitetura e jornadas do console no navegador |
| `tests/ToggleForge.Console.Tests` | Componentes e cliente do console com bUnit |
| `tests/ToggleForge.Sdk.Tests` | Leitura do snapshot (inclusive flags JSON), ordem das versões e transporte do SDK, sem rede |
| `samples/ToggleForge.RolloutDemo` | Simulação de rollout em etapas |
| `samples/ToggleForge.SdkDemo` | O SDK com rede caindo, resposta fora de ordem, reinício e uma configuração JSON; `--live` e `--live --json` contra a API |
| `tools/rollout-vectors` | Implementação de referência do algoritmo em Python |
| `docs/` | ADRs, especificação do rollout, contrato e ameaças da API |
| `tools/api-lint` | Spectral com o ruleset OWASP sobre o contrato |

O CI roda formatação, build com avisos tratados como erro, testes (inclusive os da API, com Postgres em
container), checagem dos vetores, smoke test das duas demos, lint do contrato e scan de segredos (gitleaks).
