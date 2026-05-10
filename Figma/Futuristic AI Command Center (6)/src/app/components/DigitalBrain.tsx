import { useEffect, useMemo, useRef, useState } from "react";
import { motion, AnimatePresence } from "motion/react";
import { 
  Brain, 
  Sun, 
  Snowflake, 
  Zap, 
  Activity, 
  Atom, 
  Cloud, 
  Binary, 
  Waves, 
  Dna, 
  Box, 
  Sparkles,
  Clock,
  Triangle,
  Aperture,
  Flame,
  ScanLine,
  Orbit,
  Hexagon,
  Eye,
  Radar,
  X, 
  SlidersHorizontal,
  ChevronLeft,
  ChevronRight
} from "lucide-react";

type OrbType =
  | "neural"
  | "solar"
  | "frost"
  | "void"
  | "quantum"
  | "nebula"
  | "matrix"
  | "sonic"
  | "bio"
  | "cyber"
  | "nova"
  | "eclipse"
  | "aurora"
  | "plasma"
  | "chronos"
  | "prism"
  | "ember"
  | "horizon"
  | "glyph"
  | "radar"
  | "vertex"
  | "shards"
  | "ribbon"
  | "lattice"
  | "swarm"
  | "helix"
  | "circuit"
  | "specter"
  | "cascade"
  | "quasar";

type OrbState = "idle" | "thinking" | "working" | "speaking";

interface OrbVariant {
  id: OrbType;
  name: string;
  icon: React.ElementType;
  color: string;
  desc: string;
}

const variants: OrbVariant[] = [
  { id: "neural", name: "Neural Net", icon: Brain, color: "text-orange-400", desc: "Logic" },
  { id: "solar", name: "Solar Core", icon: Sun, color: "text-yellow-400", desc: "Power" },
  { id: "frost", name: "Frost Matrix", icon: Snowflake, color: "text-cyan-400", desc: "Cooling" },
  { id: "void", name: "Void Singularity", icon: Zap, color: "text-purple-400", desc: "Deep Learning" },
  { id: "quantum", name: "Quantum Field", icon: Atom, color: "text-blue-400", desc: "Probability" },
  { id: "nebula", name: "Nebula Cloud", icon: Cloud, color: "text-pink-400", desc: "Creativity" },
  { id: "matrix", name: "Data Stream", icon: Binary, color: "text-green-400", desc: "Encryption" },
  { id: "sonic", name: "Sonic Wave", icon: Waves, color: "text-teal-400", desc: "Resonance" },
  { id: "bio", name: "Bio Organic", icon: Dna, color: "text-lime-400", desc: "Evolution" },
  { id: "cyber", name: "Cyber Construct", icon: Box, color: "text-indigo-400", desc: "Architecture" },
  { id: "nova", name: "Nova Burst", icon: Sparkles, color: "text-amber-400", desc: "Ignition" },
  { id: "eclipse", name: "Eclipse", icon: Eye, color: "text-violet-400", desc: "Focus" },
  { id: "aurora", name: "Aurora Drift", icon: Orbit, color: "text-emerald-400", desc: "Flow" },
  { id: "plasma", name: "Plasma Coil", icon: Aperture, color: "text-fuchsia-400", desc: "Energy" },
  { id: "chronos", name: "Chronos", icon: Clock, color: "text-slate-300", desc: "Timing" },
  { id: "prism", name: "Prism", icon: Triangle, color: "text-sky-400", desc: "Refraction" },
  { id: "ember", name: "Ember Forge", icon: Flame, color: "text-red-400", desc: "Heat" },
  { id: "horizon", name: "Horizon Scan", icon: ScanLine, color: "text-cyan-300", desc: "Search" },
  { id: "glyph", name: "Glyph Lattice", icon: Hexagon, color: "text-indigo-300", desc: "Structure" },
  { id: "radar", name: "Radar Pulse", icon: Radar, color: "text-teal-300", desc: "Tracking" },
  { id: "vertex", name: "Vertex Frame", icon: Box, color: "text-sky-400", desc: "Topology" },
  { id: "shards", name: "Shard Storm", icon: Triangle, color: "text-fuchsia-400", desc: "Fragments" },
  { id: "ribbon", name: "Wave Ribbon", icon: Waves, color: "text-cyan-300", desc: "Modulation" },
  { id: "lattice", name: "Data Lattice", icon: Hexagon, color: "text-emerald-400", desc: "Routing" },
  { id: "swarm", name: "Nanite Swarm", icon: Activity, color: "text-amber-400", desc: "Adapt" },
  { id: "helix", name: "Helix Drive", icon: Dna, color: "text-lime-400", desc: "Synthesis" },
  { id: "circuit", name: "Circuit Trace", icon: ScanLine, color: "text-indigo-400", desc: "Compute" },
  { id: "specter", name: "Specter Glitch", icon: Eye, color: "text-violet-400", desc: "Noise" },
  { id: "cascade", name: "Block Cascade", icon: Binary, color: "text-green-400", desc: "Throughput" },
  { id: "quasar", name: "Quasar Spikes", icon: Sparkles, color: "text-orange-400", desc: "Burst" },
];

const SELECTED_CORE_STORAGE_KEY = "atlas.commandCentre.selectedCore";
const CORE_SELECTOR_OPEN_STORAGE_KEY = "atlas.commandCentre.coreSelectorOpen";

function readStoredCore(): OrbType {
  try {
    const stored = window.localStorage.getItem(SELECTED_CORE_STORAGE_KEY);
    if (stored && variants.some((variant) => variant.id === stored)) {
      return stored as OrbType;
    }
  } catch {}

  return "neural";
}

function readStoredSelectorOpen(): boolean {
  try {
    return window.localStorage.getItem(CORE_SELECTOR_OPEN_STORAGE_KEY) === "true";
  } catch {
    return false;
  }
}

