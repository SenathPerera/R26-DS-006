// Browser-only fixtures: never submit synthetic observations to the relay.
const {chromium} = require("playwright");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const base = process.env.MONITOR_TEST_URL || "http://127.0.0.1:8090";
const output = path.resolve(".codex_tmp/project-monitor");
fs.mkdirSync(output, {recursive: true});

function samples(label, now) {
  return Array.from({length: 31}, (_, i) => {
    const at = now - (30 - i) * 5;
    const make = (size) => ({
      available: true, mode: size === 5 ? "ContextualBandit" : "PersonalizedBandit",
      state: size === 5 ? "Adaptive" : "Normal", transitioning: true, updates: size === 5 ? 3 : 2,
      applied: Array.from({length: size}, (_, j) => .35 + j * .04 + .08 * Math.sin(i / 6 + j)),
      target: Array.from({length: size}, (_, j) => .5 + j * .035),
      input: {available: true, source: "ComponentB", stress: label === "bbbbbbbbbb" ? .81 : .8 - i / 60,
        confidence: .93, quality: .88, ageSeconds: 8},
      decision: {available: true, at: now - 130, sequence: size === 5 ? 7 : 3, stress: .75,
        proposed: size === 5 ? "IncreaseAtmosphericSoftness" : "Increase ambience / reduce music",
        executed: size === 5 ? "IncreaseAtmosphericSoftness" : "Increase ambience / reduce music",
        result: "Selected", safety: "Accepted", proposedDelta: Array(size).fill(.05), safeDelta: Array(size).fill(.03)},
      reward: {available: i > 18, valid: true, at: now - 55, sequence: size === 5 ? 6 : 2,
        action: "Hold", status: "Updated", total: .31},
    });
    return {receivedAt: at, capturedAt: at, phase: "Adaptive", visual: make(5), audio: make(7)};
  });
}
async function main() {
  const browser = await chromium.launch({headless: true});
  try {
    const page = await browser.newPage({viewport: {width: 1440, height: 1050}});
    const errors = [];
    page.on("pageerror", error => errors.push(error.message));
    page.on("console", message => { if (message.type() === "error" && /Content Security|Refused to/.test(message.text())) errors.push(message.text()); });
    await page.goto(base + "/#agents");
    await page.waitForFunction(() => document.getElementById("agent-status").textContent.includes("No session"));
    await page.screenshot({path: output + "/actual-empty.png", fullPage: true});

    let state = "live", unavailable = false, fail = false, calls = 0, delay = 0;
    await page.route("**/api/snapshot?*", async route => {
      const requested = new URL(route.request().url()).searchParams.get("session");
      const session = requested || "aaaaaaaaaa";
      await route.fulfill({json: {
        checkedAt: Date.now() / 1000, session,
        sessions: ["aaaaaaaaaa", "bbbbbbbbbb"].map(label => ({label, phase: "Adaptive"})),
        services: ["component-b", "component-d", "relay", "lyria"].map(id => ({id, name: id, url: "http://127.0.0.1", reachable: true, latencyMs: 2, diagnostics: {}})),
        stages: [{id: "visual", title: "Visual policy", status: "active", group: "05 / Adapt", detail: "Unity", evidence: {}}],
        alerts: [], network: {localAddresses: ["127.0.0.1"], configuredHosts: []},
      }});
    });
    await page.route("**/api/agents?*", async route => {
      calls++;
      const label = new URL(route.request().url()).searchParams.get("session"), now = Date.now() / 1000;
      const payload = {schemaVersion: "mindsync-agents-v1", session: label, observedAt: now, state, samples: samples(label, now)};
      if (state === "stale") payload.samples.forEach(s => { s.receivedAt -= 30; s.audio.input.ageSeconds = 120; });
      if (unavailable) payload.samples.forEach(s => {
        for (const agent of [s.visual, s.audio]) { agent.available = false; agent.input.available = false; agent.decision.available = false; agent.reward.available = false; }
      });
      if (delay && label === "aaaaaaaaaa") await new Promise(resolve => setTimeout(resolve, delay));
      if (fail) await route.fulfill({status: 503, json: {detail: "relay-unavailable"}});
      else await route.fulfill({json: payload});
    });
    await page.reload();
    await page.waitForFunction(() => document.getElementById("agent-status").textContent.includes("Live observations"));
    assert.equal(await page.locator(".agent-panel").nth(0).locator("tbody tr").count(), 5);
    assert.equal(await page.locator(".agent-panel").nth(1).locator("tbody tr").count(), 7);
    assert.equal(await page.locator("#agent-events-body tr").count(), 4);
    for (const width of [1440, 390]) {
      await page.setViewportSize({width, height: width === 1440 ? 1050 : 844});
      await page.locator("#agents-view").scrollIntoViewIfNeeded();
      await page.waitForTimeout(100);
      assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), "page overflow");
      const counts = await page.locator(".agent-chart").evaluateAll(canvases => canvases.map(canvas => {
        const pixels = canvas.getContext("2d").getImageData(0, 0, canvas.width, canvas.height).data;
        let colored = 0;
        for (let i = 0; i < pixels.length; i += 4) if (pixels[i + 3] > 0 && Math.abs(pixels[i] - pixels[i + 1]) > 50) colored++;
        return colored;
      }));
      assert.ok(counts.every(n => n > 200), "nonblank data traces");
      await page.locator("#agents-view").screenshot({path: output + (width === 1440 ? "/agents-desktop.png" : "/agents-mobile.png")});
    }
    for (const [agent, count] of [["VR scene / C", 5], ["VR audio / E", 7]]) {
      for (let i = 0; i < count; i++) await page.getByLabel(agent + " variable").selectOption(String(i));
    }
    await page.locator("#agent-live").uncheck();
    const before = calls;
    await page.evaluate(() => window.agentMonitor.refresh());
    assert.equal(calls, before);
    assert.match(await page.locator("#agent-status").textContent(), /View paused/);
    await page.locator("#agent-live").check();
    await page.waitForFunction(() => document.getElementById("agent-status").textContent.includes("Live observations"));
    state = "stale";
    await page.evaluate(() => window.agentMonitor.refresh());
    await page.waitForFunction(() => document.getElementById("agent-status").dataset.state === "stale");
    assert.match(await page.locator("#agent-status").textContent(), /stale/);
    assert.match(await page.locator(".agent-panel").nth(1).locator(".agent-context").textContent(), /stale/);
    state = "disconnected";
    await page.evaluate(() => window.agentMonitor.refresh());
    await page.waitForFunction(() => document.getElementById("agent-status").dataset.state === "disconnected");
    assert.match(await page.locator("#agent-status").textContent(), /disconnected/);
    fail = true;
    await page.evaluate(() => window.agentMonitor.refresh());
    await page.waitForFunction(() => document.getElementById("agent-status").dataset.state === "offline");
    assert.match(await page.locator("#agent-status").textContent(), /unavailable/);
    fail = false; state = "live"; unavailable = true;
    await page.evaluate(() => window.agentMonitor.refresh());
    await page.waitForFunction(() => document.getElementById("agent-events-body").textContent.includes("No decisions"));
    assert.match(await page.locator(".agent-panel").first().locator("tbody").textContent(), /--/);
    assert.match(await page.locator("#agent-events-body").textContent(), /No decisions/);
    unavailable = false; delay = 700;
    await page.evaluate(() => { window.agentMonitor.refresh(); });
    await page.locator("#session").selectOption("bbbbbbbbbb");
    await page.waitForFunction(() => document.querySelector("#agent-session").textContent.includes("bbbbbbbbbb"));
    await page.waitForFunction(() => document.querySelector(".agent-context").textContent.includes("0.810"));
    await page.waitForTimeout(900);
    assert.match(await page.locator(".agent-context").first().textContent(), /0.810/);
    await page.locator("#flow-tab").click();
    assert.equal(await page.locator("#flow-view").isVisible(), true);
    await page.locator("#agents-tab").click();
    assert.equal(await page.locator("#agents-view").isVisible(), true);
    assert.deepEqual(errors, []);
    console.log("PASS browser: both agents, 12 selectors, 2 viewports, canvas pixels, freeze, stale/disconnected/offline, missing data, session race, tabs, CSP.");
    console.log("Screenshots: " + output);
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
