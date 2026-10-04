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
}
