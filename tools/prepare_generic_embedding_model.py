#!/usr/bin/env python3
"""Prepare the one generic Athmar product-embedding ONNX model for Unity.

The upstream model is a pinned TIMM MobileNetV3 Small classifier export. This script:
1. downloads the exact upstream ONNX artifact;
2. verifies its published SHA-256 before parsing it;
3. exposes the 1024-D pre-classifier feature vector instead of ImageNet logits;
4. prepends NHWC -> NCHW and ImageNet mean/std normalization so Unity can feed RGB [0,1];
5. validates the resulting ONNX graph and writes provenance next to the generated model.

Generated artifacts live under Assets/Generated and are intentionally not committed.
"""

from __future__ import annotations

import hashlib
import json
import pathlib
import urllib.request

import onnx
from onnx import TensorProto, helper, numpy_helper, shape_inference
import numpy as np

SOURCE_COMMIT = "1d25e5d466f1df28e8e530e747b8d75b76a36735"
SOURCE_URL = (
    "https://huggingface.co/deepghs/timms/resolve/"
    f"{SOURCE_COMMIT}/mobilenetv3_small_075.lamb_in1k/model.onnx?download=true"
)
SOURCE_SHA256 = "0593e80f3671dee6369804a9440c6fa2fd5337c3e524dde3e11f2376a8b8fcf3"
MODEL_ID = "timm/mobilenetv3_small_075.lamb_in1k"
MODEL_REVISION = "fa65a043c25690a5779ff856052a5ae55ec03eda"
FEATURE_DIMENSION = 1024
INPUT_SIZE = 224
OUTPUT_DIR = pathlib.Path("Assets/Generated/Resources")
OUTPUT_PATH = OUTPUT_DIR / "AthmarGenericEmbedding.onnx"
PROVENANCE_PATH = OUTPUT_DIR / "AthmarGenericEmbedding.provenance.json"
DOWNLOAD_PATH = pathlib.Path(".ci/generic-embedding/upstream.onnx")


