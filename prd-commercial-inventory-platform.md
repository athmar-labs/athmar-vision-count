# PRD: Athmar intelligent physical inventory platform

## 1. Introduction

Athmar Vision Count will become a commercial, Arabic-first intelligent inventory platform for organizations that handle physical materials. The long-term vision covers restaurants, quick-service restaurants, cafés, supermarkets, malls, warehouses, workshops, retailers, and similar businesses, but each vertical and item category is supported only after its own certification evidence exists.

The first commercial release in Saudi Arabia targets restaurants, quick-service restaurants, and cafés. Its initial certified categories are visible packaged goods, individually countable kitchen and bar items, and customer-approved partial-quantity workflows. Supermarkets, mall operators, warehouses, workshops, lot/expiry workflows, and additional categories enter later release cohorts after their domain and acceptance requirements are defined.

Athmar uses a hybrid architecture. Counting and computer-vision inference continue on the device when offline. A secure multi-tenant cloud service manages organizations, branches, catalogues, device activation, signed model packages, synchronization, audit history, reports, and integrations. Human confirmation remains mandatory before inventory records affect an external system.

The first supported client is Android phones and tablets. iOS/iPadOS feasibility, platform abstraction, and build validation begin while the shared commercial core is developed; certified iPhone and iPad distribution follows the Android release.

## 2. Goals

- Reduce the time required for a supported stock-count session by at least 50% compared with the customer's measured manual baseline.
- Support inventory hierarchies from a base unit through packages, cases, and pallets with exact conversion rules.
- Support partial quantities using explicit count, weight, volume, fraction, barcode, and operator-confirmed inputs.
- Provide offline-first counting with reliable, idempotent synchronization when connectivity returns.
- Isolate every tenant, branch, user, device, catalogue, model package, and inventory record.
- Produce a complete audit trail from proposed machine count through human correction and final confirmation.
- Meet customer-approved, per-SKU accuracy thresholds on certified devices and supported operating conditions.
- Integrate through CSV and versioned APIs without autonomously posting unconfirmed counts to ERP, POS, accounting, or purchasing systems.
- Ship a protected, signed, observable, and reversible commercial release for Android, followed by iOS/iPadOS.
- Provide Arabic and English experiences suitable for operators, supervisors, customer administrators, and Athmar support staff.

## 3. Product principles

1. **Evidence before claims:** no accuracy or device-support claim without recorded acceptance evidence.
2. **Human accountability:** machine output is a proposal until an authorized operator confirms it.
3. **Offline first:** a site must be able to count during a network outage.
4. **Fail closed:** unknown items, invalid packages, impossible quantities, and incompatible models are rejected or sent to review.
5. **Tenant isolation:** data, models, users, devices, and reports cannot cross organization boundaries.
6. **Smallest trusted surface:** images stay on the device by default; collection requires explicit customer approval and policy.
7. **Auditable change:** catalogue, conversion, model, count, correction, export, and integration changes are versioned.
8. **Certified support, not universal claims:** supported devices and conditions are published from measured evidence.

## 4. Users and roles

- **Inventory operator:** performs and confirms assigned counts.
- **Shift or site supervisor:** reviews variances, reopens eligible sessions, and approves exceptions.
- **Customer administrator:** manages branches, users, devices, catalogues, units, policies, and integrations.
- **Customer auditor:** reads immutable count and change history without operational write access.
- **Athmar support operator:** sees explicitly authorized diagnostics but not customer inventory by default.
- **Athmar release administrator:** signs and publishes approved application and model releases under dual control.

## 5. Core domain model

- **Tenant:** legally and operationally isolated customer organization.
- **Branch:** physical customer site.
- **Location:** stock room, kitchen, freezer, bar, shelf, aisle, bin, preparation station, or other count area.
- **Identity principal:** a tenant-bound human or service identity with explicit branch scope, role assignments, lifecycle state, and authentication assurance.
- **Item:** stable customer inventory identity, independent of model labels and display names.
- **Unit of measure:** a dimensionally typed quantity unit such as piece, gram, kilogram, millilitre, or litre. Container names are not units of measure.
- **Package definition:** a named container such as tray, bottle, bag, box, case, or pallet with a positive conversion to a lower package or the base unit.
- **Measurement profile:** allowed modes, decimal scale, rounding rule, minimum increment, fraction denominators, plausible bounds, tare policy, and approved density source where mass-to-volume conversion is permitted.
- **Catalogue version:** immutable set of items, units, packages, barcodes, model labels, measurement profiles, and active dates.
- **Count assignment:** expected scope for a branch, location, item set, catalogue version, ownership policy, and deadline.
- **Count observation:** one or more machine, barcode, package, piece, weight, volume, fraction, or manual components normalized without discarding the originals.
- **Count session:** durable draft, review, confirmed, synchronized, superseded, or voided inventory event with explicit conflict state.
- **Model package:** signed and versioned inference model plus preprocessing, output contract, catalogue compatibility, and acceptance evidence.
- **Device:** tenant-bound installation with platform, hardware capability, application version, status, and last synchronization.

