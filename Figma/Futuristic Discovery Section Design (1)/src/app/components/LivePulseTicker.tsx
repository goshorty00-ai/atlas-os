import { motion } from "motion/react";
import { Film, Gamepad2, Music, Sparkles, TrendingUp } from "lucide-react";

const liveUpdates = [
  { icon: Film, text: "Quantum Realm: Official Trailer Drops Tomorrow", type: "trailer" },
  { icon: Gamepad2, text: "Cyber Chronicles 2077 - Pre-orders Now Live", type: "game" },
  { icon: Music, text: "Nova's New Album 'Starlight' Announced", type: "music" },
  { icon: Sparkles, text: "Breaking: Shadow Protocol Movie Confirmed", type: "announcement" },
  { icon: TrendingUp, text: "Eclipse Series Breaks Streaming Records", type: "trending" },
];

export function LivePulseTicker() {
  return (
    <div className="relative overflow-hidden border-b border-cyan-500/20 bg-gradient-to-r from-slate-950 via-cyan-950/10 to-slate-950">
      {/* Grid texture overlay */}
      <div className="absolute inset-0 opacity-10" style={{
        backgroundImage: `linear-gradient(rgba(0, 217, 255, 0.1) 1px, transparent 1px),
                          linear-gradient(90deg, rgba(0, 217, 255, 0.1) 1px, transparent 1px)`,
        backgroundSize: '20px 20px'
      }} />
      
      <div className="relative flex items-center gap-3 px-4 py-2">
        <div className="flex items-center gap-2 text-cyan-400 shrink-0">
          <motion.div
            animate={{ scale: [1, 1.2, 1] }}
            transition={{ duration: 2, repeat: Infinity }}
            className="w-2 h-2 bg-cyan-400 rounded-full shadow-[0_0_10px_rgba(0,217,255,0.8)]"
          />
          <span className="text-xs font-medium tracking-wider">LIVE PULSE</span>
        </div>
        
        <div className="flex-1 overflow-hidden">
          <motion.div
            animate={{ x: [0, -2000] }}
            transition={{ duration: 40, repeat: Infinity, ease: "linear" }}
            className="flex items-center gap-8 whitespace-nowrap"
          >
            {[...liveUpdates, ...liveUpdates, ...liveUpdates].map((update, index) => {
              const Icon = update.icon;
              return (
                <div key={index} className="flex items-center gap-2 text-cyan-100/80">
                  <Icon className="w-3.5 h-3.5 text-cyan-400" />
                  <span className="text-xs">{update.text}</span>
                  <span className="w-1 h-1 bg-cyan-400/40 rounded-full" />
                </div>
              );
            })}
          </motion.div>
        </div>
      </div>
    </div>
  );
}
