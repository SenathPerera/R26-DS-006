"use strict";
const $ = (id) => document.getElementById(id);
const labels = {unknown: "Not reported", active: "Active", ready: "Ready", waiting: "Waiting", blocked: "Blocked", stale: "Stale", complete: "Complete"};
let snapshot, selectedStage, busy = false, previous = new Map(), events = [], lastGood = 0, optionKey = "";
function node(tag, text, className) {
  const el = document.createElement(tag);
  if (text !== undefined) el.textContent = text;
  if (className) el.className = className;
  return el;
}
function badge(status) { return node("span", labels[status] || status, "badge " + status); }
function shown(value) { return value === undefined || value === null ? "Not reported" : typeof value === "boolean" ? (value ? "Yes" : "No") : String(value); }
function renderDetails() {
  const stage = snapshot?.stages.find(s => s.id === selectedStage);
  if (!stage) return;
  $("detail-title").textContent = stage.title;
  const dl = node("dl");
  for (const [key, value] of Object.entries(stage.evidence)) dl.append(node("dt", key), node("dd", shown(value)));
  $("details").replaceChildren(badge(stage.status), node("p", stage.detail), dl);
}
function render(data) {
  if (snapshot && snapshot.session !== data.session) { previous.clear(); events = []; }
  snapshot = data;
  window.agentMonitor.select(data.session);
  window.agentMonitor.refresh();
  $("backend-count").textContent = data.services.filter(s => s.reachable).length + " / " + data.services.length;
  $("stage-count").textContent = data.stages.filter(s => ["active", "ready"].includes(s.status)).length + " / " + data.stages.length;
  $("alert-count").textContent = data.alerts.length;
  $("incident-total").textContent = data.alerts.length;
  const key = JSON.stringify(data.sessions);
  if (key !== optionKey) {
    optionKey = key;
    const value = $("session").value;
    const options = [new Option("Latest connected session", ""), ...data.sessions.map(s => new Option(s.label + " / " + s.phase, s.label))];
    $("session").replaceChildren(...options);
    if (options.some(o => o.value === value)) $("session").value = value;
  }
  $("services").replaceChildren(...data.services.map(service => {
    const card = node("article", undefined, "service");
    const head = node("div", undefined, "service-head");
    head.append(node("h3", service.name), badge(service.reachable ? "ready" : "blocked"));
    card.append(head, node("p", service.url), node("p", service.reachable ? "Health " + service.latencyMs + " ms / " + (service.diagnostics ? "Diagnostics available" : "Restart for diagnostics") : "No valid health response"));
    if (service.id === "lyria" && service.reachable) {
      const generation = service.diagnostics?.operations?.generateClip;
      card.append(node("p", "Key configured: " + shown(service.health.apiKeyConfigured) + " / SDK: " + shown(service.health.sdkReady)));
      if (generation) card.append(node("p", "Clips: " + generation.successes + " succeeded / " + generation.failures + " failed / " + generation.inflight + " in progress"));
    }
    return card;
  }));
  const groups = new Map();
  for (const stage of data.stages) {
    if (!groups.has(stage.group)) groups.set(stage.group, []);
    groups.get(stage.group).push(stage);
    const prior = previous.get(stage.id);
    if (prior && prior !== stage.status) events.unshift({time: new Date().toLocaleTimeString(), text: stage.title + ": " + labels[prior] + " -> " + labels[stage.status]});
    previous.set(stage.id, stage.status);
  }
  events = events.slice(0, 40);
  const focused = document.activeElement?.dataset?.stage;
  $("flow").replaceChildren(...Array.from(groups, ([group, stages]) => {
    const row = node("div", undefined, "flow-row"), phase = node("div", undefined, "phase-label"), grid = node("div", undefined, "stage-grid");
    const [number, title] = group.split(" / ");
    phase.append(node("strong", number), node("span", title));
    for (const stage of stages) {
      const button = node("button", undefined, "stage " + stage.status + (stage.id === selectedStage ? " selected" : ""));
      button.type = "button"; button.dataset.stage = stage.id;
      button.setAttribute("aria-pressed", String(stage.id === selectedStage));
      button.append(node("h3", stage.title), badge(stage.status), node("p", stage.detail));
      button.onclick = () => {
        selectedStage = stage.id;
        document.querySelectorAll(".stage").forEach(el => { const selected = el.dataset.stage === selectedStage; el.classList.toggle("selected", selected); el.setAttribute("aria-pressed", String(selected)); });
        renderDetails();
      };
      grid.append(button);
    }
    row.append(phase, grid); return row;
  }));
  if (focused) document.querySelector('[data-stage="' + focused + '"]')?.focus({preventScroll: true});
  $("alerts").replaceChildren(...(data.alerts.length ? data.alerts.map(alert => {
    const item = node("article", undefined, "incident " + alert.severity);
    item.append(node("h3", alert.title), node("p", alert.detail)); return item;
  }) : [node("p", "No detected faults. Complete a device test to verify end-to-end behaviour.", "muted")]));
  const addresses = node("p", "PC addresses: " + (data.network.localAddresses?.join(", ") || "Not reported"), "muted");
  $("network").replaceChildren(addresses, ...(data.network.configuredHosts || []).map(entry => {
    const row = node("div", undefined, "network-row"); row.append(node("strong", entry.host), node("code", entry.source)); return row;
  }));
  renderDetails(); renderTimeline();
}
function renderTimeline() {
  $("timeline").replaceChildren(...(events.length ? events.map(event => {
    const li = node("li"); li.append(node("time", event.time), document.createTextNode(event.text)); return li;
  }) : [node("li", "No transitions observed yet.")]));
}
async function refresh() {
  if (busy) return;
  busy = true; $("refresh").disabled = true;
  const requestedSession = $("session").value;
  try {
    const response = await fetch("/api/snapshot?session=" + encodeURIComponent(requestedSession), {signal: AbortSignal.timeout(5000), cache: "no-store"});
    if (!response.ok) throw new Error("Monitor unavailable");
    const data = await response.json();
    if ($("session").value !== requestedSession) return;
    if (!data.checkedAt || Date.now() / 1000 - data.checkedAt > 15) throw new Error("Collector stale");
    render(data); lastGood = Date.now();
    $("connection-error").hidden = true; document.body.classList.remove("monitor-stale");
    $("live-state").textContent = "LIVE / EVIDENCE RECEIVED";
    $("last-update").textContent = "Checked " + new Date(data.checkedAt * 1000).toLocaleTimeString();
  } catch {
    if ($("session").value !== requestedSession) return;
    window.agentMonitor.offline();
    $("connection-error").hidden = false; document.body.classList.add("monitor-stale");
    $("live-state").textContent = "MONITOR OFFLINE / HISTORICAL VIEW";
    $("last-update").textContent = lastGood ? "Last received " + new Date(lastGood).toLocaleTimeString() : "No evidence received";
  } finally {
    busy = false; $("refresh").disabled = false;
    if ($("session").value !== requestedSession) refresh();
  }
}
$("refresh").onclick = refresh;
$("session").onchange = () => { previous.clear(); events = []; window.agentMonitor.select(null); refresh(); };
$("clear").onclick = () => { events = []; renderTimeline(); };
refresh(); setInterval(refresh, 3000);
