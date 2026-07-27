#!/usr/bin/env python3
"""Generate reviewable YOLO pseudo-labels for Athmar Vision Count images.

The pipeline uses Grounding DINO to find products, overlapping tiles to improve
small-object recall, and EasyOCR to classify binder crops as KENT, ROCO or LENO.
Uncertain brands and suspicious group boxes are retained in review reports but
are deliberately excluded from YOLO labels instead of being guessed.
"""

from __future__ import annotations

import argparse
import csv
import difflib
import json
import math
import re
import sys
import time
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Mapping, Sequence

CLASS_NAMES = [
    "KENT",
    "ROCO",
    "LENO",
    "water_bottle",
    "hp_multifunction_printer",
]
BRAND_CLASS_IDS = {"KENT": 0, "ROCO": 1, "LENO": 2}
PROMPT_LABELS = [
    "a single lever arch file binder",
    "a single ring binder",
    "a water bottle",
    "a multifunction printer",
]
PROMPT = ". ".join(PROMPT_LABELS) + "."
SUPPORTED_IMAGE_SUFFIXES = {".jpg", ".jpeg", ".png", ".webp"}


@dataclass(slots=True)
class Detection:
    image: str
    x1: float
    y1: float
    x2: float
    y2: float
    detector_score: float
    detector_label: str
    tile: str
    class_id: int | None = None
    class_name: str | None = None
    brand_score: float | None = None
    ocr_text: str = ""
    status: str = "candidate"

    @property
    def width(self) -> float:
        return max(0.0, self.x2 - self.x1)

    @property
    def height(self) -> float:
        return max(0.0, self.y2 - self.y1)

    @property
    def area(self) -> float:
        return self.width * self.height


def normalize_text(value: str) -> str:
    return re.sub(r"[^A-Z0-9]", "", value.upper())


def brand_similarity(text: str, brand: str) -> float:
    normalized = normalize_text(text)
    if not normalized:
        return 0.0
    if brand in normalized:
        return 1.0
    fragments = [normalize_text(part) for part in re.split(r"\s+", text) if part]
    fragments.append(normalized)
    return max(
        difflib.SequenceMatcher(None, fragment, brand).ratio()
        for fragment in fragments
        if fragment
    )


def classify_brand(ocr_text: str, threshold: float = 0.76) -> tuple[int | None, str | None, float]:
    scored = sorted(
        ((brand_similarity(ocr_text, brand), brand) for brand in BRAND_CLASS_IDS),
        reverse=True,
    )
    best_score, best_brand = scored[0]
    second_score = scored[1][0]
    if best_score >= threshold and (best_score == 1.0 or best_score - second_score >= 0.08):
        return BRAND_CLASS_IDS[best_brand], best_brand, best_score
    return None, None, best_score


def detector_family(label: str) -> str:
    lowered = label.lower()
    if "bottle" in lowered:
        return "bottle"
    if "printer" in lowered:
        return "printer"
    return "binder"


def intersection_over_union(a: Detection, b: Detection) -> float:
    left = max(a.x1, b.x1)
    top = max(a.y1, b.y1)
    right = min(a.x2, b.x2)
    bottom = min(a.y2, b.y2)
    intersection = max(0.0, right - left) * max(0.0, bottom - top)
    union = a.area + b.area - intersection
    return intersection / union if union > 0 else 0.0


def non_maximum_suppression(detections: Sequence[Detection], iou_threshold: float) -> list[Detection]:
    remaining = sorted(detections, key=lambda item: item.detector_score, reverse=True)
    kept: list[Detection] = []
    while remaining:
        current = remaining.pop(0)
        kept.append(current)
        remaining = [
            candidate
            for candidate in remaining
            if not (
                detector_family(current.detector_label)
                == detector_family(candidate.detector_label)
                and intersection_over_union(current, candidate) >= iou_threshold
            )
        ]
    return kept


def tile_regions(width: int, height: int, tile_size: int, overlap: float) -> list[tuple[int, int, int, int, str]]:
    regions = [(0, 0, width, height, "full")]
    if tile_size <= 0 or (width <= tile_size and height <= tile_size):
        return regions
    stride = max(1, int(tile_size * (1.0 - overlap)))

    def starts(length: int) -> list[int]:
        if length <= tile_size:
            return [0]
        values = list(range(0, max(1, length - tile_size + 1), stride))
        last = length - tile_size
        if values[-1] != last:
            values.append(last)
        return values

    for top in starts(height):
        for left in starts(width):
            regions.append(
                (left, top, min(width, left + tile_size), min(height, top + tile_size), f"tile-{left}-{top}")
            )
    return regions


