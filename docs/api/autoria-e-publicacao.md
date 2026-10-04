# Autoria e publicação: capacidades e ameaças (Marco 2)

Primeiro passo do design da API (DWA caps. 2–3): o que cada usuário precisa fazer, sem HTTP. Recursos,
paths e status saem daqui no passo seguinte, no contrato OpenAPI. As ameaças seguem o STRIDE (SA cap. 2)
e cada mitigação aponta o teste que a prova (SA cap. 12).

## Usuários

| Usuário | Quem é | O que precisa |
|---|---|---|
| Owner | Responsável pelo jogo no estúdio | Criar projeto e ambientes, gerenciar credenciais do SDK, tudo o que o Editor faz |
| Editor | Designer ou dev que opera as flags | Alterar o rascunho, simular, publicar, reverter |
| Viewer | Produtor, QA, suporte | Ler rascunho, publicações e histórico |
| Jogo | O SDK dentro do build, com uma credencial do ambiente | Baixar a configuração publicada, sem saber nada do rascunho |

Isolamento: organização → projeto → ambiente. No Marco 2 cada conta cria a própria organização e é Owner
dela; convite de membros fica para depois do tracer bullet, porque o isolamento já se prova com duas
organizações.

## Canvas

### 1. Preparar o projeto (Owner)

| Passo | Entradas | Sucesso | Falhas | Operação |
|---|---|---|---|---|
| Criar projeto | chave do projeto, nome | projeto com ambientes `dev` e `prod` e rascunhos vazios | chave inválida; chave já usada na organização | Criar projeto |
| Criar ambiente | projeto, chave, nome | ambiente com rascunho vazio | chave inválida ou repetida no projeto; limite de ambientes | Criar ambiente |
| Ver projetos | (a própria organização) | projetos e ambientes, com a versão publicada de cada ambiente | — | Listar projetos |

A chave segue o formato de `KeyFormat` (`ToggleForge.Evaluation`); ela entra no hash do rollout e,
por isso, é imutável depois de criada.

### 2. Configurar flags no rascunho (Editor)

| Passo | Entradas | Sucesso | Falhas | Operação |
|---|---|---|---|---|
| Ver o rascunho | ambiente | flags do rascunho, revisão atual e o que difere da versão publicada | ambiente inexistente ou de outra organização | Ler rascunho |
| Criar flag | ambiente, chave, ativa, valor ligado, valor desligado, percentual | flag no rascunho; seed gerada pelo servidor | chave inválida ou repetida; percentual fora de 0–100 com mais de 2 casas; limite de flags | Criar flag |
| Alterar flag | flag, revisão vista, campos alterados | flag alterada, nova revisão | revisão desatualizada (outro editor alterou); valor inválido | Alterar flag |
| Remover flag | flag, revisão vista | flag fora do rascunho | revisão desatualizada | Remover flag |

A chave e a seed não mudam depois de criadas: trocar qualquer uma redistribui os jogadores. Uma flag
removida e recriada com a mesma chave ganha seed nova.

### 3. Simular jogadores (Editor, Viewer)

| Passo | Entradas | Sucesso | Falhas | Operação |
|---|---|---|---|---|
| Simular um jogador | ambiente, rascunho ou versão publicada, ID do jogador | valor, motivo e bucket de cada flag | ID de jogador inválido (mesma regra do `PlayerId`); versão inexistente | Simular avaliação |

A simulação avalia cada flag com o mesmo código do SDK (`BooleanFlag.Evaluate`, que o `FlagEvaluator`
usa) e mostra o bucket do jogador quando ele decidiu o resultado. Ela explica, não grava nada. O ID do
jogador pode ser dado pessoal do estúdio, por isso vai no corpo e não volta na resposta.

### 4. Publicar (Editor)

| Passo | Entradas | Sucesso | Falhas | Operação |
|---|---|---|---|---|
| Revisar mudanças | ambiente | diferença entre rascunho e versão publicada, revisão do rascunho | — | Ler rascunho |
| Publicar | ambiente, revisão revisada, resumo opcional, chave de idempotência | versão N+1 imutável, que vira a atual; autor e data registrados | rascunho mudou depois da revisão; nada a publicar; repetição com outro conteúdo | Publicar rascunho |
| Repetir após falha de rede | a mesma chave e o mesmo conteúdo | o resultado original, sem nova versão | — | Publicar rascunho |

