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

INVARIANT_CASE_FIELDS = {
    "schema_version",
    "protocol_id",
    "study",
    "case_id",
    "category",
    "exact_input",
    "proposed_action",
    "validation_outcome",
    "executed_state",
    "reward_disposition",
    "model_update_count",
    "expected",
    "actual",
    "passed",
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
) -> tuple[
    int,
    set[tuple[str, str, str, str, int]],
    dict[str, set[str]],
]:
    count = 0
    sessions: set[tuple[str, str, str, str, int]] = set()
    policy_versions: dict[str, set[str]] = {}
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
            decision = event.get("policy_decision")
            if event["event_type"] == "decision" and isinstance(decision, dict):
                policy_versions.setdefault(event["experiment_id"], set()).add(
                    decision["policy_version"]
                )
            count += 1
    return count, sessions, policy_versions


def validate_invariant_cases(path: Path) -> tuple[int, int]:
    count = 0
    passed = 0
    case_ids: set[str] = set()
    with path.open("r", encoding="utf-8") as handle:
        for line_number, line in enumerate(handle, start=1):
            if not line.strip():
                raise ValueError(f"Blank JSONL row at {path}:{line_number}")
            case = json.loads(line)
            if set(case) != INVARIANT_CASE_FIELDS:
                raise ValueError(
                    f"Invariant field mismatch at {path}:{line_number}"
                )
            if case["protocol_id"] != PROTOCOL_ID or case["study"] != "study-d":
                raise ValueError(
                    f"Invariant identity mismatch at {path}:{line_number}"
                )
            if not isinstance(case["exact_input"], dict):
                raise ValueError(
                    f"Invariant exact_input is not an object at {path}:{line_number}"
                )
            case_id = case["case_id"]
            if case_id in case_ids:
                raise ValueError(f"Duplicate invariant case ID: {case_id}")
            case_ids.add(case_id)
            if not isinstance(case["model_update_count"], int) or case["model_update_count"] < 0:
                raise ValueError(
                    f"Invalid model update count at {path}:{line_number}"
                )
            count += 1
            passed += int(case["passed"] is True)
    return count, passed


def validate_study_c_summary(path: Path) -> None:
    expected_experiments = {
        f"study-c-a{alpha}-r{ridge}"
        for alpha in ("0p00", "0p10", "0p25", "0p50")
        for ridge in ("0p10", "1p00", "10p00")
    }
    expected_archetypes = {
        "A1PreferenceAlignedBalanced",
        "A5MotionAverseThreshold",
        "A6PreferencePhysiologyConflict",
        "A8NonStationaryResponder",
    }
    observed: set[tuple[str, str, int]] = set()
    with path.open("r", encoding="utf-8", newline="") as handle:
        for row in csv.DictReader(handle):
            if row["experiment_id"] not in expected_experiments:
                raise ValueError("Unexpected Study C experiment ID")
            if row["archetype"] not in expected_archetypes:
                raise ValueError("Unexpected Study C archetype")
            if row["scenario"] != "R0" or row["policy"] != "ContextualBandit":
                raise ValueError("Unexpected Study C scenario or policy")
            seed = int(row["seed"])
            if seed < 300000 or seed > 300099:
                raise ValueError("Unexpected Study C seed")
            key = (row["experiment_id"], row["archetype"], seed)
            if key in observed:
                raise ValueError("Duplicate Study C session key")
            observed.add(key)
    if len(observed) != 4800:
        raise ValueError("Study C does not contain the frozen 4,800-session grid")


def validate_invariant_manifest(
    manifest: dict,
    manifest_path: Path,
) -> dict[str, int | str | bool]:
    root = manifest_path.parent.parent
    declared = manifest.get("output_sha256")
    if not isinstance(declared, dict) or len(declared) != 1:
        raise ValueError("Study D manifest must declare exactly one case artifact")
    relative, expected_sha = next(iter(declared.items()))
    path = (root / relative).resolve()
    if root not in path.parents or not path.is_file():
        raise ValueError("Study D case artifact is missing or outside the root")
    if sha256(path) != expected_sha:
        raise ValueError("Study D case artifact SHA-256 mismatch")
    case_count, passed_count = validate_invariant_cases(path)
    if case_count != manifest.get("case_count") or case_count != 206:
        raise ValueError("Study D case count does not match the frozen catalog")
    if passed_count != manifest.get("passed_case_count"):
        raise ValueError("Study D passed-case count does not match JSONL")
    if manifest.get("failed_case_count") != case_count - passed_count:
        raise ValueError("Study D failed-case count is inconsistent")
    if not manifest.get("all_cases_passed") or passed_count != case_count:
        raise ValueError("Study D contains one or more failed invariant cases")
    return {
        "status": "valid",
        "case_count": case_count,
        "passed_case_count": passed_count,
        "checked_outputs": 1,
        "all_cases_passed": True,
    }


def validate(manifest_path: Path) -> dict[str, int | str | bool]:
    manifest_path = manifest_path.resolve()
    with manifest_path.open("r", encoding="utf-8") as handle:
        manifest = json.load(handle)
    if manifest.get("protocol_id") != PROTOCOL_ID:
        raise ValueError("Manifest protocol ID does not match the validator")
    if manifest.get("status") != "completed":
        raise ValueError("Only completed manifests are valid result packages")

    if manifest.get("artifact_type") == "deterministic-invariant-suite":
        return validate_invariant_manifest(manifest, manifest_path)

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
    transition_count, transition_sessions, policy_versions = validate_transitions(
        transition_path
    )
    summary_count = validate_csv(summary_path)
    for path in aggregate_paths:
        validate_csv(path)

    if transition_count != manifest.get("transition_line_count"):
        raise ValueError("Manifest transition_line_count does not match JSONL")
    if summary_count != manifest.get("session_count"):
        raise ValueError("Manifest session_count does not match summary CSV")
    if len(transition_sessions) != summary_count:
        raise ValueError("JSONL and summary CSV contain different session counts")
    if manifest.get("study") == "StudyCLinUcbSensitivity":
        if summary_count != 4800:
            raise ValueError("A full Study C run requires 4,800 sessions")
        validate_study_c_summary(summary_path)
        grid = manifest.get("linucb_sensitivity_grid", {})
        if grid.get("production_cell") != "a0p25-r1p00":
            raise ValueError("Study C production cell is missing or changed")
        if len(grid.get("cells", [])) != 12:
            raise ValueError("Study C manifest does not declare 12 cells")
        for cell in grid["cells"]:
            experiment_id = cell["experiment_id"]
            configuration_id = cell["configuration_id"]
            versions = policy_versions.get(experiment_id, set())
            if not versions or any(
                configuration_id not in version for version in versions
            ):
                raise ValueError(
                    "Study C transition log does not match its declared "
                    f"LinUCB cell: {experiment_id}"
                )
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
