# Runtime customer packages

Athmar Vision Count is built once as a generic signed Android application. Customer-specific models, SKU catalogues and counting parameters are installed from the protected in-app administration interface rather than embedded in the App Bundle.

## Security boundary

The administrator must receive two values through a trusted channel:

1. an absolute HTTPS URL for `manifest.json`;
2. the exact SHA-256 digest of that manifest.

The app verifies the manifest digest before parsing it. The verified manifest then pins the SHA-256 values of the model and SKU catalogue. Downloads are rejected if any digest differs, a URL is not HTTPS, the catalogue is invalid, or the Sentis model cannot be deserialized.

A hash only proves integrity relative to the trusted hash supplied by the administrator. A future activation service should replace manual hash entry with digitally signed manifests and short-lived activation codes.

## Manifest schema

```json
{
  "schemaVersion": 1,
  "customerCode": "customer-001",
  "customerName": "Customer 001",
  "defaultLanguage": "ar",
  "modelVersion": "1.0.0",
  "catalogueVersion": "1.0.0",
  "privacyNoticeVersion": "1.0",
  "modelUrl": "https://storage.example.com/customer-001/production.sentis",
  "modelSha256": "64-character-lowercase-or-uppercase-sha256",
  "catalogueUrl": "https://storage.example.com/customer-001/sku_catalogue.csv",
  "catalogueSha256": "64-character-lowercase-or-uppercase-sha256",
  "modelInputWidth": 640,
  "modelInputHeight": 640,
  "modelInputLayout": "Nchw",
  "outputTensorIndex": 0,
  "outputTensorLayout": "ChannelsFirst",
  "outputHasObjectness": false,
  "outputCoordinatesNormalized": false,
  "maxDetections": 100,
  "preferGpu": true,
  "inferenceIntervalSeconds": 0.25,
  "minimumConfidence": 0.65,
  "duplicateIouThreshold": 0.45,
  "nonMaxSuppressionIouThreshold": 0.45,
  "trackTtlSeconds": 1.25,
  "retentionDays": 30,
  "networkSyncEnabled": false,
  "syncEndpoint": ""
}
```

Do not guess model tensor values. Record them from the approved model export and verify them on held-out images and the target Android devices.

## SKU catalogue

The catalogue is UTF-8 CSV:

```csv
label_index,sku,name_en,name_ar,active
0,SKU-0001,Red Bottle,زجاجة حمراء,true
1,SKU-0002,Blue Bottle,زجاجة زرقاء,true
```

`label_index` must exactly match the model class order. Duplicate labels, duplicate SKUs, invalid active values, missing columns and catalogues without an active product are rejected.

## Computing hashes

PowerShell:

```powershell
(Get-FileHash .\production.sentis -Algorithm SHA256).Hash.ToLower()
(Get-FileHash .\sku_catalogue.csv -Algorithm SHA256).Hash.ToLower()
(Get-FileHash .\manifest.json -Algorithm SHA256).Hash.ToLower()
```

Linux or macOS:

```bash
sha256sum production.sentis sku_catalogue.csv manifest.json
```

Calculate the model and catalogue hashes first, place them in the manifest, save the final manifest, then calculate the manifest hash last.

## Installation flow

1. Open **Administration** in the app.
2. On first use, create a 6–12 digit administrator PIN.
3. On later use, unlock with that PIN.
4. Enter the trusted manifest HTTPS URL.
5. Enter the trusted manifest SHA-256.
6. Tap **Verify & Install**.
7. The app downloads to a staging directory, validates all inputs, tests model deserialization, and then atomically activates the package.
8. If a prior package exists, it remains available through **Rollback**.

Customer files are stored under `Application.persistentDataPath/customer-packages`, which maps to application-private storage on Android. They are not committed to Git and are not required by GitHub Actions.

## Supported model contract

The generic decoder accepts one float detection tensor with batch size one in one of these layouts:

```text
[1, 4 + classes, candidates]
[1, 5 + classes, candidates]
[1, candidates, 4 + classes]
[1, candidates, 5 + classes]
```

The first four values are center X, center Y, width and height. Class values and optional objectness must already be probabilities from 0 to 1. Models with multiple output tensors, raw logits, anchor-specific decoding, segmentation masks or materially different output contracts require a reviewed decoder update.

## Release limitations

The current interface supports trusted URL plus SHA-256 provisioning. It does not yet implement QR scanning, an activation-code backend, certificate-based manifest signatures, remote device revocation or automated model training. Those capabilities must not be represented as already available.
