import { useState, useEffect } from "react";
import { motion, AnimatePresence } from "motion/react";
import {
  Shield,
  HardDrive,
  Cpu,
  Zap,
  Activity,
  Send,
  AlertTriangle,
  CheckCircle2,
  XCircle,
  Brain,
  Eye,
  Network,
  Lock,
  Unlock,
  FileSearch,
  Wifi,
  Server,
  Database,
  AlertOctagon,
  ShieldCheck,
  ShieldAlert,
  Layers,
  Target,
  Crosshair,
  Radio,
  Sparkles,
  TrendingUp,
  Gauge,
  Binary,
  X,
  ChevronDown,
  ChevronUp,
} from "lucide-react";

interface SystemMetric {
  value: number;
  status: "optimal" | "warning" | "critical";
}

interface ScanProgress {
  current: number;
  total: number;
  status: string;
}

interface Message {
  id: string;
  sender: "user" | "atlas";
  content: string;
  timestamp: Date;
}

type ThreatLevel = "none" | "low" | "medium" | "high" | "critical";
type ThreatType =
  | "malware"
  | "ransomware"
  | "trojan"
  | "spyware"
  | "adware"
  | "rootkit"
  | "worm"
  | "phishing";

interface DetectedThreat {
  id: string;
  name: string;
  type: ThreatType;
  level: ThreatLevel;
  confidence: number;
  path: string;
  timestamp: Date;
  status: "detected" | "quarantined" | "removed" | "monitoring";
  aiAnalysis: string;
}

interface BehaviorPattern {
  id: string;
  process: string;
  suspicionScore: number;
  actions: string[];
  aiPrediction: string;
}

interface NetworkAlert {
  id: string;
  type: "suspicious" | "blocked" | "allowed";
  source: string;
  destination: string;
  port: number;
  protocol: string;
  timestamp: Date;
}

