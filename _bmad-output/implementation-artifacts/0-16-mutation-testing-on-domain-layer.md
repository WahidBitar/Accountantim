# Story 0.16: Mutation testing on Domain layer (AR-048)

Status: ready-for-dev

## Story

As **Wahid**,
I want **Stryker.NET configured against `Faktuboh.Domain` running weekly with a triage workflow**,
so that **solo-dev no-pair-review doesn't leak correctness bugs on Money VO, bitemporal interceptor, capability-token validator, or rate-feed reconciler**.

**Covers requirements:** Epic 0 — AR-048. Mutation testing on Domain layer via Stryker.NET.

**Why this story now:** After Stories 0.1 + 0.15 (Domain primitives exist to mutate). Weekly cadence can start at Epic 0 close and compound. Target ≥90% mutation score on the canonical `DomainMath` module is an Epic 6 milestone, but the scaffold lands now.

## Acceptance Criteria

### AC1 — Stryker.NET installed + configured

**Given** Stryker.NET is the mutation-testing framework per AR-048
**When** this story configures it
**Then** `backend/stryker-config.json` exists with:

```json
{
  "stryker-config": {
    "project": "backend/src/Faktuboh.Domain/Faktuboh.Domain.csproj",
    "test-projects": [
      "backend/tests/Faktuboh.Domain.Tests/Faktuboh.Domain.Tests.csproj"
    ],
    "mutate": [
      "**/Primitives/**.cs",
      "**/JournalEntries/**.cs",
      "**/Errors/**.cs",
      "**/Compliance/**.cs"
    ],
    "reporters": ["progress", "cleartext", "html", "json", "dashboard"],
    "thresholds": { "high": 90, "low": 80, "break": 70 },
    "concurrency": 4
  }
}
```

**And** `dotnet tool install --global dotnet-stryker` is documented in `docs/runbooks/mutation-testing.md`
**And** `dotnet stryker -c backend/stryker-config.json` runs from repo root [Source: epics.md Story 0.16 + AR-048]

### AC2 — Weekly scheduled GitHub Action

**Given** AR-048 specifies weekly runs
**When** `.github/workflows/mutation-testing.yml` exists
**Then** the workflow triggers via `schedule: [{ cron: '0 3 * * 1' }]` (every Monday 03:00 UTC)
**And** it also triggers on `workflow_dispatch` for on-demand runs
**And** the workflow installs `.NET 10`, installs `dotnet-stryker` tool, runs Stryker, publishes the HTML report as an artifact, and posts the mutation score to a status badge
**And** the status badge is linked in `README.md` as `![Mutation Score](https://img.shields.io/badge/mutation-<score>%25-green)` (dynamic shield via badge JSON endpoint or README-commit bot) [Source: epics.md Story 0.16]

### AC3 — Mutation score baseline captured

**Given** this is the first Stryker run
**When** the first weekly run completes
**Then** the mutation score is captured + committed to `docs/quality/mutation-baseline.md` with:
- Run date
- Score percentage
- Surviving-mutant count by file
- Triage plan for mutants to kill first
**And** subsequent runs compare against this baseline; regression >5 points triggers a triage review

### AC4 — Triage workflow documented

**Given** AR-048 specifies a 2h/week triage budget
**When** a surviving mutant is identified
**Then** the triage workflow in `docs/runbooks/mutation-testing.md` covers:
1. Run Stryker locally: `dotnet stryker -c backend/stryker-config.json --reporter html`
2. Open `StrykerOutput/<timestamp>/reports/mutation-report.html`
3. For each surviving mutant, decide:
   - **Kill** — add a test covering the mutated behavior
   - **Accept** — document why in `docs/quality/accepted-mutants.md` with ADR-worthy justification
   - **Exclude** — add Stryker exclusion (e.g., the mutant is in generated code)
4. Commit changes + re-run Stryker to confirm mutant killed
**And** budget per week: 2 hours — if triage overflows, defer to next week; don't sacrifice sleep

### AC5 — Mutation score target per module

**Given** AR-048 specifies ≥90% on canonical `DomainMath` module (Epic 6 milestone)
**When** this story lands (Epic 0)
**Then** initial targets are:
- `Faktuboh.Domain.Primitives.Money` + `CurrencyRegistry` — ≥90% (core fintech correctness)
- `Faktuboh.Domain.JournalEntries.CompoundJournalEntryBalance` — ≥90% (balance invariant is safety-critical)
- Rest of Domain — ≥80% (the "break" threshold)
**And** mutation targets are documented in `docs/quality/mutation-targets.md` with rationale per module
**And** Stryker's `thresholds.break: 70` fails the scheduled run if overall score drops below 70% (compound regression protection)

### AC6 — Integration with CI/CD

**Given** Stryker is expensive (minutes to hours depending on codebase size)
**When** mutation tests run in CI
**Then** they do NOT run on every PR (cost-prohibitive)
**And** they DO run weekly via `mutation-testing.yml`
**And** they CAN be triggered on-demand via `workflow_dispatch` for specific investigations
**And** the weekly run's duration is logged; if consistent runs exceed 1 hour at Epic 6 time, a follow-up story splits Stryker by module or adopts incremental mutation testing

### AC7 — First run + baseline commit

**Given** AC3 requires a baseline
**When** this story is marked complete
**Then** at least one full Stryker run has been executed locally
**And** `docs/quality/mutation-baseline.md` is committed with the first run's score + surviving mutants
**And** the first triage decisions are made (e.g., "kill 3 mutants in Money.cs by adding X test; accept 2 mutants in TestBitemporal.cs because it's a fixture")

