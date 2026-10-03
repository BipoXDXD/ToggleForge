# 0006 — Rate limit global em memória, por tipo de cliente

- **Status:** Aceita
- **Data:** 2026-10-03

## Contexto

O contrato promete 429 com `Retry-After` em todas as operações, e duas linhas do STRIDE dependem disso:
I4 (o cadastro revela se um e-mail já tem conta, risco aceito enquanto o rate limit conter a enumeração) e
D2 (polling agressivo do SDK). O bloqueio de conta do Identity já cobre a força bruta numa conta só, em
qualquer IP; falta conter quem tenta muitas contas, sonda e-mails ou martela o snapshot.

O plano roda num nó até um problema medido pedir mais (`ToggleForge.md`, seção 4), mas o Azure Container
Apps pode subir réplicas. Todos os jogadores de um build usam a mesma credencial, que é extraível.

Alternativas consideradas:

1. **Onde ficam os contadores.**
   - **Em memória, com o middleware `RateLimiter` do ASP.NET Core:** sem dependência nova nem custo por
     requisição. Contra: cada réplica conta sozinha, e N réplicas multiplicam o limite por N.
   - **Postgres, com contador por janela num UPSERT atômico:** exato entre réplicas, sem infraestrutura
     nova. Contra: uma escrita por requisição, inclusive no polling do SDK, que é o tráfego que se quer
     conter.
   - **Redis:** distribuído e barato por requisição. Contra: infraestrutura nova nos testes e no deploy,
     cedo demais para um nó.
2. **Chave do limite do jogo.**
   - **Credencial + IP:** um cliente abusivo é contido sem afetar os outros jogadores do mesmo jogo.
     Contra: não barra um ataque distribuído com a credencial extraída.
   - **Só credencial:** um teto por jogo protege o servidor. Contra: um único cliente esgota a cota e
     derruba a sincronização de todos os jogadores daquele jogo, um DoS fácil com a credencial extraída.

## Decisão

**Contadores em memória, chave do jogo por credencial + IP** (escolhas do usuário). Um limitador global,
janela fixa de 1 minuto, sem fila, roda depois da autorização, quando já se sabe quem é o cliente:

| Cliente | Partição | Teto padrão por minuto |
|---|---|---|
| Membro do console (cookie de sessão) | conta | 300 |
| Jogo (credencial do SDK) | credencial + IP | 120 (CGNAT põe muitos jogadores atrás de um IP) |
| Sem sessão nem credencial: cadastro, login, token antiforgery | IP | 20 |

Acima do teto, a resposta é 429 em Problem Details com `Retry-After` até o fim da janela, de 1 a 3600
segundos como o contrato pede. Os tetos são options (`RateLimits:MemberPerMinute`,
`RateLimits:GameClientPerMinute`, `RateLimits:AnonymousPerMinute`), validados na partida: um valor abaixo
de 1 impede a API de subir.

"IP" quer dizer a rede do cliente: o endereço IPv4, ou o prefixo /64 no IPv6, porque um cliente IPv6
costuma ter o /64 inteiro e trocaria de endereço a cada requisição para escapar do teto. IPv4 mapeado
em IPv6 conta como o próprio IPv4.

A credencial entra num claim próprio na autenticação do SDK. O id da conta e o da credencial ficam só nas
chaves das partições, em memória; nenhum deles vai para log.

## Consequências

- Com N réplicas, cada teto vale N vezes. Aceitável enquanto o bloqueio de conta, que fica no banco,
  segura a força bruta. Quando houver mais de uma réplica, esta ADR é revista (Redis ou o rate limit do
  ingress).
- Requisições recusadas na autenticação (401) não passam pelo limitador, porque ele roda depois dela.
  Uma enxurrada de credenciais inválidas custa uma busca por hash cada; o ingress da plataforma é a
  primeira barreira para isso.
- **Antes do deploy:** atrás do ingress do Container Apps, o IP da conexão é o do proxy, e todos os
  clientes cairiam na mesma partição. O deploy precisa configurar `ForwardedHeaders` confiando só no
  proxy da plataforma, junto com o repositório das chaves do Data Protection (ADR 0005).
- Um ataque distribuído com a credencial extraída não é barrado por credencial; a defesa é revogar a
  credencial e, se preciso, um teto por ambiente medido depois.
- Os testes da API sobem com tetos altíssimos, porque todos vêm do mesmo "IP"; `RateLimitTests` usa
  tetos baixos e IPs simulados por um `IStartupFilter` de teste.

## Compliance

- `RateLimitTests`:
  - tentar várias contas do mesmo IP leva a 429 com `Retry-After`;
  - sondar e-mails pelo cadastro também;
  - cada IP tem o próprio teto anônimo, e o IPv6 conta pelo /64;
  - o membro passa do teto mesmo trocando de IP, sem bloquear os colegas;
  - o jogo passa do teto, sem afetar outro IP com a mesma credencial nem outra credencial no mesmo IP;
  - um teto abaixo de 1 impede a partida.
- `SessionTests` continua provando o bloqueio da conta em qualquer IP.
- Toda resposta 429 é conferida contra o contrato, inclusive o header `Retry-After`.
