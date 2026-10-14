#!/usr/bin/env python3
"""Validate Component C evaluation artifacts without third-party packages."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
from pathlib import Path


PROTOCOL_ID = "component-c-simulation-evaluation-v1.2"
COMMON_EVENT_FIELDS = {
    "schema_version",
    "protocol_id",
    "event_type",
    "experiment_id",
    "archetype",
    "scenario",
    "policy",
    "seed",
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def validate_csv(path: Path) -> int:
    with path.open("r", encoding="utf-8", newline="") as handle:
        rows = csv.reader(handle)
        header = next(rows, None)
        if not header or len(header) != len(set(header)):
            raise ValueError(f"Invalid or duplicate CSV header: {path}")
        protocol_index = header.index("protocol_id")
        count = 0
        for line_number, row in enumerate(rows, start=2):
            if len(row) != len(header):
                raise ValueError(
                    f"CSV width mismatch at {path}:{line_number}: "
                    f"expected {len(header)}, got {len(row)}"
                )
            if row[protocol_index] != PROTOCOL_ID:
                raise ValueError(
                    f"Protocol mismatch at {path}:{line_number}"
                )
            count += 1
    return count


def validate_transitions(
    path: Path,
) -> tuple[int, set[tuple[str, str, str, str, int]]]:
    count = 0
    sessions: set[tuple[str, str, str, str, int]] = set()
    with path.open("r", encoding="utf-8") as handle:
        for line_number, line in enumerate(handle, start=1):
            if not line.strip():
                raise ValueError(f"Blank JSONL row at {path}:{line_number}")
            event = json.loads(line)
            missing = COMMON_EVENT_FIELDS.difference(event)
            if missing:
                raise ValueError(
                    f"Missing fields at {path}:{line_number}: {sorted(missing)}"
                )
            if event["protocol_id"] != PROTOCOL_ID:
                raise ValueError(
                    f"Protocol mismatch at {path}:{line_number}"
                )
            if event["event_type"] not in {"physiology", "decision", "reward"}:
                raise ValueError(
                    f"Unknown event_type at {path}:{line_number}"
                )
            sessions.add(
                (
                    event["experiment_id"],
                    event["archetype"],
                    event["scenario"],
                    event["policy"],
                    int(event["seed"]),
                )
            )
            count += 1
    return count, sessions


def validate(manifest_path: Path) -> dict[str, int | str | bool]:
    manifest_path = manifest_path.resolve()
    with manifest_path.open("r", encoding="utf-8") as handle:
        manifest = json.load(handle)
    if manifest.get("protocol_id") != PROTOCOL_ID:
        raise ValueError("Manifest protocol ID does not match the validator")
    if manifest.get("status") != "completed":
        raise ValueError("Only completed manifests are valid result packages")

    root = manifest_path.parent.parent
    declared = manifest.get("output_sha256")
    if not isinstance(declared, dict) or not declared:
        raise ValueError("Manifest has no output_sha256 map")
    resolved: dict[str, Path] = {}
    for relative, expected in declared.items():
        path = (root / relative).resolve()
        if root not in path.parents:
            raise ValueError(f"Output path escapes evaluation root: {relative}")
        if not path.is_file():
            raise FileNotFoundError(path)
        actual = sha256(path)
        if actual != expected:
            raise ValueError(f"SHA-256 mismatch: {relative}")
        resolved[relative] = path

    transition_path = next(
        path for relative, path in resolved.items() if relative.endswith(".jsonl")
    )
    summary_path = next(
        path
        for relative, path in resolved.items()
        if "/session-summaries/" in f"/{relative}"
    )
    aggregate_paths = [
        path
        for relative, path in resolved.items()
        if "/aggregate-tables/" in f"/{relative}"
    ]
    transition_count, transition_sessions = validate_transitions(transition_path)
    summary_count = validate_csv(summary_path)
    for path in aggregate_paths:
        validate_csv(path)

    if transition_count != manifest.get("transition_line_count"):
        raise ValueError("Manifest transition_line_count does not match JSONL")
    if summary_count != manifest.get("session_count"):
        raise ValueError("Manifest session_count does not match summary CSV")
    if len(transition_sessions) != summary_count:
        raise ValueError("JSONL and summary CSV contain different session counts")
    if (
        manifest.get("study") == "StudyAPrimaryPolicyComparison"
        and summary_count == 7200
        and not manifest.get("confirmatory_inference_complete")
    ):
        raise ValueError("A full Study A run requires complete H3/H4 inference")
    if (
        manifest.get("study") == "StudyAPrimaryPolicyComparison"
        and summary_count == 7200
        and not manifest.get("convergence_diagnostic_complete")
    ):
        raise ValueError("A full Study A run requires the convergence diagnostic")

    return {
        "status": "valid",
        "session_count": summary_count,
        "transition_line_count": transition_count,
        "checked_outputs": len(resolved),
        "confirmatory_inference_complete": bool(
            manifest.get("confirmatory_inference_complete")
        ),
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("manifest", type=Path)
    arguments = parser.parse_args()
    print(json.dumps(validate(arguments.manifest), indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
