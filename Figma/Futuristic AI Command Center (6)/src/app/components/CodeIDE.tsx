import { useState, useEffect, useLayoutEffect, useRef } from "react";
import { createPortal } from "react-dom";
import { motion, AnimatePresence, MotionConfig } from "motion/react";
import {
  Code2,
  Send,
  Folder,
  FolderOpen,
  FolderPlus,
  FileCode,
  FileText,
  FilePlus,
  ChevronRight,
  ChevronDown,
  Settings,
  Search,
  GitBranch,
  Bug,
  Package,
  X,
  Terminal,
  Save,
  Trash2,
  Cpu,
  Layout,
  Database,
  Wifi,
  Zap,
  Activity,
  Boxes,
  Orbit,
  Binary,
  CheckCircle2,
  Sparkles,
  ArrowLeft,
  Brain,
  Wand2,
  Play,
  FolderSearch,
  Plus,
  Globe,
  FileJson,
  Palette,
  Rocket,
  ListChecks,
  MessageSquare,
  Mic,
  Lightbulb,
  ChevronLeft,
  Copy,
  RotateCcw,
  Square,
} from "lucide-react";

// ============================================================
// TYPES & INTERFACES
// ============================================================
type IDEMode = "autonomous" | "ide";
type AgentType = "builder" | "designer" | "planner";
type ToolsPanel =
  | "explorer"
  | "search"
  | "source-control"
  | "debug"
  | "extensions"
  | null;
type ActivityState = "idle" | "thinking" | "building" | "complete" | "error";
type WorkflowPhase = "ask" | "plan" | "build" | "test" | "complete";
type AIProvider = "claude" | "openai" | "gemini";

interface ModelOption {
  id: string;
  name: string;
  description: string;
}

interface BuildComponent {
  id: string;
  name: string;
  type: "view" | "logic" | "data" | "api";
  status: "pending" | "building" | "complete";
  progress: number;
}
interface Message {
  id: string;
  sender: "user" | "atlas";
  content: string;
  timestamp: Date;
  type?: "plan" | "status" | "error" | "code";
}
interface FileNode {
  id: string;
  name: string;
  type: "file" | "folder";
  extension?: string;
  content?: string;
  isOpen?: boolean;
  children?: FileNode[];
}
interface EditorTab {
  id: string;
  name: string;
  content: string;
  isDirty: boolean;
  language?: string;
}
interface BuildingBlock {
  id: number;
  x: number;
  y: number;
  size: number;
  delay: number;
  color: string;
  label: string;
}
interface AssemblyNode {
  id: number;
  x: number;
  y: number;
  active: boolean;
}
interface PlanItem {
  id: string;
  title: string;
  status: "pending" | "active" | "done";
  description?: string;
}
interface TerminalLine {
  id: string;
  text: string;
  type: "stdout" | "stderr" | "system" | "command";
}
interface ProjectTemplate {
  id: string;
  name: string;
  language: string;
  icon: string;
  description: string;
  color: string;
}

// ============================================================
// CONSTANTS
// ============================================================
const AI_MODELS: Record<AIProvider, { id: string; name: string; description: string }[]> = {
  claude: [
    { id: "auto",                       name: "Auto",              description: "Best model automatically" },
    // Claude 4.0 → 4.6 ———————————————————
    { id: "claude-sonnet-4-20250514",   name: "Claude Sonnet 4.0", description: "Claude 4 series" },
    { id: "claude-opus-4-20250514",     name: "Claude Opus 4.0",   description: "Claude 4 series" },
    { id: "claude-haiku-4-20250514",    name: "Claude Haiku 4.0",  description: "Claude 4 series" },
    { id: "claude-sonnet-4-5-20251022", name: "Claude Sonnet 4.5", description: "Fast & capable" },
    { id: "claude-opus-4-5-20251022",   name: "Claude Opus 4.5",   description: "Most powerful" },
    { id: "claude-haiku-4-5-20251022",  name: "Claude Haiku 4.5",  description: "Lightning fast" },
    { id: "claude-sonnet-4-6-20260301", name: "Claude Sonnet 4.6", description: "Latest" },
    { id: "claude-opus-4-6-20260301",   name: "Claude Opus 4.6",   description: "Latest" },
    { id: "claude-haiku-4-6-20260301",  name: "Claude Haiku 4.6",  description: "Latest" },
  ],
  openai: [
    { id: "auto",                name: "Auto",               description: "Best model automatically" },
    // GPT 4.0 → 5.2 ———————————————————————
    { id: "gpt-4",               name: "GPT-4.0",             description: "Legacy GPT-4" },
    { id: "gpt-4-turbo",         name: "GPT-4 Turbo",         description: "Faster GPT-4" },
    { id: "gpt-4o",              name: "GPT-4o",              description: "Multimodal" },
    { id: "gpt-4.1",             name: "GPT-4.1",             description: "Strong reasoning" },
    { id: "gpt-5",               name: "GPT-5.0",             description: "Original GPT-5" },
    { id: "gpt-5-mini",          name: "GPT-5 Mini",          description: "Fast & affordable" },
    { id: "gpt-5.1",             name: "GPT-5.1",             description: "Highly capable" },
    { id: "gpt-5.2",             name: "GPT-5.2",             description: "Latest" },
    // Codex 5.1 → 5.3 —————————————————————
    { id: "gpt-5.1-codex",       name: "Codex 5.1",           description: "Code-optimised" },
    { id: "gpt-5.2-codex",       name: "Codex 5.2",           description: "Code-optimised" },
    { id: "gpt-5.3-codex",       name: "Codex 5.3",           description: "Newest" },
  ],
  gemini: [
    { id: "auto",                   name: "Auto",              description: "Best model automatically" },
    // Gemini 1.5 → 3.1 ———————————————————
    { id: "gemini-1.5-flash-latest",  name: "Gemini 1.5 Flash",  description: "Fast · latest" },
    { id: "gemini-1.5-pro-latest",    name: "Gemini 1.5 Pro",    description: "Strong reasoning · latest" },
    { id: "gemini-2.0-flash",        name: "Gemini 2.0 Flash",  description: "Efficient · fast" },
    { id: "gemini-2.5-pro",          name: "Gemini 2.5 Pro",    description: "Strong reasoning" },
    { id: "gemini-3-flash-preview",  name: "Gemini 3 Flash",    description: "Fast · preview" },
    { id: "gemini-3-pro-preview",    name: "Gemini 3 Pro",      description: "Powerful · preview" },
    { id: "gemini-3.1-pro-preview",  name: "Gemini 3.1 Pro",    description: "Latest · preview" },
  ],
};

const CHAT_STORAGE_KEY = "atlas_builder_chat_v1";

const DEFAULT_BLOCK_LABELS = [
  "EXE",
  "APK",
  "iOS",
  "C#",
  "DLL",
  "TS",
  "JS",
  "PY",
  "JAVA",
  "GO",
  "RS",
  "CSS",
] as const;

function inferBlockLabelsFromTree(root: FileNode): string[] {
  try {
    const labels = new Set<string>();
    const add = (...xs: string[]) => xs.forEach((x) => x && labels.add(x));

    let dotnet = false;
    let node = false;
    let python = false;
    let java = false;
    let go = false;
    let rust = false;
    let android = false;
    let ios = false;

    const walk = (n: FileNode) => {
      const name = (n.name || "").toLowerCase();
      const ext = (n.extension || name.split(".").pop() || "").toLowerCase();

      if (name.endsWith(".sln") || name.endsWith(".csproj") || name.endsWith(".fsproj") || name.endsWith(".vbproj")) dotnet = true;
      if (name === "nuget.config" || name.endsWith(".nupkg")) dotnet = true;
      if (name === "package.json" || name === "pnpm-lock.yaml" || name === "yarn.lock" || name === "package-lock.json") node = true;
      if (name === "requirements.txt" || name === "pyproject.toml" || ext === "py") python = true;
      if (name === "pom.xml" || name === "build.gradle" || name === "build.gradle.kts" || ext === "java" || ext === "kt") java = true;
      if (name === "go.mod" || ext === "go") go = true;
      if (name === "cargo.toml" || ext === "rs") rust = true;
      if (name.endsWith(".apk") || name === "androidmanifest.xml") android = true;
      if (name.endsWith(".xcodeproj") || ext === "swift") ios = true;

      if (ext === "exe") add("EXE");
      if (ext === "apk") add("APK");
      if (ext === "dll") add("DLL");

      if (n.children) n.children.forEach(walk);
    };

    walk(root);

    if (dotnet) add("C#", "DLL", "EXE", "NUGET");
    if (node) add("JS", "TS", "NPM", "NODE");
    if (python) add("PY", "PIP");
    if (java) add("JAVA", "JAR");
    if (go) add("GO");
    if (rust) add("RS", "CRATE");
    if (android) add("APK", "ANDROID");
    if (ios) add("iOS", "SWIFT");

    // Ensure we always have a good mix even for empty trees.
    DEFAULT_BLOCK_LABELS.forEach((x) => labels.add(x));

    return Array.from(labels).slice(0, 18);
  } catch {
    return Array.from(DEFAULT_BLOCK_LABELS);
  }
}

const PROJECT_TEMPLATES: ProjectTemplate[] = [
  { id: "react-ts", name: "React + TypeScript", language: "TypeScript", icon: "⚛️", description: "Modern React with TypeScript, Vite & Tailwind", color: "#61dafb" },
  { id: "nextjs", name: "Next.js App", language: "TypeScript", icon: "▲", description: "Full-stack React with Next.js 14", color: "#ffffff" },
  { id: "python-app", name: "Python Application", language: "Python", icon: "🐍", description: "Python with virtual environment", color: "#3776ab" },
  { id: "python-flask", name: "Flask API", language: "Python", icon: "🌶️", description: "Flask REST API with SQLAlchemy", color: "#4caf50" },
  { id: "dotnet-web", name: "ASP.NET Web API", language: "C#", icon: "🌐", description: ".NET 8 Web API with Swagger", color: "#512bd4" },
  { id: "dotnet-wpf", name: "WPF Application", language: "C#", icon: "🖥️", description: "WPF desktop application", color: "#68217a" },
  { id: "dotnet-maui", name: ".NET MAUI", language: "C#", icon: "📱", description: "Cross-platform .NET MAUI app", color: "#9b4dca" },
  { id: "node-express", name: "Node.js + Express", language: "JavaScript", icon: "🟢", description: "Express.js REST API", color: "#68a063" },
  { id: "flutter", name: "Flutter App", language: "Dart", icon: "🦋", description: "Cross-platform mobile app", color: "#027dfd" },
  { id: "rust-cli", name: "Rust CLI", language: "Rust", icon: "🦀", description: "Rust command line tool", color: "#dea584" },
  { id: "html-css", name: "HTML/CSS/JS", language: "HTML", icon: "🌍", description: "Static website with modern CSS", color: "#e34c26" },
  { id: "electron", name: "Electron App", language: "TypeScript", icon: "⚡", description: "Desktop app with Electron", color: "#47848f" },
];

const WORKFLOW_PHASES: { id: WorkflowPhase; label: string; icon: any; desc: string }[] = [
  { id: "ask", label: "ASK", icon: MessageSquare, desc: "Describe what you want" },
  { id: "plan", label: "PLAN", icon: ListChecks, desc: "AI creates a plan" },
  { id: "build", label: "BUILD", icon: Boxes, desc: "Execute the plan" },
  { id: "test", label: "TEST", icon: Bug, desc: "Verify & debug" },
  { id: "complete", label: "DONE", icon: CheckCircle2, desc: "Ready to ship" },
];

