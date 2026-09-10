#!/usr/bin/env python3
"""Prepare the official YOLOX-Nano ONNX model for Athmar Vision Count.

The official YOLOX ONNX export uses NCHW BGR float input in the 0..255 range and,
for the pre-generated model, emits raw box regression plus probability scores.
Athmar's Unity camera tensor is NCHW RGB float in the 0..1 range and its decoder
expects decoded center-x/center-y/width/height coordinates.

This script keeps the trained weights unchanged and adds only deterministic graph
operations for:
  1. RGB 0..1 -> BGR 0..255 input adaptation;
  2. YOLOX grid/stride box decoding for the 416x416 Nano model.

No NMS or class filtering is embedded here; those remain in the audited Unity
YoloOutputDecoder.
"""

from __future__ import annotations

import argparse
import hashlib
from pathlib import Path

import numpy as np
import onnx
from onnx import TensorProto, helper, numpy_helper

INPUT_SHAPE = [1, 3, 416, 416]
OUTPUT_SHAPE = [1, 3549, 85]
STRIDES = (8, 16, 32)
PREFIX = "athmar_yolox_"


def _shape(value_info: onnx.ValueInfoProto) -> list[int | None]:
    tensor_type = value_info.type.tensor_type
    result: list[int | None] = []
    for dimension in tensor_type.shape.dim:
        result.append(dimension.dim_value if dimension.HasField("dim_value") else None)
    return result


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _initializer(name: str, value: np.ndarray) -> onnx.TensorProto:
    return numpy_helper.from_array(value, name=name)


def _slice_initializers(name: str, start: int, end: int) -> list[onnx.TensorProto]:
    return [
        _initializer(f"{name}_starts", np.asarray([start], dtype=np.int64)),
        _initializer(f"{name}_ends", np.asarray([end], dtype=np.int64)),
        _initializer(f"{name}_axes", np.asarray([2], dtype=np.int64)),
        _initializer(f"{name}_steps", np.asarray([1], dtype=np.int64)),
    ]


def _make_grid() -> tuple[np.ndarray, np.ndarray]:
    grids: list[np.ndarray] = []
    strides: list[np.ndarray] = []
    for stride in STRIDES:
        height = INPUT_SHAPE[2] // stride
        width = INPUT_SHAPE[3] // stride
        grid_y, grid_x = np.meshgrid(
            np.arange(height, dtype=np.float32),
            np.arange(width, dtype=np.float32),
            indexing="ij",
        )
        grid = np.stack((grid_x, grid_y), axis=-1).reshape(1, -1, 2)
        stride_values = np.full((1, height * width, 1), float(stride), dtype=np.float32)
        grids.append(grid)
        strides.append(stride_values)

    merged_grid = np.concatenate(grids, axis=1).astype(np.float32, copy=False)
    merged_strides = np.concatenate(strides, axis=1).astype(np.float32, copy=False)
    if list(merged_grid.shape) != [1, OUTPUT_SHAPE[1], 2]:
        raise RuntimeError(f"Unexpected YOLOX grid shape: {merged_grid.shape}")
    if list(merged_strides.shape) != [1, OUTPUT_SHAPE[1], 1]:
        raise RuntimeError(f"Unexpected YOLOX stride shape: {merged_strides.shape}")
    return merged_grid, merged_strides


