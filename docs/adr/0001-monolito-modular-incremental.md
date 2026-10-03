# 0001 — Monólito modular, com projetos criados sob demanda

- **Status:** Aceita
- **Data:** 2026-10-03

## Contexto

O `ToggleForge.md` (seção 5) propõe um monólito modular com as áreas Access, Authoring, Delivery,
Evaluation e Audit, e sugere projetos `Api`, `Console`, `Domain`, `Application`, `Infrastructure`,
`Evaluation` e `Sdk`. O primeiro marco só precisa do algoritmo de avaliação, que não tem banco nem HTTP.

Alternativas consideradas:

1. **Criar todos os projetos agora.** Prós: a estrutura final fica visível desde o início. Contras:
   projetos vazios e referências sem uso; cada camada sem código cobra build, CI e manutenção sem pagar
   nada (Speculative Generality).
2. **Um projeto só, separado por namespaces.** Prós: é o mais simples. Contras: o núcleo de avaliação
   precisa ser consumido pela Unity (netstandard2.1, sem dependências), e só um projeto separado deixa
   o compilador garantir isso.
3. **Criar cada projeto quando o primeiro código dele existir.** Prós: o compilador vigia as fronteiras
   que já existem e nada fica vazio. Contras: exige disciplina para extrair o projeto no momento certo.

## Decisão

Adotamos a opção 3. O Marco 1 cria só `ToggleForge.Evaluation` (netstandard2.1, C# 9, nenhum
`PackageReference`), o projeto de testes e uma demo. `Domain`, `Application`, `Infrastructure` e `Api`
entram no Marco 2, junto com a primeira regra de persistência. O layout segue `src/`, `tests/` e
`samples/`, e as versões de pacotes ficam centralizadas em `Directory.Packages.props`.

O `Evaluation` é o núcleo compartilhado entre o simulador do console e o SDK. Por isso é uma biblioteca
pura: não referencia nenhum outro projeto da solução nem pacote de terceiro.

## Consequências

- O núcleo compila para netstandard2.1, então APIs mais novas da BCL (`HashData`, `Span`-first, etc.)
  não estão disponíveis. Records e `init` exigem o polyfill `IsExternalInit`.
- Até o Marco 2 não há teste de arquitetura (fitness function): a ausência de referências no csproj já
  é garantida pelo compilador. O teste entra quando existirem duas camadas que possam violar a regra.

## Compliance

- `src/ToggleForge.Evaluation/ToggleForge.Evaluation.csproj` não contém `PackageReference` nem
  `ProjectReference`; o review de PR confere.
- Projeto novo só entra com o primeiro código que precisa dele.
