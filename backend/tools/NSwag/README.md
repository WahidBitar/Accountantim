# NSwag — TypeScript Contracts Generator

**Decision (delegated 2026-04-22 from ADR-005):** NSwag is the TypeScript contracts generator for Faktuboh.
This supersedes the `ng-openapi-gen` reference in `architecture.md §6.5.1`.

## Why NSwag

- First-party .NET tooling; aligned with the source-gen `Microsoft.AspNetCore.OpenApi` pipeline that ships in .NET 10.
- Emits Angular `HttpClient`-flavored TypeScript clients, matching the two-target Angular workspace from Story 0.2.
- Supports the round-trip we need: C# DTO -> OpenAPI 3 (source-gen) -> TypeScript request/response classes.

## Run order

```bash
# 1. Build the API so the source-gen OpenAPI document is materialized.
dotnet build backend/src/Faktuboh.Api -c Release

# 2. Export the OpenAPI document to artifacts/openapi.json.
#    (Wired in Story 0.4 / 0.11; the `--generate-openapi-doc` switch will be added then.)
dotnet run --project backend/src/Faktuboh.Api -- --generate-openapi-doc

# 3. Generate the TypeScript contracts.
nswag run backend/tools/NSwag/nswag.json
```

The third step writes to `frontend/libs/api-contracts/src/generated/api-client.ts`.
That folder lands when Story 0.2 (Angular two-target workspace) ships.

## Hard rule

The `no-handwritten-shared-contracts` ESLint rule (added in Story 0.2) forbids hand-edited
files inside `frontend/libs/api-contracts/src/generated/`. The generated artifact is the
single source of truth: never edit it; regenerate it.

## Tooling

NSwag CLI is not installed by default. To install locally:

```bash
dotnet tool install --global NSwag.ConsoleCore
```

CI installs the same way (Story 0.9).