## Tasks / Subtasks

### T1 — Install Stryker.NET + configure (AC: 1)

- [ ] Create `backend/stryker-config.json` per AC1
- [ ] Document local install in `docs/runbooks/mutation-testing.md`: `dotnet tool install --global dotnet-stryker`
- [ ] Run `dotnet stryker -c backend/stryker-config.json --dry-run` to validate config parses

### T2 — Weekly workflow (AC: 2)

- [ ] Create `.github/workflows/mutation-testing.yml`:
  ```yaml
  name: mutation-testing
  on:
    schedule: [{ cron: '0 3 * * 1' }]
    workflow_dispatch:
  permissions: { contents: read }
  jobs:
    stryker:
      runs-on: ubuntu-latest
      steps:
        - uses: actions/checkout@<sha>
        - uses: actions/setup-dotnet@<sha>
          with: { dotnet-version: '10.0.100' }
        - run: dotnet tool install --global dotnet-stryker
        - run: dotnet stryker -c backend/stryker-config.json
          working-directory: .
        - uses: actions/upload-artifact@<sha>
          with:
            name: stryker-report
            path: StrykerOutput/**/reports/mutation-report.html
        - name: Post score to status badge
          run: |
            SCORE=$(jq '.metrics.overall.score' StrykerOutput/**/reports/mutation-report.json)
            echo "Mutation score: $SCORE"
            # TODO: push to shields.io endpoint or commit to README
  ```
- [ ] Configure the status badge — simplest path: use shields.io's dynamic badge pointing to a committed `docs/quality/mutation-score.json` that the workflow updates weekly

### T3 — First run + baseline (AC: 3, 7)

- [ ] Run Stryker locally against current Domain (post-Story 0.15)
- [ ] Capture HTML report
- [ ] Commit `docs/quality/mutation-baseline.md` with initial score + surviving mutants

### T4 — Triage workflow (AC: 4)

- [ ] Create `docs/runbooks/mutation-testing.md` with the 4-step triage workflow from AC4
- [ ] Create `docs/quality/accepted-mutants.md` template

### T5 — Module targets (AC: 5)

- [ ] Create `docs/quality/mutation-targets.md` with per-module targets (Money ≥90%, CompoundJournalEntryBalance ≥90%, rest ≥80%)
- [ ] Document RT: if any module falls below its target for 3 consecutive weeks, create a focused triage story

### T6 — First triage decisions (AC: 7)

- [ ] Review first Stryker report
- [ ] For each surviving mutant: kill / accept / exclude per AC4
- [ ] Commit follow-up tests or exclusion config

## Dev Notes

### Critical guardrails (do not violate)

1. **Mutation testing is NOT a PR gate.** Stryker runs in minutes-to-hours; blocking PRs is cost-prohibitive. Weekly cadence is the default; on-demand for investigations. [Source: epics.md Story 0.16 + AR-048]
2. **Accepted mutants require written justification.** `docs/quality/accepted-mutants.md` is an ADR-adjacent artifact — accepted mutants are architecture decisions. [Source: AR-048 triage workflow]
3. **Target Domain only.** Mutation testing on Application/Infrastructure is too noisy (DI wiring, EF configuration, messaging setup — mostly boilerplate that mutates safely). Domain is the fintech correctness surface. [Source: AR-048 scope]
4. **2h/week budget is a ceiling, not a floor.** Overflow weeks defer to the next. Don't sacrifice sleep for a score increment.

### Source tree — files to create

```
backend/
└── stryker-config.json                                  # NEW

.github/workflows/
└── mutation-testing.yml                                 # NEW

docs/runbooks/
└── mutation-testing.md                                  # NEW (install + triage workflow)

docs/quality/
├── mutation-baseline.md                                 # NEW (first run snapshot)
├── mutation-targets.md                                  # NEW (per-module targets)
├── accepted-mutants.md                                  # NEW (documented acceptances)
└── mutation-score.json                                  # NEW (badge endpoint source)
```

### Testing standards

Meta: mutation testing IS the test-of-tests. No additional testing layer.

### Project Structure Notes

- Stryker's HTML report + JSON output land in `StrykerOutput/<timestamp>/` — this path is gitignored (committed artifacts would inflate the repo).
- Badge update mechanism is TBD — simplest is a committed JSON endpoint file; more elaborate is shields.io live integration or GitHub Pages.

### Architecture compliance

- [epics.md Story 0.16 + AR-048](../planning-artifacts/epics.md) — definition
- [architecture.md §2.9 adversarial hardening baseline](../planning-artifacts/architecture.md) — mutation testing mention

### Library/framework requirements

| Layer | Tool | Purpose |
|---|---|---|
| Mutation testing | Stryker.NET (`dotnet-stryker`) | Mutant generation + scoring |
| Badge | shields.io | Status display |

### Testing requirements

- **First run is MANDATORY before story close.** Without the first score, there's no baseline to regress against.
- **Triage decisions committed** per AC7 — the workflow is only real when it's been run at least once.

### Previous Story Intelligence

- **Story 0.1 (backend scaffold)** complete: Domain + Domain.Tests exist.
- **Story 0.15 (compound-journal)** complete: `CompoundJournalEntryBalance` is the freshest high-value mutation target.
- **Story 0.9 (CI/CD)** complete: GitHub Actions infra exists for the weekly workflow.

## References

- [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) — Story 0.16 definition
- [_bmad-output/planning-artifacts/architecture.md §2.9](../planning-artifacts/architecture.md) — adversarial hardening

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

_Populated during implementation._

### File List

_Populated during implementation — expected new/modified files per the Source Tree section above._
