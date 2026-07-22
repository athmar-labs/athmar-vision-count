# Architecture

## Product boundary

Athmar Vision Count is an assisted mobile cycle-counting application. It performs camera inference on an Android device, proposes SKU quantities, allows an operator to correct them, and exports only a confirmed session. It is not an autonomous inventory posting engine.

## Runtime flow

1. The camera provider supplies frames while an operator is actively scanning.
2. A customer-approved model performs on-device inference through Unity AI Inference.
3. Model output is converted into `Detection` values with normalized bounds, SKU and confidence.
4. `CountingEngine` filters low-confidence detections and maintains short-lived spatial tracks to reduce duplicate counts.
5. The operator reviews proposed quantities and can set a manual count for any SKU.
6. `CountingEngine.Confirm` marks the reviewed session final.
7. `CsvExportService` exports only confirmed sessions.
8. `LocalScanRepository` stores confirmed session JSON in the application sandbox and supports complete local deletion.

## Assemblies

- `Athmar.VisionCount.Runtime`: domain models, counting, review, export, local storage and configuration.
- `Athmar.VisionCount.Editor`: production validation and signed Android build entry point.
- `Athmar.VisionCount.Tests.EditMode`: deterministic tests for counting and export safeguards.

## Trust boundaries

### Camera and inference

Raw camera frames remain in process memory and are not persisted by the default release. Model files and customer datasets are excluded from source control and must be delivered through an approved private artifact process.

### Local storage

Only confirmed count records are persisted. Device security, operating-system encryption, screen lock and managed-device controls remain customer deployment responsibilities.

### Optional network boundary

Network synchronization is disabled by default. Enabling it requires an approved HTTPS endpoint, customer-specific data contract, authentication design, audit logging, retention policy and incident-response contact.

### ERP boundary

Exports are files or explicit integrations initiated after review. Any future ERP connector must preserve idempotency, customer authorization, auditability and a human approval checkpoint.

## Model integration contract

The camera/model adapter is intentionally separate from the counting domain. A production adapter must:

- validate tensor dimensions and label mapping at startup;
- reject unknown labels;
- normalize coordinates to `[0, 1]`;
- cap inference frequency to the device performance budget;
- dispose tensors and workers deterministically;
- surface model/version metadata in diagnostics;
- fail closed if model integrity or configuration validation fails.

## Release controls

`ProductionReadinessValidator` blocks signed builds when the model, SKU catalogue, scene, Android identity, privacy version, review requirement or HTTPS policy is missing. Signing credentials are accepted only from environment variables during the release build and are restored after the build process.
