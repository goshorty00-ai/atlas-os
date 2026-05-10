import { motion } from "motion/react";
import { useEffect, useRef } from "react";

interface LyricLine {
  time: number;
  text: string;
}

interface LyricsDisplayProps {
  lyrics: LyricLine[];
  currentTime: number;
  isPlaying: boolean;
}

export default function LyricsDisplay({
  lyrics,
  currentTime,
  isPlaying,
}: LyricsDisplayProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const lyricsContentRef = useRef<HTMLDivElement>(null);

  // Find the current lyric index
  const currentIndex = lyrics.findIndex((lyric, i) => {
    const nextLyric = lyrics[i + 1];
    return (
      currentTime >= lyric.time &&
      (!nextLyric || currentTime < nextLyric.time)
    );
  });

  // Auto-scroll to current lyric
  useEffect(() => {
    if (
      containerRef.current &&
      lyricsContentRef.current &&
      currentIndex >= 0
    ) {
      const container = containerRef.current;
      const lyricsContent = lyricsContentRef.current;
      const activeElement = lyricsContent.children[
        currentIndex
      ] as HTMLElement;

      if (activeElement) {
        const containerHeight = container.clientHeight;
        const elementTop = activeElement.offsetTop;
        const elementHeight = activeElement.clientHeight;
        const scrollPosition =
          elementTop - containerHeight / 2 + elementHeight / 2;

        container.scrollTo({
          top: scrollPosition,
          behavior: "smooth",
        });
      }
    }
  }, [currentIndex]);

  return (
    <div
      ref={containerRef}
      className="h-80 overflow-y-auto overflow-x-hidden mb-8 px-4 relative"
      style={{
        scrollbarWidth: "thin",
        scrollbarColor: "rgba(6, 182, 212, 0.5) transparent",
      }}
    >
      {/* Gradient fade top */}
      <div className="absolute top-0 left-0 right-0 h-20 bg-gradient-to-b from-black/60 to-transparent pointer-events-none z-10" />
      {/* Gradient fade bottom */}
      <div className="absolute bottom-0 left-0 right-0 h-20 bg-gradient-to-t from-black/60 to-transparent pointer-events-none z-10" />

      <div ref={lyricsContentRef} className="space-y-8 py-32">
        {lyrics.map((lyric, index) => {
          const isActive = index === currentIndex;
          const isPast = index < currentIndex;
          const isFuture = index > currentIndex;

          return (
            <motion.div
              key={index}
              initial={{ opacity: 0, x: -50 }}
              animate={{
                opacity: isActive ? 1 : isFuture ? 0.35 : 0.25,
                x: 0,
                scale: isActive ? 1.05 : 1,
              }}
              transition={{ duration: 0.4, ease: "easeOut" }}
              className="text-center relative"
            >
              {/* Multi-layer glow for active lyric */}
              {isActive && (
                <>
                  <motion.div
                    animate={{
                      scale: [1, 1.2, 1],
                      opacity: [0.3, 0.6, 0.3],
                    }}
                    transition={{
                      duration: 2,
                      repeat: Infinity,
                      ease: "easeInOut",
                    }}
                    className="absolute inset-0 blur-3xl bg-cyan-500/40 -z-10"
                  />
                  <motion.div
                    animate={{
                      scale: [1, 1.15, 1],
                      opacity: [0.4, 0.7, 0.4],
                    }}
                    transition={{
                      duration: 1.5,
                      repeat: Infinity,
                      ease: "easeInOut",
                      delay: 0.2,
                    }}
                    className="absolute inset-0 blur-2xl bg-purple-500/30 -z-10"
                  />
                </>
              )}

              <motion.p
                className={`text-4xl md:text-5xl lg:text-6xl font-black tracking-wide transition-all duration-400 leading-tight ${
                  isActive
                    ? "text-white"
                    : isPast
                      ? "text-slate-600"
                      : "text-slate-700"
                }`}
                style={
                  isActive
                    ? {
                        textShadow: `
                    0 0 10px rgba(6, 182, 212, 0.8),
                    0 0 20px rgba(6, 182, 212, 0.6),
                    0 0 30px rgba(6, 182, 212, 0.4),
                    0 0 40px rgba(168, 85, 247, 0.4),
                    0 0 60px rgba(168, 85, 247, 0.3),
                    -2px -2px 0 rgba(6, 182, 212, 0.3),
                    2px -2px 0 rgba(6, 182, 212, 0.3),
                    -2px 2px 0 rgba(6, 182, 212, 0.3),
                    2px 2px 0 rgba(6, 182, 212, 0.3),
                    0 0 100px rgba(6, 182, 212, 0.2)
                  `,
                        WebkitTextStroke:
                          "1px rgba(6, 182, 212, 0.5)",
                        fontWeight: 900,
                      }
                    : isPast
                      ? {
                          textShadow:
                            "0 0 5px rgba(0, 0, 0, 0.5)",
                        }
                      : {
                          textShadow:
                            "0 0 5px rgba(0, 0, 0, 0.5)",
                        }
                }
                animate={
                  isActive && isPlaying
                    ? {
                        filter: [
                          "brightness(1) hue-rotate(0deg)",
                          "brightness(1.2) hue-rotate(5deg)",
                          "brightness(1) hue-rotate(0deg)",
                        ],
                      }
                    : {}
                }
                transition={{
                  duration: 2,
                  repeat: Infinity,
                  ease: "easeInOut",
                }}
              >
                {lyric.text || "\u00A0"}
              </motion.p>

              {/* Holographic scan line for active lyric */}
              {isActive && (
                <motion.div
                  initial={{ scaleX: 0, opacity: 0 }}
                  animate={{
                    scaleX: 1,
                    opacity: [0.6, 1, 0.6],
                  }}
                  transition={{
                    scaleX: { duration: 0.5, ease: "easeOut" },
                    opacity: {
                      duration: 1.5,
                      repeat: Infinity,
                      ease: "easeInOut",
                    },
                  }}
                  className="h-1 bg-gradient-to-r from-transparent via-cyan-400 to-transparent mx-auto mt-4 rounded-full shadow-lg shadow-cyan-500/50"
                  style={{ width: "80%", originX: 0.5 }}
                />
              )}

              {/* Particle effects for active lyric */}
              {isActive && isPlaying && (
                <div className="absolute inset-0 -z-20 overflow-hidden pointer-events-none">
                  {[...Array(6)].map((_, i) => (
                    <motion.div
                      key={i}
                      className="absolute w-1 h-1 bg-cyan-400 rounded-full"
                      initial={{
                        x: "50%",
                        y: "50%",
                        opacity: 0,
                        scale: 0,
                      }}
                      animate={{
                        x: `${50 + (Math.random() - 0.5) * 200}%`,
                        y: `${50 + (Math.random() - 0.5) * 200}%`,
                        opacity: [0, 1, 0],
                        scale: [0, 1, 0],
                      }}
                      transition={{
                        duration: 2,
                        repeat: Infinity,
                        delay: i * 0.3,
                        ease: "easeOut",
                      }}
                    />
                  ))}
                </div>
              )}
            </motion.div>
          );
        })}
      </div>
    </div>
  );
}