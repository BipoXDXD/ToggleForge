using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ToggleForge.Api.Tests.Support;

/// <summary>Passos de navegador que as jornadas do console e os testes de acessibilidade repetem.</summary>
internal static class ConsolePages
{
    /// <summary>Abre o rascunho de <c>dev</c> do projeto <c>space-race</c> pelo endereço: o console pede o login e volta para ele.</summary>
    public static async Task OpenDraftAsync(IPage page, ConsoleClient member)
    {
        await page.GotoAsync($"/organizations/{member.OrganizationId}/projects/space-race/environments/dev");
        await SignInAsync(page, member.Email, ConsoleClient.Password);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Draft flags" })).ToBeVisibleAsync();
    }

    public static async Task SignInAsync(IPage page, string email, string password)
    {
        await page.GetByLabel("Email").FillAsync(email);
        await page.GetByLabel("Password").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
    }

    /// <summary>
    /// Erros de script, violações da CSP e arquivos do console que não carregaram aparecem só no console do
    /// navegador. O status das chamadas à API fica de fora: o 401 de quem ainda não entrou faz parte do fluxo, e
    /// a interface é que mostra se a resposta foi tratada.
    /// </summary>
    public static List<string> CollectErrors(IPage page)
    {
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            var apiCallStatus = message.Text.StartsWith("Failed to load resource", StringComparison.Ordinal)
                && new Uri(message.Location).AbsolutePath.StartsWith("/api/", StringComparison.Ordinal);
            if (message.Type == "error" && !apiCallStatus)
            {
                errors.Add($"{message.Text} at {message.Location}");
            }
        };
        page.PageError += (_, error) => errors.Add(error);
        return errors;
    }
}
