import { useState } from 'react';
import { motion } from 'motion/react';
import { Flame, Play, Plus } from 'lucide-react';
import type { StreamingApp } from './streaming-data';

interface GridViewProps {
  apps: StreamingApp[];
  onAppSelect: (app: StreamingApp) => void;
}

export function GridView({ apps, onAppSelect }: GridViewProps) {
  const [hoveredApp, setHoveredApp] = useState<string | null>(null);

  return (
    <div className="grid grid-cols-6 gap-4 pb-8">
      {apps.map((app, index) => (
        <motion.div
          key={app.id}
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: index * 0.02 }}
          onHoverStart={() => setHoveredApp(app.id)}
          onHoverEnd={() => setHoveredApp(null)}
          onClick={() => onAppSelect(app)}
          className="relative cursor-pointer group"
        >
          <div
            className={`relative aspect-square rounded-2xl overflow-hidden transition-all duration-300 ${
              hoveredApp === app.id
                ? 'scale-105 shadow-[0_0_40px_rgba(6,182,212,0.4)]'
                : 'scale-100'
            }`}
            style={{
              background: `linear-gradient(135deg, ${app.color}20 0%, ${app.color}10 100%)`,
            }}
          >
            {/* App Logo */}
            <div className="absolute inset-0 flex items-center justify-center p-6 backdrop-blur-sm bg-black/40">
              <img
                src={app.logoUrl}
                alt={app.name}
                className="w-full h-full object-contain"
              />
            </div>

            {/* Glow Effect */}
            {hoveredApp === app.id && (
              <motion.div
                initial={{ opacity: 0 }}
                animate={{ opacity: 1 }}
                className="absolute inset-0 rounded-2xl"
                style={{
                  boxShadow: `inset 0 0 60px ${app.color}40`,
                  border: `1px solid ${app.color}60`,
                }}
              />
            )}

            {/* AI Indicators */}
            <div className="absolute top-2 right-2 flex flex-col gap-1">
              {app.trending && (
                <div className="w-6 h-6 rounded-full bg-red-500/80 backdrop-blur-sm flex items-center justify-center">
                  <Flame className="w-3 h-3 text-white" />
                </div>
              )}
              {app.continueWatching && (
                <div className="w-6 h-6 rounded-full bg-cyan-500/80 backdrop-blur-sm flex items-center justify-center">
                  <Play className="w-3 h-3 text-white" />
                </div>
              )}
              {app.newReleases && (
                <div className="w-6 h-6 rounded-full bg-purple-500/80 backdrop-blur-sm flex items-center justify-center">
                  <Plus className="w-3 h-3 text-white" />
                </div>
              )}
            </div>

            {/* Hover Overlay - Trending Content */}
            {hoveredApp === app.id && app.trendingContent.length > 0 && (
              <motion.div
                initial={{ opacity: 0, y: 10 }}
                animate={{ opacity: 1, y: 0 }}
                className="absolute inset-0 flex items-end p-3"
              >
                <div className="w-full backdrop-blur-md bg-black/60 rounded-lg p-2">
                  <div className="text-xs text-cyan-400 mb-1">Trending</div>
                  <div className="text-xs text-white/80 truncate">
                    {app.trendingContent[0].title}
                  </div>
                </div>
              </motion.div>
            )}
          </div>

          {/* App Name */}
          <div className="mt-2 text-center">
            <div className="text-sm text-white font-medium">{app.name}</div>
            <div className="text-xs text-gray-400 mt-0.5">{app.category}</div>
          </div>
        </motion.div>
      ))}
    </div>
  );
}
