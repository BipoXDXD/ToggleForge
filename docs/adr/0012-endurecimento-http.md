# 0012 — Endurecimento HTTP: headers de segurança, HSTS, teto de corpo e proxies confiáveis

- **Status:** Aceita
- **Data:** 2026-10-04
- **Complementa:** ADR 0004 (contrato e status), ADR 0006 (forwarded headers) e ADR 0009 (CSP do console)

## Contexto

A API e o console rodam no mesmo processo e na mesma origem (ADR 0009). No Azure Container Apps, o TLS termina
no ingress, e a API recebe HTTP. Uma auditoria contra a documentação do ASP.NET Core 10 (Kestrel security
considerations, Enforce HTTPS, Blazor CSP) e o OWASP REST Security Cheat Sheet achou estes problemas, todos
fáceis de explorar:

- o Kestrel anunciava `Server: Kestrel`;
- o corpo de qualquer requisição podia ter 30 MB, o padrão do Kestrel, e o System.Text.Json o leria inteiro.
  O STRIDE D1 dizia que havia limite de corpo, mas nenhum código o impunha;
- só as rotas do console tinham CSP. As respostas da API não tinham `nosniff`, proteção contra moldura,
  política de Referer nem HSTS;
- sem a rede do ingress declarada, a API ignora o `X-Forwarded-Proto` e trata toda requisição como HTTP, e o
  HSTS nunca sairia em produção.

Alternativas consideradas:

1. **Onde pôr os headers.**
   - **Middleware próprio que escreve em `OnStarting`:** um lugar só, e os headers sobrevivem ao exception
     handler, que limpa os headers antes do 500. Contra: código nosso, ainda que curto.
   - **Escrever na entrada da requisição, ou usar o `UseHsts` do framework:** mais simples. Contra: o 500
     sai sem os headers (provado por sabotagem).
   - **Deixar para o ingress:** sem código. Contra: o Container Apps não escreve esses headers, e o teste de
     integração não veria nada.
2. **Teto de corpo.**
   - **Um teto global no Kestrel, igual ao maior corpo legítimo (32 KiB, a flag JSON da ADR 0011):** um número
     só, e vale também para corpo chunked. Contra: o teste precisa de um Kestrel real, porque o TestServer
     não aplica o limite.
   - **Teto global menor (4 KiB) com override nas rotas de flag:** mais apertado. Contra: dois números e um
     override por endpoint que o TestServer não exercita.
3. **`max-age` do HSTS.**
   - **Constante de 1 ano:** o valor comum. Contra: o Learn recomenda começar curto na primeira implantação,
     porque o navegador guarda a política e um HTTPS quebrado não teria volta.
   - **Configuração, com 1 dia no primeiro deploy e 1 ano depois:** segue o Learn. Contra: mais uma chave
     de configuração para validar.
4. **Forwarded headers sem a rede do ingress.**
   - **Ignorar os headers, como hoje:** seguro contra falsificação. Contra: em produção, todos os clientes
     caem no IP do proxy e o HSTS some, sem nenhum erro visível.
   - **Recusar a partida fora de Development:** o erro de deploy aparece na hora. Contra: a configuração passa
     a ser obrigatória em todo ambiente que não seja de desenvolvimento.

## Decisão

**Headers em toda resposta**, escritos em `OnStarting` por `ApiSecurityHeaders`, depois dos forwarded headers:

| Header | Valor | Onde |
|---|---|---|
| `X-Content-Type-Options` | `nosniff` | todas |
| `X-Frame-Options` | `DENY` | todas (legado; a CSP já cobre) |
| `Referrer-Policy` | `no-referrer` | todas: a URL leva chaves de projeto e ambiente |
| `Permissions-Policy` | `camera=(), microphone=(), geolocation=(), payment=(), usb=()` | todas: o console não usa nenhum desses recursos |
| `Content-Security-Policy` | `default-src 'none'; frame-ancestors 'none'` | respostas da API |
| `Content-Security-Policy` | a do console (`script-src 'self' 'wasm-unsafe-eval'`, `frame-ancestors 'none'`...) | arquivos do console (ADR 0009) |
| `Cross-Origin-Opener-Policy` | `same-origin` | arquivos do console |
| `Cross-Origin-Resource-Policy` | `same-origin` | tudo fora de `/sdk` |
| `Strict-Transport-Security` | `max-age=<StrictTransportSecurity:MaxAge>` | só em HTTPS, fora de Development, exceto hosts de loopback |

O CORP só vale para cargas no-cors (img, script, iframe de outra origem); um fetch com CORS não passa por ele.
O `/sdk` fica de fora porque quem o lê é o jogo, fora do navegador ou num build WebGL de outra origem, e ali a
restrição não protegeria nada que a credencial já não proteja.

**HSTS configurável:** `StrictTransportSecurity:MaxAge`, de 1 segundo a 2 anos, validado na partida. O
`appsettings.json` traz **1 dia** para o primeiro deploy. Sem `includeSubDomains` nem `preload`: os dois prendem
domínios que a API não controla e são difíceis de desfazer.

