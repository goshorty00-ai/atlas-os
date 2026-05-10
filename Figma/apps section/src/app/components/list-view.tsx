import { motion } from 'motion/react';
import { Play, Flame, Plus } from 'lucide-react';
import type { StreamingApp } from './streaming-data';

interface ListViewProps {
  apps: StreamingApp[];
  onAppSelect: (app: StreamingApp) => void;
}

export function ListView({ apps, onAppSelect }: ListViewProps) {
  // Group apps by category
  const groupedApps = apps.reduce((acc, app) => {
    if (!acc[app.category]) {
      acc[app.category] = [];
    }
    acc[app.category].push(app);
    return acc;
  }, {} as Record<string, StreamingApp[]>);

  return (
    <div className="space-y-8 pb-8">
      {Object.entries(groupedApps).map(([category, categoryApps]) => (
        <div key={category}>
          <div className="text-sm text-cyan-400 font-medium mb-4 flex items-center gap-2">
            <div className="h-px flex-1 bg-gradient-to-r from-cyan-500/50 to-transparent" />
            <span>{category}</span>
            <div className="h-px flex-1 bg-gradient-to-l from-cyan-500/50 to-transparent" />
          </div>

          <div className="space-y-2">
            {categoryApps.map((app, index) => (
              <motion.div
                key={app.id}
                initial={{ opacity: 0, x: -20 }}
                animate={{ opacity: 1, x: 0 }}
                transition={{ delay: index * 0.02 }}
                onClick={() => onAppSelect(app)}
                className="group relative backdrop-blur-md bg-white/5 border border-white/10 rounded-xl p-4 hover:bg-white/10 hover:border-cyan-500/50 transition-all cursor-pointer"
              >
                <div className="flex items-center gap-4">
                  {/* App Logo */}
                  <div
                    className="relative w-16 h-16 rounded-lg overflow-hidden flex-shrink-0"
                    style={{
                      background: `linear-gradient(135deg, ${app.color}30 0%, ${app.color}15 100%)`,
                    }}
                  >
                    <div className="absolute inset-0 flex items-center justify-center p-3 backdrop-blur-sm bg-black/40">
                      <img
                        src={app.logoUrl}
                        alt={app.name}
                        className="w-full h-full object-contain"
                      />
                    </div>
                  </div>

                  {/* App Info */}
                  <div className="flex-1 min-w-0">
                    <div className="text-white font-medium">{app.name}</div>
                    <div className="text-sm text-gray-400 mt-1 line-clamp-1">
                      {app.description}
                    </div>
                  </div>

                  {/* AI Indicators */}
                  <div className="flex items-center gap-2">
                    {app.trending && (
                      <div className="flex items-center gap-1 px-2 py-1 rounded-full bg-red-500/20 border border-red-500/30">
                        <Flame className="w-3 h-3 text-red-400" />
                        <span className="text-xs text-red-400">Trending</span>
                      </div>
                    )}
                    {app.continueWatching && (
                      <div className="flex items-center gap-1 px-2 py-1 rounded-full bg-cyan-500/20 border border-cyan-500/30">
                        <Play className="w-3 h-3 text-cyan-400" />
                        <span className="text-xs text-cyan-400">Continue</span>
                      </div>
                    )}
                    {app.newReleases && (
                      <div className="flex items-center gap-1 px-2 py-1 rounded-full bg-purple-500/20 border border-purple-500/30">
                        <Plus className="w-3 h-3 text-purple-400" />
                        <span className="text-xs text-purple-400">New</span>
                      </div>
                    )}
                  </div>

                  {/* Quick Launch Button */}
                  <button
                    className="px-4 py-2 rounded-lg bg-cyan-500/20 border border-cyan-500/50 text-cyan-400 text-sm font-medium hover:bg-cyan-500/30 transition-colors opacity-0 group-hover:opacity-100"
                    onClick={(e) => {
                      e.stopPropagation();
                      onAppSelect(app);
                    }}
                  >
                    Launch
                  </button>
                </div>

                {/* Hover Glow */}
                <div
                  className="absolute inset-0 rounded-xl opacity-0 group-hover:opacity-100 transition-opacity pointer-events-none"
                  style={{
                    boxShadow: `inset 0 0 40px ${app.color}20`,
                  }}
                />
              </motion.div>
            ))}
          </div>
        </div>
      ))}
    </div>
  );
}
