"use strict";
(() => {
  const dimensions = {
    visual: ["Illumination", "Warmth", "Atmospheric softness", "Color richness", "Ambient motion"],
    audio: ["Intensity", "Density", "Brightness", "Tempo", "Fade", "Music mix", "Ambient mix"],
  };
  const finite = v => typeof v === "number" && Number.isFinite(v);
  const value = v => finite(v) ? v.toFixed(3) : "--";
  const delta = v => finite(v) ? (v > 0 ? "+" : "") + v.toFixed(3) : "--";
  const time = v => v > 0 ? new Date(v * 1000).toLocaleTimeString() : "--";
  function inputFresh(input, elapsed = 0) {
    return input?.available && input.ageSeconds + elapsed <= (input.source === "Simulator" ? 3 : 90);
  }
  function variableChange(samples, agent, index) {
    if (samples.length < 2) return null;
    const a = samples.at(-2), b = samples.at(-1);
    if (!a[agent].available || !b[agent].available || b.receivedAt - a.receivedAt > 15) return null;
    return b[agent].applied[index] - a[agent].applied[index];
  }
  function eventsFromSamples(samples) {
    const events = new Map();
    for (const sample of samples) for (const agent of ["visual", "audio"]) {
      if (!sample[agent].available) continue;
      for (const type of ["decision", "reward"]) {
        const event = sample[agent][type];
        if (!event.available) continue;
        const key = [agent, type, event.at, event.sequence].join(":");
        if (!events.has(key)) events.set(key, {...event, agent, type});
      }
    }
    return [...events.values()].sort((a, b) => b.at - a.at).slice(0, 60);
  }
  function series(samples, agent, index, key) {
    return samples.map(sample => ({
      at: sample.receivedAt,
      value: !sample[agent].available ? null : key === "stress"
        ? inputFresh(sample[agent].input) ? sample[agent].input.stress : null
        : sample[agent][key][index],
    }));
  }
  if (typeof module !== "undefined") module.exports = {dimensions, inputFresh, variableChange, eventsFromSamples, series};
  if (typeof document === "undefined") return;

  const $ = id => document.getElementById(id);
  const element = (tag, text, cls) => {
    const el = document.createElement(tag);
    if (text !== undefined) el.textContent = text;
    if (cls) el.className = cls;
    return el;
  };
  let session = null, data = null, request = null, offline = false;
  const selected = {visual: 0, audio: 0};
  const panels = {};
  const labels = {visual: "VR scene / C", audio: "VR audio / E"};
  const active = () => !$("agents-view").hidden;

  function setView(name) {
    for (const view of ["flow", "agents"]) {
      $(view + "-tab").setAttribute("aria-selected", String(name === view));
      $(view + "-tab").tabIndex = name === view ? 0 : -1;
      $(view + "-view").hidden = name !== view;
    }
    history.replaceState(null, "", name === "agents" ? "#agents" : "#flow");
    if (name === "agents") { render(); refresh(); }
  }
  for (const name of ["flow", "agents"]) {
    $(name + "-tab").onclick = () => setView(name);
    $(name + "-tab").onkeydown = event => {
      if (["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) {
        event.preventDefault();
        const next = event.key === "Home" ? "flow" : event.key === "End" ? "agents" : name === "flow" ? "agents" : "flow";
        setView(next); $(next + "-tab").focus();
      }
    };
  }

  function buildPanel(agent) {
    const section = element("section", undefined, "agent-panel");
    const title = element("div", undefined, "agent-title"), mode = element("span", "Not reported", "badge");
    title.append(element("h3", labels[agent]), mode);
    const context = element("dl", undefined, "agent-context");
    const toolbar = element("label", "Trace", "chart-toolbar");
    const select = element("select"); select.setAttribute("aria-label", labels[agent] + " variable");
    dimensions[agent].forEach((name, i) => select.append(new Option(name, i)));
    select.onchange = () => { selected[agent] = Number(select.value); drawCharts(); };
    toolbar.append(select);
    const canvas = element("canvas", undefined, "agent-chart"); canvas.setAttribute("role", "img");
    const legend = element("div", undefined, "chart-legend");
    legend.append(element("span", "Stress", "stress"), element("span", "Applied", "applied"), element("span", "Target", "target"));
    const footnote = element("p", undefined, "agent-footnote");
    const table = element("table"), head = element("thead"), row = element("tr"), body = element("tbody");
    for (const heading of ["Variable (0-1)", "Applied", "Target", "Change", "Proposed delta", "Safe delta"]) row.append(element("th", heading));
    head.append(row); table.append(head, body);
    const scroll = element("div", undefined, "table-scroll"); scroll.append(table);
    const decision = element("div", undefined, "agent-decision");
    section.append(title, context, toolbar, canvas, legend, footnote, scroll, decision);
    panels[agent] = {section, mode, context, canvas, footnote, body, decision};
    return section;
  }
  $("agent-panels").replaceChildren(buildPanel("visual"), buildPanel("audio"));

  function status(text, state) {
    $("agent-status").textContent = text;
    $("agent-status").dataset.state = state;
    $("agents-view").classList.toggle("agent-offline", ["stale", "offline", "disconnected"].includes(state));
  }
  function selectSession(label) {
    if (label === session) return;
    request?.abort(); request = null;
    session = label; data = null; offline = false;
    $("agent-live").checked = true;
    render();
  }
  function visibleSamples() {
    const cutoff = (data?.observedAt || 0) - Number($("agent-range").value);
    return (data?.samples || []).filter(s => s.receivedAt >= cutoff);
  }
  function stat(title, main, detail) {
    const item = element("div"), dd = element("dd", main);
    dd.append(element("small", detail)); item.append(element("dt", title), dd); return item;
  }
  function render() {
    $("agent-session").textContent = session ? "Session " + session + (data?.samples.length ? " / " + data.samples.at(-1).phase : "") : "No session selected";
    const hasData = Boolean(data?.samples.length);
    $("agent-content").hidden = !hasData;
    if (!session) { status("No session selected. Waiting for a paired Unity session.", "unreported"); return; }
    const paused = !$("agent-live").checked;
    if (offline) status("Agent feed unavailable. Retained observations are historical.", "offline");
    else if (paused) status("View paused at " + time(data?.observedAt) + ". Values below are historical.", "paused");
    else if (!hasData) status("No agent observations for this session. Updated Unity and relay reports are required.", "unreported");
    else {
      const age = Math.max(0, data.observedAt - data.samples.at(-1).receivedAt);
      const states = {live: "Live observations", stale: "Unity reports are stale", disconnected: "Unity disconnected"};
      status((states[data.state] || "Historical observations") + " / received " + age.toFixed(0) + "s ago / 5s sampling", data.state);
    }
    if (!hasData) return;
    const samples = visibleSamples(), latest = data.samples.at(-1);
    for (const agent of ["visual", "audio"]) {
      const report = latest[agent], panel = panels[agent], input = report.input, decision = report.decision, reward = report.reward;
      const age = input.ageSeconds + Math.max(0, data.observedAt - latest.receivedAt);
      const fresh = inputFresh(input, Math.max(0, data.observedAt - latest.receivedAt));
      panel.mode.textContent = report.available ? report.mode : "Not reported";
      panel.context.replaceChildren(
        stat("STRESS / 0-1", report.available && input.available ? value(input.stress) : "--",
          !report.available || !input.available ? "No input" : input.source + " / " + (fresh ? "fresh" : "stale") + " / " + age.toFixed(0) + "s"),
        stat("CONFIDENCE", report.available && input.available ? value(input.confidence) : "--",
          "Quality " + (report.available && input.available ? value(input.quality) : "--")),
        stat("POLICY UPDATES", report.available ? String(report.updates) : "--",
          report.available ? report.state + (report.transitioning ? " / transitioning" : "") : "Agent unavailable"));
      panel.footnote.textContent = "Normalized controls, not physical units. Change is since the previous report. Stress and controls share a 0-1 scale; gaps indicate missing or stale evidence.";
      panel.body.replaceChildren(...dimensions[agent].map((name, index) => {
        const row = element("tr"); row.append(element("td", name));
        const values = report.available
          ? [value(report.applied[index]), value(report.target[index]), delta(variableChange(data.samples, agent, index)),
            decision.available ? delta(decision.proposedDelta[index]) : "--", decision.available ? delta(decision.safeDelta[index]) : "--"]
          : ["--", "--", "--", "--", "--"];
        values.forEach(v => row.append(element("td", v))); return row;
      }));
      panel.decision.replaceChildren(
        element("p", report.available && decision.available
          ? "Last decision #" + decision.sequence + " at " + time(decision.at) + " / stress " + value(decision.stress)
          : "No decision reported."),
        element("p", report.available && decision.available
          ? decision.proposed + " -> " + decision.executed + " / " + decision.safety + " / " + decision.result
          : "Proposed and safe deltas are unavailable."),
        element("p", report.available && reward.available
          ? "Last outcome #" + (reward.sequence || "?") + " at " + time(reward.at) + ": " + (reward.valid ? value(reward.total) : "invalid") + " / " + reward.status
          : "No delayed reward reported."));
    }
    const events = eventsFromSamples(samples);
    $("agent-sample-count").textContent = samples.length + " observations / latest 60 events";
    $("agent-events-body").replaceChildren(...events.map(event => {
      const row = element("tr");
      row.append(element("td", time(event.at)), element("td", labels[event.agent]));
      row.append(element("td", "#" + (event.sequence || "?") + (event.type === "decision" ? " / stress " + value(event.stress) : " / delayed reward")));
      row.append(element("td", event.type === "decision" ? event.proposed + " -> " + event.executed : event.action));
      const detail = element("td", event.type === "decision" ? event.result : event.valid ? value(event.total) : "Invalid");
      detail.append(element("small", event.type === "decision" ? event.safety : event.status)); row.append(detail); return row;
    }));
    if (!events.length) {
      const row = element("tr"), cell = element("td", "No decisions or outcomes observed.");
      cell.colSpan = 5; row.append(cell); $("agent-events-body").append(row);
    }
    drawCharts();
  }

  function drawCharts() {
    if (!active() || !data?.samples.length) return;
    const samples = visibleSamples();
    const end = data.observedAt, start = Math.min(samples[0]?.receivedAt ?? end, end - 30);
    for (const agent of ["visual", "audio"]) {
      const canvas = panels[agent].canvas, width = canvas.clientWidth, height = canvas.clientHeight;
      if (!width) continue;
      const ratio = window.devicePixelRatio || 1;
      canvas.width = Math.round(width * ratio); canvas.height = Math.round(height * ratio);
      const ctx = canvas.getContext("2d"); ctx.scale(ratio, ratio);
      const left = 34, right = width - 10, top = 12, bottom = height - 26;
      const x = t => left + (t - start) / Math.max(1, end - start) * (right - left);
      const y = v => bottom - v * (bottom - top);
      ctx.font = '10px Consolas, monospace';
      for (const tick of [0, .25, .5, .75, 1]) {
        ctx.strokeStyle = "#d7dfd7"; ctx.lineWidth = 1;
        ctx.beginPath(); ctx.moveTo(left, y(tick)); ctx.lineTo(right, y(tick)); ctx.stroke();
        ctx.fillStyle = "#677776"; ctx.fillText(tick.toFixed(2), 1, y(tick) + 3);
      }
      ctx.fillText(time(start), left, height - 6);
      const endText = time(end); ctx.fillText(endText, right - ctx.measureText(endText).width, height - 6);
      for (const [key, color, dash] of [["stress", "#b13e40", []], ["applied", "#006e61", []], ["target", "#426db3", [5, 4]]]) {
        const points = series(samples, agent, selected[agent], key);
        let last = null; ctx.strokeStyle = color; ctx.lineWidth = 2; ctx.setLineDash(dash); ctx.beginPath();
        for (const point of points) {
          if (!finite(point.value)) { last = null; continue; }
          if (last && point.at - last.at <= 15) ctx.lineTo(x(point.at), y(point.value));
          else ctx.moveTo(x(point.at), y(point.value));
          last = point;
        }
        ctx.stroke(); ctx.setLineDash([]);
        // A single observation is a point, never a fabricated trend.
        ctx.fillStyle = color;
        for (const point of points) if (finite(point.value)) {
          ctx.beginPath(); ctx.arc(x(point.at), y(point.value), 2, 0, Math.PI * 2); ctx.fill();
        }
      }
      canvas.setAttribute("aria-label", labels[agent] + ": stress and " + dimensions[agent][selected[agent]] + ", applied and target, 0 to 1. Latest values are in the table below.");
    }
  }
  async function refresh() {
    if (!active() || !session || !$("agent-live").checked || request) return;
    const label = session, controller = new AbortController(); request = controller;
    const timeout = setTimeout(() => controller.abort(), 5000);
    try {
      const response = await fetch("/api/agents?session=" + encodeURIComponent(label), {cache: "no-store", signal: controller.signal});
      if (!response.ok) throw new Error("Agent feed unavailable");
      const received = await response.json();
      if (received.schemaVersion !== "mindsync-agents-v1" || received.session !== label || !Array.isArray(received.samples)
          || !finite(received.observedAt) || Math.abs(Date.now() / 1000 - received.observedAt) > 15) throw new Error("Stale feed");
      if (session !== label || request !== controller || !$("agent-live").checked) return;
      data = received; offline = false; render();
    } catch {
      if (session === label && request === controller && $("agent-live").checked) { offline = true; render(); }
    } finally {
      clearTimeout(timeout);
      if (request === controller) request = null;
    }
  }
  $("agent-live").onchange = () => { render(); if ($("agent-live").checked) refresh(); };
  $("agent-range").onchange = render;
  new ResizeObserver(drawCharts).observe($("agent-panels"));
  window.agentMonitor = {
    select: selectSession, refresh,
    offline: () => { offline = true; render(); }
  };
  if (location.hash === "#agents") setView("agents");
})();
