"""Bounded, session-scoped observations; never an agent input or study log."""
import copy
import math
import re
from collections import deque

SCHEMA = "mindsync-agents-v1"
HISTORY_SECONDS = 15 * 60
MAX_SAMPLES = 180


def number(value, low=0, high=1):
    return type(value) in (int, float) and low <= value <= high and math.isfinite(value)


def token(value):
    return isinstance(value, str) and re.fullmatch(r"[A-Za-z0-9 _/.-]{1,96}", value) is not None


def fields(value, validators):
    return (isinstance(value, dict) and value.keys() == validators.keys()
            and all(check(value[key]) for key, check in validators.items()))


def validate_snapshot(value):
    boolean = lambda v: type(v) is bool
    timestamp = lambda v: number(v, 0, 1e12)
    count = lambda v: type(v) is int and 0 <= v <= 10**12
    def vector(v, size, low=0):
        return isinstance(v, list) and len(v) == size and all(number(n, low, 1) for n in v)
    def agent(value, size):
        return fields(value, {
            "available": boolean, "mode": token, "state": token, "transitioning": boolean,
            "updates": count, "applied": lambda v: vector(v, size), "target": lambda v: vector(v, size),
            "input": lambda v: fields(v, {
                "available": boolean, "source": lambda s: s in ("unknown", "ComponentB", "Simulator"),
                "stress": number, "confidence": number, "quality": number,
                "ageSeconds": timestamp,
            }),
            "decision": lambda v: fields(v, {
                "available": boolean, "sequence": count, "at": timestamp, "stress": number,
                "proposed": token, "executed": token, "safety": token, "result": token,
                "proposedDelta": lambda v: vector(v, size, -1),
                "safeDelta": lambda v: vector(v, size, -1),
            }),
            "reward": lambda v: fields(v, {
                "available": boolean, "valid": boolean, "at": timestamp, "sequence": count,
                "action": token, "status": token, "total": lambda n: number(n, -100, 100),
            }),
        })
    return fields(value, {
        "schemaVersion": lambda s: s == SCHEMA, "capturedAt": timestamp, "phase": token,
        "visual": lambda v: agent(v, 5), "audio": lambda v: agent(v, 7),
    })


class AgentHistory:
    def __init__(self):
        self.samples = deque(maxlen=MAX_SAMPLES)

    def append(self, snapshot, now):
        self.prune(now)
        if not validate_snapshot(snapshot):
            return
        # Ignore duplicates, backwards clocks and floods. Server receipt time drives the chart.
        if self.samples and (now - self.samples[-1]["receivedAt"] < 1
                             or snapshot["capturedAt"] <= self.samples[-1]["capturedAt"]):
            return
        self.samples.append({"receivedAt": now, **copy.deepcopy(snapshot)})
        self.prune(now)

    def prune(self, now):
        while self.samples and now - self.samples[0]["receivedAt"] > HISTORY_SECONDS:
            self.samples.popleft()

    def snapshot(self, now):
        self.prune(now)
        return copy.deepcopy(list(self.samples))
