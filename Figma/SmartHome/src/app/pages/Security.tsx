import { motion } from 'motion/react';
import { SecurityCameras } from '../components/SecurityCameras';

export function Security() {
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
            }}
          >
            Security & Surveillance
          </h1>
        </div>
        <p className="text-cyan-400/60 text-sm ml-5">
          Real-time monitoring and alerts
        </p>
      </motion.div>

      <SecurityCameras />
    </>
  );
}
