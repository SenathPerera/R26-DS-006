#!/usr/bin/env python3
"""Generate deterministic SVG figures from Component C result tables."""

from __future__ import annotations

import argparse
import csv
import hashlib
import html
import json
import math
from pathlib import Path


PROTOCOL_ID = "component-c-simulation-evaluation-v1.2"
WIDTH = 900
HEIGHT = 520
MARGIN_LEFT = 85
MARGIN_RIGHT = 35
MARGIN_TOP = 45
MARGIN_BOTTOM = 75
COLORS = {
    "StaticPersonalized": "#4C78A8",
    "RuleBasedAdaptive": "#F58518",
    "ContextualBandit": "#54A24B",
    "RandomSafe": "#E45756",
}


def read_csv(path: Path) -> list[dict[str, str]]:
    with path.open("r", encoding="utf-8", newline="") as handle:
        rows = list(csv.DictReader(handle))
    if not rows:
        raise ValueError(f"No data rows in {path}")
    for number, row in enumerate(rows, start=2):
        if row.get("protocol_id") != PROTOCOL_ID:
            raise ValueError(f"Protocol mismatch at {path}:{number}")
    return rows


def finite(value: str, field: str) -> float:
    number = float(value)
    if not math.isfinite(number):
        raise ValueError(f"Non-finite {field}: {value}")
    return number


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def svg_document(title: str, body: list[str]) -> str:
    escaped = html.escape(title)
    return "\n".join(
        [
            '<?xml version="1.0" encoding="UTF-8"?>',
            f'<svg xmlns="http://www.w3.org/2000/svg" width="{WIDTH}" '
            f'height="{HEIGHT}" viewBox="0 0 {WIDTH} {HEIGHT}" '
            'role="img">',
            f"<title>{escaped}</title>",
            "<style>",
            "text{font-family:Arial,sans-serif;fill:#202124}",
            ".axis{stroke:#202124;stroke-width:1.2}",
            ".grid{stroke:#d9dce1;stroke-width:1}",
            ".tick{font-size:12px}",
            ".label{font-size:14px;font-weight:600}",
            ".title{font-size:18px;font-weight:700}",
            "</style>",
            *body,
            "</svg>",
            "",
        ]
    )


def scale(value: float, low: float, high: float, start: float, end: float) -> float:
    if high <= low:
        return (start + end) / 2
    return start + ((value - low) / (high - low)) * (end - start)


def nice_bounds(values: list[float], include_zero: bool = False) -> tuple[float, float]:
    low = min(values)
    high = max(values)
    if include_zero:
        low = min(low, 0.0)
        high = max(high, 0.0)
    span = high - low
    if span == 0:
        span = max(1.0, abs(low) * 0.2)
    padding = span * 0.08
    return low - padding, high + padding


def base_axes(
    title: str,
    x_label: str,
    y_label: str,
    x_low: float,
    x_high: float,
    y_low: float,
    y_high: float,
    x_ticks: int = 5,
    y_ticks: int = 5,
) -> list[str]:
    x0 = MARGIN_LEFT
    x1 = WIDTH - MARGIN_RIGHT
    y0 = HEIGHT - MARGIN_BOTTOM
    y1 = MARGIN_TOP
    body = [
        f'<text class="title" x="{WIDTH / 2}" y="27" text-anchor="middle">'
        f"{html.escape(title)}</text>",
        f'<line class="axis" x1="{x0}" y1="{y0}" x2="{x1}" y2="{y0}"/>',
        f'<line class="axis" x1="{x0}" y1="{y0}" x2="{x0}" y2="{y1}"/>',
    ]
    for index in range(x_ticks + 1):
        value = x_low + (x_high - x_low) * index / x_ticks
        x = scale(value, x_low, x_high, x0, x1)
        body.extend(
            [
                f'<line class="grid" x1="{x:.2f}" y1="{y0}" '
                f'x2="{x:.2f}" y2="{y1}"/>',
                f'<text class="tick" x="{x:.2f}" y="{y0 + 21}" '
                f'text-anchor="middle">{value:.3g}</text>',
            ]
        )
    for index in range(y_ticks + 1):
        value = y_low + (y_high - y_low) * index / y_ticks
        y = scale(value, y_low, y_high, y0, y1)
        body.extend(
            [
                f'<line class="grid" x1="{x0}" y1="{y:.2f}" '
                f'x2="{x1}" y2="{y:.2f}"/>',
                f'<text class="tick" x="{x0 - 12}" y="{y + 4:.2f}" '
                f'text-anchor="end">{value:.3g}</text>',
            ]
        )
    body.extend(
        [
            f'<text class="label" x="{(x0 + x1) / 2}" y="{HEIGHT - 20}" '
            f'text-anchor="middle">{html.escape(x_label)}</text>',
            f'<text class="label" transform="translate(22 {(y0 + y1) / 2}) '
            'rotate(-90)" text-anchor="middle">'
            f"{html.escape(y_label)}</text>",
        ]
    )
    return body


