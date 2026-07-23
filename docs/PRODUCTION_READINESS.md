# Production Readiness

## Implemented reusable product

- Unity `6000.3.10f1` with pinned inference, AR, web-request and test packages;
- one generic signed Android application for multiple customers;
- runtime bootstrap and generated minimal production scene;
- protected administrator interface with local PIN verification;
- HTTPS customer manifest installation with SHA-256 verification;
- pinned model and catalogue hashes, staging, atomic activation and rollback;
- runtime loading of customer Sentis models from application-private storage;
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
- production validation that rejects customer assets embedded in the generic build;
- signed App Bundle generation with a SHA-256 sidecar;
- repository guardrails, Unity EditMode tests and protected signing-key materialization.

## Hard launch gates

A paid deployment must not be represented as production-ready until every applicable item below is complete.

### Product and UX validation

- Review the generated scan, review and administration interfaces on every supported screen size.
- Verify Arabic shaping, RTL alignment, English layout and the approved font/license package on devices.
- Add and approve the generic application icon, launch screen, product wording and distribution artwork.
- Verify camera permission denial, application pause/resume, low storage, interrupted package download, interrupted export and delete-all behavior.
- Add or approve an About/Diagnostics surface showing application, customer, model, catalogue and privacy versions.
- Complete accessibility and operator-usability review.

### Model and data

- Obtain written rights for the model, training/tuning data, labels and customer examples.
- Publish an immutable approved runtime package according to `docs/RUNTIME_CUSTOMER_PACKAGES.md`.
- Deliver the manifest SHA-256 through a trusted channel separate from or protected relative to the download URL.
- Verify model preprocessing, tensor layout, probability outputs and label mapping with known fixtures.
- Measure per-SKU accuracy against manually verified held-out ground truth.
- Document false-positive, false-negative and duplicate-count behavior.
- Define supported device, lighting, distance, packaging, occlusion, thermal and memory limits.

### Privacy and security

- Approve and publish the customer-specific privacy notice and put its immutable version in the manifest.
- Approve the local retention period and optional synchronization terms.
- Keep captured-image storage disabled unless a separate review and written approval authorize it.
- Complete threat modeling for administrator access, local records, model replacement, manifest trust, export tampering and optional sync.
- Test invalid hashes, malformed manifests, corrupted downloads, rollback and recovery after interrupted installation.
- Test local deletion, Android application-data removal and end-of-contract deletion.
- Plan signed manifests, activation-code authorization and device revocation before broad unattended deployment.
- Enable GitHub private vulnerability reporting and protected production-environment approval.

### Android release

- Approve the package identifier, semantic version and monotonically increasing version code.
- Confirm API 26 or higher against the supported device baseline.
- Verify IL2CPP and ARM64 inference, stripping, memory, thermal behavior and battery use.
- Store signing material only in protected GitHub secrets and maintain an offline recovery copy.
- Generate and archive the signed generic App Bundle, SHA-256 file, test evidence and rollback build.
- Test clean install, upgrade, customer-package install, package update, package rollback and uninstall on physical devices.

### Commercial operations

- Sign the commercial license/SOW and customer data terms.
- Define supported devices, sites, conditions, SKU scope, service hours and escalation contacts.
- Agree pilot acceptance thresholds and remediation terms before testing.
- Prepare onboarding, administrator PIN recovery policy, operator training, support runbook, release notes and known limitations.
- Define customer-package change control, approval, hosting, support period and end-of-contract deletion.

## GitHub configuration required

Repository or protected-environment secrets:

- `UNITY_LICENSE`;
- `UNITY_EMAIL`;
- `UNITY_PASSWORD`;
- `ANDROID_KEYSTORE_BASE64`;
- `ANDROID_KEYSTORE_PASS`;
- `ANDROID_KEY_ALIAS`;
- `ANDROID_KEY_ALIAS_PASS`.

General repository variables:

- `ATHMAR_PRODUCT_NAME`;
- `ATHMAR_DEFAULT_LANGUAGE` (`en` or `ar`);
- `ATHMAR_PRIVACY_NOTICE_VERSION`;
- `ATHMAR_APP_VERSION`;
- `ATHMAR_ANDROID_VERSION_CODE`;
- `ATHMAR_ANDROID_APPLICATION_ID`;
- `ATHMAR_RETENTION_DAYS`.

Customer model URLs, hashes, SKU catalogues, model versions, customer codes and tensor settings are runtime manifest data. They are not GitHub release secrets or build variables.

Protect the `production` environment with required reviewers. Protect `main` with pull-request review and required `Repository guardrails` and `Unity EditMode tests` checks.

## Generic release procedure

1. Review application changes and run repository guardrails and Unity EditMode tests.
2. Confirm Unity activation and Android signing secrets.
3. Run the generic production validator.
4. Generate an internal signed candidate through `workflow_dispatch`.
5. Verify the candidate on supported physical Android devices.
6. Record application-level results and defects.
7. Merge only reviewed release changes and create a signed semantic version tag.
8. Verify the final App Bundle hash, installability, version metadata and smoke tests.
9. Distribute only through the approved channel with release notes and support contacts.

## Customer package release procedure

1. Approve the model card, catalogue, privacy version, commercial scope and acceptance thresholds.
2. Calculate model and catalogue hashes.
3. Create the final manifest containing those hashes and compatible model settings.
4. Calculate the final manifest hash last.
5. Host the three immutable files over HTTPS with appropriate access controls.
6. Deliver the manifest URL and trusted manifest hash to the authorized administrator.
7. Install through the app administration interface and verify activation.
8. Test on the exact physical devices and held-out stock sessions.
9. Record results, defects, approved limitations and customer acceptance.

## Current truth

The repository contains the generic application, guarded release path and runtime package installer. It does not contain a legally approved customer model, customer SKU catalogue, device acceptance evidence or signed commercial acceptance. The manual URL-plus-hash provisioning flow is implemented; QR activation, a hosted activation-code service, digitally signed manifests and remote revocation are not yet implemented. Therefore no customer deployment can truthfully be called ready for sale until its external model, package, device and acceptance gates pass.
