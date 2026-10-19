#!/usr/bin/env python3
"""Produce descriptive tables and SVG figures for a validated v1.0 Study A run.

The frozen primary inference is read from analyze_longitudinal_v1.py. This
script adds descriptive reporting; it does not recalculate or change the
prespecified primary test. Only Python's standard library is required.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import html
import json
import math
import zipfile
from collections import Counter, defaultdict
from pathlib import Path

from analyze_longitudinal_v1 import analyze, sha256


ROOT = Path(__file__).resolve().parents[3]
REPORT_ROOT = ROOT / "analysis/component-c-bandit-evaluation/longitudinal/reports"
POLICIES = ("CarryLinUcb", "ResetLinUcb", "StaticPersonalized",
            "RuleBasedAdaptive")
COLORS = {"CarryLinUcb": "#2274A5", "ResetLinUcb": "#D95D39",
          "StaticPersonalized": "#777777", "RuleBasedAdaptive": "#42935C"}
SHORT = {"CarryLinUcb": "Carry", "ResetLinUcb": "Reset",
         "StaticPersonalized": "Static", "RuleBasedAdaptive": "Rule"}
PERIODS = {"early": range(1, 6), "middle": range(6, 16),
           "late": range(16, 21)}
RAW_FILES = ("manifest.json", "sessions.csv", "probes.csv", "decisions.csv",
             "rewards.csv", "physiology.csv")


def rows(path: Path):
    with path.open(newline="", encoding="utf-8-sig") as source:
        yield from csv.DictReader(source)


def mean(values):
    return sum(values) / len(values) if values else None


def sample_sd(values):
    if len(values) < 2:
        return 0.0 if values else None
    center = mean(values)
    return math.sqrt(sum((value - center) ** 2 for value in values)
                     / (len(values) - 1))


def fmt(value, places=4):
    return "NA" if value is None else f"{value:.{places}f}"


def write_csv(path: Path, fields, records):
    with path.open("w", newline="", encoding="utf-8") as target:
        writer = csv.DictWriter(target, fieldnames=fields)
        writer.writeheader()
        writer.writerows(records)


def read_probe_means(run: Path, expected_count: int):
    sums = defaultdict(float)
    counts = Counter()
    for row in rows(run / "probes.csv"):
        key = (row["scenario"], row["archetype"], int(row["profile_seed"]),
               row["condition"], int(row["checkpoint"]))
        sums[key] += float(row["pseudo_regret"])
        counts[key] += 1
    if any(count != expected_count for count in counts.values()):
        raise ValueError("Incomplete probe bank")
    return {key: total / counts[key] for key, total in sums.items()}


def probe_curve(probe_means, replications):
    grouped = defaultdict(list)
    primary_by_seed = defaultdict(list)
    for (scenario, archetype, seed, condition, checkpoint), value in probe_means.items():
        grouped[(scenario, archetype, condition, checkpoint)].append(value)
        if scenario == "Primary":
            primary_by_seed[(seed, condition, checkpoint)].append(value)
    for (_, condition, checkpoint), values in primary_by_seed.items():
        if len(values) != 7:
            raise ValueError("Primary pooled curve needs seven archetypes per seed")
        grouped[("Primary", "PooledEqualArchetype", condition, checkpoint)].append(
            mean(values))
    result = []
    for (scenario, archetype, condition, checkpoint), values in sorted(grouped.items()):
        if len(values) != replications:
            raise ValueError("Unexpected profile count in probe curve")
        result.append({"scenario": scenario, "archetype": archetype,
                       "condition": condition, "checkpoint": checkpoint,
                       "profiles": len(values), "mean_probe_regret": mean(values),
                       "sd_across_profiles": sample_sd(values)})
    return result


def endpoint_table(probe_means, analysis, seed_start, replications):
    result = []
    all_differences = []
    for index in range(1, 8):
        archetype = next(key[1] for key in probe_means
                         if key[0] == "Primary" and key[1].startswith(f"A{index}"))
        carry_before, reset_before, carry_after, reset_after, differences = (
            [] for _ in range(5))
        for seed in range(seed_start, seed_start + replications):
            prefix = ("Primary", archetype, seed)
            carry_before.append(probe_means[(*prefix, "CarryLinUcb", 0)])
            reset_before.append(probe_means[(*prefix, "ResetLinUcb", 0)])
            carry_after.append(probe_means[(*prefix, "CarryLinUcb", 20)])
            reset_after.append(probe_means[(*prefix, "ResetLinUcb", 20)])
            differences.append(carry_after[-1] - reset_after[-1])
        all_differences.extend(differences)
        result.append({"archetype": archetype, "profiles": replications,
                       "carry_baseline": mean(carry_before),
                       "reset_baseline": mean(reset_before),
                       "carry_after_20": mean(carry_after),
                       "reset_after_20": mean(reset_after),
                       "mean_carry_minus_reset": mean(differences),
                       "profiles_better_carry": sum(value < 0 for value in differences)})
    primary = analysis["primary_confirmatory_analysis"]
    if not math.isclose(mean(all_differences),
                        primary["mean_carry_minus_reset_probe_regret"],
                        abs_tol=1e-12):
        raise ValueError("Descriptive endpoint disagrees with frozen primary analysis")
    return result


def read_sessions(run: Path):
    result = []
    cumulative_valid = defaultdict(int)
    previous_session = defaultdict(int)
    for row in rows(run / "sessions.csv"):
        key = (row["scenario"], row["archetype"], int(row["profile_seed"]),
               row["condition"])
        session_index = int(row["session_index"])
        if session_index != previous_session[key] + 1:
            raise ValueError(f"Session rows out of trajectory order: {key}")
        previous_session[key] = session_index
        count = int(row["valid_outcomes"])
        cumulative_valid[key] += count
        result.append({"scenario": key[0], "archetype": key[1],
                       "seed": key[2], "condition": key[3],
                       "session": session_index,
                       "baseline_available": row["baseline_available"].lower() == "true",
                       "eligible": int(row["eligible_decisions"]),
                       "regret_total": float(row["cumulative_regret"]),
                       "mean_regret": float(row["mean_regret"])
                       if int(row["eligible_decisions"]) else None,
                       "reward_total": float(row["cumulative_reward"]),
                       "stress_change": float(row["synthetic_stress_change"])
                       if row["synthetic_stress_change"] else None,
                       "executed_changes": int(row["executed_changes"]),
                       "valid_outcomes": count,
                       "cumulative_valid_outcomes": cumulative_valid[key],
                       "model_updates": int(row["session_model_updates"]),
                       "no_change": int(row["no_change"]),
                       "variation": float(row["total_variation"]),
                       "scene_violations": int(row["scene_violations"]),
                       "multidimension_violations": int(row["multidimension_violations"]),
                       "invalid_model_updates": int(row["invalid_model_updates"])})
    return result


def make_session_curve(sessions, replications):
    grouped = defaultdict(list)
    primary_by_seed = defaultdict(list)
    for row in sessions:
        key = (row["scenario"], row["archetype"], row["condition"], row["session"])
        grouped[key].append(row)
        if row["scenario"] == "Primary":
            primary_by_seed[(row["seed"], row["condition"], row["session"])].append(row)
    output = []
    for key, values in sorted(grouped.items()):
        if len(values) != replications:
            raise ValueError("Unexpected profile count in session curve")
        available = [row["mean_regret"] for row in values if row["mean_regret"] is not None]
        output.append({"scenario": key[0], "archetype": key[1],
                       "condition": key[2], "session": key[3],
                       "profiles": len(values), "profiles_with_eligible_decisions": len(available),
                       "mean_trajectory_regret_available": mean(available),
                       "mean_eligible_decisions": mean([row["eligible"] for row in values]),
                       "mean_valid_outcomes": mean([row["valid_outcomes"] for row in values]),
                       "mean_cumulative_valid_outcomes": mean([
                           row["cumulative_valid_outcomes"] for row in values])})
    for (seed, condition, session), values in primary_by_seed.items():
        if len(values) != 7:
            raise ValueError("Primary pooled session curve needs seven archetypes")
        grouped[("Primary", "PooledEqualArchetype", condition, session)].append({
            "eligible": mean([row["eligible"] for row in values]),
            "valid_outcomes": mean([row["valid_outcomes"] for row in values]),
            "cumulative_valid_outcomes": mean([
                row["cumulative_valid_outcomes"] for row in values]),
            "mean_regret": mean([row["mean_regret"] for row in values
                                 if row["mean_regret"] is not None])})
    for key, values in sorted(grouped.items()):
        if key[1] != "PooledEqualArchetype":
            continue
        available = [row["mean_regret"] for row in values if row["mean_regret"] is not None]
        output.append({"scenario": key[0], "archetype": key[1],
                       "condition": key[2], "session": key[3],
                       "profiles": len(values), "profiles_with_eligible_decisions": len(available),
                       "mean_trajectory_regret_available": mean(available),
                       "mean_eligible_decisions": mean([row["eligible"] for row in values]),
                       "mean_valid_outcomes": mean([row["valid_outcomes"] for row in values]),
                       "mean_cumulative_valid_outcomes": mean([
                           row["cumulative_valid_outcomes"] for row in values])})
    return output


def period_and_safety(sessions, run: Path):
    periods = defaultdict(list)
    safety = defaultdict(Counter)
    profile_sets = defaultdict(set)
    for row in sessions:
        key = (row["scenario"], row["condition"])
        profile_sets[key].add((row["archetype"], row["seed"]))
        for field in ("eligible", "valid_outcomes", "model_updates", "no_change",
                      "executed_changes", "scene_violations", "multidimension_violations",
                      "invalid_model_updates"):
            safety[key][field] += row[field]
        safety[key]["sessions"] += 1
        safety[key]["zero_eligible_sessions"] += row["eligible"] == 0
        safety[key]["baseline_unavailable_sessions"] += not row["baseline_available"]
        safety[key]["stress_change_unavailable_sessions"] += row["stress_change"] is None
        safety[key]["total_variation"] += row["variation"]
        for name, indices in PERIODS.items():
            if row["session"] in indices:
                periods[(row["scenario"], row["condition"], name)].append(row)
                break
    for row in rows(run / "decisions.csv"):
        safety[(row["scenario"], row["condition"])]["decision_" + row["result"]] += 1
    for row in rows(run / "rewards.csv"):
        safety[(row["scenario"], row["condition"])]["reward_" + row["result"]] += 1
    for row in rows(run / "physiology.csv"):
        if row["dropped"].lower() == "true":
            safety[(row["scenario"], row["condition"])]["dropped_physiology"] += 1
    period_rows = []
    for key, values in sorted(periods.items()):
        total_eligible = sum(row["eligible"] for row in values)
        total_valid = sum(row["valid_outcomes"] for row in values)
        period_rows.append({"scenario": key[0], "condition": key[1], "period": key[2],
                            "profiles": len({(row["archetype"], row["seed"]) for row in values}),
                            "sessions": len(values),
                            "zero_eligible_sessions": sum(row["eligible"] == 0 for row in values),
                            "eligible_decisions": total_eligible,
                            "valid_outcomes": total_valid,
                            "model_updates": sum(row["model_updates"] for row in values),
                            "no_change": sum(row["no_change"] for row in values),
                            "trajectory_regret_per_eligible_decision": (
                                sum(row["regret_total"] for row in values) / total_eligible
                                if total_eligible else None),
                            "production_reward_per_valid_outcome": (
                                sum(row["reward_total"] for row in values) / total_valid
                                if total_valid else None),
                            "stress_change_available_sessions": sum(
                                row["stress_change"] is not None for row in values),
                            "mean_synthetic_stress_change_per_session": mean([
                                row["stress_change"] for row in values
                                if row["stress_change"] is not None]),
                            "mean_normalized_variation_per_session": mean([
                                row["variation"] for row in values])})
    safety_rows = []
    for key, counter in sorted(safety.items()):
        safety_rows.append({"scenario": key[0], "condition": key[1],
                            "profiles": len(profile_sets[key]), **counter})
    return period_rows, safety_rows


def physiology_summary(run: Path):
    session_states = {}
    for row in rows(run / "physiology.csv"):
        if row["ingestion_result"] != "Accepted":
            continue
        key = (row["scenario"], row["archetype"], int(row["profile_seed"]),
               row["condition"], int(row["session_index"]))
        t = float(row["event_time_seconds"])
        metrics = tuple(float(row[field]) for field in
                        ("stress_score", "heart_rate_bpm", "rmssd_ms"))
        state = session_states.get(key)
        if state is None:
            session_states[key] = {"first": (t, metrics), "last": (t, metrics),
                                   "auc": 0.0, "windows": 1}
        else:
            previous_t, previous_metrics = state["last"]
            if t <= previous_t:
                raise ValueError("Physiology events are not strictly ordered")
            state["auc"] += (t - previous_t) * (metrics[0] + previous_metrics[0]) / 2
            state["last"] = (t, metrics)
            state["windows"] += 1
    grouped = defaultdict(list)
    for (scenario, _, _, condition, _), state in session_states.items():
        first_t, first = state["first"]
        last_t, last = state["last"]
        grouped[(scenario, condition)].append({
            "windows": state["windows"], "duration": last_t - first_t,
            "stress_change": last[0] - first[0],
            "heart_rate_change_bpm": last[1] - first[1],
            "rmssd_change_ms": last[2] - first[2],
            "observed_stress_auc": state["auc"]})
    output = []
    for (scenario, condition), values in sorted(grouped.items()):
        usable = [value for value in values if value["windows"] >= 2]
        output.append({"scenario": scenario, "condition": condition,
                       "sessions_with_accepted_windows": len(values),
                       "sessions_with_two_or_more_windows": len(usable),
                       "mean_observed_duration_seconds": mean([
                           value["duration"] for value in usable]),
                       "mean_synthetic_stress_change": mean([
                           value["stress_change"] for value in usable]),
                       "mean_heart_rate_change_bpm": mean([
                           value["heart_rate_change_bpm"] for value in usable]),
                       "mean_rmssd_change_ms": mean([
                           value["rmssd_change_ms"] for value in usable]),
                       "mean_observed_stress_auc_score_seconds": mean([
                           value["observed_stress_auc"] for value in usable])})
    return output


def line_figure(path: Path, title: str, y_label: str, series, *,
                drift_marker=False, x_label="Session checkpoint"):
    """Write a standalone SVG; shaded bands are descriptive ±1 profile SD."""
    width, height = 980, 590
    left, right, top, bottom = 92, 42, 78, 80
    x0, x1 = left, width - right
    y0, y1 = height - bottom, top
    all_points = [point for points in series.values() for point in points]
    if not all_points:
        raise ValueError("Figure has no data")
    maximum = max(value + (sd or 0) for points in series.values()
                  for _, value, sd in points)
    y_max = max(.05, maximum * 1.12)
    max_x = max(x for x, _, _ in all_points)

    def sx(value):
        return x0 + (x1 - x0) * value / max_x if max_x else x0

    def sy(value):
        return y0 - (y0 - y1) * value / y_max

    body = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" '
            f'height="{height}" viewBox="0 0 {width} {height}" role="img">',
            f'<title>{html.escape(title)}</title>',
            '<style>text{font-family:Arial,sans-serif;fill:#202124}'
            '.title{font-size:19px;font-weight:bold}'
            '.axis{font-size:14px}.tick{font-size:12px}'
            '.note{font-size:11px;fill:#555}</style>',
            f'<text class="title" x="{width/2}" y="33" text-anchor="middle">'
            f'{html.escape(title)}</text>']
    for index in range(6):
        value = y_max * index / 5
        y = sy(value)
        body.extend([
            f'<line x1="{x0}" y1="{y:.2f}" x2="{x1}" y2="{y:.2f}" '
            'stroke="#dfe3e8"/>',
            f'<text class="tick" x="{x0-10}" y="{y+4:.2f}" '
            f'text-anchor="end">{value:.2f}</text>'])
    for index in range(5):
        value = max_x * index / 4
        x = sx(value)
        body.extend([
            f'<line x1="{x:.2f}" y1="{y0}" x2="{x:.2f}" y2="{y1}" '
            'stroke="#eef0f2"/>',
            f'<text class="tick" x="{x:.2f}" y="{y0+22}" '
            f'text-anchor="middle">{value:.0f}</text>'])
    body.extend([
        f'<line x1="{x0}" y1="{y0}" x2="{x1}" y2="{y0}" stroke="#202124"/>',
        f'<line x1="{x0}" y1="{y0}" x2="{x0}" y2="{y1}" stroke="#202124"/>'])
    if drift_marker:
        x = sx(10.5)
        body.extend([
            f'<line x1="{x:.2f}" y1="{y0}" x2="{x:.2f}" y2="{y1}" '
            'stroke="#555" stroke-dasharray="5,4"/>',
            f'<text class="tick" x="{x+7:.2f}" y="{y1+14}">'
            'response switch: session 11</text>'])
    for label, points in series.items():
        color = COLORS.get(label, "#555555")
        ordered = sorted(points)
        if any(sd is not None and sd > 0 for _, _, sd in ordered):
            upper = [(sx(x), sy(value + (sd or 0))) for x, value, sd in ordered]
            lower = [(sx(x), sy(max(0.0, value - (sd or 0))))
                     for x, value, sd in reversed(ordered)]
            vertices = " ".join(f"{x:.2f},{y:.2f}" for x, y in upper + lower)
            body.append(f'<polygon points="{vertices}" fill="{color}" '
                        'fill-opacity="0.11" stroke="none"/>')
        vertices = " ".join(f"{sx(x):.2f},{sy(value):.2f}"
                            for x, value, _ in ordered)
        body.append(f'<polyline points="{vertices}" fill="none" '
                    f'stroke="{color}" stroke-width="2.6"/>')
    for index, label in enumerate(series):
        x = x0 + 10 + index * 185
        body.extend([
            f'<line x1="{x}" y1="{top-20}" x2="{x+28}" y2="{top-20}" '
            f'stroke="{COLORS.get(label, "#555555")}" stroke-width="3"/>',
            f'<text class="tick" x="{x+35}" y="{top-16}">'
            f'{html.escape(SHORT.get(label, label))}</text>'])
    note = ("Bands: ±1 SD across synthetic profiles (descriptive, not confidence intervals)."
            if any(sd is not None and sd > 0 for _, _, sd in all_points)
            else "Lines are descriptive means across synthetic profiles.")
    body.extend([
        f'<text class="axis" x="{(x0+x1)/2}" y="{height-34}" '
        f'text-anchor="middle">{html.escape(x_label)}</text>',
        f'<text class="axis" transform="translate(24 {(y0+y1)/2}) '
        f'rotate(-90)" text-anchor="middle">{html.escape(y_label)}</text>',
        f'<text class="note" x="{x0}" y="{height-12}">'
        f'{html.escape(note)}</text>', "</svg>", ""])
    path.write_text("\n".join(body), encoding="utf-8")


def ecdf_figure(path: Path, values):
    ordered = sorted(values)
    width, height = 800, 480
    x0, x1, y0, y1 = 90, 755, 410, 55
    low = min(ordered[0], 0.0) * 1.1
    high = max(ordered[-1], 0.0) + max(.02, (ordered[-1] - ordered[0]) * .1)
    if high <= low:
        high = low + 1

    def sx(value):
        return x0 + (x1 - x0) * (value - low) / (high - low)

    def sy(value):
        return y0 - (y0 - y1) * value

    points = [(sx(low), sy(0.0))]
    for index, value in enumerate(ordered, 1):
        points.extend([(sx(value), sy((index - 1) / len(ordered))),
                       (sx(value), sy(index / len(ordered)))])
    vertices = " ".join(f"{x:.2f},{y:.2f}" for x, y in points)
    body = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" '
            f'height="{height}" viewBox="0 0 {width} {height}" role="img">',
            '<title>SIMULATED paired seed-index effect ECDF</title>',
            '<style>text{font-family:Arial,sans-serif;fill:#202124}</style>',
            f'<text x="{width/2}" y="29" text-anchor="middle" '
            'font-size="19" font-weight="bold">'
            'SIMULATED: Carry − Reset paired effect distribution</text>',
            f'<line x1="{x0}" y1="{y0}" x2="{x1}" y2="{y0}" stroke="#202124"/>',
            f'<line x1="{x0}" y1="{y0}" x2="{x0}" y2="{y1}" stroke="#202124"/>',
            f'<line x1="{sx(0):.2f}" y1="{y0}" x2="{sx(0):.2f}" '
            f'y2="{y1}" stroke="#555" stroke-dasharray="5,4"/>',
            f'<polyline points="{vertices}" fill="none" stroke="#2274A5" '
            'stroke-width="2.8"/>']
    for index in range(5):
        value = low + (high - low) * index / 4
        body.append(f'<text x="{sx(value):.2f}" y="{y0+21}" text-anchor="middle" '
                    f'font-size="12">{value:.2f}</text>')
        body.append(f'<text x="{x0-12}" y="{sy(index/4)+4:.2f}" '
                    f'text-anchor="end" font-size="12">{index/4:.2f}</text>')
    body.extend([f'<text x="{(x0+x1)/2}" y="{height-21}" text-anchor="middle" '
                 'font-size="14">Carry − Reset probe pseudo-regret after session 20 '
                 '(lower favors Carry)</text>',
                 f'<text transform="translate(23 {(y0+y1)/2}) rotate(-90)" '
                 'text-anchor="middle" font-size="14">Empirical cumulative fraction</text>',
                 f'<text x="{x0}" y="{height-5}" font-size="11">'
                 '30 equally archetype-weighted synthetic seed-index contrasts</text>',
                 "</svg>", ""])
    path.write_text("\n".join(body), encoding="utf-8")


def figures(output: Path, curve, sessions_curve, paired_values):
    directory = output / "figures"
    directory.mkdir(parents=True, exist_ok=True)

    def probe_series(scenario, archetype, conditions):
        return {condition: [(int(row["checkpoint"]), row["mean_probe_regret"],
                             row["sd_across_profiles"])
                            for row in curve if row["scenario"] == scenario
                            and row["archetype"] == archetype
                            and row["condition"] == condition]
                for condition in conditions}

    line_figure(directory / "primary-probe-learning.svg",
                "SIMULATED: equal-archetype mean held-out probe regret",
                "Probe pseudo-regret (lower is better)",
                probe_series("Primary", "PooledEqualArchetype", POLICIES))
    for index in range(1, 8):
        archetype = next(row["archetype"] for row in curve
                         if row["scenario"] == "Primary"
                         and row["archetype"].startswith(f"A{index}"))
        line_figure(directory / f"primary-{index:02d}-probe-learning.svg",
                    f"SIMULATED: A{index} held-out probe regret",
                    "Probe pseudo-regret (lower is better)",
                    probe_series("Primary", archetype,
                                 ("CarryLinUcb", "ResetLinUcb")))
    for scenario, filename, title, marker in (
        ("BetweenSessionDrift", "drift-probe-learning.svg",
         "SIMULATED: between-session drift challenge", True),
        ("MildPhysiologyLoss", "loss-probe-learning.svg",
         "SIMULATED: 10% physiology loss challenge", False),
        ("NonResponder", "nonresponder-probe-learning.svg",
         "SIMULATED: A9 nonresponder negative control", False),
    ):
        archetype = next(row["archetype"] for row in curve
                         if row["scenario"] == scenario)
        line_figure(directory / filename, title,
                    "Probe pseudo-regret (lower is better)",
                    probe_series(scenario, archetype,
                                 ("CarryLinUcb", "ResetLinUcb")),
                    drift_marker=marker)
    update_series = {
        condition: [(int(row["session"]),
                     row["mean_cumulative_valid_outcomes"], None)
                    for row in sessions_curve
                    if row["scenario"] == "Primary"
                    and row["archetype"] == "PooledEqualArchetype"
                    and row["condition"] == condition]
        for condition in ("CarryLinUcb", "ResetLinUcb")}
    line_figure(directory / "primary-feedback-exposure.svg",
                "SIMULATED: cumulative valid feedback exposure",
                "Valid outcomes observed across sessions",
                update_series, x_label="Completed session")
    ecdf_figure(directory / "primary-paired-effect-ecdf.svg", paired_values)
    return directory


def report_markdown(run: Path, manifest, analysis, endpoint, curve,
                    periods, safety, physiology):
    primary = analysis["primary_confirmatory_analysis"]
    pooled = {(row["condition"], row["checkpoint"]): row["mean_probe_regret"]
              for row in curve if row["scenario"] == "Primary"
              and row["archetype"] == "PooledEqualArchetype"}
    challenge = {(row["scenario"], row["condition"], row["checkpoint"]): row
                 for row in curve if row["scenario"] != "Primary"}
    safety_totals = Counter()
    for row in safety:
        for name, value in row.items():
            if name not in ("scenario", "condition", "profiles"):
                safety_totals[name] += value
    lines = [
        "# Longitudinal Study A — simulated results, frozen v1.0",
        "",
        f"- **Run:** `{manifest['run_id']}`",
        f"- **Protocol:** `{manifest['protocol_id']}`; investigator freeze date 2026-10-19",
        f"- **Source commit:** `{manifest['git_commit']}`; dirty at run: "
        f"`{str(manifest['git_dirty']).lower()}`",
        f"- **Unity:** `{manifest['unity_version']}`; visual bundle "
        f"`{manifest['pilot_bundle_id']}`; mapping `{manifest['mapping_configuration_id']}`",
        f"- **Validation:** `{analysis['validation']}`; complete frozen matrix "
        f"`{str(analysis['full_frozen_matrix']).lower()}`",
        "",
        "## Primary decision-quality result",
        "",
        "The prespecified contrast is Carry minus Reset mean safe-oracle probe "
        "pseudo-regret after session 20, equally weighted across A1–A7. "
        "Negative values favor Carry. Synthetic profiles, not people, are the "
        "replication units.",
        "",
        f"- Paired mean: **{fmt(primary['mean_carry_minus_reset_probe_regret'])}**; "
        f"median {fmt(primary['median_carry_minus_reset_probe_regret'])}; "
        f"seed-index SD {fmt(primary['sample_sd_seed_index_differences'])}.",
        f"- 95% paired bootstrap interval: "
        f"**[{fmt(primary['bootstrap_95_percentile_interval'][0])}, "
        f"{fmt(primary['bootstrap_95_percentile_interval'][1])}]**; "
        f"two-sided 100,000-draw sign-flip p = "
        f"**{primary['two_sided_sign_flip_p']:.7g}**.",
        f"- Directional criterion met: "
        f"**{str(primary['primary_directional_criterion_met']).lower()}**. "
        f"Carry was better for {primary['percent_seed_indices_better_carry']:.1f}% "
        f"of the 30 equally weighted seed-index contrasts and "
        f"{primary['percent_profiles_better_carry']:.1f}% of the 210 paired "
        "archetype profiles.",
        f"- Mean baseline probe regret: Carry "
        f"{fmt(pooled[('CarryLinUcb', 0)])}, Reset "
        f"{fmt(pooled[('ResetLinUcb', 0)])}; after session 20: Carry "
        f"{fmt(pooled[('CarryLinUcb', 20)])}, Reset "
        f"{fmt(pooled[('ResetLinUcb', 20)])}.",
        "",
        "The Monte Carlo p-value is at the 100,000-draw add-one resolution; "
        "it is not an exact zero-probability result. The bootstrap interval "
        "describes variation across synthetic seed indexes under this simulator.",
        "",
        "| Archetype | Carry baseline | Reset baseline | Carry after 20 | "
        "Reset after 20 | Carry − Reset | Profiles favoring Carry |",
        "|---|---:|---:|---:|---:|---:|---:|",
    ]
    for row in endpoint:
        lines.append(f"| {row['archetype']} | {fmt(row['carry_baseline'])} | "
                     f"{fmt(row['reset_baseline'])} | {fmt(row['carry_after_20'])} | "
                     f"{fmt(row['reset_after_20'])} | "
                     f"{fmt(row['mean_carry_minus_reset'])} | "
                     f"{row['profiles_better_carry']}/{row['profiles']} |")
    lines += [
        "",
        "![Simulated primary probe learning curve](figures/primary-probe-learning.svg)",
        "",
        "*Figure 1. Simulated held-out probe pseudo-regret by session checkpoint. "
        "The pooled curve weights A1–A7 equally. Bands are ±1 SD across "
        "synthetic seed-index aggregates, not confidence intervals.*",
        "",
        "![Simulated paired effect ECDF](figures/primary-paired-effect-ecdf.svg)",
        "",
        "*Figure 2. Distribution of the 30 equally archetype-weighted "
        "paired seed-index contrasts after session 20.*",
        "",
        "## Longitudinal and challenge results (descriptive)",
        "",
        "The seven individual A1–A7 learning curves and their between-profile "
        "SD bands are in `figures/primary-01-probe-learning.svg` through "
        "`primary-07-probe-learning.svg`. A5 has the largest mean advantage; "
        "A7 has the smallest. This heterogeneity is retained in the table "
        "rather than hidden by the pooled estimate.",
        "",
        "| Challenge | Checkpoint | Carry probe regret | Reset probe regret | "
        "Carry − Reset |",
        "|---|---:|---:|---:|---:|",
    ]
    for scenario in ("BetweenSessionDrift", "MildPhysiologyLoss", "NonResponder"):
        for checkpoint in (0, 10, 11, 12, 20):
            carry = challenge[(scenario, "CarryLinUcb", checkpoint)]["mean_probe_regret"]
            reset = challenge[(scenario, "ResetLinUcb", checkpoint)]["mean_probe_regret"]
            lines.append(f"| {scenario} | {checkpoint} | {fmt(carry)} | "
                         f"{fmt(reset)} | {fmt(carry-reset)} |")
    lines += [
        "",
        "For the drift challenge, the response optimum switches at session 11. "
        "A9 is a negative control with zero modeled environment effect, so its "
        "safe-oracle probe regret is zero for both policies by construction. "
        "Neither challenge is an additional confirmatory test.",
        "",
        "![Simulated drift challenge](figures/drift-probe-learning.svg)",
        "",
        "*Figure 3. Simulated drift challenge with the session-11 response "
        "switch marked; bands show ±1 SD across synthetic profiles.*",
        "",
        "![Simulated physiology-loss challenge](figures/loss-probe-learning.svg)",
        "",
        "*Figure 4. Simulated 10% physiology-emission loss challenge; bands "
        "show ±1 SD across synthetic profiles.*",
        "",
        "The full primary policy comparison by early (sessions 1–5), middle "
        "(6–15), and late (16–20) periods follows. Trajectory regret uses "
        "eligible decisions as denominator, and availability is shown "
        "separately. These are descriptive totals across synthetic profiles, "
        "not independent-session tests.",
        "",
        "| Period | Policy | Eligible decisions | Valid outcomes | "
        "Trajectory regret / eligible decision | Stress-change available "
        "sessions | Mean synthetic stress change / available session |",
        "|---|---|---:|---:|---:|---:|---:|",
    ]
    for period in ("early", "middle", "late"):
        for condition in POLICIES:
            row = next(value for value in periods if value["scenario"] == "Primary"
                       and value["condition"] == condition and value["period"] == period)
            lines.append(f"| {period} | {SHORT[condition]} | "
                         f"{row['eligible_decisions']} | {row['valid_outcomes']} | "
                         f"{fmt(row['trajectory_regret_per_eligible_decision'])} | "
                         f"{row['stress_change_available_sessions']} | "
                         f"{fmt(row['mean_synthetic_stress_change_per_session'])} |")
    lines += [
        "",
        "![Simulated cumulative valid feedback exposure](figures/primary-feedback-exposure.svg)",
        "",
        "*Figure 5. Mean cumulative valid feedback across primary synthetic "
        "profiles. Reset observes feedback but starts each session with a "
        "fresh LinUCB model.*",
        "",
        "## Safety, attribution, and availability",
        "",
        f"Across {manifest['session_rows']:,} simulated sessions, the recorded "
        f"scene-limit violations, multi-dimension violations, and invalid "
        f"model updates were {int(safety_totals['scene_violations'])}, "
        f"{int(safety_totals['multidimension_violations'])}, and "
        f"{int(safety_totals['invalid_model_updates'])}. "
        f"The safety layer rejected {int(safety_totals['decision_SafetyRejected']):,} "
        f"proposals, while {int(safety_totals['decision_TransitionStarted']):,} "
        "visual transitions started. Rejections are expected safety actions, "
        "not executed violations.",
        "",
        f"Recorded reward events: "
        f"{int(safety_totals['reward_RewardApplied']):,} applied, "
        f"{int(safety_totals['reward_AttributionInvalidated']):,} invalidated. "
        f"There were {int(safety_totals['zero_eligible_sessions']):,} "
        f"zero-eligible sessions, {int(safety_totals['baseline_unavailable_sessions']):,} "
        f"baseline-unavailable sessions, and "
        f"{int(safety_totals['dropped_physiology']):,} dropped synthetic "
        "physiology emissions. Complete counts by scenario and condition are "
        "in `safety-summary.csv`.",
        "",
        "Accepted-window synthetic stress, heart-rate, and RMSSD changes and "
        "observed-window stress AUC are in `physiology-summary.csv`. The AUC "
        "uses trapezoids between accepted samples only and is not a complete "
        "continuous-physiology integral when emissions are missing.",
        "",
        "## Interpretation and reporting limits",
        "",
        "This result supports learning of the declared synthetic response laws "
        "by evaluation-only cross-session LinUCB carry-over. It does not "
        "establish stress reduction, safety, usability, or comfort in people. "
        "The deployed pilot still resets LinUCB each session. Its feature "
        "schema has no explicit session-context features. Study B human "
        "outcomes must be reported separately.",
        "",
        "The raw CSVs do not separately report final environment distance to "
        "the simulator optimum, snapshot-restore failure counts, reused "
        "physiology-window counts, or pause/network boundary violations. "
        "A completed trajectory and analyzer continuity check do not replace "
        "those direct measures. Do not report them as zero. This is a "
        "reporting gap against the frozen secondary-outcome checklist.",
        "",
        "The optional three-checkpoint practical-margin endpoint was omitted "
        "before the full run. No additional inferential test was added after "
        "inspecting the full-study output. Supervisor/team sign-off remains "
        "unrecorded in the repository.",
        "",
        "## Reproducibility files",
        "",
        "`primary-archetype.csv`, `probe-curve.csv`, `session-curve.csv`, "
        "`period-summary.csv`, `safety-summary.csv`, and "
        "`physiology-summary.csv` contain the descriptive values. "
        "`report-manifest.json` records the raw source and generated-file "
        "checksums. All charts are explicitly labelled simulated SVGs. "
        f"The raw run folder is `{run.as_posix()}`.",
        "",
    ]
    return "\n".join(lines)


def write_archive(run: Path, output: Path):
    archives = REPORT_ROOT.parent / "archives"
    archives.mkdir(parents=True, exist_ok=True)
    archive = archives / f"{run.name}-study-a-v1.0.zip"
    source_paths = (
        "analysis/component-c-bandit-evaluation/configs/longitudinal-study-v1.0.json",
        "analysis/component-c-bandit-evaluation/configs/longitudinal-visual-v1.0.json",
        "analysis/component-c-bandit-evaluation/scripts/analyze_longitudinal_v1.py",
        "analysis/component-c-bandit-evaluation/scripts/report_longitudinal_v1.py",
        "docs/research/LAMINAR_VR_LONGITUDINAL_SIMULATION_PROTOCOL_v1.0.md",
        "docs/research/LAMINAR_VR_LONGITUDINAL_STUDY_A_FREEZE_RECORD_v1.0.md",
        "unity/Assets/Laminar VR/Configuration/Environment/TemplePondSceneParameterProfile_VisualV3.asset",
        "unity/Assets/Laminar VR/Configuration/Environment/TemplePondEnvironmentMappingProfile_v4.asset",
    )
    with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED,
                         compresslevel=6, allowZip64=True) as target:
        for path in sorted(run.rglob("*")):
            if path.is_file():
                target.write(path, "raw/" + path.relative_to(run).as_posix())
        for path in sorted(output.rglob("*")):
            if path.is_file():
                target.write(path, "report/" + path.relative_to(output).as_posix())
        for name in source_paths:
            target.write(ROOT / name, "source/" + name)
    with zipfile.ZipFile(archive) as check:
        corrupt = check.testzip()
    if corrupt:
        raise ValueError(f"Archive CRC check failed: {corrupt}")
    digest = sha256(archive)
    checksum = archive.with_suffix(archive.suffix + ".sha256")
    checksum.write_text(f"{digest}  {archive.name}\n", encoding="utf-8")
    return archive, digest


def report(run: Path, output: Path, archive=False):
    run = run.resolve()
    output = output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    _, analysis = analyze(run)
    if analysis["validation"] != "passed" or not analysis["full_frozen_matrix"]:
        raise ValueError("Reporting requires a validated full frozen v1.0 run")
    manifest = json.loads((run / "manifest.json").read_text(encoding="utf-8-sig"))
    config = json.loads((ROOT / "analysis/component-c-bandit-evaluation/configs/longitudinal-study-v1.0.json")
                        .read_text(encoding="utf-8"))
    probe_means = read_probe_means(run, config["probes_per_checkpoint"])
    curve = probe_curve(probe_means, config["replications_per_archetype"])
    endpoint = endpoint_table(probe_means, analysis,
                              config["primary_profile_seed_start"],
                              config["replications_per_archetype"])
    sessions = read_sessions(run)
    session_curve = make_session_curve(sessions, config["replications_per_archetype"])
    periods, safety = period_and_safety(sessions, run)
    physiology = physiology_summary(run)
    paired_values = [float(row["mean_carry_minus_reset_probe_regret"])
                     for row in rows(run / "paired-index-effects.csv")]
    if len(paired_values) != config["replications_per_archetype"]:
        raise ValueError("Paired seed-index effects are incomplete")
    write_csv(output / "primary-archetype.csv", list(endpoint[0]), endpoint)
    write_csv(output / "probe-curve.csv", list(curve[0]), curve)
    write_csv(output / "session-curve.csv", list(session_curve[0]), session_curve)
    write_csv(output / "period-summary.csv", list(periods[0]), periods)
    safety_fields = ["scenario", "condition", "profiles"] + sorted({
        field for row in safety for field in row
        if field not in ("scenario", "condition", "profiles")})
    for row in safety:
        for field in safety_fields[3:]:
            row.setdefault(field, 0)
    write_csv(output / "safety-summary.csv", safety_fields, safety)
    write_csv(output / "physiology-summary.csv", list(physiology[0]), physiology)
    figures(output, curve, session_curve, paired_values)
    (output / "report.md").write_text(
        report_markdown(run, manifest, analysis, endpoint, curve,
                        periods, safety, physiology), encoding="utf-8")
    source_hashes = {name: sha256(run / name) for name in RAW_FILES}
    source_hashes["analysis.json"] = sha256(run / "analysis.json")
    source_hashes["paired-index-effects.csv"] = sha256(
        run / "paired-index-effects.csv")
    outputs = {path.relative_to(output).as_posix(): sha256(path)
               for path in sorted(output.rglob("*")) if path.is_file()
               and path.name != "report-manifest.json"}
    report_manifest = {
        "run_id": manifest["run_id"], "protocol_id": manifest["protocol_id"],
        "source_git_commit": manifest["git_commit"],
        "source_git_dirty": manifest["git_dirty"],
        "report_script_sha256": sha256(Path(__file__).resolve()),
        "run_source_sha256": source_hashes,
        "report_output_sha256": outputs,
        "primary_analysis_unchanged": True,
    }
    (output / "report-manifest.json").write_text(
        json.dumps(report_manifest, indent=2, sort_keys=True) + "\n",
        encoding="utf-8")
    archive_result = write_archive(run, output) if archive else None
    return output, archive_result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("run_directory", type=Path)
    parser.add_argument("--output-dir", type=Path)
    parser.add_argument("--archive", action="store_true",
                        help="Create and CRC-check a local ZIP of raw, report, and source files")
    args = parser.parse_args()
    output = args.output_dir or REPORT_ROOT / args.run_directory.name
    path, archive_result = report(args.run_directory, output, args.archive)
    print(f"Wrote Study A results package: {path}")
    if archive_result:
        archive, digest = archive_result
        print(f"Archived raw run, report, and source: {archive}")
        print(f"Archive SHA-256: {digest}")


if __name__ == "__main__":
    main()