Rascunho, versão atual, chave de idempotência e auditoria são gravados na mesma transação
(`ToggleForge.md`, seção 6).

### 5. Acompanhar e reverter (Editor, Viewer)

| Passo | Entradas | Sucesso | Falhas | Operação |
|---|---|---|---|---|
| Ver histórico | ambiente, página | versões com autor, data, resumo e origem (publicação ou rollback) | — | Listar publicações |
| Ver uma versão | ambiente, versão | conteúdo completo da versão | versão inexistente | Ler publicação |
| Reverter | ambiente, versão de destino, versão atual vista, chave de idempotência | versão N+1 com o conteúdo da versão de destino, marcada como rollback | versão inexistente; outra publicação entrou antes; destino igual à atual | Reverter publicação |

Reverter cria uma versão nova e preserva o histórico (`ToggleForge.md`, regra 5).

### 6. Distribuir para o jogo (Owner, Jogo)

| Passo | Entradas | Sucesso | Falhas | Operação |
|---|---|---|---|---|
| Criar credencial | ambiente, nome | credencial com o segredo exibido uma única vez | limite de credenciais ativas | Criar credencial |
| Ver credenciais | ambiente | nome, prefixo, criação, último uso, situação; nunca o segredo | — | Listar credenciais |
| Revogar credencial | credencial | credencial recusada a partir de agora | — | Revogar credencial |
| Baixar configuração | credencial, versão que o jogo já tem | snapshot publicado do ambiente da credencial, ou "não mudou" | credencial inválida ou revogada; ambiente sem publicação | Baixar snapshot |

O snapshot só tem o que o jogo precisa para avaliar: chave, ativa, valores, percentual, seed, versão e
algoritmo. Nada de autor, resumo nem rascunho, porque a credencial é extraível do build
(`ToggleForge.md`, seção 11).

## Operações consolidadas

Criar projeto, Listar projetos, Criar ambiente, Ler rascunho, Criar flag, Alterar flag, Remover flag,
Simular avaliação, Publicar rascunho, Listar publicações, Ler publicação, Reverter publicação, Criar
credencial, Listar credenciais, Revogar credencial e Baixar snapshot. Entrar e sair da conta vêm do
framework de autenticação escolhido.

## Decisões do contrato

1. **Rollback e rascunho.** Reverter também substitui o rascunho pelo conteúdo restaurado. Se o rascunho
   tem mudanças não publicadas, a operação é recusada, a menos que o Editor confirme o descarte; assim a
   próxima publicação não reaplica a mudança que motivou o rollback. Reverter para um conteúdo igual ao
   atual é recusado como "já em vigor", pelo mesmo motivo da decisão 3.
2. **Lista de credenciais.** Só as ativas têm teto (5); as revogadas se acumulam. A lista mostra sempre
   todas as ativas e completa com as revogadas mais recentes até 50. As mais antigas saem da lista e
   continuam gravadas para auditoria.
3. **"Nada a publicar".** Publicar um rascunho idêntico à versão atual é recusado como conflito de
   estado, para o histórico não ganhar versões vazias.
4. **Forma das ações.** Publicação e rollback têm vida própria (histórico, consulta), então viram
   recursos nominalizados em vez de métodos customizados (ADR 0004).

## Ameaças (STRIDE)

