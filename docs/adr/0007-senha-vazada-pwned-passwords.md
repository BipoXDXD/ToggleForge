# 0007 — Senhas vazadas recusadas pelo Pwned Passwords

- **Status:** Aceita
- **Data:** 2026-10-03

## Contexto

O NIST 800-63B pede que senhas novas sejam conferidas contra listas de senhas vazadas, além do tamanho
mínimo (12, já exigido). Conta com senha vazada é a porta do credential stuffing. A checagem exige uma
lista, local ou de um serviço externo, e qualquer serviço externo vira dependência do cadastro.

Alternativas consideradas:

1. **API Pwned Passwords (Have I Been Pwned), por k-anonymity:** gratuita e sem chave, com bilhões de
   senhas. Só os 5 primeiros hex do SHA-1 saem, e a resposta traz centenas de sufixos para comparar
   localmente. Contra: dependência externa no caminho do cadastro.
2. **Lista local das senhas mais comuns** (por exemplo, as 100 mil do SecLists): sem rede e determinística.
   Contra: cobre muito menos e cresce o repositório.
3. **Não checar:** risco aceito, com tamanho mínimo, bloqueio de conta e rate limit. Contra: contraria a
   recomendação do NIST.

## Decisão

**Pwned Passwords por k-anonymity** (escolha do usuário), como um `IPasswordValidator` do Identity: vale
para toda senha nova, inclusive a troca de senha quando ela existir.

- **O que sai:** só o prefixo de 5 hex do SHA-1, com o header `Add-Padding: true`, para o tamanho da resposta
  não revelar o prefixo. Senha, resto do hash e e-mail nunca saem. Linhas de padding (contagem 0) não contam
  como vazamento.
- **Cliente:** `PwnedPasswordsClient` é um typed client do `IHttpClientFactory` com o
  `AddStandardResilienceHandler`. Cada tentativa tem 1 segundo, com uma nova tentativa, e o total fica em 2
  segundos.
- **Fail-open:** se o serviço falhar, travar ou estiver com o circuito aberto, a senha é aceita e um aviso
  vai para o log, sem a senha. O cadastro não para por causa de um terceiro. A checagem é uma camada a mais,
  não a única.
- **Resposta:** senha encontrada é 400 em `/password`, e nada é criado.

## Consequências

- O cadastro pode levar até 2 segundos a mais quando o serviço está lento.
- Durante uma indisponibilidade do HIBP, senhas vazadas passam; o aviso no log mostra quando isso acontece.
- O SHA-1 aparece no código só como identificador do protocolo (supressão do CA5350 com justificativa). A
  senha guardada continua com o hashing do Identity.
- Os testes nunca chegam à internet: um fake responde por todos os clientes HTTP.

## Compliance

- `BreachedPasswordTests`:
  - senha vazada vira 400 em `/password` e não cria a conta;
  - sufixo que só vem como padding não conta;
  - só o prefixo sai, com padding, e o canário da senha, o resto do hash e o e-mail nunca aparecem;
  - serviço com falha ou travado: a senha é aceita, a requisição é abandonada no timeout e o aviso vai para
    o log.
