# Story 0.4: RFC 9457 Problem Details middleware and ErrorCatalog

Status: ready-for-dev

## Story

As a **developer**,
I want **every API error response to follow RFC 9457 with domain error codes in the form `faktuboh.<slice>.<condition>`**,
so that **error semantics are a first-class wire contract that the Angular frontend can walk onto Signal Forms (D4.9)**.

**Covers requirements:** Epic 0 Platform Foundation — hard gate. D4.9's wire contract + §5.3.1.1 Problem Details plumbing + §5.2.6 error code taxonomy.

**Why this story now:** Per architecture §4.7 step 6, API-contract infra (error model + versioning + idempotency) lands together after hosting + CI/CD + CDN. Within Epic 0 this is the next middleware group after infra (0.10) + telemetry (0.11). Stories 0.5 (idempotency), 0.6 (bitemporal), 0.7 (ProcessingActivity), 0.8 (NetArchTest) all build on this story's `ErrorCatalog` + `DomainException` plumbing.

## Acceptance Criteria

### AC1 — `AddProblemDetails()` + CustomizeProblemDetails wired

**Given** `Faktuboh.Api/Program.cs` has `builder.Services.AddProblemDetails()` from Story 0.1
**When** the CustomizeProblemDetails callback is augmented
**Then** the callback stamps `type = https://faktuboh.com/errors/<code>` when `code` extension is present
**And** stamps `traceId = Activity.Current?.TraceId.ToString()` on every problem response
**And** stamps `instance = context.Request.Path` on every problem response
**And** the exact code block below is present in `backend/src/Faktuboh.Api/Program.cs`:

```csharp
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = ctx =>
    {
        ctx.ProblemDetails.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? ctx.HttpContext.TraceIdentifier;
        ctx.ProblemDetails.Instance ??= ctx.HttpContext.Request.Path;
        if (ctx.ProblemDetails.Extensions.TryGetValue("code", out var raw) && raw is string code)
            ctx.ProblemDetails.Type = $"https://faktuboh.com/errors/{code}";
    };
});
```

[Source: architecture.md §4 D4.9 + §5.3.1.1]

### AC2 — `IExceptionHandler` chain registered (DomainException + Validation + fallback)

**Given** .NET 10's `IExceptionHandler` replaces Hellang per ADR-024
**When** `Program.cs` registers exception handlers
**Then** three `IExceptionHandler` implementations are registered in order:
1. `ValidationExceptionHandler` — catches `FluentValidation.ValidationException`
2. `DomainExceptionHandler` — catches `DomainException` (base from Story 0.1)
3. `FallbackExceptionHandler` — catches remaining `Exception` (emits `faktuboh.common.internal_error` with a sanitized message; stack trace never exposed)
**And** `app.UseExceptionHandler()` is invoked once in the middleware pipeline
**And** each handler returns `true` after writing the RFC 9457 response so downstream handlers don't re-write [Source: architecture.md §5.3.1.1]

### AC3 — Problem response contains all required fields

**Given** a `DomainException` with `code = "faktuboh.common.example"` is thrown
**When** the response is inspected
**Then** `Content-Type: application/problem+json`
**And** the body contains:
- `type` — `"https://faktuboh.com/errors/faktuboh.common.example"`
- `title` — human-readable title from `ErrorCatalog` lookup
- `status` — HTTP status from `ErrorCatalog` lookup (e.g., 409)
- `detail` — the exception message (developer-safe — never contains PII)
- `instance` — request path
- `extensions.traceId` — 32-char lowercase hex W3C trace ID
- `extensions.code` — `"faktuboh.common.example"`
- `extensions.codeId` — GUID (immutable cross-version anchor per §5.2.6)
- `extensions.errors[]` — array of per-field violations (empty when not applicable); each entry shape `{ pointer: "/field/path", code: "faktuboh.common.required", message: "<localized>" }`
**And** no stack trace is leaked in any production response [Source: architecture.md §4 D4.9 + §5.2.6]

### AC4 — `ErrorCatalog` registry + NetArchTest enforcement

