#!/usr/bin/env python3
"""Validate and summarize a proposed longitudinal Study A run.

Only Python's standard library is required. A reduced smoke run receives
descriptive validation; the primary inference requires the complete matrix.
"""

import argparse
import csv
import hashlib
import json
import math
from collections import defaultdict
from pathlib import Path


PROTOCOL = "laminar-vr-longitudinal-simulation-v0.2"
MASK64 = (1 << 64) - 1
MASK32 = (1 << 32) - 1
FILES = ("sessions.csv", "probes.csv", "decisions.csv", "rewards.csv",
         "physiology.csv")


def mix64(value):
    value = (value + 0x9E3779B97F4A7C15) & MASK64
    value = ((value ^ (value >> 30)) * 0xBF58476D1CE4E5B9) & MASK64
    value = ((value ^ (value >> 27)) * 0x94D049BB133111EB) & MASK64
    return value ^ (value >> 31)


class Pcg32:
    def __init__(self, seed, stream):
        self.increment = ((mix64(seed ^ 0xDA3E39CB94B95BDB
                                 ^ ((stream * 0xD1B54A32D192ED03) & MASK64))
                           << 1) | 1) & MASK64
        initial = mix64(seed ^ 0x853C49E6748FEA9B
                        ^ ((stream * 0x9E3779B97F4A7C15) & MASK64))
        self.state = 0
        self.next_uint()
        self.state = (self.state + initial) & MASK64
        self.next_uint()

    def next_uint(self):
        old = self.state
        self.state = (old * 6364136223846793005 + self.increment) & MASK64
        shifted = (((old >> 18) ^ old) >> 27) & MASK32
        rotation = old >> 59
        return ((shifted >> rotation)
                | (shifted << ((-rotation) & 31))) & MASK32

    def next_int(self, bound):
        threshold = ((-bound) & MASK32) % bound
        while True:
            value = self.next_uint()
            if value >= threshold:
                return value % bound


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def read_checked_csv(path, expected_protocol, expected_run_id):
    with path.open(newline="", encoding="utf-8-sig") as source:
        reader = csv.DictReader(source)
        if not reader.fieldnames or not {"protocol_id", "run_id"}.issubset(
                reader.fieldnames):
            raise ValueError(f"Missing identity columns: {path}")
        for row in reader:
            if (None in row or any(value is None for value in row.values())
                    or row["protocol_id"] != expected_protocol
                    or row["run_id"] != expected_run_id):
                raise ValueError(f"Malformed or foreign row in {path}")
            yield row


def percentile(sorted_values, probability):
    position = (len(sorted_values) - 1) * probability
    lower = math.floor(position)
    upper = math.ceil(position)
    return sorted_values[lower] + (position - lower) * (
        sorted_values[upper] - sorted_values[lower])


def paired_inference(paired, settings):
    observed = sum(paired) / len(paired)
    sign_rng = Pcg32(settings["sign_flip_pcg32_seed"],
                     settings["sign_flip_pcg32_stream"])
    draws = settings["sign_flip_draws"]
    extreme = 0
    for _ in range(draws):
        statistic = sum(value if sign_rng.next_int(2) else -value
                        for value in paired) / len(paired)
        extreme += abs(statistic) >= abs(observed) - 1e-15
    p_value = (extreme + 1) / (draws + 1)
    boot_rng = Pcg32(settings["bootstrap_pcg32_seed"],
                     settings["bootstrap_pcg32_stream"])
    bootstrap = sorted(
        sum(paired[boot_rng.next_int(len(paired))]
            for _ in paired) / len(paired)
        for _ in range(settings["bootstrap_draws"]))
    return {
        "paired_seed_index_count": len(paired),
        "mean_carry_minus_reset_probe_regret": observed,
        "median_carry_minus_reset_probe_regret": percentile(sorted(paired), .5),
        "percent_seed_indices_better_carry": 100 * sum(
            value < 0 for value in paired) / len(paired),
        "bootstrap_95_percentile_interval": [percentile(bootstrap, .025),
                                             percentile(bootstrap, .975)],
        "two_sided_sign_flip_p": p_value,
        "sign_flip_draws": draws,
        "alpha": settings["alpha"],
        "exploratory_directional_criterion_met": (
            observed < 0 and p_value < settings["alpha"]),
    }


