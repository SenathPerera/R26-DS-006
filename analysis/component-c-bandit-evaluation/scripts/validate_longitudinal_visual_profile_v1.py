"""Fail-closed preflight for the frozen longitudinal study's visual bundle."""

from __future__ import annotations

import hashlib
import json
import re
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
CONFIG = ROOT / "analysis/component-c-bandit-evaluation/configs/longitudinal-visual-v1.0.json"


def field_value(text: str, name: str) -> str:
    match = re.search(rf"^\s*{re.escape(name)}:\s*(.+?)\s*$", text, re.MULTILINE)
    if match is None:
        raise ValueError(f"Missing Unity YAML field: {name}")
    return match.group(1)


def verify_asset(spec: dict) -> None:
    path = ROOT / spec["path"]
    data = path.read_bytes()
    normalized = data.replace(b"\r\n", b"\n")
    actual_hash = hashlib.sha256(normalized).hexdigest()
    if actual_hash != spec["sha256_lf"]:
        raise ValueError(f"Visual asset checksum mismatch: {spec['path']}")

    actual_guid = field_value(Path(str(path) + ".meta").read_text(), "guid")
    if actual_guid != spec["guid"]:
        raise ValueError(f"Visual asset GUID mismatch: {spec['path']}")

    text = normalized.decode("utf-8")
    for name, expected in spec["fields"].items():
        actual = field_value(text, name)
        if isinstance(expected, list):
            match = re.fullmatch(r"\{x:\s*([^,]+),\s*y:\s*([^}]+)\}", actual)
            if match is None or any(
                abs(float(value) - target) > 1e-6
                for value, target in zip(match.groups(), expected)
            ):
                raise ValueError(f"Visual asset field mismatch: {spec['path']}:{name}")
        elif isinstance(expected, (int, float)):
            if abs(float(actual) - expected) > 1e-6:
                raise ValueError(f"Visual asset field mismatch: {spec['path']}:{name}")
        elif actual != expected:
            raise ValueError(f"Visual asset field mismatch: {spec['path']}:{name}")


def main() -> int:
    try:
        config = json.loads(CONFIG.read_text(encoding="utf-8"))
        if config["protocol_id"] != "laminar-vr-longitudinal-simulation-v1.0":
            raise ValueError("Wrong longitudinal protocol ID")
        if config["pilot_bundle_id"] != "temple-pond-pilot-freeze-v3":
            raise ValueError("Wrong visual pilot bundle")
        for key in ("scene_asset", "mapping_asset"):
            verify_asset(config[key])

        scene = (ROOT / config["unity_scene_path"]).read_text(encoding="utf-8")
        for key, field in (
            ("scene_asset", "sceneParameterProfile"),
            ("mapping_asset", "mappingProfile"),
        ):
            actual = field_value(scene, field)
            expected = config[key]["guid"]
            if not re.fullmatch(
                rf"\{{fileID:\s*11400000,\s*guid:\s*{expected},\s*type:\s*2\}}",
                actual,
            ):
                raise ValueError(f"Scene does not reference the study {key}")
    except (OSError, ValueError, KeyError, TypeError) as error:
        print(f"Longitudinal visual preflight FAILED: {error}", file=sys.stderr)
        return 1

    print("Longitudinal visual preflight passed: Temple Pond v3 / mapping v4")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
