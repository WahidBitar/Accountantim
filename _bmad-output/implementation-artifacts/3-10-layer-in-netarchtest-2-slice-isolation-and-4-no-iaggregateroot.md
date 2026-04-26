# Story 3.10: Layer in NetArchTest #2 slice isolation and #4 no-IAggregateRoot

Status: ready-for-dev

## Story

As a **developer enforcing architectural integrity before complex epics**,
I want **NetArchTest fitness tests #2 (slice isolation) and #4 (no `IAggregateRoot` in Shared Kernel) running as CI-required checks**,
so that **the full four-test fitness gate is active before Epics 4, 5, 6 begin** (Epic 0 deferred these per Story 0.8 because validating them demands 8+ real slices — now present). A slice's `Domain/` referencing another slice's `Domain/` directly fails with a clear message; any `IAggregateRoot`-implementing type inside `Faktuboh.Domain` (Shared Kernel) fails the build; both tests run as required GitHub Actions checks; the Epic 0 "deferred" inventory is updated.

**Covers requirements:** architecture §3.3 (fitness tests), Epic 0 Story 0.8 (deferral inventory), ADR-006 (vertical-slice architecture enforcement).

## Acceptance Criteria

### AC1 — NetArchTest #2 slice isolation test

**Given** Epic 3 has shipped 8+ real slices (Contacts/Create, Contacts/Edit, Contacts/Delete, Contacts/List, Contacts/History, JournalEntries/Create, JournalEntries/Edit, JournalEntries/Delete, JournalEntries/History, Debts/Dashboard, Debts/Charts, Export/Csv, Users/GetRecentDenominations — comfortably past 8)
**When** a PR introduces a `using Faktuboh.Api.Slices.OtherSlice.*;` statement inside a slice's `Domain/` subfolder (not allowed)
**Then** the NetArchTest fitness test #2 fails the build with message: `Slice isolation violation: <source slice>.Domain references <target slice>. Cross-slice dependencies MUST flow through Faktuboh.Domain.Events.IntegrationEvents/*. See ADR-006.`
**And** the only permitted cross-slice reference is from `Faktuboh.Domain.Events.IntegrationEvents/*` (the shared integration-event namespace)
**And** CI blocks merge on failure

### AC2 — NetArchTest #4 no-IAggregateRoot-in-Shared-Kernel test

**Given** the Shared Kernel lives in `Faktuboh.Domain` (assembly name)
**When** a type in that assembly implements `IAggregateRoot`
**Then** the fitness test #4 fails the build with message: `Shared Kernel purity violation: <TypeName> in Faktuboh.Domain implements IAggregateRoot. Aggregate roots belong in their slice's Domain/ folder, not in Shared Kernel.`
**And** the Shared Kernel is reserved for VOs (Money, CurrencyRegistry), interfaces (IBitemporal, IRepository<T>, IAggregateRoot itself), primitives (enums like Direction), and integration-event record types
**And** the test runs as a required CI check

### AC3 — CI integration

**Given** the `Faktuboh.ArchitectureTests` test project
**When** `dotnet test --filter Category=Architecture` runs in CI
**Then** both new tests run alongside the existing #1 (Shared Kernel purity — checks no EF Core or Npgsql references leak in) and #3 (IntegrationEvents-only cross-slice) from Story 0.8
**And** the GitHub Actions workflow `architecture-fitness.yml` lists all four as required status checks
**And** the job completes in < 30 seconds (NetArchTest is fast; no DB)

### AC4 — Clear failure messages

**Given** a violating PR
**When** the test fails
**Then** the error output includes:
  - Exact violating type name
  - Exact reference graph path (Source Type → Forbidden Target)
  - Link to ADR-006 with rationale
  - Suggested fix ("Route via IntegrationEvent X" / "Move to Slice Y's Domain folder")

### AC5 — Epic 0 "deferred" inventory update

**Given** the Epic 0 Story 0.8 story context file lists #2 and #4 as "deferred to Story 3.10"
**When** this story completes
**Then** the Epic 0 deferred-items tracking in `c:/Work/Accountantim/_bmad-output/planning-artifacts/epics.md` (or the equivalent tracking doc) is updated to mark these two as "shipped in Epic 3 Story 3.10"
**And** the Story 0.8 context file is annotated with a `## Status Update` section noting the deferral is resolved

### AC6 — Regression detection against earlier slices

**Given** the tests run for the first time against real Epic 2 + Epic 3 slices
**When** CI executes
**Then** ZERO violations are reported in the current codebase (any violation found is a bug to fix before merging this story)
**And** the test suite includes a smoke assertion that the known-good slices pass

## Tasks / Subtasks

### Tests (this story is entirely test-infrastructure)

- [ ] **T1 (AC: 1)** — Create `tests/Faktuboh.ArchitectureTests/SliceIsolationTests.cs`
  - [ ] `[Fact] public void Slices_MustNotReferenceOtherSlices_Domain()` 
  - [ ] NetArchTest predicate: for each slice root namespace `Faktuboh.Api.Slices.<Name>.Domain.*`, assert no type references any other slice's `.Domain.*` namespace
  - [ ] Allowed exception: types in `Faktuboh.Domain.Events.IntegrationEvents.*`
  - [ ] Test body: iterate all slice pairs; build NetArchTest predicate per pair; fail with aggregated message
