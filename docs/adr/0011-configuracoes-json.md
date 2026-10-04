# 0011 — Configurações JSON como um tipo de flag

- **Status:** Aceita
- **Data:** 2026-10-04

## Contexto

O Marco 4 promete "configurações JSON" (`ToggleForge.md`, seções 2 e 9): o estúdio publica um valor estruturado,
como `{"discount": 0.2, "featuredItems": ["sword"]}`, e o jogo o lê sem recompilar. Valem as regras que as
flags booleanas já seguem: rascunho, publicação imutável, rollback, ETag, idempotência e fallback para o
`defaultValue` do jogo.

O que já existe e pesa na escolha:

- **Flag booleana ponta a ponta.** `BooleanFlag` no núcleo, `DraftFlag`/`FlagSettings` no domínio, colunas
  `on_value`/`off_value` booleanas em `draft_flags` e `publication_flags`, contrato OpenAPI, snapshot do SDK e
  console.
- **ADR 0010.** O SDK lê o snapshot com Newtonsoft.Json à mão, sem reflection, e nenhum tipo do Newtonsoft
  aparece na API pública. A profundidade máxima do snapshot é 64 níveis, e a API deve impor às configurações
  um teto bem abaixo disso.
- **Um defeito latente no SDK.** Hoje uma flag com `onValue` não booleano invalida a flag, e uma flag inválida
  recusa o snapshot inteiro (`SnapshotReader`). Se uma flag JSON entrasse no snapshot assim, todo build antigo
  do jogo pararia de receber todas as flags. Nenhum SDK foi distribuído ainda, então ainda dá para corrigir.
- **Custo de distribuição.** Cada jogador baixa o snapshot inteiro a cada publicação. Um ambiente tem até 200
  flags (`ProjectEnvironment.MaxFlags`).

## Alternativas

### O que é uma configuração JSON

1. **Um tipo de flag.** A flag ganha um tipo, `boolean` ou `json`. A flag JSON tem `onValue` e `offValue`
   JSON e segue o mesmo rollout.
   - Prós: reaproveita rollout, rascunho, publicação, diff, histórico, ETag e console. Uma configuração
     nova pode ir primeiro para 10% dos jogadores.
   - Contras: toda configuração precisa de um `offValue`, mesmo sem rollout. O rascunho passa a ter dois
     formatos de flag.
2. **Uma entidade separada.** Uma chave e um valor JSON, sem rollout, como um parâmetro do Firebase Remote
   Config sem condições.
   - Prós: é o modelo mais simples para quem só quer um valor.
   - Contras: segundo aggregate, com tabelas, endpoints, telas e diff paralelos. O rollout de uma
     configuração fica impossível até a segmentação do Marco 5.
3. **Flags com vários tipos** (booleano, número, texto, JSON).
   - Prós: API tipada para os casos comuns, como a do OpenFeature.
   - Contras: quatro tipos para validar, persistir, editar e testar, quando o JSON já cobre número e texto.

### Como o SDK entrega o valor

1. **Texto JSON mais o parser do jogo:** `EvaluateJson<T>(chave, contexto, defaultValue, parse)`.
   - Prós: tipado. Não amarra o jogo a uma biblioteca (JsonUtility, Newtonsoft ou à mão). Um parser que lança
     exceção vira fallback, então a regra de nunca lançar continua valendo.
   - Contras: o parse roda a cada chamada, então o jogo deve guardar o resultado.
2. **Só o texto:** `EvaluateJson(chave, contexto, string defaultJson)`.
   - Prós: é a API mais simples possível.
   - Contras: o erro de parse fica com o jogo, fora do contrato de fallback, e o `defaultValue` vira JSON
     escrito à mão.
3. **Uma árvore JSON própria.**
   - Prós: nenhum parse do lado do jogo.
   - Contras: um DOM JSON para manter para sempre numa API pública que não se corrige depois.

### Como o banco guarda os valores

1. **`on_value`/`off_value` convertidas para `json`**, com `true`/`false` nas flags booleanas.
   - Prós: uma forma só por linha.
   - Contras: a migration converte colunas existentes, em vez de só adicionar.
2. **Colunas JSON extras, anuláveis, ao lado das booleanas.**
   - Prós: a migration é só aditiva (expand).
   - Contras: cada linha tem duas formas possíveis, presas por um CHECK maior.
