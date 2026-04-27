# Test Automation Summary — Story 0.1 Backend Solution Scaffold

_Generated: 2026-04-27 (post code-review patches)_

## Context

Story 0.1 scaffolded the .NET 10 / Aspire 13 backend solution. The implementer added unit + architecture tests as part of AC8 + AC9. The post-review code-review patches added 3 domain unit tests (negative-amount + whitespace-currency rejections). This QA pass adds **API integration tests** for the only HTTP surface that exists today: `/health` and `/openapi/v1.json`.

## Generated Tests

### API Integration Tests (new)

- [x] `backend/tests/Faktuboh.Api.Tests/HealthEndpointTests.cs` — `/health` endpoint coverage (2 tests)
  - `GET_health_returns_200_with_json_quoted_healthy_body_in_development` — codifies the current AC4 contract: `TypedResults.Ok("healthy")` returns JSON-quoted `"healthy"` with `application/json` Content-Type. Documents the wart from skipped review patch P12.
  - `GET_health_returns_200_in_production` — verifies the unconditional `MapGet("/health")` keeps the endpoint reachable when ServiceDefaults' `MapDefaultEndpoints()` is dormant (not Development). Documents the route-shadowing situation from skipped review patch P11.
- [x] `backend/tests/Faktuboh.Api.Tests/OpenApiEndpointTests.cs` — `/openapi/v1.json` endpoint coverage (2 tests)
  - `GET_openapi_v1_json_returns_200_in_development_with_health_path` — verifies AC4 (source-gen OpenAPI document is reachable in Development and lists `/health`).
  - `GET_openapi_v1_json_returns_404_in_production` — actively verifies review patch P10 (production gating prevents schema/endpoint disclosure).

### Pre-existing Tests (not regenerated)

These were created by the implementer (Story 0.1) and refined by the code review:

- `backend/tests/Faktuboh.Domain.Tests/Primitives/MoneyTests.cs` — 8 tests (4 valid construction + 1 unsupported currency + 2 null/whitespace + 1 negative + 3 precision + 1 FsCheck property)
- `backend/tests/Faktuboh.Domain.Tests/Primitives/CurrencyRegistryTests.cs` — 14 tests (1 cardinality + 10 minor-units + 3 unsupported)
- `backend/tests/Faktuboh.ArchitectureTests/SharedKernelPurityTests.cs` — 1 NetArchTest fitness test + sentinel
- `backend/tests/Faktuboh.ArchitectureTests/SharedKernelNoAggregateRootsTests.cs` — 1 reflection test + sentinel
- `backend/tests/Faktuboh.{Api,Application,Infrastructure}.Tests/SmokeTests.cs` — 1 assembly-load assertion each

### E2E (UI) Tests

**Skipped — no UI exists yet.** The Angular two-target workspace lands in Story 0.2 (`0-2-angular-two-target-workspace`); browser/E2E tests will land alongside it (Cypress/Playwright TBD by that story).

## Coverage

| Surface | Total | Covered | Notes |
|---|---|---|---|
| HTTP endpoints | 2 functional + 1 dev-only | 2 + 1 (gated) | `/health` (Dev + Prod), `/openapi/v1.json` (Dev pass + Prod 404 gate) |
| Domain types | 6 (Money, CurrencyRegistry, IBitemporal, Direction, DomainException, ErrorCatalog) | 2 (Money, CurrencyRegistry) | IBitemporal/Direction/DomainException/ErrorCatalog have no behavior to unit-test yet |
| Architectural invariants | 2 (SK purity, no IAggregateRoot in SK) | 2 | Both with sentinel positives |

**Total tests:** 35 passing / 0 failing (was 28 pre-review, 31 post-review-patches, +4 new API integration tests).

```
Passed!  - Failed: 0, Passed: 26, Total: 26 - Faktuboh.Domain.Tests.dll
Passed!  - Failed: 0, Passed:  1, Total:  1 - Faktuboh.Application.Tests.dll
Passed!  - Failed: 0, Passed:  1, Total:  1 - Faktuboh.Infrastructure.Tests.dll
Passed!  - Failed: 0, Passed:  2, Total:  2 - Faktuboh.ArchitectureTests.dll
Passed!  - Failed: 0, Passed:  5, Total:  5 - Faktuboh.Api.Tests.dll
```

