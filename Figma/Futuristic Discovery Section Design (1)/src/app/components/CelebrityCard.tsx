import { motion } from "motion/react";
import { Star, Sparkles } from "lucide-react";

interface CelebrityCardProps {
  name: string;
  image: string;
  role: string;
  trending?: boolean;
  onClick?: () => void;
}

export function CelebrityCard({ name, image, role, trending, onClick }: CelebrityCardProps) {
  return (
    <motion.div
      whileHover={{ scale: 1.05, y: -5 }}
      whileTap={{ scale: 0.98 }}
      onClick={onClick}
      className="relative group cursor-pointer w-[200px] shrink-0"
    >
      <div className="relative overflow-hidden rounded-xl bg-slate-900/50 backdrop-blur-sm border border-slate-700/30 group-hover:border-cyan-400/50 transition-all duration-300 shadow-lg group-hover:shadow-[0_0_25px_rgba(0,217,255,0.25)]">
        {/* Portrait */}
        <div className="relative aspect-square overflow-hidden">
          <img
            src={image}
            alt={name}
            className="w-full h-full object-cover transition-transform duration-500 group-hover:scale-110"
          />
          
          {/* Gradient overlay */}
          <div className="absolute inset-0 bg-gradient-to-t from-slate-950 via-transparent to-transparent opacity-80" />
          
          {/* Trending indicator */}
          {trending && (
            <div className="absolute top-2 right-2 p-2 rounded-full bg-cyan-500/80 backdrop-blur-sm shadow-[0_0_15px_rgba(0,217,255,0.6)]">
              <Sparkles className="w-4 h-4 text-white" />
            </div>
          )}

          {/* Name overlay at bottom */}
          <div className="absolute bottom-0 left-0 right-0 p-3">
            <h3 className="text-sm font-medium text-white mb-0.5 group-hover:text-cyan-300 transition-colors">
              {name}
            </h3>
            <p className="text-xs text-slate-400">{role}</p>
          </div>
        </div>

        {/* Glowing effect */}
        <div className="absolute inset-0 rounded-xl opacity-0 group-hover:opacity-100 transition-opacity pointer-events-none">
          <div className="absolute inset-0 rounded-xl shadow-[inset_0_0_20px_rgba(0,217,255,0.2)]" />
        </div>
      </div>
    </motion.div>
  );
}