| # | Ameaça | Mitigação | Teste que prova |
|---|---|---|---|
| S1 | Credencial do SDK extraída do build | Só lê o snapshot publicado de um ambiente; snapshot sem dado sensível; revogação | `A_credential_reads_only_its_own_environment` e `Revoked_credential_is_rejected_from_the_next_request_on` |
| S2 | Sessão do painel sequestrada | Cookie `HttpOnly`/`Secure`/`SameSite`, sessão nova no login, invalidada no logout, CSRF nas mutações | `Every_mutation_rejects_a_session_without_the_antiforgery_token`, `The_session_cookie_is_host_only_secure_http_only_and_strict` e `The_old_session_cookie_is_rejected_after_signing_out` |
| S3 | Conta criada com senha vazada (credential stuffing) | Senha nova conferida no Pwned Passwords; vazada é recusada (ADR 0007) | `A_password_found_in_breaches_is_rejected_on_password_and_creates_nothing` |
| S4 | Quem lê o banco forja um cookie de sessão | Key ring do Data Protection cifrado por um certificado montado fora do banco (ADR 0008) | `Every_key_in_the_database_is_encrypted_with_the_certificate` e `Without_the_certificate_that_protected_the_key_the_session_is_rejected` |
| T1 | Usuário altera recurso de outra organização (BOLA) | Toda consulta filtra pela organização da sessão; recurso alheio responde como inexistente | Teste A×B em cada rota: 404, corpo sem dados de A, recurso de A intacto |
| T2 | Cliente envia seed, versão, autor ou organização (mass assignment) | DTO de entrada estrito, campo desconhecido recusado; seed e versão gerados no servidor | `Unknown_fields_are_rejected_and_nothing_is_saved` |
| T3 | Edição concorrente sobrescreve outra (lost update) | Revisão esperada em toda alteração e na publicação; versão atual vista no rollback | `Second_update_with_stale_revision_conflicts_and_keeps_the_first` e `Simultaneous_rollbacks_from_the_same_version_apply_once` |
| R1 | Ninguém sabe quem publicou ou reverteu | Autor e data gravados na própria linha da publicação, na mesma transação | `A_published_flag_reaches_the_game_through_the_environment_credential` e `Rolling_back_publishes_the_target_content_as_a_new_version` |
| I1 | Segredo da credencial vaza por resposta ou log | Só o hash é guardado; segredo exibido uma vez; log com allowlist de headers | `The_credential_secret_never_reaches_the_logs` (canário) |
| I2 | Enumeração de projetos de outras organizações | IDs imprevisíveis e a mesma resposta para "não existe" e "é de outro" | Coberto pelo teste A×B de T1 |
| I3 | Erro expõe stack trace ou SQL | Problem Details genérico com referência opaca | `An_unexpected_error_hides_its_details_and_logs_them_under_the_reference` |
| I4 | Cadastro revela se um e-mail já tem conta | **Aceito** até existir confirmação por e-mail; o rate limit por IP limita a enumeração (ADR 0006) | `Probing_emails_through_sign_up_from_one_address_gets_429` e `Guessing_many_accounts_from_one_address_gets_429_with_Retry_After` |
| I5 | ID do jogador simulado (possível dado pessoal) vaza por URL, resposta, cache ou log | ID só no corpo, fora da resposta, `Cache-Control: no-store`, nenhum log com o corpo | `The_player_id_never_reaches_the_response_or_the_logs` (canário) |
| I6 | A checagem de senha vazada entrega a senha ou o e-mail a um terceiro | k-anonymity: só os 5 primeiros hex do SHA-1, com padding (ADR 0007) | `Only_the_first_5_hex_of_the_SHA1_leave_the_api_with_padding` (canário) |
| D1 | Rascunho gigante ou corpo enorme | Limite de flags por ambiente, de ambientes por projeto e de tamanho do corpo | Testes on/off point em cada limite |
| D2 | Polling agressivo do SDK | `ETag`/"não mudou", `Cache-Control`, rate limit por credencial e IP com `Retry-After` (ADR 0006) | `The_game_gets_304_while_the_snapshot_is_unchanged`, `A_game_client_over_the_limit_gets_429_with_Retry_After` e `Other_players_with_the_same_credential_keep_syncing` |
| E1 | Viewer publica ou Editor gerencia credenciais (BFLA) | Papel checado em cada operação, negado por padrão | Papel inferior em cada rota privilegiada → 403 |
| E2 | Retry duplica uma publicação ou um rollback | Chave de idempotência por (usuário, chave) com fingerprint da operação e do pedido, conferida com o ambiente travado | `PublishingTests` e `RollbackTests`: mesma chave e pedido → mesma resposta; pedido diferente → conflito; simultâneas → uma versão |
| E3 | Rota nova esquecida sem autenticação | Autenticação exigida por padrão, rotas públicas numa allowlist | `Only_the_allowlisted_operations_are_public` e `Every_other_route_answers_401_without_credentials` |
