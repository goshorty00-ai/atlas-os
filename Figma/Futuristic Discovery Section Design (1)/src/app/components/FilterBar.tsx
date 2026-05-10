import { useState } from "react";
import { motion } from "motion/react";

const filters = ["Trending", "Trailers", "News", "Upcoming", "Celebrities", "Games", "Music", "Movies", "All"];

export function FilterBar() {
  const [activeFilter, setActiveFilter] = useState("All");

  return (
    <div className="px-6 py-4">
      <div className="flex items-center gap-3 overflow-x-auto scrollbar-hide">
        {filters.map((filter) => (
          <motion.button
            key={filter}
            whileHover={{ scale: 1.05 }}
            whileTap={{ scale: 0.95 }}
            onClick={() => setActiveFilter(filter)}
            className={`relative px-5 py-2 rounded-full text-sm font-medium whitespace-nowrap transition-all ${
              activeFilter === filter
                ? "text-cyan-300 shadow-[0_0_20px_rgba(0,217,255,0.4)]"
                : "text-slate-400 hover:text-cyan-300"
            }`}
          >
            {/* Glass background */}
            <div className={`absolute inset-0 rounded-full backdrop-blur-md transition-all ${
              activeFilter === filter
                ? "bg-cyan-500/20 border-2 border-cyan-400/50"
                : "bg-slate-800/30 border border-slate-700/50 hover:border-cyan-500/30"
            }`} />
            
            {/* Glow effect for active filter */}
            {activeFilter === filter && (
              <motion.div
                layoutId="activeFilterGlow"
                className="absolute inset-0 rounded-full bg-gradient-to-r from-cyan-500/30 to-blue-500/30 blur-lg"
                transition={{ type: "spring", bounce: 0.2, duration: 0.6 }}
              />
            )}
            
            <span className="relative z-10">{filter}</span>
          </motion.button>
        ))}
      </div>
    </div>
  );
}