## 6. User stories

### US-001: Configure a tenant and branches
**Description:** As an Athmar administrator, I want to provision an isolated customer organization so that its users and inventory cannot mix with another customer's data.

**Acceptance criteria:**
- [ ] Every tenant has a unique immutable identifier and status.
- [ ] Branches and locations belong to exactly one tenant.
- [ ] Cross-tenant reads and writes are denied and covered by automated authorization tests.
- [ ] Tenant suspension blocks new synchronization while preserving an auditable recovery path.

### US-002: Define items, units, and packaging hierarchy
**Description:** As a customer administrator, I want to define base units and package conversions so that cases, packs, pieces, weights, and partial quantities produce one consistent stock value.

**Acceptance criteria:**
- [ ] An item has one dimensionally valid base unit and zero or more package definitions.
- [ ] Package conversion factors are positive, versioned, dimensionally compatible, and cannot form a cycle.
- [ ] Mixed observations such as two cases, three pieces, and half a pack normalize deterministically while preserving every component.
- [ ] Decimal scale, midpoint rounding, fraction denominators, and minimum increments are explicit and covered by boundary tests.
- [ ] Historical confirmed sessions retain the catalogue, measurement profile, and conversion version used at confirmation.

### US-003: Count packaged and individual goods using vision
**Description:** As an operator, I want the camera to propose quantities for supported items so that I count visible stock faster.

**Acceptance criteria:**
- [ ] Only labels included in the active compatible catalogue can create proposals.
- [ ] The UI shows confidence and a clear review state for uncertain detections.
- [ ] Duplicate suppression meets the documented recount-rate threshold for each certified scan pattern, camera motion, object spacing, and session duration.
- [ ] The operator can reset tracking and correct every proposed quantity before confirmation.

### US-004: Count partial and measurable stock
**Description:** As an operator, I want to record partial packages by fraction, weight, volume, or manual amount so that open kitchen and bar stock is represented accurately.

**Acceptance criteria:**
- [ ] The allowed measurement modes come from the item's measurement profile.
- [ ] Weight entries distinguish gross, tare, and net values; tare source and scale identity/calibration state are retained when available.
- [ ] Volume entries record direct measurement or the approved density profile and provenance used for conversion.
- [ ] Fractions use an allowed denominator, remain between zero and one package, and preserve numerator, denominator, and package version.
- [ ] Mixed components normalize using the configured decimal and rounding rules.
- [ ] Values outside configured bounds require supervisor review or are rejected.

### US-005: Scan barcodes
**Description:** As an operator, I want to scan item and package barcodes so that I can identify visually ambiguous products quickly.

**Acceptance criteria:**
- [ ] A barcode resolves only within the active tenant catalogue.
- [ ] Item-level and package-level barcodes produce the correct unit conversion.
- [ ] Unknown or duplicated barcodes do not alter the count and create a review event.

### US-006: Perform an offline count
**Description:** As an operator, I want to complete assigned counts without connectivity so that operations continue in kitchens, stores, and stock rooms with poor coverage.

**Acceptance criteria:**
- [ ] Authorized cached assignments and catalogues remain usable offline until their policy-defined expiry.
- [ ] Draft observations autosave durably and resume after application restart or device power loss.
- [ ] Confirmed sessions enter a durable pending-sync queue and are never silently rewritten.
- [ ] Expired authorization, stale catalogue, revoked assignment, and concurrent-count conflicts enter an explicit blocked or reconciliation state.
- [ ] The UI clearly distinguishes draft, local, pending, synchronized, blocked, conflicted, and failed records.

### US-007: Confirm and audit a session
**Description:** As an operator, I want to review and confirm a session so that only accountable final quantities are persisted and synchronized.