def build_ecdf(summary_rows: list[dict[str, str]]) -> str:
    by_policy: dict[str, list[float]] = {policy: [] for policy in COLORS}
    for row in summary_rows:
        if row.get("experiment_id") != "study-a" or not row.get("mean_pseudo_regret"):
            continue
        policy = row.get("policy", "")
        if policy in by_policy:
            by_policy[policy].append(
                finite(row["mean_pseudo_regret"], "mean_pseudo_regret")
            )
    if any(not values for values in by_policy.values()):
        raise ValueError("Study A ECDF requires all four policy conditions")
    all_values = [value for values in by_policy.values() for value in values]
    x_low, x_high = nice_bounds(all_values)
    body = base_axes(
        "Study A safe-oracle pseudo-regret distribution",
        "Session mean pseudo-regret (lower is better)",
        "Empirical cumulative probability",
        x_low,
        x_high,
        0.0,
        1.0,
    )
    x0, x1 = MARGIN_LEFT, WIDTH - MARGIN_RIGHT
    y0, y1 = HEIGHT - MARGIN_BOTTOM, MARGIN_TOP
    for policy, values in by_policy.items():
        ordered = sorted(values)
        points = []
        previous_y = y0
        for index, value in enumerate(ordered, start=1):
            x = scale(value, x_low, x_high, x0, x1)
            y = scale(index / len(ordered), 0.0, 1.0, y0, y1)
            points.extend([(x, previous_y), (x, y)])
            previous_y = y
        encoded = " ".join(f"{x:.2f},{y:.2f}" for x, y in points)
        body.append(
            f'<polyline fill="none" stroke="{COLORS[policy]}" '
            f'stroke-width="2.3" points="{encoded}"/>'
        )
    legend_x = MARGIN_LEFT + 12
    for index, (policy, color) in enumerate(COLORS.items()):
        y = MARGIN_TOP + 18 + index * 22
        body.append(
            f'<line x1="{legend_x}" y1="{y}" x2="{legend_x + 24}" '
            f'y2="{y}" stroke="{color}" stroke-width="3"/>'
        )
        body.append(
            f'<text class="tick" x="{legend_x + 31}" y="{y + 4}">'
            f"{html.escape(policy)}</text>"
        )
    return svg_document("Study A pseudo-regret ECDF", body)


def build_effects(inference_rows: list[dict[str, str]]) -> str:
    rows = [
        row
        for row in inference_rows
        if row.get("hypothesis") in {"H3", "H4"}
        and row.get("complete", "").lower() == "true"
    ]
    if {row["hypothesis"] for row in rows} != {"H3", "H4"}:
        raise ValueError("Complete H3 and H4 inference rows are required")
    rows.sort(key=lambda row: row["hypothesis"])
    values = []
    parsed = []
    for row in rows:
        mean = finite(row["mean_difference"], "mean_difference")
        lower = finite(row["ci95_lower"], "ci95_lower")
        upper = finite(row["ci95_upper"], "ci95_upper")
        p_value = finite(row["two_sided_p_value"], "two_sided_p_value")
        parsed.append((row["hypothesis"], mean, lower, upper, p_value))
        values.extend([mean, lower, upper])
    x_low, x_high = nice_bounds(values, include_zero=True)
    body = base_axes(
        "Confirmatory paired effects",
        "Paired mean pseudo-regret difference (left − right)",
        "Hypothesis",
        x_low,
        x_high,
        0.0,
        3.0,
        y_ticks=3,
    )
    x0, x1 = MARGIN_LEFT, WIDTH - MARGIN_RIGHT
    y0, y1 = HEIGHT - MARGIN_BOTTOM, MARGIN_TOP
    zero_x = scale(0.0, x_low, x_high, x0, x1)
    body.append(
        f'<line x1="{zero_x:.2f}" y1="{y0}" x2="{zero_x:.2f}" '
        f'y2="{y1}" stroke="#111" stroke-dasharray="6 4"/>'
    )
    for index, (hypothesis, mean, lower, upper, p_value) in enumerate(parsed):
        y = scale(index + 1, 0.0, 3.0, y0, y1)
        lower_x = scale(lower, x_low, x_high, x0, x1)
        upper_x = scale(upper, x_low, x_high, x0, x1)
        mean_x = scale(mean, x_low, x_high, x0, x1)
        body.extend(
            [
                f'<line x1="{lower_x:.2f}" y1="{y:.2f}" '
                f'x2="{upper_x:.2f}" y2="{y:.2f}" '
                'stroke="#3c4043" stroke-width="3"/>',
                f'<circle cx="{mean_x:.2f}" cy="{y:.2f}" r="6" '
                'fill="#4C78A8"/>',
                f'<text class="label" x="{x0 + 8}" y="{y - 13:.2f}">'
                f"{hypothesis} (p={p_value:.4g})</text>",
            ]
        )
    return svg_document("Confirmatory paired effects", body)


