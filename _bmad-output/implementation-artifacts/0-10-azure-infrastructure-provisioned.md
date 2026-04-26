# Story 0.10: Azure infrastructure provisioned

Status: ready-for-dev

## Story

As **Wahid**,
I want **Azure Container Apps environment + Azure Static Web Apps for both FE targets + Postgres Flexible Server + Azure Key Vault Standard all provisioned in Germany West Central on the €0 free-tier stack (§7.4.1)**,
so that **the platform is deployable from Day 1 with zero idle burn**.

**Covers requirements:** Epic 0 Platform Foundation — hard gate. Provisions the ACA + Postgres + SWA + Key Vault + App Insights stack that every subsequent epic deploys into.

**Why this story now:** Per architecture §4.7, infrastructure provisioning is step 4 (after region+vault, DB, identity). Story 0.9 wired the OIDC deploy credential; this story creates the Azure resources those credentials will touch. Story 0.11 (App Insights + OTel) configures telemetry into the App Insights resource this story creates.

## Acceptance Criteria

### AC1 — Resource group in Germany West Central

**Given** an Azure subscription with 12-month free-tier benefits intact
**When** `az group create --name rg-faktuboh-prod --location germanywestcentral` runs via `azd provision`
**Then** the resource group exists in region `germanywestcentral` (Frankfurt)
**And** a second resource group `rg-faktuboh-staging` exists for the staging environment
**And** the subscription-level default region is documented as `germanywestcentral` in `docs/runbooks/azure-setup.md` [Source: architecture.md §4 D4.18]

### AC2 — ACA environment provisioned per tier

**Given** Azure Container Apps is the backend hosting choice per D4.16
**When** Bicep provisions the ACA environment
**Then** the ACA Managed Environment exists in Germany West Central
**And** staging ACA app `aca-faktuboh-api-staging` has `minReplicas: 0` (true scale-to-zero)
**And** production ACA app `aca-faktuboh-api-prod` has `minReplicas: 1` (avoids cold-start for first MENA-morning request)
**And** KEDA HTTP scaler is configured with `concurrent-requests: 10` (per D4.6 83 rps sustained SLO with headroom) [Source: architecture.md §4 D4.16]
**And** the Bicep resource shape below is present in `infra/azure/modules/aca.bicep`:

```bicep
resource acaEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: acaEnvName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalyticsWorkspace.properties.customerId
        sharedKey: listKeys(logAnalyticsWorkspace.id, logAnalyticsWorkspace.apiVersion).primarySharedKey
      }
    }
    workloadProfiles: [
      { name: 'Consumption', workloadProfileType: 'Consumption' }
    ]
  }
}

resource acaApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'aca-faktuboh-api-${envSuffix}'
  location: location
  identity: { type: 'SystemAssigned' }
  properties: {
    managedEnvironmentId: acaEnv.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        allowInsecure: false
        transport: 'auto'
      }
      registries: [
        { server: 'ghcr.io', username: ghcrUsername, passwordSecretRef: 'ghcr-pat' }
      ]
      secrets: [
        { name: 'ghcr-pat', value: ghcrPat }
      ]
    }
    template: {
      containers: [{
        name: 'api'
        image: containerImage
        resources: { cpu: json('0.5'), memory: '1Gi' }
        env: [
          { name: 'Auth0__Authority', secretRef: 'auth0-authority' }
          { name: 'Auth0__Audience', secretRef: 'auth0-audience' }
          { name: 'ConnectionStrings__Faktuboh', secretRef: 'pg-connection' }
          { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', secretRef: 'ai-connection' }
          { name: 'FAKTUBOH_EMERGENCY_SHUTDOWN', value: 'false' }  // Story 0.17
        ]
      }]
      scale: {
        minReplicas: minReplicas   // 0 staging, 1 prod
        maxReplicas: 3
        rules: [{
          name: 'http-rule'
          http: { metadata: { concurrentRequests: '10' } }
        }]
      }
    }
  }
}
```

### AC3 — Azure Static Web Apps for both FE targets

**Given** the cost-floor check (§7.4.1 item 1) adopted ASWA's built-in CDN in place of Azure Front Door
**When** Bicep provisions ASWA
**Then** two SWA resources exist at production tier `Free`:
- `swa-faktuboh-owner-prod` hosting `app.faktuboh.com` (owner-workspace `ng build` output)
- `swa-faktuboh-statement-prod` hosting `statement.faktuboh.com` (public-statement `ng build` output)
**And** the Bicep resource shape below is present in `infra/azure/modules/swa.bicep`:

