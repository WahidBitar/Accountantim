# Story 0.9: GitHub Actions CI/CD with OIDC

Status: ready-for-dev

## Story

As a **developer**,
I want **three GitHub Actions workflows (`ci.yml` on PRs, `deploy-staging.yml` on main merge, `deploy-prod.yml` on tag with required-reviewer + 10-min wait timer) using OIDC federated credentials with SHA-pinned actions**,
so that **no long-lived Azure credentials exist in GitHub and deploys have a pre-agreed safety gate (D4.17)**.

**Covers requirements:** Epic 0 Platform Foundation — hard gate. Establishes the CI/CD substrate every subsequent Epic 0/1/2/3 story depends on for required checks (NetArchTest, bundle-graph, schema-audit, zoneless, NFR-A9 RTL harness).

**Why this story now:** Per architecture §4.7, CI/CD is step 4 (after region+vault, database, identity). Story 0.10 (Azure infra) uses the OIDC credential this story establishes; stories 0.11+ register required CI checks into the `ci.yml` pipeline this story creates.

## Acceptance Criteria

### AC1 — Three workflows exist at expected paths

**Given** `.github/workflows/` is the GitHub Actions workflow directory
**When** the directory is inspected
**Then** three files exist:
- `.github/workflows/ci.yml` — triggered on `pull_request` + `push` to `main`
- `.github/workflows/deploy-staging.yml` — triggered on `push` to `main`
- `.github/workflows/deploy-prod.yml` — triggered on tag push matching `v*.*.*`
**And** `ci.yml` does NOT call `azd deploy` anywhere (PRs never deploy) [Source: architecture.md §4 D4.17]

### AC2 — `ci.yml` runs the required-check matrix