def prepare(source_path: Path, output_path: Path) -> None:
    model = onnx.load(str(source_path))
    onnx.checker.check_model(model)

    if len(model.graph.input) != 1:
        raise RuntimeError(f"Expected one YOLOX input, found {len(model.graph.input)}")
    if len(model.graph.output) != 1:
        raise RuntimeError(f"Expected one YOLOX output, found {len(model.graph.output)}")

    source_input = model.graph.input[0]
    source_output = model.graph.output[0]
    source_input_shape = _shape(source_input)
    source_output_shape = _shape(source_output)
    if source_input_shape != INPUT_SHAPE:
        raise RuntimeError(f"Expected input {INPUT_SHAPE}, found {source_input_shape}")
    if source_output_shape != OUTPUT_SHAPE:
        raise RuntimeError(f"Expected output {OUTPUT_SHAPE}, found {source_output_shape}")
    if source_input.type.tensor_type.elem_type != TensorProto.FLOAT:
        raise RuntimeError("YOLOX input must be float32.")
    if source_output.type.tensor_type.elem_type != TensorProto.FLOAT:
        raise RuntimeError("YOLOX output must be float32.")

    opsets = {entry.domain: entry.version for entry in model.opset_import}
    default_opset = opsets.get("", 0)
    if default_opset < 11:
        raise RuntimeError(f"YOLOX ONNX opset {default_opset} is too old; expected >= 11.")

    existing_names = {initializer.name for initializer in model.graph.initializer}
    existing_names.update(value.name for value in model.graph.input)
    existing_names.update(value.name for value in model.graph.output)
    if any(name.startswith(PREFIX) for name in existing_names):
        raise RuntimeError("Model already appears to contain Athmar YOLOX adapter tensors.")

    original_input_name = source_input.name
    unity_input_name = f"{PREFIX}unity_rgb01"
    scaled_input_name = f"{PREFIX}scaled_rgb255"

    # Replace the external input with Unity's normalized RGB tensor. The Gather
    # emits the original input tensor name, so the trained graph remains unchanged.
    unity_input = helper.make_tensor_value_info(unity_input_name, TensorProto.FLOAT, INPUT_SHAPE)
    del model.graph.input[:]
    model.graph.input.extend([unity_input])

    scale_name = f"{PREFIX}scale255"
    channel_indices_name = f"{PREFIX}bgr_indices"
    model.graph.initializer.extend(
        [
            _initializer(scale_name, np.asarray(255.0, dtype=np.float32)),
            _initializer(channel_indices_name, np.asarray([2, 1, 0], dtype=np.int64)),
        ]
    )

    original_nodes = list(model.graph.node)
    del model.graph.node[:]
    model.graph.node.extend(
        [
            helper.make_node(
                "Mul",
                [unity_input_name, scale_name],
                [scaled_input_name],
                name=f"{PREFIX}scale_input",
            ),
            helper.make_node(
                "Gather",
                [scaled_input_name, channel_indices_name],
                [original_input_name],
                axis=1,
                name=f"{PREFIX}rgb_to_bgr",
            ),
        ]
    )
    model.graph.node.extend(original_nodes)

    raw_output_name = source_output.name
    xy_name = f"{PREFIX}raw_xy"
    wh_name = f"{PREFIX}raw_wh"
    scores_name = f"{PREFIX}probabilities"
    xy_grid_name = f"{PREFIX}xy_plus_grid"
    decoded_xy_name = f"{PREFIX}decoded_xy"
    exp_wh_name = f"{PREFIX}exp_wh"
    decoded_wh_name = f"{PREFIX}decoded_wh"
    decoded_output_name = f"{PREFIX}detections"

    for name, start, end in (
        (f"{PREFIX}slice_xy", 0, 2),
        (f"{PREFIX}slice_wh", 2, 4),
        (f"{PREFIX}slice_scores", 4, OUTPUT_SHAPE[2]),
    ):
        model.graph.initializer.extend(_slice_initializers(name, start, end))

    grid, stride_values = _make_grid()
    grid_name = f"{PREFIX}grid"
    stride_name = f"{PREFIX}strides"
    model.graph.initializer.extend(
        [
            _initializer(grid_name, grid),
            _initializer(stride_name, stride_values),
        ]
    )

    def slice_node(prefix: str, output_name: str) -> onnx.NodeProto:
        return helper.make_node(
            "Slice",
            [
                raw_output_name,
                f"{prefix}_starts",
                f"{prefix}_ends",
                f"{prefix}_axes",
                f"{prefix}_steps",
            ],
            [output_name],
            name=prefix,
        )

    model.graph.node.extend(
        [
            slice_node(f"{PREFIX}slice_xy", xy_name),
            slice_node(f"{PREFIX}slice_wh", wh_name),
            slice_node(f"{PREFIX}slice_scores", scores_name),
            helper.make_node(
                "Add",
                [xy_name, grid_name],
                [xy_grid_name],
                name=f"{PREFIX}add_grid",
            ),
            helper.make_node(
                "Mul",
                [xy_grid_name, stride_name],
                [decoded_xy_name],
                name=f"{PREFIX}scale_xy",
            ),
            helper.make_node(
                "Exp",
                [wh_name],
                [exp_wh_name],
                name=f"{PREFIX}exp_wh",
            ),
            helper.make_node(
                "Mul",
                [exp_wh_name, stride_name],
                [decoded_wh_name],
                name=f"{PREFIX}scale_wh",
            ),
            helper.make_node(
                "Concat",
                [decoded_xy_name, decoded_wh_name, scores_name],
                [decoded_output_name],
                axis=2,
                name=f"{PREFIX}decoded_output",
            ),
        ]
    )

    del model.graph.output[:]
    model.graph.output.extend(
        [helper.make_tensor_value_info(decoded_output_name, TensorProto.FLOAT, OUTPUT_SHAPE)]
    )
    model.producer_name = "Athmar Vision Count YOLOX adapter"

    onnx.checker.check_model(model, full_check=True)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    onnx.save(model, str(output_path))

    reloaded = onnx.load(str(output_path))
    onnx.checker.check_model(reloaded, full_check=True)
    if _shape(reloaded.graph.input[0]) != INPUT_SHAPE:
        raise RuntimeError("Prepared model input shape changed unexpectedly.")
    if _shape(reloaded.graph.output[0]) != OUTPUT_SHAPE:
        raise RuntimeError("Prepared model output shape changed unexpectedly.")

    print(f"SOURCE_SHA256={_sha256(source_path)}")
    print(f"PREPARED_SHA256={_sha256(output_path)}")
    print(f"INPUT={INPUT_SHAPE}")
    print(f"OUTPUT={OUTPUT_SHAPE}")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    prepare(args.input.resolve(), args.output.resolve())


if __name__ == "__main__":
    main()
