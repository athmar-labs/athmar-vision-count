# Product decision: develop as a narrow paid pilot

## Decision

Continue development, but reposition the prototype as **Athmar Vision Count**: an
assisted, human-reviewed stock-counting tool for a single narrow inventory category.
Do not launch it as a general object counter and do not promise accuracy until a field
benchmark is completed.

## Commercial hypothesis

The first buyer is a Saudi small distributor or warehouse operator whose periodic cycle
counts are manual, slow, and error-prone. The app proposes item counts from the camera;
the employee reviews and corrects them, then exports the approved result.

The sellable pilot is one site, one Android device profile, and a limited customer-approved
SKU set. Pricing should be finalized only after measuring onboarding and model-training
cost. No automatic ERP posting belongs in the first paid pilot.

## Evidence from this repository

Strengths:

- inference is designed to run on-device;
- counting, tracking, storage, diagnostics, and sync are separated into assemblies;
- Android/AR dependencies and an ONNX model are present;
- a local-first workflow can reduce privacy and infrastructure risk.

Release blockers:

- the main scene does not visibly wire the full scanning pipeline;
- the bundled generic model and custom labels have no documented accuracy benchmark;
- there are no automated unit or play-mode tests;
- there is no Arabic RTL review/confirmation/export flow;
- CI previously targeted a different Unity release than the project metadata;
- privacy notice, consent, deletion, model license, and customer data policy are absent.

## First-revenue plan

1. Make one end-to-end Android build: scan, propose, review, correct, and CSV export.
2. Benchmark 20 representative scenes against manually verified counts.
3. Reject the pilot if corrected item-level accuracy or time savings are commercially weak.
4. Demonstrate only with non-sensitive sample stock.
5. After owner approval, recruit one design partner and offer a tightly scoped paid pilot.

## Pilot acceptance gates

- zero uploads unless the customer explicitly opts in;
- every count is reviewable and editable before export;
- benchmark report records false positives, false negatives, and corrected accuracy;
- no contractual accuracy claim unsupported by benchmark evidence;
- customer can delete local sessions;
- model and dataset licenses are documented for commercial use.

## Sharia review

The proposed inventory-counting service has no apparent prohibited revenue mechanism.
Any later financing, penalty, insurance, marketplace, or revenue-sharing feature must be
reviewed separately before adoption.
