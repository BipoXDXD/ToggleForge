# 0003 — Console em Blazor com Tailwind CSS

- **Status:** Aceita; o render mode foi decidido na ADR 0009, e a integração do Tailwind fica para o Marco 3
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

Pendências para o Marco 3, que serão decididas lá:

- Render mode (Interactive Server, WebAssembly ou Auto) e como ele afeta autenticação e latência.
- Integração do Tailwind: CLI standalone (sem Node) ou via npm, e como o CSS é gerado no build e no CI.
- O console consome a API por HTTP, como o `ToggleForge.md` já previa; não acessa o banco direto.

## Compliance

Esta ADR é revisada no início do Marco 3; as pendências acima viram uma ADR nova ou uma atualização
desta.
