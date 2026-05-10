import { Minus, Square, X } from "lucide-react";
import { motion } from "motion/react";

function postToWpf(payload: any) {
  try {
    const webview = (window as any).chrome?.webview;
    if (!webview) return;
    const msg = typeof payload === "string" ? payload : JSON.stringify(payload);
    webview.postMessage(msg);
  } catch {
  }
}

export function TopNav() {
  const now = new Date();
  const dateStr = now.toLocaleDateString("en-US", {
    weekday: "short",
    month: "short",
    day: "numeric",
  });
  const timeStr = now.toLocaleTimeString("en-US", {
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
  });

  return (
    <motion.div
      initial={{ y: -50, opacity: 0 }}
      animate={{ y: 0, opacity: 1 }}
      exit={{ y: -50, opacity: 0 }}
      transition={{ type: "spring", stiffness: 300, damping: 30 }}
      className="flex items-center justify-between h-14 px-6 border-b border-cyan-500/10 bg-[#0f1419] shrink-0"
    >
      {/* Left side - Compact Neon Logo */}
      <div className="flex items-center gap-3">
        {/* Logo */}
        <div className="flex items-center gap-2">
          {/* Simple hexagon icon */}
          <svg width="24" height="24" viewBox="0 0 100 100" className="flex-shrink-0">
            <polygon
              points="50,10 85,30 85,70 50,90 15,70 15,30"
              fill="none"
              stroke="#22d3ee"
              strokeWidth="4"
              style={{
                filter: "drop-shadow(0 0 8px rgba(34, 211, 238, 0.8))"
              }}
            />
            <polygon
              points="50,25 72,37 72,63 50,75 28,63 28,37"
              fill="#22d3ee"
              opacity="0.2"
            />
          </svg>

          {/* Text inline */}
          <div className="flex items-center gap-2">
            <span 
              className="text-sm font-mono font-bold tracking-wider text-cyan-400"
              style={{
                textShadow: "0 0 10px rgba(34, 211, 238, 0.8), 0 0 20px rgba(34, 211, 238, 0.4)"
              }}
            >
              ATLAS AI
            </span>
            
            <div className="w-px h-4 bg-cyan-500/30" />
            
            <div className="flex items-center gap-1.5 px-2 py-0.5 rounded-full bg-green-500/10 border border-green-500/40">
              <div 
                className="w-1.5 h-1.5 rounded-full bg-green-400"
                style={{
                  boxShadow: "0 0 6px rgba(34, 197, 94, 0.8)"
                }}
              />
              <span 
                className="text-[10px] font-mono font-bold text-green-400 uppercase tracking-wider"
                style={{
                  textShadow: "0 0 8px rgba(34, 197, 94, 0.6)"
                }}
              >
                ONLINE
              </span>
            </div>
          </div>
        </div>
      </div>

      {/* Center spacer */}
      <div className="flex-1" />

      {/* Right side - Date, Time, Window Controls */}
      <div className="flex items-center gap-6">
        <div className="text-[12px] text-slate-400 font-mono">
          <div>{dateStr}</div>
          <div className="text-cyan-400">{timeStr}</div>
        </div>
        <div className="flex items-center gap-2">
          <button
            onClick={() => postToWpf({ type: "window_control", action: "minimize" })}
            className="p-1.5 hover:bg-slate-700/50 rounded transition-colors"
            title="Minimize"
          >
            <Minus className="w-4 h-4 text-slate-400" />
          </button>
          <button
            onClick={() => postToWpf({ type: "window_control", action: "maximize" })}
            className="p-1.5 hover:bg-slate-700/50 rounded transition-colors"
            title="Maximize"
          >
            <Square className="w-3.5 h-3.5 text-slate-400" />
          </button>
          <button
            onClick={() => postToWpf({ type: "window_control", action: "close" })}
            className="p-1.5 hover:bg-red-500/20 rounded transition-colors group"
            title="Close"
          >
            <X className="w-4 h-4 text-slate-400 group-hover:text-red-400" />
          </button>
        </div>
      </div>
    </motion.div>
  );
}