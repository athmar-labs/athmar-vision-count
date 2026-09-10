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

Reference camera frames are used only long enough to calculate a local visual descriptor. Raw enrollment photos are not written to disk by this MVP.

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
float[] visual embedding
        |
        v
ProductRecognitionMatcher
        |
        +--> known SKU when similarity + margin pass
        |
        +--> Unknown / Ambiguous otherwise
```

This means the same enrollment catalogue can survive replacement of the embedding implementation.

## MVP visual descriptor

The first MVP uses `MvpVisualEmbeddingExtractor`, a deterministic on-device visual descriptor built from an 8 x 8 spatial RGB grid plus hue, saturation, value and luminance histograms, followed by L2 normalization and cosine similarity.

It exists to prove the complete self-service workflow immediately without introducing a customer-specific ML model or storing enrollment images.

**It is not the final production embedding model and must not be represented as one.**

The next recognition-quality increment should implement the same `IProductEmbeddingExtractor` interface with one generic, commercially approved Sentis embedding model shared by every customer. The product store, customer isolation, enrollment UI and matching API should not need to change.

## Fail-closed matching

The matcher requires both a minimum similarity and a minimum gap from the runner-up product. If the best candidate is below the threshold or too close to the second candidate, the result is **Unknown/Ambiguous** instead of silently assigning a SKU.

Human review remains required by the main counting workflow.

## Validation ladder

### Gate 1 — 5 real products

Use visually different products. For each SKU, enroll 8-15 views, test at different angles and distances, test a non-enrolled object, and record accepted, rejected and ambiguous results.

Proceed only if tenant isolation, save/reload and camera capture are stable.

### Gate 2 — 20 real products

Add products with similar colors and packaging. Measure correct recognition rate, false acceptance, false rejection, ambiguity rate, recognition latency and catalogue load/save time.

### Gate 3 — 100 real products

Include near-duplicate packaging and multiple package sizes. Measure the same metrics plus storage size, memory use, search latency and Android thermal behavior.

Do not declare production recognition ready from synthetic EditMode tests alone.

## What this MVP does not yet solve

- Multiple-product localization/cropping during the normal continuous count flow.
- A production-grade generic learned embedding model.
- Automatic background synchronization of enrolled products.
- Multi-device catalogue synchronization.
- Cloud backup or customer portal.
- Barcode scanner UI.
- Product deletion/deactivation UI.
- Formal 5/20/100 physical-device acceptance evidence.

Those are follow-up increments. The architectural rule remains: **one generic app; customer differences are data, not builds.**
