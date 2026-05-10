import { motion, AnimatePresence } from 'motion/react';
import { X, Play, Film, Tv, Radio, Clock, TrendingUp } from 'lucide-react';
import type { StreamingApp } from './streaming-data';

interface AppExpansionPanelProps {
  app: StreamingApp;
  onClose: () => void;
  onLaunch: () => void;
}

export function AppExpansionPanel({ app, onClose, onLaunch }: AppExpansionPanelProps) {
  const shortcuts = [
    { icon: TrendingUp, label: 'Trending', color: 'red' },
    { icon: Film, label: 'Movies', color: 'cyan' },
    { icon: Tv, label: 'Series', color: 'purple' },
    { icon: Radio, label: 'Live TV', color: 'pink' },
    { icon: Clock, label: 'Continue Watching', color: 'blue' },
  ];

  return (
    <AnimatePresence>
      <motion.div
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        exit={{ opacity: 0 }}
        onClick={onClose}
        className="fixed inset-0 bg-black/60 backdrop-blur-sm z-50 flex items-center justify-center p-8"
      >
        <motion.div
          initial={{ scale: 0.9, opacity: 0 }}
          animate={{ scale: 1, opacity: 1 }}
          exit={{ scale: 0.9, opacity: 0 }}
          onClick={(e) => e.stopPropagation()}
          className="relative w-full max-w-5xl max-h-[90vh] overflow-hidden rounded-3xl"
        >
          {/* Background Artwork */}
          <div className="absolute inset-0">
            <div
              className="absolute inset-0 bg-cover bg-center"
              style={{
                backgroundImage: app.trendingContent[0]
                  ? `url(${app.trendingContent[0].imageUrl})`
                  : 'none',
              }}
            />
            <div className="absolute inset-0 bg-gradient-to-t from-[#0a0a0f] via-[#0a0a0f]/90 to-[#0a0a0f]/60" />
          </div>

          {/* Content */}
          <div className="relative p-8 flex flex-col gap-6">
            {/* Close Button */}
            <button
              onClick={onClose}
              className="absolute top-4 right-4 w-10 h-10 rounded-full backdrop-blur-md bg-white/10 border border-white/20 flex items-center justify-center text-white hover:bg-white/20 transition-all"
            >
              <X className="w-5 h-5" />
            </button>

            {/* App Header */}
            <div className="flex items-center gap-6">
              <div
                className="w-32 h-32 rounded-2xl overflow-hidden flex-shrink-0 shadow-[0_0_60px_rgba(0,0,0,0.5)]"
                style={{
                  background: `linear-gradient(135deg, ${app.color}40 0%, ${app.color}20 100%)`,
                }}
              >
                <div className="w-full h-full flex items-center justify-center p-6 backdrop-blur-sm bg-black/40">
                  <img
                    src={app.logoUrl}
                    alt={app.name}
                    className="w-full h-full object-contain"
                  />
                </div>
              </div>

              <div className="flex-1">
                <div className="text-4xl text-white font-semibold mb-2">{app.name}</div>
                <div className="text-gray-300 text-lg mb-4">{app.description}</div>
                <div className="flex items-center gap-3">
                  <span className="px-3 py-1 rounded-full bg-cyan-500/20 border border-cyan-500/30 text-cyan-400 text-sm">
                    {app.category}
                  </span>
                  {app.trending && (
                    <span className="px-3 py-1 rounded-full bg-red-500/20 border border-red-500/30 text-red-400 text-sm">
                      Trending
                    </span>
                  )}
                </div>
              </div>
            </div>

            {/* AI Summary */}
            <div className="backdrop-blur-md bg-white/5 border border-white/10 rounded-2xl p-6">
              <div className="text-sm text-cyan-400 font-medium mb-2">AI Platform Activity</div>
              <div className="text-gray-300">
                {app.trending && 'High activity with trending content. '}
                {app.newReleases && 'New releases available. '}
                {app.continueWatching && 'Resume your viewing experience. '}
                Popular among users in your region with {Math.floor(Math.random() * 50) + 20}+ new
                titles this week.
              </div>
            </div>

            {/* Quick Launch Shortcuts */}
            <div>
              <div className="text-sm text-white font-medium mb-4">Quick Launch</div>
              <div className="grid grid-cols-5 gap-3">
                {shortcuts.map((shortcut) => {
                  const Icon = shortcut.icon;
                  return (
                    <button
                      key={shortcut.label}
                      onClick={onLaunch}
                      className={`group relative backdrop-blur-md bg-white/5 border border-white/10 rounded-xl p-4 hover:bg-${shortcut.color}-500/10 hover:border-${shortcut.color}-500/30 transition-all`}
                    >
                      <div className="flex flex-col items-center gap-2">
                        <div
                          className={`w-12 h-12 rounded-full bg-${shortcut.color}-500/20 flex items-center justify-center group-hover:bg-${shortcut.color}-500/30 transition-colors`}
                        >
                          <Icon className={`w-5 h-5 text-${shortcut.color}-400`} />
                        </div>
                        <div className="text-xs text-gray-300 text-center">{shortcut.label}</div>
                      </div>
                    </button>
                  );
                })}
              </div>
            </div>

            {/* Main Launch Button */}
            <button
              onClick={onLaunch}
              className="w-full py-4 rounded-xl bg-gradient-to-r from-cyan-500 to-purple-500 text-white font-medium text-lg hover:shadow-[0_0_40px_rgba(6,182,212,0.5)] transition-all flex items-center justify-center gap-2"
            >
              <Play className="w-5 h-5" />
              Open {app.name}
            </button>
          </div>
        </motion.div>
      </motion.div>
    </AnimatePresence>
  );
}
