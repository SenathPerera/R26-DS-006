import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


spec = importlib.util.spec_from_file_location("audio_evaluation", Path(__file__).parents[1] / "tools/evaluate-audio-online.py")
evaluation = importlib.util.module_from_spec(spec)
spec.loader.exec_module(evaluation)


def record(user="a", session="s1", mode="PersonalizedBandit", reward=1.0, valid=True):
    return {"schemaVersion": 2, "eventType": "outcome", "userId": user, "sessionId": session,
        "decisionId": session + "-1", "policyMode": mode, "usedProductionPhysiology": True,
        "conditionSegment": 0, "rewardValid": valid, "onlineUpdateApplied": valid,
        "proposedArm": 0, "executedArm": 0, "safetyMode": "Normal", "finalSafeAction": {},
        "state": {"signal": {}}, "nextState": {"signal": {}},
        "reward": {"totalReward": reward, "preferenceMatch": 0.9, "stability": 1, "stressImprovement": 0.1} if valid else None}


class AudioEvaluationTests(unittest.TestCase):
    def test_participant_mean_is_not_weighted_by_number_of_sessions(self):
        records = [record("a", "s1", reward=0), record("a", "s2", reward=0), record("b", "s3", reward=2)]
        _, _, comparison = evaluation.summarize(records)
        self.assertEqual(comparison[0]["mean_reward_mean"], 1)
        self.assertEqual(comparison[0]["participants"], 2)

    def test_invalid_outcome_is_not_zero_reward_training_data(self):
        sessions, participants, comparison = evaluation.summarize([record(valid=False)])
        self.assertIsNone(sessions[0]["mean_reward"])
        self.assertIsNone(participants[0]["mean_reward"])
        self.assertIsNone(comparison[0]["mean_reward_mean"])
        self.assertEqual(comparison[0]["mean_reward_participants"], 0)
        self.assertEqual(comparison[0]["invalid_outcome_rate_mean"], 1)

    def test_single_participant_standard_deviation_is_missing(self):
        self.assertIsNone(evaluation.summarize([record()])[2][0]["mean_reward_std"])

    def test_log_loader_skips_decisions_legacy_duplicates_and_simulation(self):
        row = record()
        simulated = record(session="sim"); simulated["usedProductionPhysiology"] = False
        decision = record(session="decision"); decision["eventType"] = "decision"
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "test.jsonl"
            path.write_text("\n".join(json.dumps(r) for r in (row, row, simulated, decision, {"schemaVersion": 1})))
            records, errors = evaluation.load_records([path])
            self.assertEqual(len(records), 1)
            self.assertEqual(errors, 0)
            self.assertEqual(len(evaluation.load_records([path], True)[0]), 2)

    def test_simulated_and_physiology_comparisons_are_separate(self):
        simulated = record(session="sim"); simulated["usedProductionPhysiology"] = False
        comparison = evaluation.summarize([record(), simulated])[2]
        self.assertEqual({row["source"] for row in comparison}, {"simulation", "physiology"})


if __name__ == "__main__":
    unittest.main()
