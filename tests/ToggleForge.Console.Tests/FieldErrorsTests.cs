using ToggleForge.Console.Api;
using ToggleForge.Console.Drafts;

namespace ToggleForge.Console.Tests;

public sealed class FieldErrorsTests
{
    [Fact]
    public void Errors_on_the_fields_leave_nothing_for_the_general_message()
    {
        var (fields, rest) = FieldErrors.Split(Invalid(("/onValue", "The value is not JSON."), ("/offValue/a/b", "Repeats a key.")));

        Assert.Equal("The value is not JSON.", fields.For(FieldErrors.OnValue));
        Assert.Equal("Repeats a key.", fields.For(FieldErrors.OffValue));
        Assert.Null(rest);
    }

    [Fact]
    public void An_error_on_another_field_stays_in_the_general_message()
    {
        var (fields, rest) = FieldErrors.Split(Invalid(("/key", "Use lowercase letters."), ("/rolloutBasisPoints", "Use 0 to 10000.")));

        Assert.Equal("Use 0 to 10000.", fields.For(FieldErrors.Rollout));
        Assert.Equal(["Use lowercase letters."], rest!.Messages);
    }

    [Fact]
    public void A_pointer_that_only_starts_like_a_field_is_not_that_field()
    {
        var (fields, rest) = FieldErrors.Split(Invalid(("/onValueX", "Unknown field.")));

        Assert.True(fields.IsEmpty);
        Assert.Equal(["Unknown field."], rest!.Messages);
    }

    [Fact]
    public void An_error_without_a_pointer_stays_in_the_general_message()
    {
        var problem = new Problem(
            "/problems/invalid-input", "Invalid input", 400, Errors: [new ProblemError("Bad header."), new ProblemError("Bad body.", new ProblemSource())]);

        var (fields, rest) = FieldErrors.Split(problem);

        Assert.True(fields.IsEmpty);
        Assert.Same(problem, rest);
    }

    [Fact]
    public void A_problem_without_errors_stays_whole()
    {
        var conflict = new Problem("/problems/json-budget-reached", "JSON budget reached", 409, "A draft can hold at most 128 KiB of json values.");

        var (fields, rest) = FieldErrors.Split(conflict);

        Assert.True(fields.IsEmpty);
        Assert.Same(conflict, rest);
    }

    [Fact]
    public void Each_field_keeps_its_first_message()
    {
        var (fields, _) = FieldErrors.Split(Invalid(("/onValue", "First."), ("/onValue", "Second.")));

        Assert.Equal("First.", fields.For(FieldErrors.OnValue));
    }

    [Fact]
    public void A_field_without_errors_has_no_message()
    {
        var (fields, _) = FieldErrors.Split(Invalid(("/onValue", "The value is not JSON.")));

        Assert.Null(fields.For(FieldErrors.OffValue));
    }

    private static Problem Invalid(params (string Pointer, string Detail)[] errors) => new(
        "/problems/invalid-input", "Invalid input", 400,
        Errors: errors.Select(error => new ProblemError(error.Detail, new ProblemSource(error.Pointer))).ToList());
}
