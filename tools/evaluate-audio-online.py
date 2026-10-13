"""Compare complete audio controller conditions from version-2 Unity JSONL logs.

This summarizes recorded outcomes. It does not establish causal relaxation efficacy.
Do not mix simulated outcomes with physiology outcomes in a research comparison.
"""
from __future__ import annotations

import argparse
import csv
import json
import math
import statistics
from collections import defaultdict
from pathlib import Path


MODES = {"StaticPersonalized", "RuleBasedAdaptive", "PersonalizedBandit"}
ACTION_FIELDS = (
    "deltaIntensity", "deltaDensity", "deltaBrightness", "deltaTempo",
    "deltaFade", "deltaMusicMix", "deltaAmbientMix",
)


def mean(values):
    return statistics.fmean(values) if values else None


def load_records(paths, include_simulation=False):
    records, seen, rejected = [], set(), 0
    for path in paths:
        for line_number, line in enumerate(path.read_text(encoding="utf-8-sig").splitlines(), 1):
            if not line.strip():
                continue
            try:
                row = json.loads(line)
                if not isinstance(row, dict):
                    raise ValueError("Expected a JSON object")
                if row.get("schemaVersion") != 2 or row.get("eventType") != "outcome":
                    continue
                if row.get("policyMode") not in MODES:
                    raise ValueError("Unknown controller condition")
                if any(not isinstance(row.get(key), str) or not row[key] for key in ("userId", "sessionId", "decisionId")):
                    raise ValueError("Missing participant/session/decision identity")
                if not isinstance(row.get("rewardValid"), bool) or not isinstance(row.get("usedProductionPhysiology"), bool):
                    raise ValueError("Validity and source flags must be boolean")
                if not isinstance(row.get("finalSafeAction"), dict):
                    raise ValueError("Missing executed action")
                if row.get("onlineUpdateApplied") and (not row["rewardValid"] or row["policyMode"] != "PersonalizedBandit"):
                    raise ValueError("Invalid or baseline outcome claims a training update")
                if not include_simulation and not row.get("usedProductionPhysiology", False):
                    continue
                key = (row["sessionId"], row["decisionId"])
                if key in seen:
                    continue
                if row.get("rewardValid"):
                    reward = row.get("reward") or {}
                    for metric in ("totalReward", "preferenceMatch", "stability", "stressImprovement"):
                        if not isinstance(reward.get(metric), (float, int)) or not math.isfinite(reward[metric]):
                            raise ValueError("Missing or non-finite reward component")
                    if not row.get("nextState") or not row.get("state"):
                        raise ValueError("Valid outcome has no state pair")
                seen.add(key)
                records.append(row)
            except (ValueError, KeyError, TypeError) as error:
                rejected += 1
                print(f"Ignored {path.name}:{line_number}: {error}")
    return records, rejected


def summarize(records):
    sessions = defaultdict(list)
    for row in records:
        source = "physiology" if row.get("usedProductionPhysiology") else "simulation"
        sessions[(row["userId"], row["sessionId"], row["policyMode"], row.get("conditionSegment", 0), source)].append(row)
    rows = []
    for (user, session, mode, segment, source), outcomes in sorted(sessions.items()):
        valid = [row for row in outcomes if row.get("rewardValid")]
        magnitudes = [max(abs(row.get("finalSafeAction", {}).get(field, 0)) for field in ACTION_FIELDS) for row in valid]
        rows.append({
            "user_id": user, "session_id": session, "controller_mode": mode,
            "condition_segment": segment, "source": source,
            "resolved_outcomes": len(outcomes), "valid_outcomes": len(valid),
            "invalid_outcome_rate": 1 - len(valid) / len(outcomes),
            "mean_reward": mean([row["reward"]["totalReward"] for row in valid]),
            "cumulative_reward": sum(row["reward"]["totalReward"] for row in valid) if valid else None,
            "preference_satisfaction": mean([row["reward"]["preferenceMatch"] for row in valid]),
            "smoothness_score": mean([row["reward"]["stability"] for row in valid]),
            "stress_improvement": mean([row["reward"]["stressImprovement"] for row in valid]),
            "intervention_frequency": mean([float(magnitude > 1e-5) for magnitude in magnitudes]),
            "safety_filter_intervention_rate": mean([float(row.get("safetyMode") != "Normal"
                or row.get("proposedArm") != row.get("executedArm")) for row in valid]),
            "online_updates": sum(bool(row.get("onlineUpdateApplied")) for row in outcomes),
        })
    # Average repeated sessions within participant before computing between-person SD.
    participant_groups = defaultdict(list)
    for row in rows:
        participant_groups[(row["user_id"], row["controller_mode"], row["source"])].append(row)
    participant_rows = []
    metrics = ("mean_reward", "preference_satisfaction", "smoothness_score", "stress_improvement",
        "intervention_frequency", "safety_filter_intervention_rate", "invalid_outcome_rate")
    for (user, mode, source), sessions_for_user in participant_groups.items():
        participant_rows.append({"user_id": user, "controller_mode": mode, "source": source,
            **{metric: mean([row[metric] for row in sessions_for_user if row[metric] is not None]) for metric in metrics}})
    comparison = []
    for source, mode in sorted({(row["source"], row["controller_mode"]) for row in participant_rows}):
        selected = [row for row in participant_rows if row["source"] == source and row["controller_mode"] == mode]
        result = {"controller_mode": mode, "source": source, "participants": len(selected)}
        for metric in metrics:
            values = [row[metric] for row in selected if row[metric] is not None]
            result[metric + "_mean"] = mean(values)
            result[metric + "_std"] = statistics.stdev(values) if len(values) > 1 else None
            result[metric + "_participants"] = len(values)
        comparison.append(result)
    return rows, participant_rows, comparison


