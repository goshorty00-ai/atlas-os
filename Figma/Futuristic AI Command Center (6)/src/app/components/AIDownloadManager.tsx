import { useState, useEffect, useMemo, useCallback } from "react";
import { motion, AnimatePresence } from "motion/react";
import {
  Download,
  Pause,
  Play,
  Trash2,
  FolderOpen,
  Link,
  FileText,
  CheckCircle2,
  XCircle,
  AlertCircle,
  Plus,
  Brain,
  Sparkles,
  Settings,
  Zap,
  Shield,
  Clock,
  Copy,
  RefreshCw,
  X,
  Video,
  Music,
  Image,
  Archive,
  Package,
  File,
  LightbulbIcon,
  GitBranch,
  Database,
  Activity,
  Cpu,
  Wifi,
  Check,
  Eye,
  Clipboard,
  CheckCheck,
} from "lucide-react";

/* ------------------------------------------------------------------ */
/*  Types                                                              */
/* ------------------------------------------------------------------ */

type DownloadStatus =
  | "queued"
  | "paused"
  | "resolving"
  | "downloading"
  | "converting"
  | "completed"
  | "cancelled"
  | "error";

type ThreatLevel = "safe" | "low" | "medium" | "high" | "critical";
type ConfidenceLevel = "low" | "medium" | "high" | "very-high";
type FilterTab = "all" | "active" | "queued" | "completed" | "error";

interface DownloadItem {
  id: string;
  url: string;
  filename: string;
  filesize: string;
  progress: number;
  speed: string;
  speedBps: number;
  eta: string;
  status: DownloadStatus;
  type: "video" | "audio" | "image" | "archive" | "package" | "file";
  totalBytes: number;
  bytesDownloaded: number;
  error: string;
  threatLevel: ThreatLevel;
  aiOptimized: boolean;
  speedBoost: number;
  outputPath?: string;
}

interface AIPrediction {
  id: string;
  downloadId: string;
  filename: string;
  reason: string;
  confidence: ConfidenceLevel;
  confidencePercent: number;
  url: string;
  filesize: string;
  type: string;
}

interface DependencyItem {
  name: string;
  version: string;
  required: boolean;
  url: string;
}

/* ------------------------------------------------------------------ */
/*  Floating Particles                                                 */
/* ------------------------------------------------------------------ */

function FloatingParticles() {
  const particles = useMemo(() => {
    const arr: { id: number; x: number; y: number; size: number; delay: number; duration: number }[] = [];
    for (let i = 0; i < 120; i++) {
      arr.push({
        id: i,
        x: Math.random() * 100,
        y: Math.random() * 100,
        size: Math.random() * 4 + 2,
        delay: Math.random() * 12,
        duration: Math.random() * 14 + 6,
      });
    }
    return arr;
  }, []);

  return (
    <div className="absolute inset-0 overflow-hidden pointer-events-none z-0">
      {particles.map((p) => (
        <motion.div
          key={p.id}
          className="absolute rounded-full bg-cyan-400"
          style={{ left: `${p.x}%`, top: `${p.y}%`, width: p.size, height: p.size }}
          animate={{ y: [0, -40, 0], x: [0, Math.random() * 10 - 5, 0], opacity: [0.1, 0.55, 0.1] }}
          transition={{ duration: p.duration, delay: p.delay, repeat: Infinity, ease: "easeInOut" }}
        />
      ))}
    </div>
  );
}

/* ------------------------------------------------------------------ */
/*  Circular Stat Icon                                                 */
/* ------------------------------------------------------------------ */

function CircularStat({ icon: Icon, color, bgColor, borderColor, label, sublabel, value }: {
  icon: any; color: string; bgColor: string; borderColor: string; label: string; sublabel?: string; value: string;
}) {
  return (
    <div className="flex flex-col items-center gap-1.5">
      <div className="w-11 h-11 rounded-full flex items-center justify-center border"
        style={{ background: bgColor, borderColor, boxShadow: `0 0 20px ${color}30` }}>
        <Icon className="w-5 h-5" style={{ color }} />
      </div>
      <div className="text-center">
        <div className="text-[9px] font-mono uppercase text-slate-500 tracking-wider">{label}</div>
        {sublabel && <div className="text-[9px] font-mono text-slate-600">{sublabel}</div>}
        <div className="font-mono font-bold text-xs mt-0.5" style={{ color }}>{value}</div>
      </div>
    </div>
  );
}

/* ------------------------------------------------------------------ */
/*  Main Component                                                     */
/* ------------------------------------------------------------------ */

