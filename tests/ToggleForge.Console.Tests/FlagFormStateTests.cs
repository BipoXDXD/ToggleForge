using ToggleForge.Console.Api;
using ToggleForge.Console.Drafts;

namespace ToggleForge.Console.Tests;

public sealed class FlagFormStateTests
{
    private static readonly Problem _keyTaken = new("/problems/key-taken", "Key taken", 409, "The draft already has this key.");

    [Fact]
    public void Marking_fields_puts_the_fix_summary_above_them()
    {
        var form = new FlagFormState();

        form.Mark(Errors(FieldErrors.Rollout));

        Assert.Equal(FormProblems.FixMarkedFields, form.Problem);
        Assert.Equal("Wrong.", form.Fields.For(FieldErrors.Rollout));
    }

    [Fact]
    public void Marking_no_field_leaves_no_summary()
    {
        var form = new FlagFormState();
        form.Show(_keyTaken);

        form.Mark(new FieldErrors());

        Assert.Null(form.Problem);
    }

    [Fact]
    public void What_the_api_said_outside_the_fields_is_the_summary()
    {
        var form = new FlagFormState();

        form.Mark(Errors(FieldErrors.OnValue), _keyTaken);

        Assert.Equal(_keyTaken, form.Problem);
    }

    [Fact]
    public void The_summary_leaves_with_the_last_marked_field()
    {
        var form = new FlagFormState();
        form.Mark(Errors(FieldErrors.OnValue, FieldErrors.OffValue));

        form.Clear(FieldErrors.OnValue);
        Assert.Equal(FormProblems.FixMarkedFields, form.Problem);
        form.Clear(FieldErrors.OffValue);

        Assert.Null(form.Problem);
        Assert.True(form.Fields.IsEmpty);
    }

    [Fact]
    public void A_summary_from_the_api_stays_after_the_fields_are_cleared()
    {
        var form = new FlagFormState();
        form.Mark(Errors(FieldErrors.OnValue), _keyTaken);

        form.Clear(FieldErrors.OnValue);

        Assert.Equal(_keyTaken, form.Problem);
    }

    [Theory]
    [InlineData(new[] { FieldErrors.OffValue, FieldErrors.OnValue }, FieldErrors.OnValue)]
    [InlineData(new[] { FieldErrors.OffValue, FieldErrors.Rollout }, FieldErrors.Rollout)]
    [InlineData(new[] { FieldErrors.Rollout, FieldErrors.Key }, FieldErrors.Key)]
    [InlineData(new[] { FieldErrors.OffValue }, FieldErrors.OffValue)]
    public void The_first_marked_field_follows_the_page_order(string[] marked, string first)
    {
        var form = new FlagFormState();

        form.Mark(Errors(marked));

        Assert.Equal(first, form.FirstMarkedField);
    }

    [Fact]
    public void No_field_is_first_when_none_is_marked()
    {
        Assert.Null(new FlagFormState().FirstMarkedField);
    }

    [Fact]
    public void Only_the_json_budget_gets_a_heading_from_the_console()
    {
        var form = new FlagFormState();

        form.Show(FormProblems.OutOfJsonRoom);
        Assert.Equal("This draft is out of room for JSON values.", form.Heading);
        form.Show(_keyTaken);

        Assert.Null(form.Heading);
    }

    [Fact]
    public void A_new_attempt_starts_from_nothing()
    {
        var form = new FlagFormState();
        form.Mark(Errors(FieldErrors.Rollout));
        form.MarkValuesTooLarge();

        form.Reset();

        Assert.True(form.Fields.IsEmpty);
        Assert.Null(form.Problem);
        Assert.False(form.ValuesTooLarge);
    }

    [Fact]
    public void Clearing_the_summary_keeps_the_field_errors()
    {
        var form = new FlagFormState();
        form.Mark(Errors(FieldErrors.Rollout));

        form.ClearSummary();

        Assert.Null(form.Problem);
        Assert.Equal("Wrong.", form.Fields.For(FieldErrors.Rollout));
    }

    [Fact]
    public void Clearing_the_values_keeps_the_errors_of_the_other_fields()
    {
        var form = new FlagFormState();
        form.Mark(Errors(FieldErrors.Key, FieldErrors.OnValue, FieldErrors.OffValue));
        form.MarkValuesTooLarge();

        form.ClearValues();

        Assert.Equal("Wrong.", form.Fields.For(FieldErrors.Key));
        Assert.Equal([null, null], [form.Fields.For(FieldErrors.OnValue), form.Fields.For(FieldErrors.OffValue)]);
        Assert.False(form.ValuesTooLarge);
        Assert.Equal(FormProblems.FixMarkedFields, form.Problem);
    }

    [Fact]
    public void Clearing_the_last_marked_values_takes_the_summary_with_them()
    {
        var form = new FlagFormState();
        form.Mark(Errors(FieldErrors.OnValue, FieldErrors.OffValue));

        form.ClearValues();

        Assert.Null(form.Problem);
    }

    private static FieldErrors Errors(params string[] fields)
    {
        var errors = new FieldErrors();
        foreach (var field in fields)
        {
            errors.Add(field, "Wrong.");
        }

        return errors;
    }
}
