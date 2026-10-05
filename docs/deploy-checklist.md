# Checklist da primeira implantação (Marco 6)

Pendências de segurança e operação registradas até o Marco 4, para o deploy no Azure Container Apps. Cada item diz
o que fazer, por quê e como conferir. Marque e date cada item no PR da implantação.

## Rede e HTTPS

- [ ] **`ASPNETCORE_ENVIRONMENT` ausente ou `Production`, na API e nos dois jobs.** Com `Development`, o
      `KnownNetworks` obrigatório, a recusa de `ASPNETCORE_FORWARDEDHEADERS_ENABLED`, o certificado do Data Protection
      e o HSTS deixam de valer, e a API sobe sem erro. O padrão do host é `Production`, mas uma variável esquecida no
      Container Apps ou no Dockerfile desliga tudo em silêncio. Conferir: a API não sobe sem
      `ForwardedHeaders:KnownNetworks`.
- [ ] **Ingress só em HTTPS** (`allowInsecure: false`). O ingress redireciona HTTP para HTTPS; a API não faz esse
      redirect (o Learn não o recomenda para APIs, porque o cliente já mandou o segredo em claro). Conferir:
      `curl -i http://<domínio>/api/v1/antiforgery-token` (rota pública) volta o redirect do ingress, sem chegar à
      API. Ainda não há health check para esse teste.
- [ ] **`ForwardedHeaders:KnownNetworks` com a menor faixa que contém o ingress** (CIDR), que é a subnet de
      infraestrutura do ambiente do Container Apps, e não a `10.0.0.0/8` inteira: qualquer máquina de uma faixa
      confiável escolhe o próprio `X-Forwarded-For` e escapa do rate limit. Fora de Development a API não sobe sem
      ela: sem a rede, o rate limit juntaria todos os clientes no IP do proxy e o HSTS nunca sairia. Conferir: a API
      sobe, um 429 forçado de um IP não bloqueia outro, e um `X-Forwarded-For` forjado de dentro da rede privada não
      muda o IP contado.
- [ ] **`ASPNETCORE_FORWARDEDHEADERS_ENABLED` e `DOTNET_FORWARDEDHEADERS_ENABLED` ausentes ou diferentes de
      `true`.** Com `true`, o host liga um segundo middleware de forwarded headers e limpa
      `KnownProxies`/`KnownIPNetworks`. Fora de Development a API se recusa a subir com ele ligado (na
      configuração, a chave é `ForwardedHeaders_Enabled`; só o literal `true` conta). Conferir se a imagem base
      define a variável e sobrescrever com `false` no Dockerfile ou no Container Apps.
- [ ] **HSTS curto no primeiro deploy.** `StrictTransportSecurity:MaxAge` (variável
      `StrictTransportSecurity__MaxAge`) vale `1.00:00:00` no `appsettings.json`, de 1 segundo a 730 dias. Depois do
      certificado gerenciado e do domínio estáveis, suba para `365.00:00:00`. Sem `includeSubDomains` nem `preload`.
      Um HSTS longo com o certificado quebrado prende os navegadores fora do console.
- [ ] **`AllowedHosts` com o domínio público** por variável de ambiente, como defesa em profundidade (hoje `"*"`;
      a API só gera `Location` relativos). O host filtering responde 400 a um `Host` fora da lista, e os probes
      chegam com o IP do contêiner no `Host`. Conferir que os probes passam com o `AllowedHosts` restrito, ou tirar
      as rotas de health do host filtering.
- [ ] **CORS continua desligado.** O console está na mesma origem; liberar uma origem exige uma ADR nova, que
      revise a 0012.

## Segredos e chaves

- [ ] **Connection string** por segredo do Container Apps (ou Key Vault), nunca na imagem nem no repositório.
- [ ] **Certificado do Data Protection** montado como arquivo (`DataProtection:CertificatePath` e
      `DataProtection:KeyPath`); fora de Development a API não sobe sem ele (ADR 0008). Rotação: o certificado novo
      vira o atual, e o antigo vai para `DataProtection:PreviousCertificatePath` e `PreviousKeyPath`, onde só
      decifra, até as chaves que ele cifrou expirarem.
- [ ] Conferir que o `wwwroot` publicado do console só tem `_framework`, `css` e `index.html`, sem `appsettings`
      nem `.pdb`.

## Banco e jobs

- [ ] **Postgres gerenciado em rede privada**, sem IP público; backup com PITR e uma restauração ensaiada.
- [ ] **Migrations num job do Container Apps com gatilho manual, antes do rollout** (`--migrate`), nunca no boot da
      API. O job precisa da connection string e do certificado do Data Protection (a API monta o Data Protection
      antes de ler o comando), mas não de `KnownNetworks` nem de `MaxAge`, que só são validados na partida da API.
- [ ] **Dois usuários no banco.** O job do `--migrate` usa um usuário com DDL (e, se o banco não existir, com
      permissão de criá-lo: o `--migrate` cria o banco que falta). A API usa um usuário só com DML nas tabelas dos
      módulos e uso das sequências, sem DDL.
- [ ] **Expand/contract a partir daqui:** o `Initial` não é mais regenerado, e apertar um limite de valor JSON
      (bytes, níveis, números) exige migrar os dados antes (ADR 0011). Qualquer tabela como a
      `published_flag_types` nasce preenchida a partir dos dados que já existem (para ela, de
      `publication_flags`), por uma migration expand/contract.
- [ ] **job agendado de hora em hora** (CronJob no Kubernetes) com `--purge-idempotency-records`, que esquece as
      chaves com mais de 24 h.

## Headers e limites (já no código, conferir no ambiente)

- [ ] `nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`,
      `Permissions-Policy: camera=(), microphone=(), geolocation=(), payment=(), usb=()` e CSP em toda resposta.
      A CSP do console permite scripts só da própria origem (`'wasm-unsafe-eval'`). O console leva
      `Cross-Origin-Opener-Policy: same-origin`, e tudo fora de `/sdk` leva `Cross-Origin-Resource-Policy:
      same-origin`.
- [ ] Sem `Server: Kestrel` nas respostas.
- [ ] Corpo acima de 32 KiB → 413 em Problem Details, também atrás do ingress. Um corpo chunked pode levar 413
      um pouco antes do teto. Os demais limites do Kestrel ficam no padrão (32 KB de headers, 100 headers, 30 s
      para recebê-los, 240 B/s mínimos no corpo).
- [ ] Health probes: liveness sem o banco, readiness com o banco; o SIGTERM termina as requisições em andamento.
      Os endpoints de health ainda não existem: entram no Marco 6.

## Decisões em aberto registradas

- **Cache privado com e-mail de colegas:** o histórico usa `private, no-cache`, então o e-mail dos membros fica no
  cache em disco do navegador de quem o abriu. Foi o trade-off do contrato (ETag); revisar se o piloto pedir.
- **Unity WebGL lendo o snapshot de outra origem:** decide se o `/sdk` precisa de CORS antes de um build WebGL.
- **Logs:** a API não loga requisições. Se o HTTP logging for ligado, usar allowlist de headers, nunca
  `Authorization` nem `Cookie`.
- **Snapshot relido a cada poll:** cada sincronização relê e revalida a publicação inteira, até para responder
  304. Medir com tráfego real antes de otimizar.