**Teto global de 32 KiB por corpo** (`KestrelServerLimits.MaxRequestBodySize`). Acima dele, o binding do corpo
para e responde 413, em Problem Details pelas status code pages. O contrato declara `413` (`PayloadTooLarge`)
em toda operação que recebe corpo: `createAccount`, `signIn`, `createProject`, `createEnvironment`, `createFlag`,
`updateFlag`, `simulatePlayer`, `publishDraft`, `rollBack` e `createCredential`. Um teste de contrato exige o 413
em toda operação com corpo, então uma operação nova não escapa. Os demais limites do Kestrel
ficam no padrão, que o Learn considera adequado: 32 KB de headers, 100 headers, 30 s para recebê-los e
240 B/s mínimos no corpo.

**Sem `Server: Kestrel`** (`AddServerHeader = false`).

**Forwarded headers fechados:** só redes em `ForwardedHeaders:KnownNetworks` dizem o IP e o esquema do
cliente (ADR 0006). Fora de Development, a API não sobe em dois casos:

- com a lista vazia;
- com `ASPNETCORE_FORWARDEDHEADERS_ENABLED`, ou `DOTNET_FORWARDEDHEADERS_ENABLED`, valendo `true` (na configuração,
  a chave `ForwardedHeaders_Enabled`).

Segundo o Learn ("Kestrel security considerations" e "Configure ASP.NET Core to work with proxy servers and load
balancers"), essa variável faz o host ligar o middleware de forwarded headers por conta própria, com
`X-Forwarded-For` e `X-Forwarded-Proto`. Ela também limpa `KnownProxies` e `KnownIPNetworks`, de modo que todo
remetente passa a ser confiável. As notas do ASP.NET Core 3.0 dizem que as imagens de container oficiais a
definem como `true`; o Dockerfile do Marco 6 precisa conferir a imagem base.

**CORS continua desligado:** o console está na mesma origem, e nenhum site alheio lê resposta da API, nem com
o cookie do membro. Liberar uma origem passa a exigir uma ADR.

**HTTPS sem redirect:** o Learn desaconselha redirect em API, porque o cliente já mandou o segredo em claro.
O ingress recusa HTTP (`allowInsecure: false`).

## Consequências

- O 413 em Problem Details sai hoje com `type: about:blank`. A Fatia 4 o troca por
  `/problems/payload-too-large` no ponto que monta o Problem Details das status code pages.
- Todo ambiente fora de Development precisa declarar `ForwardedHeaders:KnownNetworks`, inclusive os testes
  que sobem em Production.
- **Checklist do Marco 6 (deploy):** depois do primeiro deploy estável em HTTPS, subir
  `StrictTransportSecurity:MaxAge` para `365.00:00:00`. Definir `AllowedHosts` com o domínio público. Se a imagem
  base definir `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, a API não sobe: sobrescreva a variável com `false` no
  Dockerfile ou no Container Apps.
- Um corpo acima de 32 KiB numa operação futura exige subir o teto global ou um override por endpoint, e uma
  revisão desta ADR.
- Os testes do header `Server` e do teto de corpo rodam num Kestrel real (`BrowserHost`), porque o TestServer
  não aplica nenhum dos dois.

## Compliance

- `SecurityHeadersTests`: `Every_response_tells_the_browser_not_to_sniff_frame_or_refer`,
  `Api_responses_forbid_loading_anything_and_being_framed`, `Responses_outside_the_sdk_load_only_into_the_same_origin`,
  `The_sdk_route_does_not_restrict_its_loading_origin`, `The_console_page_keeps_its_browsing_context_to_itself` e
  `An_unexpected_error_keeps_the_headers`.
- `ConsoleHostingTests.The_console_page_allows_scripts_only_from_its_own_origin` e as jornadas Playwright de
  `ConsoleJourneyTests`, que reprovam qualquer violação de CSP.
- `StrictTransportSecurityTests`: `Outside_development_https_responses_pin_https_for_the_configured_day`,
  `The_pin_lasts_as_long_as_configured`, `A_pin_duration_out_of_range_keeps_the_api_from_starting`,
  `In_development_no_response_pins_https`, `Behind_the_ingress_that_ended_tls_the_response_pins_https`,
  `An_untrusted_sender_claiming_https_gets_no_pin` e `An_unexpected_error_still_pins_https`.
- `KestrelHardeningTests`: `No_response_names_the_server_software`, `A_body_of_exactly_32_KiB_is_read` e
  `A_body_over_32_KiB_is_refused_before_anything_is_created`, que também conferem as respostas contra o contrato,
  e `Every_operation_that_takes_a_body_declares_413`.
- `ForwardedHeadersTests.Outside_development_the_api_does_not_start_without_the_ingress_network` e
  `Outside_development_the_api_does_not_start_with_forwarded_headers_opened_by_the_host`, além dos testes da
  ADR 0006.
- `CrossOriginTests`: `Another_site_cannot_read_an_api_response`, `Another_site_gets_no_preflight_approval_for_a_mutation`,
  `Another_site_cannot_read_a_signed_in_members_session` e `A_preflight_for_the_session_from_another_site_is_not_approved`.
- CI: o Spectral com o ruleset OWASP sobre o `openapi.yaml`.
