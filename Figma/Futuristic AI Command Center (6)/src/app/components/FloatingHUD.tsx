import { useState, useEffect } from "react";
import { motion } from "motion/react";
import { Shield, Grip } from "lucide-react";

export function FloatingHUD() {
  const [particles, setParticles] = useState<
    Array<{ id: number; angle: number; distance: number }>
  >([]);
  const [isDragging, setIsDragging] = useState(false);

  useEffect(() => {
    const particleArray = Array.from({ length: 20 }, (_, i) => ({
      id: i,
      angle: (i * Math.PI * 2) / 20,
      distance: 25 + Math.random() * 10,
    }));
    setParticles(particleArray);
  }, []);

  return (
    <motion.div
      drag
      dragMomentum={false}
      onDragStart={() => setIsDragging(true)}
      onDragEnd={() => setIsDragging(false)}
      className="fixed top-8 right-8 cursor-move"
      whileHover={{ scale: 1.05 }}
    >
      <div className="relative">
        {/* Drag indicator */}
        <motion.div
          className="absolute -top-6 left-1/2 -translate-x-1/2 opacity-0 hover:opacity-100 transition-opacity"
          initial={{ opacity: 0 }}
          whileHover={{ opacity: 1 }}
        >
          <Grip className="w-4 h-4 text-slate-600" />
        </motion.div>

        {/* Main HUD Container */}
        <div className="bg-[#0b0f14]/90 backdrop-blur-xl border border-cyan-500/30 rounded-2xl p-4 shadow-[0_0_40px_rgba(34,211,238,0.2)]">
          <div className="flex items-center gap-4">
            {/* Mini Orb */}
            <div className="relative w-16 h-16 flex-shrink-0">
              {/* Outer ring */}
              <motion.div
                className="absolute inset-0 rounded-full border border-cyan-500/30"
                animate={{ rotate: 360 }}
                transition={{
                  duration: 20,
                  repeat: Infinity,
                  ease: "linear",
                }}
              />

              {/* Particle mini-orb */}
              <div className="absolute inset-0 flex items-center justify-center">
                <div className="relative w-10 h-10">
                  {/* Glow */}
                  <motion.div
                    className="absolute inset-0 rounded-full bg-gradient-radial from-cyan-500/30 to-transparent"
                    animate={{
                      scale: [1, 1.2, 1],
                      opacity: [0.3, 0.6, 0.3],
                    }}
                    transition={{
                      duration: 2,
                      repeat: Infinity,
                      ease: "easeInOut",
                    }}
                  />

                  {/* Mini particles */}
                  {particles.map((particle) => (
                    <motion.div
                      key={particle.id}
                      className="absolute rounded-full bg-cyan-400"
                      style={{
                        width: 1.5,
                        height: 1.5,
                        left: "50%",
                        top: "50%",
                      }}
                      animate={{
                        x: Math.cos(particle.angle) * particle.distance,
                        y: Math.sin(particle.angle) * particle.distance,
                        opacity: [0.4, 1, 0.4],
                      }}
                      transition={{
                        duration: 2,
                        repeat: Infinity,
                        ease: "easeInOut",
                        delay: particle.id * 0.05,
                      }}
                    />
                  ))}

                  {/* Core */}
                  <motion.div
                    className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-2 h-2 rounded-full bg-cyan-400"
                    animate={{
                      scale: [1, 1.5, 1],
                      boxShadow: [
                        "0 0 10px rgba(34, 211, 238, 0.5)",
                        "0 0 20px rgba(34, 211, 238, 0.8)",
                        "0 0 10px rgba(34, 211, 238, 0.5)",
                      ],
                    }}
                    transition={{
                      duration: 2,
                      repeat: Infinity,
                      ease: "easeInOut",
                    }}
                  />
                </div>
              </div>
            </div>

            {/* Status Info */}
            <div className="flex flex-col gap-2">
              <div className="flex items-center gap-2">
                <motion.div
                  className="w-2 h-2 rounded-full bg-cyan-400"
                  animate={{
                    opacity: [0.5, 1, 0.5],
                  }}
                  transition={{
                    duration: 2,
                    repeat: Infinity,
                  }}
                />
                <span className="text-xs font-mono tracking-wider text-cyan-400">
                  LISTENING
                </span>
              </div>

              <div className="flex items-center gap-2">
                <Shield className="w-3 h-3 text-green-400" />
                <span className="text-xs font-mono text-green-400">SAFE</span>
              </div>

              <div className="text-[10px] font-mono text-slate-500 tracking-wide">
                Atlas AI v2.3.1
              </div>
            </div>
          </div>
        </div>
      </div>
    </motion.div>
  );
}