# 0004 — API HTTP com Minimal APIs, Identity por cookie e contrato spec-first

- **Status:** Aceita; emendada pela ADR 0005 (validação de entrada e sessão)
- **Data:** 2026-10-03

## Contexto

O Marco 2 expõe autoria, publicação e distribuição por HTTP. As capacidades e as ameaças estão em
`docs/api/autoria-e-publicacao.md`. Os consumidores são dois: o console Blazor (Marco 3), operado por
membros do estúdio, e o SDK dentro do jogo (Marco 4). Os guias são The Design of Web APIs 2e, API Design
Patterns e Secure APIs (ver `CLAUDE.md`).

Alternativas consideradas:

1. **Estilo de endpoint.**
   - **Controllers:** filtros e model binding prontos, como nos exemplos de Esposito. Contra: mais
     código e atributos por endpoint.
   - **Minimal APIs:** route groups e `TypedResults`, com os tipos de resposta checados pelo
     compilador, e validação nativa no .NET 10 (`AddValidation`). Contra: convenções transversais montadas à mão.
2. **Autenticação dos membros.**
   - **ASP.NET Core Identity com cookie:** sem serviço externo e testável com
     `WebApplicationFactory`. Contra: senhas ficam no nosso banco, com o hashing do framework.
   - **OIDC externo:** nenhuma senha guardada e MFA pronto. Contra: dependência externa no dev, no CI e
     no deploy.
3. **Forma das ações.**
   - **Métodos customizados (`:publish`):** padrão do ADP cap. 9.
   - **Recursos nominalizados (`publications`):** padrão preferido no DWA 4.7.

## Decisão

**Minimal APIs.** Um route group por área, `TypedResults` em todo handler e `AddValidation` para o
formato da entrada (substituído pelo parse no endpoint na ADR 0005). O endpoint só lê a requisição, chama o caso de uso e traduz o resultado; regra de
negócio fica no domínio. No .NET 10, endpoints de API com cookie respondem 401/403 em vez de redirecionar
para o login.

**Membros: ASP.NET Core Identity com cookie**, por endpoints próprios em `/api/v1/session` que chamam o
`SignInManager`. O `MapIdentityApi` fica de fora porque publica de uma vez confirmação de e-mail, reset de
senha, 2FA e refresh token, uma superfície que o Marco 2 não usa (SA cap. 3). O cookie é `HttpOnly`,
`Secure` e `SameSite=Strict`, a sessão é renovada no login e toda mutação exige o token antiforgery no
header `X-XSRF-TOKEN`.

**Jogo: credencial por ambiente.** `Authorization: Bearer tfsdk_<segredo>`, com 256 bits de um CSPRNG.
O banco guarda só o SHA-256 do segredo: com essa entropia, um hash lento não acrescenta proteção. A
credencial só lê o snapshot publicado do próprio ambiente, porque é extraível do build.

**Contrato:**

- Base paths `/api/v1` (console) e `/sdk/v1` (jogo). Uma versão só, mudanças aditivas (ADP cap. 24).
- `docs/api/openapi.yaml` (OpenAPI 3.1) é a fonte da verdade e é revisado antes do código (DWA caps. 6–7).
- Identificação:
  - organização por UUID v4 gerado no servidor;
  - projeto, ambiente e flag pelas próprias chaves, que já são imutáveis, não sensíveis e fazem parte do
    hash do rollout. Um segundo ID só duplicaria a identidade.
- Ações com vida própria viram recursos nominalizados: `publications` (publicar), `rollbacks` (o
  resultado é uma publicação), `simulations` e `revocation` da credencial. Nunca `PATCH` em status.
- Erros em Problem Details (RFC 9457) com `errors[]` exaustivo.
  - Status por situação:

    | Situação | Status |
    |---|---|
    | Toda entrada inválida | 400 |
    | Conflito de estado | 409, com `type` próprio |
    | `If-Match` desatualizado | 412 |
    | `If-Match` ausente | 428 |
    | Recurso de outra organização | 404, igual ao inexistente |
    | Papel insuficiente na própria organização | 403 |

  - Campo desconhecido no corpo é recusado: proteção contra mass assignment (SA cap. 6).
- Concorrência:
  - `ETag`/`If-Match` obrigatório para alterar e remover flags;
  - a publicação leva no corpo a revisão do rascunho que o Editor revisou.
- Idempotência:
  - `Idempotency-Key` obrigatório em publicar e reverter;
  - a chave tem escopo (usuário, chave) e guarda o fingerprint do corpo;
  - mesma chave com o mesmo corpo devolve a resposta original; com outro corpo, 409 (ADP cap. 26).
- O percentual trafega como `rolloutBasisPoints` (inteiro 0–10000), sem ponto flutuante.
- Histórico paginado por token opaco, com `maxPageSize` padrão 20 e teto 100 (ADP cap. 21). As outras
  listas são limitadas por regra de negócio e não paginam.

## Consequências

- O contrato descreve quem vê o quê em cada operação, e cada linha do STRIDE ganha um teste de
  integração na implementação.
- Chaves imutáveis no path: renomear um projeto muda o nome de exibição, nunca a chave. É o preço de
  manter o bucket do rollout estável.
- Sem `MapIdentityApi`, login, logout e cadastro são código nosso sobre o `SignInManager`, com rate
  limit e resposta igual para conta existente e inexistente.
- O console do Marco 3 precisa buscar e reenviar o token antiforgery. Ele roda no navegador, em
  WebAssembly na mesma origem, e usa o cookie como qualquer cliente (ADR 0009).

## Compliance

- CI: `spectral lint` na spec, com o ruleset OWASP.
- Na implementação:
  - teste que enumera as rotas registradas e exige 401 fora da allowlist pública;
  - testes A×B de isolamento em cada rota;
  - teste de contrato que confere as respostas contra a spec.
