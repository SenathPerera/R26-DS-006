import csv
import tempfile
import unittest
from pathlib import Path

from report_longitudinal_v1 import probe_curve, read_sessions


TEMP_ROOT = Path(__file__).resolve().parents[3] / ".codex_tmp"


class LongitudinalReportTests(unittest.TestCase):
    def test_primary_curve_weights_each_archetype_per_seed(self):
        values = {}
        for seed in (410000, 410001):
            for archetype in range(1, 8):
                value = (7 if seed == 410000 else 14) if archetype == 7 else 0
                values[("Primary", f"A{archetype}", seed,
                        "CarryLinUcb", 20)] = value
        curve = probe_curve(values, 2)
        pooled = next(row for row in curve
                      if row["archetype"] == "PooledEqualArchetype")
        self.assertEqual(pooled["profiles"], 2)
        self.assertAlmostEqual(pooled["mean_probe_regret"], 1.5)
        self.assertAlmostEqual(pooled["sd_across_profiles"], 2 ** -.5)

    def test_missing_stress_change_remains_unavailable(self):
        TEMP_ROOT.mkdir(exist_ok=True)
        with tempfile.TemporaryDirectory(dir=TEMP_ROOT) as directory:
            run = Path(directory)
            with (run / "sessions.csv").open("w", newline="", encoding="utf-8") as target:
                writer = csv.DictWriter(target, fieldnames=(
                    "scenario", "archetype", "profile_seed", "condition",
                    "session_index", "baseline_available", "eligible_decisions",
                    "mean_regret", "cumulative_regret", "cumulative_reward",
                    "synthetic_stress_change", "executed_changes", "valid_outcomes",
                    "session_model_updates", "no_change", "total_variation",
                    "scene_violations", "multidimension_violations",
                    "invalid_model_updates"))
                writer.writeheader()
                writer.writerow({"scenario": "MildPhysiologyLoss", "archetype": "A1",
                                 "profile_seed": 420000, "condition": "CarryLinUcb",
                                 "session_index": 1, "baseline_available": "false",
                                 "eligible_decisions": 0, "mean_regret": "",
                                 "cumulative_regret": 0, "cumulative_reward": 0,
                                 "synthetic_stress_change": "", "executed_changes": 0,
                                 "valid_outcomes": 0, "session_model_updates": 0,
                                 "no_change": 0, "total_variation": 0,
                                 "scene_violations": 0, "multidimension_violations": 0,
                                 "invalid_model_updates": 0})
            session = read_sessions(run)[0]
            self.assertIsNone(session["stress_change"])
            self.assertIsNone(session["mean_regret"])
            self.assertEqual(session["cumulative_valid_outcomes"], 0)


if __name__ == "__main__":
    unittest.main()
