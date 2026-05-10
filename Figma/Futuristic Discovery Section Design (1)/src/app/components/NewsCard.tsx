import { motion } from "motion/react";
import { Clock, TrendingUp } from "lucide-react";

interface NewsCardProps {
  headline: string;
  image: string;
  preview: string;
  timeAgo: string;
  trending?: boolean;
  onClick?: () => void;
}

export function NewsCard({ headline, image, preview, timeAgo, trending, onClick }: NewsCardProps) {
  return (
    <motion.div
      whileHover={{ scale: 1.03, y: -3 }}
      whileTap={{ scale: 0.98 }}
      onClick={onClick}
      className="relative group cursor-pointer w-[320px] shrink-0"
    >
      <div className="relative overflow-hidden rounded-xl bg-slate-900/50 backdrop-blur-sm border border-slate-700/30 group-hover:border-cyan-400/50 transition-all duration-300 shadow-lg group-hover:shadow-[0_0_25px_rgba(0,217,255,0.25)]">
        {/* Image section */}
        <div className="relative h-40 overflow-hidden">
          <img
            src={image}
            alt={headline}
            className="w-full h-full object-cover transition-transform duration-500 group-hover:scale-110"
          />
          <div className="absolute inset-0 bg-gradient-to-t from-slate-950 to-transparent opacity-60" />
          
          {trending && (
            <div className="absolute top-2 right-2 px-2 py-1 rounded-md bg-cyan-500/80 backdrop-blur-sm flex items-center gap-1 shadow-[0_0_15px_rgba(0,217,255,0.5)]">
              <TrendingUp className="w-3 h-3 text-white" />
              <span className="text-xs font-bold text-white">TRENDING</span>
            </div>
          )}
        </div>

        {/* Content section */}
        <div className="p-4">
          <h3 className="text-base font-medium text-white mb-2 line-clamp-2 group-hover:text-cyan-300 transition-colors">
            {headline}
          </h3>
          
          <p className="text-sm text-slate-400 mb-3 line-clamp-2 leading-relaxed">
            {preview}
          </p>

          <div className="flex items-center gap-2 text-slate-500">
            <Clock className="w-3.5 h-3.5" />
            <span className="text-xs">{timeAgo}</span>
          </div>
        </div>
      </div>
    </motion.div>
  );
}
