import { motion } from 'motion/react';
import { Bot, Camera, HandHeart, Home, LampDesk, MonitorSpeaker, PanelsTopLeft, Plug, Settings, Speaker, Tv, Video } from 'lucide-react';
import { useEffect, useState } from 'react';
import { getSidebarDeviceItems, type SidebarIconKey } from '../deviceSections';
import type { SmartHomeSnapshot } from '../types';

const navItems = [
  { id: 'agent', icon: Bot, label: 'Agent' },
  { id: 'home', icon: Home, label: 'Overview' },
  { id: 'cameras', icon: Camera, label: 'Cameras' },
  { id: 'devices', icon: PanelsTopLeft, label: 'Devices' },
  { id: 'providers', icon: Video, label: 'Providers' },
  { id: 'greetings', icon: HandHeart, label: 'Custom Greetings' },
  { id: 'responses', icon: MonitorSpeaker, label: 'Custom Responses' },
  { id: 'settings', icon: Settings, label: 'Settings' },
];

const deviceIconMap: Record<SidebarIconKey, typeof Home> = {
  agent: Bot,
  home: Home,
  settings: Settings,
  light: LampDesk,
  tv: Tv,
  speaker: Speaker,
  camera: Video,
  shield: Camera,
  plug: Plug,
  device: MonitorSpeaker,
};

interface SidebarProps {
  snapshot: SmartHomeSnapshot | null;
}

export function Sidebar({ snapshot }: SidebarProps) {
  const [activeItem, setActiveItem] = useState('home');
  const deviceItems = getSidebarDeviceItems(snapshot);
  const allItems = [...navItems, ...deviceItems];

  useEffect(() => {
    const onScroll = () => {
      const visible = allItems.find((item) => {
        const section = document.getElementById(item.id);
        if (!section) {
          return false;
        }

        const rect = section.getBoundingClientRect();
        return rect.top <= 180 && rect.bottom >= 180;
      });

      if (visible) {
        setActiveItem(visible.id);
      }
    };

    onScroll();
    window.addEventListener('scroll', onScroll, { passive: true });
    return () => window.removeEventListener('scroll', onScroll);
  }, [allItems]);

  const activateItem = (itemId: string) => {
    setActiveItem(itemId);
    const section = document.getElementById(itemId);
    if (section) {
      section.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
  };

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
        <nav className="flex-1 min-h-0 flex flex-col items-center gap-2 px-2 overflow-y-auto pb-4">
          {navItems.map((item) => {
            const isActive = activeItem === item.id;
            
            return (
              <motion.button
                key={item.id}
                type="button"
                onClick={() => activateItem(item.id)}
                aria-label={item.label}
                className="relative w-14 h-14 rounded-xl flex items-center justify-center group cursor-pointer select-none focus:outline-none"
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

          {deviceItems.length > 0 && (
            <>
              <div className="w-10 h-px my-2 bg-gradient-to-r from-transparent via-cyan-400/60 to-transparent" />
              {deviceItems.map((item) => {
                const isActive = activeItem === item.id;
                const Icon = deviceIconMap[item.iconKey] ?? MonitorSpeaker;

                return (
                  <motion.button
                    key={item.id}
                    type="button"
                    onClick={() => activateItem(item.id)}
                    aria-label={item.label}
                    className="relative w-14 h-14 rounded-xl flex items-center justify-center group cursor-pointer select-none focus:outline-none"
                    whileHover={{ scale: 1.08 }}
                    whileTap={{ scale: 0.95 }}
                  >
                    <motion.div
                      className="absolute inset-0 rounded-xl"
                      style={{
                        background: isActive
                          ? 'rgba(0, 212, 255, 0.18)'
                          : item.isOnline === false
                            ? 'rgba(255, 185, 112, 0.08)'
                            : 'rgba(0, 212, 255, 0.05)',
                        border: isActive
                          ? '1px solid rgba(0, 212, 255, 0.45)'
                          : item.isOnline === false
                            ? '1px solid rgba(255, 185, 112, 0.24)'
                            : '1px solid rgba(0, 212, 255, 0.18)',
                      }}
                      animate={isActive ? {
                        boxShadow: [
                          '0 0 14px rgba(0, 212, 255, 0.35)',
                          '0 0 22px rgba(0, 212, 255, 0.55)',
                          '0 0 14px rgba(0, 212, 255, 0.35)',
                        ],
                      } : {}}
                      transition={{ duration: 2, repeat: Infinity }}
                    />

                    <Icon
                      className="relative z-10 w-4 h-4"
                      style={{
                        color: isActive
                          ? '#00d4ff'
                          : item.isOnline === false
                            ? '#FFB970'
                            : 'rgba(0, 212, 255, 0.72)',
                      }}
                    />

                    <div
                      className="absolute left-1.5 top-1.5 w-1.5 h-1.5 rounded-full"
                      style={{
                        background: item.isOnline === false ? '#FFB970' : '#7CFFB2',
                        boxShadow: item.isOnline === false ? '0 0 8px rgba(255,185,112,0.8)' : '0 0 8px rgba(124,255,178,0.8)',
                      }}
                    />

                    {isActive && (
                      <motion.div
                        className="absolute -right-1 top-1/2 -translate-y-1/2 w-1 h-6 rounded-full bg-cyan-400"
                        style={{ boxShadow: '0 0 10px #00d4ff' }}
                        layoutId="activeIndicator"
                      />
                    )}

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
                      <p className="text-[10px] text-cyan-200/55 mt-1 tracking-[0.12em] uppercase">{item.providerLabel}</p>
                      <div
                        className="absolute right-full top-1/2 -translate-y-1/2 border-4 border-transparent"
                        style={{ borderRightColor: 'rgba(0, 212, 255, 0.3)' }}
                      />
                    </motion.div>
                  </motion.button>
                );
              })}
            </>
          )}
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
