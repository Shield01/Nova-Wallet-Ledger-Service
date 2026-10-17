using NovaWallet.Application.Idempotency;

namespace NovaWallet.UnitTests.Idempotency;

public class IdempotentRequestTests
{
    [Fact]
    public void Fingerprint_depends_on_values_not_on_how_they_were_sent()
    {
        var a = IdempotentRequest.For("c", "k", "transfer", ("amountKobo", "100"), ("to", "x"));
        var b = IdempotentRequest.For("c", "k", "transfer", ("amountKobo", "100"), ("to", "x"));
        var c = IdempotentRequest.For("c", "k", "transfer", ("amountKobo", "101"), ("to", "x"));
        var d = IdempotentRequest.For("c", "k", "credit", ("amountKobo", "100"), ("to", "x"));

        Assert.Equal(a.RequestHash, b.RequestHash);
        Assert.NotEqual(a.RequestHash, c.RequestHash);
        Assert.NotEqual(a.RequestHash, d.RequestHash);
        Assert.Equal(64, a.RequestHash.Length);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("8f14e45f-ceea-4e67-a8f0-6d2a3c1b9e01")]
    [InlineData("~!#$%")]
    public void Printable_ascii_keys_are_accepted(string key) => Assert.Null(IdempotentRequest.Validate(key));

    [Theory]
    [InlineData("naïve")]
    [InlineData("with space")]
    [InlineData("new\nline")]
    public void Keys_with_other_characters_are_invalid(string key) =>
        Assert.Equal("IDEMPOTENCY_KEY_INVALID", IdempotentRequest.Validate(key)!.Code);

    [Fact]
    public void Key_length_limit_is_128()
    {
        Assert.Null(IdempotentRequest.Validate(new string('k', 128)));
        Assert.Equal("IDEMPOTENCY_KEY_INVALID", IdempotentRequest.Validate(new string('k', 129))!.Code);
    }
}
