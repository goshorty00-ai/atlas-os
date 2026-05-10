import { motion } from 'motion/react';
import { Camera, Radio } from 'lucide-react';

const cameras = [
  { id: 'front-door', name: 'Front Door', status: 'active' },
  { id: 'back-garden', name: 'Back Garden', status: 'active' },
  { id: 'garage', name: 'Garage', status: 'active' },
];

export function SecurityCameras() {
  return (
    <div className="mt-8">
      <h3 className="text-sm text-cyan-400/80 mb-4 flex items-center gap-2">
        <div className="w-1 h-4 bg-cyan-400 rounded-full" style={{ boxShadow: '0 0 10px #00d4ff' }} />
        Security Cameras
      </h3>
      
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        {cameras.map((camera, index) => (
          <motion.div
            key={camera.id}
            className="relative group cursor-pointer"
            initial={{ opacity: 0, scale: 0.9 }}
            animate={{ opacity: 1, scale: 1 }}
            transition={{ delay: index * 0.1 }}
            whileHover={{ scale: 1.02 }}
          >
            <div
              className="relative rounded-xl overflow-hidden backdrop-blur-xl"
              style={{
                background: 'rgba(5, 10, 18, 0.6)',
                border: '1px solid rgba(0, 212, 255, 0.3)',
                boxShadow: '0 0 20px rgba(0, 212, 255, 0.15), inset 0 0 20px rgba(0, 212, 255, 0.05)',
              }}
            >
              {/* Camera Feed Placeholder */}
              <div className="relative aspect-video bg-gradient-to-br from-gray-900 to-gray-800">
                {/* Grid Overlay */}
                <div 
                  className="absolute inset-0 opacity-20"
                  style={{
                    backgroundImage: 'linear-gradient(0deg, transparent 24%, rgba(0, 212, 255, 0.3) 25%, rgba(0, 212, 255, 0.3) 26%, transparent 27%, transparent 74%, rgba(0, 212, 255, 0.3) 75%, rgba(0, 212, 255, 0.3) 76%, transparent 77%, transparent), linear-gradient(90deg, transparent 24%, rgba(0, 212, 255, 0.3) 25%, rgba(0, 212, 255, 0.3) 26%, transparent 27%, transparent 74%, rgba(0, 212, 255, 0.3) 75%, rgba(0, 212, 255, 0.3) 76%, transparent 77%, transparent)',
                    backgroundSize: '30px 30px',
                  }}
                />

                {/* Scanning Line */}
                <motion.div
                  className="absolute inset-x-0 h-1 bg-gradient-to-r from-transparent via-cyan-400 to-transparent"
                  style={{
                    boxShadow: '0 0 20px rgba(0, 212, 255, 0.8)',
                  }}
                  animate={{
                    top: ['0%', '100%'],
                  }}
                  transition={{
                    duration: 3,
                    repeat: Infinity,
                    ease: 'linear',
                  }}
                />

                {/* Center Crosshair */}
                <div className="absolute inset-0 flex items-center justify-center">
                  <Camera className="w-16 h-16 text-cyan-400/30" />
                  
                  {/* Crosshair Lines */}
                  <div className="absolute inset-0 flex items-center justify-center">
                    <div className="relative w-24 h-24">
                      <div className="absolute top-0 left-1/2 w-px h-6 bg-cyan-400/50" />
                      <div className="absolute bottom-0 left-1/2 w-px h-6 bg-cyan-400/50" />
                      <div className="absolute left-0 top-1/2 w-6 h-px bg-cyan-400/50" />
                      <div className="absolute right-0 top-1/2 w-6 h-px bg-cyan-400/50" />
                      
                      {/* Corner Brackets */}
                      <div className="absolute top-0 left-0 w-4 h-4 border-l-2 border-t-2 border-cyan-400/50" />
                      <div className="absolute top-0 right-0 w-4 h-4 border-r-2 border-t-2 border-cyan-400/50" />
                      <div className="absolute bottom-0 left-0 w-4 h-4 border-l-2 border-b-2 border-cyan-400/50" />
                      <div className="absolute bottom-0 right-0 w-4 h-4 border-r-2 border-b-2 border-cyan-400/50" />
                    </div>
                  </div>
                </div>

                {/* HUD Overlay */}
                <div className="absolute top-2 left-2 flex items-center gap-2">
                  <motion.div
                    className="w-2 h-2 rounded-full bg-red-500"
                    animate={{
                      opacity: [1, 0.3, 1],
                      boxShadow: [
                        '0 0 5px #ff0000',
                        '0 0 15px #ff0000',
                        '0 0 5px #ff0000',
                      ],
                    }}
                    transition={{
                      duration: 2,
                      repeat: Infinity,
                    }}
                  />
                  <span className="text-xs text-red-500 font-mono">REC</span>
                </div>

                {/* Timestamp */}
                <div className="absolute top-2 right-2">
                  <span className="text-xs text-cyan-400/80 font-mono">
                    {new Date().toLocaleTimeString()}
                  </span>
                </div>

                {/* Bottom Info Bar */}
                <div className="absolute bottom-0 left-0 right-0 p-2 bg-gradient-to-t from-black/80 to-transparent">
                  <div className="flex items-center justify-between">
                    <div className="flex items-center gap-2">
                      <Radio className="w-3 h-3 text-cyan-400" />
                      <span className="text-xs text-cyan-400 font-mono">LIVE</span>
                    </div>
                    <div className="flex items-center gap-2">
                      <div className="w-1 h-1 rounded-full bg-green-400"
                           style={{ boxShadow: '0 0 5px #00ff00' }} />
                      <span className="text-xs text-green-400 uppercase">{camera.status}</span>
                    </div>
                  </div>
                </div>
              </div>

              {/* Camera Name */}
              <div className="p-3 border-t" style={{ borderColor: 'rgba(0, 212, 255, 0.2)' }}>
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-2">
                    <Camera className="w-4 h-4 text-cyan-400" />
                    <span className="text-sm text-cyan-400 font-medium">{camera.name}</span>
                  </div>
                  <motion.button
                    className="px-3 py-1 rounded-lg text-xs transition-all"
                    style={{
                      background: 'rgba(0, 212, 255, 0.1)',
                      border: '1px solid rgba(0, 212, 255, 0.3)',
                      color: '#00d4ff',
                    }}
                    whileHover={{
                      background: 'rgba(0, 212, 255, 0.2)',
                      boxShadow: '0 0 15px rgba(0, 212, 255, 0.3)',
                    }}
                  >
                    View
                  </motion.button>
                </div>
              </div>

              {/* Holographic Scan Line */}
              <motion.div
                className="absolute inset-x-0 h-px bg-gradient-to-r from-transparent via-cyan-400 to-transparent opacity-30 pointer-events-none"
                animate={{
                  top: ['0%', '100%'],
                }}
                transition={{
                  duration: 4,
                  repeat: Infinity,
                  ease: 'linear',
                  delay: index * 0.5,
                }}
              />
            </div>
          </motion.div>
        ))}
      </div>
    </div>
  );
}
