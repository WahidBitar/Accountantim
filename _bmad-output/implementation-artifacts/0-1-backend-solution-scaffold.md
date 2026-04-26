# Story 0.1: Backend solution scaffold

Status: ready-for-dev

## Story

As a **developer**,
I want a **greenfield .NET 10 solution rooted in Aspire 13 with the canonical project graph (Api + Application + Domain + Infrastructure + Contracts + Tests)**,
so that **every feature slice has a stable home and the Domain layer starts with zero dependencies**.

**Covers requirements:** Epic 0 Platform Foundation — hard gate. Establishes the `.sln` graph that §3.3 + ADR-006 + ADR-024 + ADR-022 + ADR-004 all depend on. Also lands Shared Kernel primitives (Money VO, CurrencyRegistry, IBitemporal marker, Direction enum, DomainException base, ErrorCatalog stub) that the §7.5.2 first slice consumes.

**Why this story first:** Every other Epic 0 story (0.2 FE scaffold, 0.4 RFC 9457 middleware, 0.5 idempotency middleware, 0.6 bitemporal interceptor, 0.7 ProcessingActivity, 0.8 NetArchTest, 0.11 OTel wiring, 0.14 telemetry primitive, 0.15 compound-journal seam) layers into the project graph this story creates. Writing the scaffold incorrectly closes doors that ADR-002 (greenfield), ADR-006 (vertical-slice), and ADR-004 (compliance day-one) all depend on.

## Acceptance Criteria

### AC1 — Solution and project graph exist

