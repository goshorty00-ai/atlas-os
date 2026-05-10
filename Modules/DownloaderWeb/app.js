/* global chrome */

const state = {
  downloads: [],
  settings: null,
  outputFolder: "",
  providerStatus: {},
  filter: "all",
  selectedId: null,
  ctxId: null,
  selectedIds: [],
  lastSelectedIndex: -1
};

function hasBridge() {
  return typeof window !== "undefined" && window.chrome && window.chrome.webview;
}

function post(type, payload) {
  const msg = { type, payload: payload ?? {} };
  if (hasBridge()) {
    window.chrome.webview.postMessage(msg);
  } else {
    console.log("[dev] post", msg);
  }
}

function formatBytes(n) {
  if (!Number.isFinite(n) || n <= 0) return "0 B";
  const units = ["B", "KB", "MB", "GB", "TB"];
  let u = 0;
  let v = n;
  while (v >= 1024 && u < units.length - 1) {
    v /= 1024;
    u++;
  }
  return `${v.toFixed(u === 0 ? 0 : 2)} ${units[u]}`;
}

function formatSpeed(n) {
  return `${formatBytes(n)}/s`;
}

function formatEtaSeconds(sec) {
  if (!Number.isFinite(sec) || sec <= 0) return "—";
  const s = Math.floor(sec % 60);
  const m = Math.floor((sec / 60) % 60);
  const h = Math.floor(sec / 3600);
  if (h > 0) return `${h}h ${m}m`;
  if (m > 0) return `${m}m ${s}s`;
  return `${s}s`;
}

function toast(text, kind) {
  let host = document.getElementById("toastHost");
  if (!host) {
    host = document.createElement("div");
    host.className = "toastHost";
    host.id = "toastHost";
    document.body.appendChild(host);
  }
  const el = document.createElement("div");
  el.className = `toast ${kind || ""}`.trim();
  el.textContent = text;
  host.appendChild(el);
  setTimeout(() => {
    el.style.opacity = "0";
    el.style.transform = "translateY(8px)";
    el.style.transition = "opacity .2s ease, transform .2s ease";
    setTimeout(() => el.remove(), 220);
  }, 2600);
}

async function copyText(text) {
  const t = (text || "").toString();
  if (!t) return false;
  try {
    if (navigator.clipboard && navigator.clipboard.writeText) {
      await navigator.clipboard.writeText(t);
      return true;
    }
  } catch {
  }
  try {
    const ta = document.createElement("textarea");
    ta.value = t;
    ta.style.position = "fixed";
    ta.style.left = "-9999px";
    ta.style.top = "0";
    document.body.appendChild(ta);
    ta.focus();
    ta.select();
    const ok = document.execCommand("copy");
    ta.remove();
    return ok;
  } catch {
  }
  return false;
}

function statusBadgeClass(status) {
  switch ((status || "").toLowerCase()) {
    case "downloading":
      return "cyan";
    case "queued":
    case "resolving":
    case "converting":
      return "warn";
    case "completed":
      return "ok";
    case "error":
    case "cancelled":
      return "err";
    case "paused":
      return "warn";
    default:
      return "";
  }
}

function renderStats() {
  const active = state.downloads.filter(d => d.status === "Downloading" || d.status === "Resolving" || d.status === "Converting").length;
  const queued = state.downloads.filter(d => d.status === "Queued").length;
  const completed = state.downloads.filter(d => d.status === "Completed").length;
  const speed = state.downloads
    .filter(d => d.status === "Downloading" || d.status === "Resolving")
    .reduce((sum, d) => sum + (d.speedBps || 0), 0);

  document.getElementById("statActive").textContent = String(active);
  document.getElementById("statQueued").textContent = String(queued);
  document.getElementById("statCompleted").textContent = String(completed);
  document.getElementById("statSpeed").textContent = formatSpeed(speed);
  const totalEl = document.getElementById("statQueuedTotal");
  if (totalEl) totalEl.textContent = String(state.downloads.length);

  const a = document.getElementById("statCardActive");
  const q = document.getElementById("statCardQueued");
  const c = document.getElementById("statCardCompleted");
  const s = document.getElementById("statCardSpeed");
  if (a) a.classList.toggle("isSelected", state.filter === "active");
  if (q) q.classList.toggle("isSelected", state.filter === "queued");
  if (c) c.classList.toggle("isSelected", state.filter === "completed");
  if (s) s.classList.toggle("isSelected", state.filter === "all");
}