// ============================================================
// COMPONENT
// ============================================================
interface CodeIDEProps {
  initialMode?: IDEMode;
  showSidebar?: boolean;
  onReopenSidebar?: () => void;
}
export default function CodeIDE({ initialMode = "autonomous", showSidebar = true, onReopenSidebar }: CodeIDEProps) {
  // --- Core state ---
  const [mode, setMode] = useState<IDEMode>(() => {
    try {
      const urlMode = new URLSearchParams(window.location.search).get("mode");
      return urlMode === "ide" || urlMode === "autonomous" ? (urlMode as IDEMode) : initialMode;
    } catch {
      return initialMode;
    }
  });
  const [activeAgent, setActiveAgent] = useState<AgentType>("builder");
  const [activityState, setActivityState] = useState<ActivityState>("idle");
  const [activeToolsPanel, setActiveToolsPanel] = useState<ToolsPanel>("explorer");

  // --- NEW: Model selector ---
  const [selectedProvider, setSelectedProvider] = useState<AIProvider>("claude");
  const [selectedModel, setSelectedModel] = useState("auto");
  const [hostModelsByProvider, setHostModelsByProvider] = useState<Partial<Record<AIProvider, ModelOption[]>> | null>(null);
  const [showModelSelector, setShowModelSelector] = useState(false);
  const modelButtonRef = useRef<HTMLButtonElement | null>(null);
  const [modelDropdownPos, setModelDropdownPos] = useState<{ top: number; left: number } | null>(null);
  const hasAppliedHostSelectionRef = useRef(false);
  const lastUserModelSelectionAtRef = useRef<number>(0);
  const selectedProviderRef = useRef<AIProvider>("claude");

  useEffect(() => {
    selectedProviderRef.current = selectedProvider;
  }, [selectedProvider]);

  const getModelOptions = (provider: AIProvider): ModelOption[] => {
    const fromHost = hostModelsByProvider?.[provider];
    const base: ModelOption[] = Array.isArray(fromHost) && fromHost.length > 0 ? fromHost : (AI_MODELS[provider] as ModelOption[]);
    const auto: ModelOption = { id: "auto", name: "Auto", description: "Best model automatically" };
    const withoutAuto = base.filter((m) => m && m.id && m.id !== "auto");
    return [auto, ...withoutAuto];
  };

  // --- NEW: Workflow ---
  const [workflowPhase, setWorkflowPhase] = useState<WorkflowPhase>("ask");
  const [planItems, setPlanItems] = useState<PlanItem[]>([]);

  // --- NEW: Terminal ---
  const [terminalLines, setTerminalLines] = useState<TerminalLine[]>([
    { id: "init", text: "Atlas AI Terminal — Ready", type: "system" },
  ]);
  const [showTerminalPanel, setShowTerminalPanel] = useState(false);

  // --- NEW: New Project dialog ---
  const [showNewProject, setShowNewProject] = useState(false);

  // --- Design Canvas ---
  const [designOutput, setDesignOutput] = useState<string>("");
  const [designFileName, setDesignFileName] = useState<string>("");
  const [showDesignPanel, setShowDesignPanel] = useState(false);

  // --- Build & Viz state ---
  const [buildProgress, setBuildProgress] = useState(0);
  const [currentTool, setCurrentTool] = useState<string | null>(null);
  const [components, setComponents] = useState<BuildComponent[]>([]);
  const [buildingBlocks, setBuildingBlocks] = useState<BuildingBlock[]>([]);
  const [blockLabels, setBlockLabels] = useState<string[]>(Array.from(DEFAULT_BLOCK_LABELS));
  const [assemblyNodes, setAssemblyNodes] = useState<AssemblyNode[]>([]);
  const [showBuildViz, setShowBuildViz] = useState(true);
  const [showProgress, setShowProgress] = useState(true);
  const [showRightPanel, setShowRightPanel] = useState(true);
  const [lastBuildKind, setLastBuildKind] = useState<"dotnet" | "agent">("dotnet");
  const [dotnetBusy, setDotnetBusy] = useState(false);
  const completeTimerRef = useRef<any>(null);
  const thinkingTimerRef = useRef<any>(null);
  const lastThinkingActivityRef = useRef<number>(0);
  const lastAgentProgressLineRef = useRef<string>("");
  const lastPlanDigestRef = useRef<string>("");

  // --- Agent progress (WPF heartbeat + countdown) ---
  const [agentActive, setAgentActive] = useState(false);
  const [agentAttempt, setAgentAttempt] = useState(1);
  const [agentTimeoutMs, setAgentTimeoutMs] = useState<number | null>(null);
  const [agentElapsedMs, setAgentElapsedMs] = useState(0);
  const [agentRemainingMs, setAgentRemainingMs] = useState<number | null>(null);
  const [agentPhase, setAgentPhase] = useState<string>("idle");
  const [agentAction, setAgentAction] = useState<string>("");

  const formatCountdown = (ms: number | null) => {
    if (ms == null || !Number.isFinite(ms)) return "";
    const clamped = Math.max(0, Math.floor(ms));
    const totalSeconds = Math.floor(clamped / 1000);
    const m = Math.floor(totalSeconds / 60);
    const s = totalSeconds % 60;
    return `${m}:${String(s).padStart(2, "0")}`;
  };

  const setAndPersistMode = (nextMode: IDEMode) => {
    setMode(nextMode);
    try {
      const url = new URL(window.location.href);
      url.searchParams.set("mode", nextMode);
      window.history.replaceState(null, "", url.toString());
    } catch {}
  };

  // --- Chat ---
  const [messages, setMessages] = useState<Message[]>([
    {
      id: "1",
      sender: "atlas",
      content: "SYSTEMS ONLINE · READY FOR INSTRUCTIONS",
      timestamp: new Date(),
    },
  ]);
  const [input, setInput] = useState("");
  const [codeMicNote, setCodeMicNote] = useState("");
  const codeMicNoteTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Persist chat history (embedded UI)
  useEffect(() => {
    try {
      const raw = window.localStorage.getItem(CHAT_STORAGE_KEY);
      if (!raw) return;
      const parsed = JSON.parse(raw);
      if (!Array.isArray(parsed)) return;
      const restored: Message[] = parsed
        .filter((m: any) => m && typeof m.content === "string" && (m.sender === "user" || m.sender === "atlas"))
        .slice(-100)
        .map((m: any) => ({
          id: String(m.id ?? (Date.now().toString() + Math.random())),
          sender: m.sender,
          content: m.content,
          type: m.type,
          timestamp: new Date(m.timestamp ?? Date.now()),
        }));

      if (restored.length > 0) setMessages(restored);
    } catch {}
  }, []);

  useEffect(() => {
    try {
      const serializable = messages.slice(-100).map((m) => ({
        ...m,
        timestamp: m.timestamp instanceof Date ? m.timestamp.toISOString() : m.timestamp,
      }));
      window.localStorage.setItem(CHAT_STORAGE_KEY, JSON.stringify(serializable));
    } catch {}
  }, [messages]);

  // --- File tree ---
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

  // --- IDE editor state ---
  const [openTabs, setOpenTabs] = useState<EditorTab[]>([]);
  const [activeTabId, setActiveTabId] = useState<string | null>(null);
  const [searchQuery, setSearchQuery] = useState("");
  const [showTerminal, setShowTerminal] = useState(true);
  const [showChat, setShowChat] = useState(true);

  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const chatEndRef = useRef<HTMLDivElement>(null);
  const terminalEndRef = useRef<HTMLDivElement>(null);

  // ============================================================
  // HELPERS
  // ============================================================
  const addMessage = (content: string, sender: "user" | "atlas" = "atlas", type?: Message["type"]) => {
    const newMsg: Message = {
      id: Date.now().toString() + Math.random(),
      sender,
      content,
      timestamp: new Date(),
      type,
    };
    setMessages((prev) => {
      const next = [...prev, newMsg];
      return next.length > 100 ? next.slice(-100) : next;
    });
  };

  const addTerminalLine = (text: string, type: TerminalLine["type"] = "stdout") => {
    setTerminalLines((prev) => {
      const next = [...prev, { id: Date.now().toString() + Math.random(), text, type }];
      return next.length > 500 ? next.slice(-500) : next;
    });
  };

  // Stream build/tool output into the chat (single running message) to avoid the "red terminal spam".
  const buildStreamMsgIdRef = useRef<string | null>(null);
  const buildStreamLinesRef = useRef<string[]>([]);

  const resetBuildStream = () => {
    buildStreamMsgIdRef.current = null;
    buildStreamLinesRef.current = [];
  };

  const pushBuildStreamLine = (line: string) => {
    const text = String(line ?? "").trimEnd();
    if (!text) return;

    const MAX_LINES = 140;
    buildStreamLinesRef.current = [...buildStreamLinesRef.current, text].slice(-MAX_LINES);

    if (!buildStreamMsgIdRef.current) {
      buildStreamMsgIdRef.current = `build-stream-${Date.now()}-${Math.random()}`;
      const id = buildStreamMsgIdRef.current;
      const newMsg: Message = {
        id,
        sender: "atlas",
        type: "status",
        timestamp: new Date(),
        content: `AUTONOMOUS BUILD ENGINE\n\n\`\`\`text\n${buildStreamLinesRef.current.join("\n")}\n\`\`\``,
      };
      setMessages((prev) => {
        const next = [...prev, newMsg];
        return next.length > 100 ? next.slice(-100) : next;
      });
      return;
    }

    const id = buildStreamMsgIdRef.current;
    setMessages((prev) =>
      prev.map((m) =>
        m.id === id
          ? {
              ...m,
              content: `AUTONOMOUS BUILD ENGINE\n\n\`\`\`text\n${buildStreamLinesRef.current.join("\n")}\n\`\`\``,
            }
          : m
      )
    );
  };

  const postToWpf = (msg: object) => {
    const webview = (window as any).chrome?.webview;
    if (webview) webview.postMessage(JSON.stringify(msg));
  };

  const QUICK_ACTIONS: Record<AgentType, { id: string; label: string; prompt: string }[]> = {
    builder: [
      {
        id: "audit",
        label: "Audit Workspace",
        prompt:
          "Audit this workspace for reliability and UX issues, and SHOW EVERYTHING YOU DO.\n" +
          "While working: narrate what you are doing (thinking/planning/acting) and stream tool outputs (no hiding).\n" +
          "Focus areas: crashes/close behavior, UI freezes, noisy logs, model/provider errors, and brittle async/cancellation.\n" +
          "After the audit, run validation: dotnet build (and dotnet test if tests exist).\n" +
          "Deliver: (1) Summary, (2) Findings with file paths, (3) Fix plan with minimal diffs, (4) Build/test result and first error if failing.\n" +
          "Use targeted search/read operations; avoid recursive listing unless truly required."
      },
      {
        id: "fix-build",
        label: "Fix Build",
        prompt:
          "Fix the build in this repo.\n" +
          "Use these commands (prefer .slnx when present):\n" +
          "- dotnet build AtlasAI.slnx\n" +
          "- dotnet test (if a test project exists)\n" +
          "Do NOT use msbuild unless dotnet build cannot be used.\n" +
          "Iterate until the build succeeds.\n" +
          "When errors appear: summarize the first actionable error, apply the smallest safe fix, then rebuild.\n" +
          "Keep changes minimal and consistent with existing patterns."
      },
      {
        id: "explain-errors",
        label: "Explain Errors",
        prompt:
          "Check for build logs (e.g., build_errors.txt / build_log.txt) and explain the top errors in plain English.\n" +
          "For each error: what it means, likely cause, and the smallest fix.\n" +
          "Do not propose big refactors unless required." 
      },
      {
        id: "stability-pass",
        label: "Stability Pass",
        prompt:
          "Do a stability pass: find common null/async/event-handler pitfalls and places where UI could freeze.\n" +
          "Output: 3–6 concrete fixes with exact file targets.\n" +
          "Prefer small patches; avoid changing UX unless necessary." 
      }
    ],
    designer: [
      {
        id: "design-review",
        label: "Design Review",
        prompt:
          "Do a design review of the current UI (WPF + embedded UI).\n" +
          "Rules: respect the existing design system; do not hard-code new colors/fonts/shadows.\n" +
          "Deliver: (1) 3–6 high-impact polish improvements, (2) exact files/resources to adjust, (3) minimal diffs." 
      },
      {
        id: "theme-compliance",
        label: "Theme Compliance",
        prompt:
          "Scan for hard-coded styling that should use Theme resources instead (XAML brushes, typography, control styles).\n" +
          "Deliver: a short list of offenders with file paths and a minimal patch plan to align them to Theme/*.xaml." 
      }
    ],
    planner: [
      {
        id: "roadmap",
        label: "Feature Roadmap",
        prompt:
          "Create a feature roadmap to make this app feel amazing.\n" +
          "Constraints: keep UX consistent, avoid unnecessary new pages; focus on quality, speed, and reliability.\n" +
          "Deliver: 6–10 items grouped into (Now/Next/Later), each with 1–2 sentences and estimated effort." 
      }
    ]
  };

  const sendChatMessage = (text: string, agentOverride?: AgentType) => {
    const trimmed = String(text ?? "").trim();
    if (!trimmed) return;

    const agentToUse = agentOverride ?? activeAgent;
    const newMessage: Message = {
      id: Date.now().toString(),
      sender: "user",
      content: trimmed,
      timestamp: new Date(),
    };

    setMessages((prev) => [...prev, newMessage]);

    const webview = (window as any).chrome?.webview;
    if (webview) {
      const resolvedModel = selectedModel === "auto" ? null : selectedModel;
      postToWpf({
        type: "chat_message",
        text: trimmed,
        agent: agentToUse,
        provider: selectedProvider,
        ...(resolvedModel ? { model: resolvedModel } : {}),
        phase: workflowPhase,
      });
      lastThinkingActivityRef.current = Date.now();
      setActivityState("thinking");
      return;
    }

    // Dev-mode fallback
    setActivityState("thinking");
    setTimeout(() => {
      setActivityState("idle");
      addMessage(
        agentToUse === "planner"
          ? "PLAN GENERATED · (dev mode stub)"
          : agentToUse === "designer"
            ? "ACKNOWLEDGED · (dev mode stub)"
            : "ACKNOWLEDGED · (dev mode stub)"
      );
    }, 900);
  };

  // ============================================================
  // WPF <-> React BRIDGE
  // ============================================================
  useEffect(() => {
    const webview = (window as any).chrome?.webview;
    if (!webview) return;

    const handler = (event: any) => {
      try {
        const msg = typeof event.data === "string" ? JSON.parse(event.data) : event.data;
        if (!msg || !msg.type) return;

        // === AGENT HEARTBEAT / PROGRESS (stream into chat/build stream) ===
        if (msg.type === "agent_started") {
          lastThinkingActivityRef.current = Date.now();
          setAgentActive(true);
          setAgentAttempt(Number(msg.attempt ?? 1) || 1);
          setAgentTimeoutMs(typeof msg.timeoutMs === "number" ? msg.timeoutMs : null);
          setAgentElapsedMs(0);
          setAgentRemainingMs(typeof msg.timeoutMs === "number" ? msg.timeoutMs : null);
          if (typeof msg.phase === "string") setAgentPhase(msg.phase);
          if (typeof msg.action === "string") setAgentAction(msg.action);

          try {
            const attempt = Number(msg.attempt ?? 1) || 1;
            const timeoutMs = typeof msg.timeoutMs === "number" ? msg.timeoutMs : null;
            const line = `▶ Agent attempt ${attempt}${timeoutMs ? ` (timeout ${Math.round(timeoutMs / 1000)}s)` : ""}`;
            if (line !== lastAgentProgressLineRef.current) {
              lastAgentProgressLineRef.current = line;
              addMessage(line, "atlas", "status");
              pushBuildStreamLine(line);
            }
          } catch {}
        }
        if (msg.type === "agent_tick") {
          lastThinkingActivityRef.current = Date.now();
          setAgentActive(true);
          if (typeof msg.attempt === "number") setAgentAttempt(msg.attempt);
          if (typeof msg.elapsedMs === "number") setAgentElapsedMs(msg.elapsedMs);
          if (typeof msg.remainingMs === "number") setAgentRemainingMs(msg.remainingMs);
          if (typeof msg.phase === "string") setAgentPhase(msg.phase);
          if (typeof msg.action === "string") setAgentAction(msg.action);
        }
        if (msg.type === "agent_progress") {
          lastThinkingActivityRef.current = Date.now();
          setAgentActive(true);
          if (typeof msg.phase === "string") setAgentPhase(msg.phase);
          if (typeof msg.action === "string") setAgentAction(msg.action);

          try {
            const ph = typeof msg.phase === "string" ? msg.phase : "working";
            const act = typeof msg.action === "string" ? msg.action.trim() : "";
            if (act) {
              const line = `[${ph}] ${act}`;
              if (line !== lastAgentProgressLineRef.current) {
                lastAgentProgressLineRef.current = line;
                addMessage(line, "atlas", "status");
                pushBuildStreamLine(line);
              }
            }
          } catch {}
        }

        // === FILE SYSTEM ===
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
          const root = mapNode(msg.root);
          setFileTree([root]);
          try { setBlockLabels(inferBlockLabelsFromTree(root)); } catch {}

          // If files were deleted/renamed on disk, prune tabs that no longer exist.
          try {
            const ids = new Set<string>();
            const walk = (n: FileNode) => {
              if (n.type === "file") ids.add(n.id);
              if (n.children) n.children.forEach(walk);
            };
            walk(root);
            setOpenTabs((prev) => {
              const kept = prev.filter((t) => ids.has(t.id));
              if (activeTabId && !ids.has(activeTabId)) {
                setActiveTabId(kept.length ? kept[kept.length - 1].id : null);
              }
              return kept;
            });
          } catch {}
        }
        if (msg.type === "file_content" && msg.path && msg.content != null) {
          // Update file tree with content
          const updateContent = (nodes: FileNode[]): FileNode[] =>
            nodes.map((node) => {
              if (node.id === msg.path) return { ...node, content: msg.content };
              if (node.children) return { ...node, children: updateContent(node.children) };
              return node;
            });
          setFileTree((prev) => updateContent(prev));

          // Open as tab
          const tab: EditorTab = {
            id: msg.path,
            name: msg.path.split(/[\\/]/).pop() || msg.path,
            content: msg.content,
            isDirty: false,
            language: msg.path.split(".").pop() || "text",
          };
          setOpenTabs((prev) => {
            const existing = prev.find((t) => t.id === tab.id);
            if (existing) return prev.map((t) => (t.id === tab.id ? { ...t, content: msg.content } : t));
            return [...prev, tab];
          });
          setActiveTabId(msg.path);
        }

        // === FOLDER OPENED ===
        if (msg.type === "folder_opened" && msg.root) {
          const mapNode = (n: any): FileNode => ({
            id: n.id || n.name,
            name: n.name,
            type: n.type === "folder" ? "folder" : "file",
            extension: n.extension,
            content: n.content || undefined,
            isOpen: n.isOpen ?? false,
            children: n.children?.map(mapNode),
          });
          const root = mapNode(msg.root);
          setFileTree([root]);
          try { setBlockLabels(inferBlockLabelsFromTree(root)); } catch {}
          addMessage(`Opened folder: ${msg.root.name}`, "atlas", "status");

          try {
            const ids = new Set<string>();
            const walk = (n: FileNode) => {
              if (n.type === "file") ids.add(n.id);
              if (n.children) n.children.forEach(walk);
            };
            walk(root);
            setOpenTabs((prev) => {
              const kept = prev.filter((t) => ids.has(t.id));
              if (activeTabId && !ids.has(activeTabId)) {
                setActiveTabId(kept.length ? kept[kept.length - 1].id : null);
              }
              return kept;
            });
          } catch {}
        }

        // === ACTIVITY STATE TRANSITIONS ===
        if (msg.type === "chat_status" && msg.text) {
          const t = String(msg.text || "");
          const isFsAck = /^(Saved:|Created file:|Created folder:|Deleted:)/i.test(t.trim());
          if (!isFsAck) {
            lastThinkingActivityRef.current = Date.now(); // reset the inactivity clock
            setActivityState((prev) => (prev === "building" ? prev : "thinking"));
          }
          addMessage(msg.text, "atlas", "status");
        }
        if (msg.type === "tool_start" && msg.tool) {
          // Tool execution during audits/actions should not look like a dotnet build.
          setActivityState("thinking");
          setCurrentTool(msg.tool);
          addMessage(`▶ ${msg.tool}`, "atlas", "status");
          pushBuildStreamLine(`▶ ${msg.tool}`);
          setComponents((prev) => {
            if (prev.find((c) => c.name === msg.tool)) {
              return prev.map((c) => (c.name === msg.tool ? { ...c, status: "building" as const } : c));
            }
            const types: Array<"view" | "logic" | "data" | "api"> = ["logic", "data", "view", "api"];
            return [
              ...prev.slice(-5),
              {
                id: Date.now().toString(),
                name: msg.tool,
                type: types[prev.length % 4],
                status: "building" as const,
                progress: 50,
              },
            ];
          });
        }
        if (msg.type === "build_started") {
          const kind = msg.kind === "agent" || msg.kind === "dotnet" ? msg.kind : "dotnet";
          if (completeTimerRef.current) clearTimeout(completeTimerRef.current);
          // Only dotnet build should set the UI into the "building" state.
          setActivityState(kind === "dotnet" ? "building" : "thinking");
          if (kind === "dotnet") {
            setDotnetBusy(true);
            setWorkflowPhase("build");
          } else {
            setDotnetBusy(false);
          }
          setLastBuildKind(kind);
          setBuildProgress(0);
          setComponents([]);
          resetBuildStream();
          const label = kind === "dotnet" ? "Build started…" : "Action started…";
          addMessage(label, "atlas", "status");
          pushBuildStreamLine(label);
        }
        if (msg.type === "build_log" && msg.line) {
          setBuildProgress((prev) => Math.min(prev + 1.5, 95));
          pushBuildStreamLine(msg.line);
        }
        if (msg.type === "build_complete") {
          const kind = msg.kind === "agent" || msg.kind === "dotnet" ? msg.kind : "dotnet";
          if (kind === "dotnet") setDotnetBusy(false);
          setBuildProgress(100);
          setActivityState(msg.success ? "complete" : "error");
          setCurrentTool(null);
          setComponents((prev) => prev.map((c) => ({ ...c, status: "complete" as const, progress: 100 })));
          const label =
            kind === "dotnet"
              ? (msg.success ? "BUILD COMPLETE · SUCCESS" : "BUILD FAILED")
              : (msg.success ? "ACTION COMPLETE" : "ACTION FAILED");
          addMessage(label, "atlas", msg.success ? "status" : "error");
          const lineLabel =
            kind === "dotnet"
              ? (msg.success ? "✓ Build succeeded" : "✗ Build failed")
              : (msg.success ? "✓ Action complete" : "✗ Action failed");
          pushBuildStreamLine(lineLabel);
          if (kind === "dotnet" && msg.success) setWorkflowPhase("test");
          if (completeTimerRef.current) clearTimeout(completeTimerRef.current);
          completeTimerRef.current = setTimeout(() => {
            setActivityState("idle");
            setBuildProgress(0);
            setComponents([]);
          }, 4000);
        }

        if (msg.type === "test_started") {
          if (completeTimerRef.current) clearTimeout(completeTimerRef.current);
          setDotnetBusy(true);
          setActivityState("building");
          setWorkflowPhase("test");
          setBuildProgress(0);
          setComponents([]);
          resetBuildStream();
          addMessage("Tests started…", "atlas", "status");
          pushBuildStreamLine("Tests started…");
        }
        if (msg.type === "test_log" && msg.line) {
          setBuildProgress((prev) => Math.min(prev + 1.5, 95));
          pushBuildStreamLine(msg.line);
        }
        if (msg.type === "test_complete") {
          setDotnetBusy(false);
          setBuildProgress(100);
          setActivityState(msg.success ? "complete" : "error");
          setCurrentTool(null);
          setComponents((prev) => prev.map((c) => ({ ...c, status: "complete" as const, progress: 100 })));
          addMessage(msg.success ? "TESTS COMPLETE · SUCCESS" : "TESTS FAILED", "atlas", msg.success ? "status" : "error");
          pushBuildStreamLine(msg.success ? "✓ Tests passed" : "✗ Tests failed");
          if (completeTimerRef.current) clearTimeout(completeTimerRef.current);
          completeTimerRef.current = setTimeout(() => {
            setActivityState("idle");
            setBuildProgress(0);
            setComponents([]);
          }, 4000);
        }
        if (msg.type === "chat_response" && msg.text) {
          addMessage(msg.text);
          setAgentActive(false);
          setAgentPhase("complete");
          setAgentAction("");
          // Extract design files when designer agent responds
          const codeBlockMatch = msg.text.match(/```(?:xaml|xml|css|html)?\n([\s\S]*?)```/i);
          if (codeBlockMatch && activeAgent === "designer") {
            setDesignOutput(codeBlockMatch[1].trim());
            // Try to infer file name from the response
            const fnMatch = msg.text.match(/(?:File:|file:|Creating|Writing)\s+[`']?([^\s`']+\.(xaml|css|html|json))/i);
            setDesignFileName(fnMatch ? fnMatch[1] : "Design.xaml");
            setShowDesignPanel(true);
          }
          // Check if response contains a plan
          if (msg.text.includes("PLAN:") || msg.text.includes("Step 1")) {
            setWorkflowPhase("plan");
            // Try to extract plan items
            const lines = msg.text.split("\n").filter((l: string) => /^\d+[\.\)]/.test(l.trim()));
            if (lines.length > 0) {
              setPlanItems(
                lines.map((l: string, i: number) => ({
                  id: `plan-${i}`,
                  title: l.replace(/^\d+[\.\)]\s*/, "").trim(),
                  status: "pending" as const,
                }))
              );
            }
          }
          setActivityState((prev) => {
            if (prev === "building") return prev;
            setTimeout(() => setActivityState("idle"), 3000);
            return "complete";
          });
        }
        if (msg.type === "chat_error" && msg.text) {
          addMessage("ERROR: " + msg.text, "atlas", "error");
          pushBuildStreamLine("ERROR: " + msg.text);
          setActivityState("error");
          setAgentActive(false);
          setAgentPhase("error");
          setAgentAction("");
          // Reset back to idle after a short beat so UI doesn't get stuck.
          setTimeout(() => setActivityState("idle"), 2500);
        }
        if (msg.type === "set_mode" && (msg.mode === "autonomous" || msg.mode === "ide")) {
          setAndPersistMode(msg.mode);
        }

        // === TERMINAL OUTPUT ===
        if (msg.type === "terminal_output" && msg.text) {
          pushBuildStreamLine(msg.text);
        }

        // === MODEL INFO ===
        if (msg.type === "models_info" && msg.provider) {
          const rawProvider = String(msg.provider || "").toLowerCase();
          const isKnownProvider = rawProvider === "claude" || rawProvider === "openai" || rawProvider === "gemini";
          const hostProvider = (isKnownProvider ? rawProvider : "") as AIProvider;
          const hostModel = String(msg.model || "auto");

          // Apply host selection at startup. After startup, still allow host to correct the UI if:
          // - the user hasn't just changed provider/model, and
          // - host provider differs (AI host may fall back to a configured provider).
          const now = Date.now();
          const userRecentlyChanged = (now - (lastUserModelSelectionAtRef.current || 0)) < 1500;
          const currentProvider = selectedProviderRef.current;
          const shouldApplyHostSelection = !hasAppliedHostSelectionRef.current || (!userRecentlyChanged && hostProvider && hostProvider !== currentProvider);

          if (shouldApplyHostSelection) {
            setSelectedProvider(hostProvider);
            setSelectedModel(hostModel || "auto");
            hasAppliedHostSelectionRef.current = true;
          }
          if (msg.models && typeof msg.models === "object") {
            try {
              const next: Partial<Record<AIProvider, ModelOption[]>> = {};
              (Object.keys(msg.models) as AIProvider[]).forEach((k) => {
                const arr = (msg.models as any)[k];
                if (!Array.isArray(arr)) return;
                next[k] = arr
                  .filter((m: any) => m && typeof m.id === "string")
                  .map((m: any) => ({
                    id: String(m.id),
                    name: String(m.name ?? m.id),
                    description: String(m.description ?? ""),
                  }));
              });
              setHostModelsByProvider(next);
            } catch {}
          }
        }

        // === PLAN UPDATE ===
        if (msg.type === "plan_update" && msg.items) {
          setPlanItems(msg.items);
          setWorkflowPhase("plan");

          try {
            const titles = Array.isArray(msg.items) ? msg.items.map((it: any) => String(it?.title ?? "").trim()).filter(Boolean) : [];
            const digest = titles.join("|");
            if (digest && digest !== lastPlanDigestRef.current) {
              lastPlanDigestRef.current = digest;
              addMessage("PLAN:", "atlas", "status");
              titles.slice(0, 12).forEach((t: string, i: number) => {
                const line = `  ${i + 1}. ${t}`;
                addMessage(line, "atlas", "status");
                pushBuildStreamLine(line);
              });
              if (titles.length > 12) {
                const more = `  …and ${titles.length - 12} more`;
                addMessage(more, "atlas", "status");
                pushBuildStreamLine(more);
              }
            }
          } catch {}
        }
      } catch {}
    };

    webview.addEventListener("message", handler);
    webview.postMessage(JSON.stringify({ type: "ready" }));
    webview.postMessage(JSON.stringify({ type: "list_files" }));
    webview.postMessage(JSON.stringify({ type: "get_models" }));

    return () => webview.removeEventListener("message", handler);
  }, []);

  // ============================================================
  // THINKING TIMEOUT — activity-based: 45s without any WPF ping = timeout
  // ============================================================
  useEffect(() => {
    if (activityState === "thinking") {
      lastThinkingActivityRef.current = Date.now();
      thinkingTimerRef.current = setInterval(() => {
        const elapsed = Date.now() - lastThinkingActivityRef.current;
        // Host-side request timeout is typically ~120s; allow headroom.
        const threshold = Math.max(140000, (agentTimeoutMs ?? 0) + 20000);
        if (elapsed > threshold) {
          clearInterval(thinkingTimerRef.current);
          setActivityState("error");
          setAgentActive(false);
          setAgentPhase("error");
          setAgentAction("");
          addMessage("No response from the host for too long. Please try again.", "atlas", "error");
          setTimeout(() => setActivityState("idle"), 3500);
        }
      }, 2000);
      return () => {
        if (thinkingTimerRef.current) clearInterval(thinkingTimerRef.current);
      };
    }
  }, [activityState, agentTimeoutMs]);

  // ============================================================
  // AMBIENT INITIALIZATION
  // ============================================================
  useEffect(() => {
    const blocks: BuildingBlock[] = [];
    const colors = ["#22d3ee", "#f97316", "#a855f7", "#10b981", "#f59e0b"];
    // Spec: 40 building blocks, random 10–40px, 0–3s delay
    for (let i = 0; i < 40; i++) {
      const label = blockLabels[Math.floor(Math.random() * Math.max(1, blockLabels.length))] || "";
      blocks.push({
        id: i,
        // Use a 500x500 virtual canvas (spec) and map to % when rendering
        x: Math.random() * 500,
        y: Math.random() * 500,
        size: 10 + Math.random() * 30,
        delay: Math.random() * 3,
        color: colors[Math.floor(Math.random() * colors.length)],
        label,
      });
    }
    setBuildingBlocks(blocks);

    const nodes = [];
    for (let i = 0; i < 12; i++) {
      const angle = (i / 12) * Math.PI * 2;
      // Spec: 12 nodes arranged in a circle (radius ~150)
      nodes.push({ id: i, x: 250 + Math.cos(angle) * 150, y: 250 + Math.sin(angle) * 150, active: false });
    }
    setAssemblyNodes(nodes);
  }, [blockLabels]);

  // Assembly node animation — speed varies by activity state
  useEffect(() => {
    if (mode !== "autonomous") return;
    const speed =
      activityState === "building" ? 1500
        : activityState === "thinking" ? 2500
          : activityState === "complete" ? 3000
            : activityState === "error" ? 3500
              : 6000;
    const threshold =
      activityState === "building" ? 0.45
        : activityState === "thinking" ? 0.65
          : activityState === "error" ? 0.78
            : 0.88;
    const interval = setInterval(() => {
      setAssemblyNodes((prev) => prev.map((node) => ({ ...node, active: Math.random() > threshold })));
    }, speed);
    return () => clearInterval(interval);
  }, [mode, activityState]);

  // Auto-scroll chat
  useEffect(() => {
    chatEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages]);

  // Auto-scroll terminal
  useEffect(() => {
    terminalEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [terminalLines]);

  useEffect(() => {
    if (mode === "ide" && activeTabId && textareaRef.current) {
      setTimeout(() => textareaRef.current?.focus(), 100);
    }
  }, [activeTabId, mode]);

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

  // ============================================================
  // HANDLERS
  // ============================================================
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
    const text = input;
    if (!String(text ?? "").trim()) return;
    setInput("");
    sendChatMessage(text);
  };

  const showCodeMicNote = (note: string) => {
    setCodeMicNote(note);
    if (codeMicNoteTimerRef.current) {
      clearTimeout(codeMicNoteTimerRef.current);
    }
    codeMicNoteTimerRef.current = setTimeout(() => {
      setCodeMicNote("");
      codeMicNoteTimerRef.current = null;
    }, 2400);
  };

  const handleCodeMicClick = () => {
    // Code mic is currently not wired to live capture.
    showCodeMicNote("Mic not wired");
  };

  useEffect(() => {
    const onWebMessage = (event: any) => {
      try {
        const raw = event?.data;
        const msg = typeof raw === "string" ? JSON.parse(raw) : raw;
        if (!msg || msg.type !== "code.mic.transcript") {
          return;
        }

        const transcript = (msg.payload?.transcript ?? "").toString().trim();
        if (!transcript) {
          return;
        }

        setInput(transcript);
        showCodeMicNote("Transcript captured");
      } catch {
      }
    };

    const webview = (window as any)?.chrome?.webview;
    if (webview?.addEventListener) {
      webview.addEventListener("message", onWebMessage);
      return () => {
        try {
          webview.removeEventListener("message", onWebMessage);
        } catch {
        }
      };
    }

    window.addEventListener("message", onWebMessage);
    return () => {
      window.removeEventListener("message", onWebMessage);
    };
  }, []);

  useEffect(() => {
    return () => {
      if (codeMicNoteTimerRef.current) {
        clearTimeout(codeMicNoteTimerRef.current);
      }
    };
  }, []);

  const handleTerminalCommand = (command: string) => {
    if (!command.trim()) return;
    addTerminalLine(`$ ${command}`, "command");
    postToWpf({ type: "run_terminal", command });
  };

  const getFileIcon = (extension: string) => {
    switch (extension) {
      case "xaml": return <FileCode className="w-3 h-3 text-cyan-400" />;
      case "cs": return <FileCode className="w-3 h-3 text-green-400" />;
      case "md": return <FileText className="w-3 h-3 text-orange-400" />;
      case "tsx": case "jsx": return <FileCode className="w-3 h-3 text-blue-400" />;
      case "ts": case "js": return <FileCode className="w-3 h-3 text-yellow-400" />;
      case "py": return <FileCode className="w-3 h-3 text-blue-300" />;
      case "rs": return <FileCode className="w-3 h-3 text-orange-300" />;
      case "json": return <FileJson className="w-3 h-3 text-yellow-300" />;
      case "html": return <Globe className="w-3 h-3 text-orange-400" />;
      case "css": case "scss": return <Palette className="w-3 h-3 text-pink-400" />;
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

  // FIX: Request file content from WPF if not already loaded
  const openFile = (node: FileNode) => {
    if (node.type !== "file") return;
    if (node.content) {
      // Content already available — open in tab
      const existing = openTabs.find((tab) => tab.id === node.id);
      if (!existing) {
        setOpenTabs([...openTabs, { id: node.id, name: node.name, content: node.content, isDirty: false, language: node.extension }]);
      }
      setActiveTabId(node.id);
    } else {
      // Request content from WPF
      postToWpf({ type: "open_file", path: node.id });
    }
  };

  const openFolder = () => {
    postToWpf({ type: "open_folder" });
  };

  const createProject = (template: ProjectTemplate) => {
    postToWpf({ type: "new_project", template: template.id, name: template.name, language: template.language });
    setShowNewProject(false);
    addMessage(`Creating ${template.name} project...`, "atlas", "status");
    setActivityState("building");
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
    setOpenTabs(openTabs.map((tab) => (tab.id === activeTabId ? { ...tab, content, isDirty: true } : tab)));
  };

  const saveCurrentFile = () => {
    if (!activeTabId) return;
    const tab = openTabs.find((t) => t.id === activeTabId);
    if (!tab) return;
    // Save to WPF
    postToWpf({ type: "save_file", path: activeTabId, content: tab.content });
    // Update local tree
    const updateFileContent = (nodes: FileNode[]): FileNode[] => {
      return nodes.map((node) => {
        if (node.id === activeTabId) return { ...node, content: tab.content };
        if (node.children) return { ...node, children: updateFileContent(node.children) };
        return node;
      });
    };
    setFileTree(updateFileContent(fileTree));
    setOpenTabs(openTabs.map((t) => (t.id === activeTabId ? { ...t, isDirty: false } : t)));
    addTerminalLine(`Saved: ${tab.name}`, "system");
  };

  const addNewFile = (parentId: string) => {
    const fileName = prompt("Enter file name:");
    if (!fileName) return;
    const extension = fileName.split(".").pop() || "txt";
    // Build the real disk path so WPF can find it: parentId is already a full path like D:\Foo\Bar
    const sep = parentId.includes("\\") ? "\\" : "/";
    const fullPath = parentId + sep + fileName;
    const newFile: FileNode = { id: fullPath, name: fileName, type: "file", extension, content: `// ${fileName}\n` };
    const addToNode = (nodes: FileNode[]): FileNode[] => {
      return nodes.map((node) => {
        if (node.id === parentId && node.type === "folder") return { ...node, children: [...(node.children || []), newFile], isOpen: true };
        if (node.children) return { ...node, children: addToNode(node.children) };
        return node;
      });
    };
    setFileTree(addToNode(fileTree));
    postToWpf({ type: "create_file", path: fullPath, content: `// ${fileName}\n` });
  };

  const addNewFolder = (parentId: string) => {
    const folderName = prompt("Enter folder name:");
    if (!folderName) return;
    const sep = parentId.includes("\\") ? "\\" : "/";
    const fullPath = parentId + sep + folderName;
    const newFolder: FileNode = { id: fullPath, name: folderName, type: "folder", isOpen: false, children: [] };
    const addToNode = (nodes: FileNode[]): FileNode[] => {
      return nodes.map((node) => {
        if (node.id === parentId && node.type === "folder") return { ...node, children: [...(node.children || []), newFolder], isOpen: true };
        if (node.children) return { ...node, children: addToNode(node.children) };
        return node;
      });
    };
    setFileTree(addToNode(fileTree));
    postToWpf({ type: "create_folder", path: fullPath });
  };

  const deleteNode = (nodeId: string, nodeName: string) => {
    if (!confirm(`Delete ${nodeName}?`)) return;
    const removeNode = (nodes: FileNode[]): FileNode[] => {
      return nodes.filter((node) => node.id !== nodeId).map((node) => {
        if (node.children) return { ...node, children: removeNode(node.children) };
        return node;
      });
    };
    setFileTree(removeNode(fileTree));
    if (openTabs.find((tab) => tab.id === nodeId)) closeTab(nodeId);
    postToWpf({ type: "delete_file", path: nodeId });
  };

  const renderFileTree = (nodes: FileNode[], depth: number = 0, inIdeMode: boolean = false) => {
    return nodes.map((node) => (
      <div key={node.id}>
        <div
          className={`flex items-center gap-2 px-2 py-1 hover:bg-cyan-500/5 cursor-pointer group ${
            inIdeMode && activeTabId === node.id ? "bg-cyan-500/10" : ""
          }`}
          style={{ paddingLeft: `${depth * 12 + 8}px` }}
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
                {node.id !== "root" && (
                  <button onClick={() => deleteNode(node.id, node.name)} className="p-1 hover:bg-red-500/20 rounded" title="Delete"><Trash2 className="w-3 h-3 text-red-400" /></button>
                )}
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
    { id: "source-control" as ToolsPanel, icon: GitBranch, label: "Source Control", badge: 0 },
    { id: "debug" as ToolsPanel, icon: Bug, label: "Debug" },
    { id: "extensions" as ToolsPanel, icon: Package, label: "Extensions" },
  ];

  // ============================================================
  // MODEL SELECTOR DROPDOWN
  // ============================================================
  const ModelSelectorDropdown = () => {
    if (!showModelSelector) return null;
    if (!modelDropdownPos) return null;
    return (
      createPortal(
        <motion.div
          initial={{ opacity: 0, y: -10, scale: 0.95 }}
          animate={{ opacity: 1, y: 0, scale: 1 }}
          exit={{ opacity: 0, y: -10, scale: 0.95 }}
          className="fixed z-[9999] w-80 bg-[#0f1419] border border-cyan-500/20 rounded-xl shadow-2xl shadow-black/50 overflow-hidden"
          style={{ top: modelDropdownPos.top, left: modelDropdownPos.left }}
        >
        {/* Provider tabs */}
        <div className="flex border-b border-cyan-500/10">
          {(["claude", "openai", "gemini"] as AIProvider[]).map((p) => (
            <button
              key={p}
              onClick={() => {
                lastUserModelSelectionAtRef.current = Date.now();
                setSelectedProvider(p);
                setSelectedModel("auto");
                postToWpf({ type: "set_model", provider: p, model: "auto" });
                // Re-sync immediately so the UI reflects the host's *effective* provider (fallback-safe)
                // and fresh model lists.
                postToWpf({ type: "get_models" });
              }}
              className={`flex-1 px-3 py-2.5 text-[10px] font-mono uppercase tracking-wider transition-all ${
                selectedProvider === p
                  ? p === "claude" ? "bg-orange-500/15 text-orange-400 border-b-2 border-orange-400"
                    : p === "openai" ? "bg-green-500/15 text-green-400 border-b-2 border-green-400"
                    : "bg-blue-500/15 text-blue-400 border-b-2 border-blue-400"
                  : "text-slate-500 hover:text-slate-300 hover:bg-slate-800/50"
              }`}
            >
              {p === "claude" ? "🟠 Claude" : p === "openai" ? "🟢 GPT" : "🔵 Gemini"}
            </button>
          ))}
        </div>
        {/* Model list */}
        <div
          className="p-2 space-y-1 max-h-64 overflow-y-auto overscroll-contain"
          onWheel={(e) => e.stopPropagation()}
        >
          {getModelOptions(selectedProvider).map((m) => (
            <button
              key={m.id}
              onClick={() => {
                lastUserModelSelectionAtRef.current = Date.now();
                setSelectedModel(m.id);
                setShowModelSelector(false);
                postToWpf({ type: "set_model", provider: selectedProvider, model: m.id });
                // Re-sync immediately to reflect any host-side normalization.
                postToWpf({ type: "get_models" });
              }}
              className={`w-full flex items-center justify-between px-3 py-2 rounded-lg text-left transition-all ${
                selectedModel === m.id
                  ? "bg-cyan-500/15 border border-cyan-500/30"
                  : "hover:bg-slate-800/50 border border-transparent"
              }`}
            >
              <div>
                <div className={`text-xs font-mono ${selectedModel === m.id ? "text-cyan-400" : "text-slate-300"}`}>{m.name}</div>
                <div className="text-[9px] text-slate-500">{m.description}</div>
              </div>
              {selectedModel === m.id && <CheckCircle2 className="w-3.5 h-3.5 text-cyan-400" />}
            </button>
          ))}
        </div>
        <div className="p-2 border-t border-cyan-500/10">
          <div className="text-[8px] font-mono text-slate-600 text-center">
            Model selection is sent with each request
          </div>
        </div>
        </motion.div>,
        document.body
      )
    );
  };

  useLayoutEffect(() => {
    if (!showModelSelector) {
      setModelDropdownPos(null);
      return;
    }

    const updatePos = () => {
      const btn = modelButtonRef.current;
      if (!btn) return;
      const rect = btn.getBoundingClientRect();
      const width = 320;
      const margin = 12;
      const left = Math.round(Math.max(margin, Math.min(rect.right - width, window.innerWidth - width - margin)));
      const top = Math.round(Math.min(window.innerHeight - margin, rect.bottom + 8));
      setModelDropdownPos((prev) => {
        if (prev && Math.abs(prev.top - top) < 0.5 && Math.abs(prev.left - left) < 0.5) return prev;
        return { top, left };
      });
    };

    updatePos();
    window.addEventListener("resize", updatePos);
    return () => {
      window.removeEventListener("resize", updatePos);
    };
  }, [showModelSelector]);

  // ============================================================
  // DESIGN CANVAS — shows design output from Designer agent
  // ============================================================
  const DesignCanvas = () => {
    if (!showDesignPanel || !designOutput) return null;
    const lines = designOutput.split("\n");
    return (
      <motion.div
        initial={{ opacity: 0, x: -20 }}
        animate={{ opacity: 1, x: 0 }}
        exit={{ opacity: 0, x: -20 }}
        className="absolute inset-0 z-10 flex flex-col bg-[#0b0f14]"
      >
        {/* Design header */}
        <div className="h-10 bg-[#0f1419] border-b border-purple-500/20 flex items-center justify-between px-4 shrink-0">
          <div className="flex items-center gap-2">
            <Wand2 className="w-3.5 h-3.5 text-purple-400" />
            <span className="text-[10px] font-mono text-purple-400 uppercase tracking-wider">Design Output</span>
            <span className="text-[9px] font-mono text-slate-500">· {designFileName}</span>
          </div>
          <div className="flex items-center gap-2">
            <button
              onClick={() => {
                // Open in IDE as a tab
                const tab: EditorTab = { id: `design-${Date.now()}`, name: designFileName, content: designOutput, isDirty: true, language: designFileName.split(".").pop() || "xml" };
                setOpenTabs((prev) => [...prev, tab]);
                setActiveTabId(tab.id);
                setAndPersistMode("ide");
              }}
              className="flex items-center gap-1 px-2.5 py-1 bg-purple-500/10 border border-purple-500/30 rounded-lg text-[9px] font-mono text-purple-400 hover:bg-purple-500/20 transition-all"
            >
              <Code2 className="w-3 h-3" />Open in IDE
            </button>
            <button
              onClick={() => {
                // Send design to builder agent
                setActiveAgent("builder");
                const prompt = `Take this design and build it as a fully functional WPF application. Here is the XAML design:\n\n\`\`\`xaml\n${designOutput}\n\`\`\`\n\nCreate all necessary files, ViewModels, code-behind, and make it immediately runnable.`;
                setInput(prompt);
              }}
              className="flex items-center gap-1 px-2.5 py-1 bg-cyan-500/10 border border-cyan-500/30 rounded-lg text-[9px] font-mono text-cyan-400 hover:bg-cyan-500/20 transition-all"
            >
              <Boxes className="w-3 h-3" />Build This
            </button>
            <button
              onClick={() => setShowDesignPanel(false)}
              className="p-1.5 hover:bg-slate-700/50 rounded-lg transition-all"
            >
              <X className="w-3 h-3 text-slate-500" />
            </button>
          </div>
        </div>

        {/* Design preview area */}
        <div className="flex-1 flex overflow-hidden min-h-0">
          {/* Code view */}
          <div className="flex-1 overflow-y-auto p-4 font-mono text-[11px] scrollbar-hide">
            <div className="bg-[#0f1419] border border-purple-500/10 rounded-xl p-4 space-y-0.5">
              {lines.map((line, i) => {
                const isTag = line.trim().startsWith("<") || line.trim().startsWith("</") || line.trim().startsWith("/>");
                const isAttr = /^\s+\w[\w:\.]*=/.test(line) || (!isTag && line.includes("="));
                const isComment = line.trim().startsWith("<!--");
                return (
                  <div key={i} className={
                    isComment ? "text-slate-600" :
                    isTag ? "text-purple-300" :
                    isAttr ? "text-cyan-300" : "text-slate-300"
                  }>
                    <span className="text-slate-700 mr-3 select-none text-[9px]">{String(i + 1).padStart(3, " ")}</span>
                    {line}
                  </div>
                );
              })}
            </div>
          </div>

          {/* Visual Mockup panel */}
          <div className="w-72 border-l border-purple-500/10 flex flex-col bg-[#0a0d11]">
            <div className="p-3 border-b border-purple-500/10">
              <span className="text-[9px] font-mono text-purple-400/70 uppercase tracking-wider">Visual Mockup</span>
            </div>
            <div className="flex-1 flex flex-col items-center justify-center p-4 gap-3">
              {/* Rough visual representation */}
              <div className="w-full bg-[#0f1419] border border-purple-500/20 rounded-lg overflow-hidden" style={{ aspectRatio: "16/10" }}>
                <div className="h-6 bg-purple-500/10 border-b border-purple-500/10 flex items-center px-2 gap-1">
                  {[0, 1, 2].map((i) => (
                    <div key={i} className={`w-2 h-2 rounded-full ${i === 0 ? "bg-red-500/50" : i === 1 ? "bg-yellow-500/50" : "bg-green-500/50"}`} />
                  ))}
                  <div className="flex-1 text-center text-[7px] font-mono text-slate-600">
                    {designFileName.replace(".xaml", "").replace(".html", "")}
                  </div>
                </div>
                <div className="p-2 space-y-1.5">
                  <div className="h-2 bg-purple-500/10 rounded w-3/4" />
                  <div className="h-1.5 bg-slate-700/30 rounded w-full" />
                  <div className="h-1.5 bg-slate-700/30 rounded w-5/6" />
                  <div className="flex gap-1 mt-2">
                    <div className="flex-1 h-8 bg-purple-500/5 border border-purple-500/10 rounded" />
                    <div className="flex-1 h-8 bg-purple-500/5 border border-purple-500/10 rounded" />
                  </div>
                  <div className="h-6 bg-purple-500/15 rounded w-24 mx-auto mt-1" />
                </div>
              </div>
              <div className="text-[8px] text-slate-600 font-mono text-center">Schematic preview only</div>
            </div>
          </div>
        </div>
      </motion.div>
    );
  };

  // ============================================================
  // NEW PROJECT DIALOG
  // ============================================================
  const NewProjectDialog = () => {
    if (!showNewProject) return null;
    return (
      <motion.div
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        exit={{ opacity: 0 }}
        className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm"
        onClick={() => setShowNewProject(false)}
      >
        <motion.div
          initial={{ scale: 0.9, y: 20 }}
          animate={{ scale: 1, y: 0 }}
          exit={{ scale: 0.9, y: 20 }}
          className="w-[700px] max-h-[80vh] bg-[#0f1419] border border-cyan-500/20 rounded-2xl shadow-2xl overflow-hidden"
          onClick={(e) => e.stopPropagation()}
        >
          <div className="p-5 border-b border-cyan-500/10">
            <div className="flex items-center justify-between">
              <div>
                <h2 className="text-lg font-mono text-cyan-400">New Project</h2>
                <p className="text-xs text-slate-500 mt-1">Choose a template to get started</p>
              </div>
              <button onClick={() => setShowNewProject(false)} className="p-2 hover:bg-slate-700/50 rounded-lg">
                <X className="w-4 h-4 text-slate-500" />
              </button>
            </div>
          </div>
          <div className="p-5 grid grid-cols-3 gap-3 max-h-[60vh] overflow-y-auto">
            {PROJECT_TEMPLATES.map((t) => (
              <motion.button
                key={t.id}
                onClick={() => createProject(t)}
                className="p-4 bg-[#0b0f14] border border-cyan-500/10 rounded-xl text-left hover:border-cyan-500/30 transition-all group"
                whileHover={{ scale: 1.02, y: -2 }}
                whileTap={{ scale: 0.98 }}
              >
                <div className="text-2xl mb-2">{t.icon}</div>
                <div className="text-sm font-mono text-slate-200 group-hover:text-cyan-400 transition-colors">{t.name}</div>
                <div className="text-[10px] text-slate-500 mt-1">{t.description}</div>
                <div className="mt-2 flex items-center gap-1">
                  <div className="w-1.5 h-1.5 rounded-full" style={{ backgroundColor: t.color }} />
                  <span className="text-[9px] font-mono text-slate-600">{t.language}</span>
                </div>
              </motion.button>
            ))}
          </div>
        </motion.div>
      </motion.div>
    );
  };

  // ============================================================
  // AUTONOMOUS MODE RENDER
  // ============================================================
  if (mode === "autonomous") {
    const isBuilding = activityState === "building";
    // Keep the visualization panel mounted even when idle so the AI chat panel
    // doesn't jump from left -> right when activity starts.
    const showBuildVizEffective = showBuildViz;

    const orbitalSpeed = activityState === "building" ? 12 : activityState === "thinking" ? 22 : 40;
    const coreScale = activityState === "building" ? [1, 1.18, 1] : activityState === "thinking" ? [1, 1.1, 1] : [1, 1.05, 1];
    const corePulseDuration = activityState === "building" ? 1.5 : activityState === "thinking" ? 2.5 : 4;

    const agentColor =
      activeAgent === "designer"
        ? { primary: "#a855f7", secondary: "#d946ef", glow: "rgba(168, 85, 247, 0.3)" }
        : activeAgent === "planner"
          ? { primary: "#f59e0b", secondary: "#eab308", glow: "rgba(245, 158, 11, 0.3)" }
          : { primary: "#22d3ee", secondary: "#06b6d4", glow: "rgba(34, 211, 238, 0.3)" };

    const stateLabel =
      activityState === "idle"
        ? "READY · AWAITING INSTRUCTIONS"
        : activityState === "thinking"
          ? "PROCESSING…"
          : activityState === "building"
            ? "CONSTRUCTING" + (currentTool ? " · " + currentTool.toUpperCase() : "")
            : activityState === "error"
              ? "ERROR · ACTION REQUIRED"
              : "COMPLETE";

    const providerLabel = selectedProvider === "claude" ? "🟠" : selectedProvider === "openai" ? "🟢" : "🔵";
    const shortModel = selectedModel === "auto"
      ? "Auto"
      : getModelOptions(selectedProvider).find((m) => m.id === selectedModel)?.name || selectedModel;

    const showBuildStack = dotnetBusy;
    const stackLabels = blockLabels.length ? blockLabels : Array.from(DEFAULT_BLOCK_LABELS);

    const getIconForLabel = (raw: string) => {
      const label = String(raw || "").toUpperCase();
      if (label.includes("C#") || label === "DLL" || label === "EXE" || label === "NUGET") return Code2;
      if (label === "TS" || label === "JS" || label === "NODE" || label === "NPM") return FileCode;
      if (label === "JSON") return FileJson;
      if (label === "CSS") return Palette;
      if (label === "MD" || label === "TXT") return FileText;
      if (label === "PY" || label === "PIP") return Terminal;
      if (label === "JAVA" || label === "JAR") return Cpu;
      if (label === "GO") return Orbit;
      if (label === "RS" || label === "CRATE") return Binary;
      if (label === "APK" || label === "ANDROID") return Package;
      if (label === "IOS" || label === "SWIFT") return Rocket;
      return Boxes;
    };

    const BuildStack3D = () => {
      // 2x2 base + 2x2 mid + roof cap. Looks like a small "house" being assembled.
      const placements = [
        { gx: 0, gy: 0, gz: 0 },
        { gx: 1, gy: 0, gz: 0 },
        { gx: 0, gy: 0, gz: 1 },
        { gx: 1, gy: 0, gz: 1 },
        { gx: 0, gy: 1, gz: 0 },
        { gx: 1, gy: 1, gz: 0 },
        { gx: 0, gy: 1, gz: 1 },
        { gx: 1, gy: 1, gz: 1 },
        { gx: 0.5, gy: 2, gz: 0.5 },
      ] as const;

      const maxBlocks = placements.length;
      const count = Math.max(1, Math.min(maxBlocks, Math.floor(buildProgress / (100 / maxBlocks)) + 1));

      const primary = agentColor.primary;
      const secondary = agentColor.secondary;
      const faceBg = primary + "14";
      const edge = primary + "88";

      const cell = 44;
      const cube = 36;
      const lift = 34;

      return (
        <div className="absolute inset-0 flex items-center justify-center pointer-events-none z-30">
          <motion.div
            key={`stack-${lastBuildKind}`}
            className="relative"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            style={{ perspective: 900 }}
          >
            <motion.div
              className="relative"
              animate={{ rotateZ: [0, 1.2, 0, -1.2, 0] }}
              transition={{ duration: 6, repeat: Infinity, ease: "easeInOut" }}
              style={{ transformStyle: "preserve-3d", transform: "rotateX(62deg) rotateZ(45deg)" }}
            >
              {/* Base glow plane */}
              <div
                className="absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 rounded-lg"
                style={{
                  width: cell * 2.4,
                  height: cell * 2.4,
                  background: `radial-gradient(circle, ${primary}22 0%, transparent 68%)`,
                  boxShadow: `0 0 50px ${agentColor.glow}`,
                  transform: `translateZ(-${cube}px)`,
                }}
              />

              <AnimatePresence initial={false}>
                {placements.slice(0, count).map((p, i) => {
                  const x = (p.gx - 0.5) * cell;
                  const z = (p.gz - 0.5) * cell;
                  const y = -p.gy * lift;
                  const label = stackLabels[i % Math.max(1, stackLabels.length)] || (lastBuildKind === "dotnet" ? "C#" : "TS");
                  const Icon = getIconForLabel(label);
                  const tint = i % 2 === 0 ? primary : secondary;

                  return (
                    <motion.div
                      key={`b-${p.gx}-${p.gy}-${p.gz}`}
                      className="absolute left-1/2 top-1/2"
                      style={{ width: cube, height: cube, transformStyle: "preserve-3d" }}
                      initial={{ opacity: 0, x, z, y: y + 120, rotateY: -10, rotateX: 10, scale: 0.92 }}
                      animate={{ opacity: 1, x, z, y, rotateY: 0, rotateX: 0, scale: 1 }}
                      exit={{ opacity: 0, y: y - 60, scale: 0.96 }}
                      transition={{ duration: 0.55, delay: i * 0.12, ease: "easeOut" }}
                    >
                      {/* Front */}
                      <div
                        className="absolute inset-0 rounded-sm"
                        style={{
                          transform: `translateZ(${cube / 2}px)`,
                          backgroundColor: faceBg,
                          border: `1px solid ${edge}`,
                          boxShadow: `0 0 18px ${tint}66`,
                          backdropFilter: "blur(1px)",
                        }}
                      >
                        <div className="w-full h-full flex flex-col items-center justify-center gap-1">
                          <Icon className="w-4 h-4" style={{ color: tint, filter: `drop-shadow(0 0 10px ${tint}88)` }} />
                          <div
                            className="font-mono font-bold tracking-wider"
                            style={{
                              color: tint,
                              fontSize: 10,
                              opacity: 0.95,
                              textShadow: `0 0 10px ${tint}88`,
                            }}
                          >
                            {label}
                          </div>
                        </div>
                      </div>

                      {/* Top */}
                      <div
                        className="absolute inset-0 rounded-sm"
                        style={{
                          transform: `rotateX(90deg) translateZ(${cube / 2}px)`,
                          backgroundColor: tint + "10",
                          border: `1px solid ${tint}55`,
                          boxShadow: `0 0 14px ${tint}44`,
                        }}
                      />

                      {/* Right side */}
                      <div
                        className="absolute inset-0 rounded-sm"
                        style={{
                          transform: `rotateY(90deg) translateZ(${cube / 2}px)`,
                          backgroundColor: tint + "0A",
                          border: `1px solid ${tint}44`,
                          boxShadow: `0 0 14px ${tint}33`,
                        }}
                      />
                    </motion.div>
                  );
                })}
              </AnimatePresence>
            </motion.div>
          </motion.div>
        </div>
      );
    };

    return (
      <MotionConfig reducedMotion="never">
      <div className="w-full h-full flex flex-col overflow-hidden bg-[#0b0f14] flex-1 min-h-0">
        {/* === TOP BAR — matches Figma design === */}
        <div className="h-14 bg-[#0b0d11] border-b border-cyan-500/15 flex items-center justify-between px-4 shrink-0 relative z-40">
          {/* LEFT: toggle chevron + icon + title */}
          <div className="flex items-center gap-3">
            <div
              className="w-9 h-9 rounded-lg flex items-center justify-center shrink-0"
              style={{
                background: `radial-gradient(circle, ${agentColor.primary}20 0%, transparent 70%)`,
                border: `1px solid ${agentColor.primary}40`,
                boxShadow: `0 0 12px ${agentColor.glow}`,
              }}
            >
              {activeAgent === "designer" ? (
                <Wand2 className="w-4 h-4" style={{ color: agentColor.primary }} />
              ) : activeAgent === "planner" ? (
                <Brain className="w-4 h-4" style={{ color: agentColor.primary }} />
              ) : (
                <Orbit className="w-4 h-4" style={{ color: agentColor.primary }} />
              )}
            </div>
            <div>
              <div className="text-sm font-mono text-slate-100 uppercase tracking-wider leading-tight">
                AUTONOMOUS BUILD ENGINE
              </div>
              <div
                className="text-[10px] font-mono uppercase tracking-widest leading-tight flex items-center gap-2"
                style={{
                  color:
                    activityState === "complete" ? "#10b981"
                    : activityState === "building" ? "#f97316"
                    : activityState === "thinking" ? agentColor.primary
                    : "#475569",
                }}
              >
                <span>{stateLabel}</span>
                <AnimatePresence mode="wait" initial={false}>
                  {activityState === "thinking" ? (
                    <motion.div
                      key="thinking"
                      className="flex items-center gap-1"
                      initial={{ opacity: 0 }}
                      animate={{ opacity: 1 }}
                      exit={{ opacity: 0 }}
                    >
                      {[0, 1, 2].map((i) => (
                        <motion.div
                          key={i}
                          className="w-1 h-1 rounded-full bg-cyan-400/60"
                          animate={{ scale: [1, 1.6, 1], opacity: [0.3, 1, 0.3] }}
                          transition={{ duration: 1.2, repeat: Infinity, delay: i * 0.18 }}
                        />
                      ))}
                    </motion.div>
                  ) : activityState === "building" ? (
                    <motion.div
                      key="building"
                      className="flex items-center"
                      initial={{ opacity: 0, scale: 0.9 }}
                      animate={{ opacity: 1, scale: 1 }}
                      exit={{ opacity: 0, scale: 0.9 }}
                    >
                      <motion.div
                        animate={{ rotate: 360 }}
                        transition={{ duration: 1.2, repeat: Infinity, ease: "linear" }}
                      >
                        <Activity className="w-3 h-3 text-orange-400/80" />
                      </motion.div>
                    </motion.div>
                  ) : activityState === "complete" ? (
                    <motion.div
                      key="complete"
                      className="flex items-center"
                      initial={{ opacity: 0, scale: 0.85 }}
                      animate={{ opacity: 1, scale: [1, 1.12, 1] }}
                      exit={{ opacity: 0, scale: 0.85 }}
                      transition={{ duration: 0.8, repeat: Infinity, repeatDelay: 0.6 }}
                    >
                      <CheckCircle2 className="w-3 h-3 text-green-400/90" />
                    </motion.div>
                  ) : activityState === "error" ? (
                    <motion.div
                      key="error"
                      className="flex items-center"
                      initial={{ opacity: 0, scale: 0.9 }}
                      animate={{ opacity: 1, scale: 1 }}
                      exit={{ opacity: 0, scale: 0.9 }}
                    >
                      <motion.div
                        animate={{ x: [0, -1.5, 1.5, 0] }}
                        transition={{ duration: 0.6, repeat: Infinity }}
                      >
                        <Bug className="w-3 h-3 text-red-400/90" />
                      </motion.div>
                    </motion.div>
                  ) : (
                    <motion.div
                      key="idle"
                      className="flex items-center gap-1"
                      initial={{ opacity: 0 }}
                      animate={{ opacity: 1 }}
                      exit={{ opacity: 0 }}
                    >
                      {[0, 1, 2].map((i) => (
                        <motion.div
                          key={i}
                          className="w-1 h-1 rounded-full bg-slate-500/40"
                          animate={{ opacity: [0.15, 0.55, 0.15] }}
                          transition={{ duration: 2.4, repeat: Infinity, delay: i * 0.35 }}
                        />
                      ))}
                    </motion.div>
                  )}
                </AnimatePresence>
              </div>
            </div>
          </div>

          {/* RIGHT: agent pills + model + IDE + close */}
          <div className="flex items-center gap-2">
            {/* Agent Selector */}
            <div className="flex items-center bg-slate-900/60 border border-cyan-500/15 rounded-lg overflow-hidden">
              {(["builder", "designer", "planner"] as AgentType[]).map((agent) => (
                <button
                  key={agent}
                  onClick={() => setActiveAgent(agent)}
                  className={`flex items-center gap-1 px-2.5 py-1 text-[9px] font-mono uppercase tracking-wider transition-all ${
                    activeAgent === agent
                      ? agent === "designer" ? "bg-purple-500/20 text-purple-400"
                        : agent === "planner" ? "bg-amber-500/20 text-amber-400"
                        : "bg-cyan-500/20 text-cyan-400"
                      : "bg-transparent text-slate-600 hover:text-slate-300"
                  }`}
                >
                  {agent === "builder" ? <Boxes className="w-3 h-3" /> : agent === "designer" ? <Wand2 className="w-3 h-3" /> : <Brain className="w-3 h-3" />}
                  {agent.charAt(0).toUpperCase() + agent.slice(1)}
                </button>
              ))}
            </div>

            {/* Model Selector Button */}
            <button
              ref={modelButtonRef}
              onClick={() => setShowModelSelector(!showModelSelector)}
              className="flex items-center gap-1.5 px-2.5 py-1 bg-slate-800/50 hover:bg-slate-700/50 border border-slate-700/30 rounded-lg text-[9px] font-mono transition-all"
            >
              <span>{providerLabel}</span>
              <span className="text-slate-300">{shortModel}</span>
              <ChevronDown className="w-2.5 h-2.5 text-slate-500" />
            </button>

            {/* IDE Mode Button — prominent orange */}
            <button
              onClick={() => setAndPersistMode("ide")}
              className="flex items-center gap-1.5 px-4 py-1.5 bg-orange-500/10 hover:bg-orange-500/25 border border-orange-500/50 hover:border-orange-500/80 rounded-lg text-orange-400 text-[10px] font-mono uppercase tracking-wider transition-all shadow-sm"
              style={{ boxShadow: "0 0 8px rgba(249,115,22,0.15)" }}
            >
              <Code2 className="w-3.5 h-3.5" />
              IDE Mode
            </button>
          </div>

          {/* Model Selector Dropdown */}
          <AnimatePresence>{showModelSelector && <ModelSelectorDropdown />}</AnimatePresence>

          {/* Always-on ambient scanline */}
          <div className="absolute left-0 right-0 bottom-0 h-px pointer-events-none overflow-hidden" style={{ opacity: 0.35 }}>
            <motion.div
              className="h-px w-1/3 bg-cyan-400/30"
              animate={{ x: ["-40%", "140%"] }}
              transition={{ duration: 8, repeat: Infinity, ease: "linear" }}
            />
          </div>
        </div>

        {/* === CONTENT === */}
        <div className="flex-1 flex overflow-hidden min-h-0">
          {/* Sidebar reopen strip — visible when app sidebar is hidden */}
          {!showSidebar && (
            <button
              onClick={onReopenSidebar}
              className="w-8 shrink-0 bg-[#0b0d11] hover:bg-cyan-500/10 border-r border-cyan-500/20 flex items-center justify-center transition-colors group relative z-[5000] pointer-events-auto"
              title="Show sidebar"
            >
              <ChevronRight className="w-4 h-4 text-slate-600 group-hover:text-cyan-400 transition-colors" />
            </button>
          )}

          {/* Main Visualization Area */}
          <AnimatePresence>
            {showBuildVizEffective && (
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

                {/* Design Canvas overlay (designer agent) */}
                <AnimatePresence><DesignCanvas /></AnimatePresence>

                {/* Background Grid */}
                <div className="absolute inset-0 pointer-events-none" style={{ opacity: 0.02 }}>
                  <svg width="100%" height="100%">
                    <defs>
                      <pattern id="build-grid" width="40" height="40" patternUnits="userSpaceOnUse">
                        <path d="M 40 0 L 0 0 0 40" fill="none" stroke="#22d3ee" strokeWidth="0.5" />
                      </pattern>
                    </defs>
                    <rect width="100%" height="100%" fill="url(#build-grid)" />
                  </svg>
                </div>

                {/* === LAYER 1: Ambient Particles === */}
                <div className="absolute inset-0 pointer-events-none overflow-hidden">
                  {Array.from({ length: 15 }).map((_, i) => (
                    <motion.div
                      key={`p-${i}`}
                      className="absolute rounded-full"
                      style={{
                        width: 2, height: 2,
                        backgroundColor: i % 3 === 0 ? agentColor.primary : i % 3 === 1 ? "#f97316" : "#a855f7",
                        left: `${(i * 7.3) % 100}%`, top: `${(i * 11.1) % 100}%`,
                      }}
                      animate={{
                        y: [0, -20 - i * 2, 0], x: [0, i % 2 === 0 ? 10 : -10, 0],
                        opacity: [0.5, 1, 0.5],
                      }}
                      transition={{ duration: 4 + i * 0.3, repeat: Infinity, delay: i * 0.9, ease: "easeInOut" }}
                    />
                  ))}
                </div>

                {/* === LAYER 2: Data Flow Lines — always visible, faster when active === */}
                  <svg className="absolute inset-0 w-full h-full pointer-events-none" style={{ opacity: 0.4 }}>
                    {Array.from({ length: 5 }).map((_, i) => (
                      <motion.line
                        key={`flow-${i}`}
                        x1={`${10 + i * 18}%`} y1="0%" x2={`${15 + i * 16}%`} y2="100%"
                        stroke={agentColor.primary} strokeWidth="0.5" strokeDasharray="3 10"
                        animate={{ strokeDashoffset: [0, -100], opacity: [0, 0.6, 0] }}
                        transition={{
                          strokeDashoffset: { duration: 3, repeat: Infinity, ease: "linear" },
                          opacity: { duration: 2, repeat: Infinity, delay: i * 0.6 },
                        }}
                      />
                    ))}
                  </svg>

                {/* === LAYER 3: Circuit Pattern (building) === */}
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
                      <motion.rect width="100%" height="100%" fill="url(#circuit)" initial={{ opacity: 0 }} animate={{ opacity: [0, 0.4, 0] }} transition={{ duration: 6, repeat: Infinity }} />
                    </svg>
                  </div>
                )}

                {/* === LAYER 4: Hex Grid (building) === */}
                {activityState === "building" && (
                  <div className="absolute inset-0 pointer-events-none" style={{ opacity: 0.04 }}>
                    <svg width="100%" height="100%">
                      <defs>
                        <pattern id="hex" width="56" height="100" patternUnits="userSpaceOnUse">
                          <path d="M28,2 L52,18 L52,50 L28,66 L4,50 L4,18 Z" fill="none" stroke={agentColor.secondary} strokeWidth="0.5" />
                          <path d="M28,34 L52,50 L52,82 L28,98 L4,82 L4,50 Z" fill="none" stroke={agentColor.secondary} strokeWidth="0.5" />
                        </pattern>
                      </defs>
                      <motion.rect width="100%" height="100%" fill="url(#hex)" animate={{ opacity: [0.2, 0.5, 0.2] }} transition={{ duration: 4, repeat: Infinity }} />
                    </svg>
                  </div>
                )}

                {/* === CENTRAL VISUALIZATION === */}
                <div className="flex-1 flex items-center justify-center p-8 relative overflow-hidden">
                  {/* Neural Network Background */}
                  <svg
                    className="absolute inset-0 w-full h-full pointer-events-none"
                    style={{ opacity: 0.7 }}
                  >
                    {assemblyNodes.map((node, i) =>
                      assemblyNodes.slice(i + 1).filter((_, j) => j < 2).map((target, j) => (
                        <motion.line
                          key={`${i}-${j}`}
                          x1={`${(node.x / 500) * 100}%`} y1={`${(node.y / 500) * 100}%`}
                          x2={`${(target.x / 500) * 100}%`} y2={`${(target.y / 500) * 100}%`}
                          stroke={node.active || target.active ? agentColor.primary : "#1e293b"} strokeWidth="0.5"
                          animate={{ opacity: node.active || target.active ? [0, 0.5, 0] : [0, 0.05, 0] }}
                          transition={{ duration: 4, repeat: Infinity, delay: i * 0.8 }}
                        />
                      ))
                    )}
                    {assemblyNodes.map((node) => (
                      <motion.circle
                        key={node.id} cx={`${(node.x / 500) * 100}%`} cy={`${(node.y / 500) * 100}%`}
                        r={node.active ? 4 : 3} fill={node.active ? agentColor.primary : "#334155"}
                        animate={{ opacity: node.active ? [0.2, 0.7, 0.2] : [0.05, 0.1, 0.05] }}
                        transition={{ duration: 3 }}
                      />
                    ))}
                  </svg>

                  {/* 3D stacking build animation — only while dotnet build/test is running */}
                  <AnimatePresence>{showBuildStack && <BuildStack3D />}</AnimatePresence>

                  {/* Central Orbital System */}
                  <div className="relative z-10" style={{ opacity: showBuildStack ? 0.18 : 1 }}>
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
                            border: `2px solid ${ring === 0 ? "rgba(34,211,238,0.25)" : ring === 1 ? "rgba(249,115,22,0.25)" : "rgba(168,85,247,0.25)"}`,
                            width: `${ring === 0 ? 100 : ring === 1 ? 85 : 70}%`,
                            height: `${ring === 0 ? 100 : ring === 1 ? 85 : 70}%`,
                            top: `${ring === 0 ? 0 : ring === 1 ? 7.5 : 15}%`,
                            left: `${ring === 0 ? 0 : ring === 1 ? 7.5 : 15}%`,
                          }}
                          animate={{ rotate: ring === 1 ? 360 : -360, scale: [1, 1.02, 1] }}
                          transition={{
                            rotate: { duration: ring === 0 ? 20 : ring === 1 ? 25 : 30, repeat: Infinity, ease: "linear" },
                            scale: { duration: 3, repeat: Infinity, ease: "easeInOut" },
                          }}
                        />
                      ))}

                      {/* Building Blocks — visible in all states, more vivid when active */}
                      {buildingBlocks.map((block) => (
                          <motion.div
                            key={block.id}
                            className="absolute rounded-sm pointer-events-none"
                            style={{
                              width: block.size, height: block.size,
                              left: `${(block.x / 500) * 100}%`, top: `${(block.y / 500) * 100}%`,
                              backgroundColor: block.color + "66",
                              border: `1px solid ${block.color}`,
                              boxShadow: `0 0 10px ${block.color}99`,
                            }}
                            animate={{
                              opacity: [0, 1, 0],
                              scale: [0, 1, 1],
                              rotate: [0, 360],
                            }}
                            transition={{ duration: 4, repeat: Infinity, delay: block.delay, ease: "easeInOut" }}
                          >
                            {block.size >= 18 && !!block.label && (
                              <div
                                className="w-full h-full flex items-center justify-center font-mono font-bold tracking-wider"
                                style={{
                                  color: block.color,
                                  fontSize: Math.max(7, Math.min(10, block.size * 0.34)),
                                  opacity: 0.9,
                                  textShadow: `0 0 8px ${block.color}99`,
                                }}
                              >
                                {block.label}
                              </div>
                            )}
                          </motion.div>
                        ))}

                      {/* Center Core */}
                      <motion.div
                        className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2"
                        animate={{ scale: coreScale }}
                        transition={{ duration: corePulseDuration, repeat: Infinity, ease: "easeInOut" }}
                      >
                        <div
                          className="w-20 h-20 md:w-24 md:h-24 rounded-full backdrop-blur-md flex items-center justify-center"
                          style={{
                            background: `radial-gradient(circle, ${agentColor.primary}20 0%, transparent 65%)`,
                            border: `1.5px solid ${agentColor.primary}55`,
                            boxShadow: `0 0 40px ${agentColor.glow}`,
                          }}
                        >
                          <motion.div animate={{ rotate: -360 }} transition={{ duration: orbitalSpeed * 0.5, repeat: Infinity, ease: "linear" }}>
                            {activeAgent === "designer" ? (
                              <Wand2 className="w-8 h-8 md:w-10 md:h-10" style={{ color: agentColor.primary }} />
                            ) : activeAgent === "planner" ? (
                              <Brain className="w-8 h-8 md:w-10 md:h-10" style={{ color: agentColor.primary }} />
                            ) : (
                              <Boxes className="w-8 h-8 md:w-10 md:h-10" style={{ color: agentColor.primary }} />
                            )}
                          </motion.div>
                        </div>
                      </motion.div>

                      {/* Orbiting Tool Components (real data) */}
                      {components.length > 0 &&
                        components.slice(-4).map((comp, i) => {
                          const total = Math.min(components.length, 4);
                          const angle = (i / total) * Math.PI * 2;
                          const radius = 38;
                          return (
                            <motion.div key={comp.id} className="absolute" style={{ left: `${50 + Math.cos(angle) * radius}%`, top: `${50 + Math.sin(angle) * radius}%`, transform: "translate(-50%, -50%)" }}>
                              <motion.div
                                className={`w-8 h-8 md:w-10 md:h-10 rounded-lg border flex items-center justify-center backdrop-blur-sm ${
                                  comp.status === "complete" ? "bg-green-500/10 border-green-400/40" : comp.status === "building" ? "bg-orange-500/10 border-orange-400/40" : "bg-slate-800/10 border-slate-600/20"
                                }`}
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

                  {/* === Wave Form === */}
                  <div className="absolute bottom-0 left-0 right-0 h-12 pointer-events-none overflow-hidden">
                    <svg width="100%" height="100%" viewBox="0 0 1200 48" preserveAspectRatio="none">
                      <motion.path
                        fill="none" stroke={agentColor.primary} strokeWidth="0.8"
                        animate={{
                          d: [
                            "M0 24 Q 150 8, 300 24 T 600 24 T 900 24 T 1200 24",
                            "M0 24 Q 150 40, 300 24 T 600 24 T 900 24 T 1200 24",
                          ],
                          opacity: 0.5,
                        }}
                        transition={{ duration: 1.8, repeat: Infinity, repeatType: "reverse", ease: "easeInOut" }}
                      />
                    </svg>
                  </div>

                  {/* Activity Indicator */}
                  <AnimatePresence>
                    <motion.div
                      initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} exit={{ opacity: 0, y: -20 }}
                      transition={{ duration: 0.5 }}
                      className="absolute bottom-16 left-1/2 -translate-x-1/2"
                    >
                      <div className="flex items-center gap-3 bg-[#0f1419]/90 backdrop-blur-md rounded-full px-4 py-2" style={{ border: `1px solid ${agentColor.primary + "25"}` }}>
                        <motion.div animate={{ rotate: 360 }} transition={{ duration: 3, repeat: Infinity, ease: "linear" }}>
                          <Cpu className="w-3.5 h-3.5" style={{ color: agentColor.primary }} />
                        </motion.div>
                        <span className="text-[10px] font-mono" style={{ color: agentColor.primary }}>
                          {activeAgent === "designer" ? "DESIGNING…" : "BUILDING…"}
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
                      </div>
                    </motion.div>
                  </AnimatePresence>
                </div>

                {/* === PROGRESS STRIP — always visible, matches Figma === */}
                <div className="bg-[#0f1419]/90 border-t border-cyan-500/10 shrink-0 px-5 py-3">
                  <div className="flex items-center justify-between mb-2">
                    <div className="flex items-center gap-2">
                      <Activity
                        className="w-3.5 h-3.5"
                        style={{ color: agentColor.primary }}
                      />
                      <div>
                        <div className="text-[9px] font-mono text-slate-500 uppercase tracking-wider">BUILD PROGRESS</div>
                        <div
                          className="text-sm font-mono font-bold leading-tight"
                          style={{ color: agentColor.primary }}
                        >
                          {`${buildProgress.toFixed(1)}%`}
                        </div>
                      </div>
                    </div>
                    <div className="text-right">
                      <div
                        className="text-[10px] font-mono uppercase tracking-wider"
                        style={{ color: "#f97316" }}
                      >
                        {currentTool ? currentTool.toUpperCase() : "SYNTHESIZING"}
                      </div>
                      {components.length > 0 && (
                        <div className="flex items-center gap-2 mt-0.5 justify-end">
                          {components.slice(-4).map((comp) => (
                            <div key={comp.id} className="flex items-center gap-1">
                              <div className={`w-1 h-1 rounded-full ${comp.status === "complete" ? "bg-green-400" : comp.status === "building" ? "bg-orange-400 animate-pulse" : "bg-slate-700"}`} />
                              <span className="text-[8px] font-mono text-slate-600">{comp.name}</span>
                            </div>
                          ))}
                        </div>
                      )}
                    </div>
                  </div>
                  {/* Progress bar track */}
                  <div className="h-2 bg-slate-900/80 rounded-full overflow-hidden border border-cyan-500/10">
                    <motion.div
                      className="h-full rounded-full"
                      style={{
                        width: `${buildProgress}%`,
                        background: `linear-gradient(90deg, ${agentColor.primary}, #f97316)`,
                      }}
                      transition={{ duration: 0.4 }}
                    />
                  </div>
                </div>

                {/* === TERMINAL PANEL (collapsible) === */}
                <AnimatePresence>
                  {showTerminalPanel && (
                    <motion.div
                      initial={{ height: 0 }} animate={{ height: 150 }} exit={{ height: 0 }}
                      className="bg-[#0a0e12] border-t border-cyan-500/10 shrink-0 overflow-hidden flex flex-col"
                    >
                      <div className="h-7 bg-[#0f1419] border-b border-cyan-500/10 flex items-center justify-between px-3 shrink-0">
                        <div className="flex items-center gap-2">
                          <Terminal className="w-3 h-3 text-cyan-400" />
                          <span className="text-[9px] font-mono text-cyan-400 uppercase">Terminal</span>
                          <span className="text-[8px] font-mono text-slate-600">{terminalLines.length} lines</span>
                        </div>
                        <div className="flex items-center gap-1">
                          <button onClick={() => setTerminalLines([{ id: "clear", text: "Terminal cleared", type: "system" }])} className="p-1 hover:bg-slate-700/50 rounded" title="Clear">
                            <RotateCcw className="w-2.5 h-2.5 text-slate-500" />
                          </button>
                          <button onClick={() => setShowTerminalPanel(false)} className="p-1 hover:bg-slate-700/50 rounded" title="Close">
                            <X className="w-2.5 h-2.5 text-slate-500" />
                          </button>
                        </div>
                      </div>
                      <div className="flex-1 overflow-y-auto p-2 font-mono text-[10px] space-y-0.5 scrollbar-hide">
                        {terminalLines.map((line) => (
                          <div key={line.id} className={
                            line.type === "stderr" ? "text-red-400"
                              : line.type === "system" ? "text-cyan-400/60"
                                : line.type === "command" ? "text-yellow-400"
                                  : "text-slate-400"
                          }>
                            {line.text}
                          </div>
                        ))}
                        <div ref={terminalEndRef} />
                      </div>
                    </motion.div>
                  )}
                </AnimatePresence>
              </motion.div>
            )}
          </AnimatePresence>

          {/* Restore panels when viz closed */}
          {isBuilding && !showBuildViz && (
            <div className="flex-1 flex items-center justify-center bg-[#0b0f14]">
              <motion.button
                onClick={() => { setShowBuildViz(true); setShowProgress(true); }}
                className="flex items-center gap-3 px-6 py-3 border border-cyan-500/30 rounded-lg text-cyan-400 text-sm font-mono hover:bg-cyan-500/10 transition-all"
                whileHover={{ scale: 1.05 }} whileTap={{ scale: 0.95 }}
              >
                <Orbit className="w-5 h-5" />
                RESTORE BUILD VISUALIZATION
              </motion.button>
            </div>
          )}

          {/* === RIGHT PANEL — AI Chat === */}
          {!showRightPanel && (
            <div className="w-8 bg-[#0b0f14]/60 border-l border-cyan-500/10 flex items-center justify-center cursor-pointer hover:bg-cyan-500/5 transition-all shrink-0" onClick={() => setShowRightPanel(true)} title="Show AI Chat">
              <ChevronLeft className="w-3 h-3 text-slate-600" />
            </div>
          )}
          {showRightPanel && (
          <div className="w-80 flex flex-col bg-[#0b0f14]/60 border-l border-cyan-500/10 backdrop-blur-sm shrink-0 relative z-40 pointer-events-auto">
            {/* Chat Header — matches Figma AI ASSISTANT style */}
            <div className="px-4 py-3 border-b border-cyan-500/10 shrink-0">
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-2.5">
                  <Sparkles className="w-4 h-4 text-cyan-400" />
                  <div>
                    <h3 className="text-xs font-mono text-cyan-400 uppercase tracking-widest">AI ASSISTANT</h3>
                    <p className="text-[9px] text-slate-600 font-mono">
                      {activeAgent === "designer" ? "Design mode" : activeAgent === "planner" ? "Planning mode" : "Build mode"} · {shortModel}
                    </p>
                  </div>
                </div>
                <div className="flex items-center gap-1">
                  <button
                    onClick={() => setShowTerminalPanel(!showTerminalPanel)}
                    className={`p-1 rounded transition-all ${showTerminalPanel ? "bg-cyan-500/20 text-cyan-400" : "text-slate-600 hover:text-slate-400"}`}
                    title="Toggle Terminal"
                  >
                    <Terminal className="w-3 h-3" />
                  </button>
                  <button
                    onClick={() => setShowNewProject(true)}
                    className="p-1 rounded text-slate-600 hover:text-cyan-400 transition-all"
                    title="New Project"
                  >
                    <Plus className="w-3 h-3" />
                  </button>
                  <button
                    onClick={openFolder}
                    className="p-1 rounded text-slate-600 hover:text-cyan-400 transition-all"
                    title="Open Folder"
                  >
                    <FolderSearch className="w-3 h-3" />
                  </button>
                  <button
                    onClick={() => setShowRightPanel(false)}
                    className="p-1 rounded text-slate-600 hover:text-red-400 transition-all ml-0.5"
                    title="Close Panel"
                  >
                    <X className="w-3 h-3" />
                  </button>
                </div>
              </div>
            </div>

            {/* Live progress (phase / current action / countdown) */}
            {agentActive && (activityState === "thinking" || activityState === "building") && (
              <div className="px-4 py-2 border-b border-cyan-500/10 shrink-0">
                <div className="flex items-center justify-between gap-3">
                  <div className="min-w-0">
                    <div className="text-[8px] font-mono text-slate-500 uppercase tracking-wider">{agentPhase || "working"}</div>
                    <div className="text-[9px] font-mono text-slate-300 truncate">
                      {agentAction ? agentAction : "Working…"}
                    </div>
                  </div>
                  <div className="shrink-0 text-right">
                    <div className="text-[8px] font-mono text-slate-500 uppercase tracking-wider">Time Left</div>
                    <div className="text-[9px] font-mono text-slate-300">
                      {formatCountdown(agentRemainingMs)}{agentAttempt > 1 ? ` · retry ${agentAttempt}` : ""}
                    </div>
                  </div>
                </div>
                {typeof agentTimeoutMs === "number" && agentTimeoutMs > 0 && (
                  <div className="mt-2 h-1 bg-cyan-500/10 rounded">
                    <div
                      className="h-1 bg-cyan-400/60 rounded"
                      style={{ width: `${Math.min(100, Math.max(0, (agentElapsedMs / agentTimeoutMs) * 100))}%` }}
                    />
                  </div>
                )}
              </div>
            )}

            {/* Plan Items (when in plan phase) */}
            {planItems.length > 0 && (
              <div className="p-2 border-b border-cyan-500/10 shrink-0 max-h-32 overflow-y-auto">
                <div className="text-[8px] font-mono text-amber-400 uppercase tracking-wider mb-1">Plan</div>
                {planItems.map((item) => (
                  <div key={item.id} className="flex items-center gap-2 py-0.5">
                    <div className={`w-1.5 h-1.5 rounded-full ${item.status === "done" ? "bg-green-400" : item.status === "active" ? "bg-orange-400 animate-pulse" : "bg-slate-600"}`} />
                    <span className={`text-[9px] font-mono ${item.status === "done" ? "text-slate-500 line-through" : "text-slate-300"}`}>{item.title}</span>
                  </div>
                ))}
              </div>
            )}

            {/* Quick Actions */}
            <div className="p-2 border-b border-cyan-500/10 shrink-0">
              <div className="flex items-center justify-between mb-1">
                <div className="text-[8px] font-mono text-cyan-400 uppercase tracking-widest">Quick Actions</div>
                <div className="text-[8px] font-mono text-slate-600">{activeAgent.toUpperCase()}</div>
              </div>
              <div className="grid grid-cols-2 gap-1.5">
                {(QUICK_ACTIONS[activeAgent] ?? []).map((a) => (
                  <button
                    key={a.id}
                    onClick={() => sendChatMessage(a.prompt, activeAgent)}
                    disabled={activityState === "thinking" || activityState === "building"}
                    className="px-2 py-1 rounded bg-[#0f1419] border border-cyan-500/15 text-[9px] font-mono text-slate-300 hover:bg-cyan-500/10 hover:text-cyan-300 disabled:opacity-40 transition-all text-left"
                    title={a.label}
                  >
                    {a.label}
                  </button>
                ))}
              </div>
            </div>

            {/* Chat Messages */}
            <div className="flex-1 overflow-y-auto p-2.5 space-y-1.5 scrollbar-hide">
              {messages.map((msg) => (
                <motion.div key={msg.id} initial={{ opacity: 0, y: 8 }} animate={{ opacity: 1, y: 0 }}
                  className={`flex ${msg.sender === "user" ? "justify-end" : "justify-start"}`}
                >
                  <div className={`max-w-[90%] rounded-lg px-2 py-1 ${
                    msg.sender === "atlas"
                      ? msg.type === "error" ? "bg-red-500/10 border border-red-500/20" : "bg-[#0f1419] border border-cyan-500/15"
                      : "bg-slate-800/50 border border-slate-700/40"
                  }`}>
                    <div className="flex items-center gap-1 mb-0.5">
                      <span className={`text-[7px] font-mono uppercase tracking-widest ${
                        msg.sender === "atlas" ? (msg.type === "error" ? "text-red-400" : "text-cyan-400") : "text-orange-400"
                      }`}>
                        {msg.sender === "atlas" ? "Atlas" : "You"}
                      </span>
                      <span className="text-[7px] text-slate-600 font-mono">
                        {msg.timestamp.toLocaleTimeString("en-US", { hour: "2-digit", minute: "2-digit" })}
                      </span>
                    </div>
                    <p className={`text-[10px] leading-relaxed ${
                      msg.sender === "atlas" ? "text-slate-300 font-mono tracking-wide" : "text-slate-300"
                    }`}>
                      {msg.content}
                    </p>
                  </div>
                </motion.div>
              ))}
              <div ref={chatEndRef} />
            </div>

            {/* Chat Input */}
            <div className="p-2 border-t border-cyan-500/10 shrink-0">
              <div className="flex items-center gap-1.5 bg-[#0f1419] border border-cyan-500/15 rounded-lg p-1.5">
                <input
                  type="text"
                  value={input}
                  onChange={(e) => setInput(e.target.value)}
                  onKeyPress={(e) => e.key === "Enter" && handleSend()}
                  placeholder={
                    activeAgent === "planner" ? "Describe what you want to plan..."
                      : activeAgent === "designer" ? "Describe your UI design..."
                        : "Tell Atlas what to build..."
                  }
                  className="flex-1 bg-transparent text-slate-200 text-[10px] outline-none placeholder:text-slate-600 font-mono"
                />
                <button
                  onClick={() => setShowNewProject(true)}
                  className="p-1 rounded text-slate-600 hover:text-cyan-400 transition-all"
                  title="New Project"
                >
                  <Plus className="w-3 h-3" />
                </button>
                <button
                  onClick={handleCodeMicClick}
                  className="p-1 rounded text-slate-600 hover:text-cyan-400 transition-all"
                  title="Code mic"
                >
                  <Mic className="w-3 h-3" />
                </button>
                <button
                  onClick={handleSend}
                  disabled={activityState === "thinking" || activityState === "building"}
                  className="p-1 rounded bg-cyan-500/10 text-cyan-400 hover:bg-cyan-500/20 disabled:opacity-30 transition-all"
                >
                  <Send className="w-3 h-3" />
                </button>
              </div>
              {codeMicNote && <div className="mt-1 text-[9px] font-mono text-slate-500">{codeMicNote}</div>}
              <div className="flex items-center gap-1.5 mt-1.5 px-1">
                {activityState === "thinking" ? (
                  <>
                    {[0, 1, 2].map((i) => (
                      <motion.div
                        key={i}
                        className="w-1 h-1 rounded-full bg-cyan-400/60"
                        animate={{ scale: [1, 1.5, 1], opacity: [0.3, 1, 0.3] }}
                        transition={{ duration: 1.2, repeat: Infinity, delay: i * 0.2 }}
                      />
                    ))}
                    <span className="text-[8px] font-mono text-slate-600">Processing…</span>
                  </>
                ) : activityState === "building" ? (
                  <>
                    <motion.div animate={{ rotate: 360 }} transition={{ duration: 1.1, repeat: Infinity, ease: "linear" }}>
                      <Activity className="w-3 h-3 text-orange-400/80" />
                    </motion.div>
                    <span className="text-[8px] font-mono text-slate-600">Building…</span>
                  </>
                ) : activityState === "complete" ? (
                  <>
                    <motion.div animate={{ scale: [1, 1.15, 1] }} transition={{ duration: 0.9, repeat: Infinity, repeatDelay: 0.7 }}>
                      <CheckCircle2 className="w-3 h-3 text-green-400/90" />
                    </motion.div>
                    <span className="text-[8px] font-mono text-slate-600">Complete</span>
                  </>
                ) : activityState === "error" ? (
                  <>
                    <motion.div animate={{ x: [0, -1.5, 1.5, 0] }} transition={{ duration: 0.6, repeat: Infinity }}>
                      <Bug className="w-3 h-3 text-red-400/90" />
                    </motion.div>
                    <span className="text-[8px] font-mono text-slate-600">Error</span>
                  </>
                ) : (
                  <>
                    {[0, 1, 2].map((i) => (
                      <motion.div
                        key={i}
                        className="w-1 h-1 rounded-full bg-slate-500/40"
                        animate={{ opacity: [0.15, 0.5, 0.15] }}
                        transition={{ duration: 2.4, repeat: Infinity, delay: i * 0.35 }}
                      />
                    ))}
                    <span className="text-[8px] font-mono text-slate-600">Idle</span>
                  </>
                )}
              </div>
            </div>
          </div>
          )}
        </div>

        {/* New Project Dialog */}
        <AnimatePresence><NewProjectDialog /></AnimatePresence>
      </div>
      </MotionConfig>
    );
  }

  // ============================================================
  // IDE MODE RENDER
  // ============================================================
  return (
    <MotionConfig reducedMotion="never">
      <div className="w-full h-full flex flex-col overflow-hidden bg-[#0b0f14] flex-1 min-h-0">

      {/* Top Toolbar */}
      <div className="h-12 bg-[#0f1419] border-b border-cyan-500/10 flex items-center justify-between px-4 relative z-40">
        <div className="flex items-center gap-2">
          <motion.button
            onClick={() => setAndPersistMode("autonomous")}
            className="flex items-center gap-1.5 p-1.5 rounded-lg hover:bg-slate-700/50 border border-transparent hover:border-slate-600/30 transition-all group mr-1"
            title="Back to Autonomous Mode"
            whileHover={{ x: -2 }}
          >
            <ArrowLeft className="w-4 h-4 text-slate-500 group-hover:text-cyan-400 transition-colors" />
          </motion.button>
          <motion.button
            onClick={saveCurrentFile}
            disabled={!activeTab?.isDirty}
            className={`flex items-center gap-2 px-3 py-1.5 border rounded text-xs font-mono uppercase tracking-wider transition-all ${
              activeTab?.isDirty
                ? "bg-green-500/10 hover:bg-green-500/20 border-green-500/30 text-green-400"
                : "bg-slate-800/30 border-slate-700/30 text-slate-600 cursor-not-allowed"
            }`}
            whileHover={activeTab?.isDirty ? { scale: 1.02 } : {}}
            whileTap={activeTab?.isDirty ? { scale: 0.98 } : {}}
          >
            <Save className="w-3 h-3" />
            Save
          </motion.button>
          <button
            onClick={() => postToWpf({ type: "start_build" })}
            className="flex items-center gap-2 px-3 py-1.5 border border-orange-500/30 rounded text-xs font-mono uppercase tracking-wider text-orange-400 hover:bg-orange-500/10 transition-all"
          >
            <Play className="w-3 h-3" />
            Build
          </button>
          <button
            onClick={() => postToWpf({ type: "start_rebuild" })}
            className="flex items-center gap-2 px-3 py-1.5 border border-slate-600/40 rounded text-xs font-mono uppercase tracking-wider text-slate-200 hover:bg-slate-700/30 transition-all"
          >
            <RotateCcw className="w-3 h-3" />
            Rebuild
          </button>
          <button
            onClick={() => postToWpf({ type: "start_tests" })}
            className="flex items-center gap-2 px-3 py-1.5 border border-cyan-500/30 rounded text-xs font-mono uppercase tracking-wider text-cyan-400 hover:bg-cyan-500/10 transition-all"
          >
            <ListChecks className="w-3 h-3" />
            Tests
          </button>
          <button
            onClick={() => postToWpf({ type: "cancel_build" })}
            className="flex items-center gap-2 px-3 py-1.5 border border-red-500/30 rounded text-xs font-mono uppercase tracking-wider text-red-400 hover:bg-red-500/10 transition-all"
            title="Stop build/tests"
          >
            <Square className="w-3 h-3" />
            Stop
          </button>
        </div>
        <div className="flex items-center gap-3">
          {/* Model indicator */}
          <button ref={modelButtonRef} onClick={() => setShowModelSelector(!showModelSelector)} className="flex items-center gap-1 px-2 py-1 rounded-lg hover:bg-slate-700/30 text-[10px] font-mono text-slate-400 transition-all">
            {selectedProvider === "claude" ? "🟠" : selectedProvider === "openai" ? "🟢" : "🔵"}
            <span>{getModelOptions(selectedProvider).find((m) => m.id === selectedModel)?.name || (selectedModel === "auto" ? "Auto" : selectedModel)}</span>
          </button>
          <div className="text-xs font-mono text-cyan-400 uppercase tracking-wider">
            ATLAS AI — IDE MODE
          </div>
          <motion.div animate={{ opacity: [0.5, 1, 0.5] }} transition={{ duration: 2, repeat: Infinity }} className="w-2 h-2 rounded-full bg-cyan-400" />

          {/* Spec: Switch button on the right (no floating fixed button) */}
          <motion.button
            onClick={() => setAndPersistMode("autonomous")}
            className="flex items-center gap-2 px-3 py-1.5 bg-cyan-500/10 hover:bg-cyan-500/20 border border-cyan-500/30 rounded-lg text-cyan-400 text-[10px] font-mono uppercase tracking-wider transition-all"
            whileHover={{ scale: 1.03 }}
            whileTap={{ scale: 0.98 }}
          >
            <Zap className="w-3.5 h-3.5" />
            Switch to Autonomous Mode
          </motion.button>
        </div>
      </div>

      <div className="flex-1 flex overflow-hidden relative">
        {/* Sidebar reopen strip in IDE mode */}
        {!showSidebar && (
          <button
            onClick={onReopenSidebar}
            className="w-8 shrink-0 bg-[#0b0d11] hover:bg-cyan-500/10 border-r border-cyan-500/20 flex items-center justify-center transition-colors group relative z-[5000] pointer-events-auto"
            title="Show sidebar"
          >
            <ChevronRight className="w-4 h-4 text-slate-600 group-hover:text-cyan-400 transition-colors" />
          </button>
        )}

        {/* Model Selector Dropdown for IDE mode */}
        <AnimatePresence>{showModelSelector && <ModelSelectorDropdown />}</AnimatePresence>

        {/* Icon Sidebar */}
        <div className="w-12 bg-[#0f1419] border-r border-cyan-500/10 flex flex-col items-center py-4 gap-2">
          {toolsPanelOptions.map((item) => (
            <motion.button
              key={item.id}
              onClick={() => setActiveToolsPanel(activeToolsPanel === item.id ? null : item.id)}
              className={`relative p-2 rounded-lg transition-all ${
                activeToolsPanel === item.id ? "bg-cyan-500/20 text-cyan-400" : "text-slate-500 hover:text-cyan-400 hover:bg-cyan-500/10"
              }`}
              whileHover={{ scale: 1.05 }} whileTap={{ scale: 0.95 }}
              title={item.label}
            >
              <item.icon className="w-5 h-5" />
              {item.badge ? <div className="absolute -top-1 -right-1 bg-orange-500 text-[9px] font-mono text-white px-1.5 py-0.5 rounded-full min-w-[18px] text-center">{item.badge}</div> : null}
            </motion.button>
          ))}

          <div className="flex-1" />

          <motion.button
            onClick={() => setShowNewProject(true)}
            className="p-2 rounded-lg text-slate-500 hover:text-green-400 hover:bg-green-500/10 transition-all"
            whileHover={{ scale: 1.05 }} whileTap={{ scale: 0.95 }}
            title="New Project"
          >
            <Plus className="w-5 h-5" />
          </motion.button>

          <motion.button
            className="p-2 rounded-lg text-slate-500 hover:text-cyan-400 hover:bg-cyan-500/10 transition-all"
            whileHover={{ scale: 1.05, rotate: 90 }} whileTap={{ scale: 0.95 }}
            title="Settings"
          >
            <Settings className="w-5 h-5" />
          </motion.button>
        </div>

        {/* Side Panel */}
        <AnimatePresence>
          {activeToolsPanel && (
            <motion.div
              initial={{ width: 0, opacity: 0 }} animate={{ width: 280, opacity: 1 }} exit={{ width: 0, opacity: 0 }}
              transition={{ type: "spring", damping: 30, stiffness: 300 }}
              className="bg-[#0f1419] border-r border-cyan-500/20 flex flex-col overflow-hidden"
            >
              <div className="h-12 bg-[#0b0f14] border-b border-cyan-500/10 flex items-center justify-between px-4 shrink-0">
                <span className="text-xs font-mono text-cyan-400 uppercase tracking-wider">
                  {toolsPanelOptions.find((o) => o.id === activeToolsPanel)?.label}
                </span>
                <button onClick={() => setActiveToolsPanel(null)} className="p-1 hover:bg-cyan-500/20 rounded transition-colors">
                  <X className="w-4 h-4 text-slate-500 hover:text-cyan-400" />
                </button>
              </div>

              <div className="flex-1 overflow-y-auto scrollbar-hide p-3">
                {activeToolsPanel === "explorer" && (
                  <div>
                    {/* Open Folder & New Project buttons */}
                    <div className="flex items-center gap-1 mb-3">
                      <button
                        onClick={openFolder}
                        className="flex-1 flex items-center justify-center gap-1.5 px-2 py-1.5 bg-cyan-500/10 hover:bg-cyan-500/20 border border-cyan-500/20 rounded-lg text-[10px] font-mono text-cyan-400 transition-all"
                      >
                        <FolderSearch className="w-3 h-3" />
                        Open Folder
                      </button>
                      <button
                        onClick={() => setShowNewProject(true)}
                        className="flex-1 flex items-center justify-center gap-1.5 px-2 py-1.5 bg-green-500/10 hover:bg-green-500/20 border border-green-500/20 rounded-lg text-[10px] font-mono text-green-400 transition-all"
                      >
                        <Plus className="w-3 h-3" />
                        New Project
                      </button>
                    </div>
                    {renderFileTree(fileTree, 0, true)}
                  </div>
                )}

                {activeToolsPanel === "search" && (
                  <div>
                    <input
                      type="text" value={searchQuery} onChange={(e) => setSearchQuery(e.target.value)}
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
                  <div className="text-xs text-slate-500 font-mono">Debug panel — Configure launch.json</div>
                )}

                {activeToolsPanel === "extensions" && (
                  <div className="space-y-2">
                    {["C# Extension", "XAML Tools", "WPF Designer", "Python", "TypeScript", "Rust Analyzer"].map((ext) => (
                      <div key={ext} className="p-3 bg-[#0b0f14] border border-cyan-500/10 rounded hover:border-cyan-500/30 cursor-pointer">
                        <div className="text-xs text-slate-300 font-mono">{ext}</div>
                        <div className="text-[10px] text-green-400 mt-1">● Installed</div>
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
          {/* Tab Bar */}
          {openTabs.length > 0 && (
            <div className="h-10 bg-[#0f1419] border-b border-cyan-500/10 flex items-center gap-1 px-2 overflow-x-auto scrollbar-hide">
              {openTabs.map((tab) => (
                <motion.button
                  key={tab.id}
                  onClick={() => setActiveTabId(tab.id)}
                  className={`flex items-center gap-2 px-3 py-1.5 rounded-t text-xs font-mono transition-all ${
                    activeTabId === tab.id ? "bg-[#0b0f14] text-cyan-400 border-t-2 border-cyan-400" : "text-slate-400 hover:text-slate-300 hover:bg-[#0b0f14]/50"
                  }`}
                  whileHover={{ y: -2 }}
                >
                  {getFileIcon(tab.name.split(".").pop() || "")}
                  <span>{tab.name}</span>
                  {tab.isDirty && <div className="w-2 h-2 rounded-full bg-orange-400" />}
                  <button onClick={(e) => closeTab(tab.id, e)} className="ml-1 hover:bg-slate-700/50 rounded p-0.5"><X className="w-3 h-3" /></button>
                </motion.button>
              ))}
            </div>
          )}

          {/* Code Editor */}
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
              <div className="w-full h-full flex flex-col items-center justify-center gap-6">
                <div className="text-slate-600 font-mono text-sm">No file open</div>
                <div className="flex items-center gap-3">
                  <button
                    onClick={openFolder}
                    className="flex items-center gap-2 px-4 py-2 bg-cyan-500/10 hover:bg-cyan-500/20 border border-cyan-500/30 rounded-lg text-cyan-400 text-xs font-mono transition-all"
                  >
                    <FolderSearch className="w-4 h-4" />
                    Open Folder
                  </button>
                  <button
                    onClick={() => setShowNewProject(true)}
                    className="flex items-center gap-2 px-4 py-2 bg-green-500/10 hover:bg-green-500/20 border border-green-500/30 rounded-lg text-green-400 text-xs font-mono transition-all"
                  >
                    <Plus className="w-4 h-4" />
                    New Project
                  </button>
                </div>
              </div>
            )}
          </div>

          {/* Terminal */}
          {showTerminal && (
            <motion.div initial={{ height: 0 }} animate={{ height: 200 }} className="bg-[#0f1419] border-t border-cyan-500/10 overflow-hidden flex flex-col">
              <div className="h-8 bg-[#0b0f14] border-b border-cyan-500/10 flex items-center justify-between px-3 shrink-0">
                <div className="flex items-center gap-2">
                  <Terminal className="w-3 h-3 text-cyan-400" />
                  <span className="text-xs font-mono text-cyan-400 uppercase">Terminal</span>
                </div>
                <button onClick={() => setShowTerminal(false)} className="p-1 hover:bg-slate-700/50 rounded"><X className="w-3 h-3 text-slate-500" /></button>
              </div>
              <div className="flex-1 overflow-y-auto p-3 font-mono text-xs scrollbar-hide">
                {terminalLines.map((line) => (
                  <div key={line.id} className={
                    line.type === "stderr" ? "text-red-400" : line.type === "system" ? "text-cyan-400/60" : line.type === "command" ? "text-yellow-400" : "text-green-400"
                  }>{line.text}</div>
                ))}
                <div ref={terminalEndRef} />
              </div>
            </motion.div>
          )}
        </div>

        {/* AI Chat Panel in IDE Mode */}
        {showChat && (
          <motion.div initial={{ width: 0 }} animate={{ width: 320 }} className="bg-[#0f1419] border-l border-cyan-500/10 flex flex-col overflow-hidden">
            <div className="h-10 bg-[#0b0f14] border-b border-cyan-500/10 flex items-center justify-between px-3 shrink-0">
              <div className="flex items-center gap-2">
                <Sparkles className="w-3 h-3 text-cyan-400" />
                <span className="text-xs font-mono text-cyan-400 uppercase">AI Assistant</span>
              </div>
              <button onClick={() => setShowChat(false)} className="p-1 hover:bg-slate-700/50 rounded"><X className="w-3 h-3 text-slate-500" /></button>
            </div>

            <div className="flex-1 overflow-y-auto p-3 space-y-2 scrollbar-hide">
              {messages.slice(-20).map((msg) => (
                <div key={msg.id} className={`text-xs font-mono ${msg.type === "error" ? "text-red-400" : "text-slate-400"}`}>
                  <span className={msg.sender === "atlas" ? "text-cyan-400" : "text-orange-400"}>[{msg.sender === "atlas" ? "Atlas" : "You"}]</span>{" "}
                  {msg.content}
                </div>
              ))}
              <div ref={chatEndRef} />
            </div>

            <div className="p-2 border-t border-cyan-500/10 shrink-0">
              <div className="flex items-center gap-1.5 bg-[#0b0f14] border border-cyan-500/20 rounded-lg p-1.5">
                <input
                  type="text"
                  value={input}
                  onChange={(e) => setInput(e.target.value)}
                  onKeyPress={(e) => e.key === "Enter" && handleSend()}
                  placeholder="Ask AI..."
                  className="flex-1 bg-transparent text-slate-200 text-xs outline-none font-mono placeholder:text-slate-600"
                />
                <button onClick={handleSend} className="p-1 rounded bg-cyan-500/10 text-cyan-400 hover:bg-cyan-500/20 transition-all">
                  <Send className="w-3 h-3" />
                </button>
                <button
                  onClick={handleCodeMicClick}
                  className="p-1 rounded text-slate-500 hover:text-cyan-400 transition-all"
                  title="Code mic"
                >
                  <Mic className="w-3 h-3" />
                </button>
              </div>
              {codeMicNote && <div className="mt-1 text-[9px] font-mono text-slate-500">{codeMicNote}</div>}
            </div>
          </motion.div>
        )}
      </div>

      {/* New Project Dialog */}
      <AnimatePresence><NewProjectDialog /></AnimatePresence>
      </div>
    </MotionConfig>
  );
}

// Named export for compatibility
export { CodeIDE };
