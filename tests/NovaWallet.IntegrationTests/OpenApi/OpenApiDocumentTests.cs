using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NovaWallet.Api.Idempotency;
using NovaWallet.IntegrationTests.Infrastructure;

namespace NovaWallet.IntegrationTests.OpenApi;

public partial class OpenApiDocumentTests(NovaWalletApiFactory factory) : IClassFixture<NovaWalletApiFactory>
{
    private const string DocumentPath = "/swagger/v1/swagger.json";
    private static readonly string[] Verbs = ["get", "post", "put", "patch", "delete"];

    [Fact]
    public async Task Document_and_ui_are_served_anonymously()
    {
        var document = await factory.CreateClient().GetAsync(DocumentPath);
        var ui = await factory.CreateClient().GetAsync("/swagger");

        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
        Assert.StartsWith("3.", (await document.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("openapi").GetString());
        Assert.Equal(HttpStatusCode.OK, ui.StatusCode);
        Assert.Equal("text/html", ui.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Disabled_document_is_not_mapped()
    {
        using var disabled = factory.WithSettings(new Dictionary<string, string?> { ["OpenApi:Enabled"] = "false" });

        await AssertNotServedAsync(disabled);
    }

    [Fact]
    public async Task Production_does_not_serve_the_document_by_default()
    {
        using var production = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Auth:DevIssuer:Enabled"] = "false" }));
        });

        await AssertNotServedAsync(production);
    }

    [Fact]
    public async Task Bearer_scheme_is_declared_and_every_api_operation_requires_it_and_documents_401()
    {
        var document = await DocumentAsync();

        var scheme = document.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
        var operations = ApiOperations(document).ToList();
        Assert.NotEmpty(operations);
        foreach (var (name, operation) in operations)
        {
            var schemes = operation.TryGetProperty("security", out var security)
                ? security.EnumerateArray().SelectMany(r => r.EnumerateObject().Select(p => p.Name)).ToList()
                : [];
            Assert.True(schemes.Contains("Bearer"), $"{name} does not reference the bearer scheme.");
            Assert.True(operation.GetProperty("responses").TryGetProperty("401", out _), $"{name} does not document 401.");
        }
    }

    [Fact]
    public async Task Anonymous_operations_do_not_reference_the_bearer_scheme()
    {
        var document = await DocumentAsync();

        var devToken = document.GetProperty("paths").GetProperty("/dev/token").GetProperty("post");

        Assert.False(devToken.TryGetProperty("security", out var security) && security.GetArrayLength() > 0);
    }

    [Fact]
    public async Task Every_api_error_response_is_problem_details()
    {
        var document = await DocumentAsync();

        foreach (var (name, operation) in ApiOperations(document))
        {
            foreach (var response in operation.GetProperty("responses").EnumerateObject().Where(r => r.Name[0] is '4' or '5'))
            {
                var types = response.Value.GetProperty("content").EnumerateObject().Select(c => c.Name).ToList();
                Assert.True(types.SequenceEqual(["application/problem+json"]), $"{name} {response.Name} uses {string.Join(", ", types)}.");
            }
        }
    }

    [Fact]
    public async Task Every_kobo_property_is_an_int64_integer()
    {
        var document = await DocumentAsync();

        var kobo = document.GetProperty("components").GetProperty("schemas").EnumerateObject()
            .SelectMany(s => s.Value.TryGetProperty("properties", out var p) ? p.EnumerateObject().Select(x => (Schema: s.Name, Property: x)) : [])
            .Where(x => x.Property.Name.EndsWith("Kobo", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(kobo);
        foreach (var (schema, property) in kobo)
        {
            Assert.True(
                property.Value.GetProperty("type").GetString() == "integer" && property.Value.GetProperty("format").GetString() == "int64",
                $"{schema}.{property.Name} is not integer/int64.");
        }
    }

    [Fact]
    public async Task Every_endpoint_with_the_idempotency_marker_documents_the_required_header()
    {
        var document = await DocumentAsync();
        var marked = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<RequiresIdempotencyKeyAttribute>() is not null)
            .SelectMany(e => e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Select(m => (Path: "/" + Constraint().Replace(e.RoutePattern.RawText!, "{$1}"), Method: m.ToLowerInvariant())))
            .ToList();

        Assert.True(marked.Count >= 2, "Expected at least the credit and transfer endpoints to carry the marker.");
        foreach (var (path, method) in marked)
        {
            var header = document.GetProperty("paths").GetProperty(path).GetProperty(method).GetProperty("parameters").EnumerateArray()
                .SingleOrDefault(p => p.GetProperty("in").GetString() == "header" && p.GetProperty("name").GetString() == "Idempotency-Key");
            Assert.True(header.ValueKind == JsonValueKind.Object, $"{method} {path} does not document Idempotency-Key.");
            Assert.True(header.GetProperty("required").GetBoolean(), $"{method} {path} documents Idempotency-Key as optional.");
        }
    }

    private async Task AssertNotServedAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host)
    {
        var authenticated = host.CreateClient().WithBearer(factory.Tokens.For("reader", TestTokens.WalletScope));

        Assert.Equal(HttpStatusCode.NotFound, (await authenticated.GetAsync(DocumentPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await authenticated.GetAsync("/swagger/index.html")).StatusCode);

        // Like any unmapped path, an anonymous caller is told to authenticate.
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.CreateClient().GetAsync(DocumentPath)).StatusCode);
    }

    private static IEnumerable<(string Name, JsonElement Operation)> ApiOperations(JsonElement document) =>
        document.GetProperty("paths").EnumerateObject()
            .Where(p => p.Name.StartsWith("/api/v1/", StringComparison.Ordinal))
            .SelectMany(p => p.Value.EnumerateObject().Where(o => Verbs.Contains(o.Name)).Select(o => ($"{o.Name.ToUpperInvariant()} {p.Name}", o.Value)));

    private async Task<JsonElement> DocumentAsync()
    {
        var response = await factory.CreateClient().GetAsync(DocumentPath);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [GeneratedRegex(@"\{(\w+):[^}]+\}")]
    private static partial Regex Constraint();
}
