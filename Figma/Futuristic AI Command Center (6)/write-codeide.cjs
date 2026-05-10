const fs = require('fs');
const path = require('path');

const content = `import { useState, useEffect, useRef } from "react";
import { motion, AnimatePresence } from "motion/react";
import {
  Layers,
  Sparkles,
  CheckCircle2,
  Activity,
  Cpu,
  Database,
  Wifi,
  Layout,
  RefreshCw,
  Send,
  Folder,
  FolderOpen,
  FileCode,
  FilePlus,
  FolderPlus,
  Trash2,
  X,
  ChevronDown,
  ChevronRight,
  Search,
  GitBranch,
  Play,
  Bug,
  Package,
  Settings,
  Terminal,
  FileText,
  Save,
  Zap,
  Code2,
  Grid3x3,
  Box,
  Boxes,
  Binary,
  Orbit,
  Network,
  Blocks,
  ArrowLeft,
} from "lucide-react";

interface BuildComponent {
  id: string;
  name: string;
  type: "view" | "logic" | "data" | "api";
  status: "queued" | "building" | "complete";
  progress: number;
}

interface Message {
  id: string;
  sender: "user" | "atlas";
  content: string;
  timestamp: Date;
}

interface FileNode {
  id: string;
  name: string;
  type: "file" | "folder";
  extension?: string;
  content?: string;
  children?: FileNode[];
  isOpen?: boolean;
}

interface EditorTab {
  id: string;
  name: string;
  content: string;
  isDirty: boolean;
}

interface BuildingBlock {
  id: number;
  x: number;
  y: number;
  size: number;
  delay: number;
  color: string;
}

type IDEMode = "autonomous" | "ide";
type AgentType = "builder" | "designer";
type ToolsPanel = "explorer" | "search" | "source-control" | "debug" | "extensions" | null;
type ActivityState = "idle" | "thinking" | "building" | "complete";

interface CodeIDEProps {
  initialMode?: IDEMode;
}

export function CodeIDE({ initialMode = "autonomous" }: CodeIDEProps) {
  const [mode, setMode] = useState<IDEMode>(initialMode);
  const [activeAgent, setActiveAgent] = useState<AgentType>("builder");
  const [activeToolsPanel, setActiveToolsPanel] = useState<ToolsPanel>(null);

  // Activity state machine: idle → thinking → building → complete → idle
  const [activityState, setActivityState] = useState<ActivityState>("idle");
  const [buildProgress, setBuildProgress] = useState(0);
  const [currentTool, setCurrentTool] = useState<string | null>(null);
  const [buildingBlocks, setBuildingBlocks] = useState<BuildingBlock[]>([]);
  const [assemblyNodes, setAssemblyNodes] = useState<Array<{ id: number; x: number; y: number; active: boolean }>>([]);
  const [showBuildViz, setShowBuildViz] = useState(true);
  const [showProgress, setShowProgress] = useState(true);

  // No dummy components — populated from real WPF data
  const [components, setComponents] = useState<BuildComponent[]>([]);
  const completeTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const buildLogCountRef = useRef(0);

  const [messages, setMessages] = useState<Message[]>([
    {
      id: "1",
      sender: "atlas",
      content: "READY \\u00B7 AWAITING INSTRUCTIONS",
      timestamp: new Date(),
    },
  ]);
  const [input, setInput] = useState("");

  const [showProjectFiles, setShowProjectFiles] = useState(false);
  const [fileTree, setFileTree] = useState<FileNode[]>([
    {
      id: "root",
      name: "AtlasAI",
      type: "folder",
      isOpen: false,
      children: [],
    },
  ]);

  // IDE mode state
  const [openTabs, setOpenTabs] = useState<EditorTab[]>([]);
  const [activeTabId, setActiveTabId] = useState<string | null>(null);
  const [searchQuery, setSearchQuery] = useState("");
  const [showTerminal, setShowTerminal] = useState(true);
  const [showChat, setShowChat] = useState(true);

  const textareaRef = useRef<HTMLTextAreaElement>(null);

  // Helper: add message with cap
  const addMessage = (content: string, sender: "user" | "atlas" = "atlas") => {
    const newMsg: Message = {
      id: Date.now().toString() + Math.random(),
      sender,
      content,
      timestamp: new Date(),
    };
    setMessages(prev => {
      const next = [...prev, newMsg];
      return next.length > 60 ? next.slice(-60) : next;
    });
  };

  // WPF \\u2194 React bridge via WebView2 postMessage
  useEffect(() => {
    const webview = (window as any).chrome?.webview;
    if (!webview) return;

    const handler = (event: any) => {
      try {
        const msg = typeof event.data === "string" ? JSON.parse(event.data) : event.data;
        if (!msg || !msg.type) return;

        // === FILE SYSTEM MESSAGES ===
        if (msg.type === "files_tree" && msg.root) {
          const mapNode = (n: any): FileNode => ({
            id: n.id || n.name,
            name: n.name,
            type: n.type === "folder" ? "folder" : "file",
            extension: n.extension,
            content: n.content || undefined,
            isOpen: n.isOpen ?? false,
            children: n.children?.map(mapNode),
          });
          setFileTree([mapNode(msg.root)]);
        }
        if (msg.type === "file_content" && msg.path && msg.content) {
          const tab: EditorTab = {
            id: msg.path,
            name: msg.path.split(/[\\\\\\/]/).pop() || msg.path,
            content: msg.content,
            isDirty: false,
          };
          setOpenTabs(prev => {
            const exists = prev.find(t => t.id === tab.id);
            if (exists) return prev;
            return [...prev, tab];
          });
          setActiveTabId(msg.path);
        }

        // === ACTIVITY STATE MESSAGES ===
        if (msg.type === "chat_status" && msg.text) {
          setActivityState(prev => prev === "building" ? prev : "thinking");
          addMessage(msg.text);
        }
        if (msg.type === "tool_start" && msg.tool) {
          setActivityState("building");
          setCurrentTool(msg.tool);
          // Add component for this tool
          setComponents(prev => {
            const exists = prev.find(c => c.name === msg.tool);
            if (exists) return prev.map(c => c.name === msg.tool ? { ...c, status: "building" as const } : c);
            const types: Array<"view" | "logic" | "data" | "api"> = ["logic", "data", "view", "api"];
            return [...prev.slice(-5), {
              id: Date.now().toString(),
              name: msg.tool,
              type: types[prev.length % 4],
              status: "building" as const,
              progress: 50,
            }];
          });
        }
        if (msg.type === "build_started") {
          setActivityState("building");
          setBuildProgress(0);
          buildLogCountRef.current = 0;
          setComponents([]);
        }
        if (msg.type === "build_log" && msg.line) {
          buildLogCountRef.current += 1;
          // Estimate progress from log count (typical .NET build ~30-80 lines)
          setBuildProgress(prev => Math.min(prev + 1.5, 95));
          addMessage(msg.line);
        }
        if (msg.type === "build_complete") {
          setBuildProgress(100);
          setActivityState("complete");
          setCurrentTool(null);
          // Mark all components complete
          setComponents(prev => prev.map(c => ({ ...c, status: "complete" as const, progress: 100 })));
          addMessage(msg.success ? "BUILD COMPLETE \\u00B7 SUCCESS" : "BUILD FAILED");
          // Return to idle after delay
          if (completeTimerRef.current) clearTimeout(completeTimerRef.current);
          completeTimerRef.current = setTimeout(() => {
            setActivityState("idle");
            setBuildProgress(0);
            setComponents([]);
          }, 4000);
        }
        if (msg.type === "chat_response" && msg.text) {
          addMessage(msg.text);
          // If not currently building, go to complete briefly then idle
          setActivityState(prev => {
            if (prev === "building") return prev;
            setTimeout(() => setActivityState("idle"), 3000);
            return "complete";
          });
        }
        if (msg.type === "chat_error" && msg.text) {
          addMessage("ERROR: " + msg.text);
        }
        if (msg.type === "set_mode" && (msg.mode === "autonomous" || msg.mode === "ide")) {
          setMode(msg.mode);
        }
      } catch {}
    };

    webview.addEventListener("message", handler);
    webview.postMessage(JSON.stringify({ type: "ready" }));
    // Request real file tree on mount
    webview.postMessage(JSON.stringify({ type: "list_files" }));

    return () => webview.removeEventListener("message", handler);
  }, []);

  // Post messages to WPF
  const postToWpf = (msg: object) => {
    const webview = (window as any).chrome?.webview;
    if (webview) webview.postMessage(JSON.stringify(msg));
  };

  // Initialize ambient elements
  useEffect(() => {
    const blocks: BuildingBlock[] = [];
    const colors = ["#22d3ee", "#f97316", "#a855f7", "#10b981", "#f59e0b"];
    for (let i = 0; i < 30; i++) {
      blocks.push({
        id: i,
        x: Math.random() * 600,
        y: Math.random() * 400,
        size: 8 + Math.random() * 20,
        delay: Math.random() * 5,
        color: colors[Math.floor(Math.random() * colors.length)],
      });
    }
    setBuildingBlocks(blocks);

    const nodes = [];
    for (let i = 0; i < 12; i++) {
      const angle = (i / 12) * Math.PI * 2;
      nodes.push({
        id: i,
        x: 300 + Math.cos(angle) * 150,
        y: 200 + Math.sin(angle) * 100,
        active: false,
      });
    }
    setAssemblyNodes(nodes);
  }, []);

  // Assembly node animation — speed varies by activity state
  useEffect(() => {
    if (mode !== "autonomous") return;

    const speed = activityState === "building" ? 1500
      : activityState === "thinking" ? 2500
      : activityState === "complete" ? 3000
      : 6000;
    const threshold = activityState === "building" ? 0.45
      : activityState === "thinking" ? 0.65
      : 0.88;

    const interval = setInterval(() => {
      setAssemblyNodes(prev =>
        prev.map(node => ({
          ...node,
          active: Math.random() > threshold
        }))
      );
    }, speed);

    return () => clearInterval(interval);
  }, [mode, activityState]);

  // NO fake build simulation — activity is driven entirely by WPF messages

  // Focus textarea when active tab changes
  useEffect(() => {
    if (mode === "ide" && activeTabId && textareaRef.current) {
      setTimeout(() => textareaRef.current?.focus(), 100);
    }
  }, [activeTabId, mode]);

  // Auto-open README.md when switching to IDE mode
  useEffect(() => {
    if (mode === "ide" && openTabs.length === 0) {
      const findReadme = (nodes: FileNode[]): FileNode | null => {
        for (const node of nodes) {
          if (node.type === "file" && node.name === "README.md") return node;
          if (node.type === "folder" && node.children) {
            const found = findReadme(node.children);
            if (found) return found;
          }
        }
        return null;
      };
      const readmeFile = findReadme(fileTree);
      if (readmeFile) openFile(readmeFile);
    }
  }, [mode]);

  const getComponentIcon = (type: string) => {
    switch (type) {
      case "view": return <Layout className="w-4 h-4" />;
      case "logic": return <Cpu className="w-4 h-4" />;
      case "data": return <Database className="w-4 h-4" />;
      case "api": return <Wifi className="w-4 h-4" />;
      default: return <Layout className="w-4 h-4" />;
    }
  };

  const handleSend = () => {
    if (!input.trim()) return;

    const newMessage: Message = {
      id: Date.now().toString(),
      sender: "user",
      content: input,
      timestamp: new Date(),
    };
    setMessages(prev => [...prev, newMessage]);
    setInput("");

    const webview = (window as any).chrome?.webview;
    if (webview) {
      postToWpf({ type: "chat_message", text: input, agent: activeAgent });
      setActivityState("thinking");
    } else {
      // Local fallback — no WPF bridge
      setActivityState("thinking");
      setTimeout(() => {
        setActivityState("idle");
        addMessage(
          activeAgent === "designer"
            ? "ACKNOWLEDGED \\u00B7 GENERATING UI DESIGN CONCEPTS"
            : "ACKNOWLEDGED \\u00B7 ADJUSTING BUILD PARAMETERS"
        );
      }, 2000);
    }
  };

  const getFileIcon = (extension: string) => {
    switch (extension) {
      case "xaml": return <FileCode className="w-3 h-3 text-cyan-400" />;
      case "cs": return <FileCode className="w-3 h-3 text-green-400" />;
      case "md": return <FileText className="w-3 h-3 text-orange-400" />;
      case "tsx": case "jsx": return <FileCode className="w-3 h-3 text-blue-400" />;
      case "ts": case "js": return <FileCode className="w-3 h-3 text-yellow-400" />;
      default: return <FileCode className="w-3 h-3 text-slate-400" />;
    }
  };

  const toggleFolder = (nodeId: string) => {
    const toggleNode = (nodes: FileNode[]): FileNode[] => {
      return nodes.map((node) => {
        if (node.id === nodeId && node.type === "folder") return { ...node, isOpen: !node.isOpen };
        if (node.children) return { ...node, children: toggleNode(node.children) };
        return node;
      });
    };
    setFileTree(toggleNode(fileTree));
  };

  const openFile = (node: FileNode) => {
    if (node.type === "file" && node.content) {
      const existing = openTabs.find((tab) => tab.id === node.id);
      if (!existing) {
        setOpenTabs([...openTabs, { id: node.id, name: node.name, content: node.content, isDirty: false }]);
      }
      setActiveTabId(node.id);
    }
  };

  const closeTab = (tabId: string, e?: React.MouseEvent) => {
    e?.stopPropagation();
    const newTabs = openTabs.filter((tab) => tab.id !== tabId);
    setOpenTabs(newTabs);
    if (activeTabId === tabId && newTabs.length > 0) {
      setActiveTabId(newTabs[newTabs.length - 1].id);
    } else if (newTabs.length === 0) {
      setActiveTabId(null);
    }
  };

  const updateTabContent = (content: string) => {
    if (!activeTabId) return;
    setOpenTabs(openTabs.map((tab) => tab.id === activeTabId ? { ...tab, content, isDirty: true } : tab));
  };

  const saveCurrentFile = () => {
    if (!activeTabId) return;
    const tab = openTabs.find((t) => t.id === activeTabId);
    if (!tab) return;
    const updateFileContent = (nodes: FileNode[]): FileNode[] => {
      return nodes.map((node) => {
        if (node.id === activeTabId) return { ...node, content: tab.content };
        if (node.children) return { ...node, children: updateFileContent(node.children) };
        return node;
      });
    };
    setFileTree(updateFileContent(fileTree));
    setOpenTabs(openTabs.map((t) => (t.id === activeTabId ? { ...t, isDirty: false } : t)));
  };

  const addNewFile = (parentId: string) => {
    const fileName = prompt("Enter file name:");
    if (!fileName) return;
    const extension = fileName.split(".").pop() || "txt";
    const newFile: FileNode = { id: "file-" + Date.now(), name: fileName, type: "file", extension, content: "// " + fileName + "\\n" };
    const addToNode = (nodes: FileNode[]): FileNode[] => {
      return nodes.map((node) => {
        if (node.id === parentId && node.type === "folder") return { ...node, children: [...(node.children || []), newFile], isOpen: true };
        if (node.children) return { ...node, children: addToNode(node.children) };
        return node;
      });
    };
    setFileTree(addToNode(fileTree));
  };

  const addNewFolder = (parentId: string) => {
    const folderName = prompt("Enter folder name:");
    if (!folderName) return;
    const newFolder: FileNode = { id: "folder-" + Date.now(), name: folderName, type: "folder", isOpen: false, children: [] };
    const addToNode = (nodes: FileNode[]): FileNode[] => {
      return nodes.map((node) => {
        if (node.id === parentId && node.type === "folder") return { ...node, children: [...(node.children || []), newFolder], isOpen: true };
        if (node.children) return { ...node, children: addToNode(node.children) };
        return node;
      });
    };
    setFileTree(addToNode(fileTree));
  };

  const deleteNode = (nodeId: string, nodeName: string) => {
    if (!confirm("Delete " + nodeName + "?")) return;
    const removeNode = (nodes: FileNode[]): FileNode[] => {
      return nodes.filter((node) => node.id !== nodeId).map((node) => {
        if (node.children) return { ...node, children: removeNode(node.children) };
        return node;
      });
    };
    setFileTree(removeNode(fileTree));
    if (openTabs.find((tab) => tab.id === nodeId)) closeTab(nodeId);
  };

  const renderFileTree = (nodes: FileNode[], depth: number = 0, inIdeMode: boolean = false) => {
    return nodes.map((node) => (
      <div key={node.id}>
        <div
          className={"flex items-center gap-2 px-2 py-1 hover:bg-cyan-500/5 cursor-pointer group " + (inIdeMode && activeTabId === node.id ? "bg-cyan-500/10" : "")}
          style={{ paddingLeft: depth * 12 + 8 + "px" }}
        >
          {node.type === "folder" ? (
            <>
              <button onClick={() => toggleFolder(node.id)} className="flex items-center gap-1 flex-1">
                {node.isOpen ? <ChevronDown className="w-3 h-3 text-slate-500" /> : <ChevronRight className="w-3 h-3 text-slate-500" />}
                {node.isOpen ? <FolderOpen className="w-3 h-3 text-orange-400" /> : <Folder className="w-3 h-3 text-orange-400" />}
                <span className="text-xs text-slate-300 font-mono">{node.name}</span>
              </button>
              <div className="opacity-0 group-hover:opacity-100 flex items-center gap-1">
                <button onClick={() => addNewFile(node.id)} className="p-1 hover:bg-cyan-500/20 rounded" title="New File"><FilePlus className="w-3 h-3 text-cyan-400" /></button>
                <button onClick={() => addNewFolder(node.id)} className="p-1 hover:bg-cyan-500/20 rounded" title="New Folder"><FolderPlus className="w-3 h-3 text-cyan-400" /></button>
                {node.id !== "root" && <button onClick={() => deleteNode(node.id, node.name)} className="p-1 hover:bg-red-500/20 rounded" title="Delete"><Trash2 className="w-3 h-3 text-red-400" /></button>}
              </div>
            </>
          ) : (
            <>
              <button onClick={() => openFile(node)} className="flex items-center gap-1 flex-1">
                <div className="w-3" />
                {getFileIcon(node.extension || "")}
                <span className="text-xs text-slate-300 font-mono">{node.name}</span>
              </button>
              <div className="opacity-0 group-hover:opacity-100 flex items-center gap-1">
                <button onClick={() => deleteNode(node.id, node.name)} className="p-1 hover:bg-red-500/20 rounded" title="Delete"><Trash2 className="w-3 h-3 text-red-400" /></button>
              </div>
            </>
          )}
        </div>
        {node.type === "folder" && node.isOpen && node.children && (
          <div>{renderFileTree(node.children, depth + 1, inIdeMode)}</div>
        )}
      </div>
    ));
  };

  const activeTab = openTabs.find((tab) => tab.id === activeTabId);

  const toolsPanelOptions = [
    { id: "explorer" as ToolsPanel, icon: Folder, label: "Explorer" },
    { id: "search" as ToolsPanel, icon: Search, label: "Search" },
    { id: "source-control" as ToolsPanel, icon: GitBranch, label: "Source Control", badge: 752 },
    { id: "debug" as ToolsPanel, icon: Bug, label: "Debug" },
    { id: "extensions" as ToolsPanel, icon: Package, label: "Extensions" },
  ];

  // ============================================================
  // AUTONOMOUS MODE RENDER
  // ============================================================
  if (mode === "autonomous") {
    // Animation speeds per state
    const orbitalSpeed = activityState === "building" ? 25 : activityState === "thinking" ? 45 : 100;
    const coreScale = activityState === "building" ? [1, 1.12, 1] : activityState === "thinking" ? [1, 1.06, 1] : [1, 1.02, 1];
    const corePulseDuration = activityState === "building" ? 2 : activityState === "thinking" ? 3.5 : 6;
    const agentColor = activeAgent === "designer"
      ? { primary: "#a855f7", secondary: "#d946ef", glow: "rgba(168, 85, 247, 0.3)" }
      : { primary: "#22d3ee", secondary: "#06b6d4", glow: "rgba(34, 211, 238, 0.3)" };

    const stateLabel = activityState === "idle" ? "READY \\u00B7 AWAITING INSTRUCTIONS"
      : activityState === "thinking" ? "PROCESSING\\u2026"
      : activityState === "building" ? ("CONSTRUCTING" + (currentTool ? " \\u00B7 " + currentTool.toUpperCase() : ""))
      : "COMPLETE";

    return (
      <div className="fixed inset-0 w-full h-full flex flex-col overflow-hidden bg-[#0b0f14]">
        {/* === TOP BAR === */}
        <div className="h-11 bg-[#0f1419] border-b border-cyan-500/10 flex items-center justify-between px-4 shrink-0">
          <div className="flex items-center gap-3">
            {/* Close / Back button */}
            <button
              onClick={() => postToWpf({ type: "navigate_back" })}
              className="p-1.5 rounded-lg hover:bg-red-500/10 border border-transparent hover:border-red-500/20 transition-all group"
              title="Back to Command Center"
            >
              <ArrowLeft className="w-4 h-4 text-slate-500 group-hover:text-slate-200 transition-colors" />
            </button>
            <div className="w-px h-5 bg-slate-700/40" />
            <div>
              <div className="text-xs font-mono text-cyan-400 uppercase tracking-wider">ATLAS AI</div>
              <div
                className="text-[9px] font-mono uppercase tracking-wider"
                style={{ color: activityState === "complete" ? "#10b981" : activityState === "building" ? "#f97316" : activityState === "thinking" ? agentColor.primary : "#475569" }}
              >
                {stateLabel}
              </div>
            </div>
          </div>

          <div className="flex items-center gap-2">
            {/* Agent Selector */}
            <div className="flex items-center border border-cyan-500/20 rounded-lg overflow-hidden">
              <button
                onClick={() => setActiveAgent("builder")}
                className={"flex items-center gap-1.5 px-3 py-1 text-[10px] font-mono uppercase tracking-wider transition-all " + (activeAgent === "builder" ? "bg-cyan-500/20 text-cyan-400" : "bg-transparent text-slate-500 hover:text-slate-300")}
              >
                <Boxes className="w-3 h-3" />
                Builder
              </button>
              <button
                onClick={() => setActiveAgent("designer")}
                className={"flex items-center gap-1.5 px-3 py-1 text-[10px] font-mono uppercase tracking-wider transition-all " + (activeAgent === "designer" ? "bg-purple-500/20 text-purple-400" : "bg-transparent text-slate-500 hover:text-slate-300")}
              >
                <Layout className="w-3 h-3" />
                Designer
              </button>
            </div>
            <button
              onClick={() => setMode("ide")}
              className="flex items-center gap-1.5 px-3 py-1 bg-orange-500/10 hover:bg-orange-500/20 border border-orange-500/30 rounded-lg text-orange-400 text-[10px] font-mono uppercase tracking-wider transition-all"
            >
              <Code2 className="w-3 h-3" />
              IDE
            </button>
          </div>
        </div>

        {/* === CONTENT === */}
        <div className="flex-1 flex overflow-hidden min-h-0">
          {/* Main Visualization Area */}
          <AnimatePresence>
            {showBuildViz && (
              <motion.div
                initial={{ width: 0, opacity: 0 }}
                animate={{ width: "100%", opacity: 1 }}
                exit={{ width: 0, opacity: 0 }}
                className="flex-1 flex flex-col bg-[#0b0f14] overflow-hidden relative min-w-0"
              >
                {/* Close viz button */}
                <button
                  onClick={() => setShowBuildViz(false)}
                  className="absolute top-3 right-3 z-20 p-1.5 rounded-lg bg-slate-800/60 hover:bg-red-500/20 border border-slate-700/30 hover:border-red-500/30 transition-all"
                  title="Close Build Visualization"
                >
                  <X className="w-3 h-3 text-slate-500 hover:text-red-400" />
                </button>

                {/* Background Grid — very subtle */}
                <div className="absolute inset-0 pointer-events-none" style={{ opacity: 0.02 }}>
                  <svg width="100%" height="100%">
                    <defs>
                      <pattern id="build-grid" width="50" height="50" patternUnits="userSpaceOnUse">
                        <path d="M 50 0 L 0 0 0 50" fill="none" stroke={agentColor.primary} strokeWidth="0.5" />
                      </pattern>
                    </defs>
                    <rect width="100%" height="100%" fill="url(#build-grid)" />
                  </svg>
                </div>

                {/* === LAYER 1: Ambient Particles — always present, intensity varies === */}
                <div className="absolute inset-0 pointer-events-none overflow-hidden">
                  {Array.from({ length: 15 }).map((_, i) => (
                    <motion.div
                      key={"p-" + i}
                      className="absolute rounded-full"
                      style={{
                        width: 2,
                        height: 2,
                        backgroundColor: i % 3 === 0 ? agentColor.primary : i % 3 === 1 ? "#f97316" : "#a855f7",
                        left: ((i * 7.3) % 100) + "%",
                        top: ((i * 11.1) % 100) + "%",
                      }}
                      animate={{
                        y: [0, -20 - i * 2, 0],
                        x: [0, i % 2 === 0 ? 10 : -10, 0],
                        opacity: activityState === "idle" ? [0.03, 0.1, 0.03]
                          : activityState === "thinking" ? [0.05, 0.25, 0.05]
                          : activityState === "building" ? [0.1, 0.4, 0.1]
                          : [0.05, 0.2, 0.05],
                      }}
                      transition={{
                        duration: activityState === "idle" ? 12 + i * 0.8 : activityState === "building" ? 5 + i * 0.4 : 8 + i * 0.6,
                        repeat: Infinity,
                        delay: i * 1.2,
                        ease: "easeInOut"
                      }}
                    />
                  ))}
                </div>

                {/* === LAYER 2: Data Flow Lines — visible during thinking + building === */}
                {activityState !== "idle" && activityState !== "complete" && (
                  <svg className="absolute inset-0 w-full h-full pointer-events-none" style={{ opacity: 0.12 }}>
                    {Array.from({ length: 5 }).map((_, i) => (
                      <motion.line
                        key={"flow-" + i}
                        x1={(10 + i * 18) + "%"}
                        y1="0%"
                        x2={(15 + i * 16) + "%"}
                        y2="100%"
                        stroke={agentColor.primary}
                        strokeWidth="0.5"
                        strokeDasharray="3 10"
                        animate={{
                          strokeDashoffset: [0, -100],
                          opacity: [0, 0.5, 0],
                        }}
                        transition={{
                          strokeDashoffset: { duration: activityState === "building" ? 4 : 7, repeat: Infinity, ease: "linear" },
                          opacity: { duration: activityState === "building" ? 3 : 5, repeat: Infinity, delay: i * 0.6 },
                        }}
                      />
                    ))}
                  </svg>
                )}

                {/* === LAYER 3: Circuit Pattern — building only === */}
                {activityState === "building" && (
                  <div className="absolute inset-0 pointer-events-none" style={{ opacity: 0.06 }}>
                    <svg width="100%" height="100%">
                      <defs>
                        <pattern id="circuit" width="100" height="100" patternUnits="userSpaceOnUse">
                          <path d="M 0 50 H 35 V 15 H 65 V 50 H 100" fill="none" stroke={agentColor.primary} strokeWidth="0.5" />
                          <circle cx="35" cy="15" r="2" fill={agentColor.primary} opacity="0.5" />
                          <circle cx="65" cy="50" r="2" fill={agentColor.primary} opacity="0.5" />
                        </pattern>
                      </defs>
                      <motion.rect
                        width="100%" height="100%" fill="url(#circuit)"
                        initial={{ opacity: 0 }}
                        animate={{ opacity: [0, 0.4, 0] }}
                        transition={{ duration: 6, repeat: Infinity }}
                      />
                    </svg>
                  </div>
                )}

                {/* === LAYER 4: Hex Grid — building state accent === */}
                {activityState === "building" && (
                  <div className="absolute inset-0 pointer-events-none" style={{ opacity: 0.04 }}>
                    <svg width="100%" height="100%">
                      <defs>
                        <pattern id="hex" width="56" height="100" patternUnits="userSpaceOnUse">
                          <path d="M28,2 L52,18 L52,50 L28,66 L4,50 L4,18 Z" fill="none" stroke={agentColor.secondary} strokeWidth="0.5" />
                          <path d="M28,34 L52,50 L52,82 L28,98 L4,82 L4,50 Z" fill="none" stroke={agentColor.secondary} strokeWidth="0.5" />
                        </pattern>
                      </defs>
                      <motion.rect
                        width="100%" height="100%" fill="url(#hex)"
                        animate={{ opacity: [0.2, 0.5, 0.2] }}
                        transition={{ duration: 4, repeat: Infinity }}
                      />
                    </svg>
                  </div>
                )}

                {/* === CENTRAL VISUALIZATION === */}
                <div className="flex-1 flex items-center justify-center p-8 relative overflow-hidden">
                  {/* Neural Network Background lines */}
                  <svg
                    className="absolute inset-0 w-full h-full"
                    style={{ opacity: activityState === "idle" ? 0.03 : activityState === "thinking" ? 0.08 : activityState === "building" ? 0.15 : 0.05 }}
                  >
                    {assemblyNodes.map((node, i) =>
                      assemblyNodes.slice(i + 1).filter((_, j) => j < 2).map((target, j) => (
                        <motion.line
                          key={i + "-" + j}
                          x1={((node.x / 600) * 100) + "%"}
                          y1={((node.y / 400) * 100) + "%"}
                          x2={((target.x / 600) * 100) + "%"}
                          y2={((target.y / 400) * 100) + "%"}
                          stroke={node.active || target.active ? agentColor.primary : "#1e293b"}
                          strokeWidth="0.5"
                          animate={{
                            opacity: node.active || target.active ? [0, 0.5, 0] : [0, 0.05, 0]
                          }}
                          transition={{
                            duration: activityState === "idle" ? 8 : 4,
                            repeat: Infinity,
                            delay: i * 0.8
                          }}
                        />
                      ))
                    )}
                    {assemblyNodes.map((node) => (
                      <motion.circle
                        key={node.id}
                        cx={((node.x / 600) * 100) + "%"}
                        cy={((node.y / 400) * 100) + "%"}
                        r={node.active ? 2.5 : 1.5}
                        fill={node.active ? agentColor.primary : "#334155"}
                        animate={{
                          opacity: node.active ? [0.2, 0.7, 0.2] : [0.05, 0.1, 0.05]
                        }}
                        transition={{ duration: 3 }}
                      />
                    ))}
                  </svg>

                  {/* Central Orbital System */}
                  <div className="relative z-10">
                    <motion.div
                      className="relative w-[min(380px,45vh)] h-[min(380px,45vh)]"
                      animate={{ rotate: 360 }}
                      transition={{ duration: orbitalSpeed, repeat: Infinity, ease: "linear" }}
                    >
                      {/* Orbital Rings */}
                      {[0, 1, 2].map((ring) => (
                        <motion.div
                          key={ring}
                          className="absolute inset-0 rounded-full"
                          style={{
                            border: "1px solid " + (ring === 0 ? agentColor.primary + "25" : ring === 1 ? "#f9731618" : "#a855f718"),
                            width: (100 - ring * 15) + "%",
                            height: (100 - ring * 15) + "%",
                            top: (ring * 7.5) + "%",
                            left: (ring * 7.5) + "%",
                          }}
                          animate={{
                            rotate: ring % 2 === 0 ? -360 : 360,
                          }}
                          transition={{
                            duration: orbitalSpeed + ring * 15,
                            repeat: Infinity,
                            ease: "linear"
                          }}
                        />
                      ))}

                      {/* Building Blocks — only visible in thinking/building states, slow */}
                      {(activityState === "thinking" || activityState === "building") &&
                        buildingBlocks.slice(0, activityState === "thinking" ? 8 : 20).map((block) => (
                          <motion.div
                            key={block.id}
                            className="absolute rounded-sm"
                            style={{
                              width: block.size * 0.6,
                              height: block.size * 0.6,
                              left: ((block.x / 600) * 100) + "%",
                              top: ((block.y / 400) * 100) + "%",
                              backgroundColor: block.color + "15",
                              border: "1px solid " + block.color + "30",
                            }}
                            animate={{
                              opacity: activityState === "thinking" ? [0, 0.2, 0] : [0, 0.45, 0],
                              scale: [0.6, 1, 0.6],
                              rotate: [0, 45],
                            }}
                            transition={{
                              duration: activityState === "thinking" ? 12 : 8,
                              repeat: Infinity,
                              delay: block.delay * 2,
                              ease: "easeInOut"
                            }}
                          />
                        ))
                      }

                      {/* Center Core */}
                      <motion.div
                        className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2"
                        animate={{ scale: coreScale }}
                        transition={{ duration: corePulseDuration, repeat: Infinity, ease: "easeInOut" }}
                      >
                        <div
                          className="w-20 h-20 md:w-24 md:h-24 rounded-full backdrop-blur-md flex items-center justify-center"
                          style={{
                            background: "radial-gradient(circle, " + agentColor.primary + "15 0%, transparent 70%)",
                            border: "1px solid " + agentColor.primary + "30",
                            boxShadow: activityState === "idle" ? "none" : "0 0 30px " + agentColor.glow,
                          }}
                        >
                          <motion.div
                            animate={{ rotate: -360 }}
                            transition={{ duration: orbitalSpeed * 0.5, repeat: Infinity, ease: "linear" }}
                          >
                            {activityState === "complete" ? (
                              <CheckCircle2 className="w-8 h-8 md:w-10 md:h-10 text-green-400" />
                            ) : activeAgent === "designer" ? (
                              <Layout className="w-8 h-8 md:w-10 md:h-10" style={{ color: agentColor.primary }} />
                            ) : (
                              <Boxes className="w-8 h-8 md:w-10 md:h-10" style={{ color: agentColor.primary }} />
                            )}
                          </motion.div>
                        </div>
                      </motion.div>

                      {/* Orbiting Tool Components — only shown when real tools are active */}
                      {components.length > 0 && components.slice(-4).map((comp, i) => {
                        const total = Math.min(components.length, 4);
                        const angle = (i / total) * Math.PI * 2;
                        const radius = 38;
                        return (
                          <motion.div
                            key={comp.id}
                            className="absolute"
                            style={{
                              left: (50 + Math.cos(angle) * radius) + "%",
                              top: (50 + Math.sin(angle) * radius) + "%",
                              transform: "translate(-50%, -50%)",
                            }}
                          >
                            <motion.div
                              className={"w-8 h-8 md:w-10 md:h-10 rounded-lg border flex items-center justify-center backdrop-blur-sm " + (
                                comp.status === "complete"
                                  ? "bg-green-500/10 border-green-400/40"
                                  : comp.status === "building"
                                  ? "bg-orange-500/10 border-orange-400/40"
                                  : "bg-slate-800/10 border-slate-600/20"
                              )}
                              animate={comp.status === "building" ? { opacity: [0.6, 1, 0.6] } : {}}
                              transition={{ duration: 2.5, repeat: Infinity }}
                            >
                              <div className={comp.status === "complete" ? "text-green-400" : comp.status === "building" ? "text-orange-400" : "text-slate-600"}>
                                {getComponentIcon(comp.type)}
                              </div>
                            </motion.div>
                          </motion.div>
                        );
                      })}
                    </motion.div>
                  </div>

                  {/* === Wave Form — bottom ambient === */}
                  <div className="absolute bottom-0 left-0 right-0 h-12 pointer-events-none overflow-hidden">
                    <svg width="100%" height="100%" viewBox="0 0 1200 48" preserveAspectRatio="none">
                      <motion.path
                        fill="none"
                        stroke={agentColor.primary}
                        strokeWidth="0.5"
                        animate={{
                          d: activityState === "idle"
                            ? ["M0 24 Q 150 20, 300 24 T 600 24 T 900 24 T 1200 24",
                               "M0 24 Q 150 28, 300 24 T 600 24 T 900 24 T 1200 24"]
                            : activityState === "building"
                            ? ["M0 24 Q 150 8, 300 24 T 600 24 T 900 24 T 1200 24",
                               "M0 24 Q 150 40, 300 24 T 600 24 T 900 24 T 1200 24"]
                            : ["M0 24 Q 150 16, 300 24 T 600 24 T 900 24 T 1200 24",
                               "M0 24 Q 150 32, 300 24 T 600 24 T 900 24 T 1200 24"],
                          opacity: activityState === "idle" ? 0.06 : activityState === "building" ? 0.2 : 0.12,
                        }}
                        transition={{
                          duration: activityState === "idle" ? 8 : activityState === "building" ? 2 : 4,
                          repeat: Infinity,
                          repeatType: "reverse",
                          ease: "easeInOut"
                        }}
                      />
                    </svg>
                  </div>

                  {/* Activity Indicator — only when not idle */}
                  <AnimatePresence>
                    {activityState !== "idle" && (
                      <motion.div
                        initial={{ opacity: 0, y: 20 }}
                        animate={{ opacity: 1, y: 0 }}
                        exit={{ opacity: 0, y: -20 }}
                        transition={{ duration: 0.5 }}
                        className="absolute bottom-16 left-1/2 -translate-x-1/2"
                      >
                        <div
                          className="flex items-center gap-3 bg-[#0f1419]/90 backdrop-blur-md rounded-full px-4 py-2"
                          style={{ border: "1px solid " + (activityState === "complete" ? "#10b98130" : agentColor.primary + "25") }}
                        >
                          {activityState === "complete" ? (
                            <>
                              <CheckCircle2 className="w-3.5 h-3.5 text-green-400" />
                              <span className="text-[10px] font-mono text-green-400">COMPLETE</span>
                            </>
                          ) : (
                            <>
                              <motion.div
                                animate={{ rotate: 360 }}
                                transition={{ duration: activityState === "building" ? 3 : 5, repeat: Infinity, ease: "linear" }}
                              >
                                {activityState === "building"
                                  ? <Cpu className="w-3.5 h-3.5" style={{ color: agentColor.primary }} />
                                  : <Binary className="w-3.5 h-3.5" style={{ color: agentColor.primary }} />
                                }
                              </motion.div>
                              <span className="text-[10px] font-mono" style={{ color: agentColor.primary }}>
                                {activityState === "building"
                                  ? (activeAgent === "designer" ? "DESIGNING\\u2026" : "BUILDING\\u2026")
                                  : "THINKING\\u2026"
                                }
                              </span>
                              <div className="flex gap-0.5">
                                {[0, 1, 2].map((i) => (
                                  <motion.div
                                    key={i}
                                    className="w-1 h-1 rounded-full"
                                    style={{ backgroundColor: agentColor.primary }}
                                    animate={{ scale: [1, 1.4, 1], opacity: [0.3, 1, 0.3] }}
                                    transition={{ duration: 1.8, repeat: Infinity, delay: i * 0.3 }}
                                  />
                                ))}
                              </div>
                            </>
                          )}
                        </div>
                      </motion.div>
                    )}
                  </AnimatePresence>
                </div>

                {/* Build Progress Bar — only visible during building/complete with progress */}
                <AnimatePresence>
                  {showProgress && (activityState === "building" || activityState === "complete" || buildProgress > 0) && (
                    <motion.div
                      initial={{ height: 0, opacity: 0 }}
                      animate={{ height: "auto", opacity: 1 }}
                      exit={{ height: 0, opacity: 0 }}
                      className="bg-[#0f1419]/80 backdrop-blur-sm border-t border-cyan-500/10 shrink-0 overflow-hidden"
                    >
                      <div className="p-3 relative">
                        <button
                          onClick={() => setShowProgress(false)}
                          className="absolute top-2 right-2 p-1 rounded hover:bg-red-500/20 transition-all"
                          title="Close Progress"
                        >
                          <X className="w-3 h-3 text-slate-500 hover:text-red-400" />
                        </button>
                        <div className="flex items-center justify-between mb-1.5">
                          <div className="flex items-center gap-2">
                            <Activity className="w-3.5 h-3.5" style={{ color: activityState === "complete" ? "#10b981" : agentColor.primary }} />
                            <div>
                              <div className="text-[9px] font-mono text-slate-500 uppercase tracking-wider">
                                {activityState === "complete" ? "COMPLETE" : "BUILD PROGRESS"}
                              </div>
                              {buildProgress > 0 && (
                                <div className="text-xs font-mono font-bold" style={{ color: activityState === "complete" ? "#10b981" : agentColor.primary }}>
                                  {buildProgress.toFixed(0)}%
                                </div>
                              )}
                            </div>
                          </div>
                          {currentTool && (
                            <div className="text-[9px] font-mono text-orange-400 uppercase tracking-wider mr-6">
                              {currentTool}
                            </div>
                          )}
                        </div>
                        {buildProgress > 0 && (
                          <div className="h-1.5 bg-slate-900 rounded-full overflow-hidden border border-cyan-500/10">
                            <motion.div
                              className="h-full rounded-full"
                              style={{
                                width: buildProgress + "%",
                                background: activityState === "complete"
                                  ? "linear-gradient(90deg, #10b981, #22d3ee)"
                                  : "linear-gradient(90deg, " + agentColor.primary + ", #f97316)",
                              }}
                              transition={{ duration: 0.3 }}
                            />
                          </div>
                        )}
                        {components.length > 0 && (
                          <div className="flex items-center gap-3 mt-1.5">
                            {components.slice(-6).map(comp => (
                              <div key={comp.id} className="flex items-center gap-1">
                                <div className={"w-1.5 h-1.5 rounded-full " + (
                                  comp.status === "complete" ? "bg-green-400" :
                                  comp.status === "building" ? "bg-orange-400 animate-pulse" :
                                  "bg-slate-600"
                                )} />
                                <span className="text-[8px] font-mono text-slate-500">{comp.name}</span>
                              </div>
                            ))}
                          </div>
                        )}
                      </div>
                    </motion.div>
                  )}
                </AnimatePresence>
              </motion.div>
            )}
          </AnimatePresence>

          {/* Restore panels when closed */}
          {!showBuildViz && (
            <div className="flex-1 flex items-center justify-center bg-[#0b0f14]">
              <motion.button
                onClick={() => { setShowBuildViz(true); setShowProgress(true); }}
                className="flex items-center gap-3 px-6 py-3 border border-cyan-500/30 rounded-lg text-cyan-400 text-sm font-mono hover:bg-cyan-500/10 transition-all"
                whileHover={{ scale: 1.05 }}
                whileTap={{ scale: 0.95 }}
              >
                <Orbit className="w-5 h-5" />
                RESTORE BUILD VISUALIZATION
              </motion.button>
            </div>
          )}

          {/* Right Panel — AI Chat (always visible) */}
          <div className="w-72 flex flex-col bg-[#0b0f14]/60 border-l border-cyan-500/10 backdrop-blur-sm shrink-0">
            <div className="p-2.5 border-b border-cyan-500/10 shrink-0">
              <div className="flex items-center gap-2">
                <div className={"w-1.5 h-1.5 rounded-full " + (
                  activityState === "idle" ? "bg-slate-500" :
                  activityState === "thinking" ? "bg-cyan-400 animate-pulse" :
                  activityState === "building" ? "bg-orange-400 animate-pulse" :
                  "bg-green-400"
                )} />
                <div className="flex-1">
                  <h3 className="text-[10px] font-mono text-cyan-400 uppercase tracking-wider">AI Assistant</h3>
                  <p className="text-[9px] text-slate-600 font-mono">
                    {activeAgent === "designer" ? "Design mode" : "Build mode"}
                  </p>
                </div>
              </div>
            </div>

            <div className="flex-1 overflow-y-auto p-2.5 space-y-1.5 scrollbar-hide">
              {messages.map((msg) => (
                <motion.div
                  key={msg.id}
                  initial={{ opacity: 0, y: 8 }}
                  animate={{ opacity: 1, y: 0 }}
                  className={"flex " + (msg.sender === "user" ? "justify-end" : "justify-start")}
                >
                  <div className={"max-w-[90%] rounded-lg px-2 py-1 " + (
                    msg.sender === "atlas"
                      ? "bg-[#0f1419] border border-cyan-500/15"
                      : "bg-slate-800/50 border border-slate-700/40"
                  )}>
                    <div className="flex items-center gap-1 mb-0.5">
                      <span className={"text-[7px] font-mono uppercase tracking-widest " + (msg.sender === "atlas" ? "text-cyan-400" : "text-orange-400")}>
                        {msg.sender === "atlas" ? "Atlas" : "You"}
                      </span>
                      <span className="text-[7px] text-slate-600 font-mono">
                        {msg.timestamp.toLocaleTimeString("en-US", { hour: "2-digit", minute: "2-digit" })}
                      </span>
                    </div>
                    <p className={"text-[10px] leading-relaxed " + (msg.sender === "atlas" ? "text-slate-300 font-mono tracking-wide" : "text-slate-300")}>
                      {msg.content}
                    </p>
                  </div>
                </motion.div>
              ))}
            </div>

            <div className="p-2 border-t border-cyan-500/10 shrink-0">
              <div className="flex items-center gap-1.5 bg-[#0f1419] border border-cyan-500/15 rounded-lg p-1.5">
                <input
                  type="text"
                  value={input}
                  onChange={(e) => setInput(e.target.value)}
                  onKeyPress={(e) => e.key === "Enter" && handleSend()}
                  placeholder={activeAgent === "designer" ? "Describe your design..." : "Message Atlas..."}
                  className="flex-1 bg-transparent text-slate-200 text-[10px] outline-none placeholder:text-slate-600 font-mono"
                />
                <button
                  onClick={handleSend}
                  className="p-1 rounded bg-cyan-500/10 text-cyan-400 hover:bg-cyan-500/20 transition-all"
                >
                  <Send className="w-3 h-3" />
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>
    );
  }

  // ============================================================
  // IDE MODE RENDER
  // ============================================================
  return (
    <div className="fixed inset-0 w-full h-full flex flex-col overflow-hidden bg-[#0b0f14]">
      {/* Mode Toggle Button */}
      <motion.button
        onClick={() => setMode("autonomous")}
        className="fixed top-4 right-4 z-50 flex items-center gap-2 px-4 py-2 bg-cyan-500/10 hover:bg-cyan-500/20 border border-cyan-500/30 rounded-lg text-cyan-400 text-xs font-mono uppercase tracking-wider transition-all shadow-lg"
        whileHover={{ scale: 1.05 }}
        whileTap={{ scale: 0.98 }}
      >
        <Zap className="w-4 h-4" />
        Switch to Autonomous Mode
      </motion.button>

      {/* Top Toolbar */}
      <div className="h-12 bg-[#0f1419] border-b border-cyan-500/10 flex items-center justify-between px-4">
        <div className="flex items-center gap-2">
          <button
            onClick={() => postToWpf({ type: "navigate_back" })}
            className="p-1.5 rounded-lg hover:bg-red-500/10 border border-transparent hover:border-red-500/20 transition-all group mr-2"
            title="Back to Command Center"
          >
            <ArrowLeft className="w-4 h-4 text-slate-500 group-hover:text-slate-200 transition-colors" />
          </button>
          <motion.button
            onClick={saveCurrentFile}
            disabled={!activeTab?.isDirty}
            className={"flex items-center gap-2 px-3 py-1.5 border rounded text-xs font-mono uppercase tracking-wider transition-all " + (
              activeTab?.isDirty
                ? "bg-green-500/10 hover:bg-green-500/20 border-green-500/30 text-green-400"
                : "bg-slate-800/30 border-slate-700/30 text-slate-600 cursor-not-allowed"
            )}
            whileHover={activeTab?.isDirty ? { scale: 1.02 } : {}}
            whileTap={activeTab?.isDirty ? { scale: 0.98 } : {}}
          >
            <Save className="w-3 h-3" />
            Save
          </motion.button>
        </div>
        <div className="flex items-center gap-3">
          <div className="text-xs font-mono text-cyan-400 uppercase tracking-wider">
            ATLAS AI - IDE MODE
          </div>
          <motion.div
            animate={{ opacity: [0.5, 1, 0.5] }}
            transition={{ duration: 2, repeat: Infinity }}
            className="w-2 h-2 rounded-full bg-cyan-400"
          />
        </div>
      </div>

      <div className="flex-1 flex overflow-hidden">
        {/* Icon Sidebar */}
        <div className="w-12 bg-[#0f1419] border-r border-cyan-500/10 flex flex-col items-center py-4 gap-2">
          {toolsPanelOptions.map((item) => (
            <motion.button
              key={item.id}
              onClick={() => setActiveToolsPanel(activeToolsPanel === item.id ? null : item.id)}
              className={"relative p-2 rounded-lg transition-all " + (
                activeToolsPanel === item.id
                  ? "bg-cyan-500/20 text-cyan-400"
                  : "text-slate-500 hover:text-cyan-400 hover:bg-cyan-500/10"
              )}
              whileHover={{ scale: 1.05 }}
              whileTap={{ scale: 0.95 }}
              title={item.label}
            >
              <item.icon className="w-5 h-5" />
              {item.badge && (
                <div className="absolute -top-1 -right-1 bg-orange-500 text-[9px] font-mono text-white px-1.5 py-0.5 rounded-full min-w-[18px] text-center">
                  {item.badge}
                </div>
              )}
            </motion.button>
          ))}
          <div className="flex-1" />
          <motion.button
            className="p-2 rounded-lg text-slate-500 hover:text-cyan-400 hover:bg-cyan-500/10 transition-all"
            whileHover={{ scale: 1.05, rotate: 90 }}
            whileTap={{ scale: 0.95 }}
            title="Settings"
          >
            <Settings className="w-5 h-5" />
          </motion.button>
        </div>

        {/* Side Panel */}
        <AnimatePresence>
          {activeToolsPanel && (
            <motion.div
              initial={{ width: 0, opacity: 0 }}
              animate={{ width: 280, opacity: 1 }}
              exit={{ width: 0, opacity: 0 }}
              transition={{ type: "spring", damping: 30, stiffness: 300 }}
              className="bg-[#0f1419] border-r border-cyan-500/20 flex flex-col overflow-hidden"
            >
              <div className="h-12 bg-[#0b0f14] border-b border-cyan-500/10 flex items-center justify-between px-4 shrink-0">
                <span className="text-xs font-mono text-cyan-400 uppercase tracking-wider">
                  {toolsPanelOptions.find(o => o.id === activeToolsPanel)?.label}
                </span>
                <button onClick={() => setActiveToolsPanel(null)} className="p-1 hover:bg-cyan-500/20 rounded transition-colors">
                  <X className="w-4 h-4 text-slate-500 hover:text-cyan-400" />
                </button>
              </div>
              <div className="flex-1 overflow-y-auto scrollbar-hide p-3">
                {activeToolsPanel === "explorer" && <div>{renderFileTree(fileTree, 0, true)}</div>}
                {activeToolsPanel === "search" && (
                  <div>
                    <input
                      type="text"
                      value={searchQuery}
                      onChange={(e) => setSearchQuery(e.target.value)}
                      placeholder="Search files..."
                      className="w-full bg-[#0b0f14] border border-cyan-500/20 rounded px-3 py-2 text-xs text-slate-200 outline-none focus:border-cyan-500/50 font-mono"
                    />
                  </div>
                )}
                {activeToolsPanel === "source-control" && (
                  <div>
                    <div className="text-xs text-slate-400 font-mono mb-2">Changes</div>
                    <div className="text-xs text-slate-600 font-mono">Connect a repository to view changes</div>
                  </div>
                )}
                {activeToolsPanel === "debug" && (
                  <div className="text-xs text-slate-500 font-mono">Debug panel - Configure launch.json</div>
                )}
                {activeToolsPanel === "extensions" && (
                  <div className="space-y-2">
                    {["C# Extension", "XAML Tools", "WPF Designer"].map((ext) => (
                      <div key={ext} className="p-3 bg-[#0b0f14] border border-cyan-500/10 rounded hover:border-cyan-500/30 cursor-pointer">
                        <div className="text-xs text-slate-300 font-mono">{ext}</div>
                        <div className="text-[10px] text-green-400 mt-1">\\u25CF Installed</div>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </motion.div>
          )}
        </AnimatePresence>

        {/* Editor Area */}
        <div className="flex-1 flex flex-col overflow-hidden">
          {openTabs.length > 0 && (
            <div className="h-10 bg-[#0f1419] border-b border-cyan-500/10 flex items-center gap-1 px-2 overflow-x-auto scrollbar-hide">
              {openTabs.map((tab) => (
                <motion.button
                  key={tab.id}
                  onClick={() => setActiveTabId(tab.id)}
                  className={"flex items-center gap-2 px-3 py-1.5 rounded-t text-xs font-mono transition-all " + (
                    activeTabId === tab.id
                      ? "bg-[#0b0f14] text-cyan-400 border-t-2 border-cyan-400"
                      : "text-slate-400 hover:text-slate-300 hover:bg-[#0b0f14]/50"
                  )}
                  whileHover={{ y: -2 }}
                >
                  {getFileIcon(tab.name.split(".").pop() || "")}
                  <span>{tab.name}</span>
                  {tab.isDirty && <div className="w-2 h-2 rounded-full bg-orange-400" />}
                  <button onClick={(e) => closeTab(tab.id, e)} className="ml-1 hover:bg-slate-700/50 rounded p-0.5">
                    <X className="w-3 h-3" />
                  </button>
                </motion.button>
              ))}
            </div>
          )}

          <div className="flex-1 overflow-hidden">
            {activeTab ? (
              <textarea
                ref={textareaRef}
                value={activeTab.content}
                onChange={(e) => updateTabContent(e.target.value)}
                className="w-full h-full bg-[#0b0f14] text-slate-300 font-mono text-sm p-4 outline-none resize-none"
                spellCheck={false}
              />
            ) : (
              <div className="w-full h-full flex items-center justify-center text-slate-600 font-mono text-sm">
                No file open
              </div>
            )}
          </div>

          {showTerminal && (
            <motion.div initial={{ height: 0 }} animate={{ height: 200 }} className="bg-[#0f1419] border-t border-cyan-500/10 overflow-hidden">
              <div className="h-8 bg-[#0b0f14] border-b border-cyan-500/10 flex items-center justify-between px-3">
                <div className="flex items-center gap-2">
                  <Terminal className="w-3 h-3 text-cyan-400" />
                  <span className="text-xs font-mono text-cyan-400 uppercase">Terminal</span>
                </div>
                <button onClick={() => setShowTerminal(false)} className="p-1 hover:bg-slate-700/50 rounded">
                  <X className="w-3 h-3 text-slate-500" />
                </button>
              </div>
              <div className="p-3 font-mono text-xs text-green-400">
                <div>$ dotnet build</div>
                <div className="text-slate-500 mt-1">Build succeeded.</div>
              </div>
            </motion.div>
          )}
        </div>

        {showChat && (
          <motion.div initial={{ width: 0 }} animate={{ width: 320 }} className="bg-[#0f1419] border-l border-cyan-500/10 flex flex-col overflow-hidden">
            <div className="h-10 bg-[#0b0f14] border-b border-cyan-500/10 flex items-center justify-between px-3">
              <div className="flex items-center gap-2">
                <Sparkles className="w-3 h-3 text-cyan-400" />
                <span className="text-xs font-mono text-cyan-400 uppercase">AI Assistant</span>
              </div>
              <button onClick={() => setShowChat(false)} className="p-1 hover:bg-slate-700/50 rounded">
                <X className="w-3 h-3 text-slate-500" />
              </button>
            </div>
            <div className="flex-1 overflow-y-auto p-3 space-y-2 scrollbar-hide">
              {messages.slice(-10).map((msg) => (
                <div key={msg.id} className="text-xs text-slate-400 font-mono">
                  <span className="text-cyan-400">[Atlas]</span> {msg.content}
                </div>
              ))}
            </div>
            <div className="p-2 border-t border-cyan-500/10">
              <input
                type="text"
                placeholder="Ask AI..."
                className="w-full bg-[#0b0f14] border border-cyan-500/20 rounded px-2 py-1.5 text-xs text-slate-200 outline-none font-mono"
              />
            </div>
          </motion.div>
        )}
      </div>
    </div>
  );
}
`;

const outPath = path.join(__dirname, 'src', 'app', 'components', 'CodeIDE.tsx');
fs.writeFileSync(outPath, content, 'utf-8');
console.log('Written', content.length, 'chars to', outPath);
`;

fs.writeFileSync(path.join(__dirname, 'src', 'app', 'components', 'CodeIDE.tsx'), content, 'utf-8');
console.log('Written', content.length, 'chars to CodeIDE.tsx');