3. **`jsonb` em vez de `json`.**
   - Prós: indexável.
   - Contras: reordena as chaves, então o console mostraria outra ordem. Nenhuma consulta filtra pelo conteúdo.

## Decisão

- **Um tipo de flag** (escolha do usuário).
- **Texto JSON mais o parser do jogo** no SDK (escolha do usuário).
- **Limites de 4 KiB por valor, 8 níveis e 128 KiB por ambiente** (aprovados pelo usuário).
- O resto decidi eu e registro abaixo.

### Regras da flag JSON

- O tipo é escolhido na criação e **não muda**, como a chave e a semente. Um build já publicado lê a flag com
  uma classe dele, e trocar o tipo o quebraria em silêncio. Quem precisa de outro tipo cria outra chave.
- Inativa, a flag devolve `offValue`. Ativa, o rollout decide entre `onValue` e `offValue`. O tipo não entra
  no hash, então o algoritmo de rollout continua o v1 (ADR 0002), sem vetores novos.
- **Qualquer valor JSON vale**, não só objeto: `3`, `"gold"` e `[1, 2]` cobrem os casos simples sem um
  terceiro tipo.
- **Limites** (cada jogador baixa o snapshot inteiro):
  - cada valor tem no máximo **4 KiB** em UTF-8, já compactado, e **8 níveis** de profundidade;
  - a soma dos valores JSON de um ambiente é no máximo **128 KiB**. Sem isso, o pior caso seria 200 flags ×
    2 × 4 KiB = 1,6 MiB por download;
  - o corpo das rotas de flag tem teto de **32 KiB**, o que deixa espaço para JSON indentado antes da
    compactação.
- **Recusas que evitam leituras divergentes:**
  - chave duplicada num objeto, porque o Newtonsoft e o JsonUtility a resolvem de jeitos diferentes;
  - número que não cabe num `double`, como `1e400`, porque cada parser o trata de um jeito (e o Mono talvez
    recuse).
- A API **compacta** o valor ao salvar, preservando a ordem das chaves. Espaço em branco não aparece como
  mudança, e o console reformata para edição.

### Núcleo (`ToggleForge.Evaluation`)

- Uma base abstrata `Flag`, com construtor `internal`, reúne a decisão comum (inativa → off; senão o rollout
  decide). `BooleanFlag` e a nova `JsonFlag` herdam dela. Só o núcleo cria subclasses, então a hierarquia é
  fechada. Dois dicionários no snapshot, um por tipo, espalhariam a unicidade da chave e duplicariam a decisão.
- O núcleo não lê JSON: o valor de uma `JsonFlag` é texto opaco, já validado por quem o produziu.
- `EvaluateJson<T>(snapshot, chave, contexto, defaultValue, parse)` captura a exceção do parser. Toda a
  semântica da avaliação fica testável sem SDK, e a simulação da API passa `json => json`.
- Motivos novos, no fim do enum para não renumerar os existentes:
  - `TypeMismatch`: a flag existe, mas é de outro tipo;
  - `InvalidValue`: o parser do jogo lançou exceção.
  - Nos dois casos volta o `defaultValue`.

### Domínio e banco

- `JsonFlagValue` é um value object que nasce válido, lido com o System.Text.Json do .NET
  (`AllowDuplicateProperties = false`), sem pacote de terceiro.
- `FlagSettings` troca `OnValue`/`OffValue` por um par fechado, `BooleanValues` ou `JsonValues`. O tipo
  deriva do par. Um `FlagChange` com valor de outro tipo é uma falha de domínio, não uma exceção.
- `ProjectEnvironment` impõe os 128 KiB na criação e na edição, como já impõe as 200 flags. A concorrência
  continua protegida pela revisão do rascunho.
- **Banco:** `type text not null` com CHECK `in ('boolean', 'json')`, e `on_value`/`off_value` convertidas
  para `json` com `USING to_json(...)`. Um CHECK garante que uma flag `boolean` só guarda `true`/`false`. A
  conversão é feita de uma vez, sem expand/contract, porque ainda não há produção: o ToggleForge só implanta
  no Marco 6.

### Contrato

- **Mudança aditiva.** `type` é opcional na criação, com padrão `boolean`. Os valores aceitam qualquer JSON
  no schema, e o servidor os confere pelo tipo. Um `oneOf` com discriminator seria mais preciso, mas exigiria
  `type` obrigatório e quebraria o contrato. `type` não entra no PATCH: o DTO estrito o recusa.
