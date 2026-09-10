# Architecture

## Status and scope

This document records the target commercial architecture approved in `prd-commercial-inventory-platform.md`. The checked-in application is still an Android pilot: it performs on-device inference, proposes integer SKU counts, requires human correction and confirmation, and stores confirmed sessions locally.

The first commercial cohort is Saudi restaurants, quick-service restaurants, and cafés. Android phones and tablets are certified first. iOS/iPadOS feasibility begins while the shared core is built, with Apple certification following Android. Other verticals and item categories require separate domain and acceptance evidence.

Athmar remains an assisted inventory platform. Machine output is never an approved ERP, POS, accounting, purchasing, or payment record until an authorized human confirms it and any required review completes.

## Architectural decisions

1. Use a hybrid architecture: counting and inference remain available offline; central administration, trust, synchronization, audit, and integrations use a Saudi-hosted cloud service.
2. Keep one modular backend deployable, not microservices, for Phase 1.
3. Use ASP.NET Core on .NET 10 LTS and PostgreSQL so the client and server share a language and deterministic domain fixtures without sharing persistence entities.
4. Deploy the first backend to Google Cloud Dammam (`me-central2`) using Cloud Run, Cloud SQL for PostgreSQL, regional object storage, KMS, and regional logging.
5. Use immutable inventory events plus ordinary current-state projections. Do not introduce a generic event-sourcing framework.
6. Use SQLite on devices only after an AOT/IL2CPP provider spike passes on Android and iOS. It will hold durable drafts, immutable local events, outbox state, acknowledgements, cached assignments, and package activation journals.
7. Store non-exportable keys and refresh credentials through Android Keystore or iOS Keychain adapters. Encrypt bearer leases and protected SQLite content with device-bound keys; never treat Keystore as arbitrary record storage.
8. Sign immutable manifest bytes with a detached ES256 signature from a KMS-backed key separate from mobile application-signing keys. The manifest contains the signing key ID, never its own signature.
9. Deny OS backup of credentials, device identity, model packages, and sensitive inventory by default. Restore business continuity through the application-controlled synchronization service.
10. Treat Saudi PDPL transfer rules and applicable NCA cloud controls as architecture gates. Saudi hosting is a risk-control decision, not a claim of blanket legal localization.

## System context

```text
Inventory operator / supervisor / customer administrator
                           |
               Android or iOS application
        on-device inference, drafts, review, outbox
                           |
                 HTTPS + authenticated device
                           v
              Saudi regional API and ingress
                           |
             ASP.NET Core modular monolith
        +------------------+------------------+
        |                  |                  |
   PostgreSQL        Object storage       Regional KMS
 transactions,       signed packages,     manifest-signing
 events, audit        fixtures, evidence   keys
        |
 Scheduled or continuously allocated worker
 retention, outbox delivery, and bounded retries
        |
 Approved ERP/POS integrations after human confirmation
```

The API and worker are two process roles built from one codebase and container image. The worker uses Cloud Run Jobs for scheduled retention and reconciliation, while latency-sensitive outbox delivery uses a Cloud Run service with instance-based CPU, a production minimum instance, and PostgreSQL advisory-lock work claiming. PostgreSQL is the authoritative transaction, idempotency, queue, audit, and projection store. Phase 1 does not require Kubernetes, Kafka, Redis, Elasticsearch, a separate event store, or per-domain services.

## Mobile module boundaries

The current `Athmar.VisionCount.Runtime` assembly directly references Unity Inference Engine and uGUI. It will be decomposed incrementally into these boundaries:

- `Athmar.VisionCount.Domain`: tenant/item identifiers, units, packages, measurement profiles, observations, sessions, events, policies, and invariants; no Unity references.
- `Athmar.VisionCount.Application`: use cases and ports for drafts, observations, proposals, review, confirmation, supersession, synchronization, retention, and package activation.
- `Athmar.VisionCount.Infrastructure.Local`: SQLite persistence, migrations, event/outbox repositories, package journal, filesystem, retention, and CSV.
- `Athmar.VisionCount.Inference.Unity`: camera-frame adaptation, preprocessing, Unity Inference Engine worker, decoder, fixture validation, and diagnostics.
- `Athmar.VisionCount.Platform.Mobile`: secure storage, protected files, backup policy, application/device metadata, connectivity, lifecycle, camera permission, and export/share ports with Android and iOS adapters.
- `Athmar.VisionCount.Presentation.Unity`: responsive phone/tablet views, localization, accessibility, presenters, and composition root.

Dependency direction is inward:

```text
Domain <- Application <- Presentation and composition
                  ^
                  |
 Infrastructure.Local / Inference.Unity / Platform.Mobile
```

Platform-specific symbols and native calls stay in narrow adapters and composition roots. Business rules cannot depend on `UNITY_ANDROID`, `UNITY_IOS`, Unity UI, Unity filesystem paths, or inference types.

