import { motion } from 'motion/react';
import { Play, Pause, SkipBack, SkipForward, Volume2, Activity, Maximize2, Waves, Grid3x3, Layers, List } from 'lucide-react';
import { Track, Album } from '../data/albums';
import { useState } from 'react';

type ViewMode = 'grid' | 'carousel' | 'list';

interface MediaBarProps {
  currentTrack: Track | null;
  currentAlbum: Album | null;
  isPlaying: boolean;
  onPlayPause: () => void;
  onNext: () => void;
  onPrevious: () => void;
  volume: number;
  onVolumeChange: (volume: number) => void;
  totalSeconds: number;
  onSeekToSeconds: (seconds: number) => void;
  onOpenVisualizer: () => void;
  visualizerType: 'waveform' | 'circular' | 'particles' | 'bars' | 'blob' | 'spectrum' | 'neonGrid';
  onVisualizerTypeChange: (type: 'waveform' | 'circular' | 'particles' | 'bars' | 'blob' | 'spectrum' | 'neonGrid') => void;
  aiActive: boolean;
  progress: number;
  currentTimeText: string;
  totalTimeText: string;
  viewMode: ViewMode;
  onViewModeChange: (mode: ViewMode) => void;
}

export function MediaBar({
  currentTrack,
  currentAlbum,
  isPlaying,
  onPlayPause,
  onNext,
  onPrevious,
  volume,
  onVolumeChange,
  totalSeconds,
  onSeekToSeconds,
  onOpenVisualizer,
  visualizerType,
  onVisualizerTypeChange,
  aiActive,
  progress,
  currentTimeText,
  totalTimeText,
  viewMode,
  onViewModeChange,
}: MediaBarProps) {
  const [showVisualizerMenu, setShowVisualizerMenu] = useState(false);

  const visualizerTypes = [
    { id: 'waveform', label: 'Waveform', icon: Waves },
    { id: 'circular', label: 'Circular', icon: Activity },
    { id: 'particles', label: 'Particles', icon: Waves },
    { id: 'bars', label: 'Bars', icon: Activity },
    { id: 'blob', label: 'Blob', icon: Waves },
    { id: 'spectrum', label: 'Spectrum', icon: Activity },
    { id: 'neonGrid', label: 'Neon Grid', icon: Waves },
  ];

  return (
    <motion.div
      initial={{ y: 100 }}
      animate={{ y: 0 }}
      className="fixed bottom-0 left-0 right-0 h-14 bg-black/40 backdrop-blur-2xl border-t border-white/10 z-40"
      style={{
        boxShadow: '0 -4px 30px rgba(0, 0, 0, 0.3)',
      }}
    >
      {/* Progress bar */}
      <div
        className="absolute top-0 left-0 right-0 h-1 bg-white/5 cursor-pointer"
        onClick={(e) => {
          try {
            if (!currentTrack || !totalSeconds || totalSeconds <= 0) return;
            const rect = (e.currentTarget as HTMLDivElement).getBoundingClientRect();
            const ratio = rect.width > 0 ? (e.clientX - rect.left) / rect.width : 0;
            const sec = Math.max(0, Math.min(totalSeconds, ratio * totalSeconds));
            onSeekToSeconds(sec);
          } catch {
          }
        }}
      >
        <motion.div
          className="h-full bg-gradient-to-r from-[#3B82F6] to-[#8B5CF6] relative"
          style={{ width: `${progress}%` }}
        >
          <div
            className="absolute right-0 top-1/2 -translate-y-1/2 w-3 h-3 rounded-full bg-white"
            style={{
              boxShadow: '0 0 20px rgba(59, 130, 246, 0.8)',
            }}
          />
        </motion.div>
      </div>

      <div className="h-full flex items-center justify-between px-4">
        {/* Left - Track info */}
        <div className="flex items-center gap-4 flex-1 min-w-0">
          {currentAlbum && (
            <div className="w-10 h-10 rounded-lg overflow-hidden flex-shrink-0 bg-gradient-to-br from-white/10 to-white/0 border border-white/10">
              <img
                src={currentAlbum.cover}
                alt={currentAlbum.title}
                className="w-full h-full object-cover"
              />
            </div>
          )}
          {currentTrack ? (
            <div className="min-w-0">
              <div className="text-white truncate">{currentTrack.title}</div>
              <div className="text-sm text-white/60 truncate">
                {currentAlbum?.artist}
              </div>
            </div>
          ) : (
            <div className="text-white/40">No track selected</div>
          )}
        </div>

        {/* Center - Playback controls */}
        <div className="flex items-center gap-4">
          <div className="flex items-center gap-2">
            <button
              onClick={onPrevious}
              disabled={!currentTrack}
              className="w-9 h-9 rounded-full bg-white/5 hover:bg-white/10 disabled:opacity-30 disabled:cursor-not-allowed flex items-center justify-center text-white/70 hover:text-white transition-all duration-300"
            >
              <SkipBack className="w-5 h-5" />
            </button>

            <button
              onClick={onPlayPause}
              disabled={!currentTrack}
              className="w-10 h-10 rounded-full bg-gradient-to-r from-[#3B82F6] to-[#8B5CF6] hover:from-[#3B82F6]/80 hover:to-[#8B5CF6]/80 disabled:opacity-30 disabled:cursor-not-allowed flex items-center justify-center text-white transition-all duration-300"
              style={{
                boxShadow: '0 0 30px rgba(59, 130, 246, 0.4)',
              }}
            >
              {isPlaying ? (
                <Pause className="w-5 h-5" fill="currentColor" />
              ) : (
                <Play className="w-5 h-5 ml-0.5" fill="currentColor" />
              )}
            </button>

            <button
              onClick={onNext}
              disabled={!currentTrack}
              className="w-9 h-9 rounded-full bg-white/5 hover:bg-white/10 disabled:opacity-30 disabled:cursor-not-allowed flex items-center justify-center text-white/70 hover:text-white transition-all duration-300"
            >
              <SkipForward className="w-5 h-5" />
            </button>
          </div>

          {/* Time display */}
          {currentTrack && (
            <div className="flex items-center gap-2 text-xs text-white/60 min-w-[92px]">
              <span>{currentTimeText}</span>
              <span>/</span>
              <span>{totalTimeText || currentTrack.duration}</span>
            </div>
          )}
        </div>

        {/* Right - Additional controls */}
        <div className="flex items-center gap-3 flex-1 justify-end">
          {/* AI Activity indicator */}
          {aiActive && (
            <div className="flex items-center gap-2 px-3 py-1.5 rounded-lg bg-gradient-to-r from-[#3B82F6]/20 to-[#8B5CF6]/20 border border-[#3B82F6]/30">
              <div className="w-2 h-2 rounded-full bg-[#3B82F6] animate-pulse" />
              <span className="text-xs text-white/80">AI Active</span>
            </div>
          )}

          {/* Visualizer type selector */}
          <div className="relative">
            <button
              onClick={() => setShowVisualizerMenu(!showVisualizerMenu)}
              className="w-9 h-9 rounded-full bg-white/5 hover:bg-white/10 flex items-center justify-center text-white/70 hover:text-white transition-all duration-300"
            >
              <Waves className="w-5 h-5" />
            </button>

            {showVisualizerMenu && (
              <div className="absolute bottom-14 right-0 py-2 px-2 rounded-xl bg-black/90 backdrop-blur-xl border border-white/10 min-w-[160px]">
                {visualizerTypes.map((type) => (
                  <button
                    key={type.id}
                    onClick={() => {
                      onVisualizerTypeChange(type.id as any);
                      setShowVisualizerMenu(false);
                    }}
                    className={`w-full flex items-center gap-3 px-3 py-2 rounded-lg transition-all duration-200 ${
                      visualizerType === type.id
                        ? 'bg-[#3B82F6]/20 text-white'
                        : 'text-white/70 hover:bg-white/5 hover:text-white'
                    }`}
                  >
                    <type.icon className="w-4 h-4" />
                    <span className="text-sm">{type.label}</span>
                  </button>
                ))}
              </div>
            )}
          </div>

          {/* View mode */}
          <div className="flex items-center gap-1 p-1 rounded-full bg-white/5 border border-white/10">
            <button
              onClick={() => onViewModeChange('grid')}
              className={`w-8 h-8 rounded-full flex items-center justify-center transition-all ${
                viewMode === 'grid' ? 'bg-white/10 text-white' : 'text-white/60 hover:text-white hover:bg-white/5'
              }`}
              title="Grid"
            >
              <Grid3x3 className="w-4 h-4" />
            </button>
            <button
              onClick={() => onViewModeChange('carousel')}
              className={`w-8 h-8 rounded-full flex items-center justify-center transition-all ${
                viewMode === 'carousel' ? 'bg-white/10 text-white' : 'text-white/60 hover:text-white hover:bg-white/5'
              }`}
              title="Carousel"
            >
              <Layers className="w-4 h-4" />
            </button>
            <button
              onClick={() => onViewModeChange('list')}
              className={`w-8 h-8 rounded-full flex items-center justify-center transition-all ${
                viewMode === 'list' ? 'bg-white/10 text-white' : 'text-white/60 hover:text-white hover:bg-white/5'
              }`}
              title="List"
            >
              <List className="w-4 h-4" />
            </button>
          </div>

          {/* Fullscreen visualizer button */}
          <button
            onClick={onOpenVisualizer}
            disabled={!currentTrack}
            className="w-9 h-9 rounded-full bg-white/5 hover:bg-white/10 disabled:opacity-30 disabled:cursor-not-allowed flex items-center justify-center text-white/70 hover:text-white transition-all duration-300"
          >
            <Maximize2 className="w-5 h-5" />
          </button>

          {/* Volume control */}
          <div className="flex items-center gap-3">
            <Volume2 className="w-5 h-5 text-white/70" />
            <div className="relative w-24 h-1 bg-white/10 rounded-full group cursor-pointer">
              <div
                className="h-full bg-gradient-to-r from-[#3B82F6] to-[#8B5CF6] rounded-full relative"
                style={{ width: `${volume}%` }}
              >
                <div className="absolute right-0 top-1/2 -translate-y-1/2 w-3 h-3 rounded-full bg-white opacity-0 group-hover:opacity-100 transition-opacity" />
              </div>
              <input
                type="range"
                min="0"
                max="100"
                value={volume}
                onChange={(e) => onVolumeChange(Number(e.target.value))}
                className="absolute inset-0 w-full opacity-0 cursor-pointer"
              />
            </div>
          </div>
        </div>
      </div>
    </motion.div>
  );
}
