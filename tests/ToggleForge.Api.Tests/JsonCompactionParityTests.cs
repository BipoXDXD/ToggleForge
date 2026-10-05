using System.Text;
using ToggleForge.Domain.Authoring;
using ConsoleFlagValue = ToggleForge.Console.Api.FlagValue;

namespace ToggleForge.Api.Tests;

/// <summary>
/// Ameaça T5 do canvas (<c>docs/api/configuracoes-json.md</c>): o console e o jogo não podem ler valores diferentes do
/// mesmo JSON. A API grava e o jogo recebe o texto do <see cref="JsonFlagValue"/>; o console mostra e envia o do
/// <see cref="ConsoleFlagValue"/>. São dois projetos que não se referenciam (ADR 0009), então só este teste prende as
/// duas compactações uma à outra. Os limites de produto (tamanho, profundidade, chave repetida, número fora do
/// <see cref="double"/>) são só da API, e o console os deixa passar de propósito: a recusa vem da API, com o campo.
/// </summary>
public sealed class JsonCompactionParityTests
{
    // Fixa: um caso que falhe tem de falhar de novo na próxima execução, com a mesma semente.
    private const int FuzzSeed = 20261004;

    private const int FuzzCases = 500;

    private const int FuzzMaxDepth = 4;

    [Theory]
    [InlineData("\"ação çã ü\"", "\"ação çã ü\"")]
    [InlineData("\"<script>&'\"", "\"<script>&'\"")]
    [InlineData("\"😀\"", "\"\\uD83D\\uDE00\"")]
    [InlineData("\"\\uD83D\\uDE00\"", "\"\\uD83D\\uDE00\"")]
    [InlineData("\"𝄞\"", "\"\\uD834\\uDD1E\"")]
    [InlineData("\"a\u2028b\u2029c\"", "\"a\\u2028b\\u2029c\"")]
    [InlineData("\"a\\/b\"", "\"a/b\"")]
    [InlineData("\"\\u00e9\"", "\"é\"")]
    [InlineData("\"\\u0001\\t\\n\\r\\b\\f\"", "\"\\u0001\\t\\n\\r\\b\\f\"")]
    [InlineData("\"\\\"\\\\\"", "\"\\\"\\\\\"")]
    [InlineData("\"\"", "\"\"")]
    [InlineData("1.50", "1.50")]
    [InlineData("-0", "-0")]
    [InlineData("1e3", "1e3")]
    [InlineData("1E+2", "1E+2")]
    [InlineData("-12.5e-3", "-12.5e-3")]
    [InlineData("null", "null")]
    [InlineData("true", "true")]
    [InlineData("{ \"b\" : 1 , \"a\" : 2 }", "{\"b\":1,\"a\":2}")]
    [InlineData(" \r\n\t[ 1 ,\n 2 ] \n", "[1,2]")]
    [InlineData("{\"\":\"\",\"x\":[]}", "{\"\":\"\",\"x\":[]}")]
    [InlineData("{\"é\\u00e9\":{}}", "{\"éé\":{}}")]
    [InlineData("[[[[[[[[null]]]]]]]]", "[[[[[[[[null]]]]]]]]")]
    [InlineData(
        "{\"a\":{\"b\":[{\"c\":[{\"d\":{\"e\":[\"😀\"]}}]}]}}",
        "{\"a\":{\"b\":[{\"c\":[{\"d\":{\"e\":[\"\\uD83D\\uDE00\"]}}]}]}}")]
    public void Both_sides_compact_an_accepted_value_to_the_same_text(string json, string expected)
    {
        Assert.True(JsonFlagValue.TryParse(json, out var api, out var problem), $"A API recusou ({problem}).");
        Assert.True(ConsoleFlagValue.TryParse(json, out var console), "O console recusou.");
        Assert.Equal(expected, api.Text);
        Assert.Equal(expected, console.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[1,]")]
    [InlineData("{\"a\":1,}")]
    [InlineData("// c\n1")]
    [InlineData("/* c */ 1")]
    [InlineData("'a'")]
    [InlineData("{a:1}")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("01")]
    [InlineData("1.")]
    [InlineData("+1")]
    [InlineData("1 2")]
    [InlineData("\"sem fim")]
    [InlineData("\"tab\tcru\"")]
    [InlineData("\"\\x41\"")]
    [InlineData("\"\\uD800\"")]
    [InlineData("\"\\uDC00\"")]
    [InlineData("{\"\\uD800\":1}")]
    [InlineData("True")]
    public void Both_sides_refuse_the_same_syntax_errors(string text)
    {
        Assert.False(JsonFlagValue.TryParse(text, out _, out var problem));
        Assert.Equal(JsonValueProblem.NotJson, problem);
        Assert.False(ConsoleFlagValue.TryParse(text, out _));
    }

    // Fora do [InlineData]: o xUnit serializa os dados do teste, e um surrogate isolado chegaria como U+FFFD.
    [Fact]
    public void Both_sides_refuse_a_raw_lone_surrogate()
    {
        var text = "\"" + '\uD800' + "\"";

        Assert.False(JsonFlagValue.TryParse(text, out _, out var problem));
        Assert.Equal(JsonValueProblem.NotJson, problem);
        Assert.False(ConsoleFlagValue.TryParse(text, out _));
    }

    [Fact]
    public void Both_sides_refuse_a_missing_text()
    {
        Assert.False(JsonFlagValue.TryParse(null, out _, out _));
        Assert.False(ConsoleFlagValue.TryParse(null, out _));
    }

    public static TheoryData<string, JsonValueProblem> ProductLimits => new()
    {
        { "{\"a\":1,\"a\":2}", JsonValueProblem.DuplicateKey },
        { "[{\"x\":{\"a\":1,\"a\":1}}]", JsonValueProblem.DuplicateKey },
        { "[[[[[[[[[1]]]]]]]]]", JsonValueProblem.TooDeep },
        { "1e400", JsonValueProblem.NumberOutOfRange },
        { "-1e400", JsonValueProblem.NumberOutOfRange },
        { $"\"{new string('a', JsonFlagValue.MaxBytes - 1)}\"", JsonValueProblem.TooLarge },
    };

    [Theory]
    [MemberData(nameof(ProductLimits))]
    public void The_console_parser_leaves_the_product_limits_to_the_checks_that_follow(string json, JsonValueProblem expected)
    {
        Assert.False(JsonFlagValue.TryParse(json, out _, out var problem));
        Assert.Equal(expected, problem);
        Assert.True(ConsoleFlagValue.TryParse(json, out _), "O FlagValue confere só a sintaxe; os limites ficam com a API e, ao salvar, com o FlagFields.");
    }

    [Fact]
    public void Both_sides_compact_random_values_to_the_same_text()
    {
        var random = new Random(FuzzSeed);
        for (var index = 0; index < FuzzCases; index++)
        {
            var json = RandomJson.Value(random, FuzzMaxDepth);

            Assert.True(JsonFlagValue.TryParse(json, out var api, out var problem), $"Caso {index}, a API recusou ({problem}): {json}");
            Assert.True(ConsoleFlagValue.TryParse(json, out var console), $"Caso {index}, o console recusou: {json}");
            Assert.True(api.Text == console.Text, $"Caso {index}: {json}\nAPI:     {api.Text}\nconsole: {console.Text}");
        }
    }

    /// <summary>Valores JSON pequenos e válidos para a API, cheios dos caracteres que os encoders tratam diferente.</summary>
    private static class RandomJson
    {
        // Pedaços já em sintaxe de string JSON: o mesmo caractere aparece cru e escapado, para os dois lados terem de
        // concordar em desescapar e escapar de novo.
        private static readonly string[] _stringPieces =
        [
            "a", "Z", " ", "é", "\\u00e9", "ção", "<", ">", "&", "'", "\\u003C", "\\/", "/", "\\\"", "\\\\",
            "😀", "\\uD83D\\uDE00", "𝄞", "\u2028", "\u2029", "\\u2028", "\\n", "\\t", "\\r", "\\b", "\\f",
            "\\u0001", "\\u001f", "\\u007f", "\u007f", "\u00a0", "\ufeff", "\\uFFFF", "中", "+", "`",
        ];

        private static readonly string[] _numbers =
        [
            "0", "-0", "1", "-1", "1.50", "0.0", "1e3", "1E+2", "1e-7", "-12.5E-3", "123456789012345678",
            "3.141592653589793238", "1.7976931348623157e308", "5e-324",
        ];

        private static readonly string[] _whitespace = ["", "", "", " ", "\n", "\t", "\r\n", "  "];

        private const int MaxStringPieces = 6;

        private const int MaxMembers = 4;

        public static string Value(Random random, int depth)
        {
            var builder = new StringBuilder();
            AppendValue(builder, random, depth);
            return builder.ToString();
        }

        private static void AppendValue(StringBuilder builder, Random random, int depth)
        {
            builder.Append(Pick(random, _whitespace));
            var kinds = depth > 0 ? 5 : 3;
            switch (random.Next(kinds))
            {
                case 0:
                    AppendString(builder, random, prefix: "");
                    break;
                case 1:
                    builder.Append(Pick(random, _numbers));
                    break;
                case 2:
                    builder.Append(Pick(random, ["true", "false", "null"]));
                    break;
                case 3:
                    AppendArray(builder, random, depth - 1);
                    break;
                case 4:
                    AppendObject(builder, random, depth - 1);
                    break;
                default:
                    throw new InvalidOperationException("Tipo de valor sem gerador.");
            }

            builder.Append(Pick(random, _whitespace));
        }

        private static void AppendArray(StringBuilder builder, Random random, int depth)
        {
            builder.Append('[');
            var count = random.Next(MaxMembers + 1);
            for (var index = 0; index < count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                AppendValue(builder, random, depth);
            }

            builder.Append(']');
        }

        // O prefixo com o índice impede chave repetida, que só a API recusa e não é o que este gerador mede.
        private static void AppendObject(StringBuilder builder, Random random, int depth)
        {
            builder.Append('{');
            var count = random.Next(MaxMembers + 1);
            for (var index = 0; index < count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                builder.Append(Pick(random, _whitespace));
                AppendString(builder, random, prefix: $"{index}");
                builder.Append(Pick(random, _whitespace)).Append(':');
                AppendValue(builder, random, depth);
            }

            builder.Append('}');
        }

        private static void AppendString(StringBuilder builder, Random random, string prefix)
        {
            builder.Append('"').Append(prefix);
            var count = random.Next(MaxStringPieces + 1);
            for (var index = 0; index < count; index++)
            {
                builder.Append(Pick(random, _stringPieces));
            }

            builder.Append('"');
        }

        private static string Pick(Random random, string[] options) => options[random.Next(options.Length)];
    }
}
