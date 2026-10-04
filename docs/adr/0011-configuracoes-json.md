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

- `JsonFlagValue` é um value object que nasce válido, lido com o System.Text.Json do .NET, sem pacote de
  terceiro. A leitura própria aceita chaves repetidas e a varredura seguinte as recusa com `DuplicateKey`. Pela
  API, quem recusa primeiro é o binding (`AllowDuplicateProperties = false`), com o 400 genérico `INVALID_BODY`
  no campo; o `DuplicateKey` só aparece no uso direto do domínio e na reidratação do banco, como defesa em
  profundidade.
- `FlagSettings` troca `OnValue`/`OffValue` por um par fechado, `BooleanValues` ou `JsonValues`. O tipo
  deriva do par. Um `FlagChange` com valor de outro tipo é uma falha de domínio, não uma exceção.
- `ProjectEnvironment` impõe os 128 KiB na criação e na edição, como já impõe as 200 flags. Quem impede dois
  editores de passarem juntos do teto é a trava do ambiente na edição (`FOR NO KEY UPDATE`, em
  `FindForUpdateAsync`), não a revisão do rascunho: a ETag é por flag, e duas edições em flags diferentes não
  conflitam por ela.
- **Banco:** `type text not null` com CHECK `in ('boolean', 'json')`, `on_value`/`off_value` do tipo `json` (com
  `true`/`false` nas flags booleanas) e um CHECK de que uma flag `boolean` só guarda `true`/`false`. O Postgres não
  converte `boolean` em `json` sem `USING`, e as migrations são geradas, nunca editadas à mão: por escolha do
  usuário, a migration `Initial` do módulo Authoring foi **regenerada**, e todo banco de desenvolvimento existente
  é recriado (`drop` e `--migrate`). Só é aceitável porque ainda não há produção; depois do Marco 6, mudanças de
  esquema seguem expand/contract.
- **Tipo travado por chave publicada:** uma vez publicada num ambiente com um tipo, a chave mantém esse tipo em
  toda publicação seguinte do mesmo ambiente, e isso vale para o publish e para o rollback, porque os dois criam a
  publicação que chega ao jogo. Apagar a flag do rascunho não libera o tipo: mudar de tipo exige uma chave nova.
  Outros ambientes não se afetam.
  - O invariante mora no banco, não num "checa e depois insere": a tabela `published_flag_types` tem uma linha por
    (ambiente, chave) com o tipo, gravada na mesma transação da publicação, e cada linha de `publication_flags`
    aponta para ela pela chave estrangeira (ambiente, chave, tipo). Uma publicação com o tipo trocado viola a FK, e
    a linha travada não pode mudar de tipo enquanto houver publicação apontando para ela.
  - O domínio (`PublishedFlagTypes`) recusa antes, com o 409 `flag-type-locked`, que diz a chave e o tipo já
    publicado: no publish, no rollback e, mais cedo, na criação da flag no rascunho, para o console avisar antes da
    publicação. O PATCH não troca o tipo, então não chega a essa recusa.
  - Quem segura o invariante é a FK. O lock do ambiente (`FOR NO KEY UPDATE`), tomado antes de ler os tipos e
    mantido até gravar a publicação, é o que faz a resposta ser 409, e não 500, quando dois editores disputam:
    nenhuma outra publicação trava um tipo no meio. Se a recusa do domínio falhasse, a FK derrubaria a gravação com
    500: um defeito, nunca um tipo trocado no jogo.
  - Sem backfill: a migration `Initial` do Authoring foi regenerada de novo com a tabela, e os bancos de
    desenvolvimento já precisam ser recriados nesta fatia. Depois da primeira produção, uma tabela assim nasce
    preenchida a partir de `publication_flags` numa migration expand/contract.
- **Tamanho no banco:** um CHECK `octet_length(... ::text) <= 4096` em `on_value` e `off_value`, nas duas tabelas,
  repete o limite de `JsonFlagValue.MaxBytes`. Sem ele, um valor gravado por fora e maior que o limite faria toda
  leitura do ambiente falhar, inclusive o snapshot. Pelo mesmo motivo, **apertar um limite do valor depois** (bytes,
  níveis, regras de número) exige migrar os dados antes, em expand/contract: corrigir ou recusar os valores
  gravados e só então apertar a regra no domínio e no CHECK. A mensagem de erro de um valor gravado inválido
  nunca traz o valor, só o tipo e o tamanho, porque vai para o log.

### Contrato

- **Mudança aditiva.** `type` é opcional na criação, com padrão `boolean`. Os valores aceitam qualquer JSON
  no schema, e o servidor os confere pelo tipo. Um `oneOf` com discriminator seria mais preciso, mas exigiria
  `type` obrigatório e quebraria o contrato. `type` não entra no PATCH: o DTO estrito o recusa.
- `Flag`, `SnapshotFlag` e `SimulationResult` ganham `type`, e os valores saem como JSON, não como string.
  No snapshot, `type` é opcional no schema (ausente quer dizer `boolean`), mas o servidor sempre o envia.
