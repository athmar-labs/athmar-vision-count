# Athmar automatic pseudo-labeling

This tool creates **reviewable pseudo-labels**, not final ground truth.

It is designed for shelves containing many small products where drawing every bounding box by hand is too slow:

1. Grounding DINO performs open-vocabulary detection for individual binders, water bottles and multifunction printers.
2. The image is also processed as overlapping tiles so small shelf items are seen at a larger effective scale.
3. EasyOCR reads each detected binder crop, including rotated text.
4. OCR evidence maps binders to `KENT`, `ROCO` or `LENO`.
5. Uncertain brands and suspicious group-sized boxes are excluded from YOLO labels and highlighted for review.
6. The tool writes YOLO detection labels, annotated preview images, per-image JSON evidence and CSV/JSON summaries.

## Class order

The output class order is fixed and must match the runtime catalogue:

```text
0 KENT
1 ROCO
2 LENO
3 water_bottle
4 hp_multifunction_printer
```

## Privacy and repository policy

Customer photos, annotations, model weights and generated reports must remain outside Git history. The repository `.gitignore` excludes the local `dataset/` tree and downloaded `*.pt`/`*.pth` weights.

Recommended local layout:

```text
dataset/
  images/raw/
  labels/auto/
  review/auto-labels/
```

## Install

Use an isolated Python 3.12 environment:

```bash
python3 -m venv .venv-auto-label
.venv-auto-label/bin/python -m pip install --upgrade pip
.venv-auto-label/bin/python -m pip install -r tools/auto_label/requirements.txt
```

The first run downloads the Grounding DINO and EasyOCR weights. CPU works but is slower; a CUDA GPU is selected automatically when available.

## Run

From the repository root:

```bash
.venv-auto-label/bin/python tools/auto_label/auto_label.py \
  --images dataset/images/raw \
  --labels dataset/labels/auto \
  --review dataset/review/auto-labels
```

The tool refuses to overwrite a non-empty label file by default. Use `--overwrite` only after reviewing or backing up prior output.

## Outputs

For each input image:

- `dataset/labels/auto/<image>.txt`: accepted YOLO bounding boxes only;
- `dataset/review/auto-labels/<image>.jpg`: overlay showing accepted and review-required boxes;
- `dataset/review/auto-labels/<image>.json`: detector score, OCR text, brand score and decision for every candidate.

The folder also contains:

- `summary.csv`;
- `summary.json`.

Yellow `REVIEW` boxes are intentionally not written to YOLO labels. They must be corrected or approved by a human before training.

## Accuracy expectations

This pipeline substantially reduces manual drawing, but it cannot guarantee perfect labels. Exact brand recognition can fail when the spine text is blurred, hidden, too small, reflective or viewed at an extreme angle. A production model must be trained and evaluated using manually verified held-out images; pseudo-label counts are not accuracy evidence.

## Tests

The unit tests do not download model weights:

```bash
cd tools/auto_label
python -m unittest -v test_auto_label.py
```
