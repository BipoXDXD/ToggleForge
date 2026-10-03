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

A simulação usa o mesmo `FlagEvaluator` do SDK; ela explica o resultado, não grava nada.

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
   próxima publicação não reaplica a mudança que motivou o rollback.
2. **"Nada a publicar".** Publicar um rascunho idêntico à versão atual é recusado como conflito de
   estado, para o histórico não ganhar versões vazias.
3. **Forma das ações.** Publicação e rollback têm vida própria (histórico, consulta), então viram
   recursos nominalizados em vez de métodos customizados (ADR 0004).

## Ameaças (STRIDE)

| # | Ameaça | Mitigação | Teste que prova |
|---|---|---|---|
| S1 | Credencial do SDK extraída do build | Só lê o snapshot publicado de um ambiente; snapshot sem dado sensível; revogação | `Credential_of_one_environment_cannot_read_another` e `Revoked_credential_is_rejected` |
| S2 | Sessão do painel sequestrada | Cookie `HttpOnly`/`Secure`/`SameSite`, sessão nova no login, invalidada no logout, CSRF nas mutações | `Mutation_without_csrf_token_is_rejected` e `Old_session_is_rejected_after_logout` |
| T1 | Usuário altera recurso de outra organização (BOLA) | Toda consulta filtra pela organização da sessão; recurso alheio responde como inexistente | Teste A×B em cada rota: 404, corpo sem dados de A, recurso de A intacto |
| T2 | Cliente envia seed, versão, autor ou organização (mass assignment) | DTO de entrada estrito, campo desconhecido recusado; seed e versão gerados no servidor | `Unknown_fields_are_rejected_and_nothing_is_saved` |
| T3 | Edição concorrente sobrescreve outra (lost update) | Revisão esperada em toda alteração e na publicação | `Second_update_with_stale_revision_conflicts_and_keeps_the_first` |
| R1 | Ninguém sabe quem publicou ou reverteu | Auditoria com autor e data na mesma transação da publicação | `Publication_records_author_in_the_same_transaction` |
| I1 | Segredo da credencial vaza por resposta ou log | Só o hash é guardado; segredo exibido uma vez; log com allowlist de headers | Canário semeado no segredo nunca aparece em resposta nem em log |
| I2 | Enumeração de projetos de outras organizações | IDs imprevisíveis e a mesma resposta para "não existe" e "é de outro" | Coberto pelo teste A×B de T1 |
| I3 | Erro expõe stack trace ou SQL | Problem Details genérico com referência opaca | `Unexpected_error_hides_internal_details` |
| I4 | Cadastro revela se um e-mail já tem conta | **Aceito** até existir confirmação por e-mail; o rate limit por IP limita a enumeração | `Account_creation_over_the_limit_gets_429` |
| D1 | Rascunho gigante ou corpo enorme | Limite de flags por ambiente, de ambientes por projeto e de tamanho do corpo | Testes on/off point em cada limite |
| D2 | Polling agressivo do SDK | `ETag`/"não mudou", `Cache-Control`, rate limit por credencial com `Retry-After` | `Sdk_over_the_limit_gets_429_with_retry_after` |
| E1 | Viewer publica ou Editor gerencia credenciais (BFLA) | Papel checado em cada operação, negado por padrão | Papel inferior em cada rota privilegiada → 403 |
| E2 | Retry duplica uma publicação | Chave de idempotência por (usuário, chave) com fingerprint do conteúdo e reserva atômica | Mesma chave e conteúdo → mesma resposta; conteúdo diferente → conflito; duas simultâneas → uma versão |
| E3 | Rota nova esquecida sem autenticação | Autenticação exigida por padrão, rotas públicas numa allowlist | Teste que enumera as rotas e exige 401 fora da allowlist |