**Acceptance criteria:**
- [ ] Operator identity, branch, location, timestamps, application version, catalogue version, and model version are recorded.
- [ ] Proposed, corrected, and confirmed values remain distinguishable.
- [ ] Confirmation requires a complete scope or an explicit reason for each omitted item.
- [ ] A confirmed record is append-only; later changes create linked superseding events.

### US-008: Review variances and exceptions
**Description:** As a supervisor, I want to review unexpected variance and low-confidence results so that costly mistakes are caught before integration.

**Acceptance criteria:**
- [ ] Rules can flag absolute, percentage, and item-specific variances.
- [ ] Flagged sessions cannot be exported as approved until the required review is complete.
- [ ] Supervisor decisions include identity, timestamp, reason, and before/after values.

### US-009: Synchronize securely
**Description:** As a customer administrator, I want devices to synchronize safely so that retries do not create duplicate inventory events and concurrent work is reconciled explicitly.

**Acceptance criteria:**
- [ ] Synchronization uses authenticated, encrypted requests and tenant-bound device credentials.
- [ ] Immutable event IDs and durable server idempotency records provide effectively-once processing across retries.
- [ ] Retries use bounded exponential backoff and preserve original records.
- [ ] Server acknowledgement is durable before the device marks a record synchronized.
- [ ] Concurrent sessions and stale catalogue or authorization versions follow a versioned reject, merge, supersede, or supervisor-reconciliation rule.

### US-010: Export and integrate inventory
**Description:** As a customer administrator, I want approved inventory data through CSV and APIs so that existing ERP, POS, and reporting systems can consume it.

**Acceptance criteria:**
- [ ] CSV output neutralizes spreadsheet formulas and documents its schema version.
- [ ] APIs are versioned, authenticated, paginated, and tenant scoped.
- [ ] No unconfirmed or review-blocked session is delivered as approved inventory.
- [ ] Delivery attempts and acknowledgements are auditable and retryable.

### US-011: Activate a device securely
**Description:** As a customer administrator, I want to activate and revoke tenant devices so that only approved installations receive catalogues, assignments, and models.

**Acceptance criteria:**
- [ ] Activation uses a short-lived, single-use authorization.
- [ ] Device credentials are protected by platform secure storage.
- [ ] Revoked devices cannot refresh packages or synchronize new records.
- [ ] Lost-device revocation and replacement are documented and tested.

### US-012: Publish trusted model packages
**Description:** As an Athmar release administrator, I want to sign approved model packages so that devices can verify publisher authenticity, compatibility, and integrity.

**Acceptance criteria:**
- [ ] A package manifest is digitally signed by an approved key.
- [ ] The manifest binds tenant, catalogue version, model contract, application compatibility, expiry policy, and asset hashes.
- [ ] The app validates model input/output shape and executes a known fixture before activation.
- [ ] Interrupted activation recovers deterministically to a known-good package.

### US-013: Enforce retention and deletion
**Description:** As a customer administrator, I want tenant-approved retention to apply to local and cloud data so that records are not retained beyond policy.

**Acceptance criteria:**
- [ ] The effective active tenant policy governs local session and export deletion.
- [ ] Retention runs at startup, periodically, and after relevant policy changes.
- [ ] Malformed records and deletion failures create visible administrative alerts.
- [ ] End-of-contract deletion produces a verifiable completion report.

### US-014: Operate in Arabic and English
**Description:** As an operator, I want a clear Arabic or English interface so that I can count inventory without language friction.

**Acceptance criteria:**
- [ ] All operator, review, administration, error, and accessibility text is localized.
- [ ] Arabic shaping, RTL layout, mixed SKU text, numerals, and input fields pass the supported-device matrix.
- [ ] Language can be controlled by tenant policy and changed by an authorized user.

### US-015: Diagnose production safely
**Description:** As authorized support staff, I want privacy-safe diagnostics so that failures can be resolved without exposing unnecessary customer data.

**Acceptance criteria:**
- [ ] Diagnostics show application, device, model, catalogue, policy, queue, and last-sync versions/status.
- [ ] Logs use stable error codes and redact credentials, URLs, operator references, and inventory values by default.
- [ ] Diagnostic export requires explicit authorization and records an audit event.

### US-016: Release Android commercially
**Description:** As the product owner, I want a signed and reversible Android release so that approved customers can install a supported production build.