export function AIScanner() {
  const [cpu, setCpu] = useState<SystemMetric>({
    value: 23,
    status: "optimal",
  });
  const [gpu, setGpu] = useState<SystemMetric>({
    value: 18,
    status: "optimal",
  });
  const [memory, setMemory] = useState<SystemMetric>({
    value: 45,
    status: "optimal",
  });
  const [disk, setDisk] = useState<SystemMetric>({
    value: 67,
    status: "optimal",
  });

  const [scanProgress, setScanProgress] = useState<ScanProgress>({
    current: 0,
    total: 100,
    status: "INITIALIZING NEURAL THREAT SCAN",
  });

  const [threats, setThreats] = useState(3);
  const [scannedItems, setScannedItems] = useState(45892);
  const [quarantinedItems, setQuarantinedItems] = useState(12);
  const [aiConfidence, setAiConfidence] = useState(97.8);
  const [neuralStrength, setNeuralStrength] = useState(94.2);

  const [messages, setMessages] = useState<Message[]>([
    {
      id: "1",
      sender: "atlas",
      content:
        "NEURAL SECURITY MATRIX ONLINE · QUANTUM THREAT ANALYSIS ACTIVE · ALL SYSTEMS NOMINAL",
      timestamp: new Date(),
    },
  ]);
  const [input, setInput] = useState("");
  const [showChat, setShowChat] = useState(false);

  const [detectedThreats, setDetectedThreats] = useState<DetectedThreat[]>([
    {
      id: "t1",
      name: "Trojan.Generic.KD.45632",
      type: "trojan",
      level: "high",
      confidence: 94.2,
      path: "C:\\Users\\Admin\\Downloads\\setup.exe",
      timestamp: new Date(Date.now() - 1000 * 60 * 5),
      status: "quarantined",
      aiAnalysis:
        "Behavioral pattern matches known trojan signatures. Auto-quarantined.",
    },
    {
      id: "t2",
      name: "Spyware.Agent.Keylogger",
      type: "spyware",
      level: "critical",
      confidence: 98.7,
      path: "C:\\Windows\\System32\\svchost32.exe",
      timestamp: new Date(Date.now() - 1000 * 60 * 15),
      status: "removed",
      aiAnalysis:
        "Critical keylogger detected. Neural network identified malicious code injection.",
    },
    {
      id: "t3",
      name: "Suspicious.Behavior.Unknown",
      type: "malware",
      level: "medium",
      confidence: 76.4,
      path: "C:\\Program Files\\RandomApp\\runtime.dll",
      timestamp: new Date(Date.now() - 1000 * 60 * 2),
      status: "monitoring",
      aiAnalysis:
        "Unusual network activity detected. AI monitoring process behavior.",
    },
  ]);

  const [behaviorPatterns, setBehaviorPatterns] = useState<BehaviorPattern[]>([
    {
      id: "b1",
      process: "chrome.exe",
      suspicionScore: 15.2,
      actions: ["Reading browser data", "Network access"],
      aiPrediction: "Normal browser behavior",
    },
    {
      id: "b2",
      process: "unknown_service.exe",
      suspicionScore: 87.6,
      actions: [
        "Registry modification",
        "File system access",
        "Network scanning",
      ],
      aiPrediction: "Potentially malicious - Elevated monitoring",
    },
    {
      id: "b3",
      process: "system_update.exe",
      suspicionScore: 45.3,
      actions: ["Downloading files", "Process injection attempt"],
      aiPrediction: "Suspicious - Requires analysis",
    },
  ]);

  const [networkAlerts, setNetworkAlerts] = useState<NetworkAlert[]>([
    {
      id: "n1",
      type: "blocked",
      source: "192.168.1.105",
      destination: "45.142.214.123",
      port: 4444,
      protocol: "TCP",
      timestamp: new Date(Date.now() - 1000 * 30),
    },
    {
      id: "n2",
      type: "suspicious",
      source: "192.168.1.105",
      destination: "185.220.101.45",
      port: 9050,
      protocol: "TOR",
      timestamp: new Date(Date.now() - 1000 * 120),
    },
  ]);

  const [selectedView, setSelectedView] = useState<
    "threats" | "behavior" | "network"
  >("threats");

  // Simulate system metrics updates
  useEffect(() => {
    const interval = setInterval(() => {
      setCpu((prev) => {
        const newValue = Math.max(
          10,
          Math.min(90, prev.value + (Math.random() - 0.5) * 10)
        );
        return {
          value: newValue,
          status:
            newValue > 80 ? "critical" : newValue > 60 ? "warning" : "optimal",
        };
      });

      setGpu((prev) => {
        const newValue = Math.max(
          10,
          Math.min(90, prev.value + (Math.random() - 0.5) * 8)
        );
        return {
          value: newValue,
          status:
            newValue > 80 ? "critical" : newValue > 60 ? "warning" : "optimal",
        };
      });

      setMemory((prev) => {
        const newValue = Math.max(
          20,
          Math.min(85, prev.value + (Math.random() - 0.5) * 5)
        );
        return {
          value: newValue,
          status:
            newValue > 80 ? "critical" : newValue > 70 ? "warning" : "optimal",
        };
      });

      setDisk((prev) => {
        const newValue = Math.max(
          50,
          Math.min(80, prev.value + (Math.random() - 0.5) * 3)
        );
        return {
          value: newValue,
          status:
            newValue > 90 ? "critical" : newValue > 80 ? "warning" : "optimal",
        };
      });

      setAiConfidence((prev) =>
        Math.max(85, Math.min(99.9, prev + (Math.random() - 0.5) * 0.5))
      );
      setNeuralStrength((prev) =>
        Math.max(80, Math.min(100, prev + (Math.random() - 0.5) * 1))
      );
    }, 1500);

    return () => clearInterval(interval);
  }, []);

  // Simulate scan progress
  useEffect(() => {
    const interval = setInterval(() => {
      setScanProgress((prev) => {
        if (prev.current >= prev.total) {
          setScannedItems((items) => items + Math.floor(Math.random() * 50));
          return {
            current: 0,
            total: 100,
            status: [
              "SCANNING FILE SYSTEM",
              "ANALYZING NETWORK TRAFFIC",
              "CHECKING REGISTRY KEYS",
              "MONITORING PROCESSES",
              "VALIDATING SIGNATURES",
              "BEHAVIORAL ANALYSIS",
              "NEURAL PATTERN MATCHING",
              "QUANTUM THREAT DETECTION",
            ][Math.floor(Math.random() * 8)],
          };
        }
        return {
          ...prev,
          current: prev.current + 1,
        };
      });
    }, 50);

    return () => clearInterval(interval);
  }, []);

  // Simulate new threats
  useEffect(() => {
    const interval = setInterval(() => {
      if (Math.random() > 0.95) {
        const newThreat: DetectedThreat = {
          id: `t${Date.now()}`,
          name: `Threat.Detected.${Math.floor(Math.random() * 99999)}`,
          type: ["malware", "spyware", "trojan", "adware"][
            Math.floor(Math.random() * 4)
          ] as ThreatType,
          level: ["low", "medium", "high"][
            Math.floor(Math.random() * 3)
          ] as ThreatLevel,
          confidence: 70 + Math.random() * 29,
          path: `C:\\Windows\\Temp\\file${Math.floor(
            Math.random() * 9999
          )}.tmp`,
          timestamp: new Date(),
          status: "detected",
          aiAnalysis: "Neural network flagged suspicious behavior pattern.",
        };
        setDetectedThreats((prev) => [newThreat, ...prev].slice(0, 10));
        setThreats((prev) => prev + 1);

        if (showChat) {
          const atlasMessage: Message = {
            id: Date.now().toString(),
            sender: "atlas",
            content: `⚠️ THREAT DETECTED: ${newThreat.name} · LEVEL: ${newThreat.level.toUpperCase()} · AUTO-RESPONSE INITIATED`,
            timestamp: new Date(),
          };
          setMessages((prev) => [...prev, atlasMessage]);
        }
      }
    }, 15000);

    return () => clearInterval(interval);
  }, [showChat]);

  const handleSend = () => {
    if (!input.trim()) return;

    const newMessage: Message = {
      id: Date.now().toString(),
      sender: "user",
      content: input,
      timestamp: new Date(),
    };

    setMessages([...messages, newMessage]);
    setInput("");

    setTimeout(() => {
      let response = "ACKNOWLEDGED · SECURITY PARAMETERS STABLE";

      if (input.toLowerCase().includes("threat")) {
        response = `THREAT STATUS: ${threats} ACTIVE · ${quarantinedItems} QUARANTINED · AI CONFIDENCE: ${aiConfidence.toFixed(
          1
        )}%`;
      } else if (input.toLowerCase().includes("scan")) {
        response = `SCANNING: ${scannedItems.toLocaleString()} ITEMS ANALYZED · NEURAL PATTERN RECOGNITION ACTIVE`;
      } else if (input.toLowerCase().includes("system")) {
        response = `SYSTEM: CPU ${cpu.value.toFixed(
          1
        )}% · MEMORY ${memory.value.toFixed(1)}% · ALL CORES OPERATIONAL`;
      } else if (input.toLowerCase().includes("quarantine")) {
        response = `QUARANTINE INITIATED · ${threats} THREATS ISOLATED · SYSTEM SECURED`;
        setQuarantinedItems((prev) => prev + threats);
        setThreats(0);
      }

      const atlasResponse: Message = {
        id: (Date.now() + 1).toString(),
        sender: "atlas",
        content: response,
        timestamp: new Date(),
      };
      setMessages((prev) => [...prev, atlasResponse]);
    }, 800);
  };

  const handleRemoveThreat = (id: string) => {
    setDetectedThreats((prev) =>
      prev.map((t) =>
        t.id === id ? { ...t, status: "removed" as const } : t
      )
    );
    setThreats((prev) => Math.max(0, prev - 1));
    setQuarantinedItems((prev) => prev + 1);
  };

  const getStatusColor = (status: string) => {
    switch (status) {
      case "critical":
        return "#ef4444";
      case "warning":
        return "#f97316";
      default:
        return "#22d3ee";
    }
  };

  const getThreatColor = (level: ThreatLevel) => {
    switch (level) {
      case "critical":
        return "#ef4444";
      case "high":
        return "#f97316";
      case "medium":
        return "#f59e0b";
      case "low":
        return "#84cc16";
      default:
        return "#22c55e";
    }
  };

  const getThreatIcon = (type: ThreatType) => {
    switch (type) {
      case "trojan":
        return <AlertOctagon className="w-4 h-4" />;
      case "ransomware":
        return <Lock className="w-4 h-4" />;
      case "spyware":
        return <Eye className="w-4 h-4" />;
      case "malware":
        return <AlertTriangle className="w-4 h-4" />;
      default:
        return <ShieldAlert className="w-4 h-4" />;
    }
  };

  const renderCompactGauge = (
    metric: SystemMetric,
    icon: React.ReactNode,
    label: string
  ) => {
    const angle = (metric.value / 100) * 180 - 90;
    const color = getStatusColor(metric.status);
    const radius = 50;
    const strokeWidth = 6;

    return (
      <div className="flex flex-col items-center">
        <div className="relative w-28 h-16">
          <svg className="w-full h-full" viewBox="0 0 140 80">
            <defs>
              <linearGradient
                id={`gauge-${label}`}
                x1="0%"
                y1="0%"
                x2="100%"
                y2="0%"
              >
                <stop offset="0%" stopColor="#22d3ee" stopOpacity="0.3" />
                <stop offset="50%" stopColor={color} stopOpacity="0.6" />
                <stop offset="100%" stopColor="#f97316" stopOpacity="0.3" />
              </linearGradient>
            </defs>

            <path
              d={`M 20 70 A ${radius} ${radius} 0 0 1 120 70`}
              fill="none"
              stroke="#1e293b"
              strokeWidth={strokeWidth}
              strokeLinecap="round"
            />

            <motion.path
              d={`M 20 70 A ${radius} ${radius} 0 0 1 120 70`}
              fill="none"
              stroke={`url(#gauge-${label})`}
              strokeWidth={strokeWidth}
              strokeLinecap="round"
              initial={{ pathLength: 0 }}
              animate={{ pathLength: metric.value / 100 }}
              transition={{ duration: 1, ease: "easeOut" }}
            />

            <motion.line
              x1="70"
              y1="70"
              x2="70"
              y2="30"
              stroke={color}
              strokeWidth="2"
              strokeLinecap="round"
              initial={{ rotate: -90 }}
              animate={{ rotate: angle }}
              transition={{ duration: 0.5, ease: "easeOut" }}
              style={{
                originX: "70px",
                originY: "70px",
              }}
            />

            <circle cx="70" cy="70" r="3" fill={color} />
          </svg>

          <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 mt-2">
            <div style={{ color }} className="w-4 h-4">
              {icon}
            </div>
          </div>
        </div>

        <div className="text-center mt-1">
          <div className="text-base font-mono" style={{ color }}>
            {metric.value.toFixed(0)}%
          </div>
          <div className="text-[9px] text-slate-500 font-mono uppercase">
            {label}
          </div>
        </div>
      </div>
    );
  };

  return (
    <div className="flex-1 flex flex-col overflow-hidden relative">
      {/* Animated background effects */}
      <div className="absolute inset-0 opacity-5 pointer-events-none">
        {[...Array(30)].map((_, i) => (
          <motion.div
            key={i}
            className="absolute w-px h-px bg-cyan-400 rounded-full"
            style={{
              left: `${Math.random() * 100}%`,
              top: `${Math.random() * 100}%`,
            }}
            animate={{
              scale: [0, 2, 0],
              opacity: [0, 1, 0],
            }}
            transition={{
              duration: 3 + Math.random() * 2,
              repeat: Infinity,
              delay: Math.random() * 5,
            }}
          />
        ))}
      </div>

      {/* Compact Header */}
      <div className="h-14 border-b border-cyan-500/10 px-6 flex items-center justify-between bg-[#0a0e12]/80 backdrop-blur-sm relative overflow-hidden z-10">
        <motion.div
          className="absolute inset-0 bg-gradient-to-r from-transparent via-cyan-500/10 to-transparent"
          animate={{ x: ["-100%", "200%"] }}
          transition={{ duration: 3, repeat: Infinity, ease: "linear" }}
        />

        <div className="flex items-center gap-3 relative z-10">
          <motion.div className="relative">
            <motion.div
              animate={{ rotate: 360 }}
              transition={{ duration: 4, repeat: Infinity, ease: "linear" }}
            >
              <Shield className="w-5 h-5 text-cyan-400" />
            </motion.div>
            <motion.div
              className="absolute inset-0 rounded-full border-2 border-cyan-400/30"
              animate={{
                scale: [1, 2],
                opacity: [0.5, 0],
              }}
              transition={{
                duration: 2,
                repeat: Infinity,
                ease: "easeOut",
              }}
            />
          </motion.div>
          <div>
            <h2 className="text-sm font-mono tracking-wider text-cyan-400">
              NEURAL SECURITY MATRIX
            </h2>
            <p className="text-[9px] text-slate-500 font-mono">
              QUANTUM THREAT ANALYSIS • BEHAVIORAL AI
            </p>
          </div>
        </div>

        <div className="flex items-center gap-4 relative z-10">
          <div className="text-center">
            <div className="text-[8px] text-slate-500 font-mono uppercase">
              Scanned
            </div>
            <div className="text-xs font-mono text-cyan-400">
              {scannedItems.toLocaleString()}
            </div>
          </div>
          <div className="text-center">
            <div className="text-[8px] text-slate-500 font-mono uppercase">
              Threats
            </div>
            <motion.div
              className="text-xs font-mono"
              style={{ color: threats > 0 ? "#f97316" : "#22c55e" }}
              animate={threats > 0 ? { scale: [1, 1.1, 1] } : { scale: 1 }}
              transition={{ duration: 1, repeat: threats > 0 ? Infinity : 0 }}
            >
              {threats}
            </motion.div>
          </div>
          <div className="text-center">
            <div className="text-[8px] text-slate-500 font-mono uppercase">
              Quarantined
            </div>
            <div className="text-xs font-mono text-amber-400">
              {quarantinedItems}
            </div>
          </div>
          <div className="text-center">
            <div className="text-[8px] text-slate-500 font-mono uppercase">
              AI Confidence
            </div>
            <div className="text-xs font-mono text-purple-400">
              {aiConfidence.toFixed(1)}%
            </div>
          </div>
        </div>
      </div>

      {/* Scan Progress Bar */}
      <div className="h-10 border-b border-cyan-500/10 px-6 flex items-center bg-[#0a0e12]/60 relative z-10">
        <div className="flex-1">
          <div className="flex items-center justify-between mb-1">
            <div className="flex items-center gap-2">
              <motion.div
                animate={{ rotate: 360 }}
                transition={{ duration: 2, repeat: Infinity, ease: "linear" }}
              >
                <Target className="w-2.5 h-2.5 text-orange-400" />
              </motion.div>
              <span className="text-[9px] font-mono text-orange-400/80 uppercase">
                {scanProgress.status}
              </span>
            </div>
            <span className="text-[9px] font-mono text-cyan-400">
              {scanProgress.current}%
            </span>
          </div>
          <div className="relative h-0.5 bg-slate-900 rounded-full overflow-hidden">
            <motion.div
              className="absolute inset-0 bg-gradient-to-r from-cyan-500 via-orange-500 to-cyan-500"
              style={{
                width: `${scanProgress.current}%`,
                backgroundSize: "200% 100%",
              }}
              animate={{
                backgroundPosition: ["0% 50%", "100% 50%", "0% 50%"],
              }}
              transition={{
                duration: 2,
                repeat: Infinity,
                ease: "linear",
              }}
            />
          </div>
        </div>
      </div>

      {/* Main Content Area */}
      <div className="flex-1 flex overflow-hidden">
        {/* Left Sidebar - System Metrics */}
        <div className="w-64 border-r border-cyan-500/10 overflow-y-auto scrollbar-hide bg-[#0b0f14]/20">
          <div className="p-4">
            <div className="grid grid-cols-2 gap-3">
              {renderCompactGauge(cpu, <Cpu className="w-4 h-4" />, "CPU")}
              {renderCompactGauge(gpu, <Zap className="w-4 h-4" />, "GPU")}
              {renderCompactGauge(
                memory,
                <Activity className="w-4 h-4" />,
                "MEMORY"
              )}
              {renderCompactGauge(
                disk,
                <HardDrive className="w-4 h-4" />,
                "DISK"
              )}
            </div>

            <motion.div
              initial={{ opacity: 0, y: 20 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: 0.2 }}
              className="mt-4 rounded-lg border border-purple-500/30 bg-purple-500/5 backdrop-blur-sm p-2.5 relative overflow-hidden"
            >
              <motion.div
                className="absolute inset-0 bg-gradient-to-r from-purple-500/0 via-purple-500/10 to-purple-500/0"
                animate={{ x: ["-100%", "100%"] }}
                transition={{ duration: 3, repeat: Infinity }}
              />

              <div className="relative z-10">
                <div className="flex items-center gap-2 mb-1.5">
                  <Brain className="w-3.5 h-3.5 text-purple-400" />
                  <div className="flex-1">
                    <div className="text-[9px] font-mono text-purple-400 uppercase">
                      NEURAL NETWORK
                    </div>
                  </div>
                  <div className="text-sm font-mono text-purple-400">
                    {neuralStrength.toFixed(1)}%
                  </div>
                </div>

                <div className="h-1 bg-slate-900 rounded-full overflow-hidden">
                  <motion.div
                    className="h-full bg-gradient-to-r from-purple-500 to-cyan-500"
                    initial={{ width: 0 }}
                    animate={{ width: `${neuralStrength}%` }}
                    transition={{ duration: 1 }}
                  />
                </div>
              </div>
            </motion.div>

            <motion.div
              initial={{ opacity: 0, y: 20 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: 0.3 }}
              className="mt-2.5 rounded-lg border border-green-500/30 bg-green-500/5 backdrop-blur-sm p-2.5"
            >
              <div className="flex items-center gap-2">
                <motion.div
                  animate={{
                    scale: [1, 1.2, 1],
                  }}
                  transition={{
                    duration: 2,
                    repeat: Infinity,
                    ease: "easeInOut",
                  }}
                  className="w-1.5 h-1.5 rounded-full bg-green-400 flex-shrink-0"
                />
                <div className="flex-1 min-w-0">
                  <div className="text-[9px] font-mono text-green-400 uppercase">
                    ALL SYSTEMS SECURE
                  </div>
                  <div className="text-[8px] text-slate-500 font-mono truncate">
                    Real-time protection active
                  </div>
                </div>
              </div>
            </motion.div>
          </div>
        </div>

        {/* Main Content - Threat Analysis */}
        <div className="flex-1 flex flex-col overflow-hidden">
          {/* Tab Navigation */}
          <div className="flex border-b border-cyan-500/10 bg-[#0a0e12]/60">
            <button
              onClick={() => setSelectedView("threats")}
              className={`flex-1 px-4 py-2 font-mono text-xs uppercase tracking-wider transition-all relative ${
                selectedView === "threats"
                  ? "text-cyan-400"
                  : "text-slate-500 hover:text-slate-400"
              }`}
            >
              <div className="flex items-center justify-center gap-2">
                <ShieldAlert className="w-3.5 h-3.5" />
                <span>Threats ({detectedThreats.length})</span>
              </div>
              {selectedView === "threats" && (
                <motion.div
                  layoutId="activeTab"
                  className="absolute bottom-0 left-0 right-0 h-0.5 bg-cyan-400"
                />
              )}
            </button>

            <button
              onClick={() => setSelectedView("behavior")}
              className={`flex-1 px-4 py-2 font-mono text-xs uppercase tracking-wider transition-all relative ${
                selectedView === "behavior"
                  ? "text-cyan-400"
                  : "text-slate-500 hover:text-slate-400"
              }`}
            >
              <div className="flex items-center justify-center gap-2">
                <Eye className="w-3.5 h-3.5" />
                <span>Behavior ({behaviorPatterns.length})</span>
              </div>
              {selectedView === "behavior" && (
                <motion.div
                  layoutId="activeTab"
                  className="absolute bottom-0 left-0 right-0 h-0.5 bg-cyan-400"
                />
              )}
            </button>

            <button
              onClick={() => setSelectedView("network")}
              className={`flex-1 px-4 py-2 font-mono text-xs uppercase tracking-wider transition-all relative ${
                selectedView === "network"
                  ? "text-cyan-400"
                  : "text-slate-500 hover:text-slate-400"
              }`}
            >
              <div className="flex items-center justify-center gap-2">
                <Network className="w-3.5 h-3.5" />
                <span>Network ({networkAlerts.length})</span>
              </div>
              {selectedView === "network" && (
                <motion.div
                  layoutId="activeTab"
                  className="absolute bottom-0 left-0 right-0 h-0.5 bg-cyan-400"
                />
              )}
            </button>
          </div>

          {/* Tab Content */}
          <div className="flex-1 overflow-y-auto p-4 scrollbar-hide">
            <AnimatePresence mode="wait">
              {selectedView === "threats" && (
                <motion.div
                  key="threats"
                  initial={{ opacity: 0, x: 20 }}
                  animate={{ opacity: 1, x: 0 }}
                  exit={{ opacity: 0, x: -20 }}
                  className="space-y-3"
                >
                  {detectedThreats.map((threat, index) => (
                    <motion.div
                      key={threat.id}
                      initial={{ opacity: 0, y: 20 }}
                      animate={{ opacity: 1, y: 0 }}
                      transition={{ delay: index * 0.1 }}
                      className="relative p-3 rounded-lg border bg-slate-900/30 border-slate-800 hover:border-cyan-500/30 transition-all overflow-hidden group"
                    >
                      <div
                        className="absolute left-0 top-0 bottom-0 w-1"
                        style={{
                          backgroundColor: getThreatColor(threat.level),
                          boxShadow: `0 0 10px ${getThreatColor(
                            threat.level
                          )}`,
                        }}
                      />

                      {threat.status === "monitoring" && (
                        <motion.div
                          className="absolute inset-0 bg-gradient-to-r from-transparent via-amber-500/10 to-transparent"
                          animate={{ x: ["-100%", "200%"] }}
                          transition={{
                            duration: 2,
                            repeat: Infinity,
                          }}
                        />
                      )}

                      <div className="relative z-10">
                        <div className="flex items-start justify-between mb-2">
                          <div className="flex items-center gap-2 flex-1">
                            <div
                              className="p-1.5 rounded-lg"
                              style={{
                                backgroundColor: `${getThreatColor(
                                  threat.level
                                )}20`,
                                color: getThreatColor(threat.level),
                              }}
                            >
                              {getThreatIcon(threat.type)}
                            </div>
                            <div className="flex-1 min-w-0">
                              <h4 className="text-xs font-mono font-bold text-slate-200 truncate">
                                {threat.name}
                              </h4>
                              <p className="text-[9px] text-slate-500 font-mono mt-0.5 truncate">
                                {threat.path}
                              </p>
                            </div>
                          </div>

                          {threat.status === "detected" && (
                            <motion.button
                              whileHover={{ scale: 1.1 }}
                              whileTap={{ scale: 0.9 }}
                              onClick={() => handleRemoveThreat(threat.id)}
                              className="p-1 rounded-lg bg-red-500/20 border border-red-500/50 text-red-400 hover:bg-red-500/30 transition-all"
                              title="Quarantine"
                            >
                              <X className="w-3 h-3" />
                            </motion.button>
                          )}
                        </div>

                        <div className="flex items-center gap-1.5 mb-2 flex-wrap">
                          <motion.div
                            className="px-1.5 py-0.5 rounded-full text-[8px] font-mono uppercase"
                            style={{
                              backgroundColor: `${getThreatColor(
                                threat.level
                              )}20`,
                              color: getThreatColor(threat.level),
                              borderWidth: 1,
                              borderColor: `${getThreatColor(threat.level)}50`,
                            }}
                            animate={
                              threat.level === "critical" ||
                              threat.level === "high"
                                ? {
                                    boxShadow: [
                                      `0 0 5px ${getThreatColor(
                                        threat.level
                                      )}50`,
                                      `0 0 15px ${getThreatColor(
                                        threat.level
                                      )}80`,
                                      `0 0 5px ${getThreatColor(
                                        threat.level
                                      )}50`,
                                    ],
                                  }
                                : {}
                            }
                            transition={{ duration: 2, repeat: Infinity }}
                          >
                            {threat.level}
                          </motion.div>

                          <div className="px-1.5 py-0.5 rounded-full text-[8px] font-mono uppercase bg-purple-500/20 text-purple-400 border border-purple-500/50">
                            {threat.type}
                          </div>

                          <div className="px-1.5 py-0.5 rounded-full text-[8px] font-mono uppercase bg-cyan-500/20 text-cyan-400 border border-cyan-500/50">
                            {threat.confidence.toFixed(1)}%
                          </div>

                          <div
                            className={`px-1.5 py-0.5 rounded-full text-[8px] font-mono uppercase ${
                              threat.status === "removed"
                                ? "bg-green-500/20 text-green-400 border border-green-500/50"
                                : threat.status === "quarantined"
                                ? "bg-amber-500/20 text-amber-400 border border-amber-500/50"
                                : threat.status === "monitoring"
                                ? "bg-blue-500/20 text-blue-400 border border-blue-500/50"
                                : "bg-red-500/20 text-red-400 border border-red-500/50"
                            }`}
                          >
                            {threat.status}
                          </div>
                        </div>

                        <div className="p-2 rounded-lg bg-cyan-500/10 border border-cyan-500/20">
                          <div className="flex items-start gap-1.5">
                            <Sparkles className="w-2.5 h-2.5 text-cyan-400 mt-0.5 flex-shrink-0" />
                            <p className="text-[9px] text-cyan-400 font-mono">
                              {threat.aiAnalysis}
                            </p>
                          </div>
                        </div>

                        <div className="mt-1.5 text-[8px] text-slate-600 font-mono">
                          {threat.timestamp.toLocaleTimeString()}
                        </div>
                      </div>
                    </motion.div>
                  ))}

                  {detectedThreats.length === 0 && (
                    <div className="flex flex-col items-center justify-center h-full p-12 text-center">
                      <motion.div
                        animate={{
                          scale: [1, 1.1, 1],
                          opacity: [0.5, 1, 0.5],
                        }}
                        transition={{ duration: 2, repeat: Infinity }}
                      >
                        <ShieldCheck className="w-16 h-16 text-green-500 mb-4" />
                      </motion.div>
                      <p className="text-slate-500 font-mono text-sm">
                        NO THREATS DETECTED
                      </p>
                      <p className="text-slate-600 font-mono text-xs mt-2">
                        Neural defense active • System protected
                      </p>
                    </div>
                  )}
                </motion.div>
              )}

              {selectedView === "behavior" && (
                <motion.div
                  key="behavior"
                  initial={{ opacity: 0, x: 20 }}
                  animate={{ opacity: 1, x: 0 }}
                  exit={{ opacity: 0, x: -20 }}
                  className="space-y-3"
                >
                  {behaviorPatterns.map((pattern, index) => (
                    <motion.div
                      key={pattern.id}
                      initial={{ opacity: 0, y: 20 }}
                      animate={{ opacity: 1, y: 0 }}
                      transition={{ delay: index * 0.1 }}
                      className="relative p-3 rounded-lg border bg-slate-900/30 border-slate-800 hover:border-cyan-500/30 transition-all"
                    >
                      <div className="flex items-start gap-2 mb-2">
                        <div
                          className="p-1.5 rounded-lg"
                          style={{
                            backgroundColor:
                              pattern.suspicionScore > 70
                                ? "#ef444420"
                                : pattern.suspicionScore > 40
                                ? "#f59e0b20"
                                : "#22d3ee20",
                            color:
                              pattern.suspicionScore > 70
                                ? "#ef4444"
                                : pattern.suspicionScore > 40
                                ? "#f59e0b"
                                : "#22d3ee",
                          }}
                        >
                          <Binary className="w-4 h-4" />
                        </div>
                        <div className="flex-1 min-w-0">
                          <h4 className="text-xs font-mono font-bold text-slate-200 truncate">
                            {pattern.process}
                          </h4>
                          <p className="text-[9px] text-slate-500 font-mono mt-0.5">
                            Behavioral analysis
                          </p>
                        </div>
                      </div>

                      <div className="mb-2">
                        <div className="flex items-center justify-between mb-1">
                          <span className="text-[8px] text-slate-500 font-mono uppercase">
                            Suspicion Score
                          </span>
                          <span
                            className="text-[9px] font-mono font-bold"
                            style={{
                              color:
                                pattern.suspicionScore > 70
                                  ? "#ef4444"
                                  : pattern.suspicionScore > 40
                                  ? "#f59e0b"
                                  : "#22d3ee",
                            }}
                          >
                            {pattern.suspicionScore.toFixed(1)}%
                          </span>
                        </div>
                        <div className="h-1 bg-slate-900 rounded-full overflow-hidden">
                          <motion.div
                            className="h-full rounded-full"
                            style={{
                              background:
                                pattern.suspicionScore > 70
                                  ? "linear-gradient(to right, #f97316, #ef4444)"
                                  : pattern.suspicionScore > 40
                                  ? "linear-gradient(to right, #84cc16, #f59e0b)"
                                  : "linear-gradient(to right, #22d3ee, #22c55e)",
                            }}
                            initial={{ width: 0 }}
                            animate={{ width: `${pattern.suspicionScore}%` }}
                            transition={{ duration: 1, delay: index * 0.1 }}
                          />
                        </div>
                      </div>

                      <div className="mb-2">
                        <div className="text-[8px] text-slate-500 font-mono uppercase mb-1">
                          Actions
                        </div>
                        <div className="flex flex-wrap gap-1">
                          {pattern.actions.map((action, i) => (
                            <div
                              key={i}
                              className="px-1.5 py-0.5 rounded-full text-[8px] font-mono bg-slate-800/50 text-slate-400 border border-slate-700/50"
                            >
                              {action}
                            </div>
                          ))}
                        </div>
                      </div>

                      <div className="p-2 rounded-lg bg-purple-500/10 border border-purple-500/20">
                        <div className="flex items-start gap-1.5">
                          <Brain className="w-2.5 h-2.5 text-purple-400 mt-0.5 flex-shrink-0" />
                          <p className="text-[9px] text-purple-400 font-mono">
                            {pattern.aiPrediction}
                          </p>
                        </div>
                      </div>
                    </motion.div>
                  ))}
                </motion.div>
              )}

              {selectedView === "network" && (
                <motion.div
                  key="network"
                  initial={{ opacity: 0, x: 20 }}
                  animate={{ opacity: 1, x: 0 }}
                  exit={{ opacity: 0, x: -20 }}
                  className="space-y-3"
                >
                  {networkAlerts.map((alert, index) => (
                    <motion.div
                      key={alert.id}
                      initial={{ opacity: 0, y: 20 }}
                      animate={{ opacity: 1, y: 0 }}
                      transition={{ delay: index * 0.1 }}
                      className="relative p-3 rounded-lg border bg-slate-900/30 border-slate-800 hover:border-cyan-500/30 transition-all"
                    >
                      <div
                        className="absolute left-0 top-0 bottom-0 w-1"
                        style={{
                          backgroundColor:
                            alert.type === "blocked"
                              ? "#ef4444"
                              : alert.type === "suspicious"
                              ? "#f59e0b"
                              : "#22c55e",
                        }}
                      />

                      <div className="flex items-start gap-2">
                        <div
                          className="p-1.5 rounded-lg"
                          style={{
                            backgroundColor:
                              alert.type === "blocked"
                                ? "#ef444420"
                                : alert.type === "suspicious"
                                ? "#f59e0b20"
                                : "#22c55e20",
                            color:
                              alert.type === "blocked"
                                ? "#ef4444"
                                : alert.type === "suspicious"
                                ? "#f59e0b"
                                : "#22c55e",
                          }}
                        >
                          <Wifi className="w-4 h-4" />
                        </div>

                        <div className="flex-1 min-w-0">
                          <div className="flex items-center gap-1.5 mb-1.5">
                            <div
                              className="px-1.5 py-0.5 rounded-full text-[8px] font-mono uppercase"
                              style={{
                                backgroundColor:
                                  alert.type === "blocked"
                                    ? "#ef444420"
                                    : alert.type === "suspicious"
                                    ? "#f59e0b20"
                                    : "#22c55e20",
                                color:
                                  alert.type === "blocked"
                                    ? "#ef4444"
                                    : alert.type === "suspicious"
                                    ? "#f59e0b"
                                    : "#22c55e",
                              }}
                            >
                              {alert.type}
                            </div>
                            <div className="px-1.5 py-0.5 rounded-full text-[8px] font-mono uppercase bg-slate-800/50 text-slate-400">
                              {alert.protocol}
                            </div>
                          </div>

                          <div className="text-[9px] font-mono text-slate-300 space-y-0.5">
                            <div>
                              <span className="text-slate-500">Source:</span>{" "}
                              {alert.source}
                            </div>
                            <div>
                              <span className="text-slate-500">
                                Destination:
                              </span>{" "}
                              {alert.destination}:{alert.port}
                            </div>
                            <div className="text-slate-600">
                              {alert.timestamp.toLocaleTimeString()}
                            </div>
                          </div>
                        </div>
                      </div>
                    </motion.div>
                  ))}
                </motion.div>
              )}
            </AnimatePresence>
          </div>
        </div>
      </div>

      {/* Collapsible Chat at Bottom */}
      <AnimatePresence>
        {showChat && (
          <motion.div
            initial={{ height: 0, opacity: 0 }}
            animate={{ height: 200, opacity: 1 }}
            exit={{ height: 0, opacity: 0 }}
            className="border-t border-cyan-500/10 bg-[#0b0f14]/60 flex flex-col overflow-hidden"
          >
            <div className="flex-1 overflow-y-auto p-3 space-y-2 scrollbar-hide">
              {messages.slice(-3).map((msg) => (
                <motion.div
                  key={msg.id}
                  initial={{ opacity: 0, y: 10 }}
                  animate={{ opacity: 1, y: 0 }}
                  className="text-xs"
                >
                  <div className="flex items-center gap-2 mb-0.5">
                    <span
                      className={`text-[8px] font-mono uppercase ${
                        msg.sender === "atlas"
                          ? "text-cyan-400"
                          : "text-orange-400"
                      }`}
                    >
                      {msg.sender === "atlas" ? "Atlas" : "User"}
                    </span>
                    <span className="text-[8px] text-slate-600 font-mono">
                      {msg.timestamp.toLocaleTimeString("en-US", {
                        hour: "2-digit",
                        minute: "2-digit",
                      })}
                    </span>
                  </div>
                  <p className="text-[10px] text-slate-300 font-mono">
                    {msg.content}
                  </p>
                </motion.div>
              ))}
            </div>

            <div className="p-2 border-t border-cyan-500/10">
              <div className="flex items-center gap-2 bg-[#0f1419] border border-cyan-500/20 rounded-lg p-1.5">
                <input
                  type="text"
                  value={input}
                  onChange={(e) => setInput(e.target.value)}
                  onKeyPress={(e) => e.key === "Enter" && handleSend()}
                  placeholder="Ask AI..."
                  className="flex-1 bg-transparent text-slate-200 text-[10px] outline-none placeholder:text-slate-600 font-mono"
                />
                <motion.button
                  onClick={handleSend}
                  whileHover={{ scale: 1.05 }}
                  whileTap={{ scale: 0.95 }}
                  className="p-1.5 rounded-lg bg-cyan-500/10 text-cyan-400 hover:bg-cyan-500/20 transition-all"
                >
                  <Send className="w-3 h-3" />
                </motion.button>
              </div>
            </div>
          </motion.div>
        )}
      </AnimatePresence>

      {/* Chat Toggle Button */}
      <motion.button
        onClick={() => setShowChat(!showChat)}
        whileHover={{ scale: 1.05 }}
        whileTap={{ scale: 0.95 }}
        className="absolute bottom-4 right-4 p-2 rounded-lg bg-cyan-500/20 border border-cyan-500/50 text-cyan-400 hover:bg-cyan-500/30 transition-all z-20"
      >
        {showChat ? (
          <ChevronDown className="w-4 h-4" />
        ) : (
          <Brain className="w-4 h-4" />
        )}
      </motion.button>
    </div>
  );
}