- `Flag`, `SnapshotFlag` e `SimulationResult` ganham `type`, e os valores saem como JSON, não como string.
  No snapshot, `type` é opcional no schema (ausente quer dizer `boolean`), mas o servidor sempre o envia.
- **Erros:** valor inválido ou fora dos limites → 400 no campo. Passar dos 128 KiB do ambiente → 409
  `limit-reached`. Corpo acima de 32 KiB → 413.
- O `openapi.yaml` muda na fatia da API, como primeiro commit dela e antes do código, porque os testes de
  integração conferem toda resposta contra ele.

### SDK

- **Leitura do snapshot:**
  - `type` ausente → booleana, o que mantém válidos os snapshots já publicados e os caches em disco;
  - **tipo desconhecido → a flag é ignorada**, e o resto do snapshot vale. Isso corrige o defeito latente e
    deixa um tipo futuro passar por builds antigos;
  - chave duplicada continua recusando o snapshot inteiro, mesmo entre tipos.
- O valor JSON é regravado compacto. Com `DateParseHandling.None` (já em uso), datas e inteiros passam sem
  conversão; uma fração passa pelo `double`, que mantém o valor e pode mudar a grafia (`1.50` vira `1.5`). Um valor
  que só o Newtonsoft aceita (`NaN`, `1e400`) recusa o snapshot (ADR 0010).
- A exceção do parser não chega ao SDK: para diagnosticá-la, o jogo a registra dentro do próprio parser.
- `EvaluateJson<T>(chave, contexto, defaultValue, parse)` devolve `FlagResult<T>`. Cache do valor já lido
  fica para quando houver medição.

### Plano das fatias

1. ADR e canvas de capacidades.
2. SDK tolerante: tipo desconhecido é ignorado.
3. Núcleo: refactoring que extrai a base `Flag`, depois `JsonFlag`, `EvaluateJson` e os motivos novos.
4. Domínio, banco e API: o `openapi.yaml` primeiro, depois o tracer bullet de uma flag JSON criada pela API
   que chega ao snapshot.
5. SDK: leitura do JSON, `EvaluateJson<T>` e um passo novo no app demonstrativo.
6. Console, com uma jornada Playwright.
7. Fechar o Marco 4: README e status no plano.

## Consequências

- Uma configuração JSON ganha rollout, histórico, diff e rollback sem código novo para isso.
- Toda configuração precisa de dois valores, mesmo quem só quer um. O console pode preencher o `offValue`
  com o `onValue` como ponto de partida.
- O snapshot cresce até 128 KiB de valores por ambiente. O 304 continua evitando o download quando nada mudou.
- O estúdio pode pôr um segredo numa configuração, e ele chegaria a todo jogador: a credencial do SDK sai do
  build (`ToggleForge.md`, seção 11). Esse risco é **aceito**. O console avisa no editor e o canvas o registra.
- O jogo que avalia a cada frame paga o parse a cada frame. O XML doc do `EvaluateJson<T>` manda guardar o
  resultado.
- Um tipo futuro (por exemplo, número) passa por builds antigos sem derrubá-los: eles só não o enxergam.

## Compliance

- **`Evaluation.Tests`:** decisão da `JsonFlag`, `TypeMismatch` nos dois sentidos, `InvalidValue`, e os buckets
  de uma flag JSON iguais aos dos vetores v1.
- **`Domain.Tests`:** partições do `JsonFlagValue` (escalar, objeto, lista, sintaxe inválida, chave duplicada
  aninhada, profundidade 8 e 9, 4.096 e 4.097 bytes com caractere multibyte, número fora do `double`,
  compactação com a ordem das chaves preservada), os 128 KiB nos dois lados da fronteira e a troca de tipo
  recusada.
- **`Api.Tests`:**
  - criar, editar, publicar, snapshot, simulação e rollback de uma flag JSON;
  - cada 400, o 409 do teto e o 413 do corpo;
  - os CHECKs do banco;
  - duas criações concorrentes que passariam do teto: só uma entra;
  - todas as respostas conferidas contra o `openapi.yaml`.
- **`Sdk.Tests`:** tipo ausente, flag JSON, tipo desconhecido ignorado sem recusar o snapshot, datas e números
  preservados, parser que lança.
- **`Console.Tests` e `ConsoleJourneyTests`:** editor de flag JSON, o aviso de que o valor chega a todo jogador, valor com
  `<script>` exibido como texto, e a jornada criar → publicar → simular → reverter.
