import csv
import json
import tempfile
import unittest
from pathlib import Path

from generate_figures import PROTOCOL_ID, generate


class GenerateFiguresTests(unittest.TestCase):
    def test_generate_creates_three_checksummed_svg_figures(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            summary = root / "summary.csv"
            inference = root / "inference.csv"
            convergence = root / "convergence.csv"
            output = root / "figures"
            self._write_summary(summary)
            self._write_inference(inference)
            self._write_convergence(convergence)

            manifest = generate(summary, inference, convergence, output)

            self.assertEqual(3, len(manifest["figures_sha256"]))
            for name in manifest["figures_sha256"]:
                content = (output / name).read_text(encoding="utf-8")
                self.assertIn("<svg", content)
            loaded = json.loads(
                (output / "figures-manifest.json").read_text(encoding="utf-8")
            )
            self.assertEqual(PROTOCOL_ID, loaded["protocol_id"])

    @staticmethod
    def _write_summary(path: Path) -> None:
        fields = ["protocol_id", "experiment_id", "policy", "mean_pseudo_regret"]
        with path.open("w", encoding="utf-8", newline="") as handle:
            writer = csv.DictWriter(handle, fieldnames=fields)
            writer.writeheader()
            for policy_index, policy in enumerate(
                [
                    "StaticPersonalized",
                    "RuleBasedAdaptive",
                    "ContextualBandit",
                    "RandomSafe",
                ]
            ):
                for replication in range(4):
                    writer.writerow(
                        {
                            "protocol_id": PROTOCOL_ID,
                            "experiment_id": "study-a",
                            "policy": policy,
                            "mean_pseudo_regret": 0.1 * (policy_index + 1)
                            + 0.01 * replication,
                        }
                    )

    @staticmethod
    def _write_inference(path: Path) -> None:
        fields = [
            "protocol_id",
            "hypothesis",
            "mean_difference",
            "ci95_lower",
            "ci95_upper",
            "two_sided_p_value",
            "complete",
        ]
        with path.open("w", encoding="utf-8", newline="") as handle:
            writer = csv.DictWriter(handle, fieldnames=fields)
            writer.writeheader()
            writer.writerow(
                {
                    "protocol_id": PROTOCOL_ID,
                    "hypothesis": "H3",
                    "mean_difference": -0.05,
                    "ci95_lower": -0.08,
                    "ci95_upper": -0.02,
                    "two_sided_p_value": 0.01,
                    "complete": "true",
                }
            )
            writer.writerow(
                {
                    "protocol_id": PROTOCOL_ID,
                    "hypothesis": "H4",
                    "mean_difference": 0.03,
                    "ci95_lower": 0.01,
                    "ci95_upper": 0.06,
                    "two_sided_p_value": 0.02,
                    "complete": "true",
                }
            )

    @staticmethod
    def _write_convergence(path: Path) -> None:
        fields = [
            "protocol_id",
            "contrast",
            "cumulative_replications",
            "mean_difference",
            "ci95_lower",
            "ci95_upper",
            "complete",
        ]
        with path.open("w", encoding="utf-8", newline="") as handle:
            writer = csv.DictWriter(handle, fieldnames=fields)
            writer.writeheader()
            for count, mean in [(25, -0.03), (50, -0.04), (100, -0.045), (200, -0.05)]:
                writer.writerow(
                    {
                        "protocol_id": PROTOCOL_ID,
                        "contrast": "H3",
                        "cumulative_replications": count,
                        "mean_difference": mean,
                        "ci95_lower": mean - 0.02,
                        "ci95_upper": mean + 0.02,
                        "complete": "true",
                    }
                )


if __name__ == "__main__":
    unittest.main()
