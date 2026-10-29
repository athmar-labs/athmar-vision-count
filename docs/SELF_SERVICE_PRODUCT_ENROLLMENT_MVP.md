# Self-Service Product Enrollment MVP

## Product goal

Athmar Vision Count must remain one generic Android application for every customer.

Adding a customer's products must **not** require:

- a new Unity project;
- a customer-specific APK/AAB;
- a code change;
- a GitHub commit;
- retraining the detector for every new SKU.

The active customer identity still comes from the protected runtime customer package. Enrolled products are then created locally by an authenticated administrator inside the same app.

## MVP user flow

1. Open **Administration** and unlock with the administrator PIN.
2. Tap **Add & Manage Products**.
3. Enter SKU, Arabic and/or English product name, and optional barcode.
4. Place one product so it fills most of the camera frame.
5. Capture **8 to 15** reference views while changing angle and distance slightly.
6. Tap **Save Product**.
7. The app stores the product only in the active customer's private storage scope.
8. Put the product in front of the camera and tap **Test Recognition** for a one-product enrollment smoke test.
9. Repeat for 5 products, then 20, then 100.

Entering an existing SKU replaces that SKU's reference set instead of creating a duplicate.

## Privacy and tenant isolation

Reference camera frames are used only long enough to calculate a local neural feature vector. Raw enrollment photos are not written to disk.

Persisted enrollment data is stored under:

```text
Application.persistentDataPath/
  product-enrollments/
    <SHA-256(customerCode)>/
      catalogue.json
      catalogue.json.bak
```

The stored catalogue also contains `customerCode`. Loading fails closed if the directory scope and stored customer identity do not agree. Barcodes must be unique within one customer's catalogue.

## Recognition contract

The storage and matching layer is deliberately independent from YOLO class labels.

```text
camera/product crop
        |
        v
IProductEmbeddingExtractor
        |
        v
float[] learned embedding
        |
        v
ProductRecognitionMatcher
        |
        +--> known SKU when similarity + margin pass
        |
        +--> Unknown / Ambiguous otherwise
```

## Generic Sentis embedding model

The runtime implementation is now `SentisProductEmbeddingExtractor`. It uses one shared MobileNetV3 Small 0.75 neural feature backbone for every customer through Unity Inference Engine/Sentis.

The prepared model contract is:

- input: `1 x 224 x 224 x 3`, NHWC RGB values in the 0..1 range;
- ImageNet mean/std normalization is embedded into the generated ONNX graph;
- output: one 1024-dimensional pre-classifier learned feature vector;
- output is L2-normalized before storage/matching;
- backend: GPUCompute when supported, otherwise CPU;
- customer code, SKU, barcode and catalogue never select a different embedding model.

The old `MvpVisualEmbeddingExtractor` is no longer used by the runtime enrollment flow. If the neural model is absent or invalid, enrollment fails closed instead of falling back to the old deterministic color descriptor.

Existing enrollment data created by the old 240-dimensional MVP descriptor is intentionally treated as incompatible with the new 1024-dimensional model and must be deleted/re-enrolled before the neural pilot.

The model preparation tool pins the upstream ONNX export commit and expected SHA-256, exposes the 1024-D pre-classifier tensor, embeds preprocessing, validates the resulting ONNX graph and writes provenance. Generated model artifacts remain outside Git history under `Assets/Generated`.

The underlying TIMM model card declares Apache-2.0. That is technical provenance, not a substitute for final commercial/legal approval.

## Fail-closed matching

The matcher requires both a minimum similarity and a minimum gap from the runner-up product. If the best candidate is below the threshold or too close to the second candidate, the result is **Unknown/Ambiguous** instead of silently assigning a SKU.

The existing similarity thresholds are provisional until physical-device evidence is collected. Human review remains required by the main counting workflow.

## Validation ladder

### Gate 1 — 5 real products

Use the five products in `FIVE_PRODUCT_SENTIS_PHONE_PILOT.md`. For each SKU, enroll 8-15 views, test at different angles and distances, test non-enrolled objects, and record accepted, rejected and ambiguous results.

Proceed only if tenant isolation, save/reload, camera capture and the real Sentis embedding inference are stable.

### Gate 2 — 20 real products

Add products with similar colors and packaging. Measure correct recognition rate, false acceptance, false rejection, ambiguity rate, recognition latency and catalogue load/save time.

### Gate 3 — 100 real products

Include near-duplicate packaging and multiple package sizes. Measure the same metrics plus storage size, memory use, search latency and Android thermal behavior.

Do not declare production recognition ready from CI or synthetic EditMode tests alone.

## What this increment does not yet solve

- Multiple-product localization/cropping during the normal continuous count flow.
- Formal commercial/legal approval of the selected generic embedding model.
- Automatic background synchronization of enrolled products.
- Multi-device catalogue synchronization.
- Cloud backup or customer portal.
- Barcode scanner UI.
- Product deletion/deactivation UI.
- Formal 5/20/100 physical-device acceptance evidence.

Those are follow-up increments. The architectural rule remains: **one generic app; customer differences are data, not builds.**
