import { ChevronRight } from "lucide-react";
import { motion } from "motion/react";
import { ReactNode } from "react";

interface DiscoveryRowProps {
  title: string;
  children: ReactNode;
}

export function DiscoveryRow({ title, children }: DiscoveryRowProps) {
  return (
    <div className="mb-10">
      {/* Row header */}
      <div className="flex items-center justify-between mb-4 px-6">
        <div className="flex items-center gap-3">
          <div className="w-1 h-6 bg-gradient-to-b from-cyan-400 to-blue-500 rounded-full shadow-[0_0_10px_rgba(0,217,255,0.5)]" />
          <h2 className="text-xl font-semibold text-white">{title}</h2>
        </div>
        
        <motion.button
          whileHover={{ x: 5 }}
          className="flex items-center gap-1 text-sm text-cyan-400 hover:text-cyan-300 transition-colors group"
        >
          <span>View All</span>
          <ChevronRight className="w-4 h-4 group-hover:translate-x-1 transition-transform" />
        </motion.button>
      </div>

      {/* Scrollable content */}
      <div className="relative px-6">
        <div className="flex gap-4 overflow-x-auto scrollbar-hide pb-2">
          {children}
        </div>
        
        {/* Fade gradient on right edge */}
        <div className="absolute right-0 top-0 bottom-0 w-20 bg-gradient-to-l from-slate-950 to-transparent pointer-events-none" />
      </div>
    </div>
  );
}