**Given** an empty repository at `c:\Work\Accountantim\` with only `_bmad-output/` + `.claude/` + `.git/`
**When** the scaffold command sequence runs
**Then** `backend/Faktuboh.sln` exists with projects `Faktuboh.Api`, `Faktuboh.Application`, `Faktuboh.Domain`, `Faktuboh.Infrastructure`, `Faktuboh.Contracts`, plus test projects `Faktuboh.Domain.Tests`, `Faktuboh.Application.Tests`, `Faktuboh.Api.Tests`, `Faktuboh.Infrastructure.Tests`, `Faktuboh.ArchitectureTests`
**And** `backend/aspire/Faktuboh.AppHost/` + `backend/aspire/Faktuboh.ServiceDefaults/` exist and are referenced by `Faktuboh.Api`
**And** `dotnet restore backend/Faktuboh.sln` succeeds
**And** `dotnet build backend/Faktuboh.sln --configuration Release` succeeds with zero warnings

### AC2 — Domain layer purity (NetArchTest-enforced)

**Given** the `Faktuboh.Domain` project
**When** `dotnet list backend/src/Faktuboh.Domain/Faktuboh.Domain.csproj package` runs
**Then** the project has zero PackageReference entries (excluding framework implicit references)
**And** the project has zero ProjectReference entries
**And** a NetArchTest fitness test in `Faktuboh.ArchitectureTests/SharedKernelPurityTests.cs` asserts `Faktuboh.Domain` depends on nothing outside the BCL + `System.*` namespaces (per §7.5.3 test #1)
**And** the fitness test fails the build if a future PR adds any non-BCL dependency to Domain

### AC3 — Directory.Build.props enforces strict compile

**Given** `backend/Directory.Build.props` is present at the backend root
**When** any .NET project under `backend/src/` or `backend/tests/` is built
**Then** `<Nullable>enable</Nullable>` is active on every csproj via inheritance
**And** `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` is active
**And** `<LangVersion>14</LangVersion>` is pinned (C# 14 bundled with .NET 10 per §3.2)
**And** `<ImplicitUsings>enable</ImplicitUsings>` is active
**And** a `global.json` at `backend/global.json` pins `"sdk": { "version": "10.0.100", "rollForward": "latestFeature" }` [Source: architecture.md §6.2.2]

### AC4 — Minimal APIs + FluentValidation + source-gen OpenAPI wired

**Given** `Faktuboh.Api` is the HTTP host per ADR-024
**When** the project is built
**Then** `Faktuboh.Api.csproj` references `Microsoft.AspNetCore.OpenApi` (source-gen OpenAPI) and `FluentValidation.AspNetCore`
**And** `Program.cs` calls `builder.Services.AddOpenApi()` + `app.MapOpenApi()` (per .NET 10 source-gen pattern)
**And** `Program.cs` calls `builder.Services.AddProblemDetails()` (per ADR-024 + §5.3.1.1 — not Hellang)
**And** a stub `GET /health` Minimal API endpoint returns `TypedResults.Ok("healthy")` and is reachable via `dotnet run --project backend/src/Faktuboh.Api`
**And** `GET /openapi/v1.json` returns a JSON document containing the `/health` path [Source: adrs/adr-024-minimal-apis-framework.md]

### AC5 — Aspire AppHost orchestrates local dev

**Given** `backend/aspire/Faktuboh.AppHost/Program.cs`
**When** `dotnet run --project backend/aspire/Faktuboh.AppHost` executes
**Then** the AppHost boots a Postgres resource (`builder.AddPostgres("postgres")` with a database `builder.AddDatabase("faktuboh")`)
**And** the AppHost starts `Faktuboh.Api` with `builder.AddProject<Projects.Faktuboh_Api>("api").WithReference(postgres)`
**And** the Aspire dashboard URL prints to stdout [Source: architecture.md §6.5.3]
**And** `Faktuboh.ServiceDefaults` exposes `AddServiceDefaults()` extension that `Faktuboh.Api` calls in `Program.cs` [Source: architecture.md §6.3.1]

### AC6 — Shared Kernel primitives (Money, CurrencyRegistry, IBitemporal, Direction, DomainException, ErrorCatalog stub)

**Given** `Faktuboh.Domain` is the Shared Kernel per §7.5.3 test #4 ("SK holds no IAggregateRoot types")
**When** the Shared Kernel is scaffolded
**Then** `Faktuboh.Domain/Primitives/Money.cs` defines `public sealed record Money(decimal Amount, string Currency)` with constructor validation that the currency is in `CurrencyRegistry`
**And** `Faktuboh.Domain/Primitives/CurrencyRegistry.cs` contains the initial fiat set `EUR(2), USD(2), GBP(2), AED(2), SAR(2), EGP(2), JOD(3), KWD(3), BHD(3), TND(3)` as `IReadOnlyDictionary<string, int>` (ISO 4217 minor-unit counts per §5.2.5)
**And** `Faktuboh.Domain/Primitives/IBitemporal.cs` defines the marker interface per ADR-022 (`DateTimeOffset ValidFrom { get; set; }`, `DateTimeOffset? ValidTo { get; set; }`, `DateTimeOffset RecordedAt { get; set; }`)
**And** `Faktuboh.Domain/Primitives/Direction.cs` defines `public enum Direction { Receivable, Payable }` (delegated decision — consumed by Epic 2 Story 2.1)
**And** `Faktuboh.Domain/Errors/DomainException.cs` defines `public abstract class DomainException : Exception` with `string Code { get; }` constructor argument
**And** `Faktuboh.Application/Errors/ErrorCatalog.cs` defines `public static class ErrorCatalog` with a `FrozenDictionary<string, ErrorCatalogEntry>` populated by slices (initial set empty; slices register via partial class or init-time assembly scan per §6.3.1)

### AC7 — TypeScript contracts generator chosen = NSwag

**Given** ADR-005 deferred the TS contracts generator choice
**When** Story 0.1 scaffolds the Contracts project
**Then** `Faktuboh.Contracts/Faktuboh.Contracts.csproj` is a `<TargetFramework>net10.0</TargetFramework>` classlib containing request/response DTOs only (no business logic)
**And** the solution-level decision is recorded: **NSwag (first-party NSwag for .NET with source-gen OpenAPI) is the chosen TS contracts generator** — this replaces the `ng-openapi-gen` reference in architecture.md §6.5.1 (which predates the decision)
**And** `backend/tools/NSwag/nswag.json` is created with initial configuration pointing at the `openapi.json` source-gen output + target at `frontend/libs/api-contracts/src/generated/` [Source: architecture.md §6.5.1, §3.5 + delegated decision 2026-04-22]
**And** the handwritten-shared-contracts ESLint rule `no-handwritten-shared-contracts` is listed as a TODO in `frontend/eslint.config.js` (wired in Story 0.2)

### AC8 — Test projects reference source projects + baseline tooling

**Given** the five test projects
**When** each is inspected
**Then** `Faktuboh.Domain.Tests` references `Faktuboh.Domain` + xUnit v3 + FluentAssertions + FsCheck
**And** `Faktuboh.Application.Tests` references `Faktuboh.Application` + xUnit v3 + FluentAssertions + NSubstitute
**And** `Faktuboh.Api.Tests` references `Faktuboh.Api` + `Microsoft.AspNetCore.Mvc.Testing` (for `WebApplicationFactory<Program>`) + xUnit v3 + FluentAssertions + Verify + Testcontainers.PostgreSql
**And** `Faktuboh.Infrastructure.Tests` references `Faktuboh.Infrastructure` + Testcontainers.PostgreSql
**And** `Faktuboh.ArchitectureTests` references `NetArchTest.Rules` + all four source projects (Api/Application/Domain/Infrastructure) + xUnit v3
**And** Moq is absent from every test project (forbidden per §5.3.3)
**And** `Faktuboh.Api/Program.cs` declares `public partial class Program { }` at the bottom of the file so `WebApplicationFactory<Program>` can resolve it [Source: architecture.md §5.3.3]

### AC9 — Smoke test passes end-to-end

**Given** all scaffold is in place
**When** `dotnet test backend/Faktuboh.sln --configuration Release` runs
**Then** all five test projects discover and pass (initial content: one passing placeholder test per project to prove discovery)
**And** `Faktuboh.ArchitectureTests/SharedKernelPurityTests.cs` passes (AC2 fitness test)
**And** `Faktuboh.Domain.Tests/Primitives/CurrencyRegistryTests.cs` asserts the initial-set count is 10 and each entry has the correct minor-unit count
**And** CI job `ci.yml` (added in Story 0.9) will invoke this exact command as the backend gate

## Tasks / Subtasks

### T1 — Run .NET init command sequence (AC: 1, 5)

- [ ] Create `backend/` directory
- [ ] Run `dotnet new sln -n Faktuboh --output backend/`
- [ ] Run `dotnet new aspire-apphost -o backend/aspire/Faktuboh.AppHost`
- [ ] Run `dotnet new aspire-servicedefaults -o backend/aspire/Faktuboh.ServiceDefaults`
- [ ] Run `dotnet new classlib -o backend/src/Faktuboh.Domain` (no package deps)
- [ ] Run `dotnet new classlib -o backend/src/Faktuboh.Application`
- [ ] Run `dotnet new classlib -o backend/src/Faktuboh.Infrastructure`
- [ ] Run `dotnet new classlib -o backend/src/Faktuboh.Contracts`
- [ ] Run `dotnet new web -o backend/src/Faktuboh.Api`
- [ ] Run `dotnet new xunit -o backend/tests/Faktuboh.Domain.Tests` (xUnit v3 template)
- [ ] Repeat for Application.Tests, Api.Tests, Infrastructure.Tests, ArchitectureTests
- [ ] `dotnet sln backend/Faktuboh.sln add` for all 11 projects

### T2 — Wire project references per §6.3.4 (AC: 1)

- [ ] `Faktuboh.Api` → ProjectReference `Faktuboh.Application`, `Faktuboh.Infrastructure`, `Faktuboh.Contracts`, `Faktuboh.ServiceDefaults`
- [ ] `Faktuboh.Application` → ProjectReference `Faktuboh.Domain`, `Faktuboh.Contracts`
- [ ] `Faktuboh.Infrastructure` → ProjectReference `Faktuboh.Application`, `Faktuboh.Domain`, `Faktuboh.ServiceDefaults`
- [ ] `Faktuboh.Contracts` → ProjectReference `Faktuboh.Domain`
- [ ] `Faktuboh.Domain` → NO project references (pure Shared Kernel)
- [ ] `Faktuboh.AppHost` → ProjectReference `Faktuboh.Api` via `<IsAspireProjectResource>true</IsAspireProjectResource>`

### T3 — Create Directory.Build.props + global.json (AC: 3)

- [ ] Write `backend/Directory.Build.props` with `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<LangVersion>14</LangVersion>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<AnalysisLevel>latest</AnalysisLevel>`
- [ ] Write `backend/global.json` pinning .NET 10 SDK
- [ ] Verify every csproj inherits (no per-project override of these properties allowed)

### T4 — Scaffold Minimal APIs + OpenAPI wiring (AC: 4)

- [ ] Add NuGet packages to `Faktuboh.Api.csproj`: `Microsoft.AspNetCore.OpenApi` (net10.0), `FluentValidation.AspNetCore` (latest)
- [ ] Replace `Program.cs` with:
  - `var builder = WebApplication.CreateBuilder(args);`
  - `builder.AddServiceDefaults();`
  - `builder.Services.AddOpenApi();`
  - `builder.Services.AddProblemDetails();`
  - `var app = builder.Build();`
  - `app.MapDefaultEndpoints();`
  - `app.MapOpenApi();`
  - `app.MapGet("/health", () => TypedResults.Ok("healthy"));`
  - `app.Run();`
  - `public partial class Program;`
- [ ] Verify `GET /openapi/v1.json` returns the `/health` path descriptor

### T5 — Scaffold Aspire AppHost Postgres + Api resource (AC: 5)

- [ ] Edit `backend/aspire/Faktuboh.AppHost/Program.cs`:
  - `var builder = DistributedApplication.CreateBuilder(args);`
  - `var postgres = builder.AddPostgres("postgres").WithPgAdmin();`
  - `var db = postgres.AddDatabase("faktuboh");`
  - `builder.AddProject<Projects.Faktuboh_Api>("api").WithReference(db);`
  - `builder.Build().Run();`
- [ ] Run `dotnet run --project backend/aspire/Faktuboh.AppHost` locally; verify dashboard opens and `/health` responds through Aspire-provided URL

### T6 — Scaffold Shared Kernel primitives (AC: 6)

- [ ] Create `backend/src/Faktuboh.Domain/Primitives/Money.cs`:
  ```csharp
  public sealed record Money(decimal Amount, string Currency)
  {
      public Money
      {
          if (!CurrencyRegistry.IsSupported(Currency))
              throw new ArgumentException($"Unsupported currency '{Currency}'", nameof(Currency));
          if (Amount != decimal.Round(Amount, CurrencyRegistry.MinorUnits(Currency), MidpointRounding.ToEven))
              throw new ArgumentException("Amount precision exceeds currency minor units", nameof(Amount));
      }
  }
  ```
- [ ] Create `backend/src/Faktuboh.Domain/Primitives/CurrencyRegistry.cs` with `FrozenDictionary<string, int>` mapping ISO 4217 code → minor-unit count for EUR, USD, GBP, AED, SAR, EGP (2) + JOD, KWD, BHD, TND (3)
- [ ] Create `backend/src/Faktuboh.Domain/Primitives/IBitemporal.cs` with marker interface per ADR-022 exact shape (three `get; set;` properties)
- [ ] Create `backend/src/Faktuboh.Domain/Primitives/Direction.cs` = `public enum Direction { Receivable, Payable }`
- [ ] Create `backend/src/Faktuboh.Domain/Errors/DomainException.cs` = `public abstract class DomainException(string code, string message) : Exception(message) { public string Code { get; } = code; }`
- [ ] Create `backend/src/Faktuboh.Application/Errors/ErrorCatalog.cs` with a `FrozenDictionary<string, ErrorCatalogEntry>` initialized empty (slice error codes register in Epic 0 Story 0.4 populate cycle + per-slice going forward)

### T7 — Record NSwag decision + create nswag.json (AC: 7)

- [ ] Create `backend/tools/NSwag/nswag.json` configured for OpenAPI-to-TS with TypeScript template targeting Angular HttpClient; input = `../../../artifacts/openapi.json`; output = `../../../../frontend/libs/api-contracts/src/generated/`
- [ ] Create `backend/tools/NSwag/README.md` documenting: run order (backend build → `dotnet run --project Faktuboh.Api -- --generate-openapi-doc` → `nswag run nswag.json`); this supersedes architecture.md §6.5.1's `ng-openapi-gen` reference
- [ ] Add TODO comment to `frontend/eslint.config.js` (created in 0.2) for `no-handwritten-shared-contracts` rule activation once `libs/api-contracts/` has generated content

### T8 — Scaffold test project tooling (AC: 8)

- [ ] For `Faktuboh.Domain.Tests`: `dotnet add package xunit.v3 FluentAssertions FsCheck.Xunit`
- [ ] For `Faktuboh.Application.Tests`: `dotnet add package xunit.v3 FluentAssertions NSubstitute`
- [ ] For `Faktuboh.Api.Tests`: `dotnet add package xunit.v3 FluentAssertions Microsoft.AspNetCore.Mvc.Testing Verify.Xunit Testcontainers.PostgreSql`
- [ ] For `Faktuboh.Infrastructure.Tests`: `dotnet add package xunit.v3 FluentAssertions Testcontainers.PostgreSql`
- [ ] For `Faktuboh.ArchitectureTests`: `dotnet add package xunit.v3 FluentAssertions NetArchTest.Rules`
- [ ] Add one passing smoke test per project (e.g., `Test1_Passes`) so `dotnet test` discovers content

### T9 — Initial fitness tests (AC: 2, 9)

- [ ] Create `backend/tests/Faktuboh.ArchitectureTests/SharedKernelPurityTests.cs` asserting Domain assembly has zero dependencies on `Faktuboh.*` assemblies (uses `Types.InAssembly(typeof(Money).Assembly).Should().NotHaveDependencyOnAny("Faktuboh.Api", "Faktuboh.Application", "Faktuboh.Infrastructure", "Faktuboh.Contracts")`)
- [ ] Create `backend/tests/Faktuboh.ArchitectureTests/SharedKernelNoAggregateRootsTests.cs` (§7.5.3 test #4) — scan `Faktuboh.Domain` assembly for types implementing `IAggregateRoot` and assert zero; this file currently passes trivially (no aggregates in SK)
- [ ] Create `backend/tests/Faktuboh.Domain.Tests/Primitives/CurrencyRegistryTests.cs` — assert 10-currency initial set + minor-unit counts match §5.2.5 + `MoneyTests.cs` FsCheck property asserting `new Money(amount, ccy)` round-trips via `ToString`/parse equality

## Dev Notes

### Critical guardrails (do not violate)

1. **Domain layer has zero dependencies.** Any PackageReference or ProjectReference on `Faktuboh.Domain` fails the build via NetArchTest + keeps SK compile-free. Mapperly, EF Core, Wolverine, FluentValidation — all forbidden here. [Source: architecture.md §3.3, §7.5.3 test #1, ADR-002]
2. **No `[Table]`/`[Column]`/`[JsonPropertyName]` attributes anywhere.** `EFCore.NamingConventions` handles DB `snake_case` (wired in Story 0.6). `JsonNamingPolicy.CamelCase` handles wire format (wired in Story 0.4). Fitness tests added in Story 0.8 fail the build on attribute use. [Source: architecture.md §5.2.3, §5.2.1]
3. **No `DateTime` in DTOs/entities ever.** Use `DateTimeOffset` or `DateOnly`. [Source: architecture.md §5.2.4]
4. **C# 14 `LangVersion` pinned.** Do not raise to `latest` or `preview` — pin the version the team actually tests against. [Source: architecture.md §3.2]
5. **Aspire AppHost is the only local-dev entry point.** Developers run `dotnet run --project backend/aspire/Faktuboh.AppHost`, never `dotnet run --project backend/src/Faktuboh.Api` directly. The dashboard is the single observation surface. [Source: architecture.md §6.5.3]
6. **Moq forbidden.** Use NSubstitute. Enforced by test-project csproj contents + Story 0.8 fitness test. [Source: architecture.md §5.3.3]
7. **`public partial class Program;` at Program.cs end** — required for `WebApplicationFactory<Program>` discovery in integration tests. Forgetting this breaks Stories 2.1+ integration tests.

### Source tree — files to create

```
backend/
├── Faktuboh.sln
├── Directory.Build.props              # NEW
├── global.json                        # NEW
├── aspire/
│   ├── Faktuboh.AppHost/
│   │   ├── Program.cs                 # EDIT (from template)
│   │   └── Faktuboh.AppHost.csproj
│   └── Faktuboh.ServiceDefaults/
│       ├── Extensions.cs              # EDIT (from template)
│       └── Faktuboh.ServiceDefaults.csproj
├── src/
│   ├── Faktuboh.Domain/
│   │   ├── Primitives/
│   │   │   ├── Money.cs               # NEW
│   │   │   ├── CurrencyRegistry.cs    # NEW
│   │   │   ├── IBitemporal.cs         # NEW
│   │   │   └── Direction.cs           # NEW
│   │   ├── Errors/
│   │   │   └── DomainException.cs     # NEW
│   │   └── Faktuboh.Domain.csproj
│   ├── Faktuboh.Application/
│   │   ├── Errors/
│   │   │   └── ErrorCatalog.cs        # NEW (stub — populated by slices)
│   │   └── Faktuboh.Application.csproj
│   ├── Faktuboh.Infrastructure/
│   │   └── Faktuboh.Infrastructure.csproj
│   ├── Faktuboh.Contracts/
│   │   └── Faktuboh.Contracts.csproj
│   └── Faktuboh.Api/
│       ├── Program.cs                 # EDIT (Minimal APIs + AddOpenApi + AddProblemDetails + /health)
│       └── Faktuboh.Api.csproj
├── tests/
│   ├── Faktuboh.Domain.Tests/
│   │   └── Primitives/
│   │       ├── MoneyTests.cs          # NEW
│   │       └── CurrencyRegistryTests.cs # NEW
│   ├── Faktuboh.Application.Tests/
│   ├── Faktuboh.Api.Tests/
│   ├── Faktuboh.Infrastructure.Tests/
│   └── Faktuboh.ArchitectureTests/
│       ├── SharedKernelPurityTests.cs        # NEW (§7.5.3 #1)
│       └── SharedKernelNoAggregateRootsTests.cs  # NEW (§7.5.3 #4)
└── tools/
    └── NSwag/
        ├── nswag.json                 # NEW
        └── README.md                  # NEW
