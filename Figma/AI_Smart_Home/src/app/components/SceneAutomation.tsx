import { motion } from 'motion/react';
import { Film, Moon, Lock, PartyPopper, Sun } from 'lucide-react';
import { useState } from 'react';

const scenes = [
  { id: 'movie', name: 'Movie Mode', icon: Film, color: '#8b00ff' },
  { id: 'night', name: 'Night Mode', icon: Moon, color: '#4169e1' },
  { id: 'away', name: 'Away Mode', icon: Lock, color: '#ff4500' },
  { id: 'party', name: 'Party Mode', icon: PartyPopper, color: '#ff1493' },
  { id: 'morning', name: 'Morning Mode', icon: Sun, color: '#ffa500' },
];

export function SceneAutomation() {
  const [activeScene, setActiveScene] = useState<string | null>(null);

  return (
    <div className="mt-8">
      <h3 className="text-sm text-cyan-400/80 mb-4 flex items-center gap-2">
        <div className="w-1 h-4 bg-cyan-400 rounded-full" style={{ boxShadow: '0 0 10px #00d4ff' }} />
        Scene Automation
      </h3>
      <div className="grid grid-cols-2 md:grid-cols-5 gap-4">
        {scenes.map((scene) => {
          const isActive = activeScene === scene.id;
          
          return (
            <motion.button
              key={scene.id}
              onClick={() => setActiveScene(isActive ? null : scene.id)}
              className="relative group"
              whileHover={{ scale: 1.05 }}
              whileTap={{ scale: 0.95 }}
            >
              <div
                className="relative rounded-xl p-6 backdrop-blur-xl transition-all duration-300"
                style={{
                  background: isActive 
                    ? `linear-gradient(135deg, ${scene.color}30, rgba(5, 10, 18, 0.8))`
                    : 'rgba(5, 10, 18, 0.6)',
                  border: `1px solid ${isActive ? scene.color : `${scene.color}40`}`,
                  boxShadow: isActive
                    ? `0 0 40px ${scene.color}60, inset 0 0 40px ${scene.color}20`
                    : `0 0 20px ${scene.color}20, inset 0 0 20px ${scene.color}10`,
                }}
              >
                {/* Icon */}
                <div className="flex flex-col items-center gap-3">
                  <div
                    className="w-12 h-12 rounded-full flex items-center justify-center transition-all duration-300"
                    style={{
                      background: `${scene.color}20`,
                      border: `2px solid ${scene.color}`,
                      boxShadow: isActive 
                        ? `0 0 30px ${scene.color}` 
                        : `0 0 15px ${scene.color}60`,
                    }}
                  >
                    <motion.div
                      animate={isActive ? {
                        rotate: [0, 360],
                      } : {}}
                      transition={{
                        duration: 3,
                        repeat: Infinity,
                        ease: 'linear',
                      }}
                    >
                      <scene.icon
                        className="w-6 h-6"
                        style={{ color: scene.color }}
                      />
                    </motion.div>
                  </div>
                  
                  <div className="text-center">
                    <p
                      className="text-sm font-medium"
                      style={{
                        color: scene.color,
                        textShadow: isActive ? `0 0 10px ${scene.color}` : 'none',
                      }}
                    >
                      {scene.name}
                    </p>
                  </div>
                </div>

                {/* Active Indicator */}
                {isActive && (
                  <>
                    <motion.div
                      className="absolute top-2 right-2 w-2 h-2 rounded-full"
                      style={{ backgroundColor: scene.color }}
                      animate={{
                        opacity: [1, 0.3, 1],
                        boxShadow: [
                          `0 0 5px ${scene.color}`,
                          `0 0 15px ${scene.color}`,
                          `0 0 5px ${scene.color}`,
                        ],
                      }}
                      transition={{
                        duration: 2,
                        repeat: Infinity,
                      }}
                    />
                    
                    {/* Energy Particles */}
                    {[...Array(4)].map((_, i) => (
                      <motion.div
                        key={i}
                        className="absolute w-1 h-1 rounded-full"
                        style={{
                          backgroundColor: scene.color,
                          left: '50%',
                          top: '50%',
                        }}
                        animate={{
                          x: [0, (i % 2 === 0 ? 20 : -20)],
                          y: [0, (i < 2 ? -20 : 20)],
                          opacity: [1, 0],
                          scale: [1, 0],
                        }}
                        transition={{
                          duration: 1.5,
                          repeat: Infinity,
                          delay: i * 0.2,
                        }}
                      />
                    ))}
                  </>
                )}

                {/* Corner Accents */}
                <div className="absolute bottom-0 left-0 w-8 h-8 border-l-2 border-b-2 rounded-bl-xl transition-all duration-300"
                     style={{
                       borderColor: scene.color,
                       opacity: isActive ? 1 : 0.3,
                       boxShadow: isActive ? `0 0 10px ${scene.color}` : 'none',
                     }} />
              </div>
            </motion.button>
          );
        })}
      </div>
    </div>
  );
}
