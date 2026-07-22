# Commercial Pilot Framework

This framework is a technical scope baseline. Commercial prices, liability, warranties, payment terms, data terms and service levels belong in a signed agreement reviewed by qualified counsel.

## Pilot objective

Validate assisted cycle counting for a limited customer-approved SKU set in one defined shelf or small-warehouse environment. The application proposes quantities and an operator confirms or corrects them before export.

## Included deliverables

- one customer-branded Android build for the agreed supported devices;
- one versioned customer SKU catalogue and model package;
- on-device assisted counting;
- review and manual correction;
- confirmed CSV export;
- local session history and deletion;
- operator quick-start guide;
- pilot test plan and results report;
- agreed defect correction during the pilot window.

## Excluded unless separately contracted

- autonomous posting to ERP, accounting, purchasing or warehouse systems;
- continuous surveillance or unattended counting;
- facial recognition, employee monitoring or biometric processing;
- storage of camera images or video;
- unlimited SKUs, sites, devices or environmental conditions;
- custom integrations, dashboards, hosting or managed-device services;
- guarantees outside the agreed test conditions and acceptance dataset.

## Customer inputs

The customer provides:

- authorized SKU names, identifiers and representative examples;
- access to the agreed test environment and physical stock;
- supported Android devices;
- operators for training and acceptance testing;
- manually verified ground-truth counts;
- approval of privacy, retention and export procedures;
- written confirmation that supplied data and images may be used for the pilot purpose.

## Acceptance method

Before testing, both parties record:

- exact SKU scope;
- supported devices and Android versions;
- lighting, shelf distance, packaging and occlusion conditions;
- number and type of test sessions;
- ground-truth method;
- target counting accuracy or error tolerance;
- maximum correction rate;
- target time saving;
- export success requirement;
- severity definitions and remediation window.

Results must be calculated from the agreed held-out acceptance sessions, not training or tuning examples. Accuracy claims must state the SKU set, device, environment and test date.

## Recommended pilot stages

1. **Discovery:** confirm process, risks, devices, SKU scope and data rights.
2. **Data readiness:** prepare labelled examples and versioned catalogue.
3. **Configuration:** integrate the approved model and customer settings.
4. **Internal validation:** test core flows, privacy controls and failure states.
5. **Customer acceptance:** execute the signed test plan against ground truth.
6. **Decision:** approve production rollout, extend tuning, narrow scope or stop.

## Production conversion

A successful pilot does not automatically authorize broad production use. Production conversion requires a signed production license/SOW, approved support model, release and rollback process, confirmed device fleet, customer-specific privacy documentation, final acceptance thresholds and a new signed build.