def box_to_yolo(detection: Detection, image_width: int, image_height: int) -> str:
    if detection.class_id is None:
        raise ValueError("Cannot serialize an unclassified detection")
    values = [
        ((detection.x1 + detection.x2) / 2.0) / image_width,
        ((detection.y1 + detection.y2) / 2.0) / image_height,
        detection.width / image_width,
        detection.height / image_height,
    ]
    values = [min(1.0, max(0.0, value)) for value in values]
    return f"{detection.class_id} " + " ".join(f"{value:.6f}" for value in values)


def image_files(images_dir: Path) -> list[Path]:
    return sorted(
        path
        for path in images_dir.iterdir()
        if path.is_file() and path.suffix.lower() in SUPPORTED_IMAGE_SUFFIXES
    )


def choose_device(requested: str) -> str:
    if requested != "auto":
        return requested
    import torch

    if torch.cuda.is_available():
        return "cuda"
    if getattr(torch.backends, "mps", None) and torch.backends.mps.is_available():
        return "mps"
    return "cpu"


def load_detector(model_id: str, device: str):
    from transformers import AutoModelForZeroShotObjectDetection, AutoProcessor

    processor = AutoProcessor.from_pretrained(model_id)
    model = AutoModelForZeroShotObjectDetection.from_pretrained(model_id)
    model.to(device)
    model.eval()
    return processor, model


def load_ocr(device: str):
    import easyocr

    return easyocr.Reader(["en"], gpu=device.startswith("cuda"), verbose=False)


def extract_result_labels(result: Mapping[str, object]) -> list[str]:
    """Normalize Transformers 4.x/5.x Grounding DINO label output."""
    text_labels = result.get("text_labels")
    if text_labels is not None:
        return [str(value) for value in text_labels]

    raw_labels = result.get("labels")
    if raw_labels is None:
        return []
    if hasattr(raw_labels, "detach"):
        raw_labels = raw_labels.detach().cpu().tolist()
    labels: list[str] = []
    for value in raw_labels:  # type: ignore[union-attr]
        try:
            index = int(value)
        except (TypeError, ValueError):
            labels.append(str(value))
            continue
        labels.append(PROMPT_LABELS[index] if 0 <= index < len(PROMPT_LABELS) else str(index))
    return labels


def run_detector_on_crop(crop, processor, model, device: str, box_threshold: float, text_threshold: float):
    import torch

    batch = processor(images=crop, text=PROMPT, return_tensors="pt")
    inputs = {
        key: value.to(device) if hasattr(value, "to") else value
        for key, value in batch.items()
    }
    with torch.inference_mode():
        outputs = model(**inputs)

    result = processor.post_process_grounded_object_detection(
        outputs,
        input_ids=inputs.get("input_ids"),
        threshold=box_threshold,
        text_threshold=text_threshold,
        target_sizes=[(crop.height, crop.width)],
    )[0]
    boxes = result["boxes"].detach().cpu().tolist()
    scores = result["scores"].detach().cpu().tolist()
    labels = extract_result_labels(result)
    if not (len(boxes) == len(scores) == len(labels)):
        raise RuntimeError(
            f"Grounding DINO output mismatch: boxes={len(boxes)} scores={len(scores)} labels={len(labels)}"
        )
    return boxes, scores, labels


def collect_detections(
    image_path: Path,
    processor,
    model,
    device: str,
    box_threshold: float,
    text_threshold: float,
    tile_size: int,
    tile_overlap: float,
    nms_iou: float,
):
    from PIL import Image

    image = Image.open(image_path).convert("RGB")
    detections: list[Detection] = []
    for left, top, right, bottom, tile_name in tile_regions(
        image.width, image.height, tile_size, tile_overlap
    ):
        crop = image.crop((left, top, right, bottom))
        boxes, scores, labels = run_detector_on_crop(
            crop, processor, model, device, box_threshold, text_threshold
        )
        for box, score, label in zip(boxes, scores, labels, strict=True):
            x1, y1, x2, y2 = [float(value) for value in box]
            detection = Detection(
                image=image_path.name,
                x1=max(0.0, min(float(image.width), x1 + left)),
                y1=max(0.0, min(float(image.height), y1 + top)),
                x2=max(0.0, min(float(image.width), x2 + left)),
                y2=max(0.0, min(float(image.height), y2 + top)),
                detector_score=float(score),
                detector_label=str(label),
                tile=tile_name,
            )
            if detection.width >= 8 and detection.height >= 8:
                detections.append(detection)
    return image, non_maximum_suppression(detections, nms_iou)


