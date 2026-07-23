# Athmar Vision Count

Privacy-first assisted inventory counting for Android, built with Unity and Unity AI Inference. The application performs inference on the device, proposes SKU quantities, requires an operator to review or correct them, and stores or exports only a confirmed session.

> **Release status:** the generic mobile application and signed release pipeline are implemented. Customer-specific models, SKU catalogues and counting settings are installed from a protected administration interface after installation. Counting accuracy still requires an approved customer model and physical-device acceptance evidence.

## Implemented application

- One generic signed Android application for multiple customers.
- Protected in-app administrator interface with a locally salted and hashed 6–12 digit PIN.
- Runtime customer package installation from an HTTPS manifest URL plus a trusted manifest SHA-256.
- SHA-256 verification of the manifest, model and SKU catalogue before activation.
- Sentis model deserialization test, staging, atomic activation and rollback to the previous package.
- Customer-specific name, code, language, model version, catalogue version and model tensor contract supplied by the runtime package.
- Customer-specific confidence, NMS, duplicate suppression, tracking, inference interval, retention and optional HTTPS sync settings supplied by the runtime package.
- Rear-camera permission, startup, pause/resume and lifecycle handling.
- On-device Unity AI Inference worker with GPU preference and CPU fallback.
- Configurable YOLO-style output decoding for channels-first or rows-first tensors.
- Strict bilingual SKU catalogue validation and Arabic/English product names.
- Live camera preview, detection overlays and proposed-count summary.
- Arabic/English review workflow with manual count correction.
- Required operator and location references and explicit human confirmation before persistence or CSV export.
- No captured image storage and no network synchronization by default.

## Release engineering

- Unity `6000.3.10f1` and pinned package dependencies.
- Generated generic production scene, `Resources` bootstrap configuration and Android settings.
- Android API 26 baseline, portrait mode, IL2CPP and ARM64.
- Fail-closed production validator that rejects customer models and catalogues embedded in the generic App Bundle.
- Signed Android App Bundle build entry point with SHA-256 sidecar.
- GitHub Actions release requires only Unity activation and Android signing secrets plus general application metadata.
- Repository guardrails reject models, datasets, generated configuration, credentials and signing material.
- EditMode tests cover counting, confirmation, CSV escaping, retention, catalogue validation, tensor decoding, NMS, customer manifest validation, hashes, activation, rollback and PIN rules.

## Product safeguards

- Camera images and video are not persisted.
- Runtime package URLs and optional synchronization endpoints must use HTTPS.
- The administrator must provide the manifest SHA-256 through a trusted channel.
- Human confirmation is mandatory before export or integration.
- Inactive or unknown model labels fail closed and are not exported.
- No autonomous updates to inventory, ERP, accounting or purchasing systems.
- Accuracy claims require customer-approved held-out ground truth on supported devices and environments.

## Repository layout

```text
Assets/AthmarVisionCount/Runtime/       Camera, inference, customer packages, administration, UI and storage
Assets/AthmarVisionCount/Editor/        Generic bootstrap, validation and signed Android build
Assets/AthmarVisionCount/Tests/EditMode Deterministic EditMode tests
Packages/                               Pinned Unity package manifest
ProjectSettings/                        Pinned editor version
.github/workflows/                      Tests, guardrails and signed generic release
docs/                                   Architecture, package contract and launch gates
```

## Build-time release inputs

The protected release job requires only:

- `UNITY_LICENSE`
- `UNITY_EMAIL`
- `UNITY_PASSWORD`
- `ANDROID_KEYSTORE_BASE64`
- `ANDROID_KEYSTORE_PASS`
- `ANDROID_KEY_ALIAS`
- `ANDROID_KEY_ALIAS_PASS`
- general application variables such as product name, application ID and version

Customer models, SKU catalogues and tensor settings are no longer GitHub release secrets and are not embedded during the build.

## Runtime customer provisioning

A customer administrator supplies:

1. an HTTPS URL for `manifest.json`;
2. the trusted SHA-256 digest of that exact manifest.

The manifest contains HTTPS URLs and SHA-256 values for `production.sentis` and `sku_catalogue.csv`, together with the customer identity and compatible model settings. The app validates and installs the package in application-private storage.

See [Runtime customer packages](docs/RUNTIME_CUSTOMER_PACKAGES.md) for the manifest format, catalogue schema, hash commands and installation flow.

## Local preparation

1. Open with Unity `6000.3.10f1` and Android Build Support.
2. Run **Athmar → Vision Count → Prepare Production Project**.
3. Run **Athmar → Vision Count → Validate Production Readiness**.
4. Build the generic app. No customer model or catalogue is required at build time.
5. Install a validated customer package from the app administration interface.

## Continuous integration

`Repository guardrails` and Unity EditMode tests run on pull requests and pushes to `main`. A protected manual run or signed `v*` tag creates a signed generic `.aab` plus `.sha256` artifact. The release workflow no longer downloads customer assets.

## Current provisioning scope

The implemented provisioning path uses a trusted manifest URL and manually supplied manifest SHA-256. QR scanning, a hosted activation-code service, certificate-signed manifests, remote device revocation and automated model training are planned extensions and are not represented as already available.

## Commercial deployment

Use a narrow paid pilot covering an approved SKU set, device baseline and environment. Define acceptance thresholds before testing and calculate results only from manually verified held-out sessions. A successful CI build or successful package installation is not evidence of counting accuracy.

## Documentation

- [Runtime customer packages](docs/RUNTIME_CUSTOMER_PACKAGES.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Customer model package contract](docs/MODEL_PACKAGE.md)
- [Production readiness and launch gates](docs/PRODUCTION_READINESS.md)
- [Commercial pilot framework](docs/COMMERCIAL_PILOT.md)
- [Privacy design](PRIVACY.md)
- [Security policy](SECURITY.md)
- [Proprietary software notice](LICENSE.md)

## License

Proprietary and confidential. Commercial use requires a written agreement with Athmar Labs. See [LICENSE.md](LICENSE.md).