def analyze(run_directory):
    manifest_path = run_directory / "manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
    if manifest.get("status") != "completed":
        raise ValueError("Only a completed run can be analyzed")
    if manifest.get("protocol_id") != PROTOCOL:
        raise ValueError("Unexpected protocol ID")
    for filename in FILES:
        expected = manifest.get(filename.replace(".", "_") + "_sha256")
        if not expected or sha256(run_directory / filename) != expected:
            raise ValueError(f"Checksum mismatch: {filename}")

    config_path = (Path(__file__).resolve().parents[1] / "configs"
                   / "longitudinal-study-v0.2.json")
    config = json.loads(config_path.read_text(encoding="utf-8"))
    if config["protocol_id"] != PROTOCOL:
        raise ValueError("Study configuration protocol mismatch")
    for key, path in (
        ("study_config_sha256", config_path),
        ("analysis_script_sha256", Path(__file__).resolve()),
    ):
        if manifest.get(key) != sha256(path):
            raise ValueError(f"Run was produced with another {key}")

    sessions = {}
    session_rows = 0
    for row in read_checked_csv(run_directory / "sessions.csv", PROTOCOL,
                                manifest["run_id"]):
        key = (row["scenario"], row["archetype"], int(row["profile_seed"]),
               row["condition"], int(row["session_index"]))
        if key in sessions:
            raise ValueError(f"Duplicate session: {key}")
        if int(row["scene_violations"]) or int(row["multidimension_violations"]):
            raise ValueError(f"Safety violation: {key}")
        if int(row["invalid_model_updates"]):
            raise ValueError(f"Invalid model update: {key}")
        sessions[key] = row
        session_rows += 1
    if session_rows != manifest["session_rows"]:
        raise ValueError("Session count differs from manifest")

    probe_totals = defaultdict(lambda: [0.0, 0, 0])
    probe_rows = 0
    for row in read_checked_csv(run_directory / "probes.csv", PROTOCOL,
                                manifest["run_id"]):
        key = (row["scenario"], row["archetype"], int(row["profile_seed"]),
               row["condition"], int(row["checkpoint"]))
        index = int(row["probe_index"])
        if not 0 <= index < manifest["probes_per_checkpoint"]:
            raise ValueError(f"Invalid probe index: {key}/{index}")
        regret = float(row["pseudo_regret"])
        if not math.isfinite(regret) or regret < -1e-8:
            raise ValueError(f"Invalid probe regret: {key}/{index}")
        record = probe_totals[key]
        bit = 1 << index
        if record[2] & bit:
            raise ValueError(f"Duplicate probe: {key}/{index}")
        record[0] += regret
        record[1] += 1
        record[2] |= bit
        probe_rows += 1
    if probe_rows != manifest["probe_rows"]:
        raise ValueError("Probe count differs from manifest")

    for filename, manifest_key in (("decisions.csv", "decision_rows"),
                                   ("rewards.csv", "reward_rows"),
                                   ("physiology.csv", "physiology_rows")):
        count = sum(1 for _ in read_checked_csv(run_directory / filename,
                                                 PROTOCOL, manifest["run_id"]))
        if count != manifest[manifest_key]:
            raise ValueError(f"{filename} count differs from manifest")

    session_count = manifest["sessions_per_profile"]
    probe_count = manifest["probes_per_checkpoint"]
    for key, value in probe_totals.items():
        if value[1] != probe_count:
            raise ValueError(f"Incomplete probe bank: {key}")
    trajectories = {(key[0], key[1], key[2], key[3]) for key in sessions}
    for trajectory in trajectories:
        prior_updates = 0
        for checkpoint in range(session_count + 1):
            if (*trajectory, checkpoint) not in probe_totals:
                raise ValueError(f"Missing probe checkpoint: {trajectory}/{checkpoint}")
            if checkpoint and (*trajectory, checkpoint) not in sessions:
                raise ValueError(f"Missing session: {trajectory}/{checkpoint}")
            if checkpoint:
                row = sessions[(*trajectory, checkpoint)]
                if {"session_model_updates", "total_model_updates",
                        "valid_outcomes"}.issubset(row):
                    updates = int(row["session_model_updates"])
                    total = int(row["total_model_updates"])
                    outcomes = int(row["valid_outcomes"])
                    condition = trajectory[3]
                    if condition == "CarryLinUcb":
                        valid = updates == outcomes and total == prior_updates + updates
                        prior_updates = total
                    elif condition == "ResetLinUcb":
                        valid = updates == outcomes and total == updates
                    else:
                        valid = updates == 0 and total == 0
                    if not valid:
                        raise ValueError(f"Model update continuity failed: {trajectory}/{checkpoint}")
    if len(sessions) != len(trajectories) * session_count:
        raise ValueError("Trajectory/session matrix is incomplete")
    if len(probe_totals) != len(trajectories) * (session_count + 1):
        raise ValueError("Unexpected probe trajectory")

    full = (manifest["replications_per_archetype"] == 30
            and session_count == 20 and probe_count == 32
            and session_rows == 20400 and len(trajectories) == 1020)
    if full:
        primary_names = sorted({key[1] for key in trajectories
                                if key[0] == "Primary"})
        if len(primary_names) != 7 or any(
                not any(name.startswith(f"A{index}") for name in primary_names)
                for index in range(1, 8)):
            raise ValueError("Unexpected primary archetype set")
        a1_name = next(name for name in primary_names if name.startswith("A1"))
        a9_names = {key[1] for key in trajectories
                    if key[0] == "NonResponder"}
        if len(a9_names) != 1 or not next(iter(a9_names)).startswith("A9"):
            raise ValueError("Unexpected negative-control archetype")
        expected_trajectories = {
            ("Primary", archetype, seed, condition)
            for archetype in primary_names
            for seed in range(410000, 410030)
            for condition in config["primary_conditions"]
        }
        expected_trajectories.update(
            (scenario, next(iter(a9_names)) if scenario == "NonResponder"
             else a1_name, seed, condition)
            for scenario in ("BetweenSessionDrift", "MildPhysiologyLoss",
                             "NonResponder")
            for seed in range(420000, 420030)
            for condition in config["challenge_conditions"])
        if trajectories != expected_trajectories:
            raise ValueError("Full trajectory matrix differs from study config")
    output = {
        "protocol_id": PROTOCOL,
        "run_id": manifest["run_id"],
        "research_status": manifest["research_status"],
        "validation": "passed",
        "full_proposed_matrix": full,
        "session_rows": session_rows,
        "probe_rows": probe_rows,
        "trajectory_count": len(trajectories),
    }

    curve_path = run_directory / "learning-curve.csv"
    curve = defaultdict(list)
    for key, (total, count, _) in probe_totals.items():
        scenario, archetype, _, condition, checkpoint = key
        curve[(scenario, archetype, condition, checkpoint)].append(total / count)
    with curve_path.open("w", newline="", encoding="utf-8") as target:
        writer = csv.writer(target)
        writer.writerow(("scenario", "archetype", "condition", "checkpoint",
                         "profiles", "mean_probe_pseudo_regret"))
        for key in sorted(curve):
            values = curve[key]
            writer.writerow((*key, len(values),
                             format(sum(values) / len(values), ".17g")))

    if full:
        paired = []
        per_archetype = defaultdict(list)
        for offset in range(30):
            archetype_differences = []
            for archetype_number in range(1, 8):
                archetype = next((key[1] for key in trajectories
                                  if key[0] == "Primary" and key[2] == 410000 + offset
                                  and key[1].startswith(f"A{archetype_number}")), None)
                if archetype is None:
                    raise ValueError("Missing primary archetype")
                def mean_regret(condition):
                    key = ("Primary", archetype, 410000 + offset,
                           condition, 20)
                    if key not in probe_totals:
                        raise ValueError(f"Missing paired endpoint: {key}")
                    total, count, _ = probe_totals[key]
                    return total / count

                difference = (mean_regret("CarryLinUcb")
                              - mean_regret("ResetLinUcb"))
                archetype_differences.append(difference)
                per_archetype[archetype].append(difference)
            paired.append(sum(archetype_differences) / 7)

        output["primary_proposed_analysis"] = {
            **paired_inference(paired, config["inference"]),
            "sample_sd_seed_index_differences": math.sqrt(
                sum((value - sum(paired) / len(paired)) ** 2
                    for value in paired) / (len(paired) - 1)),
            "percent_profiles_better_carry": 100 * sum(
                value < 0 for values in per_archetype.values()
                for value in values) / 210,
            "archetype_mean_differences": {
                name: sum(values) / len(values)
                for name, values in sorted(per_archetype.items())
            },
        }
        with (run_directory / "paired-index-effects.csv").open(
                "w", newline="", encoding="utf-8") as target:
            writer = csv.writer(target)
            writer.writerow(("seed_index", "mean_carry_minus_reset_probe_regret"))
            for offset, value in enumerate(paired):
                writer.writerow((offset, format(value, ".17g")))

    output_path = run_directory / "analysis.json"
    output_path.write_text(json.dumps(output, indent=2, sort_keys=True) + "\n",
                           encoding="utf-8")
    return output_path, output


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("run_directory", type=Path)
    args = parser.parse_args()
    path, output = analyze(args.run_directory.resolve())
    print(f"Validated {output['session_rows']} sessions; wrote {path}")
    if not output["full_proposed_matrix"]:
        print("Reduced run: primary inference was not calculated")


if __name__ == "__main__":
    main()
