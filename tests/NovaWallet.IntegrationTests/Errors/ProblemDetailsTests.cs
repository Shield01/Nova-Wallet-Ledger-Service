using System.Net;
using System.Net.Http.Json;
using System.Text;
using NovaWallet.IntegrationTests.Infrastructure;

namespace NovaWallet.IntegrationTests.Errors;

public class ProblemDetailsTests(NovaWalletApiFactory factory) : IClassFixture<NovaWalletApiFactory>
{
    [Fact]
    public async Task Problem_responses_carry_every_standard_field()
    {
        var response = await factory.CreateClient().GetAsync("/test/refuse/INSUFFICIENT_FUNDS");

        var problem = await response.AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "INSUFFICIENT_FUNDS");
        Assert.Equal("https://novawallet.example/errors/insufficient-funds", problem.GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("title").GetString()));
        Assert.Equal("Refused by the test endpoint.", problem.GetProperty("detail").GetString());
        Assert.Equal("/test/refuse/INSUFFICIENT_FUNDS", problem.GetProperty("instance").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.Equal(42, problem.GetProperty("example").GetInt32());
    }

    [Fact]
    public async Task Unhandled_exception_returns_500_without_leaking_internals()
    {
        var response = await factory.CreateClient().GetAsync("/test/throw");

        var problem = await response.AssertProblemAsync(HttpStatusCode.InternalServerError, "INTERNAL_ERROR");
        var raw = problem.GetRawText();
        Assert.DoesNotContain(TestOnlyController.SecretMessage, raw);
        Assert.DoesNotContain("InvalidOperationException", raw);
        Assert.DoesNotContain("   at ", raw);
    }

    [Fact]
    public async Task Invalid_body_returns_validation_problem_keyed_by_camel_case_field()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/test/validate", new { customerName = "", amountKobo = 0 });

        var problem = await response.AssertProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        var errors = problem.GetProperty("errors");
        Assert.True(errors.TryGetProperty("customerName", out _), errors.GetRawText());
        Assert.True(errors.TryGetProperty("amountKobo", out _), errors.GetRawText());
    }

    [Theory]
    [InlineData("""{"customerName":"a","amountKobo":"100"}""")]
    [InlineData("""{"customerName":"a","amountKobo":10.5}""")]
    [InlineData("""{"customerName":"a","amountKobo":}""")]
    public async Task Amounts_that_are_not_json_integers_are_rejected(string json)
    {
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await factory.CreateClient().PostAsync("/test/validate", content);

        var problem = await response.AssertProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        Assert.True(problem.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task Unknown_route_returns_not_found_problem()
    {
        var response = await factory.CreateClient().GetAsync("/no/such/route");

        await response.AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task Unsupported_method_returns_405_problem()
    {
        var response = await factory.CreateClient().DeleteAsync("/test/validate");

        await response.AssertProblemAsync(HttpStatusCode.MethodNotAllowed, "METHOD_NOT_ALLOWED");
    }

    [Fact]
    public async Task Unsupported_content_type_returns_415_problem()
    {
        var content = new StringContent("customerName=a", Encoding.UTF8, "text/plain");

        var response = await factory.CreateClient().PostAsync("/test/validate", content);

        await response.AssertProblemAsync(HttpStatusCode.UnsupportedMediaType, "UNSUPPORTED_MEDIA_TYPE");
    }

    [Fact]
    public async Task Unreachable_database_returns_503()
    {
        using var broken = factory.WithSettings(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Ledger"] = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2",
        });

        var response = await broken.CreateClient().GetAsync("/test/db");

        await response.AssertProblemAsync(HttpStatusCode.ServiceUnavailable, "SERVICE_UNAVAILABLE");
    }
}
