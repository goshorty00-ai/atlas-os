import { motion } from 'motion/react';
import { useState } from 'react';

interface EqualizerProps {
  isPlaying: boolean;
}

export default function Equalizer({ isPlaying }: EqualizerProps) {
  const [bars] = useState(80);

  return (
    <div className="h-40 flex items-end justify-center gap-0.5 mb-8 px-4 relative overflow-hidden rounded-xl bg-black/30 border border-cyan-500/20 py-4">
      {/* Grid background */}
      <div 
        className="absolute inset-0 opacity-20"
        style={{
          backgroundImage: `
            linear-gradient(rgba(6, 182, 212, 0.2) 1px, transparent 1px),
            linear-gradient(90deg, rgba(6, 182, 212, 0.2) 1px, transparent 1px)
          `,
          backgroundSize: '20px 20px'
        }}
      />
      
      {/* Reflection effect */}
      <div className="absolute bottom-0 left-0 right-0 h-1/3 bg-gradient-to-t from-cyan-500/10 to-transparent pointer-events-none" />
      
      {Array.from({ length: bars }).map((_, i) => {
        const delay = i * 0.01;
        const baseHeight = 10 + Math.random() * 15;
        const intensity = Math.abs((i - bars / 2) / (bars / 2));
        const color = intensity < 0.3 ? 'from-cyan-400 to-cyan-600' 
                    : intensity < 0.6 ? 'from-cyan-500 to-purple-500'
                    : 'from-purple-500 to-pink-500';

        return (
          <motion.div
            key={i}
            className={`flex-1 rounded-t-sm bg-gradient-to-t ${color} relative`}
            initial={{ height: baseHeight }}
            animate={
              isPlaying
                ? {
                    height: [
                      baseHeight, 
                      baseHeight + (Math.random() * 100) + 20, 
                      baseHeight + (Math.random() * 80),
                      baseHeight
                    ],
                    opacity: [0.4, 1, 0.7, 0.4],
                  }
                : { height: baseHeight, opacity: 0.2 }
            }
            transition={
              isPlaying
                ? {
                    duration: 0.4 + Math.random() * 0.4,
                    repeat: Infinity,
                    delay: delay,
                    ease: 'easeInOut',
                  }
                : { duration: 0.3 }
            }
            style={{
              minWidth: '1px',
              maxWidth: '8px',
              boxShadow: isPlaying ? `0 0 10px currentColor, 0 -5px 15px currentColor` : 'none',
            }}
          >
            {/* Top glow */}
            {isPlaying && (
              <motion.div
                className="absolute -top-1 left-0 right-0 h-2 bg-white rounded-full blur-sm"
                animate={{
                  opacity: [0.3, 0.8, 0.3],
                }}
                transition={{
                  duration: 0.5,
                  repeat: Infinity,
                  delay: delay,
                }}
              />
            )}
          </motion.div>
        );
      })}
      
      {/* Center line */}
      <div className="absolute top-1/2 left-0 right-0 h-px bg-cyan-500/20" />
    </div>
  );
}
