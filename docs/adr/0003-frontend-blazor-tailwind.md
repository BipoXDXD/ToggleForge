# 0003 — Console em Blazor com Tailwind CSS

- **Status:** Aceita; o render mode está na ADR 0009, e a integração do Tailwind, abaixo
- **Data:** 2026-10-03

## Contexto

O `ToggleForge.md` (seções 4 e 14) deixava a base do console em aberto: Blazor Web App com CSS próprio
ou React com TypeScript.

| Opção | Prós | Contras |
|---|---|---|
| **Blazor + Tailwind** | Tudo em C#: DTOs compartilhados, um build, um CI; Tailwind dá um sistema visual consistente sem escrever CSS do zero | Ecossistema de componentes menor que o do React; o Tailwind precisa de um passo de build de CSS |
| React + TypeScript | Maior ecossistema; stack de frontend mais comum no mercado | Duas toolchains (npm e dotnet), tipos gerados do OpenAPI, CI maior |

## Decisão

O console será um **Blazor Web App** estilizado com **Tailwind CSS**.

## Consequências

- O render mode é WebAssembly, servido pela API na mesma origem (ADR 0009).
- O console consome a API por HTTP, como o `ToggleForge.md` já previa; não acessa o banco direto.
- **Tailwind pelo npm, no build** (escolha do usuário, no início do Marco 3). Alternativas:
  - **npm, rodando no build:** versão travada no `package-lock.json` e atualizada pelo Dependabot, como o
    `tools/api-lint`; o CSS é gerado a cada build e nunca vai para o repositório. Contra: compilar o
    console exige Node.
  - **npm, CSS commitado e conferido no CI:** o `dotnet build` dispensa Node. Contra: um arquivo derivado
    no repositório, e o risco de esquecer de regenerar.
  - **CLI standalone:** sem Node. Contra: o binário de cada plataforma é baixado e conferido por script
    nosso, e o Dependabot não o atualiza.

  O `tailwindcss` e o `@tailwindcss/cli` ficam no `package.json` de `src/ToggleForge.Console`. Um target do
  MSBuild roda `npm ci` quando o `node_modules` falta e gera `wwwroot/css/app.css` antes de o build
  recolher os arquivos estáticos. Esse arquivo fica no `.gitignore`.

## Compliance

- O build de Release no CI gera o CSS do zero, com Node instalado no job.
- O Dependabot acompanha o `package.json` do console.