def write_csv(path, rows):
    if not rows:
        return
    with path.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--logs", type=Path, required=True, help="Unity RLTransitions directory or one JSONL file")
    parser.add_argument("--output", type=Path, default=Path("results/audio_online"))
    parser.add_argument("--include-simulation", action="store_true", help="Keep simulated rows in a SEPARATE comparison group")
    args = parser.parse_args()
    paths = sorted(args.logs.glob("*.jsonl")) if args.logs.is_dir() else [args.logs]
    if not paths or any(not path.is_file() for path in paths):
        parser.error("No JSONL logs found")
    records, rejected = load_records(paths, args.include_simulation)
    if not records:
        parser.error("No eligible version-2 outcomes. Run sessions in each controller condition first.")
    sessions, participants, comparison = summarize(records)
    args.output.mkdir(parents=True, exist_ok=True)
    write_csv(args.output / "sessions.csv", sessions)
    write_csv(args.output / "participants.csv", participants)
    write_csv(args.output / "comparison.csv", comparison)
    (args.output / "comparison.json").write_text(json.dumps({"comparison": comparison, "rejected_records": rejected}, indent=2, allow_nan=False), encoding="utf-8")
    report = ["# Audio Controller Condition Comparison", "",
        "Conditions: StaticPersonalized, RuleBasedAdaptive, PersonalizedBandit.", "",
        "Statistics are computed between participants after averaging their repeated sessions.",
        "Mean reward is the participant-averaged session mean per valid outcome, not an episode total.",
        "A single participant has no estimable between-participant standard deviation.",
        "Online learning updates are distinct from evaluated rewards. Invalid outcomes do not train the model.", "",
        "| Condition | Source | Participants | Mean Reward | Preference Match | Online Updates |",
        "| --- | --- | ---: | ---: | ---: | ---: |"]
    for row in comparison:
        updates = sum(s["online_updates"] for s in sessions if s["controller_mode"] == row["controller_mode"] and s["source"] == row["source"])
        reward = "NA" if row['mean_reward_mean'] is None else f"{row['mean_reward_mean']:.3f}"
        preference = "NA" if row['preference_satisfaction_mean'] is None else f"{row['preference_satisfaction_mean']:.3f}"
        report.append(f"| {row['controller_mode']} | {row['source']} | {row['participants']} | {reward} | {preference} | {updates} |")
    available = {row["controller_mode"] for row in comparison}
    report.extend(["", "## Interpretation", "",
        "This is descriptive evidence, not a randomized causal test or clinical result.",
        "A physiology-labelled row indicates the Component B transport format, not independent verification that its sensor values are real.",
        "Counterbalance conditions, control clip content and visual adaptation, and use an agreed participant protocol.",
        "Controller changes inside a session are demo segments, not independent experimental sessions.",
        "Safety-filter intervention is not a safety violation. This report does not infer physical comfort or clinical safety from bounds alone.",
        "Synthetic PPO/TD3/SAC results belong to the earlier offline experiment and are not comparable to these online participant statistics.",
        "Invalid-only sessions contribute to invalid-outcome rates but cannot supply reward estimates. Each metric reports its contributing participant count.",
        "Missing conditions: " + (", ".join(sorted(MODES - available)) or "none")])
    (args.output / "report.md").write_text("\n".join(report) + "\n", encoding="utf-8")
    print(json.dumps(comparison, indent=2, allow_nan=False))
    print(f"Report: {(args.output / 'report.md').resolve()}")


if __name__ == "__main__":
    main()
