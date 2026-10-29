# Five-Product Sentis Phone Pilot

## Purpose

This pilot validates the new **generic neural embedding path** on a physical Android phone before any production-recognition claim.

The embedding model is one shared MobileNetV3 Small 0.75 feature backbone for every customer. Customer products remain local enrollment data; no customer-specific embedding model or APK/AAB is created.

## Pilot products

Use the five physical products already identified for the first Athmar pilot:

1. KENT lever-arch file
2. ROCO lever-arch file
3. LENO lever-arch file
4. Water bottle
5. HP multifunction printer

KENT, ROCO and LENO must remain separate SKUs even when their packaging is visually similar.

## Before testing

- Build must contain the generated `AthmarGenericEmbedding` model and pass the dedicated Sentis inference test.
- Use a valid active customer identity/package so the administrator-only enrollment screen is available.
- If the device contains enrollment data from the previous MVP visual descriptor, use **Delete Local Data** first. Old 240-D descriptors are intentionally rejected by the new 1024-D path.
- Do not use synthetic recognition results as evidence of real-world accuracy.

## Enrollment procedure

For each product:

1. Enter a stable SKU and Arabic and/or English product name.
2. Keep one product centered and occupying most of the preview.
3. Capture 8-15 references.
4. Deliberately vary angle, distance and small changes in background/lighting.
5. Save the product.
6. Immediately run **Test Recognition** on a view that was not one of the enrollment captures.

Do not save raw reference photos. The application persists only normalized neural feature vectors.

## Test matrix

For each of the five enrolled products, perform at least 20 recognition attempts covering:

- front/primary view;
- left/right angle;
- farther distance;
- different normal indoor lighting;
- background variation.

Additionally perform at least 10 attempts with objects that were **not enrolled**. Include direct cross-confusion tests among KENT, ROCO and LENO.

Record for every attempt:

- expected SKU or `unknown`;
- returned SKU / Unknown / Ambiguous;
- best similarity;
- runner-up similarity;
- whether the decision was correct;
- approximate recognition latency;
- lighting/distance/view notes.

## Metrics to report

Report, per SKU and overall:

- correct recognition rate;
- false acceptance count/rate;
- false rejection count/rate;
- ambiguous count/rate;
- KENT/ROCO/LENO cross-confusions;
- median and worst observed recognition latency;
- any camera rotation/mirroring or thermal/memory problem.

No production acceptance threshold is declared in this pilot document. Thresholds must be chosen from physical evidence rather than invented from synthetic tests.

## Current scope boundary

This pilot validates **one-product enrolled-SKU recognition**. It does not yet prove multi-object localization or continuous inventory counting. The generic detector/localization and crop-to-embedding integration remains a separate increment.

## Model provenance

The build preparation script pins and verifies the upstream ONNX export before modifying it. The underlying TIMM model card declares Apache-2.0 and documents a 224x224 MobileNetV3 Small 0.75 backbone with a 1024-dimensional pre-classifier feature vector. This is technical provenance only; final commercial/legal approval remains a release gate.
