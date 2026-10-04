# 0009 — Render mode do console

- **Status:** Aceita
- **Data:** 2026-10-03
- **Resolve:** a pendência de render mode da ADR 0003 e a revisão prevista na ADR 0004

## Contexto

O Marco 3 entrega o console: editor de flags, simulador, publicação e histórico (`ToggleForge.md`, seções 8
e 9). A ADR 0003 escolheu Blazor com Tailwind e deixou o render mode para agora. A escolha decide onde o
código do console roda e, por isso, como ele chega à API.

O que já existe e pesa na escolha:

- **A API é a porta de entrada.** O contrato (`docs/api/openapi.yaml`) e os testes cobrem autorização A×B,
  rate limit, `ETag`/`If-Match` e `Idempotency-Key` em `/api/v1`. O plano diz que o console consome a API
  por HTTP e não acessa o banco (ADR 0003).
- **A sessão é um cookie do navegador.** Ele é `HttpOnly`, `Secure` e `SameSite=Strict`, e toda mutação
  leva o token de `/api/v1/antiforgery-token` no header `X-XSRF-TOKEN` (ADR 0004). Fora da mesma origem,
  o cookie não acompanha a requisição.
- **Réplicas sem estado.** O Container Apps pode subir réplicas e as distribui sem afinidade. As chaves do
  Data Protection já são compartilhadas (ADR 0008), então um cookie emitido por uma réplica vale nas outras.
- **Usuários.** Membros do estúdio, em desktop, que abrem o console várias vezes por dia. O tempo do
  primeiro carregamento importa menos que a resposta de cada ação.

Alternativas consideradas:

1. **Interactive WebAssembly, global e sem prerender, servido pela API na mesma origem.**
   - Prós:
     - o console é só mais um cliente de `/api/v1`, com o cookie e o `X-XSRF-TOKEN` que a API já espera, e
       tudo o que os testes da API provam vale para ele;
     - mesma origem: sem CORS, e o `SameSite=Strict` continua funcionando;
     - servidor sem estado por usuário: sem afinidade de sessão, e um deploy não derruba quem está editando;
     - um container e um pipeline só.
   - Contras:
     - o primeiro acesso baixa o runtime .NET (alguns MB, que ficam em cache);
     - sem prerender, a tela fica em branco até o runtime subir, e o console precisa de uma tela de carregamento;
     - o código do console é público e pode ser inspecionado. Isso não é problema, porque ele não guarda
       segredo e a autorização fica toda na API;
     - a API passa a servir os arquivos do console e precisa de um fallback de rota que não alcance
       `/api` nem `/sdk`.
2. **Interactive Server** (circuito SignalR, componentes rodando no servidor).
   - Prós:
     - primeiro carregamento rápido e pequeno;
     - código do console fica no servidor, com a API completa do .NET.
   - Contras:
     - o estado de cada usuário fica na memória de uma réplica. O Container Apps passa a exigir afinidade de
       sessão, e cada deploy derruba os circuitos abertos;
     - o componente roda no servidor, longe do cookie do navegador. Ou ele chama os casos de uso direto,
       e o console vira uma segunda porta de entrada fora do contrato, do rate limit e dos testes de
       isolamento, ou chama a API por HTTP repassando a sessão. Nesse caso, o rate limit vê o IP do
       servidor, e a ADR 0004 precisa ser reaberta;
     - toda ação depende de ida e volta pelo WebSocket: com latência alta, o editor trava.
3. **Interactive Auto** (começa no servidor, passa para o WebAssembly depois do download).
   - Prós: primeiro acesso rápido e acessos seguintes no cliente.
   - Contras: soma os custos das duas opções anteriores. Exige afinidade de sessão, cada componente precisa
     funcionar nos dois ambientes e o acesso a dados precisa de duas implementações.
4. **Static SSR com formulários aprimorados** (sem interatividade).
   - Prós: o mais simples e o mais leve; sem runtime no navegador nem circuito.
   - Contras:
     - o simulador e o editor pedem resposta imediata a cada mudança, e cada ação vira um POST com a
       página recarregada;
     - os formulários chegam ao servidor, que precisa chamar os casos de uso direto. É o mesmo problema da
       segunda porta de entrada da opção 2.

## Decisão

**Interactive WebAssembly, global e sem prerender, servido pela API na mesma origem** (escolha do
usuário). Ela mantém a API como única porta de entrada e preserva a sessão por cookie e o servidor sem
estado. O custo é o download do runtime no primeiro acesso, aceitável para uma ferramenta interna aberta
várias vezes por dia.

- **Projeto `ToggleForge.Console`:** contém os componentes que rodam no navegador e não referencia nenhum
  projeto de `src/`. Domínio e casos de uso não vão para o navegador; a regra continua na API (o simulador
  usa `simulations`).
- **Contratos próprios:** o console declara os próprios records só com os campos que usa (tolerant
  reader). Ele não compartilha os DTOs da API: o contrato é o `openapi.yaml`, não um assembly.
- **Sessão:** o console pergunta à API quem está logado (`GET /api/v1/session`) e, a cada mutação, manda o
  token de `/api/v1/antiforgery-token` em `X-XSRF-TOKEN`. Um 401 em qualquer chamada leva ao login. A
  interface esconde o que o papel não permite, mas quem nega é a API.
- **Rotas:** a API serve os arquivos do console e devolve a página dele para qualquer rota fora de `/api` e
  `/sdk`. Rota desconhecida dentro delas continua 404 em Problem Details. O jeito de servir os arquivos
  (Blazor Web App com um projeto de cliente, ou WebAssembly standalone com fallback para `index.html`) é
  decidido no tracer bullet do console, pelo que deixar a API sem conhecer componentes.

## Consequências

- O console herda a autorização, o rate limit, a concorrência e a idempotência que os testes da API já
  provam. Nada disso é reimplementado na interface.
- Nenhuma afinidade de sessão no deploy. Um deploy troca os arquivos, e o navegador recarrega o console na
  próxima navegação.
- O primeiro acesso baixa o runtime. A página mostra um estado de carregamento até ele subir, e a publicação
  usa trimming e compressão.
- A pendência da ADR 0004 (rever a sessão caso o console rodasse em Interactive Server) deixa de existir:
  o console roda no navegador e usa o cookie como qualquer cliente.
- Testes do console: componentes com bUnit, sem API; jornadas com Playwright contra a API real com
  Postgres, como o `ToggleForge.md` previa.
- Fica pendente da ADR 0003: integração do Tailwind (CLI standalone ou npm) no build e no CI.

## Compliance

- Teste de arquitetura: `ToggleForge.Console` não referencia nenhum projeto de `src/`.
- Testes da API:
  - uma rota do console (por exemplo `/projects`) devolve a página do console;
  - rota desconhecida em `/api` ou `/sdk` continua 404 em Problem Details, nunca a página;
  - a página do console sai com `Content-Security-Policy` que só permite script da própria origem (com
    `wasm-unsafe-eval`, que o runtime exige).
- Jornada Playwright: login, criar flag, publicar e ver no histórico, tudo pela interface.
