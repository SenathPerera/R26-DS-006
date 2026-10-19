import csv
import json
import runpy
import tempfile
import unittest
from pathlib import Path


SCRIPT = Path(__file__).with_name("analyze_longitudinal.py")
TEMP_ROOT = SCRIPT.parents[3] / ".codex_tmp"
MODULE = runpy.run_path(str(SCRIPT))
PROTOCOL = MODULE["PROTOCOL"]
SHA256 = MODULE["sha256"]
ANALYZE = MODULE["analyze"]
PAIRED_INFERENCE = MODULE["paired_inference"]


class LongitudinalAnalysisTests(unittest.TestCase):
    def test_paired_inference_is_seeded_and_detects_consistent_gain(self):
        settings = {
            "sign_flip_pcg32_seed": 930001,
            "sign_flip_pcg32_stream": 7,
            "bootstrap_pcg32_seed": 940001,
            "bootstrap_pcg32_stream": 8,
            "sign_flip_draws": 1000,
            "bootstrap_draws": 200,
            "alpha": .05,
        }
        differences = [-0.1 - index / 1000 for index in range(30)]
        result = PAIRED_INFERENCE(differences, settings)
        self.assertEqual(result, PAIRED_INFERENCE(differences, settings))
        self.assertLess(result["two_sided_sign_flip_p"], .05)
        self.assertTrue(result["exploratory_directional_criterion_met"])
        self.assertLess(result["bootstrap_95_percentile_interval"][1], 0)

    def test_reduced_complete_run_validates_without_primary_inference(self):
        TEMP_ROOT.mkdir(exist_ok=True)
        with tempfile.TemporaryDirectory(dir=TEMP_ROOT) as directory:
            run = Path(directory)
            self._write_fixture(run)
            output_path, output = ANALYZE(run)
            self.assertTrue(output_path.exists())
            self.assertEqual(output["validation"], "passed")
            self.assertFalse(output["full_proposed_matrix"])
            self.assertNotIn("primary_proposed_analysis", output)
            self.assertTrue((run / "learning-curve.csv").exists())

    def test_checksum_mismatch_rejects_tampered_probe(self):
        TEMP_ROOT.mkdir(exist_ok=True)
        with tempfile.TemporaryDirectory(dir=TEMP_ROOT) as directory:
            run = Path(directory)
            self._write_fixture(run)
            with (run / "probes.csv").open("a", encoding="utf-8") as target:
                target.write("tampered\n")
            with self.assertRaisesRegex(ValueError, "Checksum mismatch"):
                ANALYZE(run)

    @staticmethod
    def _write_fixture(run):
        identity = [PROTOCOL, "fixture", "Primary",
                    "A1PreferenceAlignedBalanced", "410000"]
        with (run / "sessions.csv").open("w", newline="", encoding="utf-8") as file:
            writer = csv.writer(file)
            writer.writerow(("protocol_id", "run_id", "scenario", "archetype",
                             "profile_seed", "session_index", "condition",
                             "scene_violations", "multidimension_violations",
                             "invalid_model_updates"))
            writer.writerow((*identity, 1, "CarryLinUcb", 0, 0, 0))
        with (run / "probes.csv").open("w", newline="", encoding="utf-8") as file:
            writer = csv.writer(file)
            writer.writerow(("protocol_id", "run_id", "scenario", "archetype",
                             "profile_seed", "condition", "checkpoint",
                             "probe_index", "pseudo_regret"))
            writer.writerow((*identity, "CarryLinUcb", 0, 0, .1))
            writer.writerow((*identity, "CarryLinUcb", 1, 0, .05))
        for name in ("decisions.csv", "rewards.csv", "physiology.csv"):
            (run / name).write_text("protocol_id,run_id\n", encoding="utf-8")

        config = SCRIPT.parents[1] / "configs" / "longitudinal-study-v0.2.json"
        manifest = {
            "protocol_id": PROTOCOL,
            "run_id": "fixture",
            "status": "completed",
            "research_status": "proposed_not_frozen",
            "sessions_per_profile": 1,
            "replications_per_archetype": 1,
            "probes_per_checkpoint": 1,
            "session_rows": 1,
            "probe_rows": 2,
            "decision_rows": 0,
            "reward_rows": 0,
            "physiology_rows": 0,
            "analysis_script_sha256": SHA256(SCRIPT),
            "study_config_sha256": SHA256(config),
        }
        for name in MODULE["FILES"]:
            manifest[name.replace(".", "_") + "_sha256"] = SHA256(run / name)
        (run / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")


if __name__ == "__main__":
    unittest.main()
