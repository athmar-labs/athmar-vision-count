# Production Readiness

## Implemented reusable product

- Unity `6000.3.10f1` with pinned inference, AR and test packages;
- runtime bootstrap and generated minimal production scene;
- rear-camera permission, lifecycle, preview and scan controls;
- on-device inference with GPU preference and CPU fallback;
- configurable YOLO-style tensor decoding, confidence filtering and class-aware NMS;
- conservative short-lived spatial tracking to reduce repeated counts;
- strict bilingual SKU catalogue parsing and unknown-label rejection;
- English/Arabic scan and review flow with manual correction;
- required operator and location references;
- mandatory human confirmation before final persistence or CSV export;
- atomic local JSON and UTF-8 CSV writes;
- configured retention enforcement and confirmed delete-all control;
- captured-image storage and network synchronization disabled by default;
- generated Android API 26, portrait, IL2CPP and ARM64 settings;
- production validation that fails closed when assets, versions, privacy or platform settings are missing;
- signed App Bundle generation with a SHA-256 sidecar;
- repository guardrails, Unity EditMode tests and protected private-asset materialization;
- security, privacy, architecture, model contract, commercial pilot and proprietary-license documentation.

## Hard launch gates

A paid build must not be released until every customer-specific item below is complete.

### Product and UX validation

- Review the generated scan and count-review interface on every supported screen size.
- Verify Arabic shaping, RTL alignment, English layout and the approved font/license package on devices.
- Add customer-approved icon, launch screen, product wording and distribution artwork.
- Verify camera permission denial, application pause/resume, low storage, interrupted export and delete-all behavior.
- Add or approve an About/Diagnostics surface showing application, model, catalogue and privacy versions.
- Complete accessibility and operator-usability review.

### Model and data

- Obtain written rights for the model, training/tuning data, labels and customer examples.
- Supply the immutable approved model and SHA-256 digest through the protected release environment.
- Supply the approved versioned SKU catalogue using `docs/MODEL_PACKAGE.md`.
- Verify model preprocessing, tensor layout, probability outputs and label mapping with known fixtures.
- Measure per-SKU accuracy against manually verified held-out ground truth.
- Document false-positive, false-negative and duplicate-count behavior.
- Define supported device, lighting, distance, packaging, occlusion, thermal and memory limits.

### Privacy and security

- Approve and publish the customer-specific privacy notice and set its immutable version.
- Approve the local retention period and optional synchronization terms.
- Keep captured-image storage disabled unless a separate review and written approval authorize it.
- Complete threat modeling for local records, model replacement, export tampering and any optional sync.
- Test local deletion, Android application-data removal and end-of-contract deletion.
- Enable GitHub private vulnerability reporting and protected production-environment approval.

### Android release

- Approve the package identifier, semantic version and monotonically increasing version code.
- Confirm API 26 or higher against the exact customer device baseline.
- Verify IL2CPP and ARM64 inference, stripping, memory, thermal behavior and battery use.
- Store signing material only in protected GitHub environment secrets.
- Generate and archive the signed App Bundle, SHA-256 file, test evidence and rollback build.
- Test clean install, upgrade, rollback and uninstall on physical devices.

### Commercial operations

- Sign the commercial license/SOW and customer data terms.
- Define supported devices, sites, conditions, SKU scope, service hours and escalation contacts.
- Agree pilot acceptance thresholds and remediation terms before testing.
- Prepare onboarding, operator training, support runbook, release notes and known limitations.
- Define change control, release ownership, support period and end-of-contract deletion.

## GitHub configuration required

Set repository variable:

- `UNITY_CI_ENABLED=true` after Unity activation works in Actions.

Set protected `production` environment secrets:

- `UNITY_LICENSE`, or the currently supported Unity activation credentials;
- `MODEL_PACKAGE_URL`;
- `MODEL_PACKAGE_SHA256`;
- `SKU_CATALOGUE_BASE64`;
- `ANDROID_KEYSTORE_BASE64`;
- `ANDROID_KEYSTORE_PASS`;
- `ANDROID_KEY_ALIAS`;
- `ANDROID_KEY_ALIAS_PASS`.

Set customer release variables:

- `ATHMAR_PRODUCT_NAME`;
- `ATHMAR_CUSTOMER_CODE`;
- `ATHMAR_DEFAULT_LANGUAGE` (`en` or `ar`);
- `ATHMAR_MODEL_VERSION`;
- `ATHMAR_CATALOGUE_VERSION`;
- `ATHMAR_PRIVACY_NOTICE_VERSION`;
- `ATHMAR_APP_VERSION`;
- `ATHMAR_ANDROID_VERSION_CODE`;
- `ATHMAR_ANDROID_APPLICATION_ID`;
- model input/output variables defined in `docs/MODEL_PACKAGE.md`;
- `ATHMAR_RETENTION_DAYS`.

Protect the `production` environment with required reviewers. Protect `main` with pull-request review and required `Repository guardrails` and `Unity EditMode tests` checks after Unity CI is enabled.

## Release procedure

1. Approve the model card, catalogue, privacy version, commercial scope and acceptance thresholds.
2. Configure protected secrets and immutable customer release variables.
3. Run Unity EditMode tests and the production validator.
4. Generate an internal signed candidate through `workflow_dispatch`.
5. Verify the candidate on the exact physical devices and held-out stock sessions.
6. Record results, defects, approved limitations and customer acceptance.
7. Merge only reviewed release changes and create a signed semantic version tag.
8. Verify the final App Bundle hash, installability, version metadata and smoke tests.
9. Distribute only through the customer-approved channel with release notes and support contacts.

## Current truth

The repository now contains the reusable application and guarded release path. It does not contain a legally approved customer model, customer SKU catalogue, signing key, Unity activation, device test evidence or signed commercial acceptance. Therefore no specific customer build can truthfully be called ready for sale until those external release inputs pass the gates above.
