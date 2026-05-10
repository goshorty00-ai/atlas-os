import { useEffect, useMemo, useState } from "react";
import { motion } from "motion/react";

type OrbState = "idle" | "thinking" | "working" | "speaking";

type Particle = {
  id: number;
  angle: number;
  distance: number;
  size: number;
  phase: number;
};

export function OrbOverlayOnly() {
  const [state, setState] = useState<OrbState>("idle");
  const [audioLevel, setAudioLevel] = useState(0);

  const particles = useMemo<Particle[]>(
    () =>
      Array.from({ length: 56 }, (_, i) => ({
        id: i,
        angle: Math.random() * Math.PI * 2,
        distance: Math.random() * 74 + 34,
        size: Math.random() * 2.6 + 1.2,
        phase: Math.random() * Math.PI * 2,
      })),
    []
  );

  useEffect(() => {
    // Force a transparent host surface for overlay use in WebView2.
    const html = document.documentElement;
    const body = document.body;
    const root = document.getElementById("root");
    const prevHtmlBg = html.style.background;
    const prevBodyBg = body.style.background;
    const prevRootBg = root?.style.background;
    const prevMargin = body.style.margin;
    const prevTitle = document.title;

    html.style.background = "transparent";
    body.style.background = "transparent";
    if (root) root.style.background = "transparent";
    body.style.margin = "0";
    document.title = "ATLAS_ORBS_READY";

    return () => {
      html.style.background = prevHtmlBg;
      body.style.background = prevBodyBg;
      if (root && prevRootBg !== undefined) root.style.background = prevRootBg;
      body.style.margin = prevMargin;
      document.title = prevTitle;
    };
  }, []);

  useEffect(() => {
    const webview = (window as any)?.chrome?.webview;
    if (!webview?.addEventListener) return;

    const handler = (event: any) => {
      try {
        const msg = event?.data;
        if (!msg || typeof msg !== "object") return;

        if (msg.type === "orbs.audio") {
          const level = Number(msg.level);
          if (Number.isFinite(level)) setAudioLevel(Math.max(0, Math.min(1, level)));
          return;
        }

        if (msg.type === "orbs.state") {
          const next = String(msg.state || "").toLowerCase();
          if (next === "idle" || next === "thinking" || next === "working" || next === "speaking")
            setState(next as OrbState);
        }
      } catch {
      }
    };

    webview.addEventListener("message", handler);
    return () => {
      try { webview.removeEventListener("message", handler); } catch {}
    };
  }, []);

  const activeAudio = Math.max(0, Math.min(1, audioLevel));
  const ringDuration = state === "thinking" ? 16 : state === "working" ? 14 : state === "speaking" ? 12 : 24;
  const corePulse = state === "thinking"
    ? [1, 1.08, 1]
    : state === "working"
      ? [1, 1.06, 1]
      : state === "speaking"
        ? [1, 1.03 + activeAudio * 0.08, 1]
        : [1, 1.015, 1];
  const glowOpacity = state === "speaking"
    ? [0.18, 0.3 + activeAudio * 0.45, 0.18]
    : state === "thinking"
      ? [0.14, 0.3, 0.14]
      : [0.08, 0.16, 0.08];

  const ringClass = state === "thinking" ? "border-orange-400/22" : state === "speaking" ? "border-purple-400/20" : "border-cyan-400/18";
  const particleClass = state === "thinking" ? "bg-orange-300" : state === "speaking" ? "bg-purple-300" : "bg-cyan-300";
  const dotClass = state === "thinking" ? "bg-orange-400" : state === "speaking" ? "bg-purple-400" : "bg-cyan-400";

  return (
    <div className="fixed inset-0 w-full h-full overflow-hidden bg-transparent pointer-events-none select-none">
      <div className="w-full h-full flex items-center justify-center">
        <div className="relative" style={{ width: 260, height: 260 }}>
          {/* Outer rotating rings */}
          <motion.div
            className={`absolute inset-0 rounded-full border ${ringClass}`}
            animate={{ rotate: 360 }}
            transition={{ duration: ringDuration, repeat: Infinity, ease: "linear" }}
          />
          <motion.div
            className={`absolute inset-5 rounded-full border ${ringClass}`}
            animate={{ rotate: -360 }}
            transition={{ duration: ringDuration * 0.8, repeat: Infinity, ease: "linear" }}
          />
          <motion.div
            className={`absolute inset-10 rounded-full border ${ringClass}`}
            animate={{ rotate: 360 }}
            transition={{ duration: ringDuration * 0.65, repeat: Infinity, ease: "linear" }}
          />

          {/* Particle orb */}
          <div className="absolute inset-0 flex items-center justify-center">
            <div className="relative" style={{ width: 136, height: 136 }}>
              {/* Core glow */}
              <motion.div
                className="absolute inset-0 rounded-full bg-gradient-radial from-cyan-400/20 to-transparent"
                animate={{ scale: corePulse, opacity: glowOpacity }}
                transition={{ duration: state === "speaking" ? Math.max(1.15, 2.1 - activeAudio * 0.85) : state === "thinking" ? 2.2 : 4.8, repeat: Infinity, ease: "easeInOut" }}
              />

              {/* Particles */}
              {particles.map((p) => (
                <motion.div
                  key={p.id}
                  className={`absolute rounded-full ${particleClass}`}
                  style={{ width: p.size, height: p.size, left: "50%", top: "50%" }}
                  animate={{
                    x: Math.cos(p.angle) * p.distance + Math.cos(p.phase) * (state === "thinking" ? 4 : state === "speaking" ? 1 + activeAudio * 6 : 0.6),
                    y: Math.sin(p.angle) * p.distance + Math.sin(p.phase) * (state === "thinking" ? 4 : state === "speaking" ? 1 + activeAudio * 6 : 0.6),
                    opacity: state === "speaking" ? [0.16, 0.45 + activeAudio * 0.5, 0.16] : [0.18, 0.42, 0.18],
                    scale: state === "thinking" ? [1, 1.22, 1] : state === "speaking" ? [1, 1.05 + activeAudio * 0.25, 1] : [1, 1.04, 1],
                  }}
                  transition={{
                    duration: state === "idle" ? 6 + (p.id % 7) * 0.4 : 2.6 + (p.id % 7) * 0.25,
                    repeat: Infinity,
                    ease: "easeInOut",
                    delay: p.id * 0.015,
                  }}
                />
              ))}

              {/* Center dot */}
              <motion.div
                className={`absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-3.5 h-3.5 rounded-full ${dotClass}`}
                animate={{ scale: state === "speaking" ? [1, 1.08 + activeAudio * 0.3, 1] : [1, 1.03, 1], opacity: state === "speaking" ? [0.45, 0.85 + activeAudio * 0.15, 0.45] : [0.42, 0.58, 0.42] }}
                transition={{ duration: state === "idle" ? 5.4 : state === "speaking" ? Math.max(1.05, 2.0 - activeAudio * 0.7) : 2.6, repeat: Infinity, ease: "easeInOut" }}
              />
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
