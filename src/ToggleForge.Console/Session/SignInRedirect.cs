namespace ToggleForge.Console.Session;

/// <summary>
/// Para onde o login devolve o membro. O destino vem da query string, que qualquer link pode montar: só um
/// endereço dentro do console é aceito, nunca o de outro site (open redirect).
/// </summary>
public static class SignInRedirect
{
    public const string SignInPath = "sign-in";

    /// <param name="returnPath">Caminho relativo à base, como <c>organizations/…/projects</c>.</param>
    public static string SignInPathFor(string returnPath) =>
        string.IsNullOrEmpty(returnPath) || returnPath.StartsWith(SignInPath, StringComparison.OrdinalIgnoreCase)
            ? SignInPath
            : $"{SignInPath}?returnUrl={Uri.EscapeDataString(returnPath)}";

    /// <summary>
    /// Resolve o destino como o navegador resolveria e confere que ele continua sob a base do console. Uma lista
    /// do que recusar não basta: o <see cref="Uri"/> descarta espaços nas pontas, e <c>" //outro.site"</c> vira
    /// um endereço de outro host. O resultado é absoluto porque um pedaço relativo como <c>//outro.site</c>
    /// voltaria a sair do console ao navegar.
    /// </summary>
    /// <param name="consoleBase">A base do console, como <c>https://toggleforge.example/</c>.</param>
    /// <returns>O endereço do destino, ou a base do console se o destino sai dele.</returns>
    public static Uri SafeReturnUri(string? returnUrl, Uri consoleBase)
    {
        if (string.IsNullOrEmpty(returnUrl)
            || !Uri.TryCreate(consoleBase, returnUrl, out var destination)
            || !destination.AbsoluteUri.StartsWith(consoleBase.AbsoluteUri, StringComparison.Ordinal))
        {
            return consoleBase;
        }

        return destination;
    }
}
