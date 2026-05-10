import { motion, AnimatePresence } from "motion/react";
import { X, Languages, BookOpen } from "lucide-react";
import { useEffect, useRef, useState } from "react";

interface LyricsPanelProps {
  isOpen: boolean;
  onClose: () => void;
  songTitle: string;
  artist: string;
  artwork: string;
  currentSeconds: number;
  lines: Array<{
    timeSeconds: number;
    text: string;
  }>;
}
export function LyricsPanel({ isOpen, onClose, songTitle, artist, artwork, currentSeconds, lines }: LyricsPanelProps) {
  const [showTranslation, setShowTranslation] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);
  const currentLineRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (currentLineRef.current && containerRef.current) {
      currentLineRef.current.scrollIntoView({
        behavior: "smooth",
        block: "center",
      });
    }
  }, [currentSeconds, isOpen]);

  const activeLines = lines.length > 0 ? lines : [{ timeSeconds: 0, text: "No lyrics found for this track yet." }];

  const getCurrentLineIndex = () => {
    for (let i = activeLines.length - 1; i >= 0; i--) {
      if (currentSeconds >= activeLines[i].timeSeconds) {
        return i;
      }
    }
    return 0;
  };

  const currentLineIndex = getCurrentLineIndex();

  return (
    <AnimatePresence>
      {isOpen && (
        <motion.div
          className="fixed inset-0 bg-black/95 backdrop-blur-xl z-50 flex items-center justify-center p-4"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          onClick={onClose}
        >
          <motion.div
            className="max-w-4xl w-full h-[90vh] bg-gradient-to-br from-gray-900/80 via-black/80 to-gray-900/80 rounded-3xl border border-white/10 overflow-hidden flex flex-col"
            initial={{ scale: 0.9, opacity: 0 }}
            animate={{ scale: 1, opacity: 1 }}
            exit={{ scale: 0.9, opacity: 0 }}
            onClick={(e) => e.stopPropagation()}
          >
            {/* Header */}
            <div className="relative h-48 flex-shrink-0">
              {/* Background blur */}
              <div className="absolute inset-0 overflow-hidden">
                <img
                  src={artwork}
                  alt={songTitle}
                  className="w-full h-full object-cover blur-3xl scale-110 opacity-30"
                />
                <div className="absolute inset-0 bg-gradient-to-b from-transparent to-black" />
              </div>

              {/* Content */}
              <div className="relative h-full flex items-end p-6">
                <div className="flex items-end gap-4 w-full">
                  <div className="w-24 h-24 rounded-xl overflow-hidden border border-white/20 flex-shrink-0">
                    <img src={artwork} alt={songTitle} className="w-full h-full object-cover" />
                  </div>

                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2 mb-2">
                      <BookOpen className="w-4 h-4 text-cyan-400" />
                      <span className="text-cyan-400 text-sm font-semibold">Lyrics</span>
                    </div>
                    <h2 className="text-2xl font-bold text-white truncate">{songTitle}</h2>
                    <p className="text-gray-300">{artist}</p>
                  </div>

                  <div className="flex items-center gap-2">
                    <motion.button
                      className={`px-4 py-2 rounded-xl flex items-center gap-2 transition-colors ${
                        showTranslation
                          ? "bg-cyan-500/20 border border-cyan-500/50 text-cyan-400"
                          : "bg-white/5 border border-white/10 text-gray-400 hover:text-white"
                      }`}
                      whileHover={{ scale: 1.05 }}
                      whileTap={{ scale: 0.95 }}
                      onClick={() => setShowTranslation(!showTranslation)}
                    >
                      <Languages className="w-4 h-4" />
                      <span className="text-sm">Translate</span>
                    </motion.button>

                    <motion.button
                      className="w-10 h-10 rounded-xl bg-white/5 backdrop-blur-sm flex items-center justify-center border border-white/10 text-white hover:bg-white/10 transition-colors"
                      whileHover={{ scale: 1.05 }}
                      whileTap={{ scale: 0.95 }}
                      onClick={onClose}
                    >
                      <X className="w-5 h-5" />
                    </motion.button>
                  </div>
                </div>
              </div>
            </div>

            {/* Lyrics content */}
            <div
              ref={containerRef}
              className="flex-1 overflow-y-auto px-6 py-8 space-y-6"
              style={{
                scrollbarWidth: "thin",
                scrollbarColor: "rgba(6, 182, 212, 0.3) transparent",
              }}
            >
              {activeLines.map((line, index) => {
                const isCurrent = index === currentLineIndex;
                const isPast = index < currentLineIndex;

                return (
                  <motion.div
                    key={index}
                    ref={isCurrent ? currentLineRef : null}
                    className="relative"
                    initial={{ opacity: 0, y: 20 }}
                    animate={{
                      opacity: line.text ? 1 : 0.3,
                      y: 0,
                      scale: isCurrent ? 1.1 : 1,
                    }}
                    transition={{ delay: index * 0.05 }}
                  >
                    <motion.p
                      className={`text-center transition-all duration-300 ${
                        isCurrent
                          ? "text-4xl font-bold bg-gradient-to-r from-cyan-400 via-purple-400 to-pink-400 bg-clip-text text-transparent"
                          : isPast
                          ? "text-xl text-gray-500"
                          : "text-xl text-gray-600"
                      }`}
                      animate={
                        isCurrent
                          ? {
                              textShadow: [
                                "0 0 20px rgba(6, 182, 212, 0.5)",
                                "0 0 40px rgba(168, 85, 247, 0.5)",
                                "0 0 20px rgba(6, 182, 212, 0.5)",
                              ],
                            }
                          : {}
                      }
                      transition={{
                        duration: 2,
                        repeat: isCurrent ? Infinity : 0,
                      }}
                    >
                      {line.text || "♪"}
                    </motion.p>

                    {/* Glow effect for current line */}
                    {isCurrent && (
                      <motion.div
                        className="absolute -inset-4 bg-gradient-to-r from-cyan-500/10 via-purple-500/10 to-pink-500/10 rounded-xl blur-xl -z-10"
                        animate={{
                          opacity: [0.3, 0.6, 0.3],
                          scale: [1, 1.05, 1],
                        }}
                        transition={{
                          duration: 2,
                          repeat: Infinity,
                        }}
                      />
                    )}

                    {/* Translation */}
                    {showTranslation && line.text && (
                      <motion.p
                        className="text-center text-sm text-gray-500 mt-2"
                        initial={{ opacity: 0 }}
                        animate={{ opacity: 1 }}
                      >
                        {/* Mock translation */}
                        {line.text.split(" ").reverse().join(" ")}
                      </motion.p>
                    )}
                  </motion.div>
                );
              })}
            </div>

            {/* Progress indicator */}
            <div className="h-1 bg-white/5">
              <motion.div
                className="h-full bg-gradient-to-r from-cyan-500 via-purple-500 to-pink-500"
                style={{ width: `${activeLines.length > 1 ? ((currentLineIndex + 1) / activeLines.length) * 100 : 0}%` }}
              />
            </div>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
