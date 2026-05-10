import { motion, AnimatePresence } from 'motion/react';
import { Users, X, Sparkles } from 'lucide-react';
import { Track } from '../data/albums';
import { useEffect, useMemo, useRef, useState } from 'react';

interface LyricsViewProps {
  isOpen: boolean;
  onClose: () => void;
  onOpenParty?: () => void;
  track: Track | null;
  lines: Array<{ timeSeconds: number; text: string }>;
  currentSeconds: number;
}

export function LyricsView({ isOpen, onClose, onOpenParty, track, lines, currentSeconds }: LyricsViewProps) {
  const [isFetching, setIsFetching] = useState(false);
  const listRef = useRef<HTMLDivElement | null>(null);

  const hasTiming = useMemo(() => {
    if (!lines || lines.length === 0) return false;
    let max = 0;
    for (const l of lines) {
      const t = Number((l as any)?.timeSeconds || 0);
      if (Number.isFinite(t) && t > max) max = t;
    }
    return max > 0.5;
  }, [lines]);

  useEffect(() => {
    if (isOpen && track) {
      setIsFetching(true);
    }
  }, [isOpen, track]);

  useEffect(() => {
    if (!isOpen) return;
    if (lines.length > 0) setIsFetching(false);
  }, [isOpen, lines.length]);

  const currentLineIndex = useMemo(() => {
    if (lines.length === 0) return 0;
    if (!hasTiming) return 0;
    for (let i = lines.length - 1; i >= 0; i--) {
      if (currentSeconds >= (lines[i].timeSeconds || 0)) {
        return i;
      }
    }
    return 0;
  }, [currentSeconds, lines, hasTiming]);

  useEffect(() => {
    if (!isOpen) return;
    if (!hasTiming) {
      try {
        if (listRef.current) listRef.current.scrollTop = 0;
      } catch {
      }
      return;
    }
    const el = document.getElementById(`lyric-line-${currentLineIndex}`);
    if (el) el.scrollIntoView({ block: 'center', behavior: 'smooth' });
  }, [isOpen, currentLineIndex, hasTiming]);

  return (
    <AnimatePresence>
      {isOpen && track && (
        <motion.div
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-xl"
        >
          {onOpenParty && (
            <button
              onClick={onOpenParty}
              className="absolute top-8 right-24 h-12 px-4 rounded-full bg-white/5 backdrop-blur-md border border-white/10 flex items-center justify-center gap-2 text-white/70 hover:text-white hover:bg-white/10 transition-all duration-300 z-10"
              aria-label="Karaoke Party"
            >
              <Users className="w-5 h-5" />
              <span className="text-sm">Party</span>
            </button>
          )}
          {/* Close button */}
          <button
            onClick={onClose}
            className="absolute top-8 right-8 w-12 h-12 rounded-full bg-white/5 backdrop-blur-md border border-white/10 flex items-center justify-center text-white/70 hover:text-white hover:bg-white/10 transition-all duration-300 z-10"
          >
            <X className="w-6 h-6" />
          </button>

          {/* Lyrics container */}
          <div className="relative w-full max-w-3xl h-[600px] flex flex-col">
            {/* AI Fetch indicator */}
            <AnimatePresence>
              {isFetching && (
                <motion.div
                  initial={{ opacity: 0, y: -10 }}
                  animate={{ opacity: 1, y: 0 }}
                  exit={{ opacity: 0, y: -10 }}
                  className="mb-6 flex items-center justify-center gap-2 px-4 py-2 rounded-xl bg-gradient-to-r from-[#3B82F6]/20 to-[#8B5CF6]/20 border border-[#3B82F6]/30 mx-auto"
                >
                  <Sparkles className="w-4 h-4 text-[#3B82F6] animate-pulse" />
                  <span className="text-sm text-white/90">Fetching lyrics with AI...</span>
                  <div className="w-2 h-2 rounded-full bg-[#3B82F6] animate-pulse" />
                </motion.div>
              )}
            </AnimatePresence>

            {/* Track info */}
            <div className="text-center mb-8">
              <h2 className="text-2xl text-white mb-1">{track.title}</h2>
              <p className="text-white/60">{hasTiming ? 'Karaoke Mode • Auto-scroll' : 'Lyrics'}</p>
            </div>

            {/* Lyrics */}
            <div className="flex-1 relative overflow-hidden">
              <div className="absolute top-0 left-0 right-0 h-24 bg-gradient-to-b from-[#0B0F14] to-transparent pointer-events-none z-10" />
              <div className="absolute bottom-0 left-0 right-0 h-24 bg-gradient-to-t from-[#0B0F14] to-transparent pointer-events-none z-10" />
              <div className="absolute top-1/2 left-0 right-0 h-px bg-gradient-to-r from-transparent via-[#3B82F6]/50 to-transparent -translate-y-1/2 pointer-events-none z-10" />

              <div ref={listRef} className="h-full overflow-y-auto custom-scrollbar px-4">
                <div className="py-24 space-y-3">
                  {(lines.length > 0 ? lines : [{ timeSeconds: 0, text: '' }]).map((line, index) => {
                    const isActive = index === currentLineIndex;
                    return (
                      <motion.div
                        key={index}
                        id={`lyric-line-${index}`}
                        className={`text-center transition-all duration-300 ${isActive ? 'opacity-100' : 'opacity-50'}`}
                      >
                        <span
                          className={`inline-block transition-all duration-300 ${
                            isActive ? 'text-white text-3xl' : 'text-white/60 text-2xl'
                          }`}
                          style={{
                            textShadow: isActive
                              ? '0 0 30px rgba(59, 130, 246, 0.6), 0 0 60px rgba(139, 92, 246, 0.4)'
                              : 'none',
                          }}
                        >
                          {line.text || '\u00A0'}
                        </span>
                      </motion.div>
                    );
                  })}
                </div>
              </div>
            </div>
          </div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
