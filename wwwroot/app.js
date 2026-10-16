(() => {
  "use strict";

  const PAGE_SIZE = 12;
  const REQUIRED = ["#", "sesion", "areas", "organo jurisdiccional", "contacto", "cto", "tema", "tipo", "detalle tema", "acuerdos", "seguimiento", "estatus"];
  const state = { datasets: {}, system: null, rows: [], filtered: [], page: 1, sortKey: "id", sortDir: "asc" };
  const $ = (id) => document.getElementById(id);
  const esc = (v) => String(v ?? "").replace(/[&<>'"]/g, (c) => ({"&":"&amp;","<":"&lt;",">":"&gt;","'":"&#39;",'"':"&quot;"}[c]));
  const norm = (v) => String(v ?? "").normalize("NFD").replace(/[\u0300-\u036f]/g, "").replace(/\s+/g, " ").trim().toLowerCase();
  const slug = (v) => norm(v).replace(/[^a-z0-9]+/g, "-").replace(/(^-|-$)/g, "") || "sin-estatus";
  const statusColor = {"Atendida":"#78C9C6","Atendida con explicación":"#78C9C6","En análisis":"#868C9A","En proceso":"#575F71","Inviable":"#EE848E","Pendiente OJ":"#EE848E","Sin estatus":"#9FA0A8"};

  function columnIndex(ref) {
    const letters = ref.replace(/\d/g, "");
    let n = 0;
    for (const ch of letters) n = n * 26 + ch.charCodeAt(0) - 64;
    return n - 1;
  }

  function excelDate(serial) {
    const utc = Math.round((Number(serial) - 25569) * 86400 * 1000);
    const d = new Date(utc);
    return Number.isNaN(d.getTime()) ? String(serial) : d.toISOString().slice(0, 10);
  }

  function formatDate(value) {
    if (!value) return "Sin fecha";
    const raw = /^\d{4}-\d{2}-\d{2}/.test(String(value)) ? String(value).slice(0, 10) : value;
    const d = new Date(`${raw}T12:00:00`);
    return Number.isNaN(d.getTime()) ? String(value) : new Intl.DateTimeFormat("es-MX", {day:"2-digit", month:"short", year:"numeric"}).format(d);
  }

  let requestSequence = 0;
  async function loadServerData() {
    const currentRequest = ++requestSequence;
    const notice = $("dashboardNotice");
    try {
      const response = await fetch("api/dashboard", { cache: "no-store", headers: { "Accept": "application/json" } });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      const payload = await response.json();
      if (currentRequest !== requestSequence) return;
      // ASP.NET Core aplica camelCase al serializar JSON: SISE -> sise, SIGE -> sige.
      // Aceptar ambas convenciones y validar la respuesta antes de actualizar la pantalla.
      const datasets = payload.datasets ?? payload.Datasets ?? {};
      const sise = datasets.SISE ?? datasets.sise;
      const sige = datasets.SIGE ?? datasets.sige;
      if (!Array.isArray(sise) || !Array.isArray(sige)) {
        throw new Error("La API no devolvió los arreglos SISE/SIGE esperados. Revise /api/dashboard.");
      }
      state.datasets = { SISE: sise, SIGE: sige };
      const total = state.datasets.SISE.length + state.datasets.SIGE.length;
      document.querySelectorAll(".compact-system-button").forEach((button) => {
        button.disabled = !state.datasets[button.dataset.system]?.length;
      });
      if (total) {
        notice.hidden = true;
        selectSystem(state.datasets[state.system]?.length ? state.system : (state.datasets.SISE.length ? "SISE" : "SIGE"));
      } else {
        selectSystem("SISE");
        notice.hidden = false;
        notice.className = "dashboard-notice warning";
        notice.textContent = "No se recibieron registros desde /api/dashboard. Verifique la respuesta del servicio y consulte /api/meta.";
      }
    } catch (error) {
      if (currentRequest !== requestSequence) return;
      notice.hidden = false;
      notice.className = "dashboard-notice warning";
      notice.textContent = `Error al consultar el tablero: ${error.message}. Compruebe /api/dashboard y /api/meta.`;
    }
  }

  function unique(field, splitLines = false) {
    const values = state.rows.flatMap((r) => splitLines ? r[field].split(/\n+/) : [r[field]]).map((v) => v.trim()).filter(Boolean);
    return [...new Set(values)].sort((a,b) => a.localeCompare(b, "es", {numeric:true}));
  }

  function fillSelect(id, values, formatter = (v) => v) {
    const select = $(id); const keep = select.options[0]; select.innerHTML = ""; select.append(keep);
    values.forEach((v) => select.insertAdjacentHTML("beforeend", `<option value="${esc(v)}">${esc(formatter(v))}</option>`));
  }

  function populateFilters() {
    fillSelect("filterStatus", unique("status"));
    fillSelect("filterCircuit", unique("circuit"), (v) => `${v}° Circuito`);
    fillSelect("filterTopic", unique("topic"));
    fillSelect("filterArea", unique("areas", true));
    fillSelect("filterType", unique("type"));
    fillSelect("filterSession", unique("session").sort().reverse(), formatDate);
  }

  function isAttended(r) { return norm(r.status).startsWith("atendida"); }
  function isPending(r) { return !isAttended(r) && norm(r.status) !== "inviable"; }
  function countBy(rows, field) { return rows.reduce((acc, r) => (acc[r[field] || "Sin dato"] = (acc[r[field] || "Sin dato"] || 0) + 1, acc), {}); }

  function renderMetrics() {
    const total = state.rows.length, done = state.rows.filter(isAttended).length;
    $("metricTotal").textContent = total;
    $("metricSessions").textContent = `${unique("session").length} ${unique("session").length === 1 ? "sesión" : "sesiones"}`;
    $("metricDone").textContent = done;
    $("metricDoneRate").textContent = `${total ? Math.round(done / total * 100) : 0}% del total`;
    $("metricPending").textContent = state.rows.filter(isPending).length;
    $("metricInviable").textContent = state.rows.filter((r) => r.status === "Inviable").length;
  }

  function renderCharts() {
    const counts = countBy(state.filtered, "status");
    const preferred = ["Atendida", "Atendida con explicación", "En análisis", "En proceso", "Pendiente OJ", "Inviable", "Sin estatus"];
    const statusOrder = [...new Set([...preferred, ...Object.keys(counts)])];
    const maxStatus = Math.max(1, ...Object.values(counts));
    $("statusChart").innerHTML = statusOrder.filter((s) => counts[s]).map((s) => `<div class="status-row"><span class="status-name">${esc(s)}</span><span class="track"><span style="width:${counts[s]/maxStatus*100}%;background:${statusColor[s] || "#899198"}"></span></span><strong>${counts[s]}</strong></div>`).join("") || "<p>Sin resultados.</p>";
    $("statusScope").textContent = state.filtered.length === state.rows.length ? "Todos los registros" : `${state.filtered.length} filtrados`;
    const topics = Object.entries(countBy(state.filtered, "topic")).sort((a,b) => b[1]-a[1] || a[0].localeCompare(b[0], "es")).slice(0,6);
    const maxTopic = Math.max(1, ...topics.map((x) => x[1]));
    $("topicChart").innerHTML = topics.map(([name, value]) => `<div class="bar-row" title="${esc(name)}"><span class="bar-label">${esc(name)}</span><span class="track"><span style="width:${value/maxTopic*100}%"></span></span><strong>${value}</strong></div>`).join("") || "<p>Sin resultados.</p>";
  }

  function renderPriority() {
    const rows = state.rows.filter(isPending).sort((a,b) => (a.session || "").localeCompare(b.session || "")).slice(0,5);
    $("priorityList").innerHTML = rows.map((r) => `<button type="button" class="priority-item" data-uid="${esc(r.uid)}"><strong>${esc(r.contact || "Sin contacto")}</strong><span class="status-pill status-${slug(r.status)}">${esc(r.status)}</span><p>${esc(r.topic)} · ${esc(r.request || "Sin detalle")}</p></button>`).join("") || "<p>No hay asuntos abiertos.</p>";
  }

  function applyFilters(resetPage = true) {
    const query = norm($("search").value);
    const status = $("filterStatus").value, circuit = $("filterCircuit").value, topic = $("filterTopic").value;
    const area = $("filterArea").value, type = $("filterType").value, session = $("filterSession").value;
    state.filtered = state.rows.filter((r) => {
      const searchable = norm(Object.values(r).join(" "));
      return (!query || searchable.includes(query)) && (!status || r.status === status) && (!circuit || r.circuit === circuit) && (!topic || r.topic === topic) && (!area || r.areas.split(/\n+/).includes(area)) && (!type || r.type === type) && (!session || r.session === session);
    });
    const direction = state.sortDir === "asc" ? 1 : -1;
    state.filtered.sort((a,b) => String(a[state.sortKey] ?? "").localeCompare(String(b[state.sortKey] ?? ""), "es", {numeric:true, sensitivity:"base"}) * direction);
    if (resetPage) state.page = 1;
    renderTable(); renderCharts();
  }

  function renderTable() {
    const pages = Math.max(1, Math.ceil(state.filtered.length / PAGE_SIZE));
    state.page = Math.min(state.page, pages);
    const start = (state.page - 1) * PAGE_SIZE;
    const rows = state.filtered.slice(start, start + PAGE_SIZE);
    $("tableBody").innerHTML = rows.map((r) => `<tr data-uid="${esc(r.uid)}">
      <td>${esc(r.id)}</td><td>${esc(formatDate(r.session))}</td>
      <td><span class="cell-primary">${esc(r.contact || "Sin contacto")}</span><span class="cell-secondary">${esc(r.organ || "Sin órgano")}</span></td>
      <td>${esc(r.circuit ? `${r.circuit}°` : "—")}</td><td>${esc(r.topic || "—")}</td>
      <td class="request-cell"><span class="truncate-3">${esc(r.request || "Sin detalle")}</span></td>
      <td><span class="status-pill status-${slug(r.status)}">${esc(r.status)}</span></td>
      <td><button class="row-open" type="button" aria-label="Abrir detalle">›</button></td></tr>`).join("") || `<tr><td colspan="8">No hay registros que coincidan con los filtros.</td></tr>`;
    $("resultCount").textContent = `${state.filtered.length} ${state.filtered.length === 1 ? "resultado" : "resultados"}`;
    $("pageInfo").textContent = `Página ${state.page} de ${pages}`;
    $("prevPage").disabled = state.page <= 1; $("nextPage").disabled = state.page >= pages;
    document.querySelectorAll("th button[data-sort]").forEach((b) => {
      b.classList.remove("sorted-asc", "sorted-desc");
      if (b.dataset.sort === state.sortKey) b.classList.add(state.sortDir === "asc" ? "sorted-asc" : "sorted-desc");
    });
  }

  function openDetail(uid) {
    const r = state.rows.find((x) => x.uid === uid); if (!r) return;
    $("detailTitle").textContent = `${r.type || "Registro"} ${r.id ? `#${r.id}` : ""}`;
    const block = (title, text) => `<section class="detail-block ${text ? "" : "empty"}"><h3>${esc(title)}</h3><p>${esc(text || "Sin información registrada")}</p></section>`;
    $("drawerContent").innerHTML = `<div class="drawer-summary">
      <div><span>Estatus</span><strong><span class="status-pill status-${slug(r.status)}">${esc(r.status)}</span></strong></div>
      <div><span>Sesión</span><strong>${esc(formatDate(r.session))}</strong></div>
      <div><span>Contacto</span><strong>${esc(r.contact || "Sin contacto")}</strong></div>
      <div><span>Circuito</span><strong>${esc(r.circuit ? `${r.circuit}° Circuito` : "Sin dato")}</strong></div>
      <div><span>Área responsable</span><strong>${esc(r.areas.replace(/\n+/g, ", ") || "Sin dato")}</strong></div>
      <div><span>Tema</span><strong>${esc(r.topic || "Sin tema")}</strong></div>
    </div>${block("Órgano jurisdiccional", r.organ)}${block("Qué pidió", r.request)}${block("Acuerdos", r.agreements)}${block("Seguimiento", r.followup)}`;
    $("drawerBackdrop").hidden = false; $("detailDrawer").classList.add("open"); $("detailDrawer").setAttribute("aria-hidden", "false");
  }

  function closeDetail() { $("detailDrawer").classList.remove("open"); $("detailDrawer").setAttribute("aria-hidden", "true"); setTimeout(() => $("drawerBackdrop").hidden = true, 180); }

  function resetFilters() {
    $("search").value = "";
    ["filterStatus","filterCircuit","filterTopic","filterArea","filterType","filterSession"].forEach((id) => $(id).value = "");
    applyFilters();
  }

  function csvValue(value) { return `"${String(value ?? "").replace(/"/g, '""')}"`; }
  function exportCsv() {
    const headers = ["#","Sesión","Áreas","Órgano Jurisdiccional","Contacto","Cto","Tema","Tipo","Detalle tema","Acuerdos","Seguimiento","Estatus"];
    const fields = ["id","session","areas","organ","contact","circuit","topic","type","request","agreements","followup","status"];
    const csv = "\ufeff" + [headers.map(csvValue).join(","), ...state.filtered.map((r) => fields.map((f) => csvValue(r[f])).join(","))].join("\r\n");
    const a = document.createElement("a"); a.href = URL.createObjectURL(new Blob([csv], {type:"text/csv;charset=utf-8"}));
    a.download = `seguimiento_${(state.system || "sistema").toLowerCase()}_filtrado_${new Date().toISOString().slice(0,10)}.csv`; a.click(); setTimeout(() => URL.revokeObjectURL(a.href), 1000);
  }

  function selectSystem(system) {
    const rows = state.datasets[system] || [];
    closeDetail();
    state.system = system; state.rows = rows; state.page = 1; state.sortKey = "id"; state.sortDir = "asc";
    document.querySelectorAll(".system-button").forEach((button) => {
      const active = button.dataset.system === system;
      button.classList.toggle("active", active);
      button.setAttribute("aria-pressed", active ? "true" : "false");
    });
    document.querySelectorAll(".compact-system-button").forEach((button) => {
      const active = button.dataset.system === system;
      button.classList.toggle("active", active);
      button.setAttribute("aria-pressed", active ? "true" : "false");
    });
    $("activeSystem").textContent = system;
    $("dashboard").hidden = false;
    resetFilters(); populateFilters(); renderMetrics(); renderPriority(); applyFilters();
  }



  $("dashboardSystemSwitcher").addEventListener("click", (e) => { const button = e.target.closest("[data-system]"); if (button && !button.disabled) selectSystem(button.dataset.system); });
  $("search").addEventListener("input", () => applyFilters());
  ["filterStatus","filterCircuit","filterTopic","filterArea","filterType","filterSession"].forEach((id) => $(id).addEventListener("change", () => applyFilters()));
  $("clearFilters").addEventListener("click", resetFilters);
  $("showPending").addEventListener("click", () => { resetFilters(); $("filterStatus").value = ""; state.filtered = state.rows.filter(isPending); state.page = 1; renderTable(); renderCharts(); document.querySelector(".records-panel").scrollIntoView({behavior:"smooth"}); });
  $("exportCsv").addEventListener("click", exportCsv);
  $("prevPage").addEventListener("click", () => { state.page--; renderTable(); }); $("nextPage").addEventListener("click", () => { state.page++; renderTable(); });
  document.querySelector("thead").addEventListener("click", (e) => { const b = e.target.closest("button[data-sort]"); if (!b) return; const key = b.dataset.sort; state.sortDir = state.sortKey === key && state.sortDir === "asc" ? "desc" : "asc"; state.sortKey = key; applyFilters(false); });
  $("tableBody").addEventListener("click", (e) => { const tr = e.target.closest("tr[data-uid]"); if (tr) openDetail(tr.dataset.uid); });
  $("priorityList").addEventListener("click", (e) => { const b = e.target.closest("[data-uid]"); if (b) openDetail(b.dataset.uid); });
  document.querySelector(".metrics").addEventListener("click", (e) => { const b = e.target.closest("[data-metric-filter]"); if (!b) return; resetFilters(); const value = b.dataset.metricFilter; if (value === "all") return; if (value === "pending") { state.filtered = state.rows.filter(isPending); state.page = 1; renderTable(); renderCharts(); } else if (value === "attended") { state.filtered = state.rows.filter(isAttended); state.page = 1; renderTable(); renderCharts(); } else { $("filterStatus").value = value; applyFilters(); } document.querySelector(".records-panel").scrollIntoView({behavior:"smooth"}); });
  $("closeDrawer").addEventListener("click", closeDetail); $("drawerBackdrop").addEventListener("click", closeDetail); document.addEventListener("keydown", (e) => e.key === "Escape" && closeDetail());
  // Mostrar el tablero desde el primer render, incluso si el servicio aún no responde.
  selectSystem("SISE");
  loadServerData();
  window.addEventListener("pageshow", () => loadServerData());
  document.addEventListener("visibilitychange", () => { if (!document.hidden) loadServerData(); });
})();
