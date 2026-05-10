import { motion, AnimatePresence } from "motion/react";
import { X, Play, Plus, Star, Calendar, Clock } from "lucide-react";

interface InfoPanelProps {
  isOpen: boolean;
  onClose: () => void;
  media?: {
    title: string;
    image: string;
    rating: number;
    releaseDate: string;
    runtime: string;
    description: string;
    genres: string[];
    type: string;
  };
}

export function InfoPanel({ isOpen, onClose, media }: InfoPanelProps) {
  if (!media) return null;

  return (
    <AnimatePresence>
      {isOpen && (
        <>
          {/* Backdrop */}
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            onClick={onClose}
            className="fixed inset-0 bg-black/60 backdrop-blur-sm z-40"
          />

          {/* Panel */}
          <motion.div
            initial={{ x: "100%" }}
            animate={{ x: 0 }}
            exit={{ x: "100%" }}
            transition={{ type: "spring", damping: 30, stiffness: 300 }}
            className="fixed right-0 top-0 bottom-0 w-full max-w-md bg-slate-950 border-l border-cyan-400/20 shadow-[-10px_0_50px_rgba(0,217,255,0.15)] z-50 overflow-y-auto"
          >
            {/* Grid texture overlay */}
            <div className="absolute inset-0 opacity-5 pointer-events-none" style={{
              backgroundImage: `linear-gradient(rgba(0, 217, 255, 0.3) 1px, transparent 1px),
                                linear-gradient(90deg, rgba(0, 217, 255, 0.3) 1px, transparent 1px)`,
              backgroundSize: '20px 20px'
            }} />

            {/* Content */}
            <div className="relative">
              {/* Close button */}
              <button
                onClick={onClose}
                className="absolute top-4 right-4 z-10 p-2 rounded-lg bg-slate-900/80 backdrop-blur-md border border-slate-700/50 hover:border-cyan-400/50 text-slate-400 hover:text-cyan-300 transition-all"
              >
                <X className="w-5 h-5" />
              </button>

              {/* Poster image */}
              <div className="relative h-96">
                <img
                  src={media.image}
                  alt={media.title}
                  className="w-full h-full object-cover"
                />
                <div className="absolute inset-0 bg-gradient-to-t from-slate-950 via-slate-950/40 to-transparent" />
                
                {/* Glowing top border */}
                <div className="absolute top-0 left-0 right-0 h-1 bg-gradient-to-r from-transparent via-cyan-400 to-transparent shadow-[0_0_20px_rgba(0,217,255,0.6)]" />
              </div>

              {/* Info content */}
              <div className="p-6 space-y-6">
                {/* Title and type */}
                <div>
                  <div className="inline-flex items-center gap-2 px-3 py-1 mb-3 rounded-full bg-cyan-500/20 backdrop-blur-md border border-cyan-400/30">
                    <span className="text-xs font-medium text-cyan-300 uppercase tracking-wider">{media.type}</span>
                  </div>
                  <h2 className="text-3xl font-bold text-white mb-3">{media.title}</h2>
                  
                  {/* Meta info */}
                  <div className="flex flex-wrap items-center gap-4 text-sm">
                    <div className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-yellow-500/20 backdrop-blur-sm border border-yellow-400/30">
                      <Star className="w-4 h-4 text-yellow-400 fill-yellow-400" />
                      <span className="font-medium text-yellow-300">{media.rating}</span>
                    </div>
                    <div className="flex items-center gap-1.5 text-slate-300">
                      <Calendar className="w-4 h-4 text-cyan-400" />
                      <span>{media.releaseDate}</span>
                    </div>
                    <div className="flex items-center gap-1.5 text-slate-300">
                      <Clock className="w-4 h-4 text-cyan-400" />
                      <span>{media.runtime}</span>
                    </div>
                  </div>
                </div>

                {/* Description */}
                <div>
                  <h3 className="text-sm font-semibold text-cyan-300 mb-2 tracking-wider uppercase">Overview</h3>
                  <p className="text-slate-300 leading-relaxed">{media.description}</p>
                </div>

                {/* Genres */}
                <div>
                  <h3 className="text-sm font-semibold text-cyan-300 mb-3 tracking-wider uppercase">Genres</h3>
                  <div className="flex flex-wrap gap-2">
                    {media.genres.map((genre) => (
                      <span
                        key={genre}
                        className="px-3 py-1.5 rounded-lg bg-slate-800/50 backdrop-blur-sm border border-slate-700/50 text-sm text-slate-300"
                      >
                        {genre}
                      </span>
                    ))}
                  </div>
                </div>

                {/* Action buttons */}
                <div className="space-y-3 pt-4">
                  <motion.button
                    whileHover={{ scale: 1.02 }}
                    whileTap={{ scale: 0.98 }}
                    className="w-full group relative px-6 py-3 rounded-xl overflow-hidden"
                  >
                    <div className="absolute inset-0 bg-gradient-to-r from-cyan-500 to-blue-500 shadow-[0_0_25px_rgba(0,217,255,0.4)]" />
                    <div className="absolute inset-0 bg-gradient-to-r from-cyan-400 to-blue-400 opacity-0 group-hover:opacity-100 transition-opacity" />
                    <div className="relative flex items-center justify-center gap-2 text-white font-medium">
                      <Play className="w-5 h-5 fill-white" />
                      Watch Trailer
                    </div>
                  </motion.button>

                  <motion.button
                    whileHover={{ scale: 1.02 }}
                    whileTap={{ scale: 0.98 }}
                    className="w-full group relative px-6 py-3 rounded-xl overflow-hidden"
                  >
                    <div className="absolute inset-0 bg-slate-800/50 backdrop-blur-md border border-cyan-400/30 group-hover:border-cyan-400/50 transition-colors" />
                    <div className="relative flex items-center justify-center gap-2 text-cyan-300 font-medium">
                      <Plus className="w-5 h-5" />
                      Add to Library
                    </div>
                  </motion.button>

                  <motion.button
                    whileHover={{ scale: 1.02 }}
                    whileTap={{ scale: 0.98 }}
                    className="w-full group relative px-6 py-3 rounded-xl overflow-hidden"
                  >
                    <div className="absolute inset-0 bg-slate-800/30 backdrop-blur-sm border border-slate-600/30 group-hover:border-cyan-500/40 transition-colors" />
                    <div className="relative flex items-center justify-center gap-2 text-slate-300 group-hover:text-cyan-300 transition-colors font-medium">
                      <Play className="w-5 h-5" />
                      Play Now
                    </div>
                  </motion.button>
                </div>
              </div>
            </div>
          </motion.div>
        </>
      )}
    </AnimatePresence>
  );
}