**Acceptance criteria:**
- [ ] The exact release commit passes guardrails, automated tests, signed AAB build, manifest inspection, and physical-device acceptance.
- [ ] Release metadata and Android version code are explicit and monotonically increasing.
- [ ] The release has immutable provenance, checksums, notes, known limitations, and a retained rollback artifact.
- [ ] Production deployment requires an authorized human approval.

### US-017: Release iPhone and iPad
**Description:** As the product owner, I want the validated shared workflow available on iOS and iPadOS so that supported Apple devices can serve customer sites.

**Acceptance criteria:**
- [ ] A Phase 1 feasibility build validates platform abstractions, camera access, inference support, local persistence, and CI requirements before Android architecture is finalized.
- [ ] Camera, local storage, secure credentials, backgrounding, inference, localization, synchronization, and deletion pass on the published Apple device matrix.
- [ ] The iOS build uses managed signing and protected App Store Connect credentials.
- [ ] Platform privacy declarations and review requirements are complete.
- [ ] Accuracy evidence is recorded separately for each supported Apple device class.

### US-018: Manage user identity and authorization
**Description:** As a customer administrator, I want controlled identity and role lifecycle management so that every action is attributable and branch scoped.

**Acceptance criteria:**
- [ ] Users have tenant-bound identities, branch scopes, role assignments, and active, suspended, or revoked states.
- [ ] A versioned permission matrix separates operation, supervision, administration, audit, support, and release duties.
- [ ] Offline confirmation records authenticated cached identity and assurance time; expired or revoked identities require reconciliation before synchronization.
- [ ] Deprovisioning blocks new actions without rewriting historical audit identity.
- [ ] Operator identity comes from authentication context and cannot be only unrestricted free text.

### US-019: Certify Android tablet workflows
**Description:** As an operator, I want a responsive tablet workflow so that larger Android devices improve rather than obstruct counting.

**Acceptance criteria:**
- [ ] Published tablet classes pass approved orientations, window resizing, lifecycle, camera placement, and external scanner input tests.
- [ ] Critical count, confidence, review, and synchronization state remains visible at every supported resolution and text scale.
- [ ] Phone and tablet layouts share domain behavior and acceptance fixtures.
- [ ] Split-screen and unsupported orientation behavior are either tested and supported or explicitly blocked and documented.

## 7. Functional requirements

### Catalogue and quantity model

- **FR-1:** The platform must model each item's dimensionally typed base unit, allowed measurement modes, decimal scale, rounding rule, and minimum increment.
- **FR-2:** The platform must support acyclic, positive, dimensionally compatible, versioned package conversions with effective dates.
- **FR-3:** The platform must preserve every mixed observation component and its deterministic normalized base-unit quantity.
- **FR-4:** The platform must support piece, package, barcode, fraction, gross/tare/net weight, direct volume, approved density conversion, and manual correction.
- **FR-5:** Catalogue and measurement-profile updates must not reinterpret previously confirmed sessions.

### Counting workflow

- **FR-6:** The application must support assignments scoped by tenant, branch, location, catalogue, and item list.
- **FR-7:** Every machine-proposed count must remain editable before confirmation.
- **FR-8:** Unknown, inactive, incompatible, or low-confidence detections must fail closed or enter review.
- **FR-9:** Confirmation must record operator identity and immutable runtime versions.
- **FR-10:** Confirmed inventory events must be append-only and superseded rather than overwritten.

### Cloud and synchronization

- **FR-11:** The service must enforce tenant isolation in authentication, authorization, queries, storage, and observability.
- **FR-12:** Devices must use revocable tenant-bound credentials stored in platform secure storage.
- **FR-13:** Synchronization must use durable idempotency records, be retryable, resumable, observable, and define stale-version and concurrent-assignment reconciliation.
- **FR-14:** The platform must expose versioned APIs and spreadsheet-safe CSV exports.
- **FR-15:** Integrations must not deliver unconfirmed or approval-blocked counts as final records.
- **FR-31:** Identity lifecycle, branch scoping, role permissions, separation of duties, and offline assurance rules must be authorization-tested.
- **FR-32:** Draft sessions must autosave durably, preserve catalogue and assignment versions, and resume or reconcile after interruption.

### Model lifecycle