**Given** a PR is opened or updated
**When** `ci.yml` executes
**Then** the workflow runs (in separate jobs, in parallel where independent):
- **backend-build**: `dotnet restore`, `dotnet build --configuration Release`, `dotnet format --verify-no-changes`
- **backend-unit-tests**: `dotnet test --filter "Category!=Integration" --configuration Release`
- **backend-integration-tests**: `dotnet test --filter "Category=Integration" --configuration Release` (uses Testcontainers Postgres; runs on `ubuntu-latest` with Docker available)
- **backend-architecture-tests**: runs `Faktuboh.ArchitectureTests` (NetArchTest fitness tests #1 + #3 per Story 0.8)
- **backend-schema-audit**: runs `tests/Faktuboh.Infrastructure.Tests/SchemaAudit.cs` (asserts naming conventions post-migration)
- **frontend-install**: `npm ci --prefix frontend`
- **frontend-lint**: `npm run lint --prefix frontend` (includes ESLint boundary rules from Story 0.2)
- **frontend-unit-tests**: `npm run test:ci --prefix frontend`
- **frontend-build**: `npm run build --prefix frontend` (fails on bundle-budget violation per Story 0.2 AC8)
- **frontend-bundle-graph-gate**: runs `frontend/scripts/verify-public-statement-bundle.ts` (Story 0.2 AC3)
- **frontend-zoneless-gate**: runs `frontend/scripts/verify-zoneless.ts` (Story 0.2 AC5)
- **contracts-drift-gate**: regenerates via NSwag + `git diff --exit-code frontend/libs/api-contracts/` (fails if contracts are stale)
**And** all jobs are required checks configured via branch protection on `main` [Source: architecture.md §4 D4.17 point 1 + §6.5.2]

### AC3 — Playwright matrix runs on affected projects

**Given** `ci.yml` includes a Playwright job
**When** a PR touches files under `apps/*/src/**` OR `libs/**` OR `backend/src/**`
**Then** the Playwright job runs Chromium + WebKit against affected apps (owner-workspace + public-statement)
**And** browsers are cached via `actions/cache` keyed on `package-lock.json`
**And** a PR that touches only `docs/` or `_bmad-output/` skips the Playwright job (path filter) [Source: architecture.md §4 D4.17 point 1]

### AC4 — OIDC federated credential + Entra ID workload identity

**Given** an Entra ID app registration exists with federated credentials
**When** `deploy-staging.yml` or `deploy-prod.yml` executes
**Then** each workflow step that calls Azure uses `azure/login@<sha>` with `client-id`, `tenant-id`, `subscription-id` inputs (no `client-secret`)
**And** the federated credential subject matches the expected GitHub repo + environment:
- staging: `repo:<owner>/<repo>:environment:staging`
- prod: `repo:<owner>/<repo>:environment:production`
**And** GitHub Secrets contains only `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` (no `AZURE_CLIENT_SECRET`) [Source: architecture.md §4 D4.17 point 5]
**And** `docs/runbooks/ci-cd-setup.md` documents the federated credential registration procedure

### AC5 — SHA-pinned actions (no tag pinning)

**Given** any `uses:` clause in any workflow
**When** the workflow YAML is inspected
**Then** every third-party action reference is pinned to a 40-char commit SHA (not `@v4`, not `@main`):

```yaml
# Correct:
- uses: actions/checkout@11bd71901bbe5b1630ceea73d27597364c9af683   # v4.2.2
# Forbidden:
- uses: actions/checkout@v4
- uses: actions/checkout@main
```

**And** each SHA has a trailing comment with the tag equivalent (for human readability)
**And** Dependabot is configured to update action SHAs (`.github/dependabot.yml` includes `package-ecosystem: "github-actions"`) [Source: architecture.md §4 D4.17 point 3]

### AC6 — `deploy-staging.yml` deploys via `azd` to staging ACA on main merge

**Given** a PR merges to `main`
**When** `deploy-staging.yml` runs
**Then** the workflow checks out, installs azd, runs `azd deploy --no-prompt --environment staging`
**And** the deploy targets the staging ACA environment provisioned in Story 0.10
**And** no approval gate — staging is a fast-deploy surface [Source: architecture.md §4 D4.17 point 1]
**And** the workflow fails if `azd deploy` returns non-zero

### AC7 — `deploy-prod.yml` requires reviewer + 10-min wait timer

**Given** a git tag matching `v*.*.*` is pushed
**When** `deploy-prod.yml` triggers
**Then** the workflow uses the `production` GitHub Environment which has:
- Required reviewer: `wahid-bitar` (or the org admin team)
- Wait timer: `10 minutes` (GitHub Environment protection rule)
**And** the environment protection rules are documented in `docs/runbooks/ci-cd-setup.md`
**And** the workflow step to deploy is gated by `environment: production` in the job spec:

```yaml
deploy-prod:
  runs-on: ubuntu-latest
  environment:
    name: production
    url: https://app.faktuboh.com
  permissions:
    id-token: write
    contents: read
  steps:
    - uses: actions/checkout@<sha>
    - uses: azure/login@<sha>
      with:
        client-id: ${{ secrets.AZURE_CLIENT_ID }}
        tenant-id: ${{ secrets.AZURE_TENANT_ID }}
        subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}
    - run: azd deploy --no-prompt --environment production
```

**And** even with a solo-dev (Wahid reviewing own PR), the 10-minute timer still catches impulsive deploys [Source: architecture.md §4 D4.17 point 1]

### AC8 — Build-once promotion (same image staging → prod)

**Given** `deploy-staging.yml` produces a container image tagged with the commit SHA
**When** `deploy-prod.yml` deploys that same SHA
**Then** the container image is pulled from the registry — not rebuilt
**And** `deploy-prod.yml` explicitly references the image digest from the staging deploy (recorded via GitHub Deployments API or a workflow artifact)
**And** ACA revision promotion uses `az containerapp revision copy` or equivalent `azd` flow to roll the staging image into prod [Source: architecture.md §4 D4.17 point 2]

### AC9 — Container registry configured

**Given** the cost-floor check chose GitHub Container Registry (`ghcr.io`) over Azure Container Registry (§7.4.1 item 5)
**When** `deploy-staging.yml` pushes an image
**Then** the image target is `ghcr.io/<owner>/faktuboh-api:<sha>`
**And** authentication to GHCR uses the built-in `GITHUB_TOKEN` with `packages: write` permission (no separate credential)
**And** ACA pulls from GHCR using an ACR-compatible pull secret configured in Story 0.10 [Source: architecture.md §7.4.1 item 5]

### AC10 — CI time budget

**Given** a typical PR with backend + frontend changes
**When** `ci.yml` runs end-to-end
**Then** the pipeline completes in under 8 minutes at p50 and under 12 minutes at p95
**And** a ratcheting CI-time trend check logs the wall-clock duration to App Insights (wired in Story 0.11)
**And** RT-31 (PR CI time > 12 min p95 triggers E2E split) is documented in `docs/runbooks/ci-cd-setup.md` [Source: architecture.md §6.5.13 RT-31]

## Tasks / Subtasks

### T1 — Register Entra ID app + federated credentials (AC: 4)

- [ ] In Azure Portal → Entra ID → App registrations → New registration `faktuboh-github-oidc`
- [ ] Add Federated Credential (subject `repo:<owner>/faktuboh:environment:staging`, issuer `https://token.actions.githubusercontent.com`)
- [ ] Add second Federated Credential for `repo:<owner>/faktuboh:environment:production`
- [ ] Grant app registration `Contributor` role on the target Azure subscription (scope to resource group in Story 0.10)
- [ ] Record `client-id`, `tenant-id`, `subscription-id` → add to GitHub Secrets (repo-level + environment-scoped where applicable)
- [ ] Document the exact subject format + scope minimization in `docs/runbooks/ci-cd-setup.md`

### T2 — Create GitHub Environments with protection rules (AC: 7)

- [ ] GitHub repo → Settings → Environments → Create `staging` (no protection rules)
- [ ] Create `production` with:
  - Required reviewer: `wahid-bitar`
  - Wait timer: 10 minutes
  - Deployment branches: `v*.*.*` tags only
- [ ] Store `AZURE_CLIENT_ID` + `AZURE_TENANT_ID` + `AZURE_SUBSCRIPTION_ID` as environment-scoped secrets on both environments

### T3 — Write `.github/workflows/ci.yml` (AC: 1, 2, 3, 5, 10)

- [ ] Create job matrix per AC2 (backend-build, backend-unit-tests, backend-integration-tests, backend-architecture-tests, backend-schema-audit, frontend-install, frontend-lint, frontend-unit-tests, frontend-build, frontend-bundle-graph-gate, frontend-zoneless-gate, contracts-drift-gate, e2e-playwright)
- [ ] Use `actions/checkout@<sha>`, `actions/setup-dotnet@<sha>` (version `10.0.100`), `actions/setup-node@<sha>` (version `22`)
- [ ] Cache `~/.nuget/packages` keyed on `packages.lock.json`; cache `node_modules` keyed on `package-lock.json`; cache Playwright browsers keyed on `package-lock.json`
- [ ] Add path filters so docs-only PRs skip expensive jobs
- [ ] Pin every action to a SHA (not tag) per AC5

### T4 — Write `.github/workflows/deploy-staging.yml` (AC: 6, 9)

- [ ] Trigger on `push` to `main`
- [ ] Concurrency group: `deploy-staging-${{ github.ref }}` with `cancel-in-progress: false` (don't cancel mid-deploy)
- [ ] Steps: checkout → azure/login via OIDC → azd install → `azd deploy --environment staging --no-prompt`
- [ ] Push container image to `ghcr.io/<owner>/faktuboh-api:${{ github.sha }}` + tag `ghcr.io/<owner>/faktuboh-api:staging-latest`
- [ ] Emit a GitHub Deployment (via `actions/github-script@<sha>`) recording the image digest for AC8

### T5 — Write `.github/workflows/deploy-prod.yml` (AC: 7, 8)

- [ ] Trigger on tag push `v*.*.*`
- [ ] Assert the tagged commit is reachable from `main` (guard against tag-a-branch deploys)
- [ ] Job specifies `environment: production`
- [ ] Steps: checkout → azure/login via OIDC → resolve image digest from staging's GitHub Deployment → `az containerapp revision copy --image <digest>` OR `azd deploy --no-prompt --environment production` (prefer the former to enforce image reuse)
- [ ] Post-deploy: emit GitHub Release via `gh release create` with auto-generated notes

### T6 — Dependabot config (AC: 5)

- [ ] Create `.github/dependabot.yml` with three ecosystems:
  - `github-actions` weekly
  - `nuget` (directory: `/backend`) weekly
  - `npm` (directory: `/frontend`) weekly
- [ ] Label PRs with `dependencies`

### T7 — NSwag contracts-drift CI step (AC: 2)

- [ ] Add job `contracts-drift-gate` in `ci.yml`:
  ```yaml
  contracts-drift-gate:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@<sha>
      - uses: actions/setup-dotnet@<sha>
        with: { dotnet-version: '10.0.100' }
      - uses: actions/setup-node@<sha>
        with: { node-version: 22 }
      - run: dotnet run --project backend/src/Faktuboh.Api -- --generate-openapi-doc > backend/artifacts/openapi.json
      - run: npx --package=nswag nswag run backend/tools/NSwag/nswag.json
      - run: git diff --exit-code frontend/libs/api-contracts/ || (echo "contracts drifted — regenerate locally and commit" && exit 1)
  ```

### T8 — Runbook (AC: 4, 7, 10)

- [ ] Create `docs/runbooks/ci-cd-setup.md` covering:
  - Entra ID app registration + federated credential subject format
  - GitHub Environments setup (staging, production, required reviewers, wait timer)
  - SHA-pinning policy + Dependabot
  - RT-31 threshold for splitting E2E suite (PR CI p95 > 12 min)

## Dev Notes

### Critical guardrails (do not violate)

1. **No long-lived Azure credentials in GitHub Secrets.** Only `AZURE_CLIENT_ID` + `AZURE_TENANT_ID` + `AZURE_SUBSCRIPTION_ID`. A `AZURE_CLIENT_SECRET` is forbidden per ADR-021-adjacent OIDC policy. [Source: architecture.md §4 D4.17 point 5]
2. **SHA-pin every action.** `@v4` is a moving target — `tj-actions/changed-files` supply-chain incident (March 2025) is the canonical example. [Source: architecture.md §4 D4.17 point 3]
3. **Build once, promote** — the image deployed to prod MUST be the same SHA-tagged image built for staging. `az containerapp revision copy` or equivalent; never rebuild. [Source: architecture.md §4 D4.17 point 2]
4. **10-minute wait timer applies even to Wahid.** Solo-dev does not bypass the timer. The timer is anti-impulsive-deploy, not anti-external-attacker. [Source: architecture.md §4 D4.17 point 1]
5. **No preview environments at MVP.** Revisit RT when >1 collaborator OR UX change needs staging design review. Do not prematurely wire `ng serve`-per-PR. [Source: architecture.md §4 D4.17 point 4]
6. **`permissions:` block on every workflow** uses least-privilege (`id-token: write`, `contents: read`, `packages: write` only where needed). Default-permissive permissions is forbidden.

### Source tree — files to create

```
.github/
├── workflows/
│   ├── ci.yml                         # NEW
│   ├── deploy-staging.yml             # NEW
│   └── deploy-prod.yml                # NEW
└── dependabot.yml                     # NEW

docs/
└── runbooks/
    └── ci-cd-setup.md                 # NEW
```

### Testing standards

CI itself is a test harness. There is no "test the workflow"; the workflow IS the test. The only meta-test is a one-off PR that proves:
- A deliberately broken commit (e.g., failing unit test) produces a red PR check
- A deliberately passing commit produces a green PR check
- A merge to main triggers staging deploy
- A tag push triggers prod deploy (then cancel before approval to avoid real deploy)

### Project Structure Notes

- **No variance from architecture.** `.github/workflows/` is the canonical path; file naming (`ci.yml`, `deploy-staging.yml`, `deploy-prod.yml`) matches architecture.md §4 D4.17 point 1 verbatim.
- **GHCR over ACR per §7.4.1 cost-floor item 5.** Revisit when the enterprise RFP forces ACR (RT-ACR, not yet registered).

### Architecture compliance

- [architecture.md §4 D4.17](../planning-artifacts/architecture.md) — CI/CD with OIDC
- [architecture.md §4.7 step 4](../planning-artifacts/architecture.md) — hosting + CI/CD implementation sequence
- [architecture.md §6.5.2](../planning-artifacts/architecture.md) — pr.yml stages reference
- [architecture.md §7.4.1 item 5](../planning-artifacts/architecture.md) — GHCR choice over ACR
- [architecture.md §6.5.13 RT-31](../planning-artifacts/architecture.md) — PR CI time revisit trigger

### Library/framework requirements

| Layer | Action / Tool | Purpose |
|---|---|---|
| CI runner | GitHub Actions `ubuntu-latest` | All jobs |
| .NET | actions/setup-dotnet | `10.0.100` SDK |
| Node | actions/setup-node | `22` LTS |
| Azure login | azure/login (OIDC mode) | Federated credential only |
| azd | azure/setup-azd | Aspire + ACA deploy |
| Playwright | microsoft/playwright-github-action OR manual install | Chromium + WebKit |
| Dependabot | github/dependabot | Action + NuGet + npm updates |

### Testing requirements

- **Every CI job must be a required check** configured in `Settings → Branches → Branch protection rules → main`. Marking a required check non-required to force-merge is a Story 0.8 violation.
- **The staging deploy workflow must fail audibly** if `azd deploy` returns non-zero — do not `continue-on-error: true` anywhere in the deploy path.
- **The prod deploy workflow's image-reuse assertion** (AC8) should fail fast if the image digest resolution can't find a staging deployment for the tagged commit.

### Previous Story Intelligence

- **Story 0.1 (backend scaffold)** — CI steps invoke `dotnet test backend/Faktuboh.sln` and `dotnet build backend/Faktuboh.sln`. If Story 0.1 is not complete, `ci.yml` cannot pass.
- **Story 0.2 (FE workspace)** — CI steps invoke `npm run lint`, `npm run test:ci`, `npm run build`, `frontend/scripts/verify-public-statement-bundle.ts`, `frontend/scripts/verify-zoneless.ts`.
- **Story 0.3 (Auth0)** — not prerequisite for CI setup; deploy-staging/prod workflows do not call Auth0 Management API.
- **Stories 0.4-0.8 (backend middleware + fitness tests)** — each adds new required checks to `ci.yml` (e.g., Story 0.8 adds NetArchTest fitness test job; Story 0.12 adds NFR-A9 RTL harness job). Story 0.9 provides the substrate; per-story AC updates `ci.yml` with the new check.
- **Story 0.10 (Azure infra)** — provisions the ACA + Key Vault that `deploy-staging.yml` + `deploy-prod.yml` target. Can be drafted in parallel; actual deploy path requires 0.10's resources to exist.

## References

- [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) — Story 0.9 definition
- [_bmad-output/planning-artifacts/architecture.md §4 D4.17](../planning-artifacts/architecture.md) — CI/CD decision
- [_bmad-output/planning-artifacts/architecture.md §4.7](../planning-artifacts/architecture.md) — implementation sequence
- [_bmad-output/planning-artifacts/architecture.md §6.5.2](../planning-artifacts/architecture.md) — CI stages reference
- [_bmad-output/planning-artifacts/architecture.md §7.4.1](../planning-artifacts/architecture.md) — GHCR choice

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

_Populated during implementation._

### File List

_Populated during implementation — expected new/modified files per the Source Tree section above._
