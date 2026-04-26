# Story 0.8: NetArchTest fitness tests (hard gates)

Status: ready-for-dev

## Story

As a **developer**,
I want **NetArchTest fitness tests for Shared Kernel purity (#1) and cross-slice-communication-only-via-IntegrationEvents (#3) wired as CI required checks**,
so that **the E2 first-slice compound invariants are mechanically asserted before any feature slice can merge**.

**Covers requirements:** Epic 0 Platform Foundation — hard gate. §7.5.3 fitness tests #1 + #3. (Tests #2 + #4 layer in during Epic 3 per Story 3.10.)

**Why this story now:** After Stories 0.4-0.7 landed the code surfaces that the fitness tests scan. Before Story 2.1 — the first real cross-slice integration-event flow validates against test #3.

## Acceptance Criteria

### AC1 — Fitness test #1 — Shared Kernel purity

**Given** Story 0.1 already shipped `SharedKernelPurityTests.cs` scaffold (AC2 of that story)
**When** this story finalizes + activates the test
**Then** `backend/tests/Faktuboh.ArchitectureTests/FitnessTest1_SharedKernelPurity.cs` asserts:

```csharp
[Fact]
public void Shared_kernel_has_no_dependency_on_api_or_infrastructure()
{
    var result = Types.InAssembly(typeof(Money).Assembly)
        .Should()
        .NotHaveDependencyOnAny("Faktuboh.Api", "Faktuboh.Application", "Faktuboh.Infrastructure")
        .GetResult();
    result.IsSuccessful.Should().BeTrue($"Shared Kernel violation: {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}");
}
```

**And** the assertion fails the build if any future commit adds a dependency [Source: architecture.md §7.5.3 #1 + Story 0.1 AC2]

### AC2 — Fitness test #3 — cross-slice via IntegrationEvents only

**Given** slices live under `Faktuboh.Api/Slices/<SliceName>/`
**When** the fitness test runs
**Then** `backend/tests/Faktuboh.ArchitectureTests/FitnessTest3_CrossSliceViaIntegrationEvents.cs` asserts:
- Types in `Faktuboh.Api.Slices.X` namespace do NOT depend on types in `Faktuboh.Api.Slices.Y` namespace (for any distinct X, Y)
- The SOLE exception: types in `Faktuboh.Domain.Events.IntegrationEvents` namespace may be referenced cross-slice
- `Faktuboh.Domain.Events.IntegrationEvents.*` namespace contains only record types with `public init` properties (no mutable state, no aggregates, no value objects with invariants)

```csharp
[Fact]
public void Slices_do_not_reference_other_slices_except_via_integration_events()
{
    var sliceTypes = Types.InAssembly(typeof(Program).Assembly)
        .That().ResideInNamespaceMatching(@"Faktuboh\.Api\.Slices\.[A-Za-z0-9]+");
    foreach (var sliceRoot in DiscoverSliceRoots())
    {
        var otherSlices = AllSliceRoots.Except(new[] { sliceRoot });
        var result = sliceTypes
            .That().ResideInNamespaceStartingWith($"Faktuboh.Api.Slices.{sliceRoot}")
            .Should().NotHaveDependencyOnAny(otherSlices.Select(o => $"Faktuboh.Api.Slices.{o}").ToArray())
            .GetResult();
        result.IsSuccessful.Should().BeTrue($"Slice {sliceRoot} depends on another slice directly (cross-slice must route through IntegrationEvents).");
    }
}

[Fact]
public void Integration_events_are_init_records_only()
{
    var result = Types.InAssembly(typeof(ContactCreatedEvent).Assembly)
        .That().ResideInNamespace("Faktuboh.Domain.Events.IntegrationEvents")
        .Should().BeRecord()  // Custom predicate — records only
        .And().MeetCustomRule(new AllPropertiesInitOnlyRule())
        .GetResult();
    result.IsSuccessful.Should().BeTrue();
}
```

[Source: architecture.md §7.5.3 #3 + §4 D4.12]

### AC3 — IntegrationEvents namespace scaffolded

**Given** cross-slice communication routes via `Faktuboh.Domain.Events.IntegrationEvents`
**When** this story scaffolds the namespace
**Then** `backend/src/Faktuboh.Domain/Events/IntegrationEvents/` exists with:
- A README.md documenting the "records only, public init, no logic" rule
- A placeholder `IIntegrationEvent.cs` marker interface (non-generic)
**And** Story 2.1's `ContactCreatedEvent` will be the first real entry in this namespace

### AC4 — CI-gate-first rule — test activates in CI BEFORE content is marked done

**Given** the honor-code commitment from §7.5.3 ("for each fitness test, CI wiring (required check) lands before test content is marked done")
**When** this story ships
**Then** `backend-architecture-tests` job in Story 0.9's `.github/workflows/ci.yml` is already wired (from 0.9 AC2)
**And** `ci.yml` is updated to add an explicit required-check annotation for `FitnessTest1_SharedKernelPurity` and `FitnessTest3_CrossSliceViaIntegrationEvents` test names (GitHub branch-protection-level: the test methods' FQN is configured as a required check)
**And** before the fitness tests pass content, a deliberately-broken branch (e.g., Domain references Api) is pushed + CI red is observed + branch is discarded — proving the gate enforces

### AC5 — Local pre-push git hook (per §7.5.3 mechanism note)

**Given** solo-dev context — self-enforcement is the only observer
**When** a developer runs `git push` on a feature branch
**Then** a pre-push hook runs `dotnet test backend/tests/Faktuboh.ArchitectureTests --no-restore --configuration Release --filter "FullyQualifiedName~Fitness"` and blocks push on failure
**And** the hook script lives at `.githooks/pre-push` (committed to repo) + `git config core.hooksPath .githooks` is documented in README.md for developer setup
**And** the hook skips when the commit message contains `[skip fitness]` (intended as a rare escape hatch — an ADR amendment is required per §7.5.6 RT-36)

### AC6 — Deliberately-broken scenario dry run

**Given** AC4 requires proving the gate actually enforces
**When** the dev runs this dry run once
**Then**:
1. Branch off main
2. Add a `using Faktuboh.Api;` line to `Faktuboh.Domain/Primitives/Money.cs` (violation)
3. `git push` the branch
4. Observe `ci.yml` fail with `FitnessTest1_SharedKernelPurity.Shared_kernel_has_no_dependency_on_api_or_infrastructure` assertion failure
5. Delete the branch
**And** a second dry run triggers fitness test #3 by adding a cross-slice reference (to be added once Story 2.1's slices exist — before first merge)
**And** the dry-run log + screenshots are committed to `docs/runbooks/fitness-test-dry-run.md`

## Tasks / Subtasks

### T1 — Write fitness test #1 (AC: 1)

- [ ] Create `backend/tests/Faktuboh.ArchitectureTests/FitnessTest1_SharedKernelPurity.cs` per AC1 snippet
- [ ] Delete placeholder `SharedKernelPurityTests.cs` from Story 0.1 (consolidated into FitnessTest1)

### T2 — Scaffold IntegrationEvents namespace (AC: 3)

- [ ] Create `backend/src/Faktuboh.Domain/Events/IntegrationEvents/README.md`:
  ```
  # IntegrationEvents

  This namespace is the ONLY permitted cross-slice coupling surface per fitness test #3 (architecture.md §7.5.3).

  Rules:
  - Records only (no classes, no structs)
  - All properties `public init;` (immutable after construction)
  - No domain invariants or business logic
  - No references to Api / Application / Infrastructure

  A slice emitting a cross-slice event defines the record here; consumer slices reference it.
  ```
- [ ] Create `backend/src/Faktuboh.Domain/Events/IntegrationEvents/IIntegrationEvent.cs` marker interface

### T3 — Write fitness test #3 (AC: 2)

- [ ] Create `backend/tests/Faktuboh.ArchitectureTests/FitnessTest3_CrossSliceViaIntegrationEvents.cs` per AC2 snippet
- [ ] Create helper `DiscoverSliceRoots()` + `AllPropertiesInitOnlyRule` custom NetArchTest rule
- [ ] Because slices don't exist yet (Story 2.1 creates the first), the test's slice-pair iteration is vacuous at this commit — but the test must still pass (no slices = no violations)

### T4 — Wire CI required check (AC: 4)

- [ ] Edit Story 0.9's `.github/workflows/ci.yml` `backend-architecture-tests` job to add:
  ```yaml
  backend-architecture-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@<sha>
      - uses: actions/setup-dotnet@<sha>
        with: { dotnet-version: '10.0.100' }
      - run: dotnet test backend/tests/Faktuboh.ArchitectureTests --configuration Release --filter "FullyQualifiedName~Fitness"
  ```
- [ ] In GitHub repo Settings → Branches → Branch protection rules → main, add `backend-architecture-tests / build` as required check

### T5 — Pre-push hook (AC: 5)

- [ ] Create `.githooks/pre-push` (bash):
  ```bash
  #!/usr/bin/env bash
  set -e
  if git log -1 --pretty=%B | grep -q "\[skip fitness\]"; then
    echo "skip fitness tests (per commit marker)"
    exit 0
  fi
  echo "Running fitness tests before push..."
  dotnet test backend/tests/Faktuboh.ArchitectureTests --no-restore --configuration Release --filter "FullyQualifiedName~Fitness" || {
    echo "Fitness tests failed. Push blocked."
    exit 1
  }
  ```
- [ ] Add `chmod +x .githooks/pre-push`
- [ ] Document in `README.md`: `git config core.hooksPath .githooks`

### T6 — Deliberately-broken dry run (AC: 6)

- [ ] Execute dry-run scenario 1 (Domain → Api reference)
- [ ] Capture CI log + screenshot, commit to `docs/runbooks/fitness-test-dry-run.md`
- [ ] Scenario 2 (cross-slice reference) is deferred to post-Story 2.1 since slices don't exist yet — add a TODO in the runbook + fulfill before Epic 2 merges

## Dev Notes

### Critical guardrails (do not violate)

1. **CI-gate-first rule is sacred.** Never mark a fitness test complete unless CI already requires it. "Authored but not wired" = unfinished. [Source: architecture.md §7.5.3]
2. **Tests #2 + #4 are NOT in scope for Story 0.8.** They ship with Story 3.10 when Epic 3 stabilizes slice shapes. This story delivers #1 + #3 only. [Source: epics.md Epic 0 hard-gate split — Murat]
3. **`[skip fitness]` is nuclear.** The commit message escape hatch requires an ADR amendment post-merge (§7.5.6 RT-36). It's not a routine tool — it's a break-glass.
4. **Fitness test #3 is vacuously true until slices exist.** That's fine — the test's structure must be in place so Story 2.1's slices land into enforcement.
5. **IntegrationEvents namespace is sacred.** Adding anything other than immutable-init records fails fitness test #3's property rule.

### Source tree — files to create

```
backend/src/Faktuboh.Domain/Events/IntegrationEvents/
├── README.md                                           # NEW
└── IIntegrationEvent.cs                                # NEW

backend/tests/Faktuboh.ArchitectureTests/
├── FitnessTest1_SharedKernelPurity.cs                  # NEW (replaces Story 0.1 stub)
├── FitnessTest3_CrossSliceViaIntegrationEvents.cs      # NEW
└── Rules/
    └── AllPropertiesInitOnlyRule.cs                    # NEW (custom NetArchTest rule)

.githooks/
└── pre-push                                            # NEW

.github/workflows/ci.yml                                # EDIT (required-check wiring)

docs/runbooks/
└── fitness-test-dry-run.md                             # NEW (evidence log)
```

### Testing standards

Meta: fitness tests are tests of tests. The tests themselves run via xUnit v3 on CI; the "dry-run" proves they enforce. No additional testing of the fitness tests beyond AC6.

### Project Structure Notes

- **Fitness tests #2 (slice isolation) and #4 (no IAggregateRoot in SK)** are CREATED in Story 0.1 (#4 trivially — no aggregates in SK) but formal wiring lands in Story 3.10 when slice count justifies the enforcement cost. Avoid overbuilding now.
- **Roslyn-based call-site inspection** for "every `throw DomainException` references a registered code" (from Story 0.4) is a separate fitness test — not counted here. Story 0.4 owns its fitness tests; Story 0.8 owns #1 + #3 only.

### Architecture compliance

- [architecture.md §7.5.3](../planning-artifacts/architecture.md) — fitness tests registry
- [architecture.md §7.5.6](../planning-artifacts/architecture.md) — single-observer risk + RT-36
- [architecture.md §4 D4.12](../planning-artifacts/architecture.md) — integration events as the cross-slice contract
- [architecture.md ADR-006](../planning-artifacts/architecture.md) — vertical-slice architecture
- [architecture.md ADR-011](../planning-artifacts/architecture.md) — DDD × VSA reconciliation (Shared Kernel holds primitives)

### Library/framework requirements

| Layer | Package | Purpose |
|---|---|---|
| Architecture tests | NetArchTest.Rules | Assembly-dependency assertions |
| Unit tests | xUnit v3 + FluentAssertions | Test runner |
| Dry-run tooling | git + CI | AC6 evidence |

### Testing requirements

- **Dry-run in AC6 is mandatory.** Without the dry run, we don't know the gate works. Document in `docs/runbooks/fitness-test-dry-run.md` with logs.
- **Test coverage of fitness test internals** is not required — NetArchTest's own tests cover the library behavior. Our tests are declarative assertions.

### Previous Story Intelligence

- **Story 0.1 (backend scaffold)** complete: stub `SharedKernelPurityTests.cs` + `SharedKernelNoAggregateRootsTests.cs` exist. This story consolidates them into `FitnessTest1_SharedKernelPurity.cs` (#4 remains in place as a trivial assertion).
- **Story 0.4 (RFC 9457)** complete: `ErrorCatalogTests` fitness test exists (per Story 0.4 AC8). Separate from #1-#4.
- **Story 0.7 (ProcessingActivity)** complete: `ProcessingActivityEnforcementTests` exists (per 0.7 AC7). Separate from #1-#4.
- **Story 0.9 (CI/CD)** complete: `backend-architecture-tests` job exists; this story adds the specific required-check mapping in branch protection.
- **Story 2.1 (Contacts/Create)** is the first real validator — fitness test #3's cross-slice assertion becomes non-vacuous when `ContactCreatedEvent` is consumed by `Slices/Debts/Projections/ContactCreatedHandler.cs`.

## References

- [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) — Story 0.8 definition
- [_bmad-output/planning-artifacts/architecture.md §7.5.3](../planning-artifacts/architecture.md) — fitness tests registry
- [_bmad-output/planning-artifacts/architecture.md §7.5.6](../planning-artifacts/architecture.md) — single-observer risk
- [_bmad-output/planning-artifacts/architecture.md ADR-006](../planning-artifacts/architecture.md) — vertical-slice

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

_Populated during implementation._

### File List

_Populated during implementation — expected new/modified files per the Source Tree section above._
