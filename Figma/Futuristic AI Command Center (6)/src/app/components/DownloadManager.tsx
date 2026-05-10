import { useState, useEffect, useMemo, useRef } from "react";
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
  Settings,
  Mic,
  Zap,
  HardDrive,
  TrendingDown,
  Clock,
  X,
  Copy,
  File,
  Image,
  Music,
  Video,
  Archive,
  Brain,
  Shield,
  Sparkles,
  LightbulbIcon,
  GitBranch,
  RefreshCw,
  Target,
  Network,
  Cpu,
  Activity,
  Gauge,
} from "lucide-react";
import { DownloadManagerSettings } from "./DownloadManagerSettings";

type DownloadStatus =
  | "queued"
  | "downloading"
  | "resolving"
  | "converting"
  | "paused"
  | "completed"
  | "error"
  | "cancelled";
type ThreatLevel = "safe" | "low" | "medium" | "high" | "critical";
type ConfidenceLevel = "low" | "medium" | "high" | "very-high";

interface DownloadItem {
  id: string;
  url: string;
  filename: string;
  filesize: string;
  progress: number;
  speed: string;
  speedBps?: number;
  eta: string;
  status: DownloadStatus;
  type: "file" | "video" | "audio" | "image" | "archive";
  totalBytes?: number;
  bytesDownloaded?: number;
  error?: string;
  threatLevel?: ThreatLevel;
  aiOptimized?: boolean;
  speedBoost?: number;
  dependencies?: string[];
}

interface AIPrediction {
  id: string;
  filename: string;
  reason: string;
  confidence: ConfidenceLevel;
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

export function DownloadManager() {
  const [downloads, setDownloads] = useState<DownloadItem[]>([]);

  const [showAddModal, setShowAddModal] = useState(false);
  const [showSettingsModal, setShowSettingsModal] = useState(false);
  const [newUrl, setNewUrl] = useState("");
  const [micNote, setMicNote] = useState("");
  const micNoteTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  const [contextMenu, setContextMenu] = useState<{
    open: boolean;
    x: number;
    y: number;
    itemId: string;
  }>({ open: false, x: 0, y: 0, itemId: "" });

  type HostDownload = {
    id: string;
    url: string;
    provider?: string;
    resolver?: string | null;
    resolvedUrl?: string | null;
    filename?: string | null;
    outputPath?: string | null;
    status?: string | null;
    progress?: number | null;
    bytesDownloaded?: number | null;
    totalBytes?: number | null;
    speedBps?: number | null;
    etaSeconds?: number | null;
    error?: string | null;
    createdUtc?: string | null;
  };

  type HostUiState = {
    downloads?: HostDownload[];
    outputFolder?: string;
    settings?: unknown;
  };

  const hasBridge = () => {
    try {
      return (
        typeof window !== "undefined" &&
        (window as any).chrome &&
        (window as any).chrome.webview
      );
    } catch {
      return false;
    }
  };

  const post = (type: string, payload: any = {}) => {
    const msg = { type, payload };
    try {
      if (hasBridge()) (window as any).chrome.webview.postMessage(msg);
    } catch {
    }
  };

  const formatBytes = (n: number) => {
    if (!Number.isFinite(n) || n <= 0) return "0 B";
    const units = ["B", "KB", "MB", "GB", "TB"];
    let u = 0;
    let v = n;
    while (v >= 1024 && u < units.length - 1) {
      v /= 1024;
      u++;
    }
    return `${v.toFixed(u === 0 ? 0 : 2)} ${units[u]}`;
  };

  const formatSpeed = (bps: number) => `${formatBytes(bps)}/s`;

  const formatEtaSeconds = (sec: number) => {
    if (!Number.isFinite(sec) || sec <= 0) return "—";
    const s = Math.floor(sec % 60);
    const m = Math.floor((sec / 60) % 60);
    const h = Math.floor(sec / 3600);
    if (h > 0) return `${h}h ${m}m`;
    if (m > 0) return `${m}m ${s}s`;
    return `${s}s`;
  };

  const inferType = (filename: string): DownloadItem["type"] => {
    const name = (filename || "").toLowerCase();
    const ext = name.includes(".") ? name.split(".").pop() || "" : "";
    if (["mp4", "mkv", "mov", "avi", "webm"].includes(ext)) return "video";
    if (["mp3", "wav", "flac", "aac", "m4a", "ogg"].includes(ext))
      return "audio";
    if (["png", "jpg", "jpeg", "gif", "webp", "bmp"].includes(ext))
      return "image";
    if (["zip", "rar", "7z", "tar", "gz", "bz2"].includes(ext))
      return "archive";
    return "file";
  };

  const normalizeStatus = (raw?: string | null): DownloadStatus => {
    const s = (raw || "").toLowerCase();
    if (s === "downloading") return "downloading";
    if (s === "resolving") return "resolving";
    if (s === "converting") return "converting";
    if (s === "queued") return "queued";
    if (s === "paused") return "paused";
    if (s === "completed") return "completed";
    if (s === "cancelled") return "cancelled";
    if (s === "error") return "error";
    return "queued";
  };

  const mapHostDownload = (d: HostDownload): DownloadItem => {
    const url = (d.url || "").toString();
    const status = normalizeStatus(d.status);

    const totalBytes = Number(d.totalBytes ?? 0) || 0;
    const bytesDownloaded = Number(d.bytesDownloaded ?? 0) || 0;
    const progressFromHost = Number(d.progress ?? 0);
    
    // Progress is already in percentage (0-100) from the backend
    const progress =
      Number.isFinite(progressFromHost) && progressFromHost > 0
        ? Math.max(0, Math.min(100, progressFromHost))
        : totalBytes > 0
          ? Math.max(0, Math.min(100, (bytesDownloaded / totalBytes) * 100))
          : 0;

    const filenameRaw = (d.filename || "").toString().trim();
    const filename = filenameRaw || url.split("/").pop() || url || "(unknown)";
    const speedBps = Number(d.speedBps ?? 0) || 0;
    const etaSeconds = Number(d.etaSeconds ?? 0) || 0;

    const filesize = totalBytes > 0 ? formatBytes(totalBytes) : "—";
    const speed = speedBps > 0 ? formatSpeed(speedBps) : "0 B/s";
    const eta = status === "completed" ? "Complete" : formatEtaSeconds(etaSeconds);

    return {
      id: (d.id || "").toString(),
      url,
      filename,
      filesize,
      progress: status === "completed" ? 100 : progress,
      speed,
      speedBps,
      eta,
      status,
      type: inferType(filename),
      totalBytes,
      bytesDownloaded,
      error: (d.error || "").toString(),
    };
  };

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
    };