def expanded_crop(image, detection: Detection, padding_ratio: float = 0.06):
    pad_x = detection.width * padding_ratio
    pad_y = detection.height * padding_ratio
    return image.crop(
        (
            max(0, int(math.floor(detection.x1 - pad_x))),
            max(0, int(math.floor(detection.y1 - pad_y))),
            min(image.width, int(math.ceil(detection.x2 + pad_x))),
            min(image.height, int(math.ceil(detection.y2 + pad_y))),
        )
    )


def read_binder_brand(image, detection: Detection, reader, brand_threshold: float) -> None:
    import numpy as np
    from PIL import ImageEnhance, ImageFilter

    crop = expanded_crop(image, detection)
    enlarged = crop.resize((max(1, crop.width * 2), max(1, crop.height * 2)))
    passes = [crop, ImageEnhance.Contrast(enlarged).enhance(1.5).filter(ImageFilter.SHARPEN)]
    texts: list[str] = []
    for candidate in passes:
        try:
            results = reader.readtext(
                np.asarray(candidate),
                detail=1,
                paragraph=False,
                rotation_info=[90, 180, 270],
                text_threshold=0.35,
                low_text=0.25,
                mag_ratio=1.5,
            )
        except Exception as exc:
            texts.append(f"OCR_ERROR:{type(exc).__name__}")
            continue
        for item in results:
            if len(item) >= 3 and float(item[2]) >= 0.15:
                texts.append(str(item[1]))

    joined = " | ".join(dict.fromkeys(texts))
    class_id, brand, score = classify_brand(joined, brand_threshold)
    detection.ocr_text = joined
    detection.brand_score = score
    detection.class_id = class_id
    detection.class_name = brand
    detection.status = "accepted" if class_id is not None else "review-unknown-brand"


def classify_generic_detection(detection: Detection) -> None:
    family = detector_family(detection.detector_label)
    if family == "bottle":
        detection.class_id = 3
        detection.class_name = CLASS_NAMES[3]
        detection.status = "accepted"
    elif family == "printer":
        detection.class_id = 4
        detection.class_name = CLASS_NAMES[4]
        detection.status = "accepted"


def mark_geometry_risks(detection: Detection, image_width: int, image_height: int) -> None:
    if detector_family(detection.detector_label) != "binder":
        return
    width_ratio = detection.width / image_width
    area_ratio = detection.area / (image_width * image_height)
    if width_ratio > 0.30 or area_ratio > 0.22:
        detection.status = "review-possible-group-box"
        detection.class_id = None
        detection.class_name = None


def draw_overlay(image, detections: Sequence[Detection], output_path: Path) -> None:
    from PIL import ImageDraw, ImageFont

    overlay = image.copy()
    draw = ImageDraw.Draw(overlay)
    font = ImageFont.load_default()
    colors = ["#ef4444", "#3b82f6", "#22c55e", "#f59e0b", "#a855f7"]
    for detection in detections:
        color = colors[detection.class_id] if detection.class_id is not None else "#facc15"
        draw.rectangle((detection.x1, detection.y1, detection.x2, detection.y2), outline=color, width=3)
        text = f"{detection.class_name or 'REVIEW'} {detection.detector_score:.2f}"
        text_box = draw.textbbox((detection.x1, detection.y1), text, font=font)
        draw.rectangle(text_box, fill=color)
        draw.text((detection.x1, detection.y1), text, fill="black", font=font)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    overlay.save(output_path, quality=92)


def write_image_outputs(
    image,
    image_path: Path,
    detections: Sequence[Detection],
    labels_dir: Path,
    review_dir: Path,
    overwrite: bool,
) -> dict[str, int]:
    labels_dir.mkdir(parents=True, exist_ok=True)
    review_dir.mkdir(parents=True, exist_ok=True)
    label_path = labels_dir / f"{image_path.stem}.txt"
    if label_path.exists() and label_path.read_text(encoding="utf-8").strip() and not overwrite:
        raise FileExistsError(
            f"Refusing to replace non-empty {label_path}; use --overwrite only after backup."
        )

    accepted = [
        item for item in detections if item.class_id is not None and item.status == "accepted"
    ]
    lines = [box_to_yolo(item, image.width, image.height) for item in accepted]
    label_path.write_text("\n".join(lines) + ("\n" if lines else ""), encoding="utf-8")
    (review_dir / f"{image_path.stem}.json").write_text(
        json.dumps([asdict(item) for item in detections], indent=2, ensure_ascii=False),
        encoding="utf-8",
    )
    draw_overlay(image, detections, review_dir / f"{image_path.stem}.jpg")
    return {
        "accepted": len(accepted),
        "review": sum(item.class_id is None for item in detections),
        "candidates": len(detections),
    }


