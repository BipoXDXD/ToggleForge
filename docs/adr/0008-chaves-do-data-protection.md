# 0008 — Chaves do Data Protection no Postgres, cifradas por certificado

- **Status:** Aceita
- **Data:** 2026-10-03
- **Resolve:** a pendência de deploy da ADR 0005

## Contexto

O Data Protection do ASP.NET Core protege o cookie de sessão, o token antiforgery e os tokens de página do
histórico. Por padrão, as chaves ficam no perfil do usuário da máquina. Assim, cada réplica teria as suas,
e um restart do container apagaria todas. Um cookie emitido por uma instância falharia em outra, e todo
deploy derrubaria as sessões. Quem tem as chaves forja qualquer sessão, então o lugar delas também é uma
decisão de segurança.

Alternativas consideradas:

1. **Postgres, cifrado por um certificado X.509 montado como segredo:** sem infraestrutura nova, igual no
   Container Apps e no Kubernetes. Contra: rotacionar o certificado é tarefa nossa.
2. **Azure Blob Storage com Key Vault:** o caminho recomendado no Azure, com a rotação no cofre. Contra:
   dois recursos Azure, SDKs do Azure e Azurite nos testes, e o deploy preso ao Azure.
3. **Postgres sem cifra:** o mais simples. Contra: quem lê o banco forja sessões.

## Decisão

**Postgres com certificado** (escolha do usuário):

- **Armazenamento:** as chaves ficam em `access.data_protection_keys`, pelo pacote oficial
  `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`. O módulo Access é o dono, porque a sessão é dele.
- **Cifra:** cada chave é cifrada pelo certificado de `DataProtection:CertificatePath` e
  `DataProtection:KeyPath`, em PEM, o formato que um secret do Kubernetes ou do Container Apps monta como
  arquivo.
- **Rotação:**
  1. O certificado novo vira o atual, e o antigo vai para `DataProtection:PreviousCertificatePath` e
     `DataProtection:PreviousKeyPath`, onde só decifra.
  2. Ele fica lá até as chaves que cifrou expirarem: 90 dias, o padrão do Data Protection.
  3. Depois disso, sai da configuração.
- **Nome de aplicação fixo** (`ToggleForge`): sem ele, o isolamento das chaves depende do caminho do app,
  e uma imagem instalada em outra pasta derrubaria todas as sessões.
- **Partida falha:**
  - fora do Development, sem certificado, a API não sobe;
  - metade de um par configurada também impede a partida, em qualquer ambiente.
  - No Development, sem certificado, as chaves ficam no banco sem cifra, só para rodar localmente.

## Consequências

- Todas as réplicas e restarts leem os mesmos cookies e tokens, e o deploy deixa de derrubar sessões.
- Ler o banco não basta para forjar uma sessão: falta o certificado, que fica fora dele.
- O certificado é um segredo novo do deploy. Ele vai por arquivo montado, nunca na imagem nem no repositório.
  A rotação segue o roteiro acima.
- Os testes geram certificados autoassinados em tempo de execução. O host padrão usa um como atual e o
  outro como anterior, para os testes de rotação não deixarem chaves ilegíveis no banco compartilhado.

## Compliance

- `DataProtectionTests`:
  - toda chave no banco está cifrada (`<encryptedSecret>`, nunca `<masterKey>` em claro);
  - a sessão sobrevive à rotação e a uma instalação em outra pasta;
  - sem o certificado que cifrou a chave, a sessão é recusada;
  - sem certificado fora do Development, ou com meio par configurado, a API não sobe.
