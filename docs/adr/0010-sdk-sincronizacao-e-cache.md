# 0010 — SDK: sincronização, cache e leitura do snapshot

- **Status:** Aceita
- **Data:** 2026-10-04

## Contexto

O Marco 4 entrega o SDK que roda dentro do jogo (`ToggleForge.md`, seções 7 e 9). A evidência do marco é que
ele "funciona com perda de rede e snapshots fora de ordem". O Marco 5 leva o mesmo SDK para a Unity, então
as escolhas de agora precisam valer lá também.

O que já existe e pesa na escolha:

- **Contrato de distribuição.** `GET /sdk/v1/snapshot` com `Authorization: Bearer tfsdk_...` devolve a
  publicação atual do ambiente da credencial, com `ETag`. `If-None-Match` faz a API responder 304. 404 quer
  dizer que o ambiente nunca foi publicado; 429 vem com `Retry-After` (ADR 0006).
- **Avaliação pronta.** `ToggleForge.Evaluation` (netstandard2.1, C# 9, sem dependências, ADR 0001) avalia
  um `ConfigurationSnapshot` imutável e nunca lança exceção por dado ruim.
- **Requisitos do plano (seção 7):**
  - sincronizar a cada 60 s, com variação aleatória, timeout e backoff limitado;
  - usar ETag/304;
  - manter o último snapshot válido e trocar de versão atomicamente;
  - descartar resposta fora de ordem;
  - respeitar uma idade máxima do cache e, depois dela, usar o fallback;
  - informar em cada avaliação o valor, o motivo, a versão e a origem dos dados.
- **Restrições da Unity:**
  - netstandard2.1;
  - IL2CPP: reflection limitada e stripping de código;
  - o loop do jogo não pode esperar rede nem disco;
  - nenhuma dependência que a Unity não distribua.

### Leitura do JSON

1. **Newtonsoft.Json.**
   - Prós:
     - a Unity distribui um pacote oficial ajustado para IL2CPP (`com.unity.nuget.newtonsoft-json`);
     - é o padrão de fato em jogos;
     - é uma dependência só, que funciona igual no .NET.
   - Contras:
     - é mais lento e mais pesado que o System.Text.Json;
     - os tipos dele não podem vazar na API pública.
2. **System.Text.Json.**
   - Prós: é mais rápido e tem source generator, sem reflection.
   - Contras: na netstandard2.1 arrasta System.Memory, Encodings.Web, Bcl.AsyncInterfaces e outros, que a
     Unity não traz. Copiá-los à mão conflita com as versões de outros plugins.
3. **Leitor próprio.**
   - Prós: zero dependência.
   - Contras: reimplementa algo crítico (escape, unicode, números, profundidade), e as configurações JSON do
     marco o fariam crescer.
4. **Porta com um adaptador por plataforma.**
   - Prós: cada plataforma usa a biblioteca dela.
   - Contras: são dois adaptadores para manter e testar, e as configurações JSON precisariam de um tipo
     neutro próprio.

### Idade máxima do cache

1. **7 dias.** Cobre quem joga offline por alguns dias, e uma configuração antiga não vale para sempre.
2. **24 horas.**
   - Prós: uma configuração velha dura no máximo um dia.
   - Contras: quem fica mais tempo offline volta aos valores padrão no meio do uso.
3. **Sem limite por padrão.**
   - Prós: é o mais tolerante a ficar offline.
   - Contras: contraria o plano, e um aparelho que nunca sincroniza fica com a configuração antiga para
     sempre.

### Quem conduz a sincronização

1. **Um laço do próprio SDK, em segundo plano.**
   - Prós: o jogo só chama `StartAsync` e avalia.
   - Contras: o SDK precisa de cancelamento e descarte corretos.
2. **O jogo chama a sincronização a cada quadro ou por corrotina.**
   - Prós: a Unity controla o ritmo.
   - Contras: cada jogo reimplementa o intervalo, a variação, o timeout e o backoff, que são justamente as
     regras do plano.

## Decisão

- **Newtonsoft.Json** (escolha do usuário).
- **Idade máxima padrão de 7 dias**, configurável (escolha do usuário).
- **Laço de sincronização do próprio SDK**, com `RefreshAsync` exposto para o jogo pedir uma sincronização
  na hora, por exemplo ao voltar do segundo plano.

### Projeto

- `src/ToggleForge.Sdk`: netstandard2.1, C# 9.
- Dependências: só `ToggleForge.Evaluation`, Newtonsoft.Json e Microsoft.Bcl.TimeProvider. O relógio entra
  como `TimeProvider`, para os testes controlarem o tempo com `FakeTimeProvider`.
- Nenhum tipo do Newtonsoft na API pública.
- O snapshot é lido com `JsonTextReader`/`JObject` e mapeado à mão, sem desserialização por reflection.
  Assim o código fica imune ao stripping do IL2CPP, e a validação fica explícita.

### Leitura do snapshot

- **Tolerant reader:** campo desconhecido é ignorado, porque builds antigos do jogo continuam no ar quando o
  contrato cresce de forma aditiva.
- **O snapshot inteiro é recusado** se o `algorithm` for desconhecido, se faltar ou estiver errado um campo
  que o SDK usa, ou se uma flag for inválida. Nesse caso o SDK mantém o snapshot anterior e registra a falha.
  Nada de snapshot pela metade. Campo que o SDK não usa, como `publishedAt`, não é conferido.
- **Profundidade máxima de 64 níveis no JSON.** O snapshot de hoje tem três, mas as configurações JSON vão
  aninhar mais, e um SDK publicado num jogo não se corrige depois. A API deve impor às configurações um teto
  bem abaixo desse.

### Troca e ordem

- O estado atual (snapshot, origem, ETag e o instante da última confirmação do servidor) é um objeto
  imutável, trocado com uma escrita atômica.
- A avaliação lê esse estado sem lock e sem I/O, então pode rodar no loop do jogo.
- Snapshot com versão menor ou igual à atual, do mesmo projeto e ambiente, é descartado.
- A ordem vem da versão da publicação, nunca do relógio. Um rollback é uma versão nova, então chega
  normalmente.

### Sincronização

- **Início:** `StartAsync` faz a primeira tentativa já dentro do laço e espera por ela. O token do jogo limita
  só essa espera: se o jogo desistir (fim da tela de abertura, por exemplo), o laço continua tentando.
- **Ritmo:** depois, a cada 60 s com ±10% de variação. O intervalo é configurável de 15 s (piso que protege a
  API) a 1 h (acima disso, uma correção ou um rollback demorariam demais a chegar).
- Cada pedido tem timeout de 10 s, que cobre também a leitura do corpo.
- **Em caso de falha:** backoff exponencial a partir do intervalo, dobrando a cada falha, com teto de 10 min
  antes da variação. Um sucesso zera a contagem. Um snapshot ilegível conta como falha, porque tentar de novo
  logo não o torna legível.
- **`Retry-After`:** quando é maior que a espera calculada, vence, com teto de 1 h (o máximo do contrato). A
  variação sobre ele é só para cima, de 0 a 10%: nunca antes do que o servidor pediu, e os jogadores que levaram
  o mesmo 429 não voltam todos no mesmo segundo.
- **Casos especiais:**
  - 304 conta como sucesso e renova a confirmação;
  - 404 (nunca publicado) volta ao intervalo normal;
  - 401 e 403 (credencial revogada ou recusada) mantêm o cache e tentam de novo no teto do backoff.
- **`RefreshAsync`:** sincroniza na hora, por exemplo quando o jogo volta do segundo plano. Para o jogo não furar
  o ritmo que protege a API, ele não sai antes de 15 s da última tentativa, do prazo de um `Retry-After` ou do
  teto depois de um 401/403. Nesses casos devolve `Skipped` sem chamar o servidor.
- **Encerramento:** `DisposeAsync` cancela o laço, inclusive uma sincronização em andamento, e espera ele
  terminar. `Dispose` só cancela, para o `OnDestroy` da Unity, que não pode esperar. Um cliente descartado não
  inicia de novo.
- **Tempo:** o relógio é o `TimeProvider`, tanto para as esperas do laço quanto para o limite do `RefreshAsync`
  (monotônico). Os testes do laço trocam só a espera, por uma que eles controlam, e afirmam o valor exato de
  cada uma; os do `RefreshAsync` usam o `FakeTimeProvider`.
- **Fronteira de transporte:** o transporte é uma porta. O adaptador .NET usa `HttpClient`; o Marco 5 traz o
  da Unity.
- **Credencial:** só vai no header `Authorization`. Nunca aparece em mensagem de erro, log ou arquivo do cache.

### Cache em disco

- O armazenamento é uma porta. O adaptador .NET grava um arquivo no diretório escolhido pelo jogo, e o da
  Unity, em `Application.persistentDataPath`.
- **Um arquivo por credencial:** o nome vem de um hash SHA-256 da credencial. Um cache de outro ambiente,
  deixado por um build anterior, nunca é lido por engano, e a credencial não vai para o disco. Trocar a
  credencial descarta o cache, o que é aceitável.
- O arquivo guarda o corpo recebido, a ETag e o instante da última confirmação.
- A gravação é atômica (arquivo temporário e troca). Um arquivo corrompido é ignorado, como se não existisse.
- **Adoção:** `StartAsync` lê o cache antes da primeira sincronização e o adota só se ainda não houver snapshot
  em uso. O cache nunca passa por cima do que veio do servidor. Mesmo expirado, ele é adotado, porque a ETag
  dele ainda pode render um 304 que o confirma de novo.
- **Confirmação:** um 304 confirma o snapshot em uso (origem `Server`) só se ele ainda for o snapshot cuja
  ETag foi enviada.
- **Gravação:** sempre que entra uma versão nova. A confirmação de um 304 só é gravada se a última confirmação
  gravada tiver mais de 1 h; gravar a cada minuto gastaria o disco do celular, e a idade guardada atrasa no
  máximo isso.
- **Falhas de disco** (cheio, sem permissão) não chegam ao jogo: ele segue com o snapshot em memória, e a
  próxima sincronização tenta gravar de novo.
- **Idade máxima:** é contada desde a última confirmação do servidor (200 ou 304), não desde a publicação.
  Ela atravessa reinícios do jogo, por isso usa o relógio de parede em UTC. Vale também para o snapshot em
  memória: um jogo aberto e sem rede por mais de 7 dias também cai no fallback.
- **Expirado:** a avaliação devolve o `defaultValue` com o motivo `SnapshotExpired`, e a origem `None`. O
  motivo fica no enum do núcleo, para o jogo ter um só vocabulário de motivos, mas só o SDK o produz.
- **Integridade:** o jogador pode editar o arquivo num aparelho com root. Isso não muda nada, porque o
  snapshot já é público por desenho, e decisões sensíveis ficam no backend do jogo (`ToggleForge.md`, seção 11).

### Resultado da avaliação

Além de valor, motivo, versão e bucket, o resultado traz a origem dos dados:

- **`Server`:** confirmado pelo servidor nesta execução;
- **`Cache`:** lido do disco e ainda não confirmado;
- **`None`:** sem snapshot válido; o resultado é o `defaultValue`.

## Consequências

- **Unity:** o jogo depende do pacote oficial do Newtonsoft e do Microsoft.Bcl.TimeProvider. O Marco 5 confere
  os dois num build IL2CPP.
- **Offline:**
  - um jogador offline por até 7 dias continua com a última configuração;
  - depois disso, o jogo volta aos seus valores padrão;
  - um rollback só chega ao aparelho na próxima sincronização, como o plano já admite.
- **Relógio do aparelho:** mudar o relógio afeta só a idade do cache, nunca a ordem das versões.
  - Adiantar o relógio expira o cache antes da hora.
  - Atrasar o relógio estende a validade, até o próximo contato com o servidor.
- **Carga na API:** com 60 s ± 10% e 304 quando nada mudou, cada jogador faz no máximo um pedido por minuto,
  quase sempre sem corpo. O backoff evita que uma API fora do ar receba uma onda de tentativas.
- **Plano das fatias:**
  1. tracer bullet: projeto, leitura, troca e avaliação, contra a API real;
  2. laço com variação, backoff e `Retry-After`;
  3. cache em disco e idade máxima;
  4. app demonstrativo;
  5. configurações JSON, com canvas próprio antes do código.

## Compliance

- **`SdkBoundaryTests`:**
  - `ToggleForge.Sdk` só referencia `ToggleForge.Evaluation`, Newtonsoft.Json e Microsoft.Bcl.TimeProvider;
  - nenhum tipo público do SDK expõe tipo do Newtonsoft.
- **`ToggleForge.Sdk.Tests`:** relógio falso e transporte em stub, sem rede real. Os testes cobrem:
  - resposta fora de ordem descartada;
  - 304 renova a confirmação sem trocar o snapshot;
  - snapshot inválido ou de algoritmo desconhecido mantém o anterior;
  - cache expirado cai no fallback com `SnapshotExpired`;
  - backoff com teto e `Retry-After`;
  - timeout;
  - a credencial nunca aparece em erro, log ou cache (canário).
- **Integração contra a API real (Testcontainers):** o SDK baixa, troca de versão a cada publicação e a cada
  rollback, e recebe 304 quando nada mudou.
