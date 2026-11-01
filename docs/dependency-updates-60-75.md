# ToggleForge — relatório final dos PRs #60 a #75

Registro histórico das atualizações realizadas entre 31/10 e 01/11/2026. O recorte termina no merge do #75, commit `15b3d6c58395d027ba86b90adb808689334ebd9e`, e nas verificações do Dependabot sobre esse conteúdo. Estados, SHAs e resultados foram reconferidos em 01/11/2026 às 04:48 BRT (07:48 UTC). Este documento foi adicionado posteriormente; as afirmações de estado abaixo se referem a esse recorte.

## Resultado final

O intervalo contém **15 PRs mesclados e um fechado sem merge (#69)**. O bump de Http proposto no #69 foi incorporado ao #70. Os dois episódios de NU1109 foram corrigidos sem desativar o gerenciamento centralizado ou as proteções existentes. Os grupos `dataprotection-crypto` e `http-resilience` foram integrados e preservados nas atualizações seguintes.

A integração terminou na main em [`15b3d6c58395d027ba86b90adb808689334ebd9e`](https://github.com/BipoXDXD/ToggleForge/commit/15b3d6c58395d027ba86b90adb808689334ebd9e), com árvore `b05cdeffd793eddfc4adef87f1ef9995f364f8b4`. Seu [CI final, 41280350602](https://github.com/BipoXDXD/ToggleForge/actions/runs/41280350602), passou nos três jobs, com **1.711 testes aprovados, 0 falhas e 0 ignorados**. Os 15 heads mesclados e seus commits de merge são ancestrais dessa main.

A análise do Dependabot sobre esse mesmo SHA não encontrou atualizações elegíveis nos ecossistemas configurados. Na consulta de 01/11/2026 às 04:48 BRT não havia PRs abertos nem conflito pendente identificado. O resultado está limitado ao commit e ao momento registrados.

## Inventário de PRs e merges

O CI indicado é o do **head final revisado de cada PR**, após correções ou resoluções quando aplicável. Não representa o resultado de todos os heads históricos. Os links dos commits permitem conferir os SHAs completos; cada execução está ligada ao head final revisado do respectivo PR.

| PR | Alteração | Integração | CI do head final |
| --- | --- | --- | --- |
| [#60](https://github.com/BipoXDXD/ToggleForge/pull/60) | bunit 2.11.3 → 2.13.3 | Merge [`6177b42`](https://github.com/BipoXDXD/ToggleForge/commit/6177b428f7708a4a0b14423a6dc6a664d88302d6) | [41261474952](https://github.com/BipoXDXD/ToggleForge/actions/runs/41261474952) — sucesso |
| [#61](https://github.com/BipoXDXD/ToggleForge/pull/61) | JsonSchema.Net 9.4.0 → 9.6.0 | Merge [`b68e05a`](https://github.com/BipoXDXD/ToggleForge/commit/b68e05addaab0cd244644b2d93870cd52a822381) | [41238884473](https://github.com/BipoXDXD/ToggleForge/actions/runs/41238884473) — sucesso |
| [#62](https://github.com/BipoXDXD/ToggleForge/pull/62) | Components.WebAssembly 10.0.12 → 10.0.14 | Merge [`22f381f`](https://github.com/BipoXDXD/ToggleForge/commit/22f381fd11322a23761efff9382c6d1644ef5477) | [41238897507](https://github.com/BipoXDXD/ToggleForge/actions/runs/41238897507) — sucesso |
| [#63](https://github.com/BipoXDXD/ToggleForge/pull/63) | DataProtection.EntityFrameworkCore 10.0.12 → 10.0.14; Xml/Pkcs 10.0.14 | Merge [`1be62e6`](https://github.com/BipoXDXD/ToggleForge/commit/1be62e6301c841d9e83aedf79aeae845d251cc59) | [41260721509](https://github.com/BipoXDXD/ToggleForge/actions/runs/41260721509) — sucesso |
| [#64](https://github.com/BipoXDXD/ToggleForge/pull/64) | Identity.EntityFrameworkCore 10.0.12 → 10.0.14 | Merge [`c002fcf`](https://github.com/BipoXDXD/ToggleForge/commit/c002fcf66a9dd5b30842a850277ed4e743920a6b) | [41261516966](https://github.com/BipoXDXD/ToggleForge/actions/runs/41261516966) — sucesso |
| [#65](https://github.com/BipoXDXD/ToggleForge/pull/65) | Grupo dataprotection-crypto; referências explícitas de Xml/Pkcs em Api.Tests | Merge [`1870011`](https://github.com/BipoXDXD/ToggleForge/commit/1870011058820843e67cf1830769f932515bc1e4) | [41264400894](https://github.com/BipoXDXD/ToggleForge/actions/runs/41264400894) — sucesso |
| [#66](https://github.com/BipoXDXD/ToggleForge/pull/66) | Mvc.Testing 10.0.12 → 10.0.14 | Merge [`162e92b`](https://github.com/BipoXDXD/ToggleForge/commit/162e92b126cff9833d8799a250f01e86e5422244) | [41265285397](https://github.com/BipoXDXD/ToggleForge/actions/runs/41265285397) — sucesso |
| [#67](https://github.com/BipoXDXD/ToggleForge/pull/67) | Microsoft.Bcl.TimeProvider 10.0.13 → 10.0.14 | Merge [`01eff93`](https://github.com/BipoXDXD/ToggleForge/commit/01eff93211a27dfff3b475d12d34ab3330aeb6d6) | [41265299641](https://github.com/BipoXDXD/ToggleForge/actions/runs/41265299641) — sucesso |
| [#68](https://github.com/BipoXDXD/ToggleForge/pull/68) | Microsoft.EntityFrameworkCore.Design 10.0.12 → 10.0.14 | Merge [`4a4cf0a`](https://github.com/BipoXDXD/ToggleForge/commit/4a4cf0a0d82599fe17b8e053194bbe6aea14af4f) | [41271794724](https://github.com/BipoXDXD/ToggleForge/actions/runs/41271794724) — sucesso |
| [#69](https://github.com/BipoXDXD/ToggleForge/pull/69) | Microsoft.Extensions.Http 10.0.12 → 10.0.14 | Fechado sem merge; bump incluído no #70 | [41265339077](https://github.com/BipoXDXD/ToggleForge/actions/runs/41265339077) — sucesso |
| [#70](https://github.com/BipoXDXD/ToggleForge/pull/70) | Http.Resilience 10.10.0 → 10.11.0; Http 10.0.14 | Merge [`84a36e8`](https://github.com/BipoXDXD/ToggleForge/commit/84a36e8e3fc2927afef75c6de540e0359adbd025) | [41266818438](https://github.com/BipoXDXD/ToggleForge/actions/runs/41266818438) — sucesso |
| [#71](https://github.com/BipoXDXD/ToggleForge/pull/71) | Grupo http-resilience, preservando dataprotection-crypto | Merge [`77887f6`](https://github.com/BipoXDXD/ToggleForge/commit/77887f64adf80cfa232b4a47ca34ad45f7e70966) | [41272860758](https://github.com/BipoXDXD/ToggleForge/actions/runs/41272860758) — sucesso |
| [#72](https://github.com/BipoXDXD/ToggleForge/pull/72) | TimeProvider.Testing 10.10.0 → 10.11.0 | Merge [`17c1d3e`](https://github.com/BipoXDXD/ToggleForge/commit/17c1d3e00064fc498b55b1c6204d78f83cee1e64) | [41275743345](https://github.com/BipoXDXD/ToggleForge/actions/runs/41275743345) — sucesso |
| [#73](https://github.com/BipoXDXD/ToggleForge/pull/73) | Microsoft.Playwright 1.63.0 → 1.64.1 | Merge [`a50452b`](https://github.com/BipoXDXD/ToggleForge/commit/a50452bb89389e608ef886e06a97b40a8b5992c7) | [41277163888](https://github.com/BipoXDXD/ToggleForge/actions/runs/41277163888) — sucesso |
| [#74](https://github.com/BipoXDXD/ToggleForge/pull/74) | TngTech.ArchUnitNET 0.13.4 → 0.13.5 | Merge [`a9deb08`](https://github.com/BipoXDXD/ToggleForge/commit/a9deb08fb1d8f4e71c037afb8186aa85e90aed57) | [41278943848](https://github.com/BipoXDXD/ToggleForge/actions/runs/41278943848) — sucesso |
| [#75](https://github.com/BipoXDXD/ToggleForge/pull/75) | Yaml2JsonNode 3.0.1 → 3.0.2 | Merge [`15b3d6c`](https://github.com/BipoXDXD/ToggleForge/commit/15b3d6c58395d027ba86b90adb808689334ebd9e) | [41279795333](https://github.com/BipoXDXD/ToggleForge/actions/runs/41279795333) — sucesso |

Ordem efetiva dos merges: **#63 → #60 → #61 → #62 → #64 → #65 → #70 → #71 → #66 → #67 → #68 → #72 → #73 → #74 → #75**. O #69 não aparece nessa sequência porque foi fechado sem merge. Sua versão de Http está comprovadamente no diff integrado do #70.

## Correções de NU1109

### #63 — DataProtection, Xml e Pkcs

O [CI original 41238920746](https://github.com/BipoXDXD/ToggleForge/actions/runs/41238920746), no head `ee60fa86ea9c0a83ecdc3d79dcf2623a52b45d9d`, falhou em **Restore**. As etapas Build e Testes foram marcadas como ignoradas. Essa falha de resolução não comprova regressão funcional.

A dependência transitiva era:

```text
DataProtection.EntityFrameworkCore 10.0.14
  → DataProtection 10.0.14
    → System.Security.Cryptography.Xml >= 10.0.14
      → System.Security.Cryptography.Pkcs >= 10.0.14
```

O arquivo central fixava Xml e Pkcs em 10.0.13. A reprodução local mostrou NU1109 em Xml; elevar somente Xml revelou NU1109 em Pkcs. A correção elevou **ambos para 10.0.14**, no commit [`6ee2670`](https://github.com/BipoXDXD/ToggleForge/commit/6ee26703f3b2bbd08e6828b22437af83fb0803a2). Restore, build e testes passaram; o [novo CI 41260721509](https://github.com/BipoXDXD/ToggleForge/actions/runs/41260721509) terminou com sucesso antes do merge.

Os requisitos foram conferidos nos nuspecs, no grafo restaurado e em `dotnet nuget why`. Não foi demonstrada necessidade de outros pins criptográficos para o alvo net10.0; nenhuma atualização adicional dessa família foi introduzida na correção. A justificativa que impede o retorno às versões 10.0.12 vulneráveis foi mantida.

### #70 — Http.Resilience e Http

O [CI original 41265357338](https://github.com/BipoXDXD/ToggleForge/actions/runs/41265357338), no head `75d9f804121f19fb2c2a3bedbcc1433ef7fd5d28`, também falhou em **Restore**, com Build e Testes ignorados. O erro foi reproduzido localmente.

```text
Microsoft.Extensions.Http.Resilience 10.11.0
  → Microsoft.Extensions.Http.Diagnostics 10.11.0
    → Microsoft.Extensions.Http >= 10.0.13
```

Http ainda estava fixado em 10.0.12. A correção mínima alterou uma linha para **Http 10.0.14**, no commit [`91fa5cb`](https://github.com/BipoXDXD/ToggleForge/commit/91fa5cbc34cccdf2171865dcbdf4470476f1bd24). Essa versão satisfaz o mínimo 10.0.13 e coincide com o bump já proposto no #69.

O conteúdo corrigido corresponde à união das atualizações de #69 e #70. O [CI 41266818438](https://github.com/BipoXDXD/ToggleForge/actions/runs/41266818438) passou no head corrigido; o #70 foi mesclado e o #69 foi posteriormente fechado sem merge. O grupo criptográfico e seus pins permaneceram inalterados.

## Prevenção e grupos preservados

O #65 criou `dataprotection-crypto` e adicionou referências explícitas de Xml/Pkcs **somente em Api.Tests**, permitindo ao Dependabot descobri-los como candidatos normais de atualização. O #71 adicionou `http-resilience` com os dois nomes exatos, preservando o grupo anterior. Ambos se aplicam a **version-updates**, no ecossistema NuGet e diretório raiz.

O reconhecimento no serviço hospedado foi comprovado após cada mudança: [41264993790](https://github.com/BipoXDXD/ToggleForge/actions/runs/41264993790) após #65 e [41273304434](https://github.com/BipoXDXD/ToggleForge/actions/runs/41273304434) após #71. Foi reconfirmado na análise final [41281782859](https://github.com/BipoXDXD/ToggleForge/actions/runs/41281782859):

| Grupo | Pacote reconhecido | Versão final | Resultado do bot |
| --- | --- | --- | --- |
| `dataprotection-crypto` | Microsoft.AspNetCore.DataProtection.EntityFrameworkCore | 10.0.14 | Sem atualização elegível |
| `dataprotection-crypto` | System.Security.Cryptography.Xml | 10.0.14 | Sem atualização elegível |
| `dataprotection-crypto` | System.Security.Cryptography.Pkcs | 10.0.14 | Sem atualização elegível |
| `http-resilience` | Microsoft.Extensions.Http | 10.0.14 | Sem atualização elegível |
| `http-resilience` | Microsoft.Extensions.Http.Resilience | 10.11.0 | Sem atualização elegível |

As cinco referências foram reconhecidas e analisadas dentro dos grupos corretos. Não havia atualização elegível para esses grupos; portanto, **a geração de um PR com novas versões agrupadas não foi exercitada**. A descoberta e o agrupamento configurado estão comprovados; isso não equivale a garantir toda atualização futura.

A [configuração final](https://github.com/BipoXDXD/ToggleForge/blob/15b3d6c58395d027ba86b90adb808689334ebd9e/.github/dependabot.yml) tem SHA-256 `cf45ee23d8ca8c860faad7c213f7e77ebdb9250d7b9f23e70b7c89072f5fb357` e permaneceu byte a byte idêntica nas integrações posteriores ao #71. A agenda semanal e os diretórios de npm e GitHub Actions foram preservados. O alcance dos grupos não foi ampliado para atualizações de segurança.

Continuam ativos `ManagePackageVersionsCentrally=true`, o pinning transitivo restrito a Api.Tests e o tratamento de avisos como erros. Não foram introduzidas supressões de NU1109 ou de avisos de vulnerabilidade. Os pins criptográficos não foram propagados ao pacote distribuído do SDK.

## Conflitos e validação conjunta

| Integração | Tratamento e evidência |
| --- | --- |
| #60–64 | Conflitos em linhas vizinhas do arquivo central. Os branches #60 e #64 receberam a correção do #63 nos commits `e7fcc75` e `5588fbe`, preservando seus bumps e Xml/Pkcs 10.0.14. Os novos heads passaram no CI. O conteúdo final da main foi comparado à combinação validada. |
| #66–68 | A combinação sobre a main que já continha #70/#71 aplicou sem conflitos. Restore, build, testes, demos, auditoria e empacotamento do SDK passaram antes dos merges; a árvore final coincidiu com a validada. |
| #72–73 | Conflito textual entre TimeProvider.Testing e Playwright. A resolução foi publicada no #73, commit `54add1f`, com ambos os bumps. O CI conjunto [41277163888](https://github.com/BipoXDXD/ToggleForge/actions/runs/41277163888) passou antes dos merges. |
| #74–75 | Conflito textual entre ArchUnitNET e Yaml2JsonNode. A resolução foi publicada no #75, commit `6a3e017`, com ambos os bumps. O CI conjunto [41279795333](https://github.com/BipoXDXD/ToggleForge/actions/runs/41279795333) passou antes dos merges. |

Os pushes de resolução preservaram o histórico, e os merges usaram o head esperado como condição. Não se presumiu compatibilidade conjunta a partir das aprovações individuais. Nas combinações finais, versões e conteúdo foram conferidos, seguidos pelo CI da main.

## CIs da main efetivamente comprovados

Os metadados foram reconferidos no GitHub. Os logs preservados das sete execuções confirmam, em cada estado, build Release com **0 avisos e 0 erros**, **1.711 testes aprovados**, **0 falhas e 0 ignorados**. A mesma suíte foi executada novamente em estados sucessivos.

| Estado integrado | Commit da main | Execução por push | Resultado comprovado |
| --- | --- | --- | --- |
| Após #60–64 | [`c002fcf`](https://github.com/BipoXDXD/ToggleForge/commit/c002fcf66a9dd5b30842a850277ed4e743920a6b) | [41261520357](https://github.com/BipoXDXD/ToggleForge/actions/runs/41261520357) | 3 jobs; 1.711 aprovados; 0 falhas; 0 ignorados |
| Após #65 | [`1870011`](https://github.com/BipoXDXD/ToggleForge/commit/1870011058820843e67cf1830769f932515bc1e4) | [41264988662](https://github.com/BipoXDXD/ToggleForge/actions/runs/41264988662) | 3 jobs; 1.711 aprovados; 0 falhas; 0 ignorados |
| Após #70 | [`84a36e8`](https://github.com/BipoXDXD/ToggleForge/commit/84a36e8e3fc2927afef75c6de540e0359adbd025) | [41271453054](https://github.com/BipoXDXD/ToggleForge/actions/runs/41271453054) | 3 jobs; 1.711 aprovados; 0 falhas; 0 ignorados |
| Após #71 | [`77887f6`](https://github.com/BipoXDXD/ToggleForge/commit/77887f64adf80cfa232b4a47ca34ad45f7e70966) | [41273302143](https://github.com/BipoXDXD/ToggleForge/actions/runs/41273302143) | 3 jobs; 1.711 aprovados; 0 falhas; 0 ignorados |
| Após #66–68 | [`4a4cf0a`](https://github.com/BipoXDXD/ToggleForge/commit/4a4cf0a0d82599fe17b8e053194bbe6aea14af4f) | [41275407465](https://github.com/BipoXDXD/ToggleForge/actions/runs/41275407465) | 3 jobs; 1.711 aprovados; 0 falhas; 0 ignorados |
| Após #72–73 | [`a50452b`](https://github.com/BipoXDXD/ToggleForge/commit/a50452bb89389e608ef886e06a97b40a8b5992c7) | [41277705902](https://github.com/BipoXDXD/ToggleForge/actions/runs/41277705902) | 3 jobs; 1.711 aprovados; 0 falhas; 0 ignorados |
| Após #74–75 | [`15b3d6c`](https://github.com/BipoXDXD/ToggleForge/commit/15b3d6c58395d027ba86b90adb808689334ebd9e) | [41280350602](https://github.com/BipoXDXD/ToggleForge/actions/runs/41280350602) | 3 jobs; 1.711 aprovados; 0 falhas; 0 ignorados |

Em todas essas execuções passaram restore, formatação, build, instalação do Chromium, testes, vetores de rollout, demos de rollout e SDK, lint do contrato da API e scan de segredos. CIs intermediários cancelados pela regra de concorrência não foram contados como aprovações; a comprovação usada é a do estado final de cada integração.

## Validações locais e alcance

Os registros locais das correções e das combinações #60–64, #66–68, #72–73 e #74–75 comprovam restore, build Release, suíte completa de 1.711 testes, formatação, vetores e demos. As auditorias NuGet registradas examinaram 14 projetos, incluindo transitivos, sem vulnerabilidades reportadas pela fonte consultada naquele momento. O #70 também teve restore antes/depois, build, formatação, suíte completa e auditoria documentados.

Os testes de integração usaram PostgreSQL real via Testcontainers e Chromium via Playwright. As regras de arquitetura e o carregamento/validação do contrato YAML permaneceram habilitados e passaram com os pacotes atualizados. Nenhuma asserção, timeout ou regra de arquitetura foi enfraquecida.

O #67 teve build/testes .NET e empacotamento do SDK netstandard2.1/C# 9 verificados; **Unity/IL2CPP não foi executado**. A validação de navegador cobre Chromium, não Firefox/WebKit. Não foi criada uma nova migration de teste para o #68; as migrations existentes foram exercitadas pelos testes. Auditoria do feed e scan de segredos não constituem uma auditoria completa de segurança da aplicação.

Algumas execuções locais de formatação emitiram aviso genérico de carregamento do workspace, mantendo saída 0; isso é distinto dos builds com zero avisos. Os logs foram preservados para não transformar êxito de comando em alegação de ausência absoluta de avisos.

## Última execução do Dependabot e pendências

Após os merges de #74/#75, foram solicitadas verificações sobre `15b3d6c58395d027ba86b90adb808689334ebd9e`:

| Ecossistema | Execução | Resultado dos logs |
| --- | --- | --- |
| NuGet, / | [41281782859](https://github.com/BipoXDXD/ToggleForge/actions/runs/41281782859) | Sucesso; grupos reconhecidos; ArchUnitNET 0.13.5 e Yaml2JsonNode 3.0.2 sem atualização elegível; nenhum PR novo |
| npm, /src/ToggleForge.Console | [41281805373](https://github.com/BipoXDXD/ToggleForge/actions/runs/41281805373) | Sucesso; tailwindcss e @tailwindcss/cli 4.3.3 sem atualização necessária |
| GitHub Actions, / | [41281798453](https://github.com/BipoXDXD/ToggleForge/actions/runs/41281798453) | Sucesso; nenhuma atualização necessária nas quatro actions examinadas |

Os logs finais não contêm NU1109, registro de erro do job ou solicitação de criação de PR. O NuGet emitiu 84 avisos de conexão do proxy, mas completou a análise; não foram classificados como conflitos de dependências. O histórico contém verificações anteriores limitadas pelo número de PRs abertos: seus status verdes não foram usados como prova de ausência de pendências. A conclusão registrada considera os logs e a consulta de PRs abertos, além do status da execução.

Na consulta registrada, a main e a configuração continuavam iguais às analisadas nessas execuções e não havia PR aberto no repositório. A conclusão se limita ao estado auditado e aos ecossistemas, diretórios e atualizações elegíveis configurados.

## Conteúdo final e rastreabilidade

Comparado à base `cd084969b33487b266cb5de08e9b260bc4b4e40c`, o resultado altera **somente três arquivos**: `Directory.Packages.props`, `.github/dependabot.yml` e `tests/ToggleForge.Api.Tests/ToggleForge.Api.Tests.csproj`. Não houve alteração no código da aplicação, nas asserções dos testes ou no workflow de CI. As 16 mudanças de versão centralizadas são:

| Pacote | Base inicial | Main final |
| --- | --- | --- |
| JsonSchema.Net | 9.4.0 | 9.6.0 |
| Microsoft.AspNetCore.Components.WebAssembly | 10.0.12 | 10.0.14 |
| Microsoft.AspNetCore.DataProtection.EntityFrameworkCore | 10.0.12 | 10.0.14 |
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 10.0.12 | 10.0.14 |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.12 | 10.0.14 |
| Microsoft.Bcl.TimeProvider | 10.0.13 | 10.0.14 |
| Microsoft.EntityFrameworkCore.Design | 10.0.12 | 10.0.14 |
| Microsoft.Extensions.Http | 10.0.12 | 10.0.14 |
| Microsoft.Extensions.Http.Resilience | 10.10.0 | 10.11.0 |
| Microsoft.Extensions.TimeProvider.Testing | 10.10.0 | 10.11.0 |
| Microsoft.Playwright | 1.63.0 | 1.64.1 |
| System.Security.Cryptography.Pkcs | 10.0.13 | 10.0.14 |
| System.Security.Cryptography.Xml | 10.0.13 | 10.0.14 |
| TngTech.ArchUnitNET | 0.13.4 | 0.13.5 |
| Yaml2JsonNode | 3.0.1 | 3.0.2 |
| bunit | 2.11.3 | 2.13.3 |

## Fontes no estado auditado

Os links de PRs, commits e execuções ao longo do relatório permitem conferir os resultados no GitHub. Os arquivos de configuração abaixo usam o commit auditado como referência permanente:

- [Gerenciamento centralizado de pacotes](https://github.com/BipoXDXD/ToggleForge/blob/15b3d6c58395d027ba86b90adb808689334ebd9e/Directory.Packages.props).
- [Grupos do Dependabot](https://github.com/BipoXDXD/ToggleForge/blob/15b3d6c58395d027ba86b90adb808689334ebd9e/.github/dependabot.yml).
- [Referências explícitas e pinning do projeto de integração](https://github.com/BipoXDXD/ToggleForge/blob/15b3d6c58395d027ba86b90adb808689334ebd9e/tests/ToggleForge.Api.Tests/ToggleForge.Api.Tests.csproj).
- [Workflow de CI](https://github.com/BipoXDXD/ToggleForge/blob/15b3d6c58395d027ba86b90adb808689334ebd9e/.github/workflows/ci.yml).
- [Diff completo do intervalo auditado](https://github.com/BipoXDXD/ToggleForge/compare/cd084969b33487b266cb5de08e9b260bc4b4e40c...15b3d6c58395d027ba86b90adb808689334ebd9e).

As mudanças descritas neste relatório são as dos PRs #60 a #75. A inclusão deste documento é uma alteração posterior e não acrescenta resultados de teste às execuções históricas.