## Backend modules

The modular monolith contains explicit modules with application interfaces and transaction boundaries:

- **Tenancy and authorization:** tenants, branches, locations, principals, role and branch scopes, support grants, and permission-matrix versions.
- **Device trust:** one-time activation, installation public keys, rotating credentials, offline authorization leases, and revocation.
- **Catalogue:** immutable versions, stable item identities, units, package graphs, conversions, barcodes, and measurement profiles.
- **Package release:** signed model manifests, immutable assets, compatibility, approvals, key rotation, and package revocation.
- **Assignments:** branch/location scope, catalogue version, item scope, deadline, and concurrency policy.
- **Inventory and synchronization:** append-only events, observations, projections, durable idempotency, acknowledgements, and cursors.
- **Review and reconciliation:** variance flags, blocked/conflicted records, supervisor decisions, supersession, and voiding.
- **Audit and retention:** append-only security/business audit, deletion jobs, deletion evidence, and controlled export. Cryptographic anchoring is deferred until a threat model defines canonicalization and verification.
- **Integration:** outbox and delivery state; ERP/POS connectors are added only when contracted.

## Core data rules

- Use UUIDv7-compatible identifiers, UTC timestamps, and decimal quantities. Binary floating point cannot represent authoritative inventory quantities.
- Every tenant-owned row includes `tenant_id`; tenant relationships use composite foreign keys containing it.
- Authentication derives tenant context from server-side identity/device bindings, never a request header or payload. Each pooled PostgreSQL transaction sets a transaction-local tenant context before Row Level Security-protected queries, and the runtime role cannot bypass RLS.
- Object keys, signed download authorization, log access, support grants, package publication, and KMS IAM are tenant scoped independently because PostgreSQL RLS cannot protect those systems.
- Stable `ItemId` is separate from external SKU codes and model class labels.
- Published catalogue and measurement-profile versions are immutable.
- Package conversions are positive, acyclic, dimensionally compatible, versioned, and covered by shared golden fixtures.
- Original observation components are preserved alongside normalized base-unit quantities.
- Confirmed events are append-only. Correction creates a linked superseding event rather than updating history.
- Legacy integer sessions remain read-only `legacy-v0` evidence and are never enriched with current tenant or catalogue values.

## Device identity and offline authorization

A customer administrator creates a short-lived, one-time activation grant. The application generates a non-exportable installation key through platform secure storage and exchanges the grant plus public key for a tenant-bound device identity and rotating credential.

Human authentication uses an approved OIDC provider. The server maps immutable issuer/subject identity to tenant, role, and branch scope. Effective authorization is the intersection of active user scope, active device scope, branch policy, and endpoint permission. Offline work uses a short-lived signed authorization lease bound to the user, device public key, tenant, branches, permissions, policy version, and server issue/expiry time.

Device API requests and uploaded event envelopes use proof of possession from the installation key. Server receipt time and lease validity bound untrusted device timestamps. Revocation prevents new credentials, packages, leases, and synchronization; pre-revocation offline events enter a versioned effective-time accept-or-review policy rather than trusting the device clock. Revocation never rewrites historical actor identity.

Support access is time limited, customer approved, inventory masked by default, and fully audited. Package release separates requester, approver, and KMS-signing IAM roles.

## Offline persistence and synchronization

Before adoption, an AOT/IL2CPP spike must select and test a maintained SQLite provider on Android ARM64 and iOS ARM64/simulator, including migrations, transactions, process interruption, and native-plugin packaging. Device SQLite then uses transactions and WAL for:

- autosaved drafts and original observations;
- immutable local event envelopes;
- pending synchronization and retry state;
- durable server acknowledgements and down-sync cursor;
- cached assignments, catalogues, and encrypted offline authorization leases;
- active, previous, and staging package journal.

The database, WAL, SHM, backups, and temporary files share explicit platform protection and backup-exclusion policy. A device-bound non-exportable key encrypts protected database content or an application-managed envelope key; refresh credentials remain in Keystore/Keychain. Logout, revocation, reinstall, key invalidation, and end-of-contract deletion define how ciphertext, sidecars, and wrapped keys are removed or rendered inaccessible.

Synchronization provides effectively-once event processing through immutable event IDs and durable PostgreSQL idempotency records. Event append, projection update, audit/outbox creation, and stored acknowledgement commit in one server transaction.

The system never numerically merges independent physical counts automatically. Duplicate requests return their original acknowledgement. Stale catalogues, expired leases, revoked assignments, or concurrent exclusive counts become blocked or conflicted records for explicit policy or supervisor resolution.

## Model and catalogue trust

