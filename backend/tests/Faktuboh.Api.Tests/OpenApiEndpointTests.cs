using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Faktuboh.Api.Tests;

/// <summary>
/// Integration tests for the source-gen OpenAPI endpoint (AC4) and its
/// Production environment gating (Story 0.1 review patch P10).
/// </summary>
public class OpenApiEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public OpenApiEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GET_openapi_v1_json_returns_200_in_development_with_health_path()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory
            .WithWebHostBuilder(b => b.UseEnvironment("Development"))
            .CreateClient();

        var response = await client.GetAsync("/openapi/v1.json", ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync(ct);
        body.Should().Contain("\"/health\"",
            because: "spec AC4 requires /health to appear in the source-gen OpenAPI document");
    }

    [Fact]
    public async Task GET_openapi_v1_json_returns_404_in_production()
    {
        // Patch P10: MapOpenApi() is gated behind IsDevelopment() to remove the
        // schema/endpoint disclosure surface from Production.
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory
            .WithWebHostBuilder(b => b.UseEnvironment("Production"))
            .CreateClient();

        var response = await client.GetAsync("/openapi/v1.json", ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
