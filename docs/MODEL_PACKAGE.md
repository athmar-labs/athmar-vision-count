# Customer Model Package Contract

The Android application is built once as a generic signed product. Customer-specific models, SKU catalogues and compatible inference settings are installed after deployment through the protected in-app administration interface.

See [Runtime customer packages](RUNTIME_CUSTOMER_PACKAGES.md) for the complete manifest schema, installation flow and hash commands.

## Required private artifacts

Each approved customer package provides:

- `manifest.json`: customer identity, versions, model contract, counting settings, HTTPS asset URLs and pinned hashes;
- `production.sentis`: the legally approved Unity Inference Engine model;
- `sku_catalogue.csv`: the approved UTF-8 catalogue whose label order exactly matches the model;
- written rights, validation and customer-acceptance evidence retained outside source control.

Models, catalogues, datasets, images and signing files must never be committed to this repository. The app downloads customer assets only to its application-private storage, verifies them and supports rollback to the previous validated package.

## Trust and integrity

The administrator must enter an HTTPS URL for the manifest and the SHA-256 digest of that exact manifest received through a trusted channel. The verified manifest pins the SHA-256 values of the model and catalogue.

The current mechanism protects integrity relative to the trusted hash. It is not a replacement for a future activation service with digitally signed manifests, device authorization and revocation.

## Supported detector outputs

The generic adapter supports one float detection tensor with batch size one and either of these shapes:

```text
[1, 4 + classes, candidates]                 channels-first, no objectness
[1, 5 + classes, candidates]                 channels-first, objectness
[1, candidates, 4 + classes]                 rows-first, no objectness
[1, candidates, 5 + classes]                 rows-first, objectness
```

The first four values are center X, center Y, width and height. Coordinates may be normalized from 0 to 1 or expressed in configured model-input pixels. Class values and optional objectness must already be probabilities from 0 to 1. Models that emit raw logits, multiple output tensors, anchors requiring custom decoding or segmentation masks need a reviewed adapter extension.

The following manifest properties describe the model contract:

```json
{
  "modelInputWidth": 640,
  "modelInputHeight": 640,
  "modelInputLayout": "Nchw",
  "outputTensorIndex": 0,
  "outputTensorLayout": "ChannelsFirst",
  "outputHasObjectness": false,
  "outputCoordinatesNormalized": false,
  "minimumConfidence": 0.65,
  "nonMaxSuppressionIouThreshold": 0.45,
  "duplicateIouThreshold": 0.45,
  "trackTtlSeconds": 1.25,
  "inferenceIntervalSeconds": 0.25,
  "maxDetections": 100
}
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

- `label_index` is the zero-based model class index and must be unique;
- `sku` is the customer's stable product identifier and must be unique;
- `name_en` and `name_ar` are operator-facing names;
- `active` is optional; omitted values default to true;
- inactive or unknown model labels are ignored rather than exported;
- quoted values and escaped double quotes are supported.

The runtime installer rejects missing columns, duplicate labels, duplicate SKUs, invalid active values and catalogues with no active products.

## Required provenance and validation evidence

Before publishing a customer package, retain written evidence covering:

- ownership or a commercial license for the model, training data and customer examples;
- intended SKUs, sites, devices and environmental limits;
- excluded or unsafe conditions;
- immutable model and catalogue versions;
- SHA-256 digests of the model, catalogue and final manifest;
- held-out ground-truth results by SKU and device;
- known false-positive, false-negative and duplicate-count failure modes;
- approver and approval date.

A successful software build or successful package installation does not establish counting accuracy. Paid deployment still requires physical-device and customer acceptance evidence.