```bicep
resource swaOwner 'Microsoft.Web/staticSites@2023-12-01' = {
  name: 'swa-faktuboh-owner-${envSuffix}'
  location: location
  sku: { name: 'Free', tier: 'Free' }
  properties: {
    repositoryUrl: 'https://github.com/<owner>/faktuboh'
    branch: 'main'
    buildProperties: {
      appLocation: 'frontend/apps/owner-workspace'
      outputLocation: 'dist/owner-workspace'
      appBuildCommand: 'npm run build:owner-workspace'
    }
  }
}

resource swaOwnerDomain 'Microsoft.Web/staticSites/customDomains@2023-12-01' = {
  parent: swaOwner
  name: 'app.faktuboh.com'
  properties: { validationMethod: 'cname-delegation' }
}
```

**And** custom domains `app.faktuboh.com` and `statement.faktuboh.com` are bound to each SWA via CNAME records
**And** SWA free-tier bandwidth allowance (100 GB/month) is documented in `docs/runbooks/azure-setup.md` as the RT-33 trigger [Source: architecture.md §7.4.1 item 1]

### AC4 — Postgres Flex Server Burstable B1ms

**Given** Postgres Flex Server is the data tier per D4.1
**When** Bicep provisions Postgres
**Then** the server SKU is `Standard_B1ms` (Burstable tier — free 12 months on new Azure subscription per §7.4.1 item 3)
**And** production-tier config enables `HA Zone Redundant` (zone-redundant HA per Story 0.10 epic definition)
**And** the Bicep shape below is present in `infra/azure/modules/postgres.bicep`:

```bicep
resource pgServer 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: 'pg-faktuboh-${envSuffix}'
  location: location
  sku: { name: 'Standard_B1ms', tier: 'Burstable' }
  properties: {
    version: '16'
    administratorLogin: pgAdminUser
    administratorLoginPassword: pgAdminPassword
    storage: { storageSizeGB: 32, autoGrow: 'Enabled' }
    backup: { backupRetentionDays: 7, geoRedundantBackup: 'Disabled' }  // geo-redundant is a paid feature
    highAvailability: {
      mode: envSuffix == 'prod' ? 'ZoneRedundant' : 'Disabled'
    }
    network: { publicNetworkAccess: 'Enabled' }
  }
}

resource pgDatabase 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: pgServer
  name: 'faktuboh'
  properties: { charset: 'UTF8', collation: 'en_US.utf8' }
}

resource pgFirewallAzureServices 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = {
  parent: pgServer
  name: 'AllowAzureServices'
  properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
}
```

**And** `ConnectionStrings__Faktuboh` is emitted into Key Vault as a secret for ACA consumption [Source: architecture.md §4 D4.1, §7.4.1 item 3, adrs/adr-022]

### AC5 — Azure Key Vault Standard per environment

**Given** Azure Key Vault Standard is chosen over Premium per ADR-025
**When** Bicep provisions Key Vault
**Then** two Key Vault instances exist — `kv-faktuboh-staging` + `kv-faktuboh-prod`
**And** SKU is `Standard` (not `Premium`) per ADR-025
**And** the ACA system-assigned managed identity has `get` + `list` permission on secrets
**And** the Bicep shape below is present in `infra/azure/modules/key-vault.bicep`:

```bicep
resource kv 'Microsoft.KeyVault/vaults@2024-04-01-preview' = {
  name: 'kv-faktuboh-${envSuffix}'
  location: location
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: envSuffix == 'prod' ? true : null  // one-way door in prod
  }
}

// ACA managed identity → Key Vault Secrets User role
resource kvRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: kv
  name: guid(kv.id, acaApp.id, 'kv-secrets-user')
  properties: {
    roleDefinitionId: '/providers/Microsoft.Authorization/roleDefinitions/4633458b-17de-408a-b874-0445c86b69e6'  // Key Vault Secrets User
    principalId: acaApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}
```

**And** initial secrets inserted: `auth0-authority`, `auth0-audience`, `auth0-spa-client-id`, `pg-connection`, `ai-connection`, `resend-api-key` [Source: architecture.md §4 D4.8 + adrs/adr-025]

### AC6 — App Insights with 20% ingestion sampling

**Given** App Insights is the observability sink per D4.20
**When** Bicep provisions App Insights
**Then** a Log Analytics Workspace + App Insights resource exist (App Insights workspace-based mode)
**And** the sampling percentage is set to `20` at the App Insights resource level (`SamplingPercentage = 20`)
**And** adaptive sampling is enabled on the Aspire auto-instrumentation (via `ServiceDefaults`)
**And** the Bicep shape below is present in `infra/azure/modules/app-insights.bicep`:

