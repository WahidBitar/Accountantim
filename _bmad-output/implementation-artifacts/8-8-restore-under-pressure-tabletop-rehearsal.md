# Story 8.8: Restore-under-pressure tabletop rehearsal

Status: ready-for-dev

## Story

As **Wahid**,
I want **a rehearsed Postgres point-in-time-restore from a 30-day-old backup with KMS-encrypted field round-trip validation**,
so that **AR-044 #1 is executable before first paying user (RT-46 hard gate)** [Source: epics.md Epic 8 Story 8.8; architecture.md AR-044, RT-46].

**Covers requirements:** Epic 8 — AR-044 #1 (restore-under-pressure), RT-46 (operational rehearsal hard gate before first paying user), §7.5.7 ops-readiness deferral, §7.5.8 backup/rotation gates [Source: epics.md Story 8.8; architecture.md §7.5.7, §7.5.8].

**Why this story now:** Stories 8.1–8.7 establish admin + ops surfaces. This story is the first of three operational-drill stories (8.8, 8.9, 8.10) that cannot be tested in development — they require real production-shaped data + real Azure resources. Sequenced before 8.10 (rotation) because restore-from-backup is the foundational disaster-recovery rehearsal.

## Acceptance Criteria

### AC1 — Staging environment provisioned to mirror production

**Given** the rehearsal must execute against production-shaped infra
**When** the staging environment is provisioned per Story 0.10 + verified
**Then** the staging Postgres Flexible Server is in Germany West Central (matches production region per §4.6 D4.18)
**And** automated backups are enabled with 30-day retention per AR-044 + Azure default
**And** Key Vault Standard with KEK + per-subject DEK schema mirrors production (Story 4.1 + ADR-025)
**And** Auth0 staging tenant mirrors production tenant config [Source: architecture.md §4.6 D4.18; Story 0.10].

### AC2 — Synthetic production-shaped data seeded

**Given** real PII can never be used in staging
**When** synthetic data is seeded via `tests/SyntheticData/SeedRehearsalDataset.cs`
**Then** the seed creates:
- 200 user accounts (synthetic Auth0 sub IDs + opaque emails)
- 1,000 contacts distributed across users (avg 5 per user)
- 5,000 journal entries
- 100 capability tokens (mix of active, expired, revoked)
- 50 statement confirmations
- 20 audit-log entries from prior admin actions
- 5 grandfathering rules (mix of permanent + grace)
- 2 active account suspensions
**And** the dataset includes data older than 30 days (so the backup retention boundary is exercised)
**And** the dataset includes encrypted fields populated via the KMS pipeline (verifies round-trip on restore) [Source: design decision aligned with AR-044].

### AC3 — Anonymization log rehearsal seed

**Given** the restore validates that previously-erased PII does not reintroduce
**When** the rehearsal seeds anonymization log entries
**Then** 10 user accounts are created + then "erased" via the GDPR erasure flow (ADR-004)
**And** the erasure entries are recorded in `anonymization_log` table (schema established in Epic 1 Story 1.7)
**And** the rehearsal will assert: post-restore, the 10 erased users' PII is still null (key-shredded data cannot resurrect even from old backup) [Source: Story 1.7 GDPR erasure; ADR-004].

### AC4 — Point-in-time restore execution

**Given** the rehearsal is scheduled in a maintenance window
**When** the operator executes the restore per the runbook
**Then** the operator runs:
```bash
# Choose target restore time = 30 days ago
RESTORE_TIME=$(date -u -d '30 days ago' +%Y-%m-%dT%H:%M:%SZ)

az postgres flexible-server restore \
  --resource-group rg-faktuboh-staging-restore \
  --name pg-faktuboh-staging-restored \
  --source-server pg-faktuboh-staging \
  --restore-time "$RESTORE_TIME" \
  --location germanywestcentral
```
**And** the restore completes within Azure's documented RTO (< 1h for a small instance)
**And** the restored DB is on a NEW server name (`pg-faktuboh-staging-restored`) — original is untouched
**And** the actual elapsed wall-clock time is recorded in the rehearsal log [Source: epics.md Story 8.8 AC; Azure Postgres Flexible Server PITR documentation].

### AC5 — Anonymization log replay as step zero

