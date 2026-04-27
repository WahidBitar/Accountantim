using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Faktuboh.Api.Tests;

/// <summary>
/// Integration tests for the /health stub endpoint declared in Program.cs (AC4).
/// </summary>
/// <remarks>
/// Note (Story 0.1 review P11/P12 — skipped): the unconditional <c>MapGet("/health")</c>
/// in Program.cs shadows ServiceDefaults' <c>MapHealthChecks(/health)</c> that
/// activates in Development, and returns a JSON-quoted string rather than text/plain.
/// These tests codify the current behavior; revisit during Story 0.11 (App Insights /
/// OTel) when the real production health-check posture is wired.
/// </remarks>
public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GET_health_returns_200_with_json_quoted_healthy_body_in_development()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory
            .WithWebHostBuilder(b => b.UseEnvironment("Development"))
            .CreateClient();

        var response = await client.GetAsync("/health", ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync(ct);
        body.Should().Be("\"healthy\"");

        response.Content.Headers.ContentType?.MediaType
            .Should().Be("application/json");
    }

    [Fact]
    public async Task GET_health_returns_200_in_production()
    {
        // The unconditional MapGet keeps /health reachable in Production
        // (real health-check pipeline is downstream work — see Story 0.11).
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory
            .WithWebHostBuilder(b => b.UseEnvironment("Production"))
            .CreateClient();

        var response = await client.GetAsync("/health", ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
