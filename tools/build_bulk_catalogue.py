#!/usr/bin/env python3
"""Build a customer bulk-recognition package from catalogue metadata and existing product images.

This is intentionally an offline/server-side bootstrap step. It uses the same generic embedding
model as the APK, creates one normalized prototype per SKU, quantizes it to int8, and writes a
compact index that the phone can search without downloading or retaining raw product images.

Input CSV columns:
  sku,name_en,name_ar,barcode,ocr_aliases,image_paths,image_urls,active

- image_paths: one or more local files separated by | (relative to the input CSV is allowed)
- image_urls: one or more absolute HTTPS URLs separated by |
- ocr_aliases: strong printed aliases/brand strings separated by |
- active: optional true/false, 1/0, yes/no (default true)

Dependencies:
  pip install numpy pillow onnxruntime
"""

from __future__ import annotations

import argparse
import csv
import io
import json
import math
import os
import pathlib
import struct
import sys
import urllib.parse
import urllib.request
from dataclasses import dataclass
from typing import Iterable, List, Sequence

import numpy as np
from PIL import Image

try:
    import onnxruntime as ort
except ImportError as exc:  # pragma: no cover - user-facing dependency guard
    raise SystemExit("onnxruntime is required: pip install onnxruntime") from exc

MAGIC = b"ATHVEC01"
FORMAT_VERSION = 1
MODEL_ID = "timm/mobilenetv3_small_075.lamb_in1k@fa65a043c25690a5779ff856052a5ae55ec03eda"
FEATURE_DIMENSION = 1024
MAX_PRODUCTS = 100_000
MAX_REMOTE_IMAGE_BYTES = 20 * 1024 * 1024
OUTPUT_CATALOGUE = "bulk_products.csv"
OUTPUT_EMBEDDINGS = "bulk_embeddings.bin"
OUTPUT_REPORT = "bulk_bootstrap_report.json"


@dataclass
class ProductRow:
    sku: str
    name_en: str
    name_ar: str
    barcode: str
    ocr_aliases: str
    image_paths: List[str]
    image_urls: List[str]
    active: bool


class GenericEmbeddingSession:
    def __init__(self, model_path: pathlib.Path) -> None:
        self._session = ort.InferenceSession(str(model_path), providers=["CPUExecutionProvider"])
        inputs = self._session.get_inputs()
        outputs = self._session.get_outputs()
        if len(inputs) != 1 or not outputs:
            raise ValueError("Generic embedding ONNX must expose exactly one input and at least one output.")
        self._input_name = inputs[0].name
        self._output_name = outputs[0].name
        self._shape = list(inputs[0].shape)
        if len(self._shape) != 4:
            raise ValueError(f"Expected a rank-4 embedding input, received {self._shape}.")
        if self._shape[-1] == 3:
            self._layout = "NHWC"
        elif self._shape[1] == 3:
            self._layout = "NCHW"
        else:
            raise ValueError(f"Unable to determine RGB input layout from {self._shape}.")

    def embed(self, image_bytes: bytes) -> np.ndarray:
        with Image.open(io.BytesIO(image_bytes)) as image:
            image = image.convert("RGB").resize((224, 224), Image.Resampling.BILINEAR)
            values = np.asarray(image, dtype=np.float32) / 255.0
        if self._layout == "NCHW":
            values = np.transpose(values, (2, 0, 1))
        values = np.expand_dims(values, axis=0)

        result = self._session.run([self._output_name], {self._input_name: values})[0]
        vector = np.asarray(result, dtype=np.float32).reshape(-1)
        if vector.size != FEATURE_DIMENSION:
            raise ValueError(
                f"Generic embedding model must output {FEATURE_DIMENSION} values; received {vector.size}."
            )
        if not np.all(np.isfinite(vector)):
            raise ValueError("Embedding contains a non-finite value.")
        return normalize(vector)


def normalize(vector: np.ndarray) -> np.ndarray:
    norm = float(np.linalg.norm(vector))
    if not math.isfinite(norm) or norm <= 1e-12:
        raise ValueError("Embedding magnitude is zero or invalid.")
    return (vector / norm).astype(np.float32, copy=False)


def parse_bool(value: str) -> bool:
    normalized = (value or "").strip().lower()
    if normalized in ("", "1", "true", "yes"):
        return True
    if normalized in ("0", "false", "no"):
        return False
    raise ValueError(f"Invalid active value: {value!r}")


def split_pipe(value: str) -> List[str]:
    return [piece.strip() for piece in (value or "").split("|") if piece.strip()]


