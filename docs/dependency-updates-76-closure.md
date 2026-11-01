# Encerramento do PR #76 e verificação do Dependabot

Registro histórico de 01/11/2026, complementar ao [relatório dos PRs #60 a #75](dependency-updates-60-75.md).
O recorte deste documento é a main no commit `1ec3c415f601d6cee2243cc72ab11caf499e9498`, após o merge do #76,
e as análises do Dependabot executadas sobre esse conteúdo. As afirmações de estado se referem àquela
verificação, anterior à abertura do PR que publica este registro.

## Merge do #76 e CI da main

O [PR #76](https://github.com/BipoXDXD/ToggleForge/pull/76) publicou somente
`docs/dependency-updates-60-75.md`, com 175 linhas adicionadas. Foi mesclado após a aprovação do CI do head
`0ee8328ad42800f160397e87bff6c2702b081be8`, conferido como condição do merge.

O commit de merge é [`1ec3c415f601d6cee2243cc72ab11caf499e9498`](https://github.com/BipoXDXD/ToggleForge/commit/1ec3c415f601d6cee2243cc72ab11caf499e9498).
A árvore da main foi comparada ao head aprovado e é idêntica. Contra a base
`15b3d6c58395d027ba86b90adb808689334ebd9e`, a diferença contém apenas o documento.

| Estado validado | Execução | Resultado comprovado |
| --- | --- | --- |
| Head do PR #76, `0ee8328` | [41287293264](https://github.com/BipoXDXD/ToggleForge/actions/runs/41287293264) | Sucesso nos três jobs; 1.711 testes aprovados, 0 falhas, 0 ignorados |
| Main após o merge, `1ec3c41` | [41288823399](https://github.com/BipoXDXD/ToggleForge/actions/runs/41288823399) | Sucesso nos três jobs; 1.711 testes aprovados, 0 falhas, 0 ignorados |

Os logs confirmaram build Release com 0 avisos e 0 erros. Restore, formatação, instalação do Chromium,
testes, vetores de rollout e demos de rollout/SDK passaram. Os outros jobs, lint do contrato da API e
scan de segredos, também passaram. Os resultados estão nos logs de
[build/testes do PR](https://github.com/BipoXDXD/ToggleForge/actions/runs/41287293264/job/124791072615)
e de [build/testes da main](https://github.com/BipoXDXD/ToggleForge/actions/runs/41288823399/job/124795596132).
São resultados dessas execuções históricas, anteriores ao PR deste documento.

## Análises do Dependabot após o merge

Como o histórico consultado ainda não continha uma análise posterior ao #76, foram solicitadas
verificações pelo botão Check for updates do painel oficial, nos três ecossistemas configurados.
Todas usaram o commit `1ec3c415f601d6cee2243cc72ab11caf499e9498`.

| Ecossistema e diretório | Execução | Resultado dos logs |
| --- | --- | --- |
| NuGet, / | [41289678129](https://github.com/BipoXDXD/ToggleForge/actions/runs/41289678129) | Sucesso; nenhuma atualização elegível ou PR novo |
| npm, /src/ToggleForge.Console | [41289694521](https://github.com/BipoXDXD/ToggleForge/actions/runs/41289694521) | Sucesso; tailwindcss e @tailwindcss/cli 4.3.3 sem atualização necessária |
| GitHub Actions, / | [41289684693](https://github.com/BipoXDXD/ToggleForge/actions/runs/41289684693) | Sucesso; nenhuma atualização necessária nas quatro actions examinadas |

O [job NuGet e seu log](https://github.com/BipoXDXD/ToggleForge/actions/runs/41289678129/job/124798118674)
confirmaram o recebimento dos dois grupos e a análise dos cinco pacotes dentro dos grupos corretos.
Todos foram reconhecidos como referências explícitas, sem atualização elegível:

| Grupo | Pacote reconhecido | Versão |
| --- | --- | --- |
| `dataprotection-crypto` | Microsoft.AspNetCore.DataProtection.EntityFrameworkCore | 10.0.14 |
| `dataprotection-crypto` | System.Security.Cryptography.Xml | 10.0.14 |
| `dataprotection-crypto` | System.Security.Cryptography.Pkcs | 10.0.14 |
| `http-resilience` | Microsoft.Extensions.Http | 10.0.14 |
| `http-resilience` | Microsoft.Extensions.Http.Resilience | 10.11.0 |

A análise efetiva de `dataprotection-crypto` começa na linha 6894 do log baixado; a de
`http-resilience`, na linha 12872. ArchUnitNET 0.13.5 e Yaml2JsonNode 3.0.2 também foram reconhecidos
sem nova atualização elegível. Os logs das três análises não contêm NU1109, registro de erro do job
ou solicitação de criação de PR. O NuGet emitiu 84 avisos de conexão do proxy, mas concluiu a análise;
esses avisos não foram tratados como conflitos de dependências.

## Dependências e grupos preservados

O #76 e a verificação posterior preservaram versões, lockfiles, projetos, pins criptográficos e
workflow. Continuam ativos o gerenciamento centralizado e o pinning transitivo restrito a Api.Tests.

A [configuração do Dependabot no estado auditado](https://github.com/BipoXDXD/ToggleForge/blob/1ec3c415f601d6cee2243cc72ab11caf499e9498/.github/dependabot.yml)
permaneceu byte a byte idêntica à anterior ao #76, com SHA-256
`cf45ee23d8ca8c860faad7c213f7e77ebdb9250d7b9f23e70b7c89072f5fb357`.
Os grupos mantêm padrões restritos aos nomes de pacote da tabela, aplicação a `version-updates`,
diretório NuGet raiz e agenda semanal. O escopo dos grupos e dos demais ecossistemas não foi ampliado.

As fontes permanentes também incluem o
[arquivo central de pacotes](https://github.com/BipoXDXD/ToggleForge/blob/1ec3c415f601d6cee2243cc72ab11caf499e9498/Directory.Packages.props),
o [projeto de integração](https://github.com/BipoXDXD/ToggleForge/blob/1ec3c415f601d6cee2243cc72ab11caf499e9498/tests/ToggleForge.Api.Tests/ToggleForge.Api.Tests.csproj)
e o [workflow de CI](https://github.com/BipoXDXD/ToggleForge/blob/1ec3c415f601d6cee2243cc72ab11caf499e9498/.github/workflows/ci.yml).

## Estado de encerramento

Na consulta realizada após essas análises, a main remota ainda correspondia a `1ec3c41`, com CI
aprovado, e não havia PRs abertos. Não restavam atualizações elegíveis ou conflitos identificados no
escopo verificado. O checkout local estava atualizado e limpo; nenhuma alteração ou novo merge foi
efetuado durante a verificação do bot.

A conclusão considera os logs e a consulta dos PRs, além do status das execuções. Erros antigos por
limite de PRs não foram usados como resultado da nova análise. Como não havia versões elegíveis para
os grupos, essa execução não exercitou a criação de um PR com novas versões agrupadas.

O encerramento se limita ao commit, às execuções e aos ecossistemas/diretórios registrados acima.
Publicações de versões e PRs posteriores, inclusive o que acrescenta este documento, ficam fora desse
recorte histórico.