def sha256(path: pathlib.Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def download_verified() -> None:
    DOWNLOAD_PATH.parent.mkdir(parents=True, exist_ok=True)
    request = urllib.request.Request(SOURCE_URL, headers={"User-Agent": "athmar-vision-count-ci/1"})
    with urllib.request.urlopen(request, timeout=120) as response, DOWNLOAD_PATH.open("wb") as output:
        while True:
            chunk = response.read(1024 * 1024)
            if not chunk:
                break
            output.write(chunk)
    actual = sha256(DOWNLOAD_PATH)
    if actual != SOURCE_SHA256:
        raise SystemExit(f"Upstream model SHA-256 mismatch: expected {SOURCE_SHA256}, got {actual}")


def find_preclassifier_feature(model: onnx.ModelProto) -> str:
    if len(model.graph.output) != 1:
        raise SystemExit(f"Expected one classifier output, found {len(model.graph.output)}")

    wanted = model.graph.output[0].name
    producers = {output: node for node in model.graph.node for output in node.output if output}
    visited: set[str] = set()
    while wanted in producers and wanted not in visited:
        visited.add(wanted)
        node = producers[wanted]
        if node.op_type in {"Gemm", "MatMul"}:
            if not node.input:
                break
            return node.input[0]
        if node.op_type in {"Softmax", "Identity", "Cast"} and node.input:
            wanted = node.input[0]
            continue
        break
    raise SystemExit("Could not identify the final classifier Gemm/MatMul and its pre-classifier feature tensor")


def replace_input_with_embedded_preprocessing(model: onnx.ModelProto) -> None:
    if len(model.graph.input) != 1:
        raise SystemExit(f"Expected one model input, found {len(model.graph.input)}")

    old_input = model.graph.input[0]
    old_name = old_input.name
    dims = [dimension.dim_value for dimension in old_input.type.tensor_type.shape.dim]
    if len(dims) != 4 or dims[-2:] != [INPUT_SIZE, INPUT_SIZE]:
        raise SystemExit(f"Unexpected upstream input shape: {dims}")

    normalized_name = "athmar_normalized_nchw"
    transpose_name = "athmar_nchw"
    centered_name = "athmar_centered"
    new_input_name = "images"

    for node in model.graph.node:
        for index, value in enumerate(node.input):
            if value == old_name:
                node.input[index] = normalized_name

    del model.graph.input[:]
    model.graph.input.extend([
        helper.make_tensor_value_info(
            new_input_name,
            TensorProto.FLOAT,
            [1, INPUT_SIZE, INPUT_SIZE, 3],
        )
    ])

    mean = np.asarray([0.485, 0.456, 0.406], dtype=np.float32).reshape(1, 3, 1, 1)
    std = np.asarray([0.229, 0.224, 0.225], dtype=np.float32).reshape(1, 3, 1, 1)
    model.graph.initializer.extend([
        numpy_helper.from_array(mean, name="athmar_imagenet_mean"),
        numpy_helper.from_array(std, name="athmar_imagenet_std"),
    ])
    preprocessing = [
        helper.make_node("Transpose", [new_input_name], [transpose_name], perm=[0, 3, 1, 2], name="athmar_nhwc_to_nchw"),
        helper.make_node("Sub", [transpose_name, "athmar_imagenet_mean"], [centered_name], name="athmar_subtract_mean"),
        helper.make_node("Div", [centered_name, "athmar_imagenet_std"], [normalized_name], name="athmar_divide_std"),
    ]
    original_nodes = list(model.graph.node)
    del model.graph.node[:]
    model.graph.node.extend(preprocessing + original_nodes)


def expose_embedding_output(model: onnx.ModelProto, feature_name: str) -> None:
    del model.graph.output[:]
    model.graph.output.extend([
        helper.make_tensor_value_info(feature_name, TensorProto.FLOAT, [1, FEATURE_DIMENSION])
    ])


def main() -> None:
    download_verified()
    model = onnx.load(str(DOWNLOAD_PATH))
    onnx.checker.check_model(model)

    feature_name = find_preclassifier_feature(model)
    replace_input_with_embedded_preprocessing(model)
    expose_embedding_output(model, feature_name)

    model = shape_inference.infer_shapes(model)
    model.producer_name = "Athmar Vision Count generic embedding preparation"
    model.doc_string = (
        "Generic MobileNetV3 Small 0.75 learned image embedding for Athmar Vision Count; "
        "NHWC RGB [0,1] input; ImageNet normalization embedded; 1024-D pre-classifier output."
    )
    onnx.checker.check_model(model)

    output_dims = [dimension.dim_value for dimension in model.graph.output[0].type.tensor_type.shape.dim]
    if output_dims != [1, FEATURE_DIMENSION]:
        raise SystemExit(f"Unexpected prepared embedding shape: {output_dims}")

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    onnx.save(model, str(OUTPUT_PATH))
    prepared_sha = sha256(OUTPUT_PATH)
    provenance = {
        "purpose": "generic_product_embedding",
        "model_id": MODEL_ID,
        "model_revision": MODEL_REVISION,
        "model_card": "https://huggingface.co/timm/mobilenetv3_small_075.lamb_in1k",
        "declared_license": "apache-2.0",
        "upstream_export_repository": "deepghs/timms",
        "upstream_export_commit": SOURCE_COMMIT,
        "upstream_sha256": SOURCE_SHA256,
        "prepared_sha256": prepared_sha,
        "input": {"layout": "NHWC", "shape": [1, INPUT_SIZE, INPUT_SIZE, 3], "range": "0..1 RGB"},
        "output": {"shape": [1, FEATURE_DIMENSION], "semantic": "pre-classifier learned feature vector"},
        "note": "Technical provenance only; commercial/legal approval remains an explicit release gate.",
    }
    PROVENANCE_PATH.write_text(json.dumps(provenance, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(f"Prepared {OUTPUT_PATH} sha256={prepared_sha}")


if __name__ == "__main__":
    main()
