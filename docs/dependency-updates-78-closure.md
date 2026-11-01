# Encerramento do PR #78 e verificação do Dependabot

Registro histórico de 01/11/2026, complementar ao [encerramento do #77](dependency-updates-77-closure.md)
e ao [relatório dos PRs #60 a #75](dependency-updates-60-75.md). O recorte é a main no commit
`3d00dabed13f27c8d378921350c079ecaedd121c`, após o merge do #78, e as análises do Dependabot sobre esse
conteúdo. As afirmações de estado se referem àquela verificação, anterior à abertura do PR que publica
este registro.

## Merge do #78 e CI da main

O [PR #78](https://github.com/BipoXDXD/ToggleForge/pull/78) adicionou somente
`docs/dependency-updates-77-closure.md`, com 90 linhas. Foi mesclado após a aprovação do CI do head
`17b613a6f322004455a5cc175927079fcb5ea0c5`, conferido como condição do merge.

O commit de merge é [`3d00dabed13f27c8d378921350c079ecaedd121c`](https://github.com/BipoXDXD/ToggleForge/commit/3d00dabed13f27c8d378921350c079ecaedd121c).
A árvore da main foi comparada ao head aprovado e é idêntica. Contra a base
`d25a262483aa1a786f322d339d761512289fdf25`, a diferença contém apenas aquele documento.

| Estado validado | Execução | Resultado comprovado |
| --- | --- | --- |
| Head do PR #78, `17b613a` | [41295500223](https://github.com/BipoXDXD/ToggleForge/actions/runs/41295500223) | Sucesso nos três jobs; 1.711 testes aprovados, 0 falhas, 0 ignorados |
| Main após o merge, `3d00dab` | [41296090163](https://github.com/BipoXDXD/ToggleForge/actions/runs/41296090163) | Sucesso nos três jobs; 1.711 testes aprovados, 0 falhas, 0 ignorados |

Os logs confirmaram build Release com 0 avisos e 0 erros. Restore, formatação, instalação do Chromium,
testes, vetores de rollout e demos de rollout/SDK passaram. Lint do contrato da API e scan de segredos
também passaram. Os resultados podem ser conferidos nos logs de
[build/testes do PR](https://github.com/BipoXDXD/ToggleForge/actions/runs/41295500223/job/124815397403)
e de [build/testes da main](https://github.com/BipoXDXD/ToggleForge/actions/runs/41296090163/job/124817132735).
São resultados dessas execuções históricas, anteriores ao PR deste documento.

## Análises do Dependabot após o merge

Como não havia análise posterior ao #78 no histórico consultado, foram solicitadas verificações pelo
botão Check for updates do painel oficial, nos três ecossistemas configurados. Todas usaram o commit
`3d00dabed13f27c8d378921350c079ecaedd121c`.

| Ecossistema e diretório | Execução | Resultado dos logs |
| --- | --- | --- |
| NuGet, / | [41296728018](https://github.com/BipoXDXD/ToggleForge/actions/runs/41296728018) | Sucesso; nenhuma atualização elegível ou PR novo |
| npm, /src/ToggleForge.Console | [41296746559](https://github.com/BipoXDXD/ToggleForge/actions/runs/41296746559) | Sucesso; tailwindcss e @tailwindcss/cli 4.3.3 sem atualização necessária |
| GitHub Actions, / | [41296737842](https://github.com/BipoXDXD/ToggleForge/actions/runs/41296737842) | Sucesso; nenhuma atualização necessária nas quatro actions examinadas |

O [job NuGet e seu log](https://github.com/BipoXDXD/ToggleForge/actions/runs/41296728018/job/124819037954)
confirmaram o recebimento dos dois grupos e a análise dos cinco pacotes dentro dos grupos corretos.
Todos foram reconhecidos como referências explícitas, sem atualização elegível:

| Grupo | Pacote reconhecido | Versão |
| --- | --- | --- |
| `dataprotection-crypto` | Microsoft.AspNetCore.DataProtection.EntityFrameworkCore | 10.0.14 |
| `dataprotection-crypto` | System.Security.Cryptography.Xml | 10.0.14 |
| `dataprotection-crypto` | System.Security.Cryptography.Pkcs | 10.0.14 |
| `http-resilience` | Microsoft.Extensions.Http | 10.0.14 |
| `http-resilience` | Microsoft.Extensions.Http.Resilience | 10.11.0 |

A análise efetiva de `dataprotection-crypto` começa na linha 6895 do log baixado; a de
`http-resilience`, na linha 12871. ArchUnitNET 0.13.5 e Yaml2JsonNode 3.0.2 também foram reconhecidos
sem nova atualização elegível. Os logs das três análises não contêm NU1109, registro de erro do job
ou solicitação de criação de PR. O NuGet emitiu 86 avisos de conexão do proxy, mas concluiu a análise;
esses avisos não foram tratados como conflitos de dependências.

## Dependências e grupos preservados

O #78 e a verificação posterior preservaram versões, lockfiles, projetos, pins criptográficos e
workflow. Continuam ativos o gerenciamento centralizado e o pinning transitivo restrito a Api.Tests.

A [configuração do Dependabot no estado auditado](https://github.com/BipoXDXD/ToggleForge/blob/3d00dabed13f27c8d378921350c079ecaedd121c/.github/dependabot.yml)
permaneceu byte a byte idêntica à anterior ao #78, com SHA-256
`cf45ee23d8ca8c860faad7c213f7e77ebdb9250d7b9f23e70b7c89072f5fb357`.
Os grupos mantêm padrões restritos aos nomes de pacote da tabela, aplicação a `version-updates`,
diretório NuGet raiz e agenda semanal. O escopo dos grupos e dos demais ecossistemas não foi ampliado.

As fontes permanentes também incluem o
[arquivo central de pacotes](https://github.com/BipoXDXD/ToggleForge/blob/3d00dabed13f27c8d378921350c079ecaedd121c/Directory.Packages.props),
o [projeto de integração](https://github.com/BipoXDXD/ToggleForge/blob/3d00dabed13f27c8d378921350c079ecaedd121c/tests/ToggleForge.Api.Tests/ToggleForge.Api.Tests.csproj)
e o [workflow de CI](https://github.com/BipoXDXD/ToggleForge/blob/3d00dabed13f27c8d378921350c079ecaedd121c/.github/workflows/ci.yml).

## Estado de encerramento

Na consulta realizada após essas análises, a main remota ainda correspondia a `3d00dab`, com CI
aprovado, e não havia PRs abertos. Não restavam atualizações elegíveis ou conflitos identificados no
escopo verificado. O checkout local estava atualizado e limpo; nenhuma alteração ou novo merge foi
efetuado durante a verificação do bot.

A conclusão considera os logs e a consulta dos PRs, além do status das execuções. Erros antigos por
limite de PRs não foram usados como resultado da nova análise. Como não havia versões elegíveis para
os grupos, essa execução não exercitou a criação de um PR com novas versões agrupadas.

O encerramento se limita ao commit, às execuções e aos ecossistemas/diretórios registrados acima.
Publicações de versões e PRs posteriores, inclusive o que acrescenta este documento, ficam fora desse
recorte histórico.