```bicep
resource logWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-faktuboh-${envSuffix}'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
    workspaceCapping: { dailyQuotaGb: 1 }  // matches free-tier cap (5GB/mo = ~170MB/day); 1GB cap protects the $40/mo budget
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-faktuboh-${envSuffix}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logWorkspace.id
    SamplingPercentage: 20
  }
}
```

**And** the App Insights connection string is written to Key Vault as `ai-connection` [Source: architecture.md §4 D4.20]

### AC7 — €50/month budget alert

**Given** Azure Cost Management supports budget alerts
**When** Bicep provisions the budget resource
**Then** a Consumption Budget `budget-faktuboh-prod` exists with `amount: 50`, currency `EUR`, time grain `Monthly`
**And** alert thresholds fire at `50%` (€25) and `90%` (€45) to Wahid's email
**And** Story 0.17 (cost circuit-breaker) builds on top of this baseline
**And** the Bicep shape below is present in `infra/azure/modules/budget.bicep`:

```bicep
resource budget 'Microsoft.Consumption/budgets@2023-11-01' = {
  name: 'budget-faktuboh-${envSuffix}'
  properties: {
    category: 'Cost'
    amount: 50
    timeGrain: 'Monthly'
    timePeriod: { startDate: '2026-04-01T00:00:00Z' }
    notifications: {
      Threshold50: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 50
        contactEmails: ['wahid.wb@gmail.com']
      }
      Threshold90: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 90
        contactEmails: ['wahid.wb@gmail.com']
      }
    }
  }
}
```

[Source: architecture.md §7.4.1 scaffold no-go gate + Story 0.17]

### AC8 — `azd provision` end-to-end succeeds

**Given** `infra/azure/main.bicep` orchestrates all modules above
**When** `azd provision --environment staging` runs with valid OIDC credentials from Story 0.9
**Then** all resources provision successfully in <15 minutes
**And** `azd env get-values` shows ACA URL, Postgres hostname, Key Vault URI, App Insights connection string
**And** the staging deployment is reachable via `https://aca-faktuboh-api-staging.<region>.azurecontainerapps.io/health` returning `"healthy"`
**And** `azd provision --environment production` completes in parallel against `rg-faktuboh-prod` [Source: architecture.md §4 D4.17 + §6.5.4]

## Tasks / Subtasks

### T1 — Create Bicep module structure (AC: 1-7)

- [ ] Create `infra/azure/main.bicep` (root orchestrator)
- [ ] Create `infra/azure/modules/aca.bicep` (AC2)
- [ ] Create `infra/azure/modules/swa.bicep` (AC3)
- [ ] Create `infra/azure/modules/postgres.bicep` (AC4)
- [ ] Create `infra/azure/modules/key-vault.bicep` (AC5)
- [ ] Create `infra/azure/modules/app-insights.bicep` (AC6)
- [ ] Create `infra/azure/modules/budget.bicep` (AC7)
- [ ] Create `infra/azure/parameters/staging.bicepparam` + `infra/azure/parameters/production.bicepparam`

### T2 — `azure.yaml` for azd (AC: 8)

- [ ] Create `azure.yaml` at repo root referencing `infra/azure/main.bicep` as IaC
- [ ] Declare services: `api` → `backend/src/Faktuboh.Api` (container), `owner-workspace` → `frontend/apps/owner-workspace` (SWA), `public-statement` → `frontend/apps/public-statement` (SWA)
- [ ] Configure environments `staging` + `production` with resource group bindings

### T3 — Seed Key Vault secrets (AC: 5)

- [ ] Post-provision script `scripts/seed-keyvault.ps1`:
  - Reads Auth0 secrets from Story 0.3's local dev user-secrets (or GitHub Secrets if already in prod)
  - Inserts into Key Vault via `az keyvault secret set`
  - Secrets list: `auth0-authority`, `auth0-audience`, `auth0-spa-client-id`, `auth0-mgmt-client-id`, `auth0-mgmt-client-secret`, `pg-connection`, `ai-connection`, `resend-api-key` (Resend stub; populated in Epic 1 when first email slice ships)
- [ ] Document secret rotation cadence (quarterly per ADR-025) in `docs/runbooks/azure-setup.md`

### T4 — Custom domain binding (AC: 3)

- [ ] At `faktuboh.com` registrar, add CNAME records:
  - `app.faktuboh.com` → `<swa-owner-default-hostname>.azurestaticapps.net`
  - `statement.faktuboh.com` → `<swa-statement-default-hostname>.azurestaticapps.net`