function statusRank(status) {
  switch ((status || "").toLowerCase()) {
    case "downloading":
    case "resolving":
    case "converting":
      return 0;
    case "queued":
      return 1;
    case "paused":
      return 2;
    case "error":
      return 3;
    case "completed":
      return 4;
    case "cancelled":
      return 5;
    default:
      return 9;
  }
}

function getVisibleDownloads() {
  let list = state.downloads.slice();
  if (state.filter === "active") {
    list = list.filter(d => d.status === "Downloading" || d.status === "Resolving" || d.status === "Converting");
  } else if (state.filter === "queued") {
    list = list.filter(d => d.status === "Queued" || d.status === "Paused" || d.status === "Error");
  } else if (state.filter === "completed") {
    list = list.filter(d => d.status === "Completed");
  }

  list.sort((a, b) => {
    const ra = statusRank(a.status);
    const rb = statusRank(b.status);
    if (ra !== rb) return ra - rb;
    const ta = Date.parse(a.createdUtc || "") || 0;
    const tb = Date.parse(b.createdUtc || "") || 0;
    return tb - ta;
  });
  return list;
}

function renderList() {
  const body = document.getElementById("listBody");
  body.innerHTML = "";
  const empty = document.getElementById("emptyState");
  const visible = getVisibleDownloads();
  empty.classList.toggle("hidden", visible.length !== 0);

  let orderIndex = 0;
  for (const d of visible) {
    const idx = orderIndex++;
    const row = document.createElement("div");
    const selectedSet = new Set(state.selectedIds || []);
    const isSelected = selectedSet.has(d.id) || state.selectedId === d.id;
    row.className = `rowItem ${isSelected ? "isSelected" : ""}`.trim();
    row.tabIndex = 0;
    row.onmousedown = (ev) => {
      if (ev.button === 0) {
        const mod = ev.ctrlKey || ev.metaKey;
        const shift = ev.shiftKey;
        const ids = visible.map(x => x.id);
        let set = new Set(state.selectedIds || []);

        if (shift && state.lastSelectedIndex >= 0 && state.lastSelectedIndex < ids.length) {
          const a = Math.min(state.lastSelectedIndex, idx);
          const b = Math.max(state.lastSelectedIndex, idx);
          if (!mod) set = new Set();
          for (let i = a; i <= b; i++) set.add(ids[i]);
        } else if (mod) {
          if (set.has(d.id)) set.delete(d.id);
          else set.add(d.id);
          state.lastSelectedIndex = idx;
        } else {
          set = new Set([d.id]);
          state.lastSelectedIndex = idx;
        }

        state.selectedIds = Array.from(set);
        state.selectedId = d.id;
        hideContextMenu();
        renderAll();
      }
    };
    row.ondblclick = () => post("downloader.openFolder", { id: d.id });
    row.oncontextmenu = (ev) => {
      ev.preventDefault();
      const set = new Set(state.selectedIds || []);
      if (!set.has(d.id)) {
        state.selectedIds = [d.id];
        state.selectedId = d.id;
        state.lastSelectedIndex = idx;
        renderAll();
      }
      showContextMenu(ev.clientX, ev.clientY, d, visible);
    };

    const fileCol = document.createElement("div");
    const name = document.createElement("div");
    name.className = "fileName";
    name.textContent = d.filename || d.url || "(unknown)";
    const sub = document.createElement("div");
    sub.className = "subText";
    const err = (d.error || "").trim();
    if (err) sub.textContent = err;
    else sub.textContent = d.resolver ? `via ${d.resolver}` : (d.url || "");
    fileCol.appendChild(name);
    fileCol.appendChild(sub);

    const statusCol = document.createElement("div");
    const badge = document.createElement("span");
    badge.className = `badge ${statusBadgeClass(d.status)}`.trim();
    badge.textContent = d.status || "—";
    statusCol.appendChild(badge);

    const progCol = document.createElement("div");
    progCol.className = "progressWrap";
    const bar = document.createElement("div");
    bar.className = "progressBar";
    const fill = document.createElement("div");
    fill.className = "progressFill";
    const done = Number(d.bytesDownloaded || 0);
    const total = Number(d.totalBytes || 0);
    let pct = Number((d.progress01 ?? d.progress) || 0);
    if (Number.isFinite(pct) && pct > 1) pct = pct / 100;
    if ((!Number.isFinite(pct) || pct <= 0) && total > 0 && done > 0) pct = done / total;
    pct = Math.max(0, Math.min(1, Number.isFinite(pct) ? pct : 0));
    if (total <= 0 && (d.status === "Downloading" || d.status === "Resolving")) fill.classList.add("indeterminate");
    fill.style.width = `${(pct * 100).toFixed(3)}%`;
    bar.appendChild(fill);
    const ptxt = document.createElement("div");
    ptxt.className = "progressText";
    const left = document.createElement("div");
    const pct100 = pct * 100;
    left.textContent = pct100 < 10 ? `${pct100.toFixed(1)}%` : `${pct100.toFixed(0)}%`;
    const right = document.createElement("div");
    right.textContent = total > 0 ? `${formatBytes(done)} / ${formatBytes(total)}` : `${formatBytes(done)}`;
    ptxt.appendChild(left);
    ptxt.appendChild(right);
    progCol.appendChild(bar);
    progCol.appendChild(ptxt);

    const speedCol = document.createElement("div");
    speedCol.className = "monoSmall";
    speedCol.textContent = d.status === "Downloading" ? formatSpeed(d.speedBps || 0) : "—";

    const etaCol = document.createElement("div");
    etaCol.className = "monoSmall";
    etaCol.textContent = d.status === "Downloading" ? formatEtaSeconds(d.etaSeconds || 0) : "—";

    const actionsCol = document.createElement("div");
    actionsCol.className = "rowActions";

    const iconPlay = `<svg width="16" height="16" viewBox="0 0 24 24" fill="none"><path d="M9 6v12l10-6-10-6z" fill="rgba(226,232,240,.92)"/></svg>`;
    const iconPause = `<svg width="16" height="16" viewBox="0 0 24 24" fill="none"><path d="M7 6h3v12H7V6zm7 0h3v12h-3V6z" fill="rgba(226,232,240,.92)"/></svg>`;
    const iconTrash = `<svg width="16" height="16" viewBox="0 0 24 24" fill="none"><path d="M9 3h6l1 2h4v2H4V5h4l1-2z" stroke="rgba(226,232,240,.78)" stroke-width="1.6" stroke-linejoin="round"/><path d="M6 7l1 14h10l1-14" stroke="rgba(226,232,240,.78)" stroke-width="1.6" stroke-linejoin="round"/><path d="M10 11v6M14 11v6" stroke="rgba(34,211,238,.75)" stroke-width="1.6" stroke-linecap="round"/></svg>`;
    const iconFolder = `<svg width="16" height="16" viewBox="0 0 24 24" fill="none"><path d="M3 6a2 2 0 0 1 2-2h5l2 2h9a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6z" stroke="rgba(226,232,240,.78)" stroke-width="1.6" stroke-linejoin="round"/><path d="M3 10h18" stroke="rgba(34,211,238,.7)" stroke-width="1.6" stroke-linecap="round"/></svg>`;

    const fnLower = (d.filename || "").toLowerCase();
    const ext = fnLower.includes(".") ? fnLower.slice(fnLower.lastIndexOf(".")) : "";
    const isAudio = [".mp3", ".m4a", ".aac", ".wav", ".flac", ".ogg", ".wma"].includes(ext);
    const isVideo = [".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".m4v"].includes(ext);
    const canOpenMedia = d.status === "Completed" && (isAudio || isVideo);

    const btnPause = document.createElement("button");
    btnPause.className = "iconBtn";
    btnPause.title = d.status === "Paused" ? "Resume" : "Pause";
    btnPause.innerHTML = d.status === "Paused" ? iconPlay : iconPause;
    btnPause.onclick = (ev) => {
      ev.stopPropagation();
      if (d.status === "Paused") post("downloader.resume", { id: d.id });
      else post("downloader.pause", { id: d.id });
    };

    const btnMedia = document.createElement("button");
    btnMedia.className = "iconBtn";
    btnMedia.title = isVideo ? "Play (Media Centre)" : "Play";
    btnMedia.innerHTML = iconPlay;
    btnMedia.onclick = (ev) => {
      ev.stopPropagation();
      post("downloader.openMedia", { id: d.id });
    };

    const btnRemove = document.createElement("button");
    btnRemove.className = "iconBtn";
    btnRemove.title = "Remove";
    btnRemove.innerHTML = iconTrash;
    btnRemove.onclick = (ev) => {
      ev.stopPropagation();
      post("downloader.remove", { id: d.id });
    };

    const btnFolder = document.createElement("button");
    btnFolder.className = "iconBtn";
    btnFolder.title = "Open folder";
    btnFolder.innerHTML = iconFolder;
    btnFolder.onclick = (ev) => {
      ev.stopPropagation();
      post("downloader.openFolder", { id: d.id });
    };

    actionsCol.appendChild(canOpenMedia ? btnMedia : btnPause);
    actionsCol.appendChild(btnRemove);
    actionsCol.appendChild(btnFolder);

    row.appendChild(fileCol);
    row.appendChild(statusCol);
    row.appendChild(progCol);
    row.appendChild(speedCol);
    row.appendChild(etaCol);
    row.appendChild(actionsCol);

    body.appendChild(row);
  }
}

function renderAll() {
  renderStats();
  renderList();
  if (state.settings) {
    document.getElementById("settingParallel").value = String(state.settings.maxParallelDownloads || 3);
    document.getElementById("settingResolverMode").value = state.settings.resolverMode || "Auto";
    document.getElementById("rdEnabled").checked = !!state.settings.providers?.realDebrid?.enabled;
    document.getElementById("adEnabled").checked = !!state.settings.providers?.allDebrid?.enabled;
    document.getElementById("pmEnabled").checked = !!state.settings.providers?.premiumize?.enabled;
  }
  const outA = document.getElementById("outputFolder");
  if (outA) outA.textContent = state.outputFolder || "(default)";
  const outB = document.getElementById("outputFolderAddModal");
  if (outB) outB.textContent = state.outputFolder || "(default)";
}

function hideContextMenu() {
  const el = document.getElementById("ctxMenu");
  if (el) el.classList.add("hidden");
  state.ctxId = null;
}

async function readClipboardText() {
  try {
    if (navigator.clipboard && navigator.clipboard.readText) {
      const t = await navigator.clipboard.readText();
      return (t || "").trim();
    }
  } catch {
  }
  return "";
}

async function pasteUrlsIntoAddModal() {
  const txt = await readClipboardText();
  if (!txt) {
    toast("Clipboard paste not available. Use right-click → Paste.", "err");
    return;
  }
  showModal("addModal", true);
  const ta = document.getElementById("addUrlsText");
  if (ta) {
    const cur = (ta.value || "").trim();
    ta.value = cur ? `${cur}\n${txt}` : txt;
    ta.focus();
  }
}

function showContextMenu(x, y, d, visible) {
  const el = document.getElementById("ctxMenu");
  if (!el) return;

  state.ctxId = d?.id || null;
  const hasJob = !!d;
  const selected = new Set(state.selectedIds || []);
  const selectedJobs = hasJob ? (visible || []).filter(j => selected.has(j.id)) : [];
  const jobs = hasJob ? (selectedJobs.length > 0 ? selectedJobs : [d]) : [];

  const pasteBtn = document.getElementById("ctxPasteUrls");
  const addBtn = document.getElementById("ctxAddUrl");
  const pauseBtn = document.getElementById("ctxPause");
  const resumeBtn = document.getElementById("ctxResume");
  const retryBtn = document.getElementById("ctxRetry");
  const cancelBtn = document.getElementById("ctxCancel");
  const removeBtn = document.getElementById("ctxRemove");
  const openBtn = document.getElementById("ctxOpenFolder");
  const copyBtn = document.getElementById("ctxCopyUrl");

  if (pasteBtn) pasteBtn.style.display = hasJob ? "none" : "";
  if (addBtn) addBtn.style.display = hasJob ? "none" : "";
  if (pauseBtn) pauseBtn.style.display = hasJob ? "" : "none";
  if (resumeBtn) resumeBtn.style.display = hasJob ? "" : "none";
  if (retryBtn) retryBtn.style.display = hasJob ? "" : "none";
  if (cancelBtn) cancelBtn.style.display = hasJob ? "" : "none";
  if (removeBtn) removeBtn.style.display = hasJob ? "" : "none";
  if (openBtn) openBtn.style.display = hasJob ? "" : "none";
  if (copyBtn) copyBtn.style.display = hasJob ? "" : "none";

  if (pasteBtn) pasteBtn.onclick = async () => { await pasteUrlsIntoAddModal(); hideContextMenu(); };
  if (addBtn) addBtn.onclick = () => { showModal("addModal", true); hideContextMenu(); };

  const canPause = jobs.some(j => j.status === "Downloading" || j.status === "Resolving");
  const canResume = jobs.some(j => j.status === "Paused");
  const canRetry = jobs.some(j => j.status === "Error" || j.status === "Cancelled");
  const canCancel = jobs.some(j => j.status === "Downloading" || j.status === "Resolving" || j.status === "Queued" || j.status === "Paused");

  if (pauseBtn) pauseBtn.disabled = !canPause;
  if (resumeBtn) resumeBtn.disabled = !canResume;
  if (retryBtn) retryBtn.disabled = !canRetry;
  if (cancelBtn) cancelBtn.disabled = !canCancel;

  if (pauseBtn) pauseBtn.onclick = () => {
    for (const j of jobs) if (j.status === "Downloading" || j.status === "Resolving") post("downloader.pause", { id: j.id });
    hideContextMenu();
  };
  if (resumeBtn) resumeBtn.onclick = () => {
    for (const j of jobs) if (j.status === "Paused") post("downloader.resume", { id: j.id });
    hideContextMenu();
  };
  if (retryBtn) retryBtn.onclick = () => {
    for (const j of jobs) if (j.status === "Error" || j.status === "Cancelled") post("downloader.retry", { id: j.id });
    hideContextMenu();
  };
  if (cancelBtn) cancelBtn.onclick = () => {
    for (const j of jobs) if (j.status === "Downloading" || j.status === "Resolving" || j.status === "Queued" || j.status === "Paused") post("downloader.cancel", { id: j.id });
    hideContextMenu();
  };
  if (removeBtn) removeBtn.onclick = () => {
    for (const j of jobs) post("downloader.remove", { id: j.id });
    hideContextMenu();
  };
  if (openBtn && hasJob) openBtn.onclick = () => { post("downloader.openFolder", { id: d.id }); hideContextMenu(); };
  if (copyBtn && hasJob) copyBtn.onclick = async () => {
    const ok = await copyText(d?.url || "");
    toast(ok ? "Copied URL." : "Copy failed.", ok ? "ok" : "err");
    hideContextMenu();
  };

  el.classList.remove("hidden");
  const rect = el.getBoundingClientRect();
  const maxX = window.innerWidth - rect.width - 8;
  const maxY = window.innerHeight - rect.height - 8;
  el.style.left = `${Math.max(8, Math.min(x, maxX))}px`;
  el.style.top = `${Math.max(8, Math.min(y, maxY))}px`;
}

function showModal(id, on) {
  const el = document.getElementById(id);
  if (!el) return;
  el.classList.toggle("hidden", !on);
}

function wireUi() {
  try {
  document.addEventListener("mousedown", (e) => {
    const menu = document.getElementById("ctxMenu");
    if (!menu) return;
    if (menu.classList.contains("hidden")) return;
    if (!menu.contains(e.target)) hideContextMenu();
  });
  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape") hideContextMenu();
    if ((e.ctrlKey || e.metaKey) && (e.key === "a" || e.key === "A")) {
      const visible = getVisibleDownloads();
      state.selectedIds = visible.map(d => d.id);
      state.selectedId = state.selectedIds[0] || null;
      state.lastSelectedIndex = state.selectedIds.length ? state.selectedIds.length - 1 : -1;
      renderAll();
      e.preventDefault();
    }
  });

  const btnAdd = document.getElementById("btnAdd");
  if (btnAdd) btnAdd.onclick = () => showModal("addModal", true);
  const btnAdd2 = document.getElementById("btnAdd2");
  if (btnAdd2) btnAdd2.onclick = () => showModal("addModal", true);
  const btnSettings = document.getElementById("btnSettings");
  if (btnSettings) btnSettings.onclick = () => showModal("settingsModal", true);
  const btnImportCsv = document.getElementById("btnImportCsv");
  if (btnImportCsv) btnImportCsv.onclick = () => post("downloader.importCsv", {});
  const btnPauseAll = document.getElementById("btnPauseAll");
  if (btnPauseAll) btnPauseAll.onclick = () => post("downloader.pauseAll", {});
  const btnResumeAll = document.getElementById("btnResumeAll");
  if (btnResumeAll) btnResumeAll.onclick = () => post("downloader.resumeAll", {});
  const btnStopAll = document.getElementById("btnStopAll");
  if (btnStopAll) btnStopAll.onclick = () => post("downloader.stopAll", {});
  const btnClearFinished = document.getElementById("btnClearFinished");
  if (btnClearFinished) btnClearFinished.onclick = () => post("downloader.clearFinished", {});

  const btnSideClose = document.getElementById("btnSideClose");
  if (btnSideClose) btnSideClose.onclick = () => {
    const side = document.querySelector(".sidePanel");
    if (side) side.classList.toggle("hidden");
  };

  const setFilter = (f) => {
    state.filter = f;
    renderAll();
  };
  const statActive = document.getElementById("statCardActive");
  const statQueued = document.getElementById("statCardQueued");
  const statCompleted = document.getElementById("statCardCompleted");
  const statSpeed = document.getElementById("statCardSpeed");
  if (statActive) statActive.onclick = () => setFilter(state.filter === "active" ? "all" : "active");
  if (statQueued) statQueued.onclick = () => setFilter(state.filter === "queued" ? "all" : "queued");
  if (statCompleted) statCompleted.onclick = () => setFilter(state.filter === "completed" ? "all" : "completed");
  if (statSpeed) statSpeed.onclick = () => setFilter("all");

  const closeAdd = () => showModal("addModal", false);
  const addClose = document.getElementById("addModalClose");
  if (addClose) addClose.onclick = closeAdd;
  const addCancel = document.getElementById("addModalCancel");
  if (addCancel) addCancel.onclick = closeAdd;
  const addPaste = document.getElementById("addModalPaste");
  if (addPaste) addPaste.onclick = async () => await pasteUrlsIntoAddModal();
  const addStart = document.getElementById("addModalStart");
  if (addStart) addStart.onclick = () => {
    const raw = document.getElementById("addUrlsText").value || "";
    const urls = raw.split(/\r?\n/).map(x => x.trim()).filter(Boolean);
    if (urls.length === 0) {
      toast("Enter at least one URL.", "err");
      return;
    }
    const provider = document.getElementById("providerSelect").value || "Auto";
    post("downloader.addUrls", { urls, provider });
    state.filter = "queued";
    toast(`Queued ${urls.length} URL(s).`, "ok");
    renderAll();
    post("downloader.getState", {});
    document.getElementById("addUrlsText").value = "";
    closeAdd();
  };

  const closeSettings = () => showModal("settingsModal", false);
  const settingsClose = document.getElementById("settingsClose");
  if (settingsClose) settingsClose.onclick = closeSettings;
  const settingsCancel = document.getElementById("settingsCancel");
  if (settingsCancel) settingsCancel.onclick = closeSettings;

  const settingParallel = document.getElementById("settingParallel");
  if (settingParallel) settingParallel.onchange = () => {
    const v = Number(document.getElementById("settingParallel").value || 3);
    post("downloader.settings.set", { maxParallelDownloads: v });
  };
  const settingResolverMode = document.getElementById("settingResolverMode");
  if (settingResolverMode) settingResolverMode.onchange = () => {
    const v = document.getElementById("settingResolverMode").value || "Auto";
    post("downloader.settings.set", { resolverMode: v });
  };

  const rdSave = document.getElementById("rdSave");
  if (rdSave) rdSave.onclick = () => {
    post("downloader.settings.set", {
      providers: {
        realDebrid: {
          enabled: !!document.getElementById("rdEnabled").checked,
          token: document.getElementById("rdToken").value || ""
        }
      }
    });
    document.getElementById("rdToken").value = "";
    toast("Real-Debrid settings saved.", "ok");
  };
  const rdTest = document.getElementById("rdTest");
  if (rdTest) rdTest.onclick = () => post("downloader.provider.test", { provider: "RealDebrid" });

  const adSave = document.getElementById("adSave");
  if (adSave) adSave.onclick = () => {
    post("downloader.settings.set", {
      providers: {
        allDebrid: { enabled: !!document.getElementById("adEnabled").checked, token: document.getElementById("adToken").value || "" }
      }
    });
    document.getElementById("adToken").value = "";
    toast("AllDebrid settings saved.", "ok");
  };
  const adTest = document.getElementById("adTest");
  if (adTest) adTest.onclick = () => post("downloader.provider.test", { provider: "AllDebrid" });

  const pmSave = document.getElementById("pmSave");
  if (pmSave) pmSave.onclick = () => {
    post("downloader.settings.set", {
      providers: {
        premiumize: { enabled: !!document.getElementById("pmEnabled").checked, token: document.getElementById("pmToken").value || "" }
      }
    });
    document.getElementById("pmToken").value = "";
    toast("Premiumize settings saved.", "ok");
  };
  const pmTest = document.getElementById("pmTest");
  if (pmTest) pmTest.onclick = () => post("downloader.provider.test", { provider: "Premiumize" });

  const listBody = document.getElementById("listBody");
  if (listBody) {
    listBody.oncontextmenu = (ev) => {
      if (ev.target && ev.target.closest && ev.target.closest(".rowItem")) return;
      ev.preventDefault();
      showContextMenu(ev.clientX, ev.clientY, null, getVisibleDownloads());
    };
  }
  } catch (e) {
    try { console.error("[ui] wireUi failed", e); } catch {}
  }
}

