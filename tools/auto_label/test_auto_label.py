from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from auto_label import (
    Detection,
    box_to_yolo,
    classify_brand,
    non_maximum_suppression,
    normalize_text,
    tile_regions,
)


class AutoLabelTests(unittest.TestCase):
    def test_normalize_text(self) -> None:
        self.assertEqual(normalize_text(" K-E.N/T "), "KENT")

    def test_brand_exact_matches(self) -> None:
        self.assertEqual(classify_brand("KENT OFFICE FILE")[:2], (0, "KENT"))
        self.assertEqual(classify_brand("ROCO")[:2], (1, "ROCO"))
        self.assertEqual(classify_brand("LENO")[:2], (2, "LENO"))

    def test_uncertain_brand_is_not_guessed(self) -> None:
        class_id, brand, _ = classify_brand("GTC 2.25 MAR", threshold=0.76)
        self.assertIsNone(class_id)
        self.assertIsNone(brand)

    def test_tile_regions_include_full_image_and_edges(self) -> None:
        regions = tile_regions(1536, 864, tile_size=960, overlap=0.25)
        self.assertEqual(regions[0], (0, 0, 1536, 864, "full"))
        self.assertTrue(any(right == 1536 for _, _, right, _, _ in regions))
        self.assertTrue(any(bottom == 864 for _, _, _, bottom, _ in regions))

    def test_nms_removes_duplicate_tile_box(self) -> None:
        first = Detection("x.jpg", 10, 10, 110, 210, 0.9, "ring binder", "full")
        duplicate = Detection("x.jpg", 12, 12, 112, 212, 0.8, "lever arch file binder", "tile")
        neighbour = Detection("x.jpg", 120, 10, 220, 210, 0.7, "ring binder", "tile")
        kept = non_maximum_suppression([duplicate, neighbour, first], 0.45)
        self.assertEqual(kept, [first, neighbour])

    def test_yolo_serialization(self) -> None:
        detection = Detection(
            "x.jpg",
            100,
            50,
            300,
            250,
            0.9,
            "water bottle",
            "full",
            class_id=3,
            class_name="water_bottle",
            status="accepted",
        )
        self.assertEqual(box_to_yolo(detection, 400, 400), "3 0.500000 0.375000 0.500000 0.500000")


if __name__ == "__main__":
    unittest.main()
