# Bulk Catalogue Bootstrap

## Goal

Athmar Vision Count must support customers with 10,000-100,000 SKUs without asking staff to photograph every product 8-15 times.

The commercial onboarding path is now:

```text
ERP / Excel / CSV / supplier catalogue
        +
existing product image(s) (1-3 when available)
        |
        v
Bulk bootstrap builder
        |
        +-- bulk_products.csv
        +-- bulk_embeddings.bin
        +-- SHA-256 report
        |
        v
Customer manifest schema v2
        |
        v
ONE generic APK + ONE generic embedding model
```

`Self-Service Product Enrollment` is retained only as a **manual hard-case overlay** for a new SKU or a product that the bulk catalogue cannot distinguish reliably.

## No per-customer model

The generic embedding network is still identical for every customer:

`SentisProductEmbeddingExtractor.ModelId`

Customer differences are data only:

- SKU and names
- barcode
- OCR aliases
- compact product prototype vector
- thresholds / candidate count
- local manual hard-case overrides

A schema-v2 package is rejected if its vector index was generated with another model id or embedding dimension.

## Input CSV for the bootstrap builder

Required columns:

```csv
sku,name_en,name_ar
```

Recommended columns:

```csv
sku,name_en,name_ar,barcode,ocr_aliases,image_paths,image_urls,active
```

Multiple paths, URLs, or OCR aliases are separated with `|`.

Example:

```csv
sku,name_en,name_ar,barcode,ocr_aliases,image_paths,image_urls,active
WATER-001,Water Bottle,قارورة ماء,628100001,WATER 600ML|BRAND WATER,images/water-front.jpg|images/water-side.jpg,,true
KENT-001,Kent Gold,كنت جولد,628100002,KENT GOLD,,https://cdn.example.com/kent-front.jpg,true
```

## Automatic embedding generation

Use the prepared generic embedding ONNX graph and run:

```bash
pip install numpy pillow onnxruntime
python tools/build_bulk_catalogue.py \
  --catalogue customer_products.csv \
  --model Assets/Generated/Resources/AthmarGenericEmbedding.onnx \
  --output-dir Build/CustomerBulk
```

The builder:

1. validates SKU/barcode uniqueness;
2. reads up to three existing images per SKU by default;
3. runs the same generic 1024-D embedding model used by the APK;
4. L2-normalizes each image vector;
5. averages available image vectors into one product prototype and normalizes again;
6. quantizes the prototype to int8;
7. writes a 64-bit coarse projection signature for candidate pruning;
8. emits `bulk_products.csv` and `bulk_embeddings.bin`;
9. emits SHA-256 hashes in `bulk_bootstrap_report.json`;
10. does **not** persist the source images in the output package.

A product without an image can still exist in `bulk_products.csv` and can be resolved later through barcode/OCR/manual evidence; it simply has no visual prototype until one becomes available.

## Why the binary index

A raw float32 1024-D vector costs 4096 bytes per SKU. At 100,000 SKUs that is roughly 409.6 MB before metadata.

The v1 bulk index stores one normalized int8 prototype per SKU, approximately 1024 bytes plus a small SKU/signature header. The runtime first compares a 64-bit projection signature across the catalogue, then runs cosine scoring only on the closest candidate shortlist (default 256).

This is the first scalable on-device candidate-pruning layer. It can later be replaced by HNSW or another audited ANN implementation without changing the manifest or product metadata contract.

## Manifest schema v2

Schema v1 packages remain supported. Schema v2 adds:

```json
{
  "schemaVersion": 2,
  "bulkCatalogueUrl": "https://customer.example.com/bulk_products.csv",
  "bulkCatalogueSha256": "<64 hex>",
  "bulkEmbeddingIndexUrl": "https://customer.example.com/bulk_embeddings.bin",
  "bulkEmbeddingIndexSha256": "<64 hex>",
  "bulkEmbeddingModelId": "timm/mobilenetv3_small_075.lamb_in1k@fa65a043c25690a5779ff856052a5ae55ec03eda",
  "bulkMinimumSimilarity": 0.90,
  "bulkMinimumMargin": 0.04,
  "bulkCandidateLimit": 256
}
```

The installer streams large bulk files directly to staging storage instead of buffering the entire vector index in RAM, verifies SHA-256, validates the model identity, then performs the existing atomic activation/rollback flow.

## Evidence resolution order

`ProductEvidenceResolver` is fail-closed and resolves evidence in this order:

1. **Barcode exact match** - deterministic and preferred when available.
2. **OCR alias match** - only unique normalized aliases are accepted; collisions fail as ambiguous.
3. **Manual hard-case overlay** - several local reference images must agree through `RobustProductRecognitionMatcher`.
4. **Bulk visual prototype** - compact candidate search + similarity threshold + runner-up margin.
5. If manual and bulk visual evidence confidently disagree, the result is **Ambiguous**, not a guessed SKU.
6. Otherwise the result is **Unknown**.

## Important current boundary

The runtime resolver accepts barcode text and OCR text as evidence, but this increment does **not** yet add a native Android barcode/OCR camera extractor. The camera adapter must be implemented and licensed separately (for example with an audited mobile barcode/text-recognition SDK) before claiming live Barcode/OCR fallback in production.

The bulk bootstrap itself does not depend on that adapter: thousands of products can already be onboarded from CSV/images without manual photography.

## Manual hard cases

The administrator UI now labels the old self-service flow as **Manual Hard-Case Product**.

Use it only when:

- a new SKU is not yet in the bulk catalogue;
- a product has no usable supplier/catalogue image;
- two packages are visually difficult to separate;
- a real camera-domain example is needed to correct a recurring Unknown/Ambiguous case.

Do not manually enroll the full customer catalogue.

## Scaling gate

Before production rollout, validate in this order:

- 100 SKUs: functional correctness and Unknown rejection;
- 1,000 SKUs: search latency and memory;
- 10,000 SKUs: sustained phone performance;
- 100,000 SKUs: package size, install time, candidate recall and memory pressure.

Physical accuracy and ANN recall must be measured with real products. CI success alone does not prove recognition quality.