def write_summary(review_dir: Path, rows: Sequence[dict[str, object]]) -> None:
    review_dir.mkdir(parents=True, exist_ok=True)
    (review_dir / "summary.json").write_text(
        json.dumps(rows, indent=2, ensure_ascii=False), encoding="utf-8"
    )
    with (review_dir / "summary.csv").open("w", newline="", encoding="utf-8-sig") as handle:
        fields = ["image", "accepted", "review", "candidates", "seconds", *CLASS_NAMES]
        writer = csv.DictWriter(handle, fieldnames=fields)
        writer.writeheader()
        writer.writerows(rows)


def process(args: argparse.Namespace) -> int:
    images_dir = Path(args.images).resolve()
    labels_dir = Path(args.labels).resolve()
    review_dir = Path(args.review).resolve()
    if not images_dir.is_dir():
        raise FileNotFoundError(f"Images directory not found: {images_dir}")
    files = image_files(images_dir)
    if not files:
        raise FileNotFoundError(f"No supported images found in {images_dir}")

    device = choose_device(args.device)
    print(f"Loading detector {args.model} on {device}...", flush=True)
    processor, model = load_detector(args.model, device)
    print("Loading OCR model...", flush=True)
    reader = load_ocr(device)

    rows: list[dict[str, object]] = []
    for index, image_path in enumerate(files, start=1):
        started = time.perf_counter()
        print(f"[{index}/{len(files)}] {image_path.name}", flush=True)
        image, detections = collect_detections(
            image_path,
            processor,
            model,
            device,
            args.box_threshold,
            args.text_threshold,
            args.tile_size,
            args.tile_overlap,
            args.nms_iou,
        )
        for detection in detections:
            if detector_family(detection.detector_label) == "binder":
                read_binder_brand(image, detection, reader, args.brand_threshold)
                mark_geometry_risks(detection, image.width, image.height)
            else:
                classify_generic_detection(detection)

        counts = write_image_outputs(
            image, image_path, detections, labels_dir, review_dir, args.overwrite
        )
        class_counts = {
            name: sum(
                item.class_id == class_id and item.status == "accepted"
                for item in detections
            )
            for class_id, name in enumerate(CLASS_NAMES)
        }
        rows.append(
            {
                "image": image_path.name,
                **counts,
                "seconds": round(time.perf_counter() - started, 2),
                **class_counts,
            }
        )
        print(
            f"  accepted={counts['accepted']} review={counts['review']} "
            f"candidates={counts['candidates']}",
            flush=True,
        )

    write_summary(review_dir, rows)
    print(f"YOLO labels: {labels_dir}")
    print(f"Review overlays and audit reports: {review_dir}")
    print("Uncertain detections were intentionally excluded from YOLO labels.")
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--images", default="dataset/images/raw")
    parser.add_argument("--labels", default="dataset/labels/auto")
    parser.add_argument("--review", default="dataset/review/auto-labels")
    parser.add_argument("--model", default="IDEA-Research/grounding-dino-tiny")
    parser.add_argument("--device", default="auto", help="auto, cpu, cuda, mps, cuda:0, ...")
    parser.add_argument("--box-threshold", type=float, default=0.20)
    parser.add_argument("--text-threshold", type=float, default=0.18)
    parser.add_argument("--brand-threshold", type=float, default=0.76)
    parser.add_argument("--tile-size", type=int, default=960)
    parser.add_argument("--tile-overlap", type=float, default=0.25)
    parser.add_argument("--nms-iou", type=float, default=0.45)
    parser.add_argument("--overwrite", action="store_true")
    return parser


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    if not 0 <= args.tile_overlap < 0.9:
        parser.error("--tile-overlap must be between 0 and 0.9")
    try:
        return process(args)
    except KeyboardInterrupt:
        print("Cancelled.", file=sys.stderr)
        return 130
    except Exception as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