The current manual URL-plus-hash flow remains a restricted legacy mode only. Commercial package schema v2 is an immutable UTF-8 manifest containing tenant, catalogue, application compatibility, complete preprocessing and tensor contracts, asset sizes and hashes, known fixture, validity policy, and signing key ID. A detached ES256 signature is distributed alongside it and signs the exact manifest bytes.

The mobile application embeds a bootstrap root public key and minimum trust epoch. Root-signed trust bundles distribute active leaf signing keys, validity periods, and revocations. Every bundle has a monotonically increasing epoch, issue/expiry time, previous-bundle digest, and active root ID. The device persists the active root, highest accepted epoch, and digest through a journaled protected-store transaction and rejects lower epochs, conflicting chains, expired bundles, and rollback attempts.

Leaf private keys are non-exportable in KMS and separate from Android/iOS application-signing keys. Rotation overlaps old and new leaf keys for an approved window. Root rollover uses a transition document containing old root ID, new root ID/public key, activation epoch, old-root retirement epoch, and previous trust digest, signed by both roots. A device accepts it only when the old signature matches its currently active root, the new key verifies its signature, and the transition advances the persisted chain. It journals and atomically switches the active-root pointer and epoch before accepting new-root bundles. At and after retirement it rejects every old-root bundle regardless of epoch; only the new root can authorize later transitions.

After reinstall or loss of protected trust state, commercial package activation requires online managed re-enrollment. The server supplies the complete digest-linked, dual-signed transition chain from the application-bundled root, or from a replacement root authenticated by a signed application update, to the current root. It also supplies a short-lived current-root-signed checkpoint containing the trust epoch, chain head digest, minimum application version, issue time, and expiry. The client verifies the entire chain, checkpoint, authenticated time window, and server-enforced current epoch floor before atomically persisting trust state; a bare control-plane root or epoch claim is never trusted. Offline reinstall can use only an application-bundled package.

Routine startup, resume, package authorization, and trust refresh require the latest signed checkpoint and any missing transition chain. The client advances its protected trust state before receiving an asset authorization or using a package beyond its offline grace. The server rejects package authorization when the device's root ID, epoch, chain head, application version, or revocation state is below current policy. Checkpoint expiry and the persisted monotonic chain prevent a stale device from accepting a newly signed old-root bundle after retirement.

Suspected root compromise requires a signed application update with an old-root denylist and new bootstrap root, plus server rejection of affected client versions; a fully offline client cannot securely recover through a compromised root. A fully offline device also cannot learn a new leaf or package revocation, so the active package follows an explicit last-trusted-update grace or fail-closed tenant policy, which diagnostics expose.

Commercial activation proceeds as a journaled transaction:

1. authorize the tenant/device and obtain short-lived asset access;
2. stream each asset to bounded temporary storage;
3. enforce declared size, free-space, timeout, and cancellation policy;
4. verify the detached signature over exact manifest bytes, trust-bundle status, and all hashes;
5. verify tenant, catalogue, application compatibility, expiry, and known revocation state;
6. validate input/output tensor contracts and execute the known fixture;
7. commit an atomic active-package pointer while retaining a known-good previous package;
8. recover deterministically from every process-death or power-loss point.

Startup, resume, and every accepted trust update revalidate the active package, signing leaf, tenant, application compatibility, and revocation state. A newly known package or leaf revocation immediately deactivates the affected package or applies only an explicitly signed emergency grace policy; scanning and synchronization fail closed until a non-revoked approved package is active. Real-path tests cover active-package revocation, leaf revocation, offline grace expiry, rollback to a revoked previous package, and recovery after interruption.

Publication requires separate requester and approver identities before the protected release job can invoke the KMS signing role.

## Privacy, retention, and backup

Raw camera frames remain in process memory and are not persisted or uploaded by default. Any future image collection is a separate customer-approved feature and data flow.

Retention policies distinguish drafts, confirmed inventory, exports, audit, diagnostics, package history, logs, and backups. Device retention runs at startup, periodically, and after policy activation. Cloud deletion produces evidence and reports the last backup expiry honestly; immutable backups are not represented as immediately row-deletable.

Android builds define both legacy `fullBackupContent` and Android 12+ `dataExtractionRules`. iOS data uses explicit file-protection and backup-exclusion attributes. Credentials and non-exportable keys never use ordinary preferences or files.

Every field must have a documented purpose, lawful basis where applicable, retention, recipients, storage region, backup behavior, and deletion method. No foreign replica, log sink, crash reporter, model API, CDN data path, or support route is enabled until its transfer and subprocessor assessment is approved.

## Presentation and device support

Phone and tablet presentation uses compact, medium, and expanded layout states rather than one fixed portrait canvas. Each supported state defines safe areas, text scale, camera crop/rotation, external barcode-scanner input, lifecycle, and orientation/window behavior.

