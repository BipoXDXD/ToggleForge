using System.ComponentModel.DataAnnotations;

namespace ToggleForge.Api.Http;

/// <summary>Os mesmos limites do contrato e das regras do Identity, para o Identity nunca recusar o que a API aceitou.</summary>
internal static class AccountInput
{
    public const int MinNewPasswordLength = 12;

    private const int MinEmailLength = 3;
    private const int MaxEmailLength = 254;
    private const int MaxPasswordLength = 128;

    private static readonly EmailAddressAttribute _emailFormat = new();

    public static string? Email(string? value, InputErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        if (value is null)
        {
            errors.MissingInBody("/email");
            return null;
        }

        if (value is { Length: >= MinEmailLength and <= MaxEmailLength } && _emailFormat.IsValid(value))
        {
            return value;
        }

        errors.InBody("/email", "Use a valid email address with up to 254 characters.");
        return null;
    }

    public static string? Password(string? value, int minLength, InputErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        if (value is null)
        {
            errors.MissingInBody("/password");
            return null;
        }

        if (value.Length >= minLength && value.Length <= MaxPasswordLength && !value.Any(char.IsControl))
        {
            return value;
        }

        errors.InBody("/password", $"Use {minLength} to {MaxPasswordLength} characters without control characters.");
        return null;
    }
}