- **Erros:** valor inválido ou fora dos limites → 400 no campo. Passar dos 128 KiB do ambiente → 409
  `json-budget-reached`, um tipo próprio (mudança aditiva), para o console explicar o erro sem adivinhar; as 200
  flags continuam `limit-reached`. Recriar ou republicar uma chave com o outro tipo → 409 `flag-type-locked`, outro
  tipo próprio. Corpo acima de 32 KiB → 413 `payload-too-large`.
  - Numa flag booleana com os dois valores errados, só `/onValue` é apontado: o domínio para na primeira falha, e
    o `/offValue` aparece na tentativa seguinte.
  - O limite de corpo é o nativo do servidor (`RequestSizeLimit` nas duas rotas), aplicado pelo Kestrel na leitura
    do binding. O 413 chega vazio ao `UseStatusCodePages` e vira Problem Details lá. O TestServer não aplica o
    limite, então os testes de 413 rodam num Kestrel.
  - Com `Content-Length` a fronteira é exata; um corpo chunked pode levar 413 um pouco antes dos 32 KiB (o spike
    da Tarefa 9 viu 413 com 32.767 bytes), então o cliente que chega perto do teto deve mandar `Content-Length`.
  - `null` no PATCH é recusado no campo, porque no merge patch ele apagaria o valor; uma flag JSON só recebe o JSON
    `null` na criação.
  - Um corpo que o binding recusa aponta só o campo de topo (`/onValue`), na criação e no patch, sem as chaves
    escolhidas pelo cliente dentro do valor.
- **Saída:** o valor sai como o `JsonElement` do texto compacto. Acentos não são reescapados, então o download
  fica no tamanho contado; o que a compactação escapa (fora do BMP, U+2028/U+2029) já pesa no limite.
- O `openapi.yaml` muda na fatia da API, como primeiro commit dela e antes do código, porque os testes de
  integração conferem toda resposta contra ele.

### SDK

- **Leitura do snapshot:**
  - `type` ausente → booleana, o que mantém válidos os snapshots já publicados e os caches em disco;
  - **tipo desconhecido → a flag é ignorada**, e o resto do snapshot vale. Isso corrige o defeito latente e
    deixa um tipo futuro passar por builds antigos;
  - chave duplicada continua recusando o snapshot inteiro, mesmo entre tipos.
- O valor JSON é regravado compacto. Com `DateParseHandling.None` (já em uso), datas e inteiros passam sem
  conversão; uma fração passa pelo `double`, e o que se mantém é o `double` mais próximo: a grafia pode mudar
  (`1.50` vira `1.5`, `1e2` vira `100.0`), mais de 17 dígitos arredondam e `1e-400` vira `0.0` sem recusar o
  snapshot. Um valor que, regravado, o parser do jogo leria de outro jeito ou nem leria (`NaN`, `1e400`) recusa o
  snapshot; aspas simples, nome sem aspas, comentário e vírgula sobrando são aceitos e normalizados pelo
  Newtonsoft. A grafia do `double` no Mono fica para conferir no build Unity (ADR 0010).
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
- O jogo que avalia a cada frame paga o parse a cada frame. O XML doc do `EvaluateJson<T>` orienta avaliar em
  pontos naturais do jogo (abrir a loja, carregar a cena), não a cada frame. Um valor guardado pelo jogo não vê
  publicação, rollback nem expiração até a próxima avaliação. Um sinal de mudança (evento ou a versão em uso) é
  candidato aditivo futuro, para quando um estúdio precisar reagir sem reavaliar.
- Um tipo futuro (por exemplo, número) passa por builds antigos sem derrubá-los: eles só não o enxergam.
- **O tipo de uma chave publicada é para sempre no ambiente.** Um build no ar lê a chave com a classe dele, e
  nenhuma publicação pode entregar a ele a chave com outro tipo: recriar com o outro tipo, ou fazer rollback para uma
  versão assim, recebe 409 `flag-type-locked`. O custo é o redesenho de uma configuração exigir uma chave nova (por
  exemplo, `shop-config-v2`), e a tabela de tipos cresce com cada chave já publicada, sem nunca encolher. O
  `TypeMismatch` do núcleo continua como defesa do jogo contra um snapshot de outra origem.

## Compliance

- **`Evaluation.Tests`:** decisão da `JsonFlag`, `TypeMismatch` nos dois sentidos, `InvalidValue`, e os buckets
  de uma flag JSON iguais aos dos vetores v1.
- **`Domain.Tests`:** partições do `JsonFlagValue` (escalar, objeto, lista, sintaxe inválida, chave duplicada
  aninhada, profundidade 8 e 9, 4.096 e 4.097 bytes com caractere multibyte, número fora do `double`,
  compactação com a ordem das chaves preservada), os 128 KiB nos dois lados da fronteira, a troca de tipo
  recusada e a chave publicada com o outro tipo recusada na criação, no publish e no rollback.
- **`Api.Tests`:**
  - criar, editar, publicar, snapshot, simulação e rollback de uma flag JSON;
  - cada 400, o 409 `json-budget-reached` do teto e o 413 do corpo (num Kestrel, com e sem `Content-Length`);
  - os CHECKs do banco e a FK do tipo travado (`published_flag_types`);
  - recriar uma chave publicada com o outro tipo → 409 `flag-type-locked` na criação e, com o rascunho gravado por
    fora, na publicação; um editor publicando enquanto outro recria a chave com o outro tipo nunca publica os dois;
  - duas criações ou duas edições concorrentes que passariam do teto: só uma entra;
  - todas as respostas conferidas contra o `openapi.yaml`.
- **`Sdk.Tests`:** tipo ausente, flag JSON, tipo desconhecido ignorado sem recusar o snapshot, datas e inteiros
  preservados, fração pelo `double`, valor fora do padrão recusa o snapshot, parser que lança.
- **`Console.Tests` e `ConsoleJourneyTests`:** editor de flag JSON, o aviso de que o valor chega a todo jogador, valor com
  `<script>` exibido como texto, e a jornada criar → publicar → simular → reverter.