def build_convergence(convergence_rows: list[dict[str, str]]) -> str:
    rows = [
        row
        for row in convergence_rows
        if row.get("contrast") == "H3"
        and row.get("complete", "").lower() == "true"
    ]
    rows.sort(key=lambda row: int(row["cumulative_replications"]))
    if [int(row["cumulative_replications"]) for row in rows] != [25, 50, 100, 200]:
        raise ValueError("H3 convergence rows must be 25, 50, 100, and 200")
    parsed = [
        (
            int(row["cumulative_replications"]),
            finite(row["mean_difference"], "mean_difference"),
            finite(row["ci95_lower"], "ci95_lower"),
            finite(row["ci95_upper"], "ci95_upper"),
        )
        for row in rows
    ]
    y_values = [value for _, mean, lower, upper in parsed for value in (mean, lower, upper)]
    y_low, y_high = nice_bounds(y_values, include_zero=True)
    body = base_axes(
        "H3 cumulative-replication convergence",
        "Cumulative paired replications",
        "LinUCB − Random Safe mean pseudo-regret",
        25.0,
        200.0,
        y_low,
        y_high,
        x_ticks=7,
    )
    x0, x1 = MARGIN_LEFT, WIDTH - MARGIN_RIGHT
    y0, y1 = HEIGHT - MARGIN_BOTTOM, MARGIN_TOP
    zero_y = scale(0.0, y_low, y_high, y0, y1)
    body.append(
        f'<line x1="{x0}" y1="{zero_y:.2f}" x2="{x1}" '
        f'y2="{zero_y:.2f}" stroke="#111" stroke-dasharray="6 4"/>'
    )
    means = []
    for count, mean, lower, upper in parsed:
        x = scale(count, 25.0, 200.0, x0, x1)
        mean_y = scale(mean, y_low, y_high, y0, y1)
        lower_y = scale(lower, y_low, y_high, y0, y1)
        upper_y = scale(upper, y_low, y_high, y0, y1)
        means.append((x, mean_y))
        body.extend(
            [
                f'<line x1="{x:.2f}" y1="{lower_y:.2f}" '
                f'x2="{x:.2f}" y2="{upper_y:.2f}" '
                'stroke="#777" stroke-width="2"/>',
                f'<line x1="{x - 5:.2f}" y1="{lower_y:.2f}" '
                f'x2="{x + 5:.2f}" y2="{lower_y:.2f}" stroke="#777"/>',
                f'<line x1="{x - 5:.2f}" y1="{upper_y:.2f}" '
                f'x2="{x + 5:.2f}" y2="{upper_y:.2f}" stroke="#777"/>',
            ]
        )
    points = " ".join(f"{x:.2f},{y:.2f}" for x, y in means)
    body.append(
        f'<polyline fill="none" stroke="#54A24B" stroke-width="2.5" '
        f'points="{points}"/>'
    )
    for x, y in means:
        body.append(f'<circle cx="{x:.2f}" cy="{y:.2f}" r="5" fill="#54A24B"/>')
    return svg_document("H3 convergence", body)


def generate(
    summary_path: Path,
    inference_path: Path,
    convergence_path: Path,
    output_directory: Path,
) -> dict[str, object]:
    summary_rows = read_csv(summary_path)
    inference_rows = read_csv(inference_path)
    convergence_rows = read_csv(convergence_path)
    output_directory.mkdir(parents=True, exist_ok=True)
    figures = {
        "study-a-mean-regret-ecdf.svg": build_ecdf(summary_rows),
        "confirmatory-effects.svg": build_effects(inference_rows),
        "h3-convergence.svg": build_convergence(convergence_rows),
    }
    output_hashes = {}
    for name, content in figures.items():
        path = output_directory / name
        path.write_text(content, encoding="utf-8", newline="\n")
        output_hashes[name] = sha256(path)
    manifest = {
        "schema_version": "1.0.0",
        "protocol_id": PROTOCOL_ID,
        "sources_sha256": {
            str(summary_path): sha256(summary_path),
            str(inference_path): sha256(inference_path),
            str(convergence_path): sha256(convergence_path),
        },
        "figures_sha256": output_hashes,
    }
    manifest_path = output_directory / "figures-manifest.json"
    manifest_path.write_text(
        json.dumps(manifest, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    return manifest


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--summary", required=True, type=Path)
    parser.add_argument("--inference", required=True, type=Path)
    parser.add_argument("--convergence", required=True, type=Path)
    parser.add_argument("--output-dir", required=True, type=Path)
    arguments = parser.parse_args()
    manifest = generate(
        arguments.summary,
        arguments.inference,
        arguments.convergence,
        arguments.output_dir,
    )
    print(json.dumps(manifest, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
