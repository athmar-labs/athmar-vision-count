# Customer Model Package Contract

A production build is customer-specific. The model, catalogue, rights evidence and validation report are release inputs, not source-code fixtures.

## Required private artifacts

The protected release environment must provide:

- `MODEL_PACKAGE_URL`: short-lived HTTPS URL for the approved Unity model asset.
- `MODEL_PACKAGE_SHA256`: expected lowercase or uppercase SHA-256 digest.
- `SKU_CATALOGUE_BASE64`: base64-encoded UTF-8 SKU catalogue.
- Android signing and Unity activation secrets described in `docs/PRODUCTION_READINESS.md`.

The workflow downloads these files only inside the release runner, verifies the model hash, imports them into Unity, builds the signed App Bundle and removes the working copies. Models, catalogues, datasets, images and signing files must not be committed.

## Supported detector outputs

The generic adapter supports one float detection tensor with batch size one and either of these shapes:

```text
[1, 4 + classes, candidates]                 channels-first, no objectness
[1, 5 + classes, candidates]                 channels-first, objectness
[1, candidates, 4 + classes]                 rows-first, no objectness
[1, candidates, 5 + classes]                 rows-first, objectness
```

The first four values are center X, center Y, width and height. Coordinates may be normalized from 0 to 1 or expressed in configured model-input pixels. Class values and optional objectness must already be probabilities from 0 to 1. Models that emit raw logits, multiple output tensors, anchors requiring custom decoding or segmentation masks need a reviewed adapter extension.

The following repository variables describe the model contract:

```text
ATHMAR_MODEL_INPUT_WIDTH=640
ATHMAR_MODEL_INPUT_HEIGHT=640
ATHMAR_MODEL_INPUT_LAYOUT=Nchw              # Nchw or Nhwc
ATHMAR_OUTPUT_TENSOR_INDEX=0
ATHMAR_OUTPUT_TENSOR_LAYOUT=ChannelsFirst   # ChannelsFirst or RowsFirst
ATHMAR_OUTPUT_HAS_OBJECTNESS=false
ATHMAR_OUTPUT_COORDINATES_NORMALIZED=false
ATHMAR_MINIMUM_CONFIDENCE=0.65
ATHMAR_NMS_IOU_THRESHOLD=0.45
ATHMAR_DUPLICATE_IOU_THRESHOLD=0.45
ATHMAR_TRACK_TTL_SECONDS=1.25
ATHMAR_INFERENCE_INTERVAL_SECONDS=0.25
ATHMAR_MAX_DETECTIONS=100
```

Do not choose these values by guesswork. Record them in the model card and verify them against known test images before a device trial.

## SKU catalogue schema

The catalogue is UTF-8 CSV with these required columns:

```csv
label_index,sku,name_en,name_ar,active
0,SKU-0001,Red Bottle,زجاجة حمراء,true
1,SKU-0002,Blue Bottle,زجاجة زرقاء,true
```

Rules:

- `label_index` is the zero-based model class index and must be unique.
- `sku` is the customer's stable product identifier and must be unique.
- `name_en` and `name_ar` are operator-facing names.
- `active` is optional; omitted values default to true.
- Inactive or unknown model labels are ignored rather than exported.
- Quoted values and escaped double quotes are supported.

The production validator rejects missing columns, duplicate labels, duplicate SKUs, invalid active values and catalogues with no active products.

## Required provenance and validation evidence

Before the model secret is configured, retain written evidence covering:

- ownership or a commercial license for the model, training data and customer examples;
- intended SKUs, sites, devices and environmental limits;
- excluded or unsafe conditions;
- immutable model and catalogue versions;
- SHA-256 digest of the delivered model;
- held-out ground-truth results by SKU and device;
- known false-positive, false-negative and duplicate-count failure modes;
- approver and approval date.

A successful software build does not establish counting accuracy. Paid deployment still requires physical-device and customer acceptance evidence under issue #5.
