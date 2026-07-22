# Production Readiness

## What is implemented in this branch

- pinned Unity editor and package manifest;
- privacy-first configuration with network sync and image storage disabled by default;
- deterministic counting domain with confidence filtering, short-lived spatial deduplication and manual correction;
- mandatory human confirmation before export or final local persistence;
- CSV export and confirmed-session JSON storage;
- local data deletion capability;
- EditMode tests for core safeguards;
- automated repository guardrails;
- optional Unity CI tests and signed Android App Bundle generation;
- production validation that fails closed when required assets or settings are missing;
- security, privacy, architecture, commercial pilot and proprietary license documentation.

## Hard launch gates

A paid build must not be released until every item below is complete for the target customer.

### Product and UX

- Create the production scene and add it to Build Settings.
- Implement the camera permission flow, active-scanning indication, count review screen, manual correction, confirmation, export, deletion and error states.
- Complete Arabic RTL and English UX review.
- Verify accessibility, font licensing and small-screen layouts.
- Display product version, model version and privacy notice version in an About/Diagnostics screen.

### Model and data

- Obtain a written right to use the model, training data, labels and customer SKU catalogue.
- Train or fine-tune on representative customer-approved stock and environments.
- Establish a versioned label map and reject unknown labels.
- Measure accuracy against manually verified ground truth.
- Document false-positive, false-negative and duplicate-count behavior.
- Define device-specific inference frequency, thermal and memory budgets.

### Privacy and security

- Approve and publish the customer-specific privacy notice.
- Configure the approved retention period.
- Keep captured-image storage disabled unless separately reviewed and approved.
- Complete threat modeling for local data, model replacement, export tampering and optional network sync.
- Test local deletion and Android application-data removal.
- Enable GitHub private vulnerability reporting and protected production environment approval.

### Android release

- Set a unique package identifier and semantic version.
- Set Android minimum SDK API 26 or higher and confirm the customer device baseline.
- Add adaptive icons, launch screen and store assets.
- Configure IL2CPP/ARM64 and verify stripping does not break inference.
- Store signing material only in protected GitHub environment secrets.
- Generate and archive a signed App Bundle and mapping symbols.
- Test install, upgrade, rollback and uninstall behavior on physical devices.

### Commercial operations

- Sign the commercial license/SOW and customer data terms.
- Define supported devices, site conditions, SKU scope, service hours and escalation contacts.
- Agree acceptance thresholds before the pilot starts.
- Prepare onboarding, operator training, support runbook and incident response.
- Define release ownership, change control, backup/restore expectations and end-of-contract data deletion.

## GitHub configuration required

Set repository variable:

- `UNITY_CI_ENABLED=true` after Unity licensing is configured.

Set repository or protected environment secrets:

- `UNITY_LICENSE` or the supported Unity activation credentials;
- `ANDROID_KEYSTORE_BASE64`;
- `ANDROID_KEYSTORE_PASS`;
- `ANDROID_KEY_ALIAS`;
- `ANDROID_KEY_ALIAS_PASS`.

Protect the `production` environment with required reviewers. Protect `main` with pull-request review and required `Repository guardrails` and `Unity EditMode tests` checks after CI is enabled.

## Release procedure

1. Complete customer configuration, model and SKU catalogue in a release branch.
2. Run EditMode tests and the production readiness menu command in Unity.
3. Perform physical-device acceptance testing and record results.
4. Merge through a reviewed pull request.
5. Create a signed semantic version tag such as `v1.0.0-pilot.1`.
6. Let GitHub Actions generate the signed App Bundle.
7. Verify artifact hash, installability, version metadata and smoke tests.
8. Distribute only through the customer-approved channel.

## Current truth

This branch provides a production-oriented foundation and release controls. It cannot truthfully be called customer-ready until the customer-specific model, catalogue, scene/UI, privacy approval, physical-device validation and signing configuration have passed the gates above.
