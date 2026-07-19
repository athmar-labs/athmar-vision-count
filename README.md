# Athmar Vision Count

Privacy-first mobile inventory counting proof of concept built with Unity, AR Foundation,
Unity Sentis, and an on-device YOLO model.

## Current status

This repository is a technical prototype, not a production release. The current generic
model and scene wiring must be validated on representative customer stock before any
accuracy claim or paid deployment.

By default, scans are stored locally and network synchronization is disabled. Enabling
sync requires an explicit customer opt-in, a documented retention policy, and an HTTPS
endpoint controlled by Athmar Labs.

## Proposed paid pilot

The first narrow use case is assisted shelf or small-warehouse cycle counting for one
Saudi distributor with a limited, customer-approved SKU set. The app proposes counts;
an employee reviews and confirms them before export. It does not automatically update
inventory or accounting systems.

Pilot success should be measured against manually verified ground truth:

- counting accuracy by SKU and environment;
- time saved per counting session;
- percentage of sessions requiring correction;
- successful export after human confirmation.

## Release gates

1. Wire and validate all required components in the main scene.
2. Train or fine-tune a legally usable model on the pilot customer's approved SKUs.
3. Add a review-and-confirm screen and CSV export.
4. Add automated tests for counting, tracking, storage, and configuration validation.
5. Complete Android device testing, Arabic RTL UX, privacy notice, and deletion controls.
6. Produce a signed APK from CI using Unity `6000.3.10f1`.

No customer data, images, stable device identifiers, API keys, or credentials should be
committed to this repository.
