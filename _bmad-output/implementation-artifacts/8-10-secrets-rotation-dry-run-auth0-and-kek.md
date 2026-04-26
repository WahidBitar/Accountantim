# Story 8.10: Secrets rotation dry-run (Auth0 + KEK)

Status: ready-for-dev

## Story

As **Wahid**,
I want **quarterly rotation of Auth0 client-secret + KEK rehearsed end-to-end in staging before first paying user**,
so that **AR-046 is validated and rotation is never attempted in production without prior rehearsal** [Source: epics.md Epic 8 Story 8.10; architecture.md AR-046].

**Covers requirements:** Epic 8 — AR-046 (secrets rotation dry-run), §7.5.8 backup/rotation gates, ADR-021 (Auth0 client-secret rotation), ADR-025 (Key Vault Standard KEK rotation) [Source: epics.md Story 8.10].

**Why this story now:** Stories 8.8 + 8.9 cover restore + legal-hold drills. This story is the third operational rehearsal. Sequenced last because rotation depends on a known-good backup state (Story 8.8) — if rotation breaks something, restore is the safety net.

## Acceptance Criteria

### AC1 — Auth0 client-secret rotation procedure

**Given** the Auth0 application has a client-secret used for backend authentication operations
**When** the rotation procedure executes against staging
**Then** the operator follows `docs/runbooks/secrets-rotation.md` Auth0 section:
1. In Auth0 Dashboard, generate a new client-secret for the Faktuboh API application
2. The OLD client-secret remains valid (Auth0 supports two active secrets during rotation)
3. Update Azure Key Vault `kv-faktuboh-staging` secret `auth0-client-secret` to the NEW value (creates new version automatically; old version retained)
4. ACA env var `AUTH0_CLIENT_SECRET` references the Key Vault secret with `secretRef` syntax — updates on next deployment OR via `az containerapp secret set`
5. Restart ACA: `az containerapp revision restart`
6. Verify: backend operational against new secret (run smoke tests)
7. **Wait 24h grace window** — old secret remains valid for outstanding token requests
8. After 24h, in Auth0 Dashboard, revoke the OLD client-secret
9. Run smoke tests again to confirm only new secret in use
**And** every step is documented with the exact CLI command in the runbook [Source: epics.md Story 8.10 AC; ADR-021].

### AC2 — Key Vault KEK rotation procedure

**Given** Key Vault Standard holds the KEK that wraps per-subject DEKs (per ADR-025)
**When** the rotation procedure executes
**Then** the operator follows `docs/runbooks/secrets-rotation.md` KEK section:
1. Create new KEK version: `az keyvault key rotate --vault-name kv-faktuboh-staging --name kek-faktuboh-master`
2. The new version becomes the latest; old version retained for decryption
3. Run rewrap job: `dotnet run --project tools/RewrapDeksWithNewKek -- --vault kv-faktuboh-staging --kek kek-faktuboh-master`
   - The job iterates every `subject_keys` row, decrypts the wrapped DEK using the OLD KEK version, re-encrypts using the NEW KEK version, updates the row + `kek_version` column
4. Verify: integration test asserts every `subject_keys` row has `kek_version = NEW`
5. **Wait 24h grace window** — old KEK version remains usable as fallback in case rewrap missed any rows
6. After 24h, mark old KEK version as `disabled` in Key Vault (not deleted — retention for audit)
7. Smoke test: encrypt + decrypt a test value via the new KEK; assert success [Source: epics.md Story 8.10 AC; ADR-025].

### AC3 — Re-wrap-DEKs tool

**Given** rewrap is the most error-prone step
**When** `tools/RewrapDeksWithNewKek/Program.cs` executes
**Then** the tool:
1. Connects to Key Vault via managed identity
2. Reads the current latest KEK version
3. Iterates `subject_keys` table in batches of 100 with cursor-based pagination
4. For each row:
   a. Reads `wrapped_dek` + `kek_version`
   b. If already on latest KEK, skip
   c. Otherwise: unwrap with old KEK version → re-wrap with new KEK version → update row with new ciphertext + new `kek_version`
   d. Each update is atomic per-row (no transaction across batches — partial progress is OK on failure)
5. Outputs progress: count processed, count skipped (already-current), count failed (with error)
6. Idempotent: running twice produces same final state
**And** integration test asserts: every `subject_keys` row post-rewrap has `kek_version = NEW`
**And** test runs the tool twice and confirms second run is fully a no-op [Source: epics.md Story 8.10 AC; design decision].

### AC4 — Subject-keys schema includes `kek_version`