- [ ] Invoke ASWA domain validation for each
- [ ] Verify TLS cert auto-provisions via Azure-managed certificate
- [ ] Document CNAME records in `docs/runbooks/azure-setup.md` alongside Story 0.3's Auth0 CNAME

### T5 — Wire ACA → Key Vault secret references (AC: 2, 5)

- [ ] Update `infra/azure/modules/aca.bicep` `secrets` array to use Key Vault references (`keyVaultUrl` + `identity` per ACA 2024-03-01 API)
- [ ] Confirm ACA managed identity has `Key Vault Secrets User` role on both vaults
- [ ] Test: deploy container that logs `env` + verify all `Auth0__*` + `ConnectionStrings__Faktuboh` env vars populate

### T6 — End-to-end provisioning test (AC: 8)

- [ ] Run `azd init` against the repo
- [ ] Run `azd provision --environment staging` with staging OIDC credential
- [ ] Verify all resources exist via `az group list` + `az containerapp list` + `az postgres flexible-server list`
- [ ] Hit `/health` on the deployed ACA app (initial image is Story 0.1's smoke build)
- [ ] Tear down staging via `azd down` to confirm cleanup works (reprovision after)

### T7 — Runbook (AC: 1-7)

- [ ] Write `docs/runbooks/azure-setup.md` covering:
  - Resource group + region selection rationale (Germany West Central per D4.18)
  - Per-resource SKU choices + cost footprint
  - Secret rotation procedure
  - DR procedure: re-provisioning from scratch
  - Free-tier expiration cliff at Month 13 (Postgres Burstable B1ms free benefit ends) — RT-47 per §7.4.1

## Dev Notes

### Critical guardrails (do not violate)

1. **Germany West Central is a one-way door.** Changing region post-launch requires Postgres logical replication + cutover + ACA rebuild + Key Vault re-key + DNS — 1-2 engineer-weeks. [Source: architecture.md §4 D4.18]
2. **Key Vault purge protection is enabled in prod.** Once prod Vault is provisioned with `enablePurgeProtection: true`, it cannot be fully deleted for 90 days. Intentional — protects against accidental key-shred. [Source: adrs/adr-025]
3. **Resource names are stable.** `pg-faktuboh-prod`, `aca-faktuboh-api-prod`, etc. — changing these requires teardown + re-provision. Pick the shape now. [Source: architecture.md §6.5.4]
4. **Staging ACA has `minReplicas: 0`, prod has `minReplicas: 1`.** Reversing this in prod re-introduces cold-start on MENA-morning requests. [Source: architecture.md §4 D4.16]
5. **Postgres free-tier benefit expires Month 13.** Budget alert at €50 catches this + RT-47 documents the post-expiration floor (~€12-15/mo). [Source: architecture.md §7.4.1 RT-47]
6. **GHCR pull credentials are a rotating PAT.** Not long-lived. Rotation cadence: every 90 days; procedure in `docs/runbooks/azure-setup.md`.
7. **ASWA bandwidth cap = 100GB/month free tier.** RT-33 fires on >70% burn.

### Source tree — files to create

```
infra/
└── azure/
    ├── main.bicep                              # NEW — orchestrator
    ├── modules/
    │   ├── aca.bicep                           # NEW — ACA environment + app
    │   ├── swa.bicep                           # NEW — two SWA resources
    │   ├── postgres.bicep                      # NEW — Burstable B1ms + HA in prod
    │   ├── key-vault.bicep                     # NEW — Standard tier
    │   ├── app-insights.bicep                  # NEW — 20% sampling + Log Analytics
    │   └── budget.bicep                        # NEW — €50/mo alert
    └── parameters/
        ├── staging.bicepparam                  # NEW
        └── production.bicepparam               # NEW

azure.yaml                                      # NEW — azd orchestration at repo root

scripts/
└── seed-keyvault.ps1                           # NEW — post-provision secret seed

docs/
└── runbooks/
    └── azure-setup.md                          # NEW — full provisioning procedure
```

### Testing standards

| Level | Tool | What this story covers |
|---|---|---|
| Infrastructure | `azd provision --environment staging` | AC8 end-to-end |
| Smoke | `curl https://aca-faktuboh-api-staging.../health` | Post-provision |
| Cost verification | Azure Pricing calculator + actual Day-1 bill | Validates €0 floor |

**No CI wiring of the provision workflow** — provisioning is a one-time act per environment, not a per-PR step. Destroy + recreate as a Day-0 rehearsal only.

### Project Structure Notes

- **ADR-025 retains Standard tier for MVP.** Premium (HSM) is deferred until enterprise RFP requires FIPS 140-2 Level 3. Upgrade path is in-place tier change — no schema/code change. [Source: adrs/adr-025]
- **D4.19 Azure Front Door is superseded by ASWA built-in CDN** per §7.4.1 item 1 — cost-floor adopted at scaffold start. If AFD is needed later (RT for bot manager / multi-policy WAF / private endpoint), it layers in front of ASWA without tearing down ASWA.
- **Azure Container Registry is replaced with GitHub Container Registry** per §7.4.1 item 5. Revisit when GHCR's pull-rate limits bite (not expected at MVP).

### Architecture compliance

- [architecture.md §4 D4.1](../planning-artifacts/architecture.md) — Postgres Flexible Server
- [architecture.md §4 D4.8](../planning-artifacts/architecture.md) — Key Vault (tier reconciled by ADR-025)
- [architecture.md §4 D4.16](../planning-artifacts/architecture.md) — ACA hosting
- [architecture.md §4 D4.18](../planning-artifacts/architecture.md) — Germany West Central region
- [architecture.md §4 D4.19](../planning-artifacts/architecture.md) — CDN topology (AFD deferred; ASWA adopted)
- [architecture.md §4 D4.20](../planning-artifacts/architecture.md) — App Insights + sampling
- [architecture.md §4.7 step 1 + 4](../planning-artifacts/architecture.md) — implementation sequence
- [architecture.md §7.4.1](../planning-artifacts/architecture.md) — cost-floor stack
- [adrs/adr-025-key-vault-standard-for-mvp.md](../planning-artifacts/adrs/adr-025-key-vault-standard-for-mvp.md) — Standard-tier decision

### Library/framework requirements

| Layer | Tool | Version | Purpose |
|---|---|---|---|
| IaC | Bicep | latest | Azure resource definitions |
| Deploy CLI | Azure Developer CLI (`azd`) | latest | `azd provision` + `azd deploy` |
| Azure CLI | `az` | latest | Ad-hoc resource queries |
| Region | Germany West Central (Frankfurt) | — | Production region |
| ACA revision API | `Microsoft.App/containerApps@2024-03-01` | — | Aligns with Aspire 13 deploy target |
| Postgres API | `Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01` | — | Latest stable |
| Key Vault API | `Microsoft.KeyVault/vaults@2024-04-01-preview` | — | Latest |
| App Insights | workspace-based (`Microsoft.Insights/components@2020-02-02`) | — | Per D4.20 |

### Testing requirements

- **`azd provision` must succeed end-to-end before this story is complete.** Partial provision (some resources up, others failed) is a fail — Bicep dependencies must be correct.
- **Cost verification at Day-7** — check Azure Cost Analysis and confirm daily burn is <€1 (proves scale-to-zero is working). If >€1/day on staging with zero traffic, investigate.

### Previous Story Intelligence

- **Story 0.1 (backend scaffold)** complete: Aspire AppHost exists. ACA runs the `Faktuboh.Api` container built from this scaffold.
- **Story 0.2 (FE workspace)** complete: `dist/owner-workspace` + `dist/public-statement` are the SWA deploy sources.
- **Story 0.3 (Auth0)** complete: Auth0 secrets are populated into Key Vault by T3 seed script.
- **Story 0.9 (CI/CD OIDC)** complete: federated credential for `deploy-staging.yml` / `deploy-prod.yml` is configured and ready to provision.
- **Stories 0.11 (App Insights + traceId), 0.14 (telemetry primitive), 0.17 (cost circuit-breaker)** will consume the App Insights + budget resources provisioned here.

## References

- [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) — Story 0.10 definition
- [_bmad-output/planning-artifacts/architecture.md §4 D4.1, D4.8, D4.16, D4.18, D4.19, D4.20](../planning-artifacts/architecture.md) — infra decisions
- [_bmad-output/planning-artifacts/architecture.md §4.7](../planning-artifacts/architecture.md) — implementation sequence
- [_bmad-output/planning-artifacts/architecture.md §6.5.4](../planning-artifacts/architecture.md) — Bicep layout reference
- [_bmad-output/planning-artifacts/architecture.md §7.4.1](../planning-artifacts/architecture.md) — free-tier cost stack
- [_bmad-output/planning-artifacts/adrs/adr-025-key-vault-standard-for-mvp.md](../planning-artifacts/adrs/adr-025-key-vault-standard-for-mvp.md) — KV tier decision

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

_Populated during implementation._

### File List

_Populated during implementation — expected new/modified files per the Source Tree section above._