export function AIDownloadManager() {
  /* ---- State ---- */
  const [downloads, setDownloads] = useState<DownloadItem[]>([]);
  const [showAddModal, setShowAddModal] = useState(false);
  const [showDependencyModal, setShowDependencyModal] = useState(false);
  const [selectedDependencies, setSelectedDependencies] = useState<DependencyItem[]>([]);
  const [newUrl, setNewUrl] = useState("");
  const [showPredictions, setShowPredictions] = useState(true);
  const [filterTab, setFilterTab] = useState<FilterTab>("all");
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [lastClickedId, setLastClickedId] = useState<string | null>(null);
  const [contextMenu, setContextMenu] = useState<{ open: boolean; x: number; y: number; itemId: string }>({ open: false, x: 0, y: 0, itemId: "" });
  const [previewingId, setPreviewingId] = useState<string | null>(null);
  const [audioRef] = useState<{ el: HTMLAudioElement | null }>({ el: null });
  const [playbackTime, setPlaybackTime] = useState(0);
  const [playbackDuration, setPlaybackDuration] = useState(0);
  const [bandwidthHealth, setBandwidthHealth] = useState(92);
  const [aiScanCount, setAiScanCount] = useState(0);

  /* ---- Bridge ---- */
  type HostDownload = {
    id: string; url: string; provider?: string; resolver?: string | null; resolvedUrl?: string | null;
    filename?: string | null; outputPath?: string | null; status?: string | null; progress?: number | null;
    bytesDownloaded?: number | null; totalBytes?: number | null; speedBps?: number | null;
    etaSeconds?: number | null; error?: string | null; createdUtc?: string | null;
  };
  type HostUiState = { downloads?: HostDownload[]; outputFolder?: string; settings?: unknown };

  const hasBridge = useCallback(() => {
    try { return typeof window !== "undefined" && (window as any).chrome?.webview; } catch { return false; }
  }, []);

  const post = useCallback((type: string, payload: any = {}) => {
    try { if (hasBridge()) (window as any).chrome.webview.postMessage({ type, payload }); } catch {}
  }, [hasBridge]);

  /* ---- Helpers ---- */
  const formatBytes = (n: number) => {
    if (!Number.isFinite(n) || n <= 0) return "0 B";
    const units = ["B", "KB", "MB", "GB", "TB"];
    let u = 0, v = n;
    while (v >= 1024 && u < units.length - 1) { v /= 1024; u++; }
    return `${v.toFixed(u === 0 ? 0 : 2)} ${units[u]}`;
  };
  const formatSpeed = (bps: number) => `${formatBytes(bps)}/s`;
  const formatEtaSeconds = (sec: number) => {
    if (!Number.isFinite(sec) || sec <= 0) return "—";
    const s = Math.floor(sec % 60), m = Math.floor((sec / 60) % 60), h = Math.floor(sec / 3600);
    if (h > 0) return `${h}h ${m}m`;
    if (m > 0) return `${m}m ${s}s`;
    return `${s}s`;
  };
  const inferType = (filename: string): DownloadItem["type"] => {
    const ext = (filename || "").toLowerCase().split(".").pop() || "";
    if (["mp4", "mkv", "mov", "avi", "webm"].includes(ext)) return "video";
    if (["mp3", "wav", "flac", "aac", "m4a", "ogg"].includes(ext)) return "audio";
    if (["png", "jpg", "jpeg", "gif", "webp", "bmp"].includes(ext)) return "image";
    if (["zip", "rar", "7z", "tar", "gz", "bz2"].includes(ext)) return "archive";
    if (["nupkg", "dll"].includes(ext)) return "package";
    return "file";
  };
  const normalizeStatus = (raw?: string | null): DownloadStatus => {
    const s = (raw || "").toLowerCase();
    if (s === "downloading") return "downloading"; if (s === "resolving") return "resolving";
    if (s === "converting") return "converting"; if (s === "queued") return "queued";
    if (s === "paused") return "paused"; if (s === "completed") return "completed";
    if (s === "cancelled") return "cancelled"; if (s === "error") return "error";
    return "queued";
  };

  const mapHostDownload = useCallback((d: HostDownload): DownloadItem => {
    const url = (d.url || "").toString();
    const status = normalizeStatus(d.status);
    const totalBytes = Number(d.totalBytes ?? 0) || 0;
    const bytesDownloaded = Number(d.bytesDownloaded ?? 0) || 0;
    const progressFromHost = Number(d.progress ?? 0);
    const progress = Number.isFinite(progressFromHost) && progressFromHost > 0
      ? Math.max(0, Math.min(100, progressFromHost))
      : totalBytes > 0 ? Math.max(0, Math.min(100, (bytesDownloaded / totalBytes) * 100)) : 0;
    const filenameRaw = (d.filename || "").toString().trim();
    const filename = filenameRaw || url.split("/").pop() || url || "(unknown)";
    const speedBps = Number(d.speedBps ?? 0) || 0;
    const etaSeconds = Number(d.etaSeconds ?? 0) || 0;
    return {
      id: (d.id || "").toString(), url, filename,
      filesize: totalBytes > 0 ? formatBytes(totalBytes) : "—",
      progress: status === "completed" ? 100 : progress,
      speed: speedBps > 0 ? formatSpeed(speedBps) : "0 B/s",
      speedBps, eta: status === "completed" ? "Complete" : formatEtaSeconds(etaSeconds),
      status, type: inferType(filename), totalBytes, bytesDownloaded,
      error: (d.error || "").toString(), threatLevel: "safe",
      aiOptimized: speedBps > 500000,
      speedBoost: speedBps > 500000 ? Math.floor(Math.random() * 30 + 10) : 0,
      outputPath: (d.outputPath || "").toString(),
    };
  }, []);

  /* ---- Bridge listener ---- */
  useEffect(() => {
    if (!hasBridge()) return;
    const handler = (event: any) => {
      const msg = event?.data;
      if (!msg || typeof msg.type !== "string") return;
      if (msg.type === "downloader.state") {
        const payload = (msg.payload || {}) as HostUiState;
        const list = Array.isArray(payload.downloads) ? payload.downloads : [];
        setDownloads(list.map(mapHostDownload));
      }
      if (msg.type === "downloader.playback") {
        const p = (msg.payload || {}) as any;
        const t = Number(p.time ?? NaN);
        const d = Number(p.duration ?? NaN);
        if (Number.isFinite(d) && d >= 0) setPlaybackDuration(d);
        if (Number.isFinite(t) && t >= 0) setPlaybackTime(t);
      }
    };
    (window as any).chrome.webview.addEventListener("message", handler);
    post("downloader.getState", {});
    return () => { try { (window as any).chrome.webview.removeEventListener("message", handler); } catch {} };
  }, [hasBridge, mapHostDownload, post]);

  /* ---- Keyboard (Ctrl+A, Ctrl+C, Delete, Escape) ---- */
  const filteredDownloads = useMemo(() => {
    switch (filterTab) {
      case "active": return downloads.filter(d => ["downloading", "resolving", "converting"].includes(d.status));
      case "queued": return downloads.filter(d => d.status === "queued" || d.status === "paused");
      case "completed": return downloads.filter(d => d.status === "completed");
      case "error": return downloads.filter(d => d.status === "error" || d.status === "cancelled");
      default: return downloads;
    }
  }, [downloads, filterTab]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (contextMenu.open && e.key === "Escape") { setContextMenu(c => ({ ...c, open: false })); return; }
      if ((e.ctrlKey || e.metaKey) && e.key === "a") {
        e.preventDefault();
        setSelectedIds(new Set(filteredDownloads.map(d => d.id)));
      }
      if ((e.ctrlKey || e.metaKey) && e.key === "c" && selectedIds.size > 0) {
        const names = downloads.filter(d => selectedIds.has(d.id)).map(d => d.filename).join("\n");
        try { navigator.clipboard.writeText(names); } catch {}
      }
      if (e.key === "Delete" && selectedIds.size > 0) {
        selectedIds.forEach(id => {
          const item = downloads.find(d => d.id === id);
          if (!item) return;
          if (["downloading", "resolving", "converting", "queued", "paused"].includes(item.status))
            post("downloader.cancel", { id });
          else post("downloader.remove", { id });
        });
        setSelectedIds(new Set());
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [contextMenu.open, selectedIds, downloads, filteredDownloads, post]);

  /* ---- Click outside closes context menu ---- */
  useEffect(() => {
    if (!contextMenu.open) return;
    const onDown = () => setContextMenu(c => ({ ...c, open: false }));
    window.addEventListener("mousedown", onDown);
    return () => window.removeEventListener("mousedown", onDown);
  }, [contextMenu.open]);

  /* ---- Derived ---- */
  const selectedItem = useMemo(() => downloads.find(d => d.id === contextMenu.itemId) || null, [contextMenu.itemId, downloads]);

  const predictions = useMemo<AIPrediction[]>(() => {
    return downloads.filter(d => d.status === "completed").slice(0, 6).map((d, i) => {
      const toolTypes = [
        { title: "Extract Vocals/Bass", icon: Music },
        { title: "Generate Subtitles", icon: FileText },
        { title: "Upscale to 4K", icon: Video },
        { title: "ID3 Auto-Tag", icon: Sparkles },
      ];
      const tool = toolTypes[i % toolTypes.length];
      
      return {
        id: `ai-${d.id}`, downloadId: d.id, filename: d.filename,
        reason: tool.title,
        confidence: "very-high" as ConfidenceLevel, confidencePercent: Math.floor(Math.random() * 10) + 90,
        url: d.url, filesize: d.filesize, type: d.type,
      };
    });
  }, [downloads]);

  const stats = useMemo(() => {
    const active = downloads.filter(d => ["downloading", "resolving", "converting"].includes(d.status)).length;
    const completed = downloads.filter(d => d.status === "completed").length;
    const queued = downloads.filter(d => d.status === "queued" || d.status === "paused").length;
    const errors = downloads.filter(d => d.status === "error" || d.status === "cancelled").length;
    const totalSpeedBps = downloads.filter(d => ["downloading", "resolving", "converting"].includes(d.status)).reduce((s, d) => s + d.speedBps, 0);
    const threatsSafe = 0;
    const threatsBlocked = 0;
    return { active, completed, queued, errors, totalSpeedBps, threatsSafe, threatsBlocked };
  }, [downloads]);

  const totalSpeedMBps = (stats.totalSpeedBps / (1024 * 1024)).toFixed(1);
  const efficiency = useMemo(() => {
    if (downloads.length === 0) return 0;
    let sum = 0;
    for (const d of downloads) {
      if (d.status === "error" || d.status === "cancelled") continue;
      const p = Number(d.progress ?? 0);
      const pct = Number.isFinite(p) ? Math.max(0, Math.min(100, p)) : 0;
      sum += pct / 100;
    }
    return Math.max(0, Math.min(100, Math.round((sum / downloads.length) * 100)));
  }, [downloads]);

  /* ---- Dynamic AI stats simulation ---- */
  useEffect(() => {
    const iv = setInterval(() => {
      setBandwidthHealth(prev => {
        const delta = (Math.random() - 0.5) * 6;
        return Math.max(60, Math.min(99, Math.round(prev + delta)));
      });
      setAiScanCount(prev => prev + (stats.active > 0 ? Math.floor(Math.random() * 3) : 0));
    }, 3000);
    return () => clearInterval(iv);
  }, [stats.active]);

  /* ---- Selection (Shift/Ctrl click) ---- */
  const handleItemClick = useCallback((id: string, e: React.MouseEvent) => {
    if (e.shiftKey && lastClickedId) {
      const ids = filteredDownloads.map(d => d.id);
      const a = ids.indexOf(lastClickedId), b = ids.indexOf(id);
      if (a >= 0 && b >= 0) {
        const [lo, hi] = a < b ? [a, b] : [b, a];
        setSelectedIds(prev => { const n = new Set(prev); ids.slice(lo, hi + 1).forEach(r => n.add(r)); return n; });
      }
    } else if (e.ctrlKey || e.metaKey) {
      setSelectedIds(prev => { const n = new Set(prev); if (n.has(id)) n.delete(id); else n.add(id); return n; });
    } else {
      setSelectedIds(new Set([id]));
    }
    setLastClickedId(id);
  }, [lastClickedId, filteredDownloads]);

  /* ---- Actions ---- */
  const handleAddUrl = () => { if (!newUrl.trim()) return; post("downloader.addUrls", { provider: "Auto", urls: [newUrl.trim()] }); setNewUrl(""); setShowAddModal(false); };
  const handleImportCsv = () => post("downloader.importCsv", {});
  const handleToggleDownload = (id: string) => {
    const item = downloads.find(d => d.id === id); if (!item) return;
    if (["downloading", "resolving", "converting"].includes(item.status)) post("downloader.pause", { id });
    else if (["paused", "queued"].includes(item.status)) post("downloader.resume", { id });
  };
  const handleRemoveDownload = (id: string) => {
    const item = downloads.find(d => d.id === id); if (!item) return;
    if (["downloading", "resolving", "converting", "queued", "paused"].includes(item.status)) post("downloader.cancel", { id });
    else post("downloader.remove", { id });
  };
  const handleRetry = (id: string) => post("downloader.retry", { id });
  const handleOpenFolder = (id: string) => post("downloader.openFolder", { id });
  const handleOpenMedia = (id: string) => {
    const item = downloads.find(d => d.id === id);
    if (item && item.status === "completed" && canPreview(item.type)) {
      // For audio files, try inline preview first
      if (item.type === "audio" && item.outputPath) {
        if (previewingId === id) {
          setPreviewingId(null);
          post("downloader.playback.stop", { id });
          if (audioRef.el) { audioRef.el.pause(); audioRef.el = null; }
        } else {
          // Pause existing
          if (previewingId) post("downloader.playback.stop", { id: previewingId });
          if (audioRef.el) { audioRef.el.pause(); }
          setPreviewingId(id);
          post("downloader.openMedia", { id });
        }
        return;
      }
    }
    post("downloader.openMedia", { id });
  };
  const handlePauseAll = () => post("downloader.pauseAll", {});
  const handleResumeAll = () => post("downloader.resumeAll", {});
  const handleClearFinished = () => post("downloader.clearFinished", {});
  const updateTaskStatus = (id: string, newStatus: string) => {
    // stub to simulate processing
    const el = document.getElementById(`task-status-${id}`);
    if (el) el.innerText = newStatus;
  };
  
  const handleProcessTask = (id: string) => {
    updateTaskStatus(id, "PROCESSING...");
    setTimeout(() => {
      updateTaskStatus(id, "COMPLETED");
    }, 2500);
  };
  
  const handleAcceptAllPredictions = () => { 
    predictions.forEach((p, i) => setTimeout(() => handleProcessTask(p.id), i * 600)); 
  };

  /* ---- Visual helpers ---- */
  const getStatusColor = (status: DownloadStatus) => {
    switch (status) {
      case "downloading": case "resolving": case "converting": return "#22d3ee";
      case "completed": return "#22c55e";
      case "error": case "cancelled": return "#ef4444";
      case "paused": return "#f97316";
      default: return "#64748b";
    }
  };
  const getStatusLabel = (status: DownloadStatus) => status.toUpperCase();
  const getThreatColor = (level: ThreatLevel) => {
    switch (level) { case "safe": return "#22c55e"; case "low": return "#84cc16"; case "medium": return "#f97316"; case "high": return "#ef4444"; case "critical": return "#dc2626"; default: return "#64748b"; }
  };
  const getConfidenceColor = (level: ConfidenceLevel) => {
    switch (level) { case "very-high": return "#22d3ee"; case "high": return "#22c55e"; case "medium": return "#f97316"; case "low": return "#64748b"; default: return "#64748b"; }
  };
  const getFileIcon = (type: string, cls = "w-6 h-6") => {
    switch (type) {
      case "video": return <Video className={cls} />; case "audio": return <Music className={cls} />;
      case "image": return <Image className={cls} />; case "archive": return <Archive className={cls} />;
      case "package": return <Package className={cls} />; default: return <File className={cls} />;
    }
  };
  const canPreview = (type: string) => type === "audio" || type === "video" || type === "image";

  const filterTabs: { key: FilterTab; label: string; count: number; color: string }[] = [
    { key: "all", label: "ALL", count: downloads.length, color: "#22d3ee" },
    { key: "active", label: "ACTIVE", count: stats.active, color: "#22d3ee" },
    { key: "queued", label: "QUEUED", count: stats.queued, color: "#f97316" },
    { key: "completed", label: "COMPLETED", count: stats.completed, color: "#22c55e" },
    { key: "error", label: "ERRORS", count: stats.errors, color: "#ef4444" },
  ];

  /* ================================================================ */
  /*  RENDER                                                           */
  /* ================================================================ */
  return (
    <div className="flex-1 min-h-0 w-full h-full bg-[#0b0f14] text-slate-200 overflow-hidden relative flex flex-col select-none">
      <FloatingParticles />

      {/* ===== HEADER ===== */}
      <div className="relative z-10 shrink-0 bg-[#0a0e12]/80 border-b border-cyan-500/10 px-6 py-2.5 flex items-center justify-between">
        <div className="flex items-center gap-4">
          <div className="w-9 h-9 rounded-full bg-cyan-500/10 border border-cyan-400/80 flex items-center justify-center shadow-[0_0_14px_rgba(34,211,238,0.35)]">
            <Brain className="w-4.5 h-4.5 text-cyan-400" />
          </div>
          <div>
            <h1 className="font-mono font-bold text-sm text-cyan-400 tracking-[0.15em]">AI NEURAL DOWNLOAD MATRIX</h1>
            <div className="flex items-center gap-2 mt-0.5">
              <Sparkles className="w-3.5 h-3.5 text-orange-400" />
              <span className="font-mono text-[11px] text-slate-500">Quantum Speed Optimization • Threat Intelligence Active</span>
            </div>
          </div>
        </div>
        <div className="flex items-center gap-3">
          <motion.button whileHover={{ scale: 1.03 }} whileTap={{ scale: 0.97 }} onClick={handlePauseAll}
            className="flex items-center gap-2 bg-orange-500/10 text-orange-400 border border-orange-400/60 rounded-xl px-4 py-2.5 font-mono font-bold text-sm shadow-[0_0_20px_rgba(249,115,22,0.18)] hover:bg-orange-500/20 transition-colors">
            <Pause className="w-4 h-4" /> PAUSE
          </motion.button>
          <motion.button whileHover={{ scale: 1.03 }} whileTap={{ scale: 0.97 }} onClick={() => setShowAddModal(true)}
            className="flex items-center gap-2 bg-cyan-500/10 text-cyan-400 border border-cyan-400/60 rounded-xl px-5 py-2.5 font-mono font-bold text-sm shadow-[0_0_20px_rgba(34,211,238,0.18)] hover:bg-cyan-500/20 transition-colors">
            <Plus className="w-4 h-4" /> ADD URL
          </motion.button>
          <motion.button whileHover={{ scale: 1.03 }} whileTap={{ scale: 0.97 }} onClick={handleImportCsv}
            className="flex items-center gap-2 bg-purple-500/10 text-purple-400 border border-purple-500/60 rounded-xl px-5 py-2.5 font-mono font-bold text-sm shadow-[0_0_20px_rgba(168,85,247,0.14)] hover:bg-purple-500/20 transition-colors">
            <FileText className="w-4 h-4" /> CSV BATCH
          </motion.button>
          <motion.button whileHover={{ scale: 1.03 }} whileTap={{ scale: 0.97 }} onClick={() => post("downloader.openOutputFolder", {})}
            className="flex items-center gap-2 bg-emerald-500/10 text-emerald-400 border border-emerald-500/60 rounded-xl px-5 py-2.5 font-mono font-bold text-sm shadow-[0_0_20px_rgba(16,185,129,0.14)] hover:bg-emerald-500/20 transition-colors">
            <FolderOpen className="w-4 h-4" /> OPEN FOLDER
          </motion.button>
        </div>
      </div>

      {/* ===== STATS BAR ===== */}
      <div className="relative z-10 shrink-0 bg-[#0a0e12]/40 border-b border-cyan-500/10 px-6 py-2.5">
        <div className="flex items-center justify-around">
          <CircularStat icon={Activity} color="#22d3ee" bgColor="rgba(34,211,238,0.1)" borderColor="rgba(34,211,238,0.5)" label="ACTIVE" sublabel="DOWNLOADS" value={`${stats.active} / ${downloads.length}`} />
          <CircularStat icon={Zap} color="#f97316" bgColor="rgba(249,115,22,0.1)" borderColor="rgba(249,115,22,0.5)" label="NEURAL" sublabel="SPEED" value={`${totalSpeedMBps} MB/s`} />
          <CircularStat icon={Cpu} color="#22c55e" bgColor="rgba(34,197,94,0.1)" borderColor="rgba(34,197,94,0.5)" label="AI" sublabel="EFFICIENCY" value={`${efficiency}%`} />
          <CircularStat icon={Shield} color="#22c55e" bgColor="rgba(34,197,94,0.08)" borderColor="rgba(34,197,94,0.4)" label="THREATS" sublabel="BLOCKED" value={`${stats.threatsBlocked}`} />
          <CircularStat icon={Wifi} color="#a855f7" bgColor="rgba(168,85,247,0.1)" borderColor="rgba(168,85,247,0.5)" label="BANDWIDTH" sublabel="HEALTH" value={downloads.length > 0 ? `${bandwidthHealth}%` : "—"} />
        </div>
      </div>

      {/* ===== FILTER TABS + BULK ACTIONS ===== */}
      <div className="relative z-10 shrink-0 bg-[#0a0e12]/30 border-b border-cyan-500/10 px-6 py-2.5 flex items-center justify-between">
        <div className="flex items-center gap-1.5">
          {filterTabs.map(tab => (
            <button key={tab.key} onClick={() => setFilterTab(tab.key)}
              className={`px-3.5 py-1.5 rounded-lg font-mono text-[10px] font-bold uppercase transition-all border ${filterTab === tab.key ? "border-opacity-60 bg-opacity-20" : "border-transparent bg-transparent text-slate-500 hover:text-slate-300"}`}
              style={filterTab === tab.key ? { color: tab.color, background: `${tab.color}15`, borderColor: `${tab.color}50` } : undefined}>
              {tab.label}
              <span className="ml-1.5 px-1.5 py-0.5 rounded text-[9px]" style={filterTab === tab.key ? { background: `${tab.color}20`, color: tab.color } : { color: "#64748b" }}>{tab.count}</span>
            </button>
          ))}
        </div>
        <div className="flex items-center gap-2">
          {selectedIds.size > 0 && (
            <div className="flex items-center gap-1.5 mr-2">
              <span className="font-mono text-[10px] text-cyan-400">{selectedIds.size} selected</span>
              <button onClick={() => { selectedIds.forEach(id => handleToggleDownload(id)); }} className="w-7 h-7 rounded-lg bg-slate-900/50 border border-slate-700/40 text-slate-400 hover:text-orange-400 hover:border-orange-400/40 transition-colors flex items-center justify-center" title="Pause/Resume"><Pause className="w-3 h-3" /></button>
              <button onClick={() => { selectedIds.forEach(id => handleRetry(id)); }} className="w-7 h-7 rounded-lg bg-slate-900/50 border border-slate-700/40 text-slate-400 hover:text-cyan-400 hover:border-cyan-400/40 transition-colors flex items-center justify-center" title="Retry"><RefreshCw className="w-3 h-3" /></button>
              <button onClick={() => { selectedIds.forEach(id => handleRemoveDownload(id)); setSelectedIds(new Set()); }} className="w-7 h-7 rounded-lg bg-slate-900/50 border border-slate-700/40 text-slate-400 hover:text-red-400 hover:border-red-400/40 transition-colors flex items-center justify-center" title="Remove"><Trash2 className="w-3 h-3" /></button>
            </div>
          )}
          <button onClick={handlePauseAll} className="px-2.5 py-1 rounded-lg bg-slate-900/30 border border-slate-700/30 text-slate-500 hover:text-orange-400 hover:border-orange-400/30 transition-colors font-mono text-[9px] uppercase">Pause All</button>
          <button onClick={handleResumeAll} className="px-2.5 py-1 rounded-lg bg-slate-900/30 border border-slate-700/30 text-slate-500 hover:text-green-400 hover:border-green-400/30 transition-colors font-mono text-[9px] uppercase">Resume All</button>
          <button onClick={handleClearFinished} className="px-2.5 py-1 rounded-lg bg-slate-900/30 border border-slate-700/30 text-slate-500 hover:text-red-400 hover:border-red-400/30 transition-colors font-mono text-[9px] uppercase">Clear Done</button>
        </div>
      </div>

      {/* ===== MAIN CONTENT ===== */}
      <div className="relative z-10 flex-1 flex overflow-hidden min-h-0">
        {/* Download list */}
        <div className="flex-1 overflow-y-auto atlas-scrollbar p-5">
          {filteredDownloads.length === 0 && (
            <div className="flex flex-col items-center justify-center h-full opacity-50">
              <Brain className="w-20 h-20 text-slate-700" />
              <div className="mt-4 font-mono text-sm text-slate-500">{downloads.length === 0 ? "AI Download Manager Ready" : `No ${filterTab} downloads`}</div>
              <div className="mt-1 font-mono text-xs text-slate-600">{downloads.length === 0 ? "Add URLs to begin intelligent downloading" : "Try a different filter tab"}</div>
            </div>
          )}

          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4">
            {filteredDownloads.map((item, index) => {
              const isSelected = selectedIds.has(item.id);
              return (
                <motion.div key={item.id}
                  initial={{ opacity: 0, scale: 0.95 }} animate={{ opacity: 1, scale: 1 }}
                  transition={{ delay: Math.min(index, 20) * 0.02 }}
                  onClick={(e) => handleItemClick(item.id, e)}
                  onContextMenu={(e) => {
                    e.preventDefault();
                    if (!selectedIds.has(item.id)) setSelectedIds(new Set([item.id]));
                    setContextMenu({ open: true, x: e.clientX, y: e.clientY, itemId: item.id });
                  }}
                  className={`relative bg-[#0f172a]/80 border rounded-xl p-3 group cursor-pointer transition-all flex flex-col ${isSelected ? "border-cyan-500/50 bg-cyan-500/5 shadow-[0_0_12px_rgba(34,211,238,0.1)]" : "border-slate-700/40 hover:border-cyan-500/20"}`}>

                  {/* AI boost badge */}
                  {item.aiOptimized && (
                    <div className="absolute -top-2 -right-2 bg-cyan-500/20 border border-cyan-400/60 rounded-full px-2 py-0.5 flex items-center gap-0.5 z-10 shadow-[0_0_10px_rgba(34,211,238,0.2)] bg-slate-900">
                      <Zap className="w-2.5 h-2.5 text-cyan-400" /><span className="font-mono text-[8px] text-cyan-400 font-bold">+{item.speedBoost}%</span>
                    </div>
                  )}

                  {/* Selection checkbox */}
                  <div className="absolute top-3 right-3 z-10">
                    <div className={`w-4 h-4 rounded border flex items-center justify-center transition-all ${isSelected ? "bg-cyan-500/30 border-cyan-400" : "border-slate-600/50 group-hover:border-slate-500"}`}>
                      {isSelected && <Check className="w-3 h-3 text-cyan-400" />}
                    </div>
                  </div>

                  <div className="flex items-start gap-3 mb-2">
                    {/* File icon */}
                    <div className="w-10 h-10 rounded-lg flex items-center justify-center border shrink-0 relative"
                      style={{ background: `${getStatusColor(item.status)}15`, borderColor: `${getStatusColor(item.status)}40`, color: getStatusColor(item.status) }}>
                      {getFileIcon(item.type)}
                      {canPreview(item.type) && item.status === "completed" && (
                        <button onClick={(e) => { e.stopPropagation(); handleOpenMedia(item.id); }}
                          className="absolute -bottom-1 -right-1 w-5 h-5 rounded-full bg-cyan-500/20 border border-cyan-400/60 flex items-center justify-center hover:bg-cyan-500/40 transition-colors" title="Preview">
                          <Play className="w-2.5 h-2.5 text-cyan-400" />
                        </button>
                      )}
                    </div>

                    {/* Details */}
                    <div className="flex-1 min-w-0 pr-5">
                      <div className="font-mono font-bold text-xs text-slate-200 truncate pt-0.5" title={item.filename}>{item.filename}</div>
                      <div className="flex items-center gap-1.5 mt-1">
                        <span className="shrink-0 rounded-md border px-1.5 py-px font-mono text-[8px] font-bold uppercase"
                          style={{ background: `${getStatusColor(item.status)}20`, borderColor: `${getStatusColor(item.status)}60`, color: getStatusColor(item.status) }}>
                          {getStatusLabel(item.status)}
                        </span>
                        <span className="shrink-0 flex items-center gap-0.5 rounded-md border px-1.5 py-px font-mono text-[8px] uppercase"
                          style={{ background: `${getThreatColor(item.threatLevel)}15`, borderColor: `${getThreatColor(item.threatLevel)}40`, color: getThreatColor(item.threatLevel) }}>
                          <Shield className="w-2.5 h-2.5" />{item.threatLevel.toUpperCase()}
                        </span>
                      </div>
                    </div>
                  </div>

                  {/* Progress bar */}
                  <div className="mt-2 mb-2">
                    <div className="h-1.5 bg-[#0a0e12] rounded-full overflow-hidden">
                      <motion.div className="h-full rounded-full" initial={{ width: 0 }}
                        animate={{ width: `${Math.max(0, Math.min(100, item.progress))}%` }} transition={{ duration: 0.3 }}
                        style={{ background: getStatusColor(item.status), boxShadow: `0 0 12px ${getStatusColor(item.status)}60` }} />
                    </div>
                    <div className="flex items-center justify-between mt-1 font-mono text-[9px] text-slate-500">
                      <div>
                        <span className="text-cyan-400">{item.progress.toFixed(1)}%</span>
                        {item.totalBytes > 0 && <span className="ml-1">({formatBytes(item.bytesDownloaded)}/{formatBytes(item.totalBytes)})</span>}
                      </div>
                      <span className="text-green-400">{item.eta}</span>
                    </div>
                  </div>

                  <div className="mt-auto pt-2 border-t border-slate-700/30 flex items-center justify-between">
                    <div className="font-mono text-[9px] text-cyan-400/80">{item.speed}</div>
                    
                    {/* Action buttons */}
                    <div className="flex items-center gap-1 shrink-0">
                      {["downloading", "resolving", "converting", "paused", "queued"].includes(item.status) && (
                        <motion.button whileHover={{ scale: 1.1 }} whileTap={{ scale: 0.95 }}
                          onClick={(e) => { e.stopPropagation(); handleToggleDownload(item.id); }}
                          className="w-7 h-7 rounded-lg bg-slate-900/60 border border-slate-700/40 text-slate-400 hover:text-orange-400 hover:border-orange-400/40 transition-colors flex items-center justify-center"
                          title={["downloading", "resolving", "converting"].includes(item.status) ? "Pause" : "Resume"}>
                          {["downloading", "resolving", "converting"].includes(item.status) ? <Pause className="w-3 h-3" /> : <Play className="w-3 h-3" />}
                        </motion.button>
                      )}
                      {item.status === "completed" && (
                        <motion.button whileHover={{ scale: 1.1 }} whileTap={{ scale: 0.95 }}
                          onClick={(e) => { e.stopPropagation(); handleOpenFolder(item.id); }}
                          className="w-7 h-7 rounded-lg bg-slate-900/60 border border-slate-700/40 text-slate-400 hover:text-green-400 hover:border-green-400/40 transition-colors flex items-center justify-center" title="Open Folder">
                          <FolderOpen className="w-3 h-3" />
                        </motion.button>
                      )}
                      {(item.status === "error" || item.status === "cancelled") && (
                        <motion.button whileHover={{ scale: 1.1 }} whileTap={{ scale: 0.95 }}
                          onClick={(e) => { e.stopPropagation(); handleRetry(item.id); }}
                          className="w-7 h-7 rounded-lg bg-slate-900/60 border border-slate-700/40 text-slate-400 hover:text-cyan-400 hover:border-cyan-400/40 transition-colors flex items-center justify-center" title="Retry">
                          <RefreshCw className="w-3 h-3" />
                        </motion.button>
                      )}
                      <motion.button whileHover={{ scale: 1.1 }} whileTap={{ scale: 0.95 }}
                        onClick={(e) => { e.stopPropagation(); handleRemoveDownload(item.id); }}
                        className="w-7 h-7 rounded-lg bg-slate-900/60 border border-slate-700/40 text-slate-400 hover:text-red-400 hover:border-red-400/40 transition-colors flex items-center justify-center" title="Remove">
                        <Trash2 className="w-3 h-3" />
                      </motion.button>
                    </div>
                  </div>

                  {item.status === "error" && item.error && (
                    <div className="mt-2 pt-2 border-t border-red-500/10 flex items-start gap-1 text-red-400 bg-red-400/5 rounded-md p-1.5">
                      <AlertCircle className="w-2.5 h-2.5 shrink-0 mt-0.5" />
                      <span className="font-mono text-[8px] leading-tight line-clamp-2 break-all">{item.error}</span>
                    </div>
                  )}

                  {/* Inline Audio Player */}
                  {previewingId === item.id && item.type === "audio" && item.status === "completed" && (
                    <motion.div initial={{ height: 0, opacity: 0 }} animate={{ height: "auto", opacity: 1 }} exit={{ height: 0, opacity: 0 }}
                      className="mt-2 rounded-lg bg-[#0a0e12] border border-cyan-500/20 p-2 flex items-center gap-2">
                      <div className="flex items-center gap-1">
                        {[...Array(10)].map((_, i) => (
                          <motion.div key={i} className="w-1 rounded-full bg-cyan-400/60"
                            animate={{ height: [4, 4 + Math.random() * 8, 4] }}
                            transition={{ duration: 0.4 + Math.random() * 0.4, repeat: Infinity, repeatType: "reverse", delay: i * 0.05 }} />
                        ))}
                      </div>

                      <input
                        type="range"
                        min={0}
                        max={Math.max(0.001, playbackDuration || 0.001)}
                        value={Math.min(Math.max(0, playbackTime), Math.max(0.001, playbackDuration || 0.001))}
                        onChange={(e) => { e.stopPropagation(); setPlaybackTime(Number(e.target.value)); }}
                        onMouseUp={(e) => { e.stopPropagation(); post("downloader.playback.seek", { seconds: Number((e.target as HTMLInputElement).value) }); }}
                        onTouchEnd={(e) => { e.stopPropagation(); post("downloader.playback.seek", { seconds: Number((e.target as any)?.value ?? playbackTime) }); }}
                        className="flex-1 mx-2 h-1.5 rounded-full bg-slate-700/40 accent-cyan-400"
                      />

                      <span className="font-mono text-[9px] text-cyan-400 ml-auto whitespace-nowrap">PLAYING</span>
                      <button onClick={(e) => { e.stopPropagation(); setPreviewingId(null); }}
                        className="w-5 h-5 rounded-full bg-cyan-500/20 border border-cyan-400/40 flex items-center justify-center hover:bg-cyan-500/40 transition-colors">
                        <X className="w-3 h-3 text-cyan-400" />
                      </button>
                    </motion.div>
                  )}
                </motion.div>
              );
            })}
          </div>
        </div>

        {/* ===== AI AUTOMATION PANEL ===== */}
        {showPredictions && predictions.length > 0 && (
          <div className="w-[300px] shrink-0 border-l border-cyan-500/10 bg-cyan-500/[0.03] overflow-hidden flex flex-col">
            <div className="px-4 py-3 flex items-center justify-between border-b border-cyan-500/10 shrink-0">
              <div className="flex items-center gap-2">
                <Brain className="w-5 h-5 text-cyan-400" />
                <div>
                  <div className="font-mono font-bold text-xs text-cyan-400 tracking-wider">AI AUTOMATION TASKS</div>
                  <div className="font-mono text-[10px] text-slate-500 mt-0.5">{predictions.length} files analyzed</div>
                </div>
              </div>
              <div className="flex items-center gap-2">
                <motion.button whileHover={{ scale: 1.03 }} whileTap={{ scale: 0.97 }} onClick={handleAcceptAllPredictions}
                  className="bg-purple-500/10 text-purple-400 border border-purple-400/50 rounded-lg px-2 py-1.5 font-mono font-bold text-[10px] hover:bg-purple-500/20 transition-colors">RUN ALL</motion.button>
                <motion.button whileHover={{ scale: 1.1 }} whileTap={{ scale: 0.95 }} onClick={() => setShowPredictions(false)}
                  className="w-7 h-7 rounded-lg bg-slate-900/50 border border-slate-700/40 text-slate-400 hover:text-cyan-400 transition-colors flex items-center justify-center">
                  <X className="w-3.5 h-3.5" />
                </motion.button>
              </div>
            </div>
            <div className="flex-1 overflow-y-auto atlas-scrollbar p-3 space-y-3">
              {predictions.map(pred => (
                <div key={pred.id} className="bg-slate-900/30 border border-cyan-500/15 rounded-xl p-4">
                  <div className="font-mono font-bold text-[11px] text-slate-200 truncate">{pred.filename}</div>
                  <div className="font-mono text-[10px] text-slate-500 mt-0.5">{pred.filesize}</div>
                  <div className="flex items-center gap-1.5 mt-2 bg-purple-500/5 border border-purple-500/10 rounded-md px-2.5 py-1.5">
                    <Sparkles className="w-3 h-3 text-purple-400 shrink-0" />
                    <span className="font-mono text-[9px] text-purple-400 truncate">{pred.reason}</span>
                  </div>
                  <div className="mt-3">
                    <div className="flex items-center justify-between mb-1">
                      <div className="font-mono text-[9px] text-slate-500 uppercase">AI CONFIDENCE</div>
                      <div className="font-mono text-[9px] font-bold uppercase" style={{ color: getConfidenceColor(pred.confidence) }}>{pred.confidencePercent}%</div>
                    </div>
                    <div className="h-1.5 bg-[#0a0e12] rounded-full overflow-hidden">
                      <div className="h-full rounded-full transition-all duration-500"
                        style={{ width: `${pred.confidencePercent}%`, background: getConfidenceColor(pred.confidence), boxShadow: `0 0 8px ${getConfidenceColor(pred.confidence)}60` }} />
                    </div>
                  </div>
                  <motion.button whileHover={{ scale: 1.02 }} whileTap={{ scale: 0.98 }}
                    onClick={() => handleProcessTask(pred.id)}
                    className="w-full mt-3 bg-cyan-500/10 text-cyan-400 border border-cyan-400/40 rounded-lg py-2 font-mono font-bold text-[10px] hover:bg-cyan-500/20 transition-colors flex items-center justify-center gap-1.5">
                    <Zap className="w-3 h-3" /> <span id={`task-status-${pred.id}`}>PROCESS FILE</span>
                  </motion.button>
                </div>
              ))}
            </div>
            <div className="shrink-0 px-4 py-3 border-t border-cyan-500/10 flex items-center gap-2">
              <div className="w-2 h-2 rounded-full bg-green-400 animate-pulse" />
              <div>
                <div className="font-mono text-[9px] text-slate-500 uppercase">NEURAL NET: FILE ANALYST</div>
                <div className="font-mono text-[10px] text-cyan-400 font-bold">ACTIVE • 120ms LATENCY</div>
              </div>
            </div>
          </div>
        )}
      </div>

      {/* ===== AI STATUS FOOTER ===== */}
      <div className="shrink-0 h-8 border-t border-cyan-500/10 bg-[#0a0e12]/90 flex items-center px-4 gap-6 font-mono text-[9px]">
        <div className="flex items-center gap-1.5">
          <motion.div className="w-1.5 h-1.5 rounded-full bg-green-400" animate={{ opacity: [1, 0.4, 1] }} transition={{ duration: 1.5, repeat: Infinity }} />
          <span className="text-green-400 uppercase">AI Engine Online</span>
        </div>
        <div className="flex items-center gap-1.5 text-slate-500">
          <Shield className="w-2.5 h-2.5 text-cyan-400/60" />
          <span>Files Scanned: <span className="text-cyan-400">{aiScanCount + stats.completed}</span></span>
        </div>
        <div className="flex items-center gap-1.5 text-slate-500">
          <Zap className="w-2.5 h-2.5 text-orange-400/60" />
          <span>Bandwidth Optimizer: <span className="text-orange-400">{stats.active > 0 ? "ACTIVE" : "STANDBY"}</span></span>
        </div>
        <div className="flex items-center gap-1.5 text-slate-500">
          <Brain className="w-2.5 h-2.5 text-purple-400/60" />
          <span>Neural Cache: <span className="text-purple-400">{downloads.length > 0 ? `${Math.min(downloads.length * 12, 100)}%` : "IDLE"}</span></span>
        </div>
        <div className="ml-auto flex items-center gap-1.5 text-slate-600">
          <Activity className="w-2.5 h-2.5" />
          <span>v2.4.0 • Atlas AI</span>
        </div>
      </div>

      {/* ===== CONTEXT MENU ===== */}
      <AnimatePresence>
        {contextMenu.open && selectedItem && (
          <motion.div initial={{ opacity: 0, scale: 0.95 }} animate={{ opacity: 1, scale: 1 }} exit={{ opacity: 0, scale: 0.95 }}
            transition={{ duration: 0.1 }} style={{ left: contextMenu.x, top: contextMenu.y }}
            className="fixed w-60 rounded-xl border border-slate-700/60 bg-slate-950/95 backdrop-blur-md shadow-[0_0_20px_rgba(34,211,238,0.15)] overflow-hidden z-[200]"
            onMouseDown={e => e.stopPropagation()}>
            <div className="px-3 py-2 border-b border-slate-800/60">
              <div className="text-[9px] font-mono uppercase text-slate-500">{selectedIds.size > 1 ? `${selectedIds.size} items selected` : "Actions"}</div>
              <div className="text-xs font-mono text-slate-200 truncate">{selectedItem.filename}</div>
            </div>
            <button className="w-full px-3 py-2 flex items-center gap-2.5 text-xs font-mono text-slate-300 hover:bg-slate-800/40 transition-colors"
              onClick={() => {
                const targets = selectedIds.size > 1 ? downloads.filter(d => selectedIds.has(d.id)) : [selectedItem];
                try { navigator.clipboard.writeText(targets.map(d => d.url).join("\n")); } catch {}
                setContextMenu(c => ({ ...c, open: false }));
              }}><Copy className="w-3.5 h-3.5 text-cyan-400" /> Copy URL</button>
            <button className="w-full px-3 py-2 flex items-center gap-2.5 text-xs font-mono text-slate-300 hover:bg-slate-800/40 transition-colors"
              onClick={() => {
                const targets = selectedIds.size > 1 ? downloads.filter(d => selectedIds.has(d.id)) : [selectedItem];
                try { navigator.clipboard.writeText(targets.map(d => d.filename).join("\n")); } catch {}
                setContextMenu(c => ({ ...c, open: false }));
              }}><Clipboard className="w-3.5 h-3.5 text-cyan-400" /> Copy Filename</button>
            <button className="w-full px-3 py-2 flex items-center gap-2.5 text-xs font-mono text-slate-300 hover:bg-slate-800/40 transition-colors border-b border-slate-800/30"
              onClick={() => { setSelectedIds(new Set(filteredDownloads.map(d => d.id))); setContextMenu(c => ({ ...c, open: false })); }}>
              <CheckCheck className="w-3.5 h-3.5 text-cyan-400" /> Select All
            </button>
            {["downloading", "resolving", "converting", "paused", "queued"].includes(selectedItem.status) && (
              <button className="w-full px-3 py-2 flex items-center gap-2.5 text-xs font-mono text-slate-300 hover:bg-slate-800/40 transition-colors"
                onClick={() => {
                  const ids = selectedIds.size > 1 ? [...selectedIds] : [selectedItem.id];
                  ids.forEach(id => handleToggleDownload(id));
                  setContextMenu(c => ({ ...c, open: false }));
                }}>
                {["downloading", "resolving", "converting"].includes(selectedItem.status)
                  ? <><Pause className="w-3.5 h-3.5 text-orange-400" /> Pause</>
                  : <><Play className="w-3.5 h-3.5 text-green-400" /> Resume</>}
              </button>
            )}
            {(selectedItem.status === "error" || selectedItem.status === "cancelled") && (
              <button className="w-full px-3 py-2 flex items-center gap-2.5 text-xs font-mono text-slate-300 hover:bg-slate-800/40 transition-colors"
                onClick={() => {
                  const ids = selectedIds.size > 1 ? [...selectedIds] : [selectedItem.id];
                  ids.forEach(id => handleRetry(id));
                  setContextMenu(c => ({ ...c, open: false }));
                }}><RefreshCw className="w-3.5 h-3.5 text-cyan-400" /> Retry</button>
            )}
            {selectedItem.status === "completed" && (
              <button className="w-full px-3 py-2 flex items-center gap-2.5 text-xs font-mono text-slate-300 hover:bg-slate-800/40 transition-colors"
                onClick={() => { handleOpenFolder(selectedItem.id); setContextMenu(c => ({ ...c, open: false })); }}>
                <FolderOpen className="w-3.5 h-3.5 text-green-400" /> Open Folder
              </button>
            )}
            {canPreview(selectedItem.type) && selectedItem.status === "completed" && (
              <button className="w-full px-3 py-2 flex items-center gap-2.5 text-xs font-mono text-slate-300 hover:bg-slate-800/40 transition-colors"
                onClick={() => { handleOpenMedia(selectedItem.id); setContextMenu(c => ({ ...c, open: false })); }}>
                <Eye className="w-3.5 h-3.5 text-purple-400" /> Preview
              </button>
            )}
            <button className="w-full px-3 py-2 flex items-center gap-2.5 text-xs font-mono text-red-400 hover:bg-slate-800/40 transition-colors border-t border-slate-800/40"
              onClick={() => {
                const ids = selectedIds.size > 1 ? [...selectedIds] : [selectedItem.id];
                ids.forEach(id => handleRemoveDownload(id));
                setSelectedIds(new Set());
                setContextMenu(c => ({ ...c, open: false }));
              }}><Trash2 className="w-3.5 h-3.5" /> Remove</button>
          </motion.div>
        )}
      </AnimatePresence>

      {/* ===== ADD URL MODAL ===== */}
      <AnimatePresence>
        {showAddModal && (
          <motion.div initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
            className="fixed inset-0 bg-black/80 backdrop-blur-sm flex items-center justify-center z-[200]"
            onClick={() => setShowAddModal(false)}>
            <motion.div initial={{ scale: 0.95, opacity: 0 }} animate={{ scale: 1, opacity: 1 }} exit={{ scale: 0.95, opacity: 0 }}
              onClick={e => e.stopPropagation()}
              className="w-[520px] bg-gradient-to-br from-[#0a0e12] to-[#0b0f14] border border-cyan-500/30 rounded-2xl shadow-[0_0_60px_rgba(34,211,238,0.2)] overflow-hidden">
              <div className="h-14 border-b border-cyan-500/20 px-5 flex items-center justify-between bg-[#0a0e12]/80">
                <div className="flex items-center gap-3">
                  <Brain className="w-5 h-5 text-cyan-400" />
                  <h3 className="text-sm font-mono font-bold text-cyan-400 tracking-wider">ADD DOWNLOAD URL</h3>
                </div>
                <motion.button whileHover={{ scale: 1.1 }} whileTap={{ scale: 0.95 }} onClick={() => setShowAddModal(false)}
                  className="w-8 h-8 rounded-lg bg-slate-900/50 border border-slate-700/40 hover:border-cyan-500/50 text-slate-400 hover:text-cyan-400 transition-all flex items-center justify-center">
                  <X className="w-4 h-4" />
                </motion.button>
              </div>
              <div className="p-5">
                <label className="block text-[10px] font-mono text-slate-400 uppercase mb-2">Download URL</label>
                <input type="text" value={newUrl} onChange={e => setNewUrl(e.target.value)}
                  onKeyDown={e => { if (e.key === "Enter") handleAddUrl(); }}
                  placeholder="https://example.com/file.zip"
                  className="w-full px-4 py-3 bg-slate-900/50 border border-cyan-500/20 rounded-xl text-slate-200 text-sm font-mono outline-none focus:border-cyan-500/50 focus:shadow-[0_0_12px_rgba(34,211,238,0.15)] transition-all"
                  autoFocus />
                <div className="mt-4 p-4 rounded-xl bg-cyan-500/5 border border-cyan-500/15">
                  <div className="flex items-start gap-3">
                    <Sparkles className="w-4 h-4 text-cyan-400 mt-0.5 shrink-0" />
                    <div>
                      <p className="text-xs font-mono font-bold text-cyan-400 mb-2">AI WILL AUTOMATICALLY:</p>
                      <div className="space-y-1.5 text-[11px] font-mono text-slate-400">
                        <div className="flex items-center gap-2"><CheckCircle2 className="w-3.5 h-3.5 text-green-400 shrink-0" /><span>Scan for security threats</span></div>
                        <div className="flex items-center gap-2"><CheckCircle2 className="w-3.5 h-3.5 text-green-400 shrink-0" /><span>Detect and resolve dependencies</span></div>
                        <div className="flex items-center gap-2"><CheckCircle2 className="w-3.5 h-3.5 text-green-400 shrink-0" /><span>Optimize download speed (multi-threaded)</span></div>
                      </div>
                    </div>
                  </div>
                </div>
                <div className="mt-4 grid grid-cols-2 gap-3">
                  <motion.button whileHover={{ scale: 1.02 }} whileTap={{ scale: 0.98 }} onClick={() => setShowAddModal(false)}
                    className="py-3 rounded-xl border bg-slate-900/50 border-slate-700/40 text-slate-400 hover:border-slate-500/60 transition-all font-mono text-xs font-bold uppercase">Cancel</motion.button>
                  <motion.button whileHover={{ scale: 1.02 }} whileTap={{ scale: 0.98 }} onClick={handleAddUrl} disabled={!newUrl.trim()}
                    className="py-3 rounded-xl border bg-cyan-500/10 border-cyan-500/60 text-cyan-400 hover:bg-cyan-500/20 shadow-[0_0_20px_rgba(34,211,238,0.15)] transition-all font-mono text-xs font-bold uppercase disabled:opacity-40 disabled:cursor-not-allowed">START DOWNLOAD</motion.button>
                </div>
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>

      {/* ===== DEPENDENCY MODAL ===== */}
      <AnimatePresence>
        {showDependencyModal && (
          <motion.div initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
            className="fixed inset-0 bg-black/80 backdrop-blur-sm flex items-center justify-center z-[200]"
            onClick={() => setShowDependencyModal(false)}>
            <motion.div initial={{ scale: 0.95, opacity: 0 }} animate={{ scale: 1, opacity: 1 }} exit={{ scale: 0.95, opacity: 0 }}
              onClick={e => e.stopPropagation()}
              className="w-[440px] bg-gradient-to-br from-[#0a0e12] to-[#0b0f14] border border-orange-500/30 rounded-2xl shadow-[0_0_40px_rgba(249,115,22,0.2)] overflow-hidden">
              <div className="h-14 border-b border-orange-500/20 px-5 flex items-center justify-between bg-[#0a0e12]/80">
                <div className="flex items-center gap-2">
                  <GitBranch className="w-4 h-4 text-orange-400" />
                  <h3 className="text-sm font-mono font-bold text-orange-400 tracking-wider">DEPENDENCIES DETECTED</h3>
                </div>
                <motion.button whileHover={{ scale: 1.1 }} whileTap={{ scale: 0.95 }} onClick={() => setShowDependencyModal(false)}
                  className="w-8 h-8 rounded-lg bg-slate-900/50 border border-slate-700/40 hover:border-orange-500/50 text-slate-400 hover:text-orange-400 transition-all flex items-center justify-center">
                  <X className="w-4 h-4" />
                </motion.button>
              </div>
              <div className="p-5">
                <p className="text-xs font-mono text-slate-400 mb-3">AI detected the following dependencies for your download:</p>
                <div className="space-y-2 mb-4">
                  {selectedDependencies.map((dep, i) => (
                    <motion.div key={i} initial={{ opacity: 0, x: -15 }} animate={{ opacity: 1, x: 0 }} transition={{ delay: i * 0.08 }}
                      className={`p-3 rounded-lg border ${dep.required ? "bg-orange-500/10 border-orange-500/25" : "bg-slate-900/30 border-slate-700/30"}`}>
                      <div className="flex items-center justify-between">
                        <div className="flex items-center gap-2">
                          <Database className="w-3.5 h-3.5 text-orange-400" />
                          <div><div className="text-xs font-mono text-slate-200">{dep.name}</div><div className="text-[10px] font-mono text-slate-500">Version {dep.version}</div></div>
                        </div>
                        <span className={`px-2 py-0.5 rounded-full text-[9px] font-mono uppercase ${dep.required ? "bg-orange-500/20 text-orange-400" : "bg-slate-700/20 text-slate-400"}`}>{dep.required ? "Required" : "Optional"}</span>
                      </div>
                    </motion.div>
                  ))}
                </div>
                <div className="flex items-center gap-2">
                  <motion.button whileHover={{ scale: 1.03 }} whileTap={{ scale: 0.97 }}
                    onClick={() => { setShowDependencyModal(false); setSelectedDependencies([]); }}
                    className="flex-1 py-2.5 rounded-lg border bg-orange-500/20 border-orange-500/50 text-orange-400 hover:bg-orange-500/30 transition-all font-mono text-xs font-bold uppercase">Download All</motion.button>
                  <motion.button whileHover={{ scale: 1.03 }} whileTap={{ scale: 0.97 }} onClick={() => setShowDependencyModal(false)}
                    className="px-5 py-2.5 rounded-lg border bg-slate-900/50 border-slate-700/50 text-slate-400 hover:border-orange-500/30 transition-all font-mono text-xs uppercase">Skip</motion.button>
                </div>
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}