**Given** the restored DB contains the 10 "erased" users' pre-erasure PII (because the backup is from before erasure)
**When** the post-restore validation runs
**Then** a step-zero job executes: `dotnet run --project tools/AnonymizationLogReplay --connection "..."`
**And** the job reads `anonymization_log` from the restored DB + re-applies key-shred for every user marked erased
**And** post-replay, the 10 erased users' PII fields are NULL/encrypted-with-shredded-key
**And** an integration assertion verifies: SELECT name FROM users WHERE subject_id IN (...) → all NULL [Source: epics.md Story 8.8 AC anonymization replay; ADR-004].

### AC6 — KMS-encrypted field round-trip validation

**Given** per-subject DEK rotation must survive restore
**When** post-restore + post-anonymization replay
**Then** for a sample of 10 non-erased users, decrypt + re-read encrypted fields:
- Contact phone (encrypted at rest per ADR-004)
- Debt notes (encrypted)
- Owner email (encrypted)
**And** decryption succeeds via the still-active KEK in Key Vault Standard
**And** for 1 user whose DEK was rotated post-backup, decryption uses the historical DEK version (Key Vault retains old versions per ADR-025)
**And** an integration test asserts a sample-row decrypt round-trip succeeds [Source: epics.md Story 8.8 AC; ADR-025].

### AC7 — Full integration test suite passes against restored DB

**Given** the restored DB must function as a normal Faktuboh DB
**When** the full integration test suite is pointed at the restored DB
**Then** all integration tests pass (or document any expected failures due to time-travel — e.g., tests that depend on "now" relative to data ages)
**And** any failures are categorized as:
- "Real" failure (post-restore data corruption — must investigate)
- "Expected" failure (test depends on relative age — acceptable)
**And** the categorization is recorded in the rehearsal log [Source: epics.md Story 8.8 AC integration suite].

### AC8 — Rehearsal documented in dated runbook

**Given** the rehearsal outcome must be auditable
**When** the rehearsal completes
**Then** `docs/runbooks/restore-rehearsal-YYYY-MM-DD.md` is created (one file per rehearsal date)
**And** the runbook documents:
1. Date + operator name (Wahid)
2. Source backup time
3. Restore start + end timestamps + elapsed
4. Steps executed (verbatim CLI commands)
5. Anonymization-replay outcome (count erased users + decrypt-pii test)
6. KMS round-trip outcome (sample size + success/failure)
7. Integration test results (passed/failed/categorized)
8. Findings + action items (any procedural gaps discovered)
**And** the runbook template lives at `docs/runbooks/restore-rehearsal-template.md` to ensure consistent format across quarterly rehearsals [Source: epics.md Story 8.8 AC documentation].

### AC9 — Quarterly cadence + reminder

**Given** AR-044 #1 specifies quarterly rehearsals
**When** the rehearsal completes
**Then** a calendar reminder is set for 3 months out (operator: Wahid; mechanism: GitHub-issue auto-create via scheduled GitHub Action OR personal calendar — choose: GitHub-issue auto-create for institutional memory)
**And** the GitHub Action `.github/workflows/quarterly-rehearsal-reminder.yml` runs on 1st of every Jan/Apr/Jul/Oct + creates an issue titled "Restore-under-pressure rehearsal due"
**And** the issue links to the runbook template + last 4 rehearsal logs [Source: epics.md Story 8.8 AC quarterly].

### AC10 — RT-46 hard-gate flag

**Given** RT-46 specifies this rehearsal is a hard gate before first paying user
**When** the first rehearsal completes successfully
**Then** a marker file `docs/runbooks/restore-rehearsal-completed-YYYY-MM-DD.flag` is committed
**And** Story 8.6's enforcement-enable + any payment integration story (post-MVP) MUST verify this flag exists before activating
**And** a CI gate `scripts/check-rt46-flag.sh` warns if `enforcement_enabled=true` is being deployed without the flag — fails the deploy [Source: architecture.md RT-46].

## Tasks / Subtasks

### Infrastructure — Staging env

- [ ] **T1 (AC: 1)** — Verify staging Postgres Flexible Server config matches production
  - [ ] Region: germanywestcentral
  - [ ] Backup retention: 30 days
  - [ ] Key Vault: per-subject DEK schema active

### Synthetic data seeding

- [ ] **T2 (AC: 2)** — Create `tools/SeedRehearsalDataset/` console app
  - [ ] Seeds 200 users + 1000 contacts + 5000 entries + 100 tokens + 50 confirmations
  - [ ] Distributes data ages: 25% < 7 days, 50% 7-60 days, 25% > 60 days
  - [ ] Encrypts fields via the production-equivalent KMS pipeline
  - [ ] Run via `dotnet run --project tools/SeedRehearsalDataset -- --connection "..."`
