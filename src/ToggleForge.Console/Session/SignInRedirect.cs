namespace ToggleForge.Console.Session;

/// <summary>
/// Para onde o login devolve o membro. O destino vem da query string, que qualquer link pode montar: só um
/// caminho relativo à base do console é aceito, nunca um endereço de outro site (open redirect).
/// </summary>
public static class SignInRedirect
{
    public const string SignInPath = "sign-in";

    /// <param name="returnPath">Caminho relativo à base, como <c>organizations/…/projects</c>.</param>
    public static string SignInPathFor(string returnPath) =>
        string.IsNullOrEmpty(returnPath) || returnPath.StartsWith(SignInPath, StringComparison.OrdinalIgnoreCase)
            ? SignInPath
            : $"{SignInPath}?returnUrl={Uri.EscapeDataString(returnPath)}";

    /// <returns>O caminho, se for relativo à base do console; senão, a página inicial.</returns>
    public static string SafeReturnPath(string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl)
            || returnUrl[0] is '/' or '\\'
            || returnUrl.Contains(':', StringComparison.Ordinal)
            || returnUrl.Any(char.IsControl))
        {
            return string.Empty;
        }

        return returnUrl;
    }
}