def load_rows(csv_path: pathlib.Path) -> List[ProductRow]:
    with csv_path.open("r", encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle)
        required = {"sku", "name_en", "name_ar"}
        missing = required.difference(reader.fieldnames or [])
        if missing:
            raise ValueError(f"Input catalogue is missing columns: {', '.join(sorted(missing))}")

        rows: List[ProductRow] = []
        seen_skus = set()
        seen_barcodes = set()
        for line_number, raw in enumerate(reader, start=2):
            sku = (raw.get("sku") or "").strip()
            name_en = (raw.get("name_en") or "").strip()
            name_ar = (raw.get("name_ar") or "").strip()
            barcode = normalize_barcode(raw.get("barcode") or "")
            if not sku or len(sku) > 128:
                raise ValueError(f"Invalid SKU on line {line_number}.")
            if not name_en and not name_ar:
                raise ValueError(f"At least one product name is required on line {line_number}.")
            key = sku.casefold()
            if key in seen_skus:
                raise ValueError(f"Duplicate SKU {sku!r} on line {line_number}.")
            seen_skus.add(key)
            if barcode:
                if barcode in seen_barcodes:
                    raise ValueError(f"Duplicate barcode {barcode!r} on line {line_number}.")
                seen_barcodes.add(barcode)

            rows.append(
                ProductRow(
                    sku=sku,
                    name_en=name_en,
                    name_ar=name_ar,
                    barcode=barcode,
                    ocr_aliases=(raw.get("ocr_aliases") or "").strip(),
                    image_paths=split_pipe(raw.get("image_paths") or ""),
                    image_urls=split_pipe(raw.get("image_urls") or ""),
                    active=parse_bool(raw.get("active") or ""),
                )
            )
            if len(rows) > MAX_PRODUCTS:
                raise ValueError(f"Input catalogue exceeds {MAX_PRODUCTS} products.")
    return rows


def normalize_barcode(value: str) -> str:
    return "".join(ch.upper() for ch in (value or "") if ch.isalnum())


def read_local_image(base_directory: pathlib.Path, value: str) -> bytes:
    path = pathlib.Path(value)
    if not path.is_absolute():
        path = (base_directory / path).resolve()
    data = path.read_bytes()
    if not data:
        raise ValueError(f"Product image is empty: {path}")
    return data


def read_remote_image(url: str) -> bytes:
    parsed = urllib.parse.urlparse(url)
    if parsed.scheme.lower() != "https" or not parsed.netloc:
        raise ValueError(f"Product image URL must be absolute HTTPS: {url}")
    request = urllib.request.Request(url, headers={"User-Agent": "AthmarBulkBootstrap/1"})
    with urllib.request.urlopen(request, timeout=30) as response:
        length = response.headers.get("Content-Length")
        if length and int(length) > MAX_REMOTE_IMAGE_BYTES:
            raise ValueError(f"Remote image exceeds {MAX_REMOTE_IMAGE_BYTES} bytes: {url}")
        data = response.read(MAX_REMOTE_IMAGE_BYTES + 1)
    if len(data) == 0 or len(data) > MAX_REMOTE_IMAGE_BYTES:
        raise ValueError(f"Remote image is empty or too large: {url}")
    return data


def product_images(row: ProductRow, base_directory: pathlib.Path, maximum: int) -> Iterable[bytes]:
    emitted = 0
    for value in row.image_paths:
        if emitted >= maximum:
            return
        yield read_local_image(base_directory, value)
        emitted += 1
    for value in row.image_urls:
        if emitted >= maximum:
            return
        yield read_remote_image(value)
        emitted += 1


def prototype_for(
    row: ProductRow,
    base_directory: pathlib.Path,
    session: GenericEmbeddingSession,
    maximum_images: int,
) -> np.ndarray | None:
    vectors = [session.embed(data) for data in product_images(row, base_directory, maximum_images)]
    if not vectors:
        return None
    prototype = np.mean(np.stack(vectors, axis=0), axis=0)
    return normalize(prototype)


def projection_signature(vector: Sequence[float]) -> int:
    accumulators = [0.0] * 64
    for index, raw in enumerate(vector):
        value = float(raw)
        bit_a = index & 63
        bit_b = (index * 37 + 17) & 63
        sign_a = 1.0 if ((index * 17 + bit_a * 13) & 1) == 0 else -1.0
        sign_b = 1.0 if ((index * 29 + bit_b * 7 + 1) & 1) == 0 else -1.0
        accumulators[bit_a] += value * sign_a
        accumulators[bit_b] += value * sign_b
    signature = 0
    for bit, value in enumerate(accumulators):
        if value >= 0.0:
            signature |= 1 << bit
    return signature


