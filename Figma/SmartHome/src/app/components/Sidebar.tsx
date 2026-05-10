import { motion } from 'motion/react';
import { Home, Video, Bot, Smartphone, Shield, Settings, Puzzle } from 'lucide-react';
import { useNavigate, useLocation } from 'react-router';

const navItems = [
  { id: 'home', path: '/', icon: Home, label: 'Home' },
  { id: 'smart-devices', path: '/smart-devices', icon: Smartphone, label: 'Smart Devices' },
  { id: 'integrations', path: '/integrations', icon: Puzzle, label: 'Integrations' },
  { id: 'media', path: '/media', icon: Video, label: 'Media' },
  { id: 'ai', path: '/ai', icon: Bot, label: 'AI Assistant' },
  { id: 'security', path: '/security', icon: Shield, label: 'Security' },
  { id: 'settings', path: '/settings', icon: Settings, label: 'Settings' },
];

export function Sidebar() {
  const navigate = useNavigate();
  const location = useLocation();

  return (
    <div className="fixed left-0 top-0 bottom-0 w-20 flex flex-col items-center py-8 z-50">
      <div
        className="relative h-full rounded-r-3xl backdrop-blur-xl"
        style={{
          background: 'rgba(5, 10, 18, 0.8)',
          border: '1px solid rgba(0, 212, 255, 0.2)',
          borderLeft: 'none',
          boxShadow: '0 0 30px rgba(0, 212, 255, 0.15)',
        }}
      >
        {/* Logo */}
        <div className="flex justify-center mb-8 pt-2">
          <div className="relative">
            <motion.div
              className="w-12 h-12 rounded-xl flex items-center justify-center"
              style={{
                background: 'linear-gradient(135deg, #00d4ff, #0066ff)',
                boxShadow: '0 0 30px rgba(0, 212, 255, 0.6)',
              }}
              animate={{
                boxShadow: [
                  '0 0 30px rgba(0, 212, 255, 0.6)',
                  '0 0 50px rgba(0, 212, 255, 0.9)',
                  '0 0 30px rgba(0, 212, 255, 0.6)',
                ],
              }}
              transition={{
                duration: 2,
                repeat: Infinity,
              }}
            >
              <div className="w-6 h-6 border-2 border-white rounded-lg" />
            </motion.div>
            
            {/* Energy Ring */}
            <motion.div
              className="absolute inset-0 rounded-xl border-2 border-cyan-400"
              animate={{
                scale: [1, 1.3],
                opacity: [0.6, 0],
              }}
              transition={{
                duration: 2,
                repeat: Infinity,
              }}
            />
          </div>
        </div>

        {/* Navigation Items */}
        <nav className="flex-1 flex flex-col items-center gap-2 px-2">
          {navItems.map((item) => {
            const isActive = location.pathname === item.path;
            
            return (
              <motion.button
                key={item.id}
                onClick={() => navigate(item.path)}
                className="relative w-14 h-14 rounded-xl flex items-center justify-center group"
                whileHover={{ scale: 1.1 }}
                whileTap={{ scale: 0.95 }}
              >
                {/* Background */}
                <motion.div
                  className="absolute inset-0 rounded-xl"
                  style={{
                    background: isActive 
                      ? 'rgba(0, 212, 255, 0.2)' 
                      : 'rgba(0, 212, 255, 0.05)',
                    border: `1px solid ${isActive ? '#00d4ff' : 'rgba(0, 212, 255, 0.2)'}`,
                  }}
                  animate={isActive ? {
                    boxShadow: [
                      '0 0 15px rgba(0, 212, 255, 0.4)',
                      '0 0 25px rgba(0, 212, 255, 0.6)',
                      '0 0 15px rgba(0, 212, 255, 0.4)',
                    ],
                  } : {}}
                  transition={{ duration: 2, repeat: Infinity }}
                />

                {/* Icon */}
                <item.icon
                  className="w-5 h-5 relative z-10 transition-colors"
                  style={{
                    color: isActive ? '#00d4ff' : 'rgba(0, 212, 255, 0.6)',
                  }}
                />

                {/* Active Indicator */}
                {isActive && (
                  <motion.div
                    className="absolute -right-1 top-1/2 -translate-y-1/2 w-1 h-6 rounded-full bg-cyan-400"
                    style={{
                      boxShadow: '0 0 10px #00d4ff',
                    }}
                    layoutId="activeIndicator"
                  />
                )}

                {/* Tooltip */}
                <motion.div
                  className="absolute left-full ml-4 px-3 py-2 rounded-lg whitespace-nowrap pointer-events-none"
                  style={{
                    background: 'rgba(5, 10, 18, 0.95)',
                    border: '1px solid rgba(0, 212, 255, 0.3)',
                    boxShadow: '0 0 20px rgba(0, 212, 255, 0.2)',
                  }}
                  initial={{ opacity: 0, x: -10 }}
                  whileHover={{ opacity: 1, x: 0 }}
                >
                  <span className="text-sm text-cyan-400">{item.label}</span>
                  <div
                    className="absolute right-full top-1/2 -translate-y-1/2 border-4 border-transparent"
                    style={{
                      borderRightColor: 'rgba(0, 212, 255, 0.3)',
                    }}
                  />
                </motion.div>

                {/* Hover Effect */}
                <motion.div
                  className="absolute inset-0 rounded-xl opacity-0 group-hover:opacity-100 transition-opacity"
                  style={{
                    background: 'rgba(0, 212, 255, 0.1)',
                  }}
                />
              </motion.button>
            );
          })}
        </nav>

        {/* Bottom Decoration */}
        <div className="mt-auto flex flex-col items-center gap-2 mb-4">
          <div className="w-8 h-px bg-gradient-to-r from-transparent via-cyan-400 to-transparent" />
          <motion.div
            className="w-2 h-2 rounded-full bg-cyan-400"
            animate={{
              opacity: [0.3, 1, 0.3],
              boxShadow: [
                '0 0 5px #00d4ff',
                '0 0 15px #00d4ff',
                '0 0 5px #00d4ff',
              ],
            }}
            transition={{
              duration: 2,
              repeat: Infinity,
            }}
          />
        </div>
      </div>
    </div>
  );
}