function onNativeMessage(msg) {
  if (!msg || typeof msg !== "object") return;
  switch (msg.type) {
    case "downloader.state":
      state.downloads = Array.isArray(msg.payload?.downloads) ? msg.payload.downloads : [];
      state.settings = msg.payload?.settings || state.settings;
      state.outputFolder = msg.payload?.outputFolder || state.outputFolder;
      renderAll();
      break;
    case "downloader.progress":
      {
        const id = msg.payload?.id;
        if (!id) return;
        const idx = state.downloads.findIndex(d => d.id === id);
        if (idx >= 0) state.downloads[idx] = { ...state.downloads[idx], ...msg.payload };
        else state.downloads.unshift(msg.payload);
        renderAll();
      }
      break;
    case "downloader.completed":
      toast("Download completed.", "ok");
      post("downloader.getState", {});
      break;
    case "downloader.error":
      toast(msg.payload?.message || "Downloader error", "err");
      post("downloader.getState", {});
      break;
    case "downloader.providerStatus":
      if (msg.payload?.provider) {
        state.providerStatus[msg.payload.provider] = msg.payload;
        if (msg.payload.ok) toast(`${msg.payload.provider}: OK`, "ok");
        else toast(`${msg.payload.provider}: ${msg.payload.message || "Error"}`, "err");
      }
      break;
    case "downloader.addUrlsResult":
      {
        const added = Number(msg.payload?.added || 0);
        const skipped = Number(msg.payload?.skipped || 0);
        if (added > 0) state.filter = "queued";
        if (added > 0 && skipped > 0) toast(`Queued ${added} URL(s) · Skipped ${skipped}`, "ok");
        else if (added > 0) toast(`Queued ${added} URL(s).`, "ok");
        else toast("No valid URL(s) added.", "err");
        post("downloader.getState", {});
      }
      break;
  }
}

function initBridge() {
  if (hasBridge()) {
    window.chrome.webview.addEventListener("message", (e) => onNativeMessage(e.data));
  }
  post("downloader.getState", {});
  post("downloader.settings.get", {});
}

window.addEventListener("error", (e) => {
  try { console.error("[ui] error", e?.message || e); } catch {}
});
window.addEventListener("unhandledrejection", (e) => {
  try { console.error("[ui] unhandledrejection", e?.reason || e); } catch {}
});

document.addEventListener("DOMContentLoaded", () => {
  wireUi();
  initBridge();
  try { renderAll(); } catch (e) { try { console.error("[ui] renderAll failed", e); } catch {} }
});

if (document.readyState !== "loading") {
  try { wireUi(); } catch {}
  try { initBridge(); } catch {}
  try { renderAll(); } catch {}
}
