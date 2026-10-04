using Bunit;
using ToggleForge.Console.Components;

namespace ToggleForge.Console.Tests;

public sealed class DisplayComponentsTests
{
    [Theory]
    [InlineData("boolean", "boolean")]
    [InlineData("json", "JSON")]
    [InlineData("<b>remote-config</b>", "<b>remote-config</b>")]
    public void The_badge_names_the_type(string type, string shown)
    {
        using var console = new BunitContext();

        var badge = console.Render<FlagTypeBadge>(parameters => parameters.Add(component => component.Type, type));

        Assert.Equal(shown, badge.Find("[data-testid=flag-type]").TextContent);
    }

    // Um valor de 4 KiB indentado passa da altura do bloco: quem só usa o teclado precisa chegar nele para rolar.
    [Fact]
    public void An_open_value_scrolls_inside_a_named_group_the_keyboard_reaches()
    {
        using var console = new BunitContext();

        var block = console.Render<JsonValueBlock>(parameters => parameters
            .Add(component => component.Value, Values.Json("""{"discount":0.2}"""))
            .Add(component => component.Open, true));

        var value = block.Find("details pre");
        Assert.Equal(["group", "0", "Value, indented"], [value.GetAttribute("role"), value.GetAttribute("tabindex"), value.GetAttribute("aria-label")]);
        Assert.Contains("overflow-auto", value.ClassList);
    }

    [Fact]
    public void A_preview_shows_the_start_of_the_value_and_keeps_all_of_it_in_its_title()
    {
        using var console = new BunitContext();
        var value = Values.Json("""{"discount":0.2,"featuredItems":["sword","shield"]}""");

        var preview = console.Render<ValuePreview>(parameters => parameters.Add(component => component.Value, value));

        Assert.Equal("{\"discount\":0.2,\"featuredItems\":[\"sword…", preview.Find("code").TextContent);
        Assert.Equal("""{"discount":0.2,"featuredItems":["sword","shield"]}""", preview.Find("code").GetAttribute("title"));
    }

    [Fact]
    public void A_preview_shows_markup_as_text()
    {
        using var console = new BunitContext();

        var preview = console.Render<ValuePreview>(parameters => parameters.Add(component => component.Value, Values.Json("\"<script>x</script>\"")));

        Assert.Empty(preview.FindAll("script"));
        Assert.Equal("\"<script>x</script>\"", preview.Find("code").TextContent);
    }

    [Fact]
    public void A_shortened_preview_says_it_is_shortened()
    {
        using var console = new BunitContext();
        var value = Values.Json($"\"{new string('a', 58)}\"");

        var preview = console.Render<ValuePreview>(parameters => parameters.Add(component => component.Value, value));

        Assert.Equal(", shortened", preview.Find(".sr-only").TextContent);
    }

    [Fact]
    public void A_preview_that_fits_does_not_say_it_is_shortened()
    {
        using var console = new BunitContext();
        var value = Values.Json($"\"{new string('a', 38)}\"");

        var preview = console.Render<ValuePreview>(parameters => parameters.Add(component => component.Value, value));

        Assert.Empty(preview.FindAll(".sr-only"));
    }
}
