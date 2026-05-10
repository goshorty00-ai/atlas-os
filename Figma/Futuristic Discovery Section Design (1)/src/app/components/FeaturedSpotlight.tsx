import { motion } from "motion/react";
import { Play, Plus, ExternalLink, Star } from "lucide-react";

interface FeaturedSpotlightProps {
  onOpenInfo: () => void;
}

export function FeaturedSpotlight({ onOpenInfo }: FeaturedSpotlightProps) {
  return (
    <div className="relative px-6 pb-8">
      <motion.div
        initial={{ opacity: 0, y: 20 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.6 }}
        className="relative overflow-hidden rounded-2xl"
      >
        {/* Background image with gradient overlay */}
        <div className="relative h-[500px]">
          <img
            src="https://images.unsplash.com/photo-1758232589439-f5ec09dc92c2?crop=entropy&cs=tinysrgb&fit=max&fm=jpg&ixid=M3w3Nzg4Nzd8MHwxfHNlYXJjaHwxfHxjaW5lbWF0aWMlMjBtb3ZpZSUyMHRoZWF0ZXIlMjBzY2VuZXxlbnwxfHx8fDE3NzI4MDI5NjF8MA&ixlib=rb-4.1.0&q=80&w=1080"
            alt="Featured"
            className="w-full h-full object-cover"
          />
          
          {/* Gradient overlays */}
          <div className="absolute inset-0 bg-gradient-to-t from-slate-950 via-slate-950/60 to-transparent" />
          <div className="absolute inset-0 bg-gradient-to-r from-slate-950 via-transparent to-slate-950/80" />
          
          {/* Grid texture */}
          <div className="absolute inset-0 opacity-5" style={{
            backgroundImage: `linear-gradient(rgba(0, 217, 255, 0.3) 1px, transparent 1px),
                              linear-gradient(90deg, rgba(0, 217, 255, 0.3) 1px, transparent 1px)`,
            backgroundSize: '30px 30px'
          }} />
        </div>

        {/* Content overlay */}
        <div className="absolute inset-0 flex flex-col justify-end p-10">
          {/* Glowing border accent */}
          <div className="absolute top-0 left-0 w-1 h-32 bg-gradient-to-b from-cyan-400 to-transparent shadow-[0_0_20px_rgba(0,217,255,0.6)]" />
          
          <motion.div
            initial={{ opacity: 0, y: 30 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ delay: 0.3, duration: 0.6 }}
            className="max-w-2xl"
          >
            {/* Category badge */}
            <div className="inline-flex items-center gap-2 px-4 py-1.5 mb-4 rounded-full bg-cyan-500/20 backdrop-blur-md border border-cyan-400/30 shadow-[0_0_15px_rgba(0,217,255,0.3)]">
              <span className="w-2 h-2 bg-cyan-400 rounded-full animate-pulse" />
              <span className="text-xs font-medium text-cyan-300 tracking-wider">FEATURED SPOTLIGHT</span>
            </div>

            <h1 className="text-5xl font-bold text-white mb-4 tracking-tight">
              Quantum Realm: Beyond Time
            </h1>
            
            <div className="flex items-center gap-4 mb-4">
              <div className="flex items-center gap-1.5 px-3 py-1 rounded-lg bg-yellow-500/20 backdrop-blur-sm border border-yellow-400/30">
                <Star className="w-4 h-4 text-yellow-400 fill-yellow-400" />
                <span className="text-sm font-medium text-yellow-300">9.2</span>
              </div>
              <span className="text-slate-300 text-sm">2026</span>
              <span className="text-slate-400">•</span>
              <span className="text-slate-300 text-sm">Sci-Fi Action</span>
              <span className="text-slate-400">•</span>
              <span className="text-slate-300 text-sm">2h 35m</span>
            </div>

            <p className="text-slate-300 text-base mb-8 max-w-xl leading-relaxed">
              When the fabric of reality begins to tear, a team of quantum scientists must venture beyond 
              the boundaries of time itself to prevent the collapse of all existence. An epic journey through 
              parallel dimensions awaits.
            </p>

            {/* Action buttons */}
            <div className="flex items-center gap-4">
              <motion.button
                whileHover={{ scale: 1.05 }}
                whileTap={{ scale: 0.95 }}
                className="group relative px-8 py-3 rounded-xl overflow-hidden"
              >
                <div className="absolute inset-0 bg-gradient-to-r from-cyan-500 to-blue-500 shadow-[0_0_30px_rgba(0,217,255,0.5)]" />
                <div className="absolute inset-0 bg-gradient-to-r from-cyan-400 to-blue-400 opacity-0 group-hover:opacity-100 transition-opacity" />
                <div className="relative flex items-center gap-2 text-white font-medium">
                  <Play className="w-5 h-5 fill-white" />
                  Watch Trailer
                </div>
              </motion.button>

              <motion.button
                whileHover={{ scale: 1.05 }}
                whileTap={{ scale: 0.95 }}
                className="group relative px-8 py-3 rounded-xl overflow-hidden"
              >
                <div className="absolute inset-0 bg-slate-800/50 backdrop-blur-md border border-cyan-400/30 group-hover:border-cyan-400/50 transition-colors" />
                <div className="relative flex items-center gap-2 text-cyan-300 font-medium">
                  <Plus className="w-5 h-5" />
                  Add to Library
                </div>
              </motion.button>

              <motion.button
                whileHover={{ scale: 1.05 }}
                whileTap={{ scale: 0.95 }}
                onClick={onOpenInfo}
                className="group relative px-6 py-3 rounded-xl overflow-hidden"
              >
                <div className="absolute inset-0 bg-slate-800/30 backdrop-blur-sm border border-slate-600/30 group-hover:border-cyan-500/40 transition-colors" />
                <div className="relative flex items-center gap-2 text-slate-300 group-hover:text-cyan-300 transition-colors">
                  <ExternalLink className="w-5 h-5" />
                  More Info
                </div>
              </motion.button>
            </div>
          </motion.div>
        </div>
      </motion.div>
    </div>
  );
}