    (window as any).chrome.webview.addEventListener("message", handler);
    post("downloader.getState", {});

    return () => {
      try {
        (window as any).chrome.webview.removeEventListener("message", handler);
      } catch {
      }
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Receive a mic transcript fill from the host — fills the URL input only; never auto-downloads.
  useEffect(() => {
    if (!hasBridge()) return;
    const micHandler = (event: any) => {
      const msg = event?.data;
      if (!msg || msg.type !== "downloader.mic.fillUrl") return;
      const transcript = (msg.payload?.transcript ?? "").trim();
      if (!transcript) return;
      setNewUrl(transcript);
      setShowAddModal(true);
      showMicNote("Voice captured — press Initialize Download.");
    };
    (window as any).chrome.webview.addEventListener("message", micHandler);
    return () => {
      try { (window as any).chrome.webview.removeEventListener("message", micHandler); } catch {}
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (!contextMenu.open) return;
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") setContextMenu((c) => ({ ...c, open: false }));
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [contextMenu.open]);

  const selectedItem = useMemo(
    () => downloads.find((d) => d.id === contextMenu.itemId) || null,
    [contextMenu.itemId, downloads]
  );

  const particles = useMemo(
    () =>
      Array.from({ length: 30 }, () => ({
        left: Math.random() * 100,
        top: Math.random() * 100,
        duration: 2 + Math.random() * 2,
        delay: Math.random() * 2,
      })),
    []
  );

  const showMicNote = (note: string) => {
    setMicNote(note);
    if (micNoteTimer.current) clearTimeout(micNoteTimer.current);
    micNoteTimer.current = setTimeout(() => setMicNote(""), 2400);
  };

  // Mic is not wired for Downloads — show honest inline note; never auto-download.
  const handleMicClick = () => {
    showMicNote("Mic not wired");
  };

  const handleAddUrl = () => {
    if (!newUrl.trim()) return;

    post("downloader.addUrls", { provider: "Auto", urls: [newUrl.trim()] });
    setNewUrl("");
    setShowAddModal(false);
  };

  const handleImportCsv = () => {
    post("downloader.importCsv", {});
  };

  const handleToggleDownload = (id: string) => {
    const item = downloads.find((d) => d.id === id);
    if (!item) return;
    if (
      item.status === "downloading" ||
      item.status === "resolving" ||
      item.status === "converting"
    ) {
      post("downloader.pause", { id });
      return;
    }
    if (item.status === "paused" || item.status === "queued") {
      post("downloader.resume", { id });
    }
  };

  const handleRemoveDownload = (id: string) => {
    const item = downloads.find((d) => d.id === id);
    if (!item) return;

    if (
      item.status === "downloading" ||
      item.status === "resolving" ||
      item.status === "converting" ||
      item.status === "queued" ||
      item.status === "paused"
    ) {
      post("downloader.cancel", { id });
      return;
    }

    post("downloader.remove", { id });
  };

  const getStatusColor = (status: DownloadStatus) => {
    switch (status) {
      case "downloading":
      case "resolving":
      case "converting":
        return "#22d3ee";
      case "completed":
        return "#22c55e";
      case "error":
        return "#ef4444";
      case "cancelled":
        return "#ef4444";
      case "paused":
        return "#f97316";
      default:
        return "#64748b";
    }
  };

  const getThreatColor = (level?: ThreatLevel) => {
    switch (level) {
      case "safe":
        return "#22c55e";
      case "low":
        return "#84cc16";
      case "medium":
        return "#f59e0b";
      case "high":
        return "#f97316";
      case "critical":
        return "#ef4444";
      default:
        return "#64748b";
    }
  };

  const getConfidenceColor = (level: ConfidenceLevel) => {
    switch (level) {
      case "very-high":
        return "#22c55e";
      case "high":
        return "#84cc16";
      case "medium":
        return "#f59e0b";
      case "low":
        return "#f97316";
      default:
        return "#64748b";
    }
  };

  const getStatusIcon = (status: DownloadStatus) => {
    switch (status) {
      case "downloading":
      case "resolving":
      case "converting":
        return <TrendingDown className="w-4 h-4" />;
      case "completed":
        return <CheckCircle2 className="w-4 h-4" />;
      case "error":
        return <XCircle className="w-4 h-4" />;
      case "cancelled":
        return <XCircle className="w-4 h-4" />;
      case "paused":
        return <Pause className="w-4 h-4" />;
      default:
        return <Clock className="w-4 h-4" />;
    }
  };

  const getFileIcon = (type: string) => {
    switch (type) {
      case "video":
        return <Video className="w-5 h-5" />;
      case "audio":
        return <Music className="w-5 h-5" />;
      case "image":
        return <Image className="w-5 h-5" />;
      case "archive":
        return <Archive className="w-5 h-5" />;
      default:
        return <File className="w-5 h-5" />;
    }
  };

  const stats = useMemo(() => {
    const active = downloads.filter(
      (d) =>
        d.status === "downloading" ||
        d.status === "resolving" ||
        d.status === "converting"
    ).length;
    const queued = downloads.filter(
      (d) => d.status === "queued" || d.status === "paused"
    ).length;
    const completed = downloads.filter((d) => d.status === "completed").length;
    const errors = downloads.filter(
      (d) => d.status === "error" || d.status === "cancelled"
    ).length;
    const totalSpeedBps = downloads
      .filter((d) => d.status === "downloading" || d.status === "resolving")
      .reduce((sum, d) => sum + (Number(d.speedBps ?? 0) || 0), 0);
    const totalSpeedMBps = totalSpeedBps / (1024 * 1024);
    return { active, queued, completed, errors, totalSpeedMBps };
  }, [downloads]);

  return (
    <div className="flex-1 flex overflow-hidden bg-gradient-to-br from-[#0a0e12] to-[#0b0f14]">
      {/* Main Content */}
      <div className="flex-1 flex flex-col overflow-hidden">
        {/* Header */}
        <div className="h-20 border-b border-cyan-500/10 px-6 flex items-center justify-between bg-[#0a0e12]/80 backdrop-blur-sm relative overflow-hidden">
          {/* Animated background grid */}
          <div className="absolute inset-0 opacity-10 pointer-events-none">
            {[...Array(20)].map((_, i) => (
              <motion.div
                key={i}
                className="absolute h-px bg-gradient-to-r from-transparent via-cyan-500 to-transparent"
                style={{ top: `${i * 5}%`, left: 0, right: 0 }}
                animate={{
                  x: ["-100%", "100%"],
                }}
                transition={{
                  duration: 3 + i * 0.2,
                  repeat: Infinity,
                  ease: "linear",
                }}
              />
            ))}
          </div>

          <div className="flex items-center gap-4 relative z-10">
            <motion.div
              className="relative"
              animate={{ rotate: 360 }}
              transition={{ duration: 8, repeat: Infinity, ease: "linear" }}
            >
              <Download className="w-6 h-6 text-cyan-400" />
              <motion.div
                className="absolute inset-0 bg-cyan-400 rounded-full blur-xl"
                animate={{ opacity: [0.3, 0.6, 0.3] }}
                transition={{ duration: 2, repeat: Infinity }}
              />
            </motion.div>
            <div>
              <h2 className="text-lg font-mono tracking-wider text-cyan-400">
                AI NEURAL DOWNLOAD MATRIX
              </h2>
              <p className="text-xs text-slate-500 font-mono mt-0.5">
                Real downloads • Live progress tracking
              </p>
            </div>
          </div>

          {/* Action Buttons */}
          <div className="flex items-center gap-3 relative z-10">
            <motion.button
              whileHover={{ scale: 1.05, boxShadow: "0 0 20px rgba(34,211,238,0.5)" }}
              whileTap={{ scale: 0.95 }}
              onClick={() => setShowAddModal(true)}
              className="flex items-center gap-2 px-4 py-2 rounded-lg border bg-cyan-500/20 border-cyan-500/50 text-cyan-400 hover:bg-cyan-500/30 shadow-[0_0_15px_rgba(34,211,238,0.3)] transition-all relative overflow-hidden"
            >
              <motion.div
                className="absolute inset-0 bg-gradient-to-r from-transparent via-white/20 to-transparent"
                animate={{ x: ["-100%", "200%"] }}
                transition={{ duration: 2, repeat: Infinity }}
              />
              <Plus className="w-4 h-4 relative z-10" />
              <span className="text-xs font-mono uppercase relative z-10">Add URL</span>
            </motion.button>

            <motion.button
              whileHover={{ scale: 1.05, boxShadow: "0 0 20px rgba(168,85,247,0.5)" }}
              whileTap={{ scale: 0.95 }}
              onClick={handleImportCsv}
              className="flex items-center gap-2 px-4 py-2 rounded-lg border bg-purple-500/20 border-purple-500/50 text-purple-400 hover:bg-purple-500/30 shadow-[0_0_15px_rgba(168,85,247,0.3)] transition-all"
            >
              <FileText className="w-4 h-4" />
              <span className="text-xs font-mono uppercase">CSV Batch</span>
            </motion.button>

            <motion.button
              whileHover={{ scale: 1.05 }}
              whileTap={{ scale: 0.95 }}
              onClick={() => setShowSettingsModal(true)}
              className="flex items-center gap-2 px-4 py-2 rounded-lg border bg-slate-900/50 border-slate-700/50 text-slate-400 hover:border-cyan-500/30"
            >
              <Settings className="w-4 h-4" />
            </motion.button>
          </div>
        </div>

        {/* AI Stats Dashboard */}
        <div className="h-32 border-b border-cyan-500/10 px-6 flex items-center gap-6 bg-[#0a0e12]/60 relative overflow-hidden">
          {/* Floating particles */}
          {particles.map((p, i) => (
            <motion.div
              key={i}
              className="absolute w-1 h-1 bg-cyan-400 rounded-full pointer-events-none"
              style={{
                left: `${p.left}%`,
                top: `${p.top}%`,
              }}
              animate={{
                y: [0, -20, 0],
                opacity: [0, 1, 0],
              }}
              transition={{
                duration: p.duration,
                repeat: Infinity,
                delay: p.delay,
              }}
            />
          ))}

          {/* Active Downloads */}
          <motion.div
            className="relative"
            whileHover={{ scale: 1.05 }}
          >
            <div className="flex items-center gap-3">
              <div className="relative p-3 rounded-lg bg-cyan-500/10 border border-cyan-500/30">
                <TrendingDown className="w-5 h-5 text-cyan-400 relative z-10" />
                <motion.div
                  className="absolute inset-0 bg-cyan-400 rounded-lg blur-xl"
                  animate={{ opacity: [0.2, 0.4, 0.2] }}
                  transition={{ duration: 2, repeat: Infinity }}
                />
              </div>
              <div>
                <div className="text-xs text-slate-500 font-mono uppercase flex items-center gap-2">
                  Active Downloads
                  <motion.div
                    className="w-2 h-2 bg-cyan-400 rounded-full"
                    animate={{ scale: [1, 1.5, 1] }}
                    transition={{ duration: 1, repeat: Infinity }}
                  />
                </div>
                <div className="text-lg font-mono font-bold text-cyan-400">
                  {stats.active} / {downloads.length}
                </div>
              </div>
            </div>
          </motion.div>

          {/* Neural Speed */}
          <motion.div
            className="relative"
            whileHover={{ scale: 1.05 }}
          >
            <div className="flex items-center gap-3">
              <div className="relative p-3 rounded-lg bg-orange-500/10 border border-orange-500/30">
                <Zap className="w-5 h-5 text-orange-400 relative z-10" />
                <motion.div
                  className="absolute inset-0 bg-orange-400 rounded-lg blur-xl"
                  animate={{ opacity: [0.2, 0.4, 0.2] }}
                  transition={{ duration: 1.5, repeat: Infinity }}
                />
              </div>
              <div>
                <div className="text-xs text-slate-500 font-mono uppercase">
                  Neural Speed
                </div>
                <div className="text-lg font-mono font-bold text-orange-400">
                  {stats.totalSpeedMBps.toFixed(1)} MB/s
                </div>
              </div>
            </div>
          </motion.div>

          {/* Queued */}
          <motion.div
            className="relative"
            whileHover={{ scale: 1.05 }}
          >
            <div className="flex items-center gap-3">
              <div className="relative p-3 rounded-lg bg-green-500/10 border border-green-500/30">
                <Clock className="w-5 h-5 text-green-400 relative z-10" />
                <motion.div
                  className="absolute inset-0 bg-green-400 rounded-lg blur-xl"
                  animate={{ opacity: [0.2, 0.4, 0.2] }}
                  transition={{ duration: 2.5, repeat: Infinity }}
                />
              </div>
              <div>
                <div className="text-xs text-slate-500 font-mono uppercase">
                  Queued
                </div>
                <div className="text-lg font-mono font-bold text-green-400">
                  {stats.queued}
                </div>
              </div>
            </div>
          </motion.div>

          {/* Completed */}
          <motion.div
            className="relative"
            whileHover={{ scale: 1.05 }}
          >
            <div className="flex items-center gap-3">
              <div className="relative p-3 rounded-lg bg-red-500/10 border border-red-500/30">
                <CheckCircle2 className="w-5 h-5 text-red-400 relative z-10" />
                <motion.div
                  className="absolute inset-0 bg-red-400 rounded-lg blur-xl"
                  animate={{ opacity: [0.2, 0.4, 0.2] }}
                  transition={{ duration: 1.8, repeat: Infinity }}
                />
              </div>
              <div>
                <div className="text-xs text-slate-500 font-mono uppercase">
                  Completed
                </div>
                <div className="text-lg font-mono font-bold text-red-400">
                  {stats.completed}
                </div>
              </div>
            </div>
          </motion.div>

          {/* Errors */}
          <motion.div
            className="relative"
            whileHover={{ scale: 1.05 }}
          >
            <div className="flex items-center gap-3">
              <div className="relative p-3 rounded-lg bg-purple-500/10 border border-purple-500/30">
                <AlertCircle className="w-5 h-5 text-purple-400 relative z-10" />
                <motion.div
                  className="absolute inset-0 bg-purple-400 rounded-lg blur-xl"
                  animate={{ opacity: [0.2, 0.4, 0.2] }}
                  transition={{ duration: 2.2, repeat: Infinity }}
                />
              </div>
              <div>
                <div className="text-xs text-slate-500 font-mono uppercase">
                  Errors
                </div>
                <div className="text-lg font-mono font-bold text-purple-400">
                  {stats.errors}
                </div>
              </div>
            </div>
          </motion.div>
        </div>

        {/* Content Area with Sidebar */}
        <div className="flex-1 flex overflow-hidden">
          {/* Main Download List */}
          <div className="flex-1 overflow-y-scroll p-6 atlas-scrollbar">
            <div className="space-y-3">
              {downloads.map((item, index) => (
                <motion.div
                  key={item.id}
                  initial={{ opacity: 0, x: -20 }}
                  animate={{ opacity: 1, x: 0 }}
                  transition={{ delay: index * 0.05 }}
                  onMouseDown={(e) => {
                    // WebView2 can be inconsistent with contextmenu events depending on settings;
                    // handle right-mouse-down as a reliable fallback.
                    if (e.button !== 2) return;
                    e.preventDefault();
                    setContextMenu({
                      open: true,
                      x: e.clientX,
                      y: e.clientY,
                      itemId: item.id,
                    });
                  }}
                  onContextMenu={(e) => {
                    e.preventDefault();
                    setContextMenu({
                      open: true,
                      x: e.clientX,
                      y: e.clientY,
                      itemId: item.id,
                    });
                  }}
                  className="relative p-4 rounded-lg border bg-slate-900/30 border-slate-800 hover:border-cyan-500/30 hover:bg-slate-900/50 transition-all overflow-hidden group"
                >
                  {/* Top Row - File Info */}
                  <div className="flex items-start gap-4 mb-3 relative z-10">
                    {/* File Icon */}
                    <div
                      className="relative p-3 rounded-lg border"
                      style={{
                        backgroundColor: `${getStatusColor(item.status)}20`,
                        borderColor: `${getStatusColor(item.status)}50`,
                      }}
                    >
                      <div style={{ color: getStatusColor(item.status) }}>
                        {getFileIcon(item.type)}
                      </div>
                      {item.aiOptimized && (
                        <motion.div
                          className="absolute -top-1 -right-1 p-1 bg-cyan-500 rounded-full"
                          animate={{ scale: [1, 1.2, 1] }}
                          transition={{ duration: 2, repeat: Infinity }}
                        >
                          <Sparkles className="w-3 h-3 text-white" />
                        </motion.div>
                      )}
                    </div>


                  {/* Context menu overlay */}
                  <AnimatePresence>
                    {contextMenu.open && selectedItem && item.id === selectedItem.id && (
                      <motion.div
                        initial={{ opacity: 0 }}
                        animate={{ opacity: 1 }}
                        exit={{ opacity: 0 }}
                        className="fixed inset-0 z-50"
                        onMouseDown={() => setContextMenu((c) => ({ ...c, open: false }))}
                      >
                        <motion.div
                          initial={{ opacity: 0, scale: 0.98 }}
                          animate={{ opacity: 1, scale: 1 }}
                          exit={{ opacity: 0, scale: 0.98 }}
                          transition={{ duration: 0.12 }}
                          style={{ left: contextMenu.x, top: contextMenu.y }}
                          className="absolute w-56 rounded-lg border border-slate-700/60 bg-slate-950/90 backdrop-blur-md shadow-[0_0_15px_rgba(34,211,238,0.15)] overflow-hidden"
                          onMouseDown={(e) => e.stopPropagation()}
                        >
                          <div className="px-3 py-2 border-b border-slate-800/60">
                            <div className="text-[10px] font-mono uppercase text-slate-500">Actions</div>
                            <div className="text-xs font-mono text-slate-200 truncate">{selectedItem.filename}</div>
                          </div>

                          <button
                            className="w-full px-3 py-2 flex items-center gap-2 text-xs font-mono text-slate-300 hover:bg-slate-800/40"
                            onClick={() => {
                              try {
                                navigator.clipboard.writeText(selectedItem.url);
                              } catch {
                              }
                              setContextMenu((c) => ({ ...c, open: false }));
                            }}
                          >
                            <Copy className="w-4 h-4 text-cyan-400" />
                            Copy URL
                          </button>

                          {(selectedItem.status === "downloading" ||
                            selectedItem.status === "resolving" ||
                            selectedItem.status === "converting" ||
                            selectedItem.status === "paused") && (
                            <button
                              className="w-full px-3 py-2 flex items-center gap-2 text-xs font-mono text-slate-300 hover:bg-slate-800/40"
                              onClick={() => {
                                handleToggleDownload(selectedItem.id);
                                setContextMenu((c) => ({ ...c, open: false }));
                              }}
                            >
                              {selectedItem.status === "paused" ? (
                                <Play className="w-4 h-4 text-orange-400" />
                              ) : (
                                <Pause className="w-4 h-4 text-orange-400" />
                              )}
                              {selectedItem.status === "paused" ? "Resume" : "Pause"}
                            </button>
                          )}

                          {selectedItem.status === "error" && (
                            <button
                              className="w-full px-3 py-2 flex items-center gap-2 text-xs font-mono text-slate-300 hover:bg-slate-800/40"
                              onClick={() => {
                                post("downloader.retry", { id: selectedItem.id });
                                setContextMenu((c) => ({ ...c, open: false }));
                              }}
                            >
                              <RefreshCw className="w-4 h-4 text-cyan-400" />
                              Retry
                            </button>
                          )}

                          {(selectedItem.status === "downloading" ||
                            selectedItem.status === "resolving" ||
                            selectedItem.status === "converting" ||
                            selectedItem.status === "paused" ||
                            selectedItem.status === "queued") && (
                            <button
                              className="w-full px-3 py-2 flex items-center gap-2 text-xs font-mono text-slate-300 hover:bg-slate-800/40"
                              onClick={() => {
                                post("downloader.cancel", { id: selectedItem.id });
                                setContextMenu((c) => ({ ...c, open: false }));
                              }}
                            >
                              <X className="w-4 h-4 text-purple-400" />
                              Cancel
                            </button>
                          )}

                          {selectedItem.status === "completed" && (
                            <button
                              className="w-full px-3 py-2 flex items-center gap-2 text-xs font-mono text-slate-300 hover:bg-slate-800/40"
                              onClick={() => {
                                post("downloader.openFolder", { id: selectedItem.id });
                                setContextMenu((c) => ({ ...c, open: false }));
                              }}
                            >
                              <FolderOpen className="w-4 h-4 text-green-400" />
                              Open Folder
                            </button>
                          )}

                          <button
                            className="w-full px-3 py-2 flex items-center gap-2 text-xs font-mono text-slate-300 hover:bg-slate-800/40"
                            onClick={() => {
                              handleRemoveDownload(selectedItem.id);
                              setContextMenu((c) => ({ ...c, open: false }));
                            }}
                          >
                            <Trash2 className="w-4 h-4 text-red-400" />
                            Remove
                          </button>
                        </motion.div>
                      </motion.div>
                    )}
                  </AnimatePresence>
                    {/* File Details */}
                    <div className="flex-1 min-w-0">
                      <div className="flex items-center gap-2 mb-1 flex-wrap">
                        <h4 className="text-sm font-mono font-bold text-slate-200 truncate">
                          {item.filename}
                        </h4>
                        <div
                          className="flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-mono uppercase"
                          style={{
                            backgroundColor: `${getStatusColor(item.status)}20`,
                            color: getStatusColor(item.status),
                          }}
                        >
                          {getStatusIcon(item.status)}
                          <span>{item.status}</span>
                        </div>

                        {item.threatLevel && (
                          <motion.div
                            className="flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-mono uppercase"
                            style={{
                              backgroundColor: `${getThreatColor(item.threatLevel)}20`,
                              color: getThreatColor(item.threatLevel),
                              borderWidth: 1,
                              borderColor: `${getThreatColor(item.threatLevel)}50`,
                            }}
                            animate={{
                              boxShadow:
                                item.threatLevel !== "safe"
                                  ? [
                                      `0 0 5px ${getThreatColor(item.threatLevel)}50`,
                                      `0 0 15px ${getThreatColor(item.threatLevel)}80`,
                                      `0 0 5px ${getThreatColor(item.threatLevel)}50`,
                                    ]
                                  : "none",
                            }}
                            transition={{ duration: 2, repeat: Infinity }}
                          >
                            <Shield className="w-3 h-3" />
                            <span>{item.threatLevel}</span>
                          </motion.div>
                        )}

                        {item.aiOptimized && item.speedBoost && (
                          <motion.div
                            className="flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-mono uppercase bg-cyan-500/20 text-cyan-400 border border-cyan-500/50"
                            animate={{
                              boxShadow: [
                                "0 0 5px rgba(34,211,238,0.5)",
                                "0 0 15px rgba(34,211,238,0.8)",
                                "0 0 5px rgba(34,211,238,0.5)",
                              ],
                            }}
                            transition={{ duration: 2, repeat: Infinity }}
                          >
                            <Zap className="w-3 h-3" />
                            <span>+{item.speedBoost}%</span>
                          </motion.div>
                        )}

                        {item.dependencies && item.dependencies.length > 0 && (
                          <div className="flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-mono uppercase bg-amber-500/20 text-amber-400 border border-amber-500/50">
                            <GitBranch className="w-3 h-3" />
                            <span>{item.dependencies.length} deps</span>
                          </div>
                        )}
                      </div>

                      <div className="flex items-center gap-3 text-xs text-slate-500 font-mono">
                        <span>{item.filesize}</span>
                        <span>•</span>
                        <span className="truncate max-w-md">{item.url}</span>
                      </div>
                    </div>

                    {/* Action Buttons */}
                    <div className="flex items-center gap-2 opacity-0 group-hover:opacity-100 transition-opacity">
                      <motion.button
                        whileHover={{ scale: 1.1 }}
                        whileTap={{ scale: 0.9 }}
                        onClick={() => navigator.clipboard.writeText(item.url)}
                        className="p-2 rounded-lg bg-slate-800/50 border border-slate-700/50 hover:border-cyan-500/50 text-slate-400 hover:text-cyan-400 transition-all"
                        title="Copy URL"
                      >
                        <Copy className="w-4 h-4" />
                      </motion.button>

                      {(item.status === "downloading" ||
                        item.status === "resolving" ||
                        item.status === "converting" ||
                        item.status === "paused") && (
                        <motion.button
                          whileHover={{ scale: 1.1 }}
                          whileTap={{ scale: 0.9 }}
                          onClick={() => handleToggleDownload(item.id)}
                          className="p-2 rounded-lg bg-slate-800/50 border border-slate-700/50 hover:border-orange-500/50 text-slate-400 hover:text-orange-400 transition-all"
                          title={
                            item.status === "paused" ? "Resume" : "Pause"
                          }
                        >
                          {item.status === "paused" ? (
                            <Play className="w-4 h-4" />
                          ) : (
                            <Pause className="w-4 h-4" />
                          )}
                        </motion.button>
                      )}

                      {item.status === "completed" && (
                        <motion.button
                          whileHover={{ scale: 1.1 }}
                          whileTap={{ scale: 0.9 }}
                          onClick={() => post("downloader.openFolder", { id: item.id })}
                          className="p-2 rounded-lg bg-slate-800/50 border border-slate-700/50 hover:border-green-500/50 text-slate-400 hover:text-green-400 transition-all"
                          title="Open File"
                        >
                          <FolderOpen className="w-4 h-4" />
                        </motion.button>
                      )}

                      <motion.button
                        whileHover={{ scale: 1.1 }}
                        whileTap={{ scale: 0.9 }}
                        onClick={() => handleRemoveDownload(item.id)}
                        className="p-2 rounded-lg bg-slate-800/50 border border-slate-700/50 hover:border-red-500/50 text-slate-400 hover:text-red-400 transition-all"
                        title="Remove"
                      >
                        <Trash2 className="w-4 h-4" />
                      </motion.button>
                    </div>
                  </div>

                  {/* Progress Bar */}
                  {item.status !== "completed" && (
                    <div className="mb-2 relative z-10">
                      <div className="relative h-2 bg-slate-900 rounded-full overflow-hidden">
                        <motion.div
                          className="absolute inset-y-0 left-0 rounded-full"
                          style={{
                            background: `linear-gradient(to right, ${getStatusColor(
                              item.status
                            )}, ${getStatusColor(item.status)}90)`,
                            boxShadow: `0 0 10px ${getStatusColor(item.status)}50`,
                          }}
                          initial={{ width: 0 }}
                          animate={{
                            width: `${item.progress}%`,
                          }}
                          transition={{ duration: 0.5 }}
                        />
                        {/* Animated shimmer */}
                        {(item.status === "downloading" ||
                          item.status === "resolving" ||
                          item.status === "converting") && (
                          <motion.div
                            className="absolute inset-0 bg-gradient-to-r from-transparent via-white/30 to-transparent"
                            animate={{ x: ["-100%", "200%"] }}
                            transition={{ duration: 1.5, repeat: Infinity }}
                          />
                        )}
                      </div>
                    </div>
                  )}

                  {/* Bottom Row - Stats */}
                  <div className="flex items-center justify-between text-[10px] font-mono relative z-10">
                    <div className="flex items-center gap-4 text-slate-600">
                      <span>
                        {`${item.progress.toFixed(1)}%`}
                      </span>
                      {(item.status === "downloading" ||
                        item.status === "resolving" ||
                        item.status === "converting") && (
                        <>
                          <span>•</span>
                          <span className="text-cyan-400">{item.speed}</span>
                          <span>•</span>
                          <span className="text-orange-400">ETA: {item.eta}</span>
                        </>
                      )}
                    </div>
                  </div>

                  {/* Dependencies Alert */}
                  {item.dependencies && item.dependencies.length > 0 && (
                    <motion.div
                      initial={{ opacity: 0, height: 0 }}
                      animate={{ opacity: 1, height: "auto" }}
                      className="mt-3 p-2 rounded-lg bg-amber-500/10 border border-amber-500/30 relative z-10"
                    >
                      <div className="flex items-center gap-2 text-xs text-amber-400 font-mono">
                        <GitBranch className="w-3 h-3" />
                        <span>
                          Dependencies detected:{" "}
                          {item.dependencies.join(", ")}
                        </span>
                      </div>
                    </motion.div>
                  )}
                </motion.div>
              ))}
            </div>

            {downloads.length === 0 && (
              <div className="flex flex-col items-center justify-center h-full">
                <motion.div
                  animate={{ rotate: 360 }}
                  transition={{ duration: 8, repeat: Infinity, ease: "linear" }}
                >
                  <Download className="w-16 h-16 text-slate-700 mb-4" />
                </motion.div>
                <p className="text-slate-500 font-mono text-sm">
                  No downloads in neural queue
                </p>
                <p className="text-slate-600 font-mono text-xs mt-2">
                  Awaiting download protocols...
                </p>
              </div>
            )}
          </div>

        </div>
      </div>

      {/* Add URL Modal */}
      <AnimatePresence>
        {showAddModal && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="absolute inset-0 bg-black/80 backdrop-blur-md flex items-center justify-center z-50"
            onClick={() => setShowAddModal(false)}
          >
            <motion.div
              initial={{ scale: 0.9, opacity: 0 }}
              animate={{ scale: 1, opacity: 1 }}
              exit={{ scale: 0.9, opacity: 0 }}
              onClick={(e) => e.stopPropagation()}
              className="w-[600px] bg-gradient-to-br from-[#0a0e12] to-[#0b0f14] border border-cyan-500/30 rounded-2xl shadow-[0_0_60px_rgba(34,211,238,0.3)] overflow-hidden relative"
            >
              {/* Animated border glow */}
              <motion.div
                className="absolute inset-0 rounded-2xl"
                style={{
                  background:
                    "linear-gradient(45deg, transparent, rgba(34,211,238,0.3), transparent)",
                }}
                animate={{ rotate: 360 }}
                transition={{ duration: 4, repeat: Infinity, ease: "linear" }}
              />

              {/* Modal Header */}
              <div className="h-16 border-b border-cyan-500/20 px-6 flex items-center justify-between bg-[#0a0e12]/80 relative z-10">
                <div className="flex items-center gap-3">
                  <Link className="w-5 h-5 text-cyan-400" />
                  <h3 className="text-lg font-mono font-bold text-cyan-400 tracking-wider">
                    INITIATE DOWNLOAD
                  </h3>
                </div>
                <motion.button
                  whileHover={{ scale: 1.1, rotate: 90 }}
                  whileTap={{ scale: 0.9 }}
                  onClick={() => setShowAddModal(false)}
                  className="p-2 rounded-lg bg-slate-900/50 border border-slate-700/50 hover:border-cyan-500/50 text-slate-400 hover:text-cyan-400 transition-all"
                >
                  <X className="w-5 h-5" />
                </motion.button>
              </div>

              {/* Modal Content */}
              <div className="p-6 relative z-10">
                <label className="block text-xs font-mono text-slate-400 uppercase mb-2">
                  Target URL
                </label>
                <div className="flex items-stretch gap-2">
                  <input
                    type="text"
                    value={newUrl}
                    onChange={(e) => setNewUrl(e.target.value)}
                    onKeyPress={(e) => e.key === "Enter" && handleAddUrl()}
                    placeholder="https://neural.network/quantum-data.zip"
                    className="flex-1 px-4 py-3 bg-slate-900/50 border border-cyan-500/20 rounded-lg text-slate-200 text-sm font-mono outline-none focus:border-cyan-500/50 focus:shadow-[0_0_15px_rgba(34,211,238,0.2)] transition-all"
                    autoFocus
                  />
                  <button
                    type="button"
                    onClick={handleMicClick}
                    title="URL mic"
                    className="px-3 rounded-lg border bg-slate-900/50 border-slate-700/50 text-slate-400 hover:border-cyan-500/30 hover:text-cyan-400 transition-all"
                  >
                    <Mic className="w-4 h-4" />
                  </button>
                </div>
                {micNote && (
                  <p className="mt-1 text-xs font-mono text-slate-400">{micNote}</p>
                )}

                <div className="mt-4 p-3 rounded-lg bg-cyan-500/10 border border-cyan-500/20">
                  <p className="text-xs text-slate-400 font-mono">
                    <span className="text-cyan-400">FEATURES:</span> Provider resolving
                    • Live progress • Pause/Resume • Output folder integration
                  </p>
                </div>

                <div className="mt-6 flex items-center gap-3">
                  <motion.button
                    whileHover={{
                      scale: 1.05,
                      boxShadow: "0 0 20px rgba(34,211,238,0.5)",
                    }}
                    whileTap={{ scale: 0.95 }}
                    onClick={handleAddUrl}
                    className="flex-1 px-4 py-3 rounded-lg border bg-cyan-500/20 border-cyan-500/50 text-cyan-400 hover:bg-cyan-500/30 transition-all font-mono text-sm uppercase relative overflow-hidden"
                  >
                    <motion.div
                      className="absolute inset-0 bg-gradient-to-r from-transparent via-white/20 to-transparent"
                      animate={{ x: ["-100%", "200%"] }}
                      transition={{ duration: 2, repeat: Infinity }}
                    />
                    <span className="relative z-10">Initialize Download</span>
                  </motion.button>
                  <motion.button
                    whileHover={{ scale: 1.05 }}
                    whileTap={{ scale: 0.95 }}
                    onClick={() => setShowAddModal(false)}
                    className="px-4 py-3 rounded-lg border bg-slate-900/50 border-slate-700/50 text-slate-400 hover:border-cyan-500/30 transition-all font-mono text-sm uppercase"
                  >
                    Cancel
                  </motion.button>
                </div>
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>

      {/* Settings Modal */}
      <DownloadManagerSettings
        isOpen={showSettingsModal}
        onClose={() => setShowSettingsModal(false)}
      />
    </div>
  );
}
