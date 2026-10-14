using ERP.MES.Domain.Requests;

namespace ERP.MES.Domain.Tests.Requests;

public class RequestNoTests
{
    [Theory]
    [InlineData("1")]
    [InlineData("27")]
    [InlineData("846")]
    [InlineData("1234")]
    [InlineData("P-565")]
    [InlineData("wsj-898")]
    [InlineData("A 1")]
    [InlineData("۱۲")] // Persian digits are kept as given, not converted
    public void Free_form_numbers_are_accepted_exactly_as_given(string text)
    {
        var requestNo = RequestNo.Create(text);

        Assert.Equal(text, requestNo.Value);
        Assert.Equal(RequestNoError.None, RequestNo.Validate(text));
    }

    [Theory]
    [InlineData(null, RequestNoError.Empty)]
    [InlineData("", RequestNoError.Empty)]
    [InlineData(" 12", RequestNoError.LeadingOrTrailingWhitespace)]
    [InlineData("12 ", RequestNoError.LeadingOrTrailingWhitespace)]
    [InlineData("\t12", RequestNoError.LeadingOrTrailingWhitespace)]
    [InlineData("12\n", RequestNoError.LeadingOrTrailingWhitespace)]
    [InlineData(" ", RequestNoError.LeadingOrTrailingWhitespace)]
    public void Invalid_numbers_are_rejected_and_never_trimmed(string? text, RequestNoError expected)
    {
        Assert.Equal(expected, RequestNo.Validate(text));
        Assert.False(RequestNo.TryCreate(text, out var requestNo, out var error));
        Assert.Null(requestNo);
        Assert.Equal(expected, error);
        Assert.Throws<ArgumentException>(() => RequestNo.Create(text));
    }

    [Fact]
    public void Letter_case_is_kept_and_comparison_is_case_sensitive()
    {
        var lower = RequestNo.Create("wsj-898");
        var upper = RequestNo.Create("WSJ-898");

        Assert.Equal("WSJ-898", upper.Value);
        Assert.NotEqual(lower, upper);
        Assert.False(lower.Equals(upper));
        Assert.Equal(lower, RequestNo.Create("wsj-898"));
        Assert.Equal(lower.GetHashCode(), RequestNo.Create("wsj-898").GetHashCode());
    }

    [Fact]
    public void Digit_shapes_are_different_values_and_are_not_normalised()
    {
        Assert.NotEqual(RequestNo.Create("12"), RequestNo.Create("۱۲"));
        Assert.NotEqual(RequestNo.Create("12"), RequestNo.Create("١٢"));
    }

    [Fact]
    public void Case_only_differences_are_flagged_as_near_duplicates_but_do_not_block()
    {
        var lower = RequestNo.Create("wsj-898");
        var upper = RequestNo.Create("WSJ-898");

        Assert.True(RequestNo.IsCaseInsensitiveNearDuplicate(lower, upper));
        Assert.False(RequestNo.IsCaseInsensitiveNearDuplicate(lower, RequestNo.Create("wsj-898")));
        Assert.False(RequestNo.IsCaseInsensitiveNearDuplicate(lower, RequestNo.Create("wsj-899")));
    }
}
