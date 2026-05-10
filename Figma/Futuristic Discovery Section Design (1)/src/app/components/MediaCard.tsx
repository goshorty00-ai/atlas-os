import { motion } from "motion/react";
import { Play, Star, Clock } from "lucide-react";

interface MediaCardProps {
  title: string;
  image: string;
  rating?: number;
  type?: "movie" | "tv" | "game" | "music" | "trailer" | "news" | "celebrity";
  releaseDate?: string;
  isNew?: boolean;
  onClick?: () => void;
}

export function MediaCard({ title, image, rating, type, releaseDate, isNew, onClick }: MediaCardProps) {
  return (
    <motion.div
      whileHover={{ scale: 1.05, y: -5 }}
      whileTap={{ scale: 0.98 }}
      onClick={onClick}
      className="relative group cursor-pointer"
    >
      {/* Card container with glass effect */}
      <div className="relative overflow-hidden rounded-xl bg-slate-900/50 backdrop-blur-sm border border-slate-700/30 group-hover:border-cyan-400/50 transition-all duration-300 shadow-lg group-hover:shadow-[0_0_30px_rgba(0,217,255,0.3)]">
        {/* Image container */}
        <div className="relative aspect-[2/3] overflow-hidden">
          <img
            src={image}
            alt={title}
            className="w-full h-full object-cover transition-transform duration-500 group-hover:scale-110"
          />
          
          {/* Gradient overlay */}
          <div className="absolute inset-0 bg-gradient-to-t from-slate-950 via-slate-950/40 to-transparent opacity-60 group-hover:opacity-80 transition-opacity" />
          
          {/* Play button overlay */}
          {type === "trailer" && (
            <motion.div
              initial={{ opacity: 0, scale: 0.8 }}
              whileHover={{ opacity: 1, scale: 1 }}
              className="absolute inset-0 flex items-center justify-center"
            >
              <div className="w-16 h-16 rounded-full bg-cyan-500/30 backdrop-blur-md border-2 border-cyan-400/50 flex items-center justify-center shadow-[0_0_30px_rgba(0,217,255,0.5)]">
                <Play className="w-8 h-8 text-cyan-300 fill-cyan-300 ml-1" />
              </div>
            </motion.div>
          )}

          {/* New badge */}
          {isNew && (
            <div className="absolute top-2 right-2 px-2 py-1 rounded-md bg-cyan-500/80 backdrop-blur-sm shadow-[0_0_15px_rgba(0,217,255,0.6)]">
              <span className="text-xs font-bold text-white">NEW</span>
            </div>
          )}

          {/* Type badge */}
          {type && type !== "movie" && type !== "tv" && (
            <div className="absolute top-2 left-2 px-2 py-1 rounded-md bg-slate-900/80 backdrop-blur-md border border-slate-700/50">
              <span className="text-xs font-medium text-cyan-300 uppercase tracking-wider">{type}</span>
            </div>
          )}
        </div>

        {/* Info section */}
        <div className="p-3">
          <h3 className="text-sm font-medium text-white mb-2 line-clamp-2 group-hover:text-cyan-300 transition-colors">
            {title}
          </h3>
          
          <div className="flex items-center gap-3">
            {rating && (
              <div className="flex items-center gap-1">
                <Star className="w-3.5 h-3.5 text-yellow-400 fill-yellow-400" />
                <span className="text-xs font-medium text-yellow-300">{rating}</span>
              </div>
            )}
            
            {releaseDate && (
              <div className="flex items-center gap-1 text-slate-400">
                <Clock className="w-3.5 h-3.5" />
                <span className="text-xs">{releaseDate}</span>
              </div>
            )}
          </div>
        </div>

        {/* Glowing border effect on hover */}
        <div className="absolute inset-0 rounded-xl opacity-0 group-hover:opacity-100 transition-opacity pointer-events-none">
          <div className="absolute inset-0 rounded-xl shadow-[inset_0_0_20px_rgba(0,217,255,0.2)]" />
        </div>
      </div>
    </motion.div>
  );
}