**Given** the rewrap tool relies on the column
**When** the `subject_keys` table is verified
**Then** the column `kek_version text NOT NULL` exists (added by ADR-025 baseline migration; if not yet present, this story adds it via `YYYYMMDDHHMMSS_AddKekVersionColumnToSubjectKeys.cs`)
**And** the column tracks the Key Vault key version (e.g., `"v1"`, `"v2"` — actual format mirrors Key Vault's version GUID truncated for readability) [Source: ADR-025].

### AC5 — Integration test runs full suite post-rotation

**Given** rotation could break any encrypted-data path
**When** the integration test suite runs against staging post-rotation
**Then** all integration tests pass
**And** specifically: contact phone decryption, debt notes decryption, owner email decryption all succeed
**And** the result is recorded in the rehearsal runbook [Source: epics.md Story 8.10 AC integration suite].

### AC6 — Rotation runbook `docs/runbooks/secrets-rotation.md`

**Given** the runbook is the executable artifact
**When** `docs/runbooks/secrets-rotation.md` is created
**Then** it contains:

```markdown
# Secrets Rotation Runbook

## Cadence
Quarterly. Calendar reminder via GitHub Action `.github/workflows/quarterly-rotation-reminder.yml`.

## Pre-rotation checklist
- [ ] Recent restore-rehearsal flag (Story 8.8) within last 90 days
- [ ] Maintenance window scheduled
- [ ] Smoke-test suite ready

## Auth0 client-secret rotation

### Step 1: Generate new secret in Auth0 Dashboard
Navigate to Auth0 Dashboard → Applications → Faktuboh API → Settings → "Generate new secret"

### Step 2: Update Key Vault
```bash
az keyvault secret set \
  --vault-name kv-faktuboh-staging \
  --name auth0-client-secret \
  --value "<new-secret-from-auth0>"
```

### Step 3: Restart ACA
```bash
az containerapp revision restart \
  --name aca-faktuboh-api-staging \
  --resource-group rg-faktuboh-staging
```

### Step 4: Verify
```bash
curl -fsS https://api-staging.faktuboh.com/health
# Run smoke test suite
dotnet test --filter "Category=Smoke" --no-build
```

### Step 5: 24-hour grace window — DO NOT revoke old secret yet

### Step 6: After 24h, revoke old secret in Auth0 Dashboard

### Step 7: Re-verify
Re-run smoke tests after revocation to confirm only new secret in use.

## KEK rotation

### Step 1: Create new KEK version
```bash
az keyvault key rotate \
  --vault-name kv-faktuboh-staging \
  --name kek-faktuboh-master
```

### Step 2: Run rewrap job
```bash
dotnet run --project tools/RewrapDeksWithNewKek -- \
  --vault kv-faktuboh-staging \
  --kek kek-faktuboh-master
```

### Step 3: Verify rewrap completeness
```sql
SELECT kek_version, COUNT(*) FROM subject_keys GROUP BY kek_version;
-- Expected: only the new version present
```

### Step 4: Run integration test suite against staging
```bash
INTEGRATION_TEST_DB=staging dotnet test --filter "Category=Integration"
```

### Step 5: 24-hour grace window — DO NOT disable old KEK version yet

### Step 6: After 24h, disable old KEK version
```bash
az keyvault key update \
  --vault-name kv-faktuboh-staging \
  --name kek-faktuboh-master \
  --version "<old-version-id>" \
  --ops "" \
  --enabled false
```

### Step 7: Re-verify
Decrypt a test value to confirm new KEK is in use.

## Rollback procedure

If rotation breaks staging:
1. **Auth0**: revert Key Vault secret to old value (Key Vault retains versions)
2. **KEK**: re-enable old KEK version + run rewrap-back tool with `--target-version OLD`
3. Restart ACA
4. Verify health
5. Document the failure in the rehearsal runbook

## Post-rotation
- [ ] Integration test suite passes
- [ ] Smoke test suite passes
- [ ] Document rehearsal in `docs/runbooks/rotation-rehearsal-YYYY-MM-DD.md`
- [ ] Commit `docs/runbooks/rotation-rehearsal-completed-YYYY-MM-DD.flag`
- [ ] Calendar next rotation in 90 days
```

**And** the runbook is committed verbatim per AC requirement [Source: epics.md Story 8.10 AC runbook with exact commands].

### AC7 — Quarterly cadence + reminder

**Given** AR-046 specifies quarterly rotation
**When** the rehearsal completes
**Then** GitHub Action `.github/workflows/quarterly-rotation-reminder.yml` creates an issue every 90 days
**And** issue body links to the runbook + prior rotation rehearsal logs [Source: epics.md Story 8.10 AC quarterly].

### AC8 — Production rotation gate

**Given** AR-046 forbids production rotation without prior staging rehearsal
**When** the production deploy workflow attempts rotation
**Then** `scripts/check-rotation-rehearsal.sh` validates a `rotation-rehearsal-completed-*.flag` exists within last 100 days
**And** the script fails the deploy if not [Source: AR-046; design decision aligned with Story 8.8 RT-46 pattern].

### AC9 — Telemetry — `secrets_rotation_step_completed`

**Given** AR-047 telemetry primitive
**When** each rotation step completes
**Then** events emit per step:
- `secrets_rotation_step_completed` — `{ rotation_kind: "auth0" | "kek", step: "generated" | "deployed" | "verified" | "grace_window_started" | "old_revoked", elapsed_ms_bucket }`
**And** PiiGuard validates [Source: Story 0.14].

### AC10 — Rollback test

**Given** the rollback procedure must work
**When** rotation deliberately fails (simulate via tool flag `--simulate-failure`)
**Then** the rollback procedure executes per runbook
**And** post-rollback, integration test suite passes
**And** the rollback is documented in the rehearsal log [Source: design decision aligned with operational rigor].

## Tasks / Subtasks

### Backend — Subject-keys schema verification

- [ ] **T1 (AC: 4)** — Verify `kek_version` column exists on `subject_keys` table
  - [ ] If absent, create migration `YYYYMMDDHHMMSS_AddKekVersionColumnToSubjectKeys.cs`

### Rewrap tool

- [ ] **T2 (AC: 3)** — Create `tools/RewrapDeksWithNewKek/`
  - [ ] `Program.cs` per AC3 logic
  - [ ] Cursor pagination for large `subject_keys` tables
  - [ ] Idempotent + reportable progress
  - [ ] `--simulate-failure` flag for rollback testing per AC10

### Backend — Telemetry

- [ ] **T3 (AC: 9)** — Define `SecretsRotationStepCompletedTelemetry` in `Faktuboh.Application/Telemetry/Events/`
- [ ] **T4 (AC: 9)** — Wire emission via tool + runbook scripts

### Runbook

- [ ] **T5 (AC: 6)** — Create `docs/runbooks/secrets-rotation.md` per AC6 verbatim

### Quarterly reminder

- [ ] **T6 (AC: 7)** — Create `.github/workflows/quarterly-rotation-reminder.yml`
  - [ ] Cron `0 9 1 1,4,7,10 *` (1st of Jan/Apr/Jul/Oct at 09:00 UTC)
  - [ ] Creates GitHub issue

### Production gate

- [ ] **T7 (AC: 8)** — Create `scripts/check-rotation-rehearsal.sh`
  - [ ] Globs `docs/runbooks/rotation-rehearsal-completed-*.flag`
  - [ ] Validates most recent is within 100 days
  - [ ] Wired into production deploy workflow as a pre-deploy check

### First rehearsal execution

- [ ] **T8 (AC: 1, 2, 5)** — Execute first rehearsal in staging
  - [ ] Auth0 rotation per AC1 procedure
  - [ ] KEK rotation per AC2 procedure
  - [ ] Document in `docs/runbooks/rotation-rehearsal-YYYY-MM-DD.md`
  - [ ] Commit `docs/runbooks/rotation-rehearsal-completed-YYYY-MM-DD.flag`

### Tests — Backend

- [ ] **T9 (AC: 3)** — Integration test for `RewrapDeksWithNewKek` tool
  - [ ] Seed 50 `subject_keys` rows with `kek_version=v1`
  - [ ] Run tool with target version `v2`
  - [ ] Assert all 50 rows updated to `kek_version=v2`
  - [ ] Run again — assert no-op (idempotent)
- [ ] **T10 (AC: 5)** — Post-rotation full integration suite gate
  - [ ] CI step that runs `dotnet test --filter "Category=Integration"` after rotation events
- [ ] **T11 (AC: 10)** — Rollback test with `--simulate-failure` flag
  - [ ] Run rotation with simulated failure → assert tool reports error
  - [ ] Run rollback procedure → assert state restored

## Dev Notes

### Critical guardrails (do not violate)

1. **24-hour grace window between new-secret-active and old-secret-revoked.** Without it, in-flight tokens issued under the old secret could fail mid-request.
2. **Rewrap tool is idempotent.** Running twice produces same state. Without this, partial-progress retries could corrupt data.
3. **Old KEK version is DISABLED, not DELETED.** Audit + recovery require the old version remain accessible.
4. **Production rotation gated by recent staging rehearsal.** AR-046 hard requirement. The CI check enforces this.
5. **Per-row atomic updates** in the rewrap tool. No batch transactions — partial progress is OK on failure.
6. **Integration test suite full pass post-rotation.** Without this, encrypted-field paths could regress silently.
7. **Quarterly cadence per AR-046.** Reminder via GitHub Action; institutional memory across job changes.
8. **The runbook is the contract.** Every step has the exact CLI command. Without verbatim commands, ad-hoc execution leads to mistakes.
9. **Rollback procedure must be tested.** A rehearsed rollback is the safety net for production rotation that goes wrong.
10. **Auth0 + KEK rotated in same procedure.** Both depend on Key Vault; procedural tie keeps cadence aligned.
11. **No production secrets in this repo.** The runbook references `<new-secret-from-auth0>` placeholder; actual values via Azure Portal + CLI only.

### Source tree — files to create or touch

```
backend/
├── src/
│   ├── Faktuboh.Application/Telemetry/Events/
│   │   └── SecretsRotationStepCompletedTelemetry.cs            # NEW
│   └── Faktuboh.Infrastructure/Persistence/Migrations/
│       └── YYYYMMDDHHMMSS_AddKekVersionColumnToSubjectKeys.cs  # NEW (if not present)
└── tests/Faktuboh.Api.Tests/Tools/
    └── RewrapDeksWithNewKekTests.cs                            # NEW (Integration)

tools/
└── RewrapDeksWithNewKek/
    ├── RewrapDeksWithNewKek.csproj                             # NEW
    └── Program.cs                                              # NEW

scripts/
└── check-rotation-rehearsal.sh                                 # NEW

docs/runbooks/
├── secrets-rotation.md                                         # NEW
├── rotation-rehearsal-YYYY-MM-DD.md                            # NEW (per execution)
└── rotation-rehearsal-completed-YYYY-MM-DD.flag                # NEW

.github/workflows/
└── quarterly-rotation-reminder.yml                             # NEW
```

### Testing standards

| Level | Tools |
|---|---|
| Unit | xUnit v3 (rewrap tool logic) |
| Integration | Testcontainers Postgres + mocked Key Vault |
| Operational | Manual rehearsal execution against staging |

### Project Structure Notes

This story adds a third tool to `tools/` (joining 8.8's seeder + replay + KMS validator). The `scripts/` directory accumulates more pre-deploy gates (8.8's RT-46 check + this story's rotation check).

### Architecture compliance

- [epics.md Epic 8 Story 8.10](../planning-artifacts/epics.md)
- [architecture.md AR-046, §7.5.8](../planning-artifacts/architecture.md)
- [adr-021 Auth0](../planning-artifacts/adrs/adr-021-auth0-identity-provider.md)
- [adr-025 Key Vault Standard](../planning-artifacts/adrs/adr-025-key-vault-standard.md)
- [Story 0.10 Azure infra](./0-10-azure-infrastructure-provisioned.md)
- [Story 8.8 restore drill (prerequisite)](./8-8-restore-under-pressure-tabletop-rehearsal.md)

### Library / framework requirements

| Layer | Package | Notes |
|---|---|---|
| Tools | Azure.Identity + Azure.Security.KeyVault.Keys | Rewrap tool |
| Scripts | Azure CLI | Rotation commands |

### Testing requirements

- **Rewrap idempotency** — without it, rerun-on-failure could double-process or corrupt.
- **Rollback test** — without it, the safety net is theoretical.
- **Post-rotation integration suite** — without it, encrypted-field regressions ship silently.

### Previous Story Intelligence

**Prerequisites:**
- Story 0.10 — Azure infra (Key Vault + ACA)
- Story 0.6 — bitemporal interceptor (data shape stability)
- Story 8.8 — restore drill (rotation safety net)

**Related stories:**
- Story 8.11 — load test (executed after rotation drill to validate rotated system under load)

## References

- [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) — Epic 8 Story 8.10
- [_bmad-output/planning-artifacts/architecture.md AR-046](../planning-artifacts/architecture.md)
- [_bmad-output/planning-artifacts/adrs/adr-021-auth0-identity-provider.md](../planning-artifacts/adrs/adr-021-auth0-identity-provider.md)
- [_bmad-output/planning-artifacts/adrs/adr-025-key-vault-standard.md](../planning-artifacts/adrs/adr-025-key-vault-standard.md)
- [Story 8.8](./8-8-restore-under-pressure-tabletop-rehearsal.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

_Populated during implementation._

### File List

_Populated during implementation._