## Test Framework

- **Runner:** xUnit v3 (3.2.2) with `xunit.v3` + `xunit.runner.visualstudio` 3.1.1
- **Assertions:** AwesomeAssertions 9.4.0 (MIT fork of FluentAssertions 7.x; replaced 8.x to drop Xceed commercial-license exposure per code-review decision D5/5b)
- **Property-based:** FsCheck.Xunit.v3 3.3.3 (Domain.Tests only)
- **API integration:** `Microsoft.AspNetCore.Mvc.Testing` 10.0.7 via `WebApplicationFactory<Program>` against `public partial class Program;`
- **DB integration (future):** Testcontainers.PostgreSql 4.11.0 — referenced but not yet exercised (first slice in Story 2.1 will activate)
- **Architecture:** NetArchTest.Rules 1.3.2

## Notable Decisions During Generation

1. **xUnit v3 `xUnit1051` analyzer** required threading `TestContext.Current.CancellationToken` to all `HttpClient` calls. Under `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` this is a build error, not a warning — the tests were updated accordingly. This pattern should be standard for all future API integration tests.
2. **`POST /health` test removed.** A speculative `POST /health → 405 Method Not Allowed` test surfaced a real route-shadowing ambiguity: ServiceDefaults' `MapHealthChecks(/health)` (active in Development) accepts POST and returns 200, while `MapGet("/health", ...)` is GET-only. The dual-route contract is exactly what skipped review patches **P11/P12** documented; codifying a 405 expectation today would lock in a non-spec assertion that breaks once the production health-check posture is finalized in Story 0.11. Removed.
3. **Production environment override** uses `WithWebHostBuilder(b => b.UseEnvironment("Production"))` — this is the canonical pattern for testing environment-conditional code paths and should be reused for any future ASPNETCORE_ENVIRONMENT-gated behavior (rate limiting, health-check gating, OpenAPI gating).

## Next Steps

1. **Story 0.2 (Angular workspace)** — adds the FE; spin up Playwright/Cypress E2E and component tests there.
2. **Story 0.4 (RFC 9457 Problem Details middleware)** — first non-trivial API behavior; add tests asserting `application/problem+json` response shape on validation failures.
3. **Story 0.6 (`IBitemporal` interceptor + MigrationService)** — first slice that exercises the EF Core mutability rationale documented in `IBitemporal`'s XML doc; add an integration test asserting `RecordedAt` is auto-set by the interceptor on save.
4. **Story 0.11 (App Insights / OTel)** — finalize the production health-check posture; resolve skipped patches P11/P12 jointly; replace the current `HealthEndpointTests` codifications with stricter contracts.
5. **Story 2.1 (Contacts/Create)** — first real CRUD slice; activate `Testcontainers.PostgreSql` for end-to-end DB integration tests.

## Validation Against Checklist

- [x] API tests generated (4 new — health × 2, openapi × 2)
- [x] E2E tests generated — N/A (no UI; deferred to Story 0.2)
- [x] Tests use standard test framework APIs (xUnit v3 + AwesomeAssertions + WebApplicationFactory)
- [x] Tests cover happy path (`/health` 200 in Dev, `/openapi/v1.json` 200 in Dev with `/health` path)
- [x] Tests cover critical error/edge cases (`/openapi/v1.json` 404 in Prod, `/health` 200 in Prod environment switch)
- [x] All generated tests run successfully (35/35 passing at Release)
- [x] Tests use proper locators — N/A (HTTP path-based, not UI selectors)
- [x] Tests have clear descriptions — full method names describe the contract
- [x] No hardcoded waits or sleeps
- [x] Tests are independent — each constructs its own client with its own environment override; no shared mutable state
- [x] Test summary created (this file)
- [x] Tests saved to appropriate directories (`backend/tests/Faktuboh.Api.Tests/`)
- [x] Summary includes coverage metrics
