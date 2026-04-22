---
id: ADR-024
title: Server Framework — ASP.NET Core Minimal APIs (supersedes FastEndpoints)
status: ACCEPTED
date: 2026-04-21
supersedes: [ADR-003]
source: Adversarial Review R-04
decision_type: two-way-door
---

## Context

The original decision (ADR-003 / D4.5-adjacent) chose FastEndpoints 7.x as the HTTP framework, contingent on a Day 0 compatibility spike against .NET 10 + Aspire 13. Adversarial review (AR-10, AR-11) found:

1. The spike was marked "pending" but example code (§5.3.1 `CreateJournalEntryEndpoint`) already assumed FastEndpoints patterns.
2. Compatibility across .NET 10, Aspire 13, and FastEndpoints 7.x is unverified — all three are recent majors, and at least one pair commonly fails on .NET-major releases.
3. Solo-dev schedule (Day 35 dogfood) cannot absorb a failed spike followed by a framework swap.

## Decision

Build Faktuboh's backend on **ASP.NET Core Minimal APIs** + **FluentValidation** + **source-generated OpenAPI**. FastEndpoints is removed from scope; no spike required.

Problem-details handling uses **.NET 10 built-ins** (`AddProblemDetails()` + `IExceptionHandler`). Hellang.Middleware.ProblemDetails is explicitly ruled out.

### Stack Surface

- **Routing**: `MapGroup` per vertical slice, `MapPost/MapGet/...` per endpoint.
- **Validation**: FluentValidation invoked via an endpoint filter; validation failures surface as RFC 9457 problem-details with error code `faktuboh.<slice>.<condition>`.
- **OpenAPI**: .NET 10 source-generated OpenAPI; published as `openapi.json` for the ng-openapi-gen pipeline (TS contracts generation).
- **Results**: `TypedResults.Ok<T>()`, `TypedResults.Problem(...)` for strongly-typed responses.
- **Problem details**: custom `IExceptionHandler` implementations (one per domain-exception class) write RFC 9457 responses with the error-code convention baked into the `type` URI.

## Rationale

- **First-party on .NET 10**: Minimal APIs, `AddProblemDetails()`, and `IExceptionHandler` are shipped and tested by Microsoft alongside Aspire 13 — no compatibility gamble.
- **Vertical-slice layout survives the framework swap** — slices are folders of handlers, unchanged by this decision.
- **Strong typing without a layer**: `TypedResults` + source-gen OpenAPI match FastEndpoints's strong-typing benefit without a dependency.
- **Hellang ruled out**: the library predates `AddProblemDetails()` (shipped .NET 8) and `IExceptionHandler` (shipped .NET 8). On .NET 10 it is strictly redundant. DIY is ~30 LOC (one `IExceptionHandler` per domain-exception class + one `ProblemDetailsFactory` stamping the `faktuboh.<slice>.<condition>` error code onto the `type` URI).
- **Saves 2–3 days** of spike time and removes one .NET 10 compat unknown.

## Consequences

- Slice files contain explicit `MapGroup`/`MapPost` registration in a `<slice>.Endpoints.cs` file, not auto-discovered.
- Route registration is co-located with handler code per slice.
- One .NET exception hierarchy per slice (`{SliceName}Exception` base), caught by a slice-scoped `IExceptionHandler` that emits the RFC 9457 response with the correct error code.
- Central registration (`Program.cs`):
  ```csharp
  builder.Services.AddProblemDetails(options => /* stamp faktuboh.* error codes */);
  builder.Services.AddExceptionHandler<DomainExceptionHandler>();
  builder.Services.AddExceptionHandler<ValidationExceptionHandler>();
  ```
- Day 0 Gate 1 (FastEndpoints spike) is DISSOLVED.
- RT-40 (FastEndpoints spike inconclusive beyond 3 days) is DISSOLVED.

## Revisit Triggers

- **RT-API-1:** FastEndpoints publishes a version explicitly certified for .NET 10 + Aspire 13 **and** we encounter a concrete pain point that Minimal APIs does not address. Action: evaluate migration for a single slice; no global rewrite.
- **RT-API-2:** Error-code boilerplate exceeds ~200 LOC total across slices. Action: extract a small helper library; stay on Minimal APIs.

## Supersession Notes

- **ADR-003** is SUPERSEDED. Architecture.md §3.7 (FE spike) is dissolved; §5.3.1 example code (`CreateJournalEntryEndpoint`) is rewritten as a Minimal API handler + FluentValidator.
- The vertical-slice folder layout (ADR-006) is unchanged — it is framework-agnostic.

## Links

- PRD FR/NFR on API conventions — satisfied by Minimal APIs + source-gen OpenAPI.
- project_generated_ts_contracts.md (memory) — ng-openapi-gen pipeline consumes source-generated `openapi.json`; this ADR preserves that plan.