- **FR-16:** Model manifests must be digitally signed and bind all assets and compatibility settings.
- **FR-17:** Downloads must stream to bounded temporary storage with time, size, and free-space limits.
- **FR-18:** Activation must validate hashes, signature, catalogue compatibility, tensor contract, and a known inference fixture.
- **FR-19:** Activation and rollback must recover safely after process termination or power loss.
- **FR-20:** Devices must reject expired, revoked, tenant-mismatched, or application-incompatible packages.

### Security and privacy

- **FR-21:** Administrative access must use rate limiting, secure credential storage, session expiry, and reauthentication for destructive actions.
- **FR-22:** Android and iOS backup policies must exclude protected local records and credentials unless an approved encrypted recovery design exists.
- **FR-23:** Retention must apply consistently across devices, exports, cloud records, logs, and backups.
- **FR-24:** Captured images must not persist or upload unless a separate, explicit customer data-collection policy is enabled.
- **FR-25:** Security-sensitive release and model-signing actions must require dual control and auditable approval.

### Operations

- **FR-26:** The platform must provide health, synchronization, crash, package, and release telemetry without leaking customer data.
- **FR-27:** Releases must support staged rollout, suspension, rollback, and customer-visible version status.
- **FR-28:** Supported device claims must come from a versioned certification matrix.
- **FR-29:** Arabic and English localization and accessibility must be release gates.
- **FR-30:** Customer support and incident response must use documented severity, ownership, response, escalation, and recovery procedures.

## 8. Non-goals

- Inferring hidden stock that is not observable by an approved sensor or operator input.
- Claiming exact remaining volume or weight from an ordinary RGB image when the container and validated measurement model do not support it.
- Recognizing every commercial SKU without customer-specific catalogue, data, model validation, and acceptance evidence.
- Autonomously changing ERP, accounting, purchasing, or payment records without customer-approved confirmation and integration rules.
- Replacing certified weighing equipment or regulated metrology where legal certification is required.
- Persisting customer camera images by default.
- Supporting every Android or Apple device; support is limited to published, tested device and operating-system ranges.
- Providing batch/lot, expiry, serial-number, concession ownership, or mall-tenant settlement in the first restaurant/café release unless separately contracted.
- Representing supermarkets, malls, warehouses, or workshops as certified before category-specific evidence passes.
- Building all ERP/POS connectors before the versioned integration API and first contracted connector are validated.

## 9. Design considerations

- The primary operator flow must be usable with gloves, one hand, and intermittent connectivity.
- Count status, uncertainty, pending synchronization, and required review must be visible without relying on color alone.
- Arabic is a first-class layout, not a translated English afterthought.
- The application should minimize typing by using assignments, barcode resolution, recent locations, numeric controls, and supervisor exception flows.
- Partial-quantity input must show the entered unit, normalized quantity, conversion source, and precision.
- Destructive actions require explicit confirmation and, where appropriate, reauthentication.
- Tablet layouts must define supported orientations and window modes and use available space without hiding critical state.

## 10. Technical considerations

- Preserve the existing Unity 6 on-device inference foundation while separating domain logic from Unity-specific presentation and platform services.
- Introduce a versioned domain schema before adding cloud synchronization; quantity conversion must use a decimal representation and explicit units.
- Use a hybrid multi-tenant backend with strong authorization boundaries, immutable inventory events, idempotent APIs, and regional deployment suitable for Saudi customers.
- Platform secure storage must protect device credentials and local administrative secrets.
- Signed model manifests require key rotation, revocation, offline verification, and separation between application-signing and model-signing keys.
- CI must pin third-party actions to reviewed commit SHAs and build the exact release commit.
- Android remains the first certified client. An iOS/iPadOS build and platform-abstraction spike runs during Phase 1; certification follows Android.
- Phase 1 supports validated manual weight and volume readings. Hardware sensor integration is a contracted adapter that records identity, calibration, unit, precision, and time.
- Observability must avoid raw inventory values and personal identifiers by default.

## 11. Quality and launch gates

A commercial deployment cannot be approved until all applicable gates have evidence:

1. **Correctness:** domain, conversion, persistence, synchronization, and migration tests pass.
2. **Security:** threat model, tenant-isolation tests, credential storage, signed updates, backup policy, and external review pass.
3. **Model:** approved data rights, immutable package, per-SKU results, sample sizes, confidence intervals, supported-condition strata, correction and recount rates, and known failures are recorded against pre-agreed formulas.
4. **Device:** camera, inference, memory, thermal, battery, lifecycle, storage, interruption, upgrade, rollback, deletion, responsive layout, and orientation policy pass on each supported phone or tablet class.
5. **Operations:** monitoring, support, incident response, rollback, retention, backup, and end-of-contract deletion are rehearsed.
6. **Release:** protected approval builds and signs the exact commit, emits provenance, and deploys through an approved distribution channel.
7. **Commercial:** contract, privacy terms, service scope, support boundaries, acceptance thresholds, and customer acceptance are signed.