- [ ] **T3 (AC: 3)** — Extend seed to include 10 erased users + corresponding `anonymization_log` rows

### Anonymization replay tool

- [ ] **T4 (AC: 5)** — Create `tools/AnonymizationLogReplay/` console app
  - [ ] Reads `anonymization_log` rows + re-applies key-shred for each marked-erased user
  - [ ] Idempotent — running twice produces same state
  - [ ] Output report: count erased + count successful + count failed (with reason)

### Restore execution scripts

- [ ] **T5 (AC: 4)** — Create `scripts/restore-rehearsal.sh`
  - [ ] Computes 30-day-ago timestamp
  - [ ] Runs `az postgres flexible-server restore` with parameters from env vars
  - [ ] Records start + end timestamps to a log file
  - [ ] Validates restored server is reachable

### KMS round-trip test

- [ ] **T6 (AC: 6)** — Create `tools/KmsRoundTripValidation/` console app
  - [ ] Connects to restored DB
  - [ ] Selects sample 10 non-erased users
  - [ ] For each, decrypts contact phone + debt notes + owner email via Key Vault
  - [ ] Records success/failure per field
  - [ ] Includes 1 user with DEK rotation: explicitly tests historical DEK version retrieval

### Integration test suite execution

- [ ] **T7 (AC: 7)** — Create `scripts/run-integration-suite-against-restored.sh`
  - [ ] Sets `INTEGRATION_TEST_DB_CONNECTION` to restored DB
  - [ ] Runs `dotnet test --filter "Category=Integration" --logger "trx;LogFileName=restore-suite.trx"`
  - [ ] Parses results + categorizes failures (real vs expected-due-to-time-travel)

### Runbook

- [ ] **T8 (AC: 8)** — Create `docs/runbooks/restore-rehearsal-template.md`
  - [ ] Template with 8 sections from AC8
  - [ ] Placeholder fields for date, timestamps, outcomes
- [ ] **T9 (AC: 8)** — Execute first rehearsal + commit `docs/runbooks/restore-rehearsal-2026-04-26.md` (or actual date)

### Quarterly reminder automation

- [ ] **T10 (AC: 9)** — Create `.github/workflows/quarterly-rehearsal-reminder.yml`
  - [ ] Cron `0 9 1 1,4,7,10 *` (1st of Jan/Apr/Jul/Oct at 09:00 UTC)
  - [ ] Uses `actions/github-script` to create an issue titled "Restore-under-pressure rehearsal due"
  - [ ] Body links to template + lists last 4 rehearsal logs (via glob)

### RT-46 flag + CI gate

- [ ] **T11 (AC: 10)** — Create `docs/runbooks/restore-rehearsal-completed-YYYY-MM-DD.flag` after first successful rehearsal
- [ ] **T12 (AC: 10)** — Create `scripts/check-rt46-flag.sh`
  - [ ] Reads `freemium_config.enforcement_enabled` from current deploy target
  - [ ] If `enforcement_enabled=true`, checks for `docs/runbooks/restore-rehearsal-completed-*.flag` files within last 90 days
  - [ ] Fails with non-zero exit if no recent flag found
  - [ ] Wired into GitHub Actions deploy workflow (Story 0.9)

### Tests

- [ ] **T13 (AC: 5)** — Unit test for `AnonymizationLogReplay` idempotency
- [ ] **T14 (AC: 6)** — Unit test for `KmsRoundTripValidation` historical-DEK retrieval

## Dev Notes

### Critical guardrails (do not violate)

1. **Real production data NEVER seeded into staging.** Only synthetic. Even staging with real PII is a compliance gap.
2. **Restore to a NEW server name.** Never restore over the original — no recovery if PITR target is wrong.
3. **Anonymization log replay is step zero.** Without it, the restore reintroduces PII that was previously erased. GDPR violation.
4. **KMS round-trip with HISTORICAL DEK version.** Without testing this, you discover post-incident that a rotation broke historic-data decryption.
5. **Quarterly cadence is non-negotiable per AR-044.** GitHub-issue auto-create gives institutional memory across job changes.
6. **RT-46 hard-gate flag.** Without it, enforcement_enabled or paying users could ship without proven recovery capability.
7. **Document the actual elapsed time.** RTO compliance is verified by measurement, not assumption.
8. **Runbook is live + dated.** A template that's never executed is theater. Each rehearsal produces a new dated file.
9. **Categorize integration test failures.** Time-travel failures (e.g., "this token expired in the restored DB because real time advanced") are expected; data-corruption failures are not.
10. **Maintenance window.** Rehearsal runs in a window when no real users are interacting with staging — coordinate via Wahid's calendar.