**Given** Story 0.1 scaffolded `Faktuboh.Application/Errors/ErrorCatalog.cs` as a stub
**When** this story populates it
**Then** `ErrorCatalog` exposes a `FrozenDictionary<string, ErrorCatalogEntry>` with:
- `ErrorCatalogEntry` record = `(string Code, Guid Id, int HttpStatus, string TranslocoKey, bool IsExternallyVisible, IReadOnlyList<string> Aliases)`
- Initial entries (added by this story):
  - `faktuboh.common.internal_error` (500) — `codeId` GUID fixed + never changes
  - `faktuboh.common.validation_failed` (400)
  - `faktuboh.common.not_found` (404)
  - `faktuboh.common.unauthorized` (401)
  - `faktuboh.common.forbidden` (403)
- Assembly-scan mechanism: slice projects contribute codes via `[ErrorCatalogEntry(...)]` attributes on static fields inside slice `*.Errors.cs` files; `ErrorCatalogRegistry` aggregates at startup (discovered via `typeof(ErrorCatalog).Assembly.GetTypes()` scanning)
**And** `Faktuboh.ArchitectureTests/ErrorCatalogTests.cs` asserts:
- Every `throw` of a subclass of `DomainException` references a `Code` that exists in `ErrorCatalogRegistry`
- Every code matches regex `^faktuboh\.[a-z][a-z0-9_]*\.[a-z][a-z0-9_]*$` (per §5.2.6)
- Every code's length is ≤ 64 chars
- Every `codeId` GUID is unique across the catalog [Source: architecture.md §5.2.6 + §4 D4.9]

### AC5 — FluentValidation endpoint filter + `errors[]` JSON Pointers

**Given** `FluentValidation` is wired per Story 0.1
**When** a request fails validation
**Then** `ValidationExceptionHandler` maps the `ValidationException.Errors` collection into RFC 9457 `errors[]` format
**And** each error has `pointer` as a JSON Pointer targeting the offending property (e.g., `/openingBalance/amount`)
**And** JSON Pointer segments use camelCase (matches §5.2.3 wire convention — `/userId` not `/user_id`)
**And** the generic `ValidationFilter<T>` endpoint filter pattern lives at `backend/src/Faktuboh.Api/Infrastructure/Validation/ValidationFilter.cs`:

```csharp
public sealed class ValidationFilter<T> : IEndpointFilter where T : notnull
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var request = ctx.Arguments.OfType<T>().FirstOrDefault();
        if (request is null) return await next(ctx);
        var validator = ctx.HttpContext.RequestServices.GetService<IValidator<T>>();
        if (validator is null) return await next(ctx);
        var result = await validator.ValidateAsync(request, ctx.HttpContext.RequestAborted);
        if (result.IsValid) return await next(ctx);
        throw new ValidationException(result.Errors);
    }
}
```

**And** the filter is applied via `.AddEndpointFilter<ValidationFilter<TRequest>>()` on each endpoint (post-Story 2.1) [Source: architecture.md §5.3.1 + §4 D4.14]

### AC6 — Traceability test

**Given** an endpoint throws `DomainException(code: "faktuboh.contacts.duplicate_name_for_owner", status: 409)`
**When** an integration test issues a request that triggers this code path
**Then** the response status is 409
**And** the response `Content-Type` is `application/problem+json`
**And** the response body deserializes to a JSON document matching the AC3 shape
**And** the response `type` URI is `https://faktuboh.com/errors/faktuboh.contacts.duplicate_name_for_owner`
**And** the test snapshot is captured via Verify (`tests/Faktuboh.Api.Tests/ErrorHandling/DomainExceptionHandlerSnapshots.cs`) for regression detection

### AC7 — NSwag contract emission includes error shape

**Given** NSwag (Story 0.1) emits `openapi.json`
**When** the generated spec is inspected
**Then** every error response references a `components.schemas.ProblemDetails` schema matching the RFC 9457 shape
**And** the `type` URI is documented in the OpenAPI spec as an enum or free-text with examples
**And** `ErrorCatalog` entries are emitted into the OpenAPI spec as examples per endpoint (via a `Faktuboh.Api/OpenApi/ErrorCatalogComponentGenerator.cs` helper that hooks into `AddOpenApi` via an `OpenApiDocumentTransformer`) [Source: architecture.md §6.3.1 OpenApi folder]

### AC8 — Build fails if a `throw` references an unregistered code