Arabic and English use complete localization resources for operator, administration, review, errors, diagnostics, and accessibility. Global polling of legacy text components is not the final commercial Arabic architecture.

An early iOS/iPadOS feasibility build validates Unity IL2CPP, Unity Inference Engine backend behavior, camera orientation, protected persistence, Keychain, lifecycle interruption, Arabic rendering, responsive layouts, deletion, and CI on macOS before Android-only assumptions become expensive.

## Release topology and controls

Production and non-production use separate cloud projects, databases, buckets, keys, secrets, and identities. Cloud SQL uses regional high availability and point-in-time recovery. Application containers use workload identity; no long-lived cloud key is stored in GitHub. Organization policy and log-sink routing direct application and audit logs to approved regional buckets; default provider retention or multi-region sinks are not assumed to satisfy residency policy.

CI must:

- pin third-party actions to reviewed commit SHAs;
- run pure domain, migration, serialization, real PostgreSQL tenant-isolation, and Unity tests;
- inspect final Android and iOS manifests and backup/privacy settings;
- build and sign the exact approved commit;
- produce checksums, SBOM/provenance, release notes, known limitations, and retained rollback artifacts;
- require authorized human approval for model signing and production deployment.

Dammam currently provides zonal resilience but not a second Saudi Google Cloud region. The application remains offline-capable during a regional outage. Cross-region disaster recovery is not enabled without an approved data-transfer decision and documented RTO/RPO trade-off.

## Implementation sequence

1. **Phase 0 — rescue and trust:** complete effective retention, CSV formula safety, admin throttling/session expiry, secure-storage boundary, Android backup exclusion, pinned CI, explicit release metadata, bounded streaming downloads, full tensor/fixture validation, detached ES256 manifests and anti-rollback trust bundles, journaled activation/startup recovery, and minimum device/package revocation. Commercial package delivery is blocked until real-path rotation, retirement, replay, reinstall, state-loss, revocation, interruption, and rollback tests pass.
2. **Phase 1 gate — Apple feasibility:** before mobile interfaces are finalized, build and run iOS/iPadOS camera/rotation, Inference Engine fixture, protected persistence, Keychain, lifecycle, Arabic, responsive layout, deletion, and protected macOS CI paths. Select the AOT/IL2CPP-safe SQLite provider on both platforms here.
3. **Phase 1 core — contracts:** define stable identifiers, versioned schemas, migration policy, shared serialization fixtures, and Domain/Application assembly boundaries while preserving current behavior.
4. **Phase 1 core — inventory:** implement exact quantity, package, mixed-observation, barcode, and catalogue v2 rules.
5. **Phase 1 core — offline:** adopt the validated SQLite provider for durable drafts, events, outbox, package journal, and legacy migration.
6. **Phase 1 core — cloud:** establish the modular backend, PostgreSQL RLS, OIDC mapping, authorization tests, regional operations, and audit baseline.
7. **Phase 1 core — device trust:** expand the Phase 0 boundary into production activation, proof of possession, rotating credentials, revocation, offline leases, and protected package delivery.
8. **Phase 1 core — synchronization and Android client:** add durable idempotency, cursors, explicit conflicts, supervisor review, responsive phone/tablet presentation, and end-to-end Android core acceptance.
9. **Phase 2 — customer operations:** add administration, assignments, reports, audit access, versioned API/CSV delivery, diagnostics, support procedures, and the first contracted ERP/POS connector.
10. **Phase 3 — model and Android certification:** complete the data/model lifecycle, statistically defined per-SKU scorecards, Android phone/tablet device matrix, physical-device evidence, customer acceptance, and Android commercial Go/No-Go.
11. **Phase 4 — Apple certification:** complete Apple platform hardening, managed signing, privacy declarations, device certification, customer evidence, and approved iPhone/iPad distribution.
12. **Phase 5 — additional verticals:** add contracted sensors, connectors, enterprise controls, lot/expiry or mall-tenancy domains, and newly certified categories.

## Official platform constraints

- Unity build profiles: https://docs.unity3d.com/6000.0/Documentation/Manual/build-profiles.html
- Unity iOS build process: https://docs.unity3d.com/6000.0/Documentation/Manual/iphone-BuildProcess.html
- Unity platform-dependent compilation: https://docs.unity3d.com/6000.0/Documentation/Manual/platform-dependent-compilation.html
- Android Keystore: https://developer.android.com/privacy-and-security/keystore
- Android Auto Backup: https://developer.android.com/identity/data/autobackup
- Apple Keychain data protection: https://support.apple.com/guide/security/keychain-data-protection-secb0694df1a/web
- Saudi PDPL and regulations: https://sdaia.gov.sa/en/SDAIA/about/Pages/RegulationsAndPolicies.aspx
- Saudi cloud cybersecurity controls: https://nca.gov.sa/en/regulatory-documents/controls-list/ccc/
