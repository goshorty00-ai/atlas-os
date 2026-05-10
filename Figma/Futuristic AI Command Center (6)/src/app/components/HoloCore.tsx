import { useEffect, useState } from "react";
import { motion, AnimatePresence } from "motion/react";
import {
  Volume2,
  Settings,
  Activity,
  Workflow,
  Zap,
  Shield,
} from "lucide-react";

type Status = "online" | "thinking" | "speaking";

export function HoloCore() {
  const [status, setStatus] = useState<Status>("online");
  const [particles, setParticles] = useState<
    Array<{ id: number; angle: number; distance: number; size: number }>
  >([]);

  // Generate particles for the orb
  useEffect(() => {
    const particleArray = Array.from({ length: 60 }, (_, i) => ({
      id: i,
      angle: Math.random() * Math.PI * 2,
      distance: Math.random() * 80 + 40,
      size: Math.random() * 3 + 1,
    }));
    setParticles(particleArray);

    // Cycle through statuses for demo
    const interval = setInterval(() => {
      setStatus((prev) => {
        if (prev === "online") return "thinking";
        if (prev === "thinking") return "speaking";
        return "online";
      });
    }, 5000);

    return () => clearInterval(interval);
  }, []);

  const actionIcons = [
    { Icon: Volume2, label: "Volume", angle: 0 },
    { Icon: Settings, label: "Settings", angle: 60 },
    { Icon: Activity, label: "Diagnostics", angle: 120 },
    { Icon: Workflow, label: "Workflows", angle: 180 },
    { Icon: Zap, label: "Agent", angle: 240 },
    { Icon: Shield, label: "Security", angle: 300 },
  ];

  const statusText = {
    online: "ONLINE · LISTENING",
    thinking: "THINKING",
    speaking: "SPEAKING",
  };

  const statusColor = {
    online: "text-cyan-400",
    thinking: "text-orange-400",
    speaking: "text-purple-400",
  };

  return (
    <div className="flex-1 flex flex-col items-center justify-center bg-gradient-to-b from-[#0b0f14] to-[#0f1419] p-8">
      {/* Main Orb Container */}
      <div className="relative w-80 h-80 mb-8">
        {/* Outer rotating rings */}
        <motion.div
          className="absolute inset-0 rounded-full border border-cyan-500/20"
          animate={{ rotate: 360 }}
          transition={{
            duration: 20,
            repeat: Infinity,
            ease: "linear",
          }}
          style={{
            boxShadow: "0 0 40px rgba(34, 211, 238, 0.1)",
          }}
        />

        <motion.div
          className="absolute inset-4 rounded-full border border-cyan-500/10"
          animate={{ rotate: -360 }}
          transition={{
            duration: 15,
            repeat: Infinity,
            ease: "linear",
          }}
        />

        {/* Particle Orb */}
        <div className="absolute inset-0 flex items-center justify-center">
          <div className="relative w-40 h-40">
            {/* Core glow */}
            <motion.div
              className={`absolute inset-0 rounded-full ${
                status === "thinking"
                  ? "bg-gradient-radial from-orange-500/30 to-transparent"
                  : "bg-gradient-radial from-cyan-500/20 to-transparent"
              }`}
              animate={{
                scale: status === "thinking" ? [1, 1.2, 1] : [1, 1.1, 1],
                opacity: status === "speaking" ? [0.3, 0.6, 0.3] : [0.3, 0.5, 0.3],
              }}
              transition={{
                duration: status === "thinking" ? 1.5 : 2,
                repeat: Infinity,
                ease: "easeInOut",
              }}
            />

            {/* Particles */}
            {particles.map((particle) => (
              <motion.div
                key={particle.id}
                className={`absolute rounded-full ${
                  status === "thinking"
                    ? particle.id % 3 === 0
                      ? "bg-orange-400"
                      : "bg-cyan-400"
                    : "bg-cyan-400"
                }`}
                style={{
                  width: particle.size,
                  height: particle.size,
                  left: "50%",
                  top: "50%",
                }}
                animate={{
                  x:
                    Math.cos(particle.angle) * particle.distance +
                    (status === "thinking" ? Math.random() * 10 - 5 : 0),
                  y:
                    Math.sin(particle.angle) * particle.distance +
                    (status === "thinking" ? Math.random() * 10 - 5 : 0),
                  opacity: status === "speaking" ? [0.3, 1, 0.3] : [0.5, 1, 0.5],
                  scale: status === "thinking" ? [1, 1.5, 1] : [1, 1.2, 1],
                }}
                transition={{
                  duration: 2 + Math.random() * 2,
                  repeat: Infinity,
                  ease: "easeInOut",
                  delay: particle.id * 0.02,
                }}
              />
            ))}

            {/* Center core dot */}
            <motion.div
              className={`absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-3 h-3 rounded-full ${
                status === "thinking" ? "bg-orange-400" : "bg-cyan-400"
              }`}
              animate={{
                scale: [1, 1.5, 1],
                boxShadow: [
                  "0 0 20px rgba(34, 211, 238, 0.5)",
                  "0 0 40px rgba(34, 211, 238, 0.8)",
                  "0 0 20px rgba(34, 211, 238, 0.5)",
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

        {/* Orbiting Action Icons */}
        {actionIcons.map(({ Icon, label, angle }, idx) => {
          const radius = 160;
          const x = Math.cos((angle * Math.PI) / 180) * radius;
          const y = Math.sin((angle * Math.PI) / 180) * radius;

          return (
            <motion.button
              key={label}
              className="absolute top-1/2 left-1/2 p-3 rounded-full bg-slate-900/50 border border-orange-400/30 text-orange-400 hover:bg-orange-400/10 hover:border-orange-400/50 transition-all hover:shadow-[0_0_20px_rgba(249,115,22,0.3)]"
              style={{
                x,
                y,
              }}
              animate={{
                rotate: [0, 360],
              }}
              transition={{
                duration: 40,
                repeat: Infinity,
                ease: "linear",
                delay: idx * 0.1,
              }}
              whileHover={{ scale: 1.15 }}
              title={label}
            >
              <motion.div
                animate={{ rotate: [0, -360] }}
                transition={{
                  duration: 40,
                  repeat: Infinity,
                  ease: "linear",
                  delay: idx * 0.1,
                }}
              >
                <Icon className="w-4 h-4" />
              </motion.div>
            </motion.button>
          );
        })}
      </div>

      {/* Status Display */}
      <div className="text-center space-y-2">
        <AnimatePresence mode="wait">
          <motion.div
            key={status}
            initial={{ opacity: 0, y: 10 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: -10 }}
            className={`font-mono text-sm tracking-widest ${statusColor[status]}`}
          >
            {statusText[status]}
          </motion.div>
        </AnimatePresence>

        <div className="flex items-center gap-2 justify-center">
          <div className="w-1.5 h-1.5 rounded-full bg-green-400 animate-pulse" />
          <span className="text-xs text-slate-500 font-mono">OPERATIONAL</span>
        </div>
      </div>
    </div>
  );
}