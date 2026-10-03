# Toggle Forge

Plano inicial de produto e desenvolvimento — outubro de 2026.

Este documento orienta o projeto e pode mudar durante o desenvolvimento. Features, regras, stack, arquitetura e ordem das etapas são propostas. Ao mudar algo, basta registrar o motivo e ajustar os contratos e testes afetados; não precisamos transformar cada escolha numa cerimônia.

## 1. Produto e objetivo

O Toggle Forge permite que estúdios de jogos publiquem feature flags e configurações remotas, liberem mudanças gradualmente e revertam uma publicação. O primeiro público proposto são pequenos estúdios que usam Unity e C#.

A experiência central será: criar uma flag → simular jogadores → publicar para 10% → ampliar a distribuição → reverter se necessário. O jogo já contém a funcionalidade; o Toggle Forge controla sua ativação e parâmetros.

O projeto deve produzir uma API utilizável, um console bem desenvolvido e um SDK demonstrado num jogo simples. O aprendizado acompanha o produto: domínio, persistência, concorrência, segurança, testes e operação em cloud.

## 2. Features por horizonte

| Horizonte | Features propostas |
| --- | --- |
| Primeiro fluxo | Flag booleana, rollout percentual, simulador, publicação, histórico e rollback |
| MVP utilizável | Projetos e ambientes, autenticação, permissões, credenciais do SDK, cache e fallback |
| Próxima evolução | Configurações JSON, segmentação por atributos, comparação de versões e integração Unity |
| Depois do piloto | Eventos agendados, webhooks, aprovação de publicações e telemetria agregada |
| Exploração avançada | Serviço de distribuição independente, provider OpenFeature e experimentos A/B |

A prioridade pode mudar por interesse ou feedback. Cada feature deve ter um comportamento demonstrável; recursos avançados entram quando contribuírem para o produto ou para um objetivo de aprendizado definido.

## 3. Regras iniciais do domínio

1. **Isolamento:** cada projeto pertence a uma organização; ambientes e credenciais ficam limitados ao projeto correto.
2. **Identidade:** a chave de uma flag é única por ambiente. O percentual exige um ID estável do jogador.
3. **Rascunho:** editar uma configuração não altera o que os jogos recebem até a publicação.
4. **Publicação:** cada versão publicada é imutável. Publicar muda atomicamente a referência da versão atual.
5. **Rollback:** recuperar uma versão anterior cria uma nova publicação e preserva o histórico.
6. **Concorrência:** uma edição baseada numa revisão antiga gera conflito; o painel oferece recarregar e comparar.
7. **Idempotência:** repetir uma publicação com a mesma chave e conteúdo retorna a operação original.
8. **Fallback:** configuração ausente, inválida ou expirada retorna o valor padrão informado pelo consumidor.

Para uma flag booleana, propõe-se `onValue` e `offValue`. Uma flag inativa retorna `offValue`; uma ativa retorna `onValue` para jogadores incluídos no rollout e `offValue` para os demais. O `defaultValue` do SDK cobre falhas de avaliação, não substitui uma configuração válida.

O rollout usa um hash determinístico versionado, com codificação canônica dos campos: projeto, ambiente, flag, jogador e semente fixa. A versão da publicação não entra no hash. Assim, passar de 10% para 20% preserva os participantes já incluídos, mantendo a mesma semente e algoritmo. O percentual representa uma distribuição aproximada, não uma quantidade exata de jogadores. A especificação v1 (SHA-256 truncado, percentual em basis points) está em `docs/rollout-algorithm.md` e a decisão em `docs/adr/0002-algoritmo-de-rollout-v1.md`.

## 4. Stack recomendada e alternativas

| Área | Base inicial | Motivo ou alternativa |
| --- | --- | --- |
| Backend | C# e ASP.NET Core em .NET 10 | Controllers ou Minimal APIs conforme preferência |
| Frontend | Blazor Web App, componentes Razor e Tailwind CSS | Decidido em `docs/adr/0003-frontend-blazor-tailwind.md`; render mode fica para o Marco 3 |
| Persistência | EF Core com PostgreSQL | Dapper ou SQL explícito podem entrar em consultas específicas |
| Autenticação | ASP.NET Core Identity para o painel | OIDC com provedor externo pode substituir ou complementar |
| SDK | Núcleo em C# compatível com .NET Standard 2.1 | Adaptador Unity validado na versão e plataformas escolhidas |
| Testes | xUnit, WebApplicationFactory, Testcontainers e Playwright | Domínio, API, banco real e jornadas da interface |
| Desenvolvimento | Rider, Git e Docker Compose | API, console e PostgreSQL reproduzíveis localmente |
| Cloud | Containers, Azure Container Apps e PostgreSQL gerenciado | Provedor e forma de hospedagem são revisáveis |
| Observabilidade | Logs estruturados e OpenTelemetry | Acompanhar publicação, distribuição, falhas e latência |

Começar com dependências necessárias ao primeiro fluxo. Redis, broker e ferramentas adicionais podem entrar depois, por uma necessidade observada ou uma etapa de estudo. A escolha de Blazor também não determina o estilo visual do produto.

## 5. Arquitetura inicial

Começar com um **monólito modular**, aplicando Clean Architecture de maneira proporcional. O console consome a API; o domínio não depende de banco, interface ou serviços de cloud.

| Área lógica | Responsabilidade |
| --- | --- |
| Access | Organizações, membros, projetos, ambientes e credenciais |
| Authoring | Rascunhos, validação, simulação e publicação |
| Delivery | Distribuição de snapshots publicados |
| Evaluation | Avaliação pura compartilhada entre simulador e SDK |
| Audit | Histórico de ações administrativas e publicações |

Uma solução inicial pode conter `ToggleForge.Api`, `ToggleForge.Console`, `ToggleForge.Domain`, `ToggleForge.Application`, `ToggleForge.Infrastructure`, `ToggleForge.Evaluation`, `ToggleForge.Sdk` e projetos de testes. Os módulos podem ser namespaces antes de virarem assemblies separados.

As dependências apontam para o domínio e os casos de uso. A infraestrutura implementa persistência e integrações; a API compõe os serviços por DI. Separar contratos HTTP das entidades internas. Injetar relógio, transporte e armazenamento quando isso facilitar comportamento e testes.

Usar DDD principalmente onde houver regras: ambiente, rascunho e publicação. CQRS pode começar como separação entre comandos e consultas, dentro do mesmo processo. Reavaliar abstrações conforme o código crescer; registrar decisões relevantes em notas curtas no repositório.

## 6. Persistência e consistência

O modelo inicial inclui organização, membro, projeto, ambiente, rascunho, publicação, credencial e auditoria. Uma publicação contém um snapshot completo do ambiente e a versão do contrato de avaliação. JSONB é uma opção para o snapshot; relações e metadados permanecem relacionais.

- Proteger unicidade e pertencimento com constraints e índices, além da autorização na aplicação.
- Gravar snapshot, versão atual, idempotência e auditoria na mesma transação.
- Usar revisão esperada para impedir que uma publicação sobrescreva silenciosamente outra.
- Consultar histórico com paginação; indexar ambiente, versão e data conforme as consultas reais.
- Aplicar migrations reproduzíveis e verificar restauração de backup antes do piloto.

A etapa de estudo de banco pode aprofundar planos de execução, índices compostos, isolamento transacional e contenção. Particionamento e réplicas ficam associados a volume ou a um experimento explícito, não a uma obrigação inicial.

## 7. Contratos da API e comportamento do SDK

| Grupo | Operações principais |
| --- | --- |
| Administração | Projetos, ambientes, membros e credenciais |
| Autoria | Ler/alterar rascunho e simular avaliações |
| Publicações | Publicar, listar versões, comparar e reverter |
| Distribuição | Baixar o snapshot publicado do ambiente da credencial |

Usar prefixo `/api/v1` para administração e `/sdk/v1` para distribuição. Documentar com OpenAPI, retornar erros em Problem Details e tratar revisões com ETag/If-Match. Publicação e rollback aceitam Idempotency-Key; reutilizar a chave com outro conteúdo gera conflito.

O SDK baixa o snapshot e avalia localmente, fora do ciclo de renderização do jogo. Propõe-se sincronização inicial a cada 60 segundos, com variação aleatória, timeout e backoff limitado. O intervalo será ajustável. ETag/304 reduz transferências quando nada mudou.

Manter o último snapshot válido, trocar versões atomicamente e descartar respostas antigas fora de ordem. Definir idade máxima do cache; ao expirar, usar o fallback. Uma primeira instalação sem configuração válida também usa fallback. Um rollback fica disponível na próxima sincronização; não alcança instantaneamente um cliente offline.

