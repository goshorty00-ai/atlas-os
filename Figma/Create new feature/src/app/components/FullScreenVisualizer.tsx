import { motion, AnimatePresence } from 'motion/react';
import { X } from 'lucide-react';
import { Visualizer } from './Visualizer';
import { Album, Track } from '../data/albums';
import { useState, useEffect } from 'react';

interface FullScreenVisualizerProps {
  isOpen: boolean;
  onClose: () => void;
  album: Album | null;
  currentTrack: Track | null;
  isPlaying: boolean;
  visualizerType: 'waveform' | 'circular' | 'particles' | 'bars' | 'blob' | 'spectrum' | 'neonGrid';
}

export function FullScreenVisualizer({
  isOpen,
  onClose,
  album,
  currentTrack,
  isPlaying,
  visualizerType,
}: FullScreenVisualizerProps) {
  const [showControls, setShowControls] = useState(true);
  const [hideTimeout, setHideTimeout] = useState<NodeJS.Timeout | null>(null);

  useEffect(() => {
    if (isOpen) {
      setShowControls(true);
      resetHideTimeout();
    }
  }, [isOpen]);

  const resetHideTimeout = () => {
    if (hideTimeout) clearTimeout(hideTimeout);
    setShowControls(true);
    const timeout = setTimeout(() => setShowControls(false), 3000);
    setHideTimeout(timeout);
  };

  const handleMouseMove = () => {
    resetHideTimeout();
  };

  if (!album || !currentTrack) return null;

  return (
    <AnimatePresence>
      {isOpen && (
        <motion.div
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          className="fixed inset-0 z-[100] bg-[#0B0F14] overflow-hidden"
          onMouseMove={handleMouseMove}
        >
          {/* Background gradient based on mood */}
          <div
            className="absolute inset-0 opacity-30"
            style={{
              background: `radial-gradient(circle at 50% 50%, ${album.dominantColor}30 0%, transparent 70%)`,
            }}
          />

          {/* Visualizer */}
          <Visualizer
            type={visualizerType}
            dominantColor={album.dominantColor}
            isPlaying={isPlaying}
            intensity={1}
          />

          {/* Floating album art */}
          <motion.div
            className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2"
            animate={{
              rotate: [0, 360],
            }}
            transition={{
              duration: 60,
              repeat: Infinity,
              ease: 'linear',
            }}
          >
            <div
              className="w-64 h-64 rounded-full overflow-hidden"
              style={{
                boxShadow: `0 0 100px ${album.dominantColor}60, inset 0 0 60px ${album.dominantColor}40`,
              }}
            >
              <motion.img
                src={album.cover}
                alt={album.title}
                className="w-full h-full object-cover"
                animate={{
                  rotate: [0, -360],
                }}
                transition={{
                  duration: 60,
                  repeat: Infinity,
                  ease: 'linear',
                }}
              />
            </div>
          </motion.div>

          {/* Track info overlay */}
          <AnimatePresence>
            {showControls && (
              <motion.div
                initial={{ opacity: 0, y: 20 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: 20 }}
                className="absolute bottom-24 left-1/2 -translate-x-1/2 text-center"
              >
                <h2 className="text-3xl text-white mb-2">{currentTrack.title}</h2>
                <p className="text-xl text-white/70">{album.artist}</p>
                {currentTrack.bpm && currentTrack.key && (
                  <div className="mt-4 flex items-center justify-center gap-4 text-sm text-white/50">
                    <span>{currentTrack.bpm} BPM</span>
                    <span>•</span>
                    <span>{currentTrack.key}</span>
                    {currentTrack.energy && (
                      <>
                        <span>•</span>
                        <span>Energy: {currentTrack.energy}%</span>
                      </>
                    )}
                  </div>
                )}
              </motion.div>
            )}
          </AnimatePresence>

          {/* Close button */}
          <AnimatePresence>
            {showControls && (
              <motion.button
                initial={{ opacity: 0, scale: 0.8 }}
                animate={{ opacity: 1, scale: 1 }}
                exit={{ opacity: 0, scale: 0.8 }}
                onClick={onClose}
                className="absolute top-8 right-8 w-14 h-14 rounded-full bg-white/5 backdrop-blur-md border border-white/10 flex items-center justify-center text-white/70 hover:text-white hover:bg-white/10 hover:border-[#3B82F6]/50 transition-all duration-300"
                style={{
                  boxShadow: '0 0 30px rgba(59, 130, 246, 0.3)',
                }}
              >
                <X className="w-6 h-6" />
              </motion.button>
            )}
          </AnimatePresence>

          {/* Volumetric fog effect */}
          <div className="absolute inset-0 pointer-events-none">
            <div
              className="absolute top-0 left-0 w-full h-full opacity-20"
              style={{
                background: `radial-gradient(ellipse at 30% 40%, ${album.dominantColor}40 0%, transparent 50%)`,
                filter: 'blur(80px)',
              }}
            />
            <div
              className="absolute top-0 left-0 w-full h-full opacity-20"
              style={{
                background: `radial-gradient(ellipse at 70% 60%, ${album.dominantColor}30 0%, transparent 50%)`,
                filter: 'blur(100px)',
              }}
            />
          </div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
