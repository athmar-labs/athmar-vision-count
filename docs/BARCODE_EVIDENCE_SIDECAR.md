# Barcode Evidence Sidecar

## Principle

Barcode evidence is an optional sidecar. It must never replace, slow, or destabilize the existing YOLO localization and generic Sentis embedding path.

The Android adapter uses the bundled Google ML Kit Barcode Scanning library pinned to `com.google.mlkit:barcode-scanning:17.3.0`. The bundled model is available immediately and does not require a model download at first use.

## Safety contract

- The visual pipeline has no dependency on barcode availability.
- Barcode evidence is fail-closed: zero or multiple distinct values produce no barcode decision.
- Barcode evidence is short-lived and must remain spatially associated with the same product localization before reuse.
- Raw camera/product images are not persisted by the barcode layer.
- The barcode feature remains disabled until a schema-v2 manifest explicitly opts in and phone performance is measured.
- Multi-product barcode-to-box association is not guessed. Initial runtime activation is restricted to a single localized product.

## OCR boundary

ML Kit Text Recognition v2 currently provides Android recognizers for Latin, Chinese, Devanagari, Japanese and Korean scripts, but not Arabic. Athmar therefore does not label ML Kit as an Arabic OCR solution. Arabic OCR stays behind the evidence interface until a separately validated on-device option is selected.

## Integration gate

1. Android and production builds must stay green with the SDK present but inactive.
2. Activate it only through customer data/configuration, never customer-specific code or APKs.
3. Scan at a low cadence after the visual inference step, never inside the Sentis worker schedule.
4. If barcode scanning errors, times out, or returns ambiguous evidence, continue with visual recognition unchanged.
5. Benchmark phone FPS, inference latency, memory and thermal behavior before enabling it for production packages.
