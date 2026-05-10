import { motion } from 'motion/react';
import { StatusHUD } from '../components/StatusHUD';
import { HolographicHouse } from '../components/HolographicHouse';
import { SceneAutomation } from '../components/SceneAutomation';

export function Home() {
  return (
    <>
      {/* Header */}
      <motion.div
        className="mb-8"
        initial={{ opacity: 0, y: -20 }}
        animate={{ opacity: 1, y: 0 }}
      >
        <div className="flex items-center gap-3 mb-2">
          <div className="w-2 h-2 rounded-full bg-cyan-400 animate-pulse" 
               style={{ boxShadow: '0 0 10px #00d4ff' }} />
          <h1 
            className="text-4xl font-bold"
            style={{
              background: 'linear-gradient(135deg, #00d4ff, #0066ff)',
              WebkitBackgroundClip: 'text',
              WebkitTextFillColor: 'transparent',
              textShadow: '0 0 30px rgba(0, 212, 255, 0.5)',
            }}
          >
            Atlas Smart Home Command Centre
          </h1>
        </div>
        <p className="text-cyan-400/60 text-sm ml-5">
          Holographic Interface System v3.0
        </p>
      </motion.div>

      {/* Status HUD */}
      <StatusHUD />

      {/* 3D House Section */}
      <motion.div
        className="mt-8"
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        transition={{ delay: 0.3 }}
      >
        <h2 className="text-sm text-cyan-400/80 mb-4 flex items-center gap-2">
          <div className="w-1 h-4 bg-cyan-400 rounded-full" style={{ boxShadow: '0 0 10px #00d4ff' }} />
          3D House Map
        </h2>
        <div
          className="relative rounded-2xl backdrop-blur-xl"
          style={{
            background: 'rgba(5, 10, 18, 0.4)',
            border: '1px solid rgba(0, 212, 255, 0.3)',
            boxShadow: '0 0 40px rgba(0, 212, 255, 0.2), inset 0 0 40px rgba(0, 212, 255, 0.05)',
            minHeight: '600px',
          }}
        >
          {/* Corner Decorations */}
          <div className="absolute top-0 left-0 w-20 h-20 border-l-2 border-t-2 border-cyan-400/50 rounded-tl-2xl" />
          <div className="absolute top-0 right-0 w-20 h-20 border-r-2 border-t-2 border-cyan-400/50 rounded-tr-2xl" />
          <div className="absolute bottom-0 left-0 w-20 h-20 border-l-2 border-b-2 border-cyan-400/50 rounded-bl-2xl" />
          <div className="absolute bottom-0 right-0 w-20 h-20 border-r-2 border-b-2 border-cyan-400/50 rounded-br-2xl" />

          <HolographicHouse />
        </div>
      </motion.div>

      {/* Scene Automation */}
      <SceneAutomation />
    </>
  );
}
