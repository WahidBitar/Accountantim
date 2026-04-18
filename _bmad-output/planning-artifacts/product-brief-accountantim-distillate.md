---
title: "Product Brief Distillate: Accountantim"
type: llm-distillate
source: "product-brief-accountantim.md"
created: "2026-04-14"
purpose: "Token-efficient context for downstream PRD creation"
---

# Product Brief Distillate: Accountantim

## Core Design Decisions

- Debts are preserved in their original denomination — 10g gold stays 10g gold, never auto-converted to a dollar equivalent. This is a foundational architectural constraint.
- Cross-currency conversion only happens via explicit exchange operations. The exchange rate is locked at the time of the exchange transaction. No floating conversions.
- Per-contact balances are shown per denomination — no cross-currency aggregation or "total in base currency" view. Reporting may show equivalent values for informational purposes only, but these never affect the ledger.
- The user hinted at a specific vision for currency/metal price feeds that hasn't been shared yet — capture this during PRD. It was deliberately deferred from the brief.
- Single-user model: one user tracks debts unilaterally. Counterparties do not need accounts. The app is a personal ledger, not a shared system.
- Shareable statement links are read-only, unauthenticated, and serve as the primary organic growth channel (each link = acquisition touchpoint with CTA).

## Requirements Hints (from user conversation)

- Debt transfer = moving part or all of a debt from one contact to another via journal entry. User described it as: "يمكنه أن يقوم بتحويل جزء من الدين من شخص لآخر" (can transfer part of a debt from one person to another).
- Shareable links are per-contact, not per-transaction. Each contact gets their own unique link showing full transaction history + current balance.
- Freemium thresholds (500 transactions / 10 contacts) are configurable values stored as settings, not hardcoded. Must be tunable based on real usage data.
- Existing users at the time of paywall activation are grandfathered (no limits).
- Bilingual UI from day one: Arabic (RTL) + English (LTR). Not just translated strings — full RTL layout support.
- MVP is responsive web only. No native mobile. No PWA. Mobile-friendly responsive design is sufficient for Phase 1.
- Data export (CSV) is in MVP scope — a trust and portability signal.
- Legal disclaimer required: tracked debts are user records, not legally binding contracts.

## Rejected Ideas & Deferred Features

- **Counterparty accounts / two-sided system** — rejected for MVP. Strength of the product is single-user simplicity. Debt acknowledgment/confirmation flow deferred to Future.
- **Live price feeds for metals/currencies** — deferred. User has an unrevealed idea for this feature. Do not design a generic price feed — wait for user's vision during PRD.
- **Native mobile app** — deferred to post-MVP. Responsive web is sufficient.
- **Push notifications / payment reminders** — deferred. Skeptic reviewer noted this could significantly improve retention, but user chose to defer.
- **CSV import from other apps** — deferred to Future. Opportunity reviewer noted Splitwise migration path as acquisition tactic — worth revisiting.
- **Cross-currency aggregated totals** — rejected by design. Balances stay per-denomination. Informational equivalent values in reports are separate from the ledger.
- **Full small business accounting** — user does not see this as the likely expansion path despite it being listed in Vision.

## Competitive Intelligence

- **Splitwise**: 65% 1-star on Trustpilot (2026). Free tier limited to 3 expenses/day with 10-sec wait. Multi-currency paywalled. Receipt scanning behind $40/year. Users actively migrating. Group expense splitting focus, not personal lending ledger.
- **Pigeon Loans**: Formal loans only (creation, tracking, payment scheduling). No multi-currency, no metals, no journal entries, no statement generation.
- **Debtrail**: Records who owes you, schedules recurring debts, sends reminders. Single-currency only. No accounting features.
- **Settle Up / Tricount**: Free Splitwise alternatives for group expense splitting. Trip/group focused, not ongoing personal lending. No accounting, no metals.
- **SoftLedger / Sage Intacct**: Enterprise multi-currency accounting with commodity support. $500+/month. Far too complex for personal use.
- **Gap**: No consumer product combines multi-denomination tracking + debt transfers + shareable statements. This gap exists because the market has bifurcated into "social expense splitting" and "enterprise accounting" with nothing in between.

## Market Data

- Personal finance app market: ~$38B (2026), projected $173B by 2035 at 20.8% CAGR.
- Islamic fintech market: $180B by 2026 (17.9% CAGR). 56% of Muslim millennials want Islamic digital financial services, 32% lack access.
- Middle East fintech: >$3.45B by 2026. 70%+ of GCC millennials use at least one fintech app daily.
- Gold prices at historic highs (2024-2026) — increased interest in gold-denominated transactions, especially in Middle East and South Asian communities.

## User Segments Detail

- **Primary**: Individuals in cultures with strong informal lending traditions — Arabic-speaking communities, South Asian diaspora, West African communities. Gold lending between families is culturally common.
- **Secondary**: Micro-merchants and shop owners using paper ledgers for customer credit. Their workflow maps directly to Accountantim (record credit, check balance, share statement). Shop owner extending credit to 50 regular customers is replacing a paper book.
- **Launch market**: Arabic-speaking communities. Bilingual interface positions for both Arabic-first and English-speaking users in the region.

## Reviewer Insights Worth Preserving

### Skeptic (risks to address in PRD)
- Shareable link abuse: unauthenticated links could be used to generate fake debt statements — fraud/harassment vector. Need guardrails.
- Debt transfer complexity: transferring debt between parties has implicit consent and notification implications. Define the UX flow carefully.
- Cold-start UX: 60% activation target requires a compelling first-session experience. Define what the empty state looks like and how it motivates the first debt entry.
- Multi-currency balances create UX confusion if not presented clearly. Per-denomination display (no aggregation) helps but needs strong visual design.
- Web-only in a "record in the moment" use case is a friction point. The responsive design must feel native-fast on mobile browsers.

### Opportunity (upside to explore in PRD)
- ROSCAs / rotating savings circles (chit funds, susus, tontines) — multi-party, multi-currency informal structures with no digital ledger. Accountantim's model maps closely to their needs. Potential partnership/distribution channel.
- Diaspora remittance platforms (Wise, Remitly) — co-marketing at the moment users think about cross-border money.
- Gold dealers / bullion exchanges — white-label or co-branded ledger tool as value-add for their customers.
- "Proof of debt" as a social primitive — the shareable link is a new kind of social object (verifiable, neutral record). Strong narrative for press and positioning.
- Micro-merchant tier ($10-15/month) with bulk contacts, recurring credit lines, statement branding — separate B2B growth path with 4-6x higher ARPU.

### Go-to-Market (launch risks)
- No acquisition channel defined beyond shareable links. Need at least one proactive channel (community seeding, content, influencer) for initial critical mass before viral loop kicks in.
- Bilingual (AR+EN) from day one increases development cost but is essential for the target market. RTL support is non-trivial — budget for it.
- Timing between Phase 1 (free) and Phase 2 (freemium) needs a concrete trigger metric, not just "after reaching a certain number of users."

## Open Questions (for PRD phase)

- What is the user's specific vision for currency/metal price feeds? (mentioned but not yet shared)
- What does the first-session onboarding flow look like? How do we motivate the first debt entry?
- Should shareable links have expiration or revocation controls?
- What is the concrete trigger for transitioning from Phase 1 (free) to Phase 2 (freemium)?
- How should debt transfer notifications work? Does the new debtor get notified? Through what channel?
- What guardrails prevent shareable link abuse (fake statements)?
- Should the app support attaching notes, images, or documents to debt entries (e.g., photo of a check)?