def quantize(vector: np.ndarray) -> bytes:
    clipped = np.clip(vector, -1.0, 1.0)
    quantized = np.rint(clipped * 127.0).astype(np.int8)
    if not np.any(quantized):
        raise ValueError("Quantized embedding has zero magnitude.")
    return quantized.tobytes(order="C")


def write_catalogue(path: pathlib.Path, rows: Sequence[ProductRow]) -> None:
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(
            handle,
            fieldnames=["sku", "name_en", "name_ar", "barcode", "ocr_aliases", "active"],
        )
        writer.writeheader()
        for row in rows:
            writer.writerow(
                {
                    "sku": row.sku,
                    "name_en": row.name_en,
                    "name_ar": row.name_ar,
                    "barcode": row.barcode,
                    "ocr_aliases": row.ocr_aliases,
                    "active": "true" if row.active else "false",
                }
            )


def write_embedding_index(path: pathlib.Path, records: Sequence[tuple[str, np.ndarray]]) -> None:
    model_id_bytes = MODEL_ID.encode("utf-8")
    with path.open("wb") as handle:
        handle.write(MAGIC)
        handle.write(struct.pack("<iii", FORMAT_VERSION, FEATURE_DIMENSION, len(records)))
        handle.write(struct.pack("<i", len(model_id_bytes)))
        handle.write(model_id_bytes)
        for sku, vector in records:
            sku_bytes = sku.encode("utf-8")
            if len(sku_bytes) == 0 or len(sku_bytes) > 1024:
                raise ValueError(f"SKU cannot be encoded in the bulk index: {sku!r}")
            handle.write(struct.pack("<H", len(sku_bytes)))
            handle.write(sku_bytes)
            handle.write(struct.pack("<Q", projection_signature(vector)))
            handle.write(quantize(vector))


def sha256_file(path: pathlib.Path) -> str:
    import hashlib

    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def build(args: argparse.Namespace) -> int:
    source = pathlib.Path(args.catalogue).resolve()
    model = pathlib.Path(args.model).resolve()
    output = pathlib.Path(args.output_dir).resolve()
    output.mkdir(parents=True, exist_ok=True)

    rows = load_rows(source)
    session = GenericEmbeddingSession(model)
    records: List[tuple[str, np.ndarray]] = []
    missing_images: List[str] = []
    failed: List[dict] = []

    active_rows = [row for row in rows if row.active]
    for position, row in enumerate(active_rows, start=1):
        try:
            prototype = prototype_for(row, source.parent, session, args.max_images_per_sku)
            if prototype is None:
                missing_images.append(row.sku)
            else:
                records.append((row.sku, prototype))
        except Exception as exc:  # per-SKU fail report; strict mode can stop the build
            failed.append({"sku": row.sku, "error": str(exc)})
            if args.strict_images:
                raise
        if args.progress_every > 0 and position % args.progress_every == 0:
            print(f"processed {position}/{len(active_rows)} active SKUs", file=sys.stderr)

    catalogue_path = output / OUTPUT_CATALOGUE
    embeddings_path = output / OUTPUT_EMBEDDINGS
    write_catalogue(catalogue_path, rows)
    write_embedding_index(embeddings_path, records)

    report = {
        "model_id": MODEL_ID,
        "embedding_dimension": FEATURE_DIMENSION,
        "catalogue_products": len(rows),
        "active_products": len(active_rows),
        "visual_prototypes": len(records),
        "missing_product_images": missing_images,
        "failed_products": failed,
        "bulk_catalogue_sha256": sha256_file(catalogue_path),
        "bulk_embedding_index_sha256": sha256_file(embeddings_path),
        "raw_images_persisted": False,
    }
    (output / OUTPUT_REPORT).write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0


def parse_args(argv: Sequence[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Build Athmar bulk catalogue embeddings from existing product images.")
    parser.add_argument("--catalogue", required=True, help="Input CSV with product metadata and image paths/URLs.")
    parser.add_argument("--model", required=True, help="Prepared generic embedding ONNX model.")
    parser.add_argument("--output-dir", required=True, help="Destination for bulk_products.csv and bulk_embeddings.bin.")
    parser.add_argument("--max-images-per-sku", type=int, default=3, choices=range(1, 9))
    parser.add_argument("--strict-images", action="store_true", help="Fail the entire build when a product image cannot be embedded.")
    parser.add_argument("--progress-every", type=int, default=250)
    return parser.parse_args(argv)


if __name__ == "__main__":
    raise SystemExit(build(parse_args(sys.argv[1:])))