## 12. Success metrics

- Median supported count-session duration is at least 50% lower than the measured customer manual baseline.
- At least 95% of operators complete training and their first assigned session without support intervention.
- 100% of finalized records have an identified confirmer and complete version/audit metadata.
- No unconfirmed record is posted as final to an external integration.
- Crash-free supported-device sessions are at least 99.5% during launch and target 99.9% after stabilization.
- At least 99.9% of accepted synchronization events are durably acknowledged within the agreed window, and replay tests create zero duplicate server events.
- Per-SKU precision, recall, absolute and relative count error, correction rate, and exception rate meet pre-agreed thresholds by device and condition stratum.
- For ground truth `g > 0` and proposal `p`, relative count error is `abs(p - g) / g`; reports include median, 95th percentile, and sample count.
- Initial thresholds are contracted per category after baseline collection; aspirational targets are not claims until sample-size and confidence gates pass.
- Retention and deletion verification achieves 100% compliance in automated and operational audits.
- Every production release has a tested rollback artifact and recorded Go/No-Go approval.

## 13. Delivery sequence

### Phase 0: Production rescue and trust foundation

Close the verified retention, bounded-download, full model-contract, CSV-injection, credential, backup, activation-recovery, release-protection, and test gaps. Implement signed tenant-bound manifests, deterministic startup recovery, and minimum device revocation before commercial remote synchronization.

### Phase 1: Commercial Android core and Apple feasibility

Introduce the versioned quantity domain, mixed observations, barcode workflow, durable drafts, secure identity and device activation, signed package delivery, conflict-aware synchronization, supervisor review, responsive phone/tablet architecture, and an iOS/iPadOS feasibility build.

### Phase 2: Customer operations

Add administration, assignments, reports, audit, API/CSV integration, diagnostics, support procedures, and the first contracted connector.

### Phase 3: Model and Android device certification

Publish the data/model lifecycle, evaluation harness, statistically defined scorecards, phone/tablet matrix, and acceptance evidence for restaurant and café categories.

### Phase 4: iPhone and iPad certification and release

Complete Apple platform services, managed CI/signing, privacy declarations, device certification, and approved distribution.

### Phase 5: Additional verticals and scale

Add contracted sensors, connectors, regional capacity, enterprise identity, fleet policy, advanced analytics, lot/expiry or mall-tenancy domains, and additional certified categories.

## 14. Definition of done

A requirement is done only when implementation, real-path automated tests, migration or compatibility handling, security review, operational documentation, telemetry, and acceptance evidence are complete. Passing unit tests alone is not sufficient evidence for camera, model, device, integration, or release behavior.

## 15. Decided assumptions

- Initial market: Saudi Arabia, with Arabic and English support.
- Architecture: hybrid on-device counting plus secure multi-tenant cloud services.
- Integration: versioned APIs and safe exports, followed by demand-driven ERP/POS connectors.
- Initial release cohort: Saudi restaurants, quick-service restaurants, and cafés; later verticals require separate certification.
- Measurement: camera and barcode plus human-confirmed manual piece, package, fraction, weight, and volume readings; hardware sensors are contracted adapters.
- Traceability: lot, expiry, serial, concession ownership, and mall settlement are outside the initial cohort unless separately specified.
- Platform order: responsive Android phone/tablet first, with an iOS/iPadOS feasibility build in Phase 1 and certified Apple distribution after Android.
- Product posture: direct commercial target with internal controlled validation gates; no unverified universal-accuracy or universal-device claim.

## 16. Open questions tracked for customer discovery

These questions do not block Production Rescue but must be answered before the first contracted deployment:

- Which customer and branches form the first acceptance cohort?
- Which ERP/POS connector is contractually first?
- What are the item-level economic risk tiers and acceptance thresholds?
- Which approved scales, sensors, or container profiles are needed for partial stock?
- What Saudi hosting, residency, retention, identity, and contractual requirements apply to the first customer?
- What Android device models are already deployed at the first customer?
