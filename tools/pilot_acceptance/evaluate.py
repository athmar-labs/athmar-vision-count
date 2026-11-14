"""Evaluate pre-review inventory counts against held-out manual ground truth."""

import argparse
import csv
import hashlib
import io
import json
import re
from pathlib import Path


COLUMNS = ["session_id", "sku", "expected_count", "proposed_count"]


def validate_policy(policy):
    if not isinstance(policy, dict):
        raise ValueError("Acceptance policy must be a JSON object.")
    skus = policy.get("skus")
    if not isinstance(skus, list) or not skus or any(
        not isinstance(sku, str) or not sku.strip() or sku != sku.strip() for sku in skus
    ):
        raise ValueError("skus must be a nonempty list of SKU identifiers.")
    if len({sku.casefold() for sku in skus}) != len(skus):
        raise ValueError("SKU identifiers must be unique.")
    for key in ("min_sessions", "min_positive_sessions_per_sku", "min_absent_sessions_per_sku"):
        value = policy.get(key)
        if type(value) is not int or value < 1:
            raise ValueError(f"{key} must be a positive integer.")
    for key in ("min_session_exact_rate", "max_sku_weighted_absolute_error", "max_sku_false_positive_rate"):
        value = policy.get(key)
        if type(value) not in (int, float) or not 0 <= value <= 1:
            raise ValueError(f"{key} must be between 0 and 1.")
    return skus


def read_sessions(source, skus):
    reader = csv.DictReader(source, strict=True)
    if reader.fieldnames != COLUMNS:
        raise ValueError(f"Expected CSV header: {','.join(COLUMNS)}.")
    sessions = {}
    for row in reader:
        session_id, sku, expected, proposed = (row[key] for key in COLUMNS)
        if None in row or not session_id or not session_id.strip() or sku not in skus:
            raise ValueError(f"Invalid row shape, session ID or SKU at CSV line {reader.line_num}.")
        if any(value is None or not re.fullmatch(r"[0-9]{1,10}", value) for value in (expected, proposed)):
            raise ValueError(f"Counts must be nonnegative integers at CSV line {reader.line_num}.")
        session = sessions.setdefault(session_id.strip(), {})
        if sku in session:
            raise ValueError(f"Duplicate session and SKU at CSV line {reader.line_num}.")
        session[sku] = (int(expected), int(proposed))
    if any(session.keys() != skus for session in sessions.values()):
        raise ValueError("Every session must include every in-scope SKU, including explicit zero counts.")
    return sessions


def summarize_sku(pairs, policy):
    positive = sum(expected > 0 for expected, _ in pairs)
    absent = len(pairs) - positive
    expected_units = sum(expected for expected, _ in pairs)
    under = sum(max(expected - proposed, 0) for expected, proposed in pairs)
    over = sum(max(proposed - expected, 0) for expected, proposed in pairs)
    false_positives = sum(expected == 0 and proposed > 0 for expected, proposed in pairs)
    error = (under + over) / expected_units if expected_units else None
    false_positive_rate = false_positives / absent if absent else None
    checks = {
        "positive_coverage": positive >= policy["min_positive_sessions_per_sku"],
        "absent_coverage": absent >= policy["min_absent_sessions_per_sku"],
        "weighted_absolute_error": error is not None and error <= policy["max_sku_weighted_absolute_error"],
        "false_positive_rate": false_positive_rate is not None
        and false_positive_rate <= policy["max_sku_false_positive_rate"],
    }
    return {
        "positive_sessions": positive,
        "absent_sessions": absent,
        "expected_units": expected_units,
        "undercount_units": under,
        "overcount_units": over,
        "false_positive_sessions": false_positives,
        "weighted_absolute_error": error,
        "false_positive_rate": false_positive_rate,
        "checks": checks,
    }


def evaluate(source, policy):
    skus = validate_policy(policy)
    sessions = read_sessions(source, set(skus))
    exact = sum(all(expected == proposed for expected, proposed in session.values()) for session in sessions.values())
    exact_rate = exact / len(sessions) if sessions else None
    checks = {
        "session_coverage": len(sessions) >= policy["min_sessions"],
        "session_exact_rate": exact_rate is not None and exact_rate >= policy["min_session_exact_rate"],
    }
    by_sku = {sku: summarize_sku([session[sku] for session in sessions.values()], policy) for sku in skus}
    return {
        "passed": all(checks.values()) and all(all(result["checks"].values()) for result in by_sku.values()),
        "sessions": len(sessions),
        "exact_sessions": exact,
        "session_exact_rate": exact_rate,
        "checks": checks,
        "skus": by_sku,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("policy", type=Path)
    parser.add_argument("counts", type=Path)
    args = parser.parse_args()
    try:
        policy_bytes = args.policy.read_bytes()
        counts_bytes = args.counts.read_bytes()
        policy = json.loads(policy_bytes)
        report = evaluate(io.StringIO(counts_bytes.decode("utf-8-sig"), newline=""), policy)
    except (ValueError, OSError, csv.Error) as error:
        parser.exit(2, f"Invalid acceptance input: {error}\n")
    report["policy"] = policy
    report["policy_sha256"] = hashlib.sha256(policy_bytes).hexdigest()
    report["counts_sha256"] = hashlib.sha256(counts_bytes).hexdigest()
    print(json.dumps(report, ensure_ascii=False, indent=2, allow_nan=False))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