Cada avaliação deve informar valor, motivo, versão e origem dos dados. O contrato precisa diferenciar resultado normal, uso de cache e falha. Transporte, cache e adaptador Unity podem variar sem mudar o algoritmo de avaliação.

## 8. Frontend e experiência

O console deve deixar claros projeto, ambiente, rascunho e versão publicada. Ações em produção precisam de identificação inequívoca e um resumo das mudanças antes da publicação.

| Tela | Experiência principal |
| --- | --- |
| Projeto | Ambientes, versão atual, mudanças pendentes e publicações recentes |
| Flags | Busca, filtros e status; editar percentual e valores |
| Simulador | Informar jogador e atributos; explicar o resultado do rascunho ou versão publicada |
| Publicação | Comparar mudanças, confirmar destino e acompanhar o resultado |
| Histórico | Ver autor, data e diferenças; iniciar rollback |
| Integração | Criar/revogar credenciais e consultar exemplos do SDK |

Prever estados de carregamento, vazio, erro e conflito de edição. Uma publicação com resultado desconhecido deve ser consultada antes de repetir a ação. Usar componentes consistentes, navegação por teclado e contraste adequado. Tema, cores, disposição das telas e componentes ficam abertos aos seus gostos.

## 9. Etapas sugeridas

| Marco | Entrega | Evidência de funcionamento |
| --- | --- | --- |
| 1 — Núcleo | Avaliação booleana, contexto, percentual e fallback | Vetores determinísticos e preservação dos grupos passam |
| 2 — API persistente | Rascunho, publicação, rollback, autenticação e isolamento | Fluxo HTTP funciona; banco trata concorrência e repetição |
| 3 — Console | Editor, simulador, publicação e histórico | Jornada completa funciona pela interface |
| 4 — SDK e configurações | Sincronização, cache, JSON e aplicativo demonstrativo em C# | Funciona com perda de rede e snapshots fora de ordem |
| 5 — Unity e segmentação | Adaptador, jogo simples e regras por atributos | Um build real mostra mudanças sem recompilação do jogo |
| 6 — Cloud e piloto | CI, implantação, observabilidade, backups e onboarding | Ambiente reproduzível e integração observada com um estúdio |
| 7 — Distribuição independente | Eventos duráveis, serviço e armazenamento de leitura próprios | Atrasos, falhas e mensagens duplicadas são tratados |

As etapas podem ser reordenadas ou combinadas. Desenvolver console e API juntos é uma alternativa. Os critérios são referências para verificar o comportamento desejado; devem acompanhar mudanças no produto. Não há prazo fixo nem um backlog com cada classe a criar.

## 10. Conceitos avançados na prática

| Conceito | Aplicação no Toggle Forge |
| --- | --- |
| C# | Imutabilidade, generics para resultados tipados, pattern matching e cancelamento assíncrono |
| DI | Ciclos de vida corretos, relógio testável e adaptadores de transporte/persistência |
| Clean Code e refactoring | Nomes do domínio, coesão e simplificação sustentada por testes de comportamento |
| Clean Architecture e DDD | Casos de uso independentes e regras concentradas nos agregados apropriados |
| Design patterns | Strategy para tipos de avaliação, Adapter para transportes e Decorator para observabilidade quando útil |
| Concorrência | Revisões, transações, snapshots imutáveis e idempotência |
| Banco de dados | Constraints, JSONB, índices, planos de execução e isolamento |
| Sistemas distribuídos | Outbox, consumidores idempotentes, retries e consistência eventual |
| Cloud e operação | CI/CD, segredos, métricas, rastreamento e recuperação |

Os padrões são ferramentas disponíveis. Uma implementação direta e legível pode ser a melhor escolha em partes simples do sistema.

## 11. Segurança e testes prioritários

No painel, propor papéis Owner, Editor e Viewer, combinados com autorização por organização, projeto e ambiente. Publicar em produção e administrar credenciais podem exigir permissões específicas. Usar cookies seguros e proteção CSRF quando aplicável ao fluxo escolhido.

Credenciais embutidas no jogo são extraíveis: snapshots distribuídos ao cliente só contêm informações públicas. Compras, recompensas e permissões sensíveis são verificadas no backend do jogo. IDs e atributos fornecidos pelo cliente não provam identidade.

Gerar credenciais com alta entropia, armazenar hashes, permitir rotação/revogação e retirar tokens dos logs. Limitar payloads, flags e consumo. Evitar coletar dados pessoais para segmentação; regras são dados declarativos, não código arbitrário enviado por usuários.