**Given** a dev writes `throw new ContactException("faktuboh.contacts.fake_code")` without adding `fake_code` to `ErrorCatalog`
**When** `dotnet test backend/tests/Faktuboh.ArchitectureTests` runs
**Then** `ErrorCatalogTests.Every_thrown_domain_exception_references_registered_code` fails with a clear message listing the offending file + code
**And** CI (Story 0.9's `ci.yml`) blocks the PR [Source: architecture.md §4 D4.9 NetArchTest assertion]

## Tasks / Subtasks

### T1 — Populate `ErrorCatalog` + `DomainException` chain (AC: 3, 4)

- [ ] Edit `backend/src/Faktuboh.Domain/Errors/DomainException.cs` (from Story 0.1 stub):
  ```csharp
  public abstract class DomainException(string code, string message, IReadOnlyList<DomainFieldError>? fieldErrors = null) : Exception(message)
  {
      public string Code { get; } = code;
      public IReadOnlyList<DomainFieldError> FieldErrors { get; } = fieldErrors ?? Array.Empty<DomainFieldError>();
  }
  public sealed record DomainFieldError(string Pointer, string Code, string Message);
  ```
- [ ] Create `backend/src/Faktuboh.Application/Errors/ErrorCatalogEntry.cs`:
  ```csharp
  public sealed record ErrorCatalogEntry(string Code, Guid Id, int HttpStatus, string TranslocoKey, bool IsExternallyVisible, IReadOnlyList<string> Aliases);
  ```
- [ ] Create `backend/src/Faktuboh.Application/Errors/ErrorCatalog.cs` with the five common entries listed in AC4
- [ ] Create `backend/src/Faktuboh.Application/Errors/ErrorCatalogAttribute.cs` — `[AttributeUsage(AttributeTargets.Field)] public sealed class ErrorCatalogEntryAttribute : Attribute { ... }` for slice-contributed entries
- [ ] Create `backend/src/Faktuboh.Application/Errors/ErrorCatalogRegistry.cs` that scans all loaded assemblies at startup for `[ErrorCatalogEntryAttribute]` + merges into a single `FrozenDictionary<string, ErrorCatalogEntry>`

### T2 — Register exception handlers in Program.cs (AC: 1, 2)

- [ ] Edit `backend/src/Faktuboh.Api/Program.cs`:
  - Add `builder.Services.AddExceptionHandler<ValidationExceptionHandler>();`
  - Add `builder.Services.AddExceptionHandler<DomainExceptionHandler>();`
  - Add `builder.Services.AddExceptionHandler<FallbackExceptionHandler>();`
  - Add `builder.Services.AddSingleton<ErrorCatalogRegistry>();` — populated at startup
  - Enhance `CustomizeProblemDetails` callback per AC1
  - Add `app.UseExceptionHandler();` to middleware pipeline (before `UseAuthentication`)
- [ ] Create `backend/src/Faktuboh.Api/Infrastructure/ErrorHandling/DomainExceptionHandler.cs` — maps `DomainException` → RFC 9457 using `ErrorCatalogRegistry` lookup
- [ ] Create `backend/src/Faktuboh.Api/Infrastructure/ErrorHandling/ValidationExceptionHandler.cs` — maps FluentValidation failures to `errors[]` with JSON Pointers
- [ ] Create `backend/src/Faktuboh.Api/Infrastructure/ErrorHandling/FallbackExceptionHandler.cs` — sanitized 500 response

### T3 — ValidationFilter + JSON Pointer emission (AC: 5)

- [ ] Create `backend/src/Faktuboh.Api/Infrastructure/Validation/ValidationFilter.cs` per AC5 snippet
- [ ] Create `backend/src/Faktuboh.Api/Infrastructure/Validation/JsonPointerFormatter.cs` — converts FluentValidation property path (e.g., `OpeningBalance.Amount`) to JSON Pointer (`/openingBalance/amount`) respecting camelCase cascade
- [ ] Unit test `ValidationExceptionHandlerTests.cs` asserts conversion correctness + `errors[]` array shape

### T4 — OpenAPI Problem Details integration (AC: 7)

- [ ] Create `backend/src/Faktuboh.Api/OpenApi/ErrorCatalogComponentGenerator.cs` implementing `IOpenApiDocumentTransformer` — adds `ProblemDetails` schema + error examples to the spec
- [ ] Register the transformer via `builder.Services.AddOpenApi(o => o.AddDocumentTransformer<ErrorCatalogComponentGenerator>());`
- [ ] Run NSwag against the updated `openapi.json` and confirm generated TS `ProblemDetails.ts` matches RFC 9457 shape

### T5 — NetArchTest enforcement (AC: 4, 8)

- [ ] Create `backend/tests/Faktuboh.ArchitectureTests/ErrorCatalogTests.cs`:
  - Test 1: every `throw new ...DomainException...(code: ...)` call-site references a code registered in `ErrorCatalogRegistry` (via Roslyn or NetArchTest method-scan)
  - Test 2: every code matches regex `^faktuboh\.[a-z][a-z0-9_]*\.[a-z][a-z0-9_]*$`
  - Test 3: every code length ≤ 64 chars
  - Test 4: every `codeId` GUID is unique
- [ ] Wire `ErrorCatalogTests` as a required CI check in Story 0.9's `ci.yml`

### T6 — Integration snapshot test (AC: 6)

- [ ] Create `backend/tests/Faktuboh.Api.Tests/ErrorHandling/DomainExceptionHandlerTests.cs`:
  - Uses `WebApplicationFactory<Program>`
  - Maps a test endpoint `/_test/throw-domain-exception` that throws `TestDomainException("faktuboh.common.example")`
  - Asserts 409 + `application/problem+json` + full RFC 9457 shape
  - Uses Verify for JSON snapshot capture
- [ ] Create `backend/tests/Faktuboh.Api.Tests/ErrorHandling/ValidationExceptionHandlerTests.cs`:
  - Posts an invalid payload to a test endpoint
  - Asserts 400 + `errors[]` with JSON Pointers

## Dev Notes

### Critical guardrails (do not violate)

1. **No Hellang.Middleware.ProblemDetails.** .NET 10 built-ins (`AddProblemDetails()` + `IExceptionHandler`) are the only permitted path per ADR-024. [Source: adrs/adr-024]
2. **No stack traces in production responses.** `FallbackExceptionHandler` writes a sanitized message + `faktuboh.common.internal_error` code; full exception captured in App Insights via OTel. [Source: architecture.md §4 D4.20]
3. **JSON Pointer casing MUST match camelCase wire.** `/userId` not `/UserId` not `/user_id`. [Source: architecture.md §5.2.3]
4. **`codeId` GUIDs are immutable.** Once a code's GUID is assigned, it cannot change — external clients can pattern-match on GUID as the stable anchor while code string is re-litigated. [Source: architecture.md §5.2.6]
5. **Slice error codes live in slice folders.** A Contacts error code lives in `Slices/Contacts/_Shared/ContactsErrors.cs`, not in central `ErrorCatalog.cs`. The registry aggregates at startup via attribute scan. Adding a common code (cross-slice) to `Application/Errors/ErrorCatalog.cs` requires ADR justification.
6. **Error codes are public API post-external-client.** Renaming requires `aliases` deprecation period (minimum 6 months; extends one major version). [Source: architecture.md §5.2.6]

### Source tree — files to create or edit

```
backend/src/
├── Faktuboh.Domain/Errors/
│   └── DomainException.cs                          # EDIT (add FieldErrors)
├── Faktuboh.Application/Errors/
│   ├── ErrorCatalog.cs                             # EDIT (populate 5 common entries)
│   ├── ErrorCatalogEntry.cs                        # NEW
│   ├── ErrorCatalogAttribute.cs                    # NEW
│   └── ErrorCatalogRegistry.cs                     # NEW
└── Faktuboh.Api/
    ├── Program.cs                                  # EDIT (wire handlers + customize)
    ├── Infrastructure/ErrorHandling/
    │   ├── DomainExceptionHandler.cs               # NEW
    │   ├── ValidationExceptionHandler.cs           # NEW
    │   └── FallbackExceptionHandler.cs             # NEW
    ├── Infrastructure/Validation/
    │   ├── ValidationFilter.cs                     # NEW
    │   └── JsonPointerFormatter.cs                 # NEW
    └── OpenApi/
        └── ErrorCatalogComponentGenerator.cs       # NEW

backend/tests/
├── Faktuboh.Api.Tests/ErrorHandling/
│   ├── DomainExceptionHandlerTests.cs              # NEW (Verify snapshots)
│   └── ValidationExceptionHandlerTests.cs          # NEW
└── Faktuboh.ArchitectureTests/
    └── ErrorCatalogTests.cs                        # NEW (4 fitness tests)
```

### Testing standards

| Level | Tool | What this story covers |
|---|---|---|
| Unit | xUnit v3 + FluentAssertions | `JsonPointerFormatter` correctness |
| Architectural | NetArchTest + Roslyn | AC4 + AC8 every-throw-references-code |
| Integration | `WebApplicationFactory<Program>` + Verify | AC3 + AC6 full RFC 9457 response |
| Snapshot | Verify | Per-error-code snapshot to catch wire changes |

### Project Structure Notes

- `Faktuboh.Api/Infrastructure/` is the cross-slice plumbing folder per architecture.md §6.3.1 — NOT domain logic. This story populates `ErrorHandling/` + `Validation/` under that root.
- `ErrorCatalogRegistry` startup scan adds <10ms boot cost at MVP scale; if it grows past 200 codes (RT per §5.5 RT-23) revisit the mechanism.

### Architecture compliance

- [architecture.md §4 D4.9](../planning-artifacts/architecture.md) — RFC 9457 error model
- [architecture.md §5.2.6](../planning-artifacts/architecture.md) — error code taxonomy
- [architecture.md §5.3.1.1](../planning-artifacts/architecture.md) — Problem Details plumbing
- [adrs/adr-024-minimal-apis-framework.md](../planning-artifacts/adrs/adr-024-minimal-apis-framework.md) — .NET 10 built-ins over Hellang
- [architecture.md §4 D4.14](../planning-artifacts/architecture.md) — FluentValidation + applyServerErrors FE counterpart (Epic 1/2 consumers)

### Library/framework requirements

| Layer | Package | Version | Purpose |
|---|---|---|---|
| ASP.NET Core | Microsoft.AspNetCore.Http.Extensions | 10.0.x | `AddProblemDetails` + `IExceptionHandler` |
| Validation | FluentValidation + FluentValidation.DependencyInjectionExtensions | latest | AbstractValidator + DI |
| OpenAPI | Microsoft.AspNetCore.OpenApi | 10.0.x | `IOpenApiDocumentTransformer` |
| Tests | Verify.Xunit | latest | JSON snapshots |

### Testing requirements

- **AC6 snapshot test** must run green before the story is marked done. If snapshot diff appears in a later PR, that PR must explicitly update the snapshot file + include ADR-worthy rationale in commit message.
- **AC8 fitness test** runs on every PR per Story 0.9's `ci.yml`.
- **Roslyn-based `throw`-scan** can be replaced with a simpler NetArchTest + hand-maintained allowlist if Roslyn integration proves flaky — document the chosen mechanism in `tests/Faktuboh.ArchitectureTests/README.md`.

### Previous Story Intelligence

- **Story 0.1 (backend scaffold)** complete: `DomainException` stub + `ErrorCatalog` stub exist; this story fills them in.
- **Story 0.2 (FE workspace)** complete: Angular HTTP interceptor `correlation-id.interceptor.ts` will consume `traceId` from this story's Problem Details responses (wired in Story 0.11 + Epic 1/2).
- **Story 0.3 (Auth0)** complete: `AddJwtBearer()` is registered in `Program.cs`; `UnauthorizedAccessException` should route through a dedicated handler that emits `faktuboh.common.unauthorized` (already in ErrorCatalog per AC4).
- **Story 0.9 (CI/CD)** complete: `ci.yml` invokes `dotnet test` covering the new test projects + fails on unregistered codes.
- **Story 0.10 (Azure infra)** complete: Problem Details response includes `traceId` populated via `Activity.Current?.TraceId` from OTel auto-instrumentation (Story 0.11).
- **Story 0.11 (App Insights)** complete: `traceId` in Problem Details cross-references App Insights traces.

## References

- [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) — Story 0.4 definition
- [_bmad-output/planning-artifacts/architecture.md §4 D4.9](../planning-artifacts/architecture.md) — error model
- [_bmad-output/planning-artifacts/architecture.md §5.2.6](../planning-artifacts/architecture.md) — error code taxonomy
- [_bmad-output/planning-artifacts/architecture.md §5.3.1.1](../planning-artifacts/architecture.md) — Problem Details plumbing
- [_bmad-output/planning-artifacts/adrs/adr-024-minimal-apis-framework.md](../planning-artifacts/adrs/adr-024-minimal-apis-framework.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

_Populated during implementation._

### File List

_Populated during implementation — expected new/modified files per the Source Tree section above._