### Source tree — files to create or touch

```
tools/
├── SeedRehearsalDataset/
│   ├── SeedRehearsalDataset.csproj                             # NEW
│   ├── Program.cs                                              # NEW
│   └── Seeders/                                                # NEW (subfolders per entity)
├── AnonymizationLogReplay/
│   ├── AnonymizationLogReplay.csproj                           # NEW
│   └── Program.cs                                              # NEW
└── KmsRoundTripValidation/
    ├── KmsRoundTripValidation.csproj                           # NEW
    └── Program.cs                                              # NEW

scripts/
├── restore-rehearsal.sh                                        # NEW
├── run-integration-suite-against-restored.sh                   # NEW
└── check-rt46-flag.sh                                          # NEW

docs/runbooks/
├── restore-rehearsal-template.md                               # NEW
├── restore-rehearsal-YYYY-MM-DD.md                             # NEW (per execution)
└── restore-rehearsal-completed-YYYY-MM-DD.flag                 # NEW (after first success)

.github/workflows/
└── quarterly-rehearsal-reminder.yml                            # NEW
```

### Testing standards

| Level | Tools |
|---|---|
| Unit | xUnit v3 (anonymization-replay idempotency, KMS round-trip) |
| Operational | Manual rehearsal execution against staging |
| Integration | Full backend suite re-run against restored DB |

### Project Structure Notes

This story creates a new top-level `tools/` directory for ops-only console apps (seeder, replay, KMS validator). These tools are NOT part of the production deploy pipeline; they're developer tools. The `scripts/` directory holds shell automation. The `docs/runbooks/` directory accumulates dated rehearsal logs over time.

### Architecture compliance

- [epics.md Epic 8 Story 8.8](../planning-artifacts/epics.md)
- [architecture.md AR-044, RT-46, §4.6 D4.18, §7.5.7, §7.5.8](../planning-artifacts/architecture.md)
- [adr-004 compliance posture](../planning-artifacts/adrs/adr-004-day-one-compliance-posture.md)
- [adr-025 Key Vault Standard](../planning-artifacts/adrs/adr-025-key-vault-standard.md)
- [Story 0.10 Azure infra](./0-10-azure-infrastructure-provisioned.md)
- [Story 1.7 GDPR erasure](./1-7-account-deletion-gdpr-erasure.md)

### Library / framework requirements

| Layer | Package | Notes |
|---|---|---|
| Tools | Azure.Identity | KMS validator authenticates via managed identity |
| Tools | Azure.Security.KeyVault.Keys | KMS round-trip |
| Tools | Npgsql | Direct DB access for tools |
| Scripts | Azure CLI (az postgres) | PITR command |

### Testing requirements

- **First rehearsal must produce a dated runbook file committed to the repo.** Without it, AC compliance is unverifiable.
- **Anonymization replay idempotency** — without test, double-replay could corrupt state.
- **Historical DEK retrieval** — without test, KMS rotation breaks restore silently.
- **Integration suite categorization** — without rigor here, "all tests passed" claims hide real corruption.

### Previous Story Intelligence

**Prerequisites:**
- Story 0.10 — Azure infra (Postgres Flexible + Key Vault)
- Story 0.6 — bitemporal interceptor (data shape)
- Story 1.7 — GDPR erasure flow + `anonymization_log` table

**Related stories:**
- Story 8.10 — secrets rotation drill (executed AFTER this; rotation needs proven restore)

## References

- [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) — Epic 8 Story 8.8
- [_bmad-output/planning-artifacts/architecture.md AR-044, RT-46](../planning-artifacts/architecture.md)
- [_bmad-output/planning-artifacts/adrs/adr-004-day-one-compliance-posture.md](../planning-artifacts/adrs/adr-004-day-one-compliance-posture.md)
- [_bmad-output/planning-artifacts/adrs/adr-025-key-vault-standard.md](../planning-artifacts/adrs/adr-025-key-vault-standard.md)
- [Story 0.10](./0-10-azure-infrastructure-provisioned.md)
- [Story 1.7](./1-7-account-deletion-gdpr-erasure.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

_Populated during implementation._

### File List

_Populated during implementation._
