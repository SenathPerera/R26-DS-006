const {test} = require("node:test");
const assert = require("node:assert/strict");
const {inputFresh, variableChange, eventsFromSamples, series, dimensions} = require("./static/agents.js");
function sample(at, val = .4) {
  const agent = () => ({
    available: true, applied: [val], target: [.7],
    input: {available: true, source: "ComponentB", ageSeconds: 2, stress: .6},
    decision: {available: true, at: 1, sequence: 1},
    reward: {available: false},
  });
  return {receivedAt: at, visual: agent(), audio: agent()};
}
test("all scene and audio controls have defined ordering", () => {
  assert.equal(dimensions.visual.length, 5); assert.equal(dimensions.audio.length, 7);
  assert.deepEqual(dimensions.audio.slice(5), ["Music mix", "Ambient mix"]);
});
test("missing and stale input never becomes zero stress", () => {
  assert.equal(inputFresh({available: false}), false);
  assert.equal(inputFresh({available: true, source: "ComponentB", ageSeconds: 91}), false);
  assert.equal(inputFresh({available: true, source: "Simulator", ageSeconds: 4}), false);
  const a = sample(1); a.audio.input.available = false;
  assert.equal(series([a], "audio", 0, "stress")[0].value, null);
});
test("change uses adjacent same-session snapshots with no long gaps", () => {
  assert.equal(variableChange([sample(1)], "visual", 0), null);
  assert.equal(variableChange([sample(1), sample(30, .5)], "visual", 0), null);
  assert.ok(Math.abs(variableChange([sample(1), sample(6, .5)], "visual", 0) - .1) < 1e-6);
  const missing = sample(1); missing.visual.available = false;
  assert.equal(variableChange([missing, sample(6)], "visual", 0), null);
});
test("cached decisions are deduplicated and rewards remain tied to their original sequence", () => {
  const a = sample(1), b = sample(6), c = sample(11);
  c.audio.decision.sequence = 2; c.audio.decision.at = 10;
  c.audio.reward = {available: true, at: 9, sequence: 1, valid: true, total: .4};
  const events = eventsFromSamples([a, b, c]);
  assert.equal(events.length, 4);
  assert.equal(events.find(e => e.type === "reward").sequence, 1);
});
