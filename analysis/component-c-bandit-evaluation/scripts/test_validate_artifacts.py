import hashlib
import json
import tempfile
import unittest
from pathlib import Path

import validate_artifacts


class ValidateInvariantArtifactsTests(unittest.TestCase):
    def test_valid_study_d_package(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            manifest_dir = root / "manifests"
            cases_dir = root / "results" / "invariant-cases"
            manifest_dir.mkdir(parents=True)
            cases_dir.mkdir(parents=True)
            cases_path = cases_dir / "study-d-test.jsonl"
            with cases_path.open("w", encoding="utf-8", newline="\n") as handle:
                for index in range(206):
                    handle.write(
                        json.dumps(
                            {
                                "schema_version": "1.0.0",
                                "protocol_id": validate_artifacts.PROTOCOL_ID,
                                "study": "study-d",
                                "case_id": f"case-{index}",
                                "category": "test",
                                "exact_input": {"index": index},
                                "proposed_action": None,
                                "validation_outcome": "Accepted",
                                "executed_state": None,
                                "reward_disposition": "not_applicable",
                                "model_update_count": 0,
                                "expected": "Accepted",
                                "actual": "Accepted",
                                "passed": True,
                            },
                            separators=(",", ":"),
                        )
                        + "\n"
                    )
            relative = "results/invariant-cases/study-d-test.jsonl"
            digest = hashlib.sha256(cases_path.read_bytes()).hexdigest()
            manifest_path = manifest_dir / "study-d-test.json"
            manifest_path.write_text(
                json.dumps(
                    {
                        "protocol_id": validate_artifacts.PROTOCOL_ID,
                        "artifact_type": "deterministic-invariant-suite",
                        "status": "completed",
                        "case_count": 206,
                        "passed_case_count": 206,
                        "failed_case_count": 0,
                        "all_cases_passed": True,
                        "output_sha256": {relative: digest},
                    }
                ),
                encoding="utf-8",
            )

            result = validate_artifacts.validate(manifest_path)

            self.assertEqual(result["status"], "valid")
            self.assertEqual(result["case_count"], 206)
            self.assertTrue(result["all_cases_passed"])


if __name__ == "__main__":
    unittest.main()
