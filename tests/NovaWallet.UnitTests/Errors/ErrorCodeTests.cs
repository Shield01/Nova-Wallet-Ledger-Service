using NovaWallet.Domain.Errors;

namespace NovaWallet.UnitTests.Errors;

public class ErrorCodeTests
{
    [Fact]
    public void Error_codes_are_unique_upper_snake_case()
    {
        Assert.Equal(ErrorCodes.All.Count, ErrorCodes.All.Distinct().Count());
        Assert.All(ErrorCodes.All, code => Assert.Matches("^[A-Z]+(_[A-Z]+)*$", code));
    }
}
