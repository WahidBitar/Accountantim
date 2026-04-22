---
id: ADR-025
title: Key Management — Azure Key Vault Standard for MVP
status: ACCEPTED
date: 2026-04-21
supersedes_partial: [D4.8 Key Vault tier selection]
source: Adversarial Review R-05
decision_type: two-way-door
---

## Context

The original D4.8 / ADR-008 envelope-encryption decision specified Azure Key Vault **Premium** (HSM-backed, FIPS 140-2 Level 3) for the KEK. Adversarial review (AR-15) identified this as the single clearest budget violation against Faktuboh's declared €0/month burn tolerance:

- Key Vault Premium HSM: ~€3.30/key/month + per-operation fees.
- Multiple DEKs implied by the envelope design → multiple HSM-backed keys.
- No free-tier path exists for Premium HSM.

## Decision

Use **Azure Key Vault Standard** (software-protected keys, FIPS 140-2 Level 1) for the KEK. DEKs are wrapped via the Key Vault crypto API. HSM protection is deferred to post-MVP.

## Rationale

- **Cost**: Key Vault Standard is effectively free at MVP volume (€0.03 per 10K operations; projected <€1/month).
- **Compliance sufficiency at MVP**: FIPS 140-2 Level 1 is appropriate for pre-GA fintech software. Level 3 (HSM) is compliance theater until an enterprise RFP requires it.
- **Envelope pattern is tier-agnostic**: KEK wraps DEK, DEK encrypts payload. Only the KEK's backing store changes; the scheme, the rotation automation, and the domain-level erasure flow survive unchanged.

## Consequences

- Compliance posture documented as: "FIPS 140-2 Level 1, upgradeable to Level 3 via Key Vault Premium without schema or code change."
- Quarterly KEK rotation cadence unchanged.
- Cost table: Key Vault line item ~€1/mo (replaces Premium HSM estimate).
- No change to the `SubjectKeyDestroyed` domain event or the key-shred erasure flow.

## Revisit Triggers

- **RT-KMS-1:** First enterprise customer RFP specifies HSM-backed keys (FIPS 140-2 Level 3+). Action: upgrade to Key Vault Premium; scheme-level code unchanged.
- **RT-KMS-2:** Regulatory regime in a target market changes to mandate HSM. Action: same.
- **RT-KMS-3:** Threat model updates to include adversaries capable of extracting software-protected keys from Azure's infrastructure (realistically nation-state-level). Action: upgrade to Premium.

## Supersession Notes

- ADR-008 (Envelope encryption posture) retains the KEK/DEK/IV hierarchy. The specific clause choosing Premium HSM is SUPERSEDED by this ADR. Architecture.md §4 D4.8 and §7.4.1 cost table are updated to reference Key Vault Standard.

## Links

- PRD/architecture burn-tolerance constraint (€0/month) — this ADR restores compliance with it.