```

### Testing standards

| Level | Tool | What this story covers |
|---|---|---|
| Unit | xUnit v3 + FluentAssertions + FsCheck | Money VO construction + CurrencyRegistry completeness |
| Architectural | NetArchTest | SK purity + no-aggregate-root in SK (§7.5.3 #1 + #4) |
| Integration | — | No integration tests in 0.1; first real integration test lands in 0.6 |

**Forbidden:** Moq, EF Core in-memory, `[JsonPropertyName]`, `[Table]`/`[Column]`, `DateTime`.

### Project Structure Notes

- **Aspire 13 project shape:** Aspire 13's `dotnet new aspire-apphost` template produces a csproj with `<IsAspireHost>true</IsAspireHost>` and resolves project references via the `Aspire.Hosting.AppHost` package. Verify the template matches §3.2's Aspire 13 GA surface; if template output diverges, file an upstream Aspire issue rather than patch the scaffold.
- **`backend/src/Faktuboh.Api/Slices/` folder does not exist yet.** Slices arrive with Story 2.1 (Contacts/Create). This story establishes the project shape; slice folders appear on first feature ship.

### Architecture compliance

- [architecture.md §3.2](../planning-artifacts/architecture.md) — pinned versions (runtime .NET 10, C# 14, Aspire 13, Node.js 22 LTS)
- [architecture.md §3.3](../planning-artifacts/architecture.md) — backend scaffold layout (five source projects + five test projects + aspire/)
- [architecture.md §5.2.5](../planning-artifacts/architecture.md) — Money value object + CurrencyRegistry minor-units (10 fiat initial set)
- [architecture.md §6.3.1](../planning-artifacts/architecture.md) — solution tree
- [architecture.md §6.3.4](../planning-artifacts/architecture.md) — project reference graph + invariants
- [architecture.md §7.5.3](../planning-artifacts/architecture.md) — fitness tests #1 + #4 (SK purity + no IAggregateRoot in SK)
- [adrs/adr-002-greenfield-backend.md] (embedded in architecture.md §3.8 ADR-002) — no DiTracker ports
- [adrs/adr-006-vertical-slice-architecture.md] (embedded in architecture.md §3.8 ADR-006) — vertical-slice baseline
- [adrs/adr-022-application-level-bitemporal.md](../planning-artifacts/adrs/adr-022-application-level-bitemporal.md) — `IBitemporal` marker interface exact shape
- [adrs/adr-024-minimal-apis-framework.md](../planning-artifacts/adrs/adr-024-minimal-apis-framework.md) — Minimal APIs + FluentValidation + source-gen OpenAPI stack
- [adrs/adr-005-typescript-contract-seams.md] (embedded in architecture.md §3.8 ADR-005) — NSwag generator choice delegated to 0.1

### Library/framework requirements

| Layer | Package | Version | Purpose |
|---|---|---|---|
| Runtime | .NET | 10.0.100 (LTS) | Pinned via `global.json` |
| Language | C# | 14 | Pinned via `Directory.Build.props` `<LangVersion>14</LangVersion>` |
| Aspire | Aspire.Hosting.AppHost / Aspire.Hosting.Postgres | 13.x | AppHost + ServiceDefaults |
| API framework | Microsoft.AspNetCore.OpenApi | 10.0.x | Source-gen OpenAPI |
| Validation | FluentValidation.AspNetCore | latest stable | Validator base + endpoint filter (wired in 0.4) |
| Test runner | xUnit.v3 | latest stable | All test projects |
| Assertions | FluentAssertions | latest stable | All test projects |
| Mocking | NSubstitute | latest stable | Application.Tests + Api.Tests |
| Property-based | FsCheck.Xunit | latest stable | Domain.Tests (Money round-trip) |
| Integration DB | Testcontainers.PostgreSql | latest stable | Api.Tests + Infrastructure.Tests |
| Snapshot | Verify.Xunit | latest stable | Api.Tests (OpenAPI snapshot in later stories) |
| Architecture | NetArchTest.Rules | latest stable | ArchitectureTests |
| TS generator | NSwag (CLI) | latest stable | `backend/tools/NSwag/` (consumed by 0.2) |

### Testing requirements

- AC2 fitness test (SK purity) MUST be the first NetArchTest committed — it is the contract that all subsequent fitness tests layer onto.
- AC6 Money tests MUST use FsCheck — banker's rounding + currency precision is the class of bug that property-based testing catches best.
- AC9 `dotnet test backend/Faktuboh.sln` MUST succeed end-to-end before this story is marked complete. Partial green is a fail.

### Previous Story Intelligence

**None applicable.** This is the first story being expanded. No `0-*.md` story files exist in `implementation-artifacts/`. Epic 0 stories 0.2-0.17 cite this story as the baseline they assume complete.

## References

- [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) — Story 0.1 definition + acceptance criteria
- [_bmad-output/planning-artifacts/architecture.md §3.2-§3.3](../planning-artifacts/architecture.md) — versions + backend scaffold
- [_bmad-output/planning-artifacts/architecture.md §3.8 ADR-002/ADR-005/ADR-006](../planning-artifacts/architecture.md) — greenfield + seam reservation + vertical-slice
- [_bmad-output/planning-artifacts/architecture.md §5.2.5](../planning-artifacts/architecture.md) — Money + CurrencyRegistry convention
- [_bmad-output/planning-artifacts/architecture.md §6.3.1 + §6.3.4](../planning-artifacts/architecture.md) — solution tree + project reference graph
- [_bmad-output/planning-artifacts/architecture.md §7.5.3](../planning-artifacts/architecture.md) — fitness tests #1 + #4
- [_bmad-output/planning-artifacts/adrs/adr-022-application-level-bitemporal.md](../planning-artifacts/adrs/adr-022-application-level-bitemporal.md) — `IBitemporal` interface shape
- [_bmad-output/planning-artifacts/adrs/adr-024-minimal-apis-framework.md](../planning-artifacts/adrs/adr-024-minimal-apis-framework.md) — Minimal APIs framework decision

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

_Populated during implementation._

### File List

_Populated during implementation — expected new/modified files per the Source Tree section above._