Testes que merecem prioridade:

- Mesmo contexto produz o mesmo resultado; 0% e 100% respeitam os extremos e ampliar o percentual preserva os incluídos.
- Duas edições/publicações concorrentes não perdem alterações silenciosamente.
- Repetir uma operação não duplica publicação; falha transacional não deixa uma versão parcialmente aplicada.
- Um usuário ou credencial não acessa recursos de outra organização ou ambiente.
- SDK lida com rede indisponível, cache expirado, configuração inválida e respostas fora de ordem.
- Rollback cria outra versão e a jornada editar → simular → publicar → reverter funciona no console.

Usar banco real nos testes de persistência. Verificar o SDK num build Unity e nas plataformas selecionadas, incluindo IL2CPP quando aplicável. Testes de carga entram para responder perguntas sobre distribuição, latência e capacidade, com metas definidas antes da medição.

## 12. Um pouco além do MVP

**Segmentação:** começar com poucos operadores tipados e uma precedência explícita. Se houver override, regra por atributos e percentual, definir a ordem e explicar qual regra decidiu o resultado.

**Eventos agendados:** guardar horários em UTC, mostrar o fuso no painel e usar execução persistente/idempotente. Definir o que acontece se uma publicação manual ocorre antes do horário agendado; sinalizar conflito é uma opção inicial.

**Webhooks e aprovações:** emitir eventos de publicação, permitir aprovação em produção e registrar a revisão aprovada. A entrega de webhooks exige autenticação, retries e controles sobre destinos.

**Telemetria e experimentos:** iniciar com métricas operacionais agregadas. Experimentos A/B precisam de exposição, eventos de resultado e critérios de análise; ligar uma flag sozinha não comprova efeito sobre jogadores.

## 13. Microserviços e cloud

O candidato inicial à extração é Delivery. Authoring publica snapshots por uma Outbox transacional; um broker entrega eventos ao serviço de distribuição, que mantém sua própria projeção de leitura. Cada atualização carrega a versão, permitindo ignorar duplicatas e eventos antigos.

Essa etapa pode ser escolhida para aprender sistemas distribuídos, mesmo antes de existir escala comercial. Definir a propagação esperada e testar atraso, indisponibilidade e recuperação. RabbitMQ ou Azure Service Bus são alternativas; escolher uma implementação para o experimento.

Na implantação, separar ambientes, proteger segredos, automatizar migrations com cuidado, acompanhar erros e definir como reverter releases do software. Monitorar idade da configuração distribuída e atraso dos eventos, além da saúde dos processos. Aprender Kubernetes pode ser uma etapa própria depois da operação inicial em containers.

## 14. Validação comercial e próxima decisão

A hipótese de receita é assinatura por projeto/equipe, com limites de ambientes, membros, retenção e sincronização. Avaliações locais no SDK não equivalem a requisições faturáveis. Um plano gratuito de desenvolvimento e um plano pago para equipes são possibilidades a testar.

Observar a integração com dois ou três estúdios antes de expandir: tempo até a primeira publicação útil, dificuldades, uso do rollback, continuidade e disposição de pagar. Clareza, integração em C# e comportamento offline previsível são diferenciais propostos, ainda sujeitos a validação.

A base de frontend foi confirmada (Blazor + Tailwind) e o núcleo de avaliação começou junto de uma demonstração simples (`samples/ToggleForge.RolloutDemo`). Se outra feature motivar mais, podemos mudar a primeira entrega e atualizar este plano.

## Referências

As escolhas do produto são propostas. Estas fontes sustentam conceitos e compatibilidade, sem comprovar rentabilidade:

- [.NET e suporte](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- [Blazor](https://learn.microsoft.com/en-us/aspnet/core/blazor/?view=aspnetcore-10.0)
- [EF Core e concorrência](https://learn.microsoft.com/en-us/ef/core/saving/concurrency)
- [Compatibilidade .NET na Unity](https://docs.unity3d.com/6000.0/Documentation/Manual/dotnet-profile-support.html)
- [OpenFeature](https://openfeature.dev/specification/) — referência para contratos; conformidade ainda não é prometida.
- [OWASP API Security](https://api-security.owasp.org/editions/2023/en/0x11-t10/)
- [Testcontainers para ASP.NET Core](https://dotnet.testcontainers.org/examples/aspnet/)
