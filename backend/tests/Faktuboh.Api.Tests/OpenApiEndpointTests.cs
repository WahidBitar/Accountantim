using System.Net;
using System.Text.Json;
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

        // Structural assertion (replaces the prior substring match on `"/health"`):
        // parse the OpenAPI document and verify `paths` has a `/health` member.
        // Substring matching gives false positives on description/summary/tag text
        // that happens to mention `/health`.
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        doc.RootElement.TryGetProperty("paths", out var paths)
            .Should().BeTrue(because: "OpenAPI document must contain a `paths` object");

        paths.TryGetProperty("/health", out _)
            .Should().BeTrue(because: "spec AC4 requires /health to be a member of paths in the source-gen OpenAPI document");
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
