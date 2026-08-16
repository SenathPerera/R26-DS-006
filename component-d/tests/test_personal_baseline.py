"""Tests for personal-baseline normalisation: cold start makes no claim, and
once history exists a reading is reported relative to the user's own normal."""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent.parent))
from componentd.personal_baseline import MIN_HISTORY, PersonalBaseline


def _seed(pb, user, values):
    for v in values:
        pb.observe(user, v)


def test_cold_start_makes_no_claim():
    pb = PersonalBaseline()
    r = pb.relative("u1", 5.0)
    assert r["personalised"] is False and r["relative_band"] is None
    assert "learning" in r["note"]


def test_typical_reading_is_typical():
    pb = PersonalBaseline()
    _seed(pb, "u1", [4.0, 4.2, 3.8, 4.1])   # usual ~4
    r = pb.relative("u1", 4.1)
    assert r["personalised"] and r["relative_band"] == "typical for you"


def test_elevated_reading_flagged_relative():
    pb = PersonalBaseline()
    _seed(pb, "u1", [3.8, 4.0, 4.1, 3.9])   # usual ~3.95, tight
    r = pb.relative("u1", 5.6)              # clearly above their normal
    assert r["personalised"]
    assert r["relative_band"] in ("above your usual", "much higher than usual")
    assert r["deviation"] > 1.0


def test_low_reading_below_usual():
    pb = PersonalBaseline()
    _seed(pb, "u1", [5.0, 5.2, 4.8, 5.1])
    r = pb.relative("u1", 3.0)
    assert r["relative_band"] == "below your usual"


def test_history_needs_min_sessions():
    pb = PersonalBaseline()
    _seed(pb, "u1", [4.0] * (MIN_HISTORY - 1))
    assert pb.relative("u1", 4.0)["personalised"] is False
    pb.observe("u1", 4.0)                    # now at MIN_HISTORY
    assert pb.relative("u1", 4.0)["personalised"] is True


def test_users_are_independent():
    pb = PersonalBaseline()
    _seed(pb, "a", [8.0, 8.1, 7.9, 8.0])     # user a runs hot
    _seed(pb, "b", [2.0, 2.1, 1.9, 2.0])     # user b runs cool
    # same absolute 5.0 is BELOW a's usual but ABOVE b's usual
    assert pb.relative("a", 5.0)["relative_band"] == "below your usual"
    assert pb.relative("b", 5.0)["relative_band"] in (
        "above your usual", "much higher than usual")
