# Athmar Vision Count

Privacy-first assisted inventory counting for Android, built with Unity and Unity AI Inference. The application performs inference on the device, proposes SKU quantities, requires an operator to review or correct them, and stores or exports only a confirmed session.

> **Release status:** the reusable mobile application and protected release pipeline are implemented. A customer-sellable signed build is intentionally blocked until an approved customer model, SKU catalogue, privacy version, Unity activation, Android signing credentials and physical-device acceptance evidence are supplied.

## Implemented application

- Automatic mobile runtime bootstrap from a minimal production scene.
- Rear-camera permission, startup, pause/resume and lifecycle handling.
- On-device Unity AI Inference worker with GPU preference and CPU fallback.
- Configurable YOLO-style output decoding for channels-first or rows-first tensors.
- Confidence filtering, class-aware non-max suppression and conservative temporal/spatial deduplication.
- Strict bilingual SKU catalogue validation and Arabic/English product names.
- Live camera preview, detection overlays and proposed-count summary.
- Arabic/English review workflow with manual count correction.
- Required operator and location references.
- Explicit human confirmation before persistence or CSV export.
- Atomic local JSON and UTF-8 CSV writes.
- Configured retention enforcement and confirmed delete-all control.
- No captured image storage and no network sync by default.

## Release engineering

- Unity `6000.3.10f1` and pinned package dependencies.
- Generated production scene, `Resources` configuration and Android settings.
- Android API 26 baseline, portrait mode, IL2CPP and ARM64.
- Fail-closed production validator for model, catalogue, versions, privacy, scene and platform settings.
- Signed Android App Bundle build entry point with SHA-256 sidecar.
- Protected GitHub Actions release path that downloads the private model over HTTPS, verifies its hash, materializes the private catalogue and signing key, builds, then removes sensitive working files.
- Repository guardrails rejecting models, datasets, generated customer configuration, credentials and signing material.
- EditMode tests for counting, confirmation, CSV escaping, retention, catalogue validation, tensor decoding and NMS.

## Product safeguards

- Camera images and video are not persisted.
- Network synchronization is disabled unless separately approved and configured over HTTPS.
- Human confirmation is mandatory before export or integration.
- Inactive or unknown model labels fail closed and are not exported.
- No autonomous updates to inventory, ERP, accounting or purchasing systems.
- Accuracy claims require customer-approved held-out ground truth on supported devices and environments.

## Repository layout

```text
Assets/AthmarVisionCount/Runtime/       Camera, inference, counting, UI, export and storage
Assets/AthmarVisionCount/Editor/        Customer bootstrap, validation and signed Android build
Assets/AthmarVisionCount/Tests/EditMode Deterministic EditMode tests
Packages/                               Pinned Unity package manifest
ProjectSettings/                        Pinned editor version
.github/workflows/                      Tests, guardrails and protected signed release
Docs/ and docs/                         Architecture, model contract and launch gates
```

## Customer release inputs

The protected `production` environment requires:

- Unity activation secrets.
- Android keystore, passwords and alias secrets.
- A short-lived HTTPS model URL and expected SHA-256 hash.
- A base64-encoded approved SKU catalogue.
- Immutable model, catalogue, privacy and app versions.
- Customer package identifier and model tensor settings.

See [Customer Model Package Contract](docs/MODEL_PACKAGE.md) for the tensor and catalogue formats. Model files, datasets, customer content and signing keys are intentionally excluded from source control.

## Local preparation

1. Open with Unity `6000.3.10f1` and Android Build Support.
2. Put the approved model at `Assets/PrivateModels/production.sentis`.
3. Put the approved catalogue at `Assets/PrivateConfig/sku_catalogue.csv`.
4. Set the `ATHMAR_*` environment values documented in the model contract.
5. Run **Athmar → Vision Count → Prepare Production Project**.
6. Run **Athmar → Vision Count → Validate Production Readiness**.
7. Build only after device and acceptance gates are satisfied.

## Continuous integration

`Repository guardrails` always runs. After Unity activation is configured, set:

```text
UNITY_CI_ENABLED=true
```

Pull requests then run Unity EditMode tests. A protected manual run or signed `v*` tag materializes the customer-specific private inputs and creates a signed `.aab` plus `.sha256` file. Missing or inconsistent inputs cause the release to fail.

## Commercial deployment

Use a narrow paid pilot covering an approved SKU set, device baseline and environment. Define acceptance thresholds before testing and calculate results only from manually verified held-out sessions. A successful CI build is not evidence of counting accuracy.

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Customer model package contract](docs/MODEL_PACKAGE.md)
- [Production readiness and launch gates](docs/PRODUCTION_READINESS.md)
- [Commercial pilot framework](docs/COMMERCIAL_PILOT.md)
- [Privacy design](PRIVACY.md)
- [Security policy](SECURITY.md)
- [Proprietary software notice](LICENSE.md)

## License

Proprietary and confidential. Commercial use requires a written agreement with Athmar Labs. See [LICENSE.md](LICENSE.md).
