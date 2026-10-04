# Configurações JSON: capacidades e ameaças (Marco 4)

Primeiro passo do design (DWA caps. 2–3), sem HTTP, para a flag do tipo JSON da ADR 0011. Só entra o que muda
em relação a `autoria-e-publicacao.md`: usuários, isolamento, publicação, rollback e credenciais seguem
iguais. Recursos, campos e status saem daqui para o `openapi.yaml`, na fatia da API. As ameaças seguem o STRIDE
(SA cap. 2), e cada mitigação aponta o teste que a prova (SA cap. 12). Os nomes dos testes são os que as
fatias vão criar.

## Usuários

Os mesmos de `autoria-e-publicacao.md`. O que muda para cada um:

| Usuário | O que precisa a mais |
|---|---|
| Editor | Criar e editar flags JSON no rascunho, com o erro apontado no valor certo |
| Viewer | Ler o valor JSON no rascunho, na simulação e no histórico |
| Jogo | Receber o valor JSON e lê-lo com a própria classe, caindo no valor padrão se algo der errado |

## Canvas

### 1. Configurar uma flag JSON no rascunho (Editor)

| Passo | Entradas | Sucesso | Falhas | Operação |
|---|---|---|---|---|
| Criar flag JSON | ambiente, chave, tipo `json`, ativa, valor ligado, valor desligado, percentual | flag no rascunho com os valores compactados; seed gerada pelo servidor | as da flag booleana; valor que não é JSON; chave duplicada num objeto; mais de 8 níveis; mais de 4 KiB compactado; número fora do `double`; soma do ambiente acima de 128 KiB | Criar flag |
| Alterar flag JSON | flag, revisão vista, campos alterados | flag alterada, nova revisão | as da flag booleana; valor do tipo errado; tentativa de mudar o tipo; os limites acima | Alterar flag |

Sem o tipo, a flag nasce booleana, como hoje. O tipo não muda depois da criação: um build publicado lê a flag
com uma classe dele.

### 2. Simular um jogador (Editor, Viewer)

| Passo | Entradas | Sucesso | Falhas | Operação |
|---|---|---|---|---|
| Simular um jogador | as de hoje | para cada flag, o tipo, o valor (JSON de verdade, não texto), o motivo e o bucket | as de hoje | Simular avaliação |

### 3. Baixar a configuração (Jogo)

| Passo | Entradas | Sucesso | Falhas | Operação |
|---|---|---|---|---|
| Baixar configuração | as de hoje | snapshot com o tipo de cada flag e os valores JSON | as de hoje | Baixar snapshot |
| Ler uma flag JSON | chave, contexto do jogador, valor padrão, parser do jogo | o valor lido pela classe do jogo, com motivo, versão e origem | flag ausente, de outro tipo ou com valor que o parser recusa → valor padrão com o motivo | (no SDK, sem HTTP) |

Um SDK antigo que encontra um tipo que não conhece ignora aquela flag e segue com as outras.

## Decisões do contrato

1. **Aditivo.** `type` é opcional na criação, com padrão `boolean`. As respostas ganham `type`, e o snapshot
   pode omiti-lo (ausente quer dizer `boolean`) para os snapshots já publicados continuarem válidos.
2. **Valores como JSON.** `onValue`, `offValue` e o valor simulado viajam como JSON, não como string com JSON
   dentro. Assim o console e o jogo não fazem um parse duplo.
3. **Compactação no servidor.** O valor volta compacto, com a ordem das chaves preservada; quem edita
   reformata.
4. **O teto do ambiente é conflito de estado.** Passar dos 128 KiB é 409 `limit-reached`, como passar de 200
   flags. Um valor acima de 4 KiB é erro de entrada, 400 no campo.

## Ameaças (STRIDE)

| # | Ameaça | Mitigação | Teste que prova |
|---|---|---|---|
| T4 | O cliente troca o tipo de uma flag publicada e quebra builds antigos (mass assignment) | `type` só na criação; o DTO de alteração recusa o campo | `Changing_the_type_of_a_flag_is_rejected_and_nothing_is_saved` |
| T5 | O console e o jogo leem valores diferentes do mesmo JSON | Recusar chave duplicada e número fora do `double`; compactar no servidor; o SDK recusa o snapshot com chave repetida ou valor que um parser comum não leria. **Risco residual aceito:** a fração passa pelo `double` e chega ao jogo como o `double` mais próximo (mais de 17 dígitos arredondam) | `A_duplicated_key_is_rejected_at_any_depth`, `A_number_out_of_double_range_is_rejected`, `A_key_repeated_inside_a_json_value_refuses_the_snapshot` e `A_json_value_a_standard_parser_would_not_read_refuses_the_snapshot` |
| T6 | Um valor JSON injeta script no console (XSS) | Blazor codifica a saída; nada de `MarkupString` com valor do usuário | `A_json_value_with_markup_is_shown_as_text` |
| I7 | O estúdio põe um segredo numa configuração, e ele chega a todo jogador | **Aceito**: a credencial sai do build (`ToggleForge.md`, seção 11). O editor avisa que o valor é público para quem tem o jogo | `The_json_editor_warns_that_values_reach_every_player` |
| D3 | Um JSON gigante ou profundo pesa na API, no banco e em cada download | 32 KiB por corpo, 4 KiB e 8 níveis por valor, 128 KiB por ambiente | Testes on/off point em cada limite e `A_body_over_32_KiB_gets_413` |
| D4 | Duas criações simultâneas passam juntas do teto do ambiente | O teto é invariante do aggregate, gravado com a revisão do rascunho | `Concurrent_creations_cannot_exceed_the_environment_json_budget` |
| D5 | Um tipo de flag novo derruba todos os builds antigos | O SDK ignora a flag de tipo desconhecido em vez de recusar o snapshot | `A_flag_of_an_unknown_type_is_skipped_and_the_rest_still_applies` |
| D6 | Um parser do jogo que lança derruba o jogo | O núcleo captura a exceção e devolve o valor padrão com `InvalidValue` | `A_parser_that_throws_falls_back_with_InvalidValue` e, no SDK, `A_parser_that_throws_gives_the_game_its_default_with_InvalidValue` |
