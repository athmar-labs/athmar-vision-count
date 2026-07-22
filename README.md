# Athmar Vision Count

Privacy-first assisted inventory counting for Android, built with Unity, AR Foundation and Unity AI Inference. The product performs inference on the device, proposes SKU quantities, requires an operator to review or correct them, and exports only a confirmed session.

> **Release status:** production-oriented foundation. A paid customer build remains blocked until the customer-specific model, SKU catalogue, scene/UI, privacy approval, physical-device validation and signing configuration pass the documented release gates.

## Product principles

- On-device inference by default.
- Camera images and video are not stored by the privacy-first release.
- Network synchronization is disabled by default.
- Human confirmation is mandatory before export or integration.
- No autonomous updates to inventory, ERP, accounting or purchasing systems.
- Accuracy claims must be based on customer-approved ground truth under documented conditions.

## Implemented foundation

- Unity `6000.3.10f1` and pinned package dependencies.
- Runtime domain for confidence filtering and short-lived spatial deduplication.
- Manual count correction and explicit session confirmation.
- Confirmed CSV export and local JSON session storage.
- Complete local scan-data deletion.
- Privacy-first `AppConfig` asset.
- EditMode tests for counting and export safeguards.
- Production validator that fails closed if required release inputs are missing.
- Signed Android App Bundle build entry point.
- GitHub Actions guardrails, optional Unity tests and protected release build.
- Security, privacy, architecture, proprietary licensing and commercial pilot documentation.

## Repository layout

```text
Assets/AthmarVisionCount/Runtime/       Counting, review, export, storage and config
Assets/AthmarVisionCount/Editor/        Release validation and Android build method
Assets/AthmarVisionCount/Tests/EditMode Core deterministic tests
Packages/                               Unity package manifest
ProjectSettings/                        Pinned editor version
.github/workflows/                      CI and signed release workflow
docs/                                   Architecture, readiness and pilot framework
```

## Required customer configuration

1. Open the project with Unity `6000.3.10f1` and Android Build Support installed.
2. Create the production scene and add it to Build Settings.
3. Create exactly one `Athmar/Vision Count/App Config` asset.
4. Assign the legally usable customer model and approved SKU catalogue.
5. Keep human confirmation enabled and captured-image storage disabled.
6. Set the approved privacy notice version and retention period.
7. Configure the Android package identifier, semantic version, version code and minimum SDK.
8. Run **Athmar → Vision Count → Validate Production Readiness**.
9. Complete physical-device and customer acceptance testing.

Model files, datasets, customer content and signing keys are intentionally excluded from source control. Deliver them through an approved private artifact and secret-management process.

## Continuous integration

`Repository guardrails` always runs and rejects generated folders, signing material, model files, datasets and environment files.

After Unity activation is configured, set the repository variable:

```text
UNITY_CI_ENABLED=true
```

Configure the protected `production` environment with Unity and Android signing secrets described in [Production Readiness](docs/PRODUCTION_READINESS.md). Pull requests then run Unity EditMode tests. A manual run or signed `v*` tag generates a validated signed Android App Bundle.

## Commercial deployment

Start with a narrow paid pilot covering one approved SKU set, device baseline and environment. Define acceptance thresholds before testing and calculate results only from manually verified held-out sessions. See [Commercial Pilot Framework](docs/COMMERCIAL_PILOT.md).

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Production readiness and launch gates](docs/PRODUCTION_READINESS.md)
- [Commercial pilot framework](docs/COMMERCIAL_PILOT.md)
- [Privacy design](PRIVACY.md)
- [Security policy](SECURITY.md)
- [Proprietary software notice](LICENSE.md)

## License

Proprietary and confidential. Commercial use requires a written agreement with Athmar Labs. See [LICENSE.md](LICENSE.md).