- [ ] **T2 (AC: 2)** — Create `tests/Faktuboh.ArchitectureTests/SharedKernelPurityTests.cs` (or extend the existing #1 home)
  - [ ] `[Fact] public void SharedKernel_MustNotContain_IAggregateRoot_Implementations()`
  - [ ] NetArchTest predicate: types in `Faktuboh.Domain` assembly that implement `IAggregateRoot` → fail
- [ ] **T3 (AC: 3)** — Update `.github/workflows/ci.yml` to run architecture tests as a dedicated job with required status
  - [ ] Job: `fitness-tests`
  - [ ] Step: `dotnet test tests/Faktuboh.ArchitectureTests --filter Category=Architecture`
  - [ ] Mark as required in branch protection rule for `main`
- [ ] **T4 (AC: 4)** — Custom assertion helpers in `Faktuboh.ArchitectureTests/Helpers/`
  - [ ] `AssertNoCrossSliceReferences(predicate, message)` — builds actionable failure output
  - [ ] `AssertNoSharedKernelAggregateRoots(message)` — same
- [ ] **T5 (AC: 5)** — Update Epic 0 inventory
  - [ ] Modify `c:/Work/Accountantim/_bmad-output/implementation-artifacts/0-8-netarchtest-fitness-tests-hard-gates.md` with `## Status Update` section noting #2 + #4 shipped here
  - [ ] Update `c:/Work/Accountantim/_bmad-output/planning-artifacts/epics.md` if a deferred-items inventory lives there
- [ ] **T6 (AC: 6)** — Run the new tests against current `main` — zero violations expected; any found are bugs to fix in this PR

## Dev Notes

### Critical guardrails

1. **Tests must have clear failure messages.** NetArchTest's default output is cryptic; custom helpers + links to ADR-006 are mandatory for future developer usability.
2. **Allowed exception: IntegrationEvents.** The ONLY cross-slice reference that passes #2 is types living in `Faktuboh.Domain.Events.IntegrationEvents.*`. This is the single seam that keeps slices decoupled but communicable.
3. **Shared Kernel = `Faktuboh.Domain` assembly.** Not a namespace within slices. The test checks the assembly name.
4. **IAggregateRoot ITSELF lives in Shared Kernel as an interface.** The test checks for IMPLEMENTATIONS — any type that has `IAggregateRoot` in its base-or-interface list AND lives in the SK assembly.
5. **Run fast.** NetArchTest scans compiled assemblies; no DB, no reflection-heavy introspection. Target < 30s per test suite run.
6. **Smoke test ZERO violations before merge.** If this story's first run finds violations in existing code, those MUST be fixed as part of this story (not deferred) — otherwise the gate is toothless.

### Source tree

```
tests/Faktuboh.ArchitectureTests/
├── SliceIsolationTests.cs                            # NEW
├── SharedKernelPurityTests.cs                        # MODIFY — add #4 test
├── IntegrationEventsOnlyTests.cs                     # EXISTS from Story 0.8 (#3)
├── Helpers/
│   ├── AssertionHelpers.cs                           # NEW — actionable failure messages
│   └── SliceDiscovery.cs                             # NEW — enumerates slice namespaces
└── ArchitectureTests.csproj                          # MODIFY — verify NetArchTest version

.github/workflows/
└── ci.yml                                             # MODIFY — add fitness-tests job as required

_bmad-output/implementation-artifacts/
└── 0-8-netarchtest-fitness-tests-hard-gates.md       # MODIFY — add Status Update section
```

### NetArchTest predicate examples (reference)

```csharp
// #2 — slice isolation
var result = Types.InAssembly(typeof(Program).Assembly)
    .That()
    .ResideInNamespaceMatching("Faktuboh.Api.Slices.(Contacts|JournalEntries|Debts|Export|Users)\\.Domain.*")
    .ShouldNot()
    .HaveDependencyOnAny(otherSlicesPrefixes)
    .GetResult();
// Fail with aggregated message if !result.IsSuccessful

// #4 — no IAggregateRoot in SK
var result = Types.InAssembly(typeof(Money).Assembly)  // Faktuboh.Domain
    .That()
    .ImplementInterface(typeof(IAggregateRoot))
    .Should()
    .Equal(new List<Type>())  // Expect empty
    .GetResult();
```

### Testing standards

Architecture tests run on every PR as required check. Zero tolerance for failure.

### Project Structure Notes

This story ships no runtime code — only test infrastructure + CI config + a documentation update. It is the smallest possible "architectural guardrail" story, but it's load-bearing because Epic 4 onwards builds on the assumption that slice isolation is enforced.

### Architecture compliance

- **ADR-006** vertical-slice architecture
- **§3.3** fitness tests as hard gates
- **Story 0.8** deferral resolved

### Library/framework requirements

| Package | Version | Notes |
|---|---|---|
| NetArchTest.Rules | latest stable | Already consumed by Story 0.8 |
| xUnit v3 | latest stable | Test runner |
| FluentAssertions | latest stable | For asserting helper output |

### Testing requirements

| Category | Tools |
|---|---|
| Architecture | NetArchTest + xUnit v3 |

### Previous Story Intelligence

- **Story 0.8** (NetArchTest fitness tests hard gates) — shipped #1 and #3; deferred #2 and #4 to this story; the infrastructure (test project, CI integration) exists here as the foundation
- **Story 2.1 through 3.9** — the real slices that make #2 testable; this story's deferral was contingent on "8+ real slices" which Epic 3 reaches

## References

- [epics.md Story 3.10](../planning-artifacts/epics.md)
- [architecture.md §3.3](../planning-artifacts/architecture.md) fitness tests
- [adrs/adr-006-vertical-slice-architecture.md](../planning-artifacts/adrs/adr-006-vertical-slice-architecture.md) (if exists; else architecture.md section reference)
- [implementation-artifacts/0-8-netarchtest-fitness-tests-hard-gates.md](./0-8-netarchtest-fitness-tests-hard-gates.md)

## Dev Agent Record

### Agent Model Used
_To be filled by implementing dev agent._

### Debug Log References
_Populated during implementation._

### Completion Notes List
_Populated during implementation._

### File List
_Populated during implementation._