export function DigitalBrain() {
  const [selectedOrb, setSelectedOrb] = useState<OrbType>(() => readStoredCore());
  const [showSelector, setShowSelector] = useState(() => readStoredSelectorOpen());
  const [audioLevel, setAudioLevel] = useState(0);
  const [orbState, setOrbState] = useState<OrbState>("idle");
  const [neurons, setNeurons] = useState<
    Array<{ id: number; x: number; y: number; delay: number }>
  >([]);
  const [synapses, setSynapses] = useState<
    Array<{ id: number; from: number; to: number; color: "orange" | "blue" }>
  >([]);

  const scrollContainerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    try {
      window.localStorage.setItem(SELECTED_CORE_STORAGE_KEY, selectedOrb);
    } catch {}
  }, [selectedOrb]);

  useEffect(() => {
    try {
      window.localStorage.setItem(CORE_SELECTOR_OPEN_STORAGE_KEY, String(showSelector));
    } catch {}
  }, [showSelector]);

  const fxParticles = useMemo(
    () =>
      Array.from({ length: 72 }, (_, i) => ({
        id: i,
        a: Math.random() * Math.PI * 2,
        r: 105 + Math.random() * 95,
        s: 1.2 + Math.random() * 2.2,
        p: Math.random() * Math.PI * 2,
      })),
    []
  );

  // WebView2 host -> web: audio level for reactive visuals.
  useEffect(() => {
    const webview = (window as any)?.chrome?.webview;
    if (!webview?.addEventListener) return;

    const handler = (event: any) => {
      try {
        const msg = event?.data;
        if (!msg || typeof msg !== "object") return;
        if (msg.type === "orbs.audio") {
          const level = Number(msg.level);
          if (!Number.isFinite(level)) return;
          setAudioLevel(Math.max(0, Math.min(1, level)));
          return;
        }

        if (msg.type === "orbs.state") {
          const state = String(msg.state || "").toLowerCase();
          if (state === "idle" || state === "thinking" || state === "working" || state === "speaking")
            setOrbState(state);
        }
      } catch {
      }
    };

    webview.addEventListener("message", handler);
    return () => {
      try { webview.removeEventListener("message", handler); } catch { }
    };
  }, []);

  const scroll = (direction: "left" | "right") => {
    if (scrollContainerRef.current) {
      const { current } = scrollContainerRef;
      const scrollAmount = 300;
      if (direction === "left") {
        current.scrollBy({ left: -scrollAmount, behavior: "smooth" });
      } else {
        current.scrollBy({ left: scrollAmount, behavior: "smooth" });
      }
    }
  };

  // Initialize Neural Data
  useEffect(() => {
    const neuronArray = Array.from({ length: 30 }, (_, i) => {
      const angle = (i / 30) * Math.PI * 2;
      const radius = 60 + Math.random() * 50;
      const x = 150 + Math.cos(angle) * radius;
      const y = 150 + Math.sin(angle) * radius * 0.7;
      return { id: i, x, y, delay: Math.random() * 2 };
    });
    setNeurons(neuronArray);

    const synapseArray = Array.from({ length: 45 }, (_, i) => ({
      id: i,
      from: Math.floor(Math.random() * 30),
      to: Math.floor(Math.random() * 30),
      color: Math.random() > 0.5 ? ("orange" as const) : ("blue" as const),
    }));
    setSynapses(synapseArray);
  }, []);

  // --- RENDER FUNCTIONS FOR ORBS ---

  const gatedAudioLevel = audioLevel < 0.045 ? 0 : Math.min(1, (audioLevel - 0.045) / 0.955);
  const stateBoost = orbState === "speaking" ? 0.28 : orbState === "working" ? 0.20 : orbState === "thinking" ? 0.12 : 0.0;
  const activity = Math.max(stateBoost, gatedAudioLevel);
  const audioGlow = 0.1 + activity * 0.82;
  const audioScale = 1 + activity * 0.18;
  const audioSpinBoost = 1 - activity * 0.22;
  const stateSweepSpeed = orbState === "working" ? 0.65 : orbState === "thinking" ? 0.8 : orbState === "speaking" ? 0.75 : 1;

  const isNonCircle =
    selectedOrb === "vertex" ||
    selectedOrb === "shards" ||
    selectedOrb === "ribbon" ||
    selectedOrb === "lattice" ||
    selectedOrb === "swarm" ||
    selectedOrb === "helix" ||
    selectedOrb === "circuit" ||
    selectedOrb === "specter" ||
    selectedOrb === "cascade" ||
    selectedOrb === "quasar";

  const fxClip = isNonCircle
    ? "polygon(25% 6%, 75% 6%, 94% 50%, 75% 94%, 25% 94%, 6% 50%)"
    : undefined;

  const renderCoreFx = () => (
    <div className="absolute inset-0 pointer-events-none select-none">
      {/* Conic sweep */}
      <motion.div
        className={`absolute inset-0 ${isNonCircle ? "" : "rounded-full"}`}
        style={{
          background:
            "conic-gradient(from 0deg, rgba(34,211,238,0.0), rgba(34,211,238,0.16), rgba(168,85,247,0.10), rgba(34,211,238,0.0))",
          maskImage: "radial-gradient(circle, transparent 0 32%, black 38% 100%)",
          WebkitMaskImage: "radial-gradient(circle, transparent 0 32%, black 38% 100%)",
          opacity: orbState === "idle" ? 0.08 : 0.18 + activity * 0.55,
          clipPath: fxClip,
        }}
        animate={{ rotate: 360 }}
        transition={{ duration: (orbState === "idle" ? 26 : 12) * audioSpinBoost * stateSweepSpeed, repeat: Infinity, ease: "linear" }}
      />

      {/* Pulse rings */}
      <motion.div
        className={`absolute inset-0 border border-cyan-400/15 ${isNonCircle ? "" : "rounded-full"}`}
        animate={{ scale: orbState === "idle" ? [1, 1.003, 1] : [1, 1.05 + gatedAudioLevel * 0.12, 1], opacity: orbState === "idle" ? [0.12, 0.14, 0.12] : [0.25, 0.65, 0.25] }}
        transition={{ duration: orbState === "idle" ? 5.8 : 2.4 - audioLevel * 0.7, repeat: Infinity, ease: "easeInOut" }}
        style={{
          boxShadow: `0 0 ${orbState === "idle" ? 10 : 20 + audioLevel * 55}px rgba(34,211,238,${orbState === "idle" ? 0.12 : 0.15 + audioGlow * 0.5})`,
          clipPath: fxClip,
        }}
      />
      <motion.div
        className={`absolute inset-10 border border-purple-400/10 ${isNonCircle ? "" : "rounded-full"}`}
        animate={{ rotate: -360, opacity: orbState === "idle" ? [0.05, 0.07, 0.05] : [0.12, 0.35 + gatedAudioLevel * 0.35, 0.12] }}
        transition={{ duration: (orbState === "idle" ? 32 : 22) * audioSpinBoost, repeat: Infinity, ease: "linear" }}
        style={{ clipPath: fxClip }}
      />

      {/* Floating particles */}
      {fxParticles.map((p) => (
        <motion.div
          key={p.id}
          className="absolute rounded-full bg-cyan-200/70"
          style={{ left: "50%", top: "50%", width: p.s, height: p.s }}
          animate={{
            x: Math.cos(p.a) * (p.r + Math.sin(p.p) * (orbState === "idle" ? 1.5 : 10 + gatedAudioLevel * 25)),
            y: Math.sin(p.a) * (p.r + Math.cos(p.p) * (orbState === "idle" ? 1.5 : 10 + gatedAudioLevel * 25)),
            opacity: orbState === "idle" ? [0.04, 0.1, 0.04] : [0.1, 0.75, 0.1],
            scale: orbState === "idle" ? [1, 1.04, 1] : [1, 1.8, 1],
          }}
          transition={{
            duration: orbState === "idle" ? 7.5 + (p.id % 9) * 0.4 : 2.8 + (p.id % 9) * 0.22,
            repeat: Infinity,
            ease: "easeInOut",
            delay: p.id * 0.015,
          }}
        />
      ))}

      {/* State overlays */}
      {orbState === "thinking" && (
        <>
          {[...Array(9)].map((_, i) => (
            <motion.div
              key={`t-${i}`}
              className="absolute left-1/2 top-1/2 h-[2px] w-[420px] -translate-x-1/2 -translate-y-1/2 bg-gradient-to-r from-transparent via-cyan-200/30 to-transparent"
              style={{ rotate: i * 20 }}
              animate={{ opacity: [0.0, 0.55 + activity * 0.25, 0.0], scaleX: [0.75, 1.0, 0.75] }}
              transition={{ duration: 1.8, repeat: Infinity, delay: i * 0.06, ease: "easeInOut" }}
            />
          ))}
        </>
      )}
      {orbState === "working" && (
        <>
          {[...Array(18)].map((_, i) => (
            <motion.div
              key={`w-${i}`}
              className="absolute left-1/2 top-1/2 w-[3px] h-10 bg-gradient-to-t from-transparent via-amber-200/50 to-transparent"
              style={{ rotate: i * 20, transformOrigin: "center", translateX: "-50%", translateY: "-50%" } as any}
              animate={{ y: [-60, 60], opacity: [0.0, 0.8, 0.0] }}
              transition={{ duration: 0.9 + (i % 6) * 0.12, repeat: Infinity, delay: i * 0.03, ease: "linear" }}
            />
          ))}
        </>
      )}
      {orbState === "speaking" && (
        <>
          {[0, 1, 2].map((i) => (
            <motion.div
              key={`s-${i}`}
              className={`absolute inset-0 border border-cyan-200/20 ${isNonCircle ? "" : "rounded-full"}`}
              animate={{ scale: [0.9, 1.15 + audioLevel * 0.25, 0.9], opacity: [0.0, 0.8, 0.0] }}
              transition={{ duration: 1.15 - audioLevel * 0.25, repeat: Infinity, delay: i * 0.18, ease: "easeInOut" }}
              style={{ clipPath: fxClip }}
            />
          ))}
        </>
      )}
    </div>
  );

  const vertexPoints = useMemo(
    () =>
      Array.from({ length: 16 }, (_, i) => ({
        id: i,
        a: (i / 16) * Math.PI * 2,
        r: 95 + Math.random() * 35,
        z: -1 + Math.random() * 2,
        d: Math.random() * 2,
      })),
    []
  );

  const swarm = useMemo(
    () =>
      Array.from({ length: 54 }, (_, i) => ({
        id: i,
        a: Math.random() * Math.PI * 2,
        r: 40 + Math.random() * 110,
        s: 1 + Math.random() * 2,
        d: Math.random() * 3,
      })),
    []
  );

  const renderNeuralOrb = () => (
    <motion.div
      key="neural"
      className="relative w-[300px] h-[300px]"
      initial={{ opacity: 0, scale: 0.8 }}
      animate={{ opacity: 1, scale: orbState === "idle" ? 1 : audioScale, rotate: 360 }}
      exit={{ opacity: 0, scale: 0.8 }}
      transition={{ duration: 1, rotate: { duration: 40 * audioSpinBoost, repeat: Infinity, ease: "linear" } }}
    >
      <motion.div className="absolute inset-0 rounded-full border border-orange-500/20" animate={{ scale: orbState === "idle" ? 1 : [1, 1.06 + gatedAudioLevel * 0.08, 1] }} transition={{ scale: { duration: orbState === "idle" ? 10 : 4, repeat: Infinity, ease: "easeInOut" } }} />
      <motion.div className="absolute inset-4 rounded-full border border-cyan-500/20" animate={{ scale: orbState === "idle" ? 1 : [1, 1.03 + gatedAudioLevel * 0.06, 1] }} transition={{ scale: { duration: orbState === "idle" ? 10 : 3, repeat: Infinity, ease: "easeInOut" } }} />
      <svg className="absolute inset-0 w-full h-full" viewBox="0 0 300 300" style={{ filter: "url(#glow)" }}>
        <defs>
          <filter id="glow" x="-50%" y="-50%" width="200%" height="200%">
            <feGaussianBlur stdDeviation="2" result="coloredBlur" />
            <feMerge><feMergeNode in="coloredBlur" /><feMergeNode in="SourceGraphic" /></feMerge>
          </filter>
        </defs>
        {synapses.map((synapse) => {
          const from = neurons[synapse.from];
          const to = neurons[synapse.to];
          if (!from || !to) return null;
          return (
            <motion.line key={synapse.id} x1={from.x} y1={from.y} x2={to.x} y2={to.y} stroke={synapse.color === "orange" ? "#f97316" : "#22d3ee"} strokeWidth="0.5" strokeOpacity="0.3" initial={{ pathLength: 0, opacity: 0 }} animate={{ pathLength: [0, 1, 0], opacity: orbState === "idle" ? [0, 0.18, 0] : [0, 0.6, 0] }} transition={{ duration: orbState === "idle" ? 6 : 3, repeat: Infinity, delay: Math.random() * 2, ease: "easeInOut" }} />
          );
        })}
        {neurons.map((neuron) => (
          <g key={neuron.id}>
            <motion.circle cx={neuron.x} cy={neuron.y} r="5" fill={neuron.id % 2 === 0 ? "#f97316" : "#22d3ee"} opacity="0.2" animate={{ r: orbState === "idle" ? [5, 5.8, 5] : [5, 8, 5], opacity: orbState === "idle" ? [0.18, 0.24, 0.18] : [0.2, 0.4, 0.2] }} transition={{ duration: orbState === "idle" ? 5.5 : 2, repeat: Infinity, delay: neuron.delay, ease: "easeInOut" }} />
            <motion.circle cx={neuron.x} cy={neuron.y} r="1.5" fill={neuron.id % 2 === 0 ? "#f97316" : "#22d3ee"} animate={{ opacity: orbState === "idle" ? [0.55, 0.72, 0.55] : [0.6, 1, 0.6] }} transition={{ duration: orbState === "idle" ? 5.5 : 2, repeat: Infinity, delay: neuron.delay, ease: "easeInOut" }} />
          </g>
        ))}
      </svg>
      <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2">
        <motion.div className="w-12 h-12 rounded-full bg-gradient-to-br from-orange-500/30 via-cyan-500/30 to-orange-500/30" animate={{ scale: orbState === "idle" ? 1 : [1, 1.18 + gatedAudioLevel * 0.12, 1], rotate: 360 }} transition={{ scale: { duration: orbState === "idle" ? 8 : 3, repeat: Infinity, ease: "easeInOut" }, rotate: { duration: 10, repeat: Infinity, ease: "linear" } }} style={{ boxShadow: "0 0 40px rgba(249, 115, 22, 0.3), 0 0 60px rgba(34, 211, 238, 0.3)" }} />
      </div>
    </motion.div>
  );

  const renderSolarOrb = () => (
    <motion.div
      key="solar"
      className="relative w-[300px] h-[300px] flex items-center justify-center"
      initial={{ opacity: 0, scale: 0.8 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={{ opacity: 0, scale: 0.8 }}
      transition={{ duration: 1 }}
    >
      <motion.div className="w-40 h-40 rounded-full bg-gradient-to-r from-yellow-500 via-orange-500 to-red-600 blur-md" animate={{ scale: [1, 1.1 + audioLevel * 0.15, 1], rotate: 360 }} transition={{ duration: 8 * audioSpinBoost, repeat: Infinity, ease: "linear" }} style={{ boxShadow: `0 0 ${60 + audioLevel * 80}px rgba(234, 179, 8, ${0.35 + audioGlow * 0.55})` }} />
      <motion.div className="absolute w-56 h-56 rounded-full border-4 border-yellow-500/30 border-dashed" animate={{ rotate: -360 }} transition={{ duration: 20, repeat: Infinity, ease: "linear" }} />
      <motion.div className="absolute w-72 h-72 rounded-full border-2 border-orange-500/20" animate={{ rotate: 360, scale: [1, 1.05, 1] }} transition={{ rotate: { duration: 30, repeat: Infinity, ease: "linear" }, scale: { duration: 5, repeat: Infinity } }} />
      {[...Array(8)].map((_, i) => (
        <motion.div key={i} className="absolute w-1 h-32 bg-gradient-to-t from-transparent via-yellow-400/50 to-transparent" style={{ rotate: i * 45, transformOrigin: "center" }} animate={{ height: ["8rem", "10rem", "8rem"], opacity: [0.3, 0.6, 0.3] }} transition={{ duration: 2 + Math.random(), repeat: Infinity, delay: Math.random() }} />
      ))}
    </motion.div>
  );

  const renderFrostOrb = () => (
    <motion.div
      key="frost"
      className="relative w-[300px] h-[300px] flex items-center justify-center"
      initial={{ opacity: 0, scale: 0.8 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={{ opacity: 0, scale: 0.8 }}
      transition={{ duration: 1 }}
    >
      <motion.div className="w-32 h-32 rotate-45 border-4 border-cyan-400/50 bg-cyan-900/20 backdrop-blur-md" animate={{ rotate: [45, 225], borderRadius: ["10%", "50%", "10%"] }} transition={{ duration: 10, repeat: Infinity, ease: "easeInOut" }} style={{ boxShadow: "0 0 40px rgba(34, 211, 238, 0.4)" }} />
      {[...Array(3)].map((_, i) => (
        <motion.div key={i} className="absolute border border-cyan-500/30" style={{ width: `${180 + i * 40}px`, height: `${180 + i * 40}px`, clipPath: "polygon(50% 0%, 100% 25%, 100% 75%, 50% 100%, 0% 75%, 0% 25%)" }} animate={{ rotate: i % 2 === 0 ? 360 : -360 }} transition={{ duration: 20 + i * 5, repeat: Infinity, ease: "linear" }} />
      ))}
      {[...Array(12)].map((_, i) => (
        <motion.div key={i} className="absolute w-1 h-1 bg-white rounded-full" style={{ top: "50%", left: "50%" }} animate={{ x: Math.cos(i * 30) * 100, y: Math.sin(i * 30) * 100, opacity: [0, 1, 0] }} transition={{ duration: 3, repeat: Infinity, delay: i * 0.2 }} />
      ))}
    </motion.div>
  );

  const renderVoidOrb = () => (
    <motion.div
      key="void"
      className="relative w-[300px] h-[300px] flex items-center justify-center"
      initial={{ opacity: 0, scale: 0.8 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={{ opacity: 0, scale: 0.8 }}
      transition={{ duration: 1 }}
    >
      <motion.div className="w-40 h-40 rounded-full bg-black border border-purple-500/50" animate={{ scale: [1, 0.95 - audioLevel * 0.04, 1] }} transition={{ duration: 4, repeat: Infinity, ease: "easeInOut" }} style={{ boxShadow: `inset 0 0 ${40 + audioLevel * 30}px rgba(168, 85, 247, ${0.35 + audioGlow * 0.55}), 0 0 ${20 + audioLevel * 25}px rgba(168, 85, 247, ${0.35 + audioGlow * 0.55})` }} />
      <motion.div className="absolute w-[280px] h-[60px] rounded-[100%] border-t-2 border-b-2 border-purple-500/80 bg-purple-500/10" style={{ top: "120px" }} animate={{ rotate: 360 }} transition={{ duration: 8, repeat: Infinity, ease: "linear" }} />
      <motion.div className="absolute w-[60px] h-[280px] rounded-[100%] border-l-2 border-r-2 border-purple-500/80 bg-purple-500/10" style={{ left: "120px" }} animate={{ rotate: 360 }} transition={{ duration: 12, repeat: Infinity, ease: "linear" }} />
      {[...Array(5)].map((_, i) => (
        <motion.div key={i} className="absolute w-full h-[1px] bg-purple-400" style={{ top: "50%", left: 0, transformOrigin: "center", rotate: Math.random() * 360 }} animate={{ opacity: [0, 1, 0], width: ["50%", "100%", "50%"] }} transition={{ duration: 0.2, repeat: Infinity, repeatDelay: Math.random() * 3 }} />
      ))}
    </motion.div>
  );

  const renderQuantumOrb = () => (
    <motion.div
      key="quantum"
      className="relative w-[300px] h-[300px] flex items-center justify-center"
      initial={{ opacity: 0, scale: 0.8 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={{ opacity: 0, scale: 0.8 }}
      transition={{ duration: 1 }}
    >
      {/* Nucleus */}
      <motion.div className="w-12 h-12 rounded-full bg-blue-500" animate={{ scale: [1, 1.2 + audioLevel * 0.15, 1] }} transition={{ duration: 2, repeat: Infinity }} style={{ boxShadow: `0 0 ${30 + audioLevel * 50}px rgba(59,130,246,${0.35 + audioGlow * 0.55})` }} />
      {/* Orbital Rings */}
      {[0, 60, 120].map((deg, i) => (
        <motion.div key={i} className="absolute w-64 h-64 rounded-full border border-blue-400/30" style={{ rotateX: 70, rotateY: deg }} animate={{ rotateZ: 360 }} transition={{ duration: 3 + i, repeat: Infinity, ease: "linear" }}>
          <motion.div className="w-3 h-3 rounded-full bg-white shadow-[0_0_10px_white]" style={{ offsetPath: "path('M 128,0 A 128,128 0 1,1 128,256 A 128,128 0 1,1 128,0')", offsetDistance: "0%" }} animate={{ offsetDistance: "100%" }} transition={{ duration: 3 + i, repeat: Infinity, ease: "linear" }} />
        </motion.div>
      ))}
    </motion.div>
  );

  const renderNebulaOrb = () => (
    <motion.div
      key="nebula"
      className="relative w-[300px] h-[300px] flex items-center justify-center"
      initial={{ opacity: 0, scale: 0.8 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={{ opacity: 0, scale: 0.8 }}
      transition={{ duration: 1 }}
    >
      {[...Array(5)].map((_, i) => (
        <motion.div
          key={i}
          className={`absolute w-40 h-40 rounded-full blur-[40px] mix-blend-screen ${i % 2 === 0 ? 'bg-pink-600/40' : 'bg-purple-600/40'}`}
          animate={{ x: Math.sin(i) * 30, y: Math.cos(i) * 30, scale: [1, 1.2, 1] }}
          transition={{ duration: 5 + i, repeat: Infinity, repeatType: "reverse" }}
        />
      ))}
      <motion.div className="absolute inset-0 w-full h-full rounded-full border border-pink-500/10" animate={{ rotate: 360 }} transition={{ duration: 20, repeat: Infinity, ease: "linear" }} />
      {[...Array(20)].map((_, i) => (
        <motion.div key={i} className="absolute w-1 h-1 bg-white rounded-full" style={{ top: Math.random() * 300, left: Math.random() * 300 }} animate={{ opacity: [0, 1, 0] }} transition={{ duration: 2, repeat: Infinity, delay: Math.random() * 2 }} />
      ))}
    </motion.div>
  );

  const renderMatrixOrb = () => (
    <motion.div
      key="matrix"
      className="relative w-[300px] h-[300px] flex items-center justify-center overflow-hidden rounded-full border border-green-500/20 bg-black"
      initial={{ opacity: 0, scale: 0.8 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={{ opacity: 0, scale: 0.8 }}
      transition={{ duration: 1 }}
    >
      <div className="absolute inset-0 bg-green-900/10" />
      {[...Array(15)].map((_, i) => (
        <motion.div
          key={i}
          className="absolute text-[10px] text-green-500 font-mono writing-vertical-rl"
          style={{ left: `${i * 20}px`, top: -50 }}
          animate={{ top: 350 }}
          transition={{ duration: 2 + Math.random() * 2, repeat: Infinity, ease: "linear", delay: Math.random() * 2 }}
        >
          {Array.from({ length: 10 }).map(() => String.fromCharCode(0x30A0 + Math.random() * 96)).join('')}
        </motion.div>
      ))}
      <div className="absolute inset-0 rounded-full border-4 border-green-500/30" style={{ boxShadow: "inset 0 0 20px rgba(34,197,94,0.5)" }} />
    </motion.div>
  );

  const renderSonicOrb = () => (
    <motion.div
      key="sonic"
      className="relative w-[300px] h-[300px] flex items-center justify-center"
      initial={{ opacity: 0, scale: 0.8 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={{ opacity: 0, scale: 0.8 }}
      transition={{ duration: 1 }}
    >
      {[...Array(5)].map((_, i) => (
        <motion.div
          key={i}
          className="absolute rounded-full border-2 border-teal-500/30"
          style={{ width: 50 + i * 40, height: 50 + i * 40 }}
          animate={{ scale: [1, 1.08 + audioLevel * 0.22, 1], opacity: [0.25, 0.85, 0.25], borderWidth: ["2px", `${3 + audioLevel * 3}px`, "2px"] }}
          transition={{ duration: (1.6 - audioLevel * 0.4) + i * 0.05, repeat: Infinity, delay: i * 0.2 }}
        />
      ))}
      <motion.div className="w-20 h-20 rounded-full bg-teal-500/20 backdrop-blur-sm flex items-center justify-center border border-teal-400">
         <Waves className="w-8 h-8 text-teal-400" />
      </motion.div>
    </motion.div>
  );

  const renderBioOrb = () => (
    <motion.div
      key="bio"
      className="relative w-[300px] h-[300px] flex items-center justify-center"
      initial={{ opacity: 0, scale: 0.8 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={{ opacity: 0, scale: 0.8 }}
      transition={{ duration: 1 }}
    >
      <motion.div
        className="w-48 h-48 bg-lime-500/20 rounded-full blur-md mix-blend-screen"
        animate={{ borderRadius: ["50%", "40% 60% 70% 30% / 40% 50% 60% 50%", "50%"], rotate: 360 }}
        transition={{ duration: 8, repeat: Infinity }}
      />
      <motion.div
        className="absolute w-32 h-32 bg-lime-400/30 rounded-full mix-blend-overlay"
        animate={{ borderRadius: ["50%", "60% 40% 30% 70% / 60% 30% 70% 40%", "50%"], rotate: -360 }}
        transition={{ duration: 6, repeat: Infinity }}
      />
      <motion.div
        className="absolute w-10 h-10 bg-lime-300 rounded-full shadow-[0_0_20px_rgba(163,230,53,0.8)]"
        animate={{ scale: [1, 1.2, 1] }}
        transition={{ duration: 2, repeat: Infinity, ease: "easeInOut" }}
      />
    </motion.div>
  );

  const renderCyberOrb = () => (
    <motion.div
      key="cyber"
      className="relative w-[300px] h-[300px] flex items-center justify-center"
      initial={{ opacity: 0, scale: 0.8 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={{ opacity: 0, scale: 0.8 }}
      transition={{ duration: 1 }}
    >
      <motion.div className="w-40 h-40 border-2 border-indigo-500 bg-indigo-900/20" animate={{ rotateX: 360, rotateY: 360 }} transition={{ duration: 10, repeat: Infinity, ease: "linear" }} style={{ transformStyle: "preserve-3d" }} />
      <motion.div className="absolute w-56 h-56 border border-indigo-500/50 rounded-full" animate={{ rotateX: -360, rotateY: 180 }} transition={{ duration: 15, repeat: Infinity, ease: "linear" }} style={{ transformStyle: "preserve-3d" }} />
      {[...Array(4)].map((_, i) => (
        <motion.div key={i} className="absolute w-full h-[1px] bg-indigo-500/50" style={{ top: "50%", rotate: i * 45 }} animate={{ opacity: [0.2, 0.8, 0.2] }} transition={{ duration: 2, repeat: Infinity }} />
      ))}
    </motion.div>
  );

  const renderNovaOrb = () => (
    <motion.div
      key="nova"
      className="relative w-[300px] h-[300px] flex items-center justify-center"
      initial={{ opacity: 0, scale: 0.8 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={{ opacity: 0, scale: 0.8 }}
      transition={{ duration: 1 }}
    >
      <motion.div
        className="w-24 h-24 rounded-full bg-amber-400/20 border border-amber-400/40"
        animate={{ scale: [1, 1.25 + audioLevel * 0.35, 1], opacity: [0.6, 1, 0.6] }}
        transition={{ duration: 1.7 - audioLevel * 0.5, repeat: Infinity, ease: "easeInOut" }}
        style={{ boxShadow: `0 0 ${40 + audioLevel * 90}px rgba(251,191,36,${0.25 + audioGlow * 0.65})` }}
      />
      {[...Array(10)].map((_, i) => (
        <motion.div
          key={i}
          className="absolute w-[2px] h-28 bg-gradient-to-t from-transparent via-amber-300/60 to-transparent"
          style={{ rotate: i * 36, transformOrigin: "center" }}
          animate={{ opacity: [0.1, 0.9, 0.1], height: ["7rem", `${8 + audioLevel * 4}rem`, "7rem"] }}
          transition={{ duration: 1.2 + (i % 3) * 0.25, repeat: Infinity, delay: i * 0.04 }}
        />
      ))}
      <motion.div
        className="absolute w-64 h-64 rounded-full border border-amber-300/20"
        animate={{ rotate: 360 }}
        transition={{ duration: 18 * audioSpinBoost, repeat: Infinity, ease: "linear" }}
      />
    </motion.div>
  );

  const renderEclipseOrb = () => (
    <motion.div
      key="eclipse"
      className="relative w-[300px] h-[300px] flex items-center justify-center"
      initial={{ opacity: 0, scale: 0.8 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={{ opacity: 0, scale: 0.8 }}
      transition={{ duration: 1 }}
    >
      <motion.div
        className="absolute w-40 h-40 rounded-full bg-black"
        style={{ boxShadow: `inset 0 0 ${35 + audioLevel * 35}px rgba(139,92,246,${0.35 + audioGlow * 0.55})` }}
      />
      <motion.div
        className="absolute w-56 h-56 rounded-full border border-violet-400/30"
        animate={{ rotate: 360, scale: [1, 1.03 + audioLevel * 0.08, 1] }}
        transition={{ rotate: { duration: 22 * audioSpinBoost, repeat: Infinity, ease: "linear" }, scale: { duration: 2.2, repeat: Infinity, ease: "easeInOut" } }}
      />
      <motion.div
        className="absolute w-72 h-72 rounded-full border border-violet-400/15 border-dashed"
        animate={{ rotate: -360 }}
        transition={{ duration: 34 * audioSpinBoost, repeat: Infinity, ease: "linear" }}
      />
    </motion.div>
  );

  const renderAuroraOrb = () => (
    <motion.div
      key="aurora"
      className="relative w-[300px] h-[300px] flex items-center justify-center"
      initial={{ opacity: 0, scale: 0.8 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={{ opacity: 0, scale: 0.8 }}
      transition={{ duration: 1 }}
    >
      {[...Array(4)].map((_, i) => (
        <motion.div
          key={i}
          className={`absolute w-48 h-48 rounded-full blur-[45px] mix-blend-screen ${i % 2 === 0 ? "bg-emerald-500/25" : "bg-cyan-500/25"}`}
          animate={{
            x: Math.sin(i) * (18 + audioLevel * 18),
            y: Math.cos(i) * (18 + audioLevel * 18),
            scale: [1, 1.15 + audioLevel * 0.15, 1],
          }}
          transition={{ duration: 5.5 + i, repeat: Infinity, repeatType: "reverse", ease: "easeInOut" }}
        />
      ))}
      <motion.div className="absolute w-64 h-64 rounded-full border border-emerald-400/15" animate={{ rotate: 360 }} transition={{ duration: 26 * audioSpinBoost, repeat: Infinity, ease: "linear" }} />
    </motion.div>
  );

  const renderPlasmaOrb = () => (
    <motion.div
      key="plasma"
      className="relative w-[300px] h-[300px] flex items-center justify-center"
      initial={{ opacity: 0, scale: 0.8 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={{ opacity: 0, scale: 0.8 }}
      transition={{ duration: 1 }}
    >
      <motion.div
        className="absolute w-60 h-60 rounded-full border border-fuchsia-400/25"
        animate={{ rotate: 360 }}
        transition={{ duration: 16 * audioSpinBoost, repeat: Infinity, ease: "linear" }}
      />
      <motion.div
        className="absolute w-40 h-40 rounded-full bg-fuchsia-500/10"
        animate={{ scale: [1, 1.18 + audioLevel * 0.22, 1] }}
        transition={{ duration: 2.4 - audioLevel * 0.7, repeat: Infinity, ease: "easeInOut" }}
        style={{ boxShadow: `0 0 ${45 + audioLevel * 80}px rgba(217,70,239,${0.22 + audioGlow * 0.70})` }}
      />
      {[...Array(8)].map((_, i) => (
        <motion.div
          key={i}
          className="absolute w-[3px] h-24 bg-gradient-to-t from-transparent via-fuchsia-300/60 to-transparent"
          style={{ rotate: i * 45, transformOrigin: "center" }}
          animate={{ opacity: [0.1, 0.8, 0.1] }}
          transition={{ duration: 1.3 + (i % 4) * 0.2, repeat: Infinity, delay: i * 0.06 }}
        />
      ))}
    </motion.div>
  );

  const renderChronosOrb = () => (
    <motion.div
      key="chronos"
      className="relative w-[300px] h-[300px] flex items-center justify-center"
      initial={{ opacity: 0, scale: 0.8 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={{ opacity: 0, scale: 0.8 }}
      transition={{ duration: 1 }}
    >
      <motion.div className="absolute w-64 h-64 rounded-full border border-slate-400/15" />
      {[...Array(12)].map((_, i) => (
        <motion.div
          key={i}
          className="absolute w-[2px] h-8 bg-slate-300/40"
          style={{ rotate: i * 30, transformOrigin: "center" }}
          animate={{ opacity: [0.12, 0.6 + audioLevel * 0.35, 0.12] }}
          transition={{ duration: 1.6, repeat: Infinity, delay: i * 0.03 }}
        />
      ))}
      <motion.div
        className="absolute w-1 h-24 bg-slate-200/70 rounded"
        animate={{ rotate: 360 }}
        transition={{ duration: 10 * audioSpinBoost, repeat: Infinity, ease: "linear" }}
      />
      <motion.div
        className="absolute w-[3px] h-16 bg-slate-200/50 rounded"
        animate={{ rotate: -360 }}
        transition={{ duration: 6 * audioSpinBoost, repeat: Infinity, ease: "linear" }}
      />
      <motion.div className="absolute w-10 h-10 rounded-full bg-slate-300/10 border border-slate-300/20" />
    </motion.div>
  );

  const renderPrismOrb = () => (
    <motion.div
      key="prism"
      className="relative w-[300px] h-[300px] flex items-center justify-center"
      initial={{ opacity: 0, scale: 0.8 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={{ opacity: 0, scale: 0.8 }}
      transition={{ duration: 1 }}
    >
      <motion.div
        className="absolute w-48 h-48"
        style={{ clipPath: "polygon(50% 6%, 96% 92%, 4% 92%)" }}
        animate={{ rotate: 360, scale: [1, 1.06 + audioLevel * 0.12, 1] }}
        transition={{ rotate: { duration: 18 * audioSpinBoost, repeat: Infinity, ease: "linear" }, scale: { duration: 2.2, repeat: Infinity, ease: "easeInOut" } }}
      >
        <div className="w-full h-full bg-gradient-to-br from-sky-400/15 via-cyan-300/10 to-violet-400/15 border border-sky-400/25" />
      </motion.div>
      <motion.div
        className="absolute w-64 h-64 rounded-full border border-sky-400/10"
        animate={{ rotate: -360 }}
        transition={{ duration: 34 * audioSpinBoost, repeat: Infinity, ease: "linear" }}
      />
    </motion.div>
  );

  const renderEmberOrb = () => (
    <motion.div
      key="ember"
      className="relative w-[300px] h-[300px] flex items-center justify-center overflow-hidden"
      initial={{ opacity: 0, scale: 0.8 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={{ opacity: 0, scale: 0.8 }}
      transition={{ duration: 1 }}
    >
      <motion.div
        className="absolute w-36 h-36 rounded-full bg-gradient-to-r from-red-500/20 via-orange-500/20 to-amber-500/20 border border-red-400/20"
        animate={{ scale: [1, 1.12 + audioLevel * 0.22, 1] }}
        transition={{ duration: 1.9 - audioLevel * 0.5, repeat: Infinity, ease: "easeInOut" }}
        style={{ boxShadow: `0 0 ${40 + audioLevel * 90}px rgba(248,113,113,${0.18 + audioGlow * 0.75})` }}
      />
      {[...Array(22)].map((_, i) => (
        <motion.div
          key={i}
          className="absolute w-1 h-1 rounded-full bg-amber-300/70"
          style={{ left: `${10 + (i * 4) % 80}%`, bottom: "20%" }}
          animate={{ y: [-10, -(90 + audioLevel * 80)], opacity: [0, 1, 0], scale: [0.6, 1.3, 0.8] }}
          transition={{ duration: 1.4 + (i % 7) * 0.18, repeat: Infinity, delay: (i % 10) * 0.08, ease: "easeOut" }}
        />
      ))}
    </motion.div>
  );

  const renderHorizonOrb = () => (
    <motion.div
      key="horizon"
      className="relative w-[300px] h-[300px] flex items-center justify-center"
      initial={{ opacity: 0, scale: 0.8 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={{ opacity: 0, scale: 0.8 }}
      transition={{ duration: 1 }}
    >
      <motion.div className="absolute w-64 h-64 rounded-full border border-cyan-300/12" />
      {[...Array(7)].map((_, i) => (
        <motion.div
          key={i}
          className="absolute w-72 h-[2px] bg-gradient-to-r from-transparent via-cyan-300/30 to-transparent"
          animate={{ y: [-110, 110], opacity: [0, 0.7 + audioLevel * 0.25, 0] }}
          transition={{ duration: 2.8 - audioLevel * 0.8, repeat: Infinity, delay: i * 0.25, ease: "linear" }}
        />
      ))}
      <motion.div
        className="absolute w-28 h-28 rounded-full border border-cyan-300/20"
        animate={{ scale: [1, 1.08 + audioLevel * 0.18, 1] }}
        transition={{ duration: 1.8, repeat: Infinity, ease: "easeInOut" }}
      />
    </motion.div>
  );

  const renderGlyphOrb = () => (
    <motion.div
      key="glyph"
      className="relative w-[300px] h-[300px] flex items-center justify-center"
      initial={{ opacity: 0, scale: 0.8 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={{ opacity: 0, scale: 0.8 }}
      transition={{ duration: 1 }}
    >
      <motion.div
        className="absolute w-56 h-56"
        style={{ clipPath: "polygon(25% 6%, 75% 6%, 94% 50%, 75% 94%, 25% 94%, 6% 50%)" }}
        animate={{ rotate: 360, scale: [1, 1.04 + audioLevel * 0.12, 1] }}
        transition={{ rotate: { duration: 24 * audioSpinBoost, repeat: Infinity, ease: "linear" }, scale: { duration: 2.6, repeat: Infinity, ease: "easeInOut" } }}
      >
        <div className="w-full h-full border border-indigo-300/25 bg-indigo-500/5" />
      </motion.div>
      {[...Array(14)].map((_, i) => (
        <motion.div
          key={i}
          className="absolute w-[2px] h-10 bg-indigo-300/25"
          style={{ rotate: i * (360 / 14), transformOrigin: "center" }}
          animate={{ opacity: [0.1, 0.55 + audioLevel * 0.35, 0.1] }}
          transition={{ duration: 2.2, repeat: Infinity, delay: i * 0.04 }}
        />
      ))}
    </motion.div>
  );

  const renderRadarOrb = () => (
    <motion.div
      key="radar"
      className="relative w-[300px] h-[300px] flex items-center justify-center"
      initial={{ opacity: 0, scale: 0.8 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={{ opacity: 0, scale: 0.8 }}
      transition={{ duration: 1 }}
    >
      {[0, 1, 2].map((i) => (
        <motion.div
          key={i}
          className="absolute rounded-full border border-teal-300/20"
          style={{ width: 120 + i * 70, height: 120 + i * 70 }}
          animate={{ scale: [1, 1.08 + audioLevel * 0.18, 1], opacity: [0.15, 0.65, 0.15] }}
          transition={{ duration: 1.9 - audioLevel * 0.55, repeat: Infinity, delay: i * 0.18, ease: "easeInOut" }}
        />
      ))}
      <motion.div
        className="absolute w-64 h-64 rounded-full"
        animate={{ rotate: 360 }}
        transition={{ duration: 6.5 * audioSpinBoost, repeat: Infinity, ease: "linear" }}
        style={{
          background:
            "conic-gradient(from 0deg, rgba(45,212,191,0.0), rgba(45,212,191,0.22), rgba(45,212,191,0.0))",
          maskImage: "radial-gradient(circle, transparent 0 32%, black 36% 100%)",
          WebkitMaskImage: "radial-gradient(circle, transparent 0 32%, black 36% 100%)",
        }}
      />
      {[...Array(10)].map((_, i) => (
        <motion.div
          key={i}
          className="absolute w-1.5 h-1.5 rounded-full bg-teal-200/70"
          style={{ left: `${15 + (i * 7) % 70}%`, top: `${20 + (i * 11) % 60}%` }}
          animate={{ opacity: [0.1, 0.95, 0.1] }}
          transition={{ duration: 1.7, repeat: Infinity, delay: i * 0.08 }}
        />
      ))}
    </motion.div>
  );

  const renderVertexOrb = () => (
    <motion.div
      key="vertex"
      className="relative w-[300px] h-[300px] flex items-center justify-center"
      initial={{ opacity: 0, scale: 0.8 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={{ opacity: 0, scale: 0.8 }}
      transition={{ duration: 1 }}
    >
      <motion.div
        className="absolute w-64 h-64"
        animate={{ rotateX: [28, 56, 28], rotateY: [0, 180, 360], rotateZ: 360 }}
        transition={{ duration: 14 * audioSpinBoost, repeat: Infinity, ease: "linear" }}
        style={{ transformStyle: "preserve-3d" }}
      >
        <svg className="w-full h-full" viewBox="0 0 300 300">
          {vertexPoints.map((p) => {
            const x = 150 + Math.cos(p.a) * (p.r + Math.sin(p.d + activity * 2) * (10 + activity * 18));
            const y = 150 + Math.sin(p.a) * (p.r * 0.7 + Math.cos(p.d + activity * 2) * (10 + activity * 18));
            return (
              <motion.rect
                key={p.id}
                x={x}
                y={y}
                width={3}
                height={3}
                fill="rgba(56,189,248,0.85)"
                animate={{ opacity: [0.15, 1, 0.15] }}
                transition={{ duration: 1.6, repeat: Infinity, delay: p.id * 0.03 }}
              />
            );
          })}
          {vertexPoints.map((p, i) => {
            const q = vertexPoints[(i + 3) % vertexPoints.length];
            const x1 = 150 + Math.cos(p.a) * p.r;
            const y1 = 150 + Math.sin(p.a) * (p.r * 0.7);
            const x2 = 150 + Math.cos(q.a) * q.r;
            const y2 = 150 + Math.sin(q.a) * (q.r * 0.7);
            return (
              <motion.line
                key={`l-${p.id}`}
                x1={x1}
                y1={y1}
                x2={x2}
                y2={y2}
                stroke="rgba(56,189,248,0.22)"
                strokeWidth={1}
                animate={{ opacity: [0.05, 0.35 + activity * 0.35, 0.05] }}
                transition={{ duration: 2.2, repeat: Infinity, delay: i * 0.04, ease: "easeInOut" }}
              />
            );
          })}
        </svg>
      </motion.div>
      <motion.div
        className="absolute w-14 h-14"
        style={{ clipPath: "polygon(50% 0%, 100% 38%, 81% 100%, 19% 100%, 0% 38%)" }}
        animate={{ rotate: 360, scale: [1, 1.15 + activity * 0.12, 1] }}
        transition={{ rotate: { duration: 10 * audioSpinBoost, repeat: Infinity, ease: "linear" }, scale: { duration: 1.6, repeat: Infinity, ease: "easeInOut" } }}
      >
        <div className="w-full h-full bg-sky-400/15 border border-sky-300/25" />
      </motion.div>
    </motion.div>
  );

  const renderShardsOrb = () => (
    <motion.div
      key="shards"
      className="relative w-[300px] h-[300px] flex items-center justify-center"
      initial={{ opacity: 0, scale: 0.8 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={{ opacity: 0, scale: 0.8 }}
      transition={{ duration: 1 }}
    >
      {[...Array(18)].map((_, i) => (
        <motion.div
          key={i}
          className="absolute w-28 h-10"
          style={{
            clipPath: "polygon(0% 50%, 70% 0%, 100% 50%, 70% 100%)",
            rotate: i * 20,
          }}
          animate={{
            x: [0, (18 + activity * 40) * (i % 2 === 0 ? 1 : -1), 0],
            opacity: [0.05, 0.55 + activity * 0.35, 0.05],
            scale: [0.85, 1.0 + activity * 0.15, 0.85],
          }}
          transition={{ duration: 1.4 + (i % 6) * 0.18, repeat: Infinity, ease: "easeInOut", delay: i * 0.03 }}
        >
          <div className="w-full h-full bg-fuchsia-400/10 border border-fuchsia-300/25" />
        </motion.div>
      ))}
      <motion.div
        className="absolute w-32 h-32"
        style={{ clipPath: "polygon(50% 0%, 100% 50%, 50% 100%, 0% 50%)" }}
        animate={{ rotate: 360, opacity: [0.25, 0.8, 0.25] }}
        transition={{ duration: 7.5 * audioSpinBoost, repeat: Infinity, ease: "linear" }}
      >
        <div className="w-full h-full bg-fuchsia-500/5 border border-fuchsia-300/20" />
      </motion.div>
    </motion.div>
  );

  const renderRibbonOrb = () => (
    <motion.div
      key="ribbon"
      className="relative w-[300px] h-[300px] flex items-center justify-center"
      initial={{ opacity: 0, scale: 0.8 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={{ opacity: 0, scale: 0.8 }}
      transition={{ duration: 1 }}
    >
      <svg className="absolute w-[280px] h-[280px]" viewBox="0 0 300 300">
        <defs>
          <linearGradient id="ribbon" x1="0" y1="0" x2="1" y2="1">
            <stop offset="0%" stopColor="rgba(34,211,238,0.05)" />
            <stop offset="50%" stopColor={`rgba(34,211,238,${0.25 + activity * 0.55})`} />
            <stop offset="100%" stopColor="rgba(168,85,247,0.05)" />
          </linearGradient>
        </defs>
        {[0, 1, 2, 3].map((i) => (
          <motion.path
            key={i}
            d={`M 20 ${120 + i * 22} C 70 ${70 + activity * 40}, 120 ${170 - activity * 40}, 170 ${120 + i * 22} S 260 ${170 + activity * 30}, 280 ${120 + i * 22}`}
            fill="none"
            stroke="url(#ribbon)"
            strokeWidth={3}
            strokeLinecap="round"
            strokeDasharray="14 10"
            animate={{ strokeDashoffset: [0, -120] }}
            transition={{ duration: 2.4 - activity * 0.8 + i * 0.12, repeat: Infinity, ease: "linear" }}
            opacity={0.55}
          />
        ))}
      </svg>
      <motion.div
        className="absolute w-16 h-16"
        style={{ clipPath: "polygon(50% 0%, 92% 28%, 92% 72%, 50% 100%, 8% 72%, 8% 28%)" }}
        animate={{ rotate: -360, scale: [1, 1.1 + activity * 0.2, 1] }}
        transition={{ rotate: { duration: 9 * audioSpinBoost, repeat: Infinity, ease: "linear" }, scale: { duration: 1.5, repeat: Infinity, ease: "easeInOut" } }}
      >
        <div className="w-full h-full bg-cyan-400/10 border border-cyan-300/20" />
      </motion.div>
    </motion.div>
  );

  const renderLatticeOrb = () => (
    <motion.div
      key="lattice"
      className="relative w-[300px] h-[300px] flex items-center justify-center"
      initial={{ opacity: 0, scale: 0.8 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={{ opacity: 0, scale: 0.8 }}
      transition={{ duration: 1 }}
    >
      <motion.div
        className="absolute w-64 h-64"
        animate={{ rotate: 360 }}
        transition={{ duration: 18 * audioSpinBoost, repeat: Infinity, ease: "linear" }}
      >
        <svg className="w-full h-full" viewBox="0 0 300 300">
          {[...Array(10)].map((_, i) => (
            <motion.line
              key={`g1-${i}`}
              x1={30}
              y1={45 + i * 22}
              x2={270}
              y2={45 + i * 22}
              stroke="rgba(52,211,153,0.12)"
              strokeWidth={1}
              animate={{ opacity: [0.05, 0.25 + activity * 0.25, 0.05] }}
              transition={{ duration: 2.2, repeat: Infinity, delay: i * 0.05 }}
            />
          ))}
          {[...Array(10)].map((_, i) => (
            <motion.line
              key={`g2-${i}`}
              x1={45 + i * 22}
              y1={30}
              x2={45 + i * 22}
              y2={270}
              stroke="rgba(52,211,153,0.10)"
              strokeWidth={1}
              animate={{ opacity: [0.03, 0.22 + activity * 0.28, 0.03] }}
              transition={{ duration: 2.0, repeat: Infinity, delay: i * 0.04 }}
            />
          ))}
          {[...Array(18)].map((_, i) => (
            <motion.rect
              key={`n-${i}`}
              x={55 + (i * 17) % 190}
              y={55 + (i * 29) % 190}
              width={3}
              height={3}
              fill="rgba(167,243,208,0.85)"
              animate={{ opacity: [0.1, 1, 0.1] }}
              transition={{ duration: 1.4, repeat: Infinity, delay: i * 0.06 }}
            />
          ))}
        </svg>
      </motion.div>
      <motion.div
        className="absolute w-44 h-44"
        style={{ clipPath: "polygon(50% 0%, 100% 50%, 50% 100%, 0% 50%)" }}
        animate={{ rotate: -360, opacity: [0.18, 0.75, 0.18] }}
        transition={{ duration: 10.5 * audioSpinBoost, repeat: Infinity, ease: "linear" }}
      >
        <div className="w-full h-full border border-emerald-300/20 bg-emerald-500/5" />
      </motion.div>
    </motion.div>
  );

  const renderSwarmOrb = () => (
    <motion.div
      key="swarm"
      className="relative w-[300px] h-[300px] flex items-center justify-center"
      initial={{ opacity: 0, scale: 0.8 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={{ opacity: 0, scale: 0.8 }}
      transition={{ duration: 1 }}
    >
      {swarm.map((p) => (
        <motion.div
          key={p.id}
          className="absolute bg-amber-200/70"
          style={{ left: "50%", top: "50%", width: p.s, height: p.s }}
          animate={{
            x: Math.cos(p.a + activity * 2) * (p.r + Math.sin(p.d + activity * 2) * (12 + activity * 22)),
            y: Math.sin(p.a + activity * 2) * (p.r * 0.85 + Math.cos(p.d + activity * 2) * (12 + activity * 22)),
            opacity: [0.08, 0.9, 0.08],
            rotate: [0, 90, 180, 270, 360],
          }}
          transition={{ duration: 2.2 + (p.id % 7) * 0.18, repeat: Infinity, ease: "easeInOut", delay: p.id * 0.01 }}
        />
      ))}
      <motion.div
        className="absolute w-28 h-28"
        style={{ clipPath: "polygon(50% 0%, 100% 50%, 50% 100%, 0% 50%)" }}
        animate={{ rotate: 360 }}
        transition={{ duration: 6.5 * audioSpinBoost, repeat: Infinity, ease: "linear" }}
      >
        <div className="w-full h-full bg-amber-400/5 border border-amber-200/18" />
      </motion.div>
    </motion.div>
  );

  const renderHelixOrb = () => (
    <motion.div
      key="helix"
      className="relative w-[300px] h-[300px] flex items-center justify-center"
      initial={{ opacity: 0, scale: 0.8 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={{ opacity: 0, scale: 0.8 }}
      transition={{ duration: 1 }}
    >
      {[...Array(18)].map((_, i) => {
        const t = i / 18;
        const y = -110 + i * 13;
        const x1 = Math.sin((t + activity * 0.4) * Math.PI * 2) * (45 + activity * 12);
        const x2 = -x1;
        return (
          <div key={i} className="absolute left-1/2 top-1/2" style={{ transform: `translate(-50%, -50%) translateY(${y}px)` }}>
            <motion.div
              className="absolute w-[2px] h-12 bg-lime-200/10"
              style={{ left: -1, top: -8 }}
              animate={{ opacity: [0.08, 0.35 + activity * 0.35, 0.08] }}
              transition={{ duration: 1.4, repeat: Infinity, delay: i * 0.04 }}
            />
            <motion.div
              className="absolute w-2 h-2 bg-lime-200/80"
              style={{ transform: `translateX(${x1}px)` }}
              animate={{ scale: [0.8, 1.6, 0.8], opacity: [0.15, 1, 0.15] }}
              transition={{ duration: 1.2, repeat: Infinity, delay: i * 0.03 }}
            />
            <motion.div
              className="absolute w-2 h-2 bg-emerald-200/70"
              style={{ transform: `translateX(${x2}px)` }}
              animate={{ scale: [0.8, 1.6, 0.8], opacity: [0.15, 1, 0.15] }}
              transition={{ duration: 1.2, repeat: Infinity, delay: 0.5 + i * 0.03 }}
            />
          </div>
        );
      })}
      <motion.div
        className="absolute w-20 h-20 rounded-lg border border-lime-300/15"
        animate={{ rotate: 360, scale: [1, 1.08 + activity * 0.16, 1] }}
        transition={{ rotate: { duration: 9.5 * audioSpinBoost, repeat: Infinity, ease: "linear" }, scale: { duration: 1.6, repeat: Infinity, ease: "easeInOut" } }}
      />
    </motion.div>
  );

  const renderCircuitOrb = () => (
    <motion.div
      key="circuit"
      className="relative w-[300px] h-[300px] flex items-center justify-center"
      initial={{ opacity: 0, scale: 0.8 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={{ opacity: 0, scale: 0.8 }}
      transition={{ duration: 1 }}
    >
      <div className="absolute w-64 h-64 border border-indigo-300/12" />
      <svg className="absolute w-[260px] h-[260px]" viewBox="0 0 260 260">
        {[...Array(8)].map((_, i) => (
          <motion.path
            key={i}
            d={`M ${20 + i * 10} ${40 + (i % 3) * 30} H ${220 - i * 8} V ${90 + (i % 4) * 20} H ${60 + i * 6}`}
            fill="none"
            stroke="rgba(129,140,248,0.18)"
            strokeWidth={2}
            strokeLinejoin="round"
            strokeLinecap="round"
            strokeDasharray="18 10"
            animate={{ strokeDashoffset: [0, -120] }}
            transition={{ duration: 2.8 - activity * 0.9 + i * 0.18, repeat: Infinity, ease: "linear" }}
          />
        ))}
      </svg>
      <motion.div
        className="absolute w-10 h-10"
        style={{ clipPath: "polygon(50% 0%, 100% 50%, 50% 100%, 0% 50%)" }}
        animate={{ rotate: 360, opacity: [0.2, 0.9, 0.2] }}
        transition={{ duration: 2.1 - activity * 0.6, repeat: Infinity, ease: "easeInOut" }}
      >
        <div className="w-full h-full bg-indigo-400/10 border border-indigo-200/18" />
      </motion.div>
    </motion.div>
  );

  const renderSpecterOrb = () => (
    <motion.div
      key="specter"
      className="relative w-[300px] h-[300px] flex items-center justify-center overflow-hidden"
      initial={{ opacity: 0, scale: 0.8 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={{ opacity: 0, scale: 0.8 }}
      transition={{ duration: 1 }}
    >
      <motion.div
        className="absolute inset-0"
        animate={{ opacity: [0.15, 0.6 + activity * 0.3, 0.15] }}
        transition={{ duration: 1.7, repeat: Infinity, ease: "easeInOut" }}
        style={{
          background:
            "linear-gradient(90deg, rgba(139,92,246,0.0), rgba(139,92,246,0.16), rgba(34,211,238,0.12), rgba(139,92,246,0.0))",
        }}
      />
      {[...Array(18)].map((_, i) => (
        <motion.div
          key={i}
          className="absolute left-0 right-0 h-[2px] bg-violet-200/10"
          style={{ top: `${(i * 100) / 18}%` }}
          animate={{ x: [-(40 + (i % 6) * 10), 40 + (i % 6) * 10, -(40 + (i % 6) * 10)], opacity: [0.0, 0.7, 0.0] }}
          transition={{ duration: 1.1 + (i % 5) * 0.12, repeat: Infinity, delay: i * 0.03, ease: "easeInOut" }}
        />
      ))}
      {[...Array(10)].map((_, i) => (
        <motion.div
          key={`b-${i}`}
          className="absolute w-24 h-6 border border-violet-300/12 bg-violet-500/5"
          style={{ left: `${10 + (i * 9) % 70}%`, top: `${10 + (i * 13) % 70}%` }}
          animate={{ opacity: [0.0, 0.55 + activity * 0.35, 0.0], y: [0, (i % 2 === 0 ? -10 : 10), 0] }}
          transition={{ duration: 0.9 + (i % 4) * 0.15, repeat: Infinity, delay: i * 0.06 }}
        />
      ))}
    </motion.div>
  );

  const renderCascadeOrb = () => (
    <motion.div
      key="cascade"
      className="relative w-[300px] h-[300px] flex items-center justify-center overflow-hidden"
      initial={{ opacity: 0, scale: 0.8 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={{ opacity: 0, scale: 0.8 }}
      transition={{ duration: 1 }}
    >
      <div className="absolute inset-0 bg-green-900/5" />
      {[...Array(22)].map((_, i) => (
        <motion.div
          key={i}
          className="absolute w-8 h-3 bg-green-300/10 border border-green-300/10"
          style={{ left: `${(i * 7) % 95}%`, top: -20 }}
          animate={{
            y: [0, 360],
            opacity: [0.0, 0.6 + activity * 0.35, 0.0],
            scaleX: [0.8, 1.4 + activity * 0.35, 0.8],
          }}
          transition={{ duration: 1.4 + (i % 7) * 0.12 - activity * 0.4, repeat: Infinity, delay: (i % 10) * 0.12, ease: "linear" }}
        />
      ))}
      <motion.div
        className="absolute w-56 h-56"
        style={{ clipPath: "polygon(50% 0%, 100% 50%, 50% 100%, 0% 50%)" }}
        animate={{ rotate: 360 }}
        transition={{ duration: 22 * audioSpinBoost, repeat: Infinity, ease: "linear" }}
      >
        <div className="w-full h-full border border-green-300/12 bg-green-500/5" />
      </motion.div>
    </motion.div>
  );

  const renderQuasarOrb = () => (
    <motion.div
      key="quasar"
      className="relative w-[300px] h-[300px] flex items-center justify-center"
      initial={{ opacity: 0, scale: 0.8 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={{ opacity: 0, scale: 0.8 }}
      transition={{ duration: 1 }}
    >
      <motion.div
        className="absolute w-20 h-20"
        style={{ clipPath: "polygon(50% 0%, 60% 35%, 100% 50%, 60% 65%, 50% 100%, 40% 65%, 0% 50%, 40% 35%)" }}
        animate={{ rotate: 360, scale: [1, 1.25 + activity * 0.35, 1] }}
        transition={{ rotate: { duration: 6.2 * audioSpinBoost, repeat: Infinity, ease: "linear" }, scale: { duration: 1.15 - activity * 0.25, repeat: Infinity, ease: "easeInOut" } }}
      >
        <div className="w-full h-full bg-orange-400/12 border border-orange-300/22" style={{ boxShadow: `0 0 ${35 + activity * 95}px rgba(249,115,22,${0.12 + audioGlow * 0.55})` }} />
      </motion.div>
      {[...Array(14)].map((_, i) => (
        <motion.div
          key={i}
          className="absolute w-[3px] h-28 bg-gradient-to-t from-transparent via-orange-200/55 to-transparent"
          style={{ rotate: i * (360 / 14), transformOrigin: "center" }}
          animate={{ opacity: [0.05, 0.85, 0.05], height: ["7rem", `${8 + activity * 6}rem`, "7rem"] }}
          transition={{ duration: 1.05 - activity * 0.25 + (i % 4) * 0.08, repeat: Infinity, delay: i * 0.02, ease: "easeInOut" }}
        />
      ))}
      <motion.div
        className="absolute w-64 h-64"
        style={{ clipPath: "polygon(50% 0%, 100% 50%, 50% 100%, 0% 50%)" }}
        animate={{ rotate: -360, opacity: [0.08, 0.35 + activity * 0.35, 0.08] }}
        transition={{ duration: 14 * audioSpinBoost, repeat: Infinity, ease: "linear" }}
      >
        <div className="w-full h-full border border-orange-300/12 bg-orange-500/5" />
      </motion.div>
    </motion.div>
  );

  return (
    <div className="w-full h-full flex flex-col items-center justify-center bg-transparent relative overflow-hidden transition-all duration-300 min-h-0 min-w-0">
      {/* Main Visualizer */}
      <motion.div
        className="flex-1 flex items-center justify-center w-full min-h-0 pb-40 pt-8 px-6"
        animate={{ scale: 1.18 + activity * 0.16 }}
        transition={{ type: "spring", stiffness: 180, damping: 20 }}
      >
        <div className="relative w-[300px] h-[300px] mx-auto" style={{ transformOrigin: "center" }}>
          {renderCoreFx()}
          <AnimatePresence mode="wait">
            {selectedOrb === "neural" && renderNeuralOrb()}
            {selectedOrb === "solar" && renderSolarOrb()}
            {selectedOrb === "frost" && renderFrostOrb()}
            {selectedOrb === "void" && renderVoidOrb()}
            {selectedOrb === "quantum" && renderQuantumOrb()}
            {selectedOrb === "nebula" && renderNebulaOrb()}
            {selectedOrb === "matrix" && renderMatrixOrb()}
            {selectedOrb === "sonic" && renderSonicOrb()}
            {selectedOrb === "bio" && renderBioOrb()}
            {selectedOrb === "cyber" && renderCyberOrb()}
            {selectedOrb === "nova" && renderNovaOrb()}
            {selectedOrb === "eclipse" && renderEclipseOrb()}
            {selectedOrb === "aurora" && renderAuroraOrb()}
            {selectedOrb === "plasma" && renderPlasmaOrb()}
            {selectedOrb === "chronos" && renderChronosOrb()}
            {selectedOrb === "prism" && renderPrismOrb()}
            {selectedOrb === "ember" && renderEmberOrb()}
            {selectedOrb === "horizon" && renderHorizonOrb()}
            {selectedOrb === "glyph" && renderGlyphOrb()}
            {selectedOrb === "radar" && renderRadarOrb()}
            {selectedOrb === "vertex" && renderVertexOrb()}
            {selectedOrb === "shards" && renderShardsOrb()}
            {selectedOrb === "ribbon" && renderRibbonOrb()}
            {selectedOrb === "lattice" && renderLatticeOrb()}
            {selectedOrb === "swarm" && renderSwarmOrb()}
            {selectedOrb === "helix" && renderHelixOrb()}
            {selectedOrb === "circuit" && renderCircuitOrb()}
            {selectedOrb === "specter" && renderSpecterOrb()}
            {selectedOrb === "cascade" && renderCascadeOrb()}
            {selectedOrb === "quasar" && renderQuasarOrb()}
          </AnimatePresence>
        </div>
      </motion.div>

      {/* Orb Selector UI */}
      <AnimatePresence>
        {showSelector ? (
          <motion.div 
            initial={{ y: 200, opacity: 0 }}
            animate={{ y: 0, opacity: 1 }}
            exit={{ y: 200, opacity: 0 }}
            transition={{ type: "spring", stiffness: 300, damping: 30 }}
            className="fixed bottom-0 left-0 right-0 z-10 w-full max-w-4xl px-6 pb-4 mx-auto"
          >
            <div className="bg-[#0f1419]/90 backdrop-blur-md border border-cyan-500/20 rounded-2xl p-4 shadow-[0_0_40px_rgba(0,0,0,0.5)] flex flex-col">
              <div className="flex items-center justify-between mb-2 px-1 shrink-0">
                <span className="text-xs font-mono text-slate-400 uppercase tracking-widest">Select Core Engine ({variants.length})</span>
                <button
                  onClick={() => setShowSelector(false)}
                  className="p-1 hover:bg-slate-700/50 rounded transition-colors group"
                  title="Hide Selector"
                >
                   <X className="w-4 h-4 text-slate-500 group-hover:text-slate-300" />
                </button>
              </div>
              
              <div className="relative flex items-center">
                <button
                  onClick={() => scroll("left")}
                  className="absolute left-0 z-20 p-2 bg-[#0b0f14]/80 rounded-full border border-slate-700 hover:border-cyan-500 hover:text-cyan-400 text-slate-400 transition-all -ml-2 shadow-lg"
                >
                  <ChevronLeft className="w-4 h-4" />
                </button>
                
                <div 
                  ref={scrollContainerRef}
                  className="flex gap-3 overflow-x-auto scrollbar-hide px-8 w-full scroll-smooth"
                  style={{ scrollbarWidth: "none", msOverflowStyle: "none" }}
                >
                  {variants.map((variant) => (
                    <button
                      key={variant.id}
                      onClick={() => setSelectedOrb(variant.id)}
                      className={`relative group flex flex-col items-center justify-center w-24 p-2 shrink-0 rounded-xl border transition-all duration-300 ${
                        selectedOrb === variant.id
                          ? `bg-slate-800/80 border-${variant.color.split('-')[1]}-500/50 shadow-[0_0_15px_rgba(0,0,0,0.3)]`
                          : "bg-slate-900/40 border-transparent hover:bg-slate-800/50 hover:border-slate-700"
                      }`}
                    >
                      <div className={`p-2 rounded-full mb-2 transition-all duration-300 ${
                        selectedOrb === variant.id 
                          ? `bg-${variant.color.split('-')[1]}-500/20 text-${variant.color.split('-')[1]}-400 scale-110` 
                          : "bg-slate-800 text-slate-500 group-hover:text-slate-300"
                      }`}>
                        <variant.icon className="w-5 h-5" />
                      </div>
                      <div className={`text-[10px] font-bold font-mono uppercase truncate w-full text-center transition-colors ${
                        selectedOrb === variant.id ? variant.color : "text-slate-400"
                      }`}>
                        {variant.name}
                      </div>
                      
                      {selectedOrb === variant.id && (
                        <motion.div
                          layoutId="active-glow"
                          className={`absolute inset-0 rounded-xl border border-${variant.color.split('-')[1]}-500/30`}
                          transition={{ type: "spring", stiffness: 300, damping: 30 }}
                        />
                      )}
                    </button>
                  ))}
                </div>

                <button
                  onClick={() => scroll("right")}
                  className="absolute right-0 z-20 p-2 bg-[#0b0f14]/80 rounded-full border border-slate-700 hover:border-cyan-500 hover:text-cyan-400 text-slate-400 transition-all -mr-2 shadow-lg"
                >
                  <ChevronRight className="w-4 h-4" />
                </button>
              </div>
            </div>
          </motion.div>
        ) : (
           <motion.div
             initial={{ y: 100, opacity: 0 }}
             animate={{ y: 0, opacity: 1 }}
             exit={{ y: 100, opacity: 0 }}
             className="fixed bottom-3 left-1/2 -translate-x-1/2 z-10"
           >
             <button
               onClick={() => setShowSelector(true)}
               className="flex items-center gap-2 px-4 py-2 bg-[#0f1419]/80 backdrop-blur-md border border-cyan-500/20 rounded-full hover:bg-cyan-500/10 transition-all shadow-[0_0_20px_rgba(0,0,0,0.5)] group"
             >
               <SlidersHorizontal className="w-3 h-3 text-cyan-500 group-hover:text-cyan-400" />
               <span className="text-xs font-mono text-cyan-500 group-hover:text-cyan-400 uppercase tracking-widest">
                 Customize Core
               </span>
             </button>
           </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}