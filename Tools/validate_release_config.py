#!/usr/bin/env python3
"""Small dependency-free guard for privacy and Unity CI configuration."""

import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
EXPECTED_UNITY = (ROOT / "ProjectSettings/ProjectVersion.txt").read_text().splitlines()[0].split(":", 1)[1].strip()
config = json.loads((ROOT / "Assets/Resources/CONFIG.json").read_text())
errors = []

sync = config.get("sync", {})
if sync.get("enabled", True):
    errors.append("Network sync must remain disabled in the distributable default config")
if sync.get("base_url"):
    errors.append("Default config must not embed a sync endpoint")

for workflow in (ROOT / ".github/workflows").glob("*.yml"):
    text = workflow.read_text()
    for configured in re.findall(r"unityVersion:\s*([^\s]+)", text):
        if configured != EXPECTED_UNITY:
            errors.append(f"{workflow.name}: Unity {configured} != project {EXPECTED_UNITY}")

if errors:
    print("Release configuration validation failed:")
    print("\n".join(f"- {error}" for error in errors))
    sys.exit(1)

print(f"Release configuration OK (Unity {EXPECTED_UNITY}; sync disabled).")
