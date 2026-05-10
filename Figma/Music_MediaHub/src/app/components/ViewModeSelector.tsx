import { motion } from "motion/react";
import { Grid3x3, List, Disc3, Globe, Layers, Sparkles, Music } from "lucide-react";

export type ViewMode = "grid" | "list" | "songs" | "carousel" | "wall" | "spectrum" | "galaxy";

interface ViewModeSelectorProps {
  currentMode: ViewMode;
  onModeChange: (mode: ViewMode) => void;
}

const viewModes = [
  { id: "grid" as ViewMode, icon: Grid3x3, label: "Grid View" },
  { id: "songs" as ViewMode, icon: Music, label: "All Songs" },
  { id: "list" as ViewMode, icon: List, label: "List View" },
  { id: "carousel" as ViewMode, icon: Disc3, label: "3D Carousel" },
  { id: "wall" as ViewMode, icon: Layers, label: "Holographic Wall" },
  { id: "spectrum" as ViewMode, icon: Sparkles, label: "Wave Spectrum" },
  { id: "galaxy" as ViewMode, icon: Globe, label: "Music Galaxy" },
];

export function ViewModeSelector({ currentMode, onModeChange }: ViewModeSelectorProps) {
  return (
    <div className="flex items-center gap-2 p-1 rounded-2xl bg-white/5 backdrop-blur-xl border border-white/10">
      {viewModes.map((mode) => {
        const Icon = mode.icon;
        const isActive = currentMode === mode.id;
        
        return (
          <motion.button
            key={mode.id}
            className={`relative px-4 py-2 rounded-xl flex items-center gap-2 transition-all ${
              isActive
                ? "text-white"
                : "text-gray-400 hover:text-white"
            }`}
            whileHover={{ scale: 1.05 }}
            whileTap={{ scale: 0.95 }}
            onClick={() => onModeChange(mode.id)}
          >
            {isActive && (
              <motion.div
                className="absolute inset-0 bg-gradient-to-r from-cyan-500/20 to-purple-500/20 rounded-xl border border-cyan-500/30"
                layoutId="activeViewMode"
                transition={{ type: "spring", bounce: 0.2, duration: 0.6 }}
              />
            )}
            
            <Icon className={`w-4 h-4 relative z-10 ${isActive ? "text-cyan-400" : ""}`} />
            <span className="text-sm font-medium relative z-10 hidden lg:inline">
              {mode.label}
            </span>
          </motion.button>
        );
      })}
    </div>
  );
}