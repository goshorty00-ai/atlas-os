import { motion } from 'motion/react';
import { Lightbulb, PlugZap, Shield, Wifi, Wrench } from 'lucide-react';
import type { SmartHomeSnapshot } from '../types';

interface StatusHUDProps {
  snapshot: SmartHomeSnapshot | null;
}

export function StatusHUD({ snapshot }: StatusHUDProps) {
  const stats = [
    { icon: PlugZap, label: 'Live Devices', value: snapshot ? String(snapshot.totalDevices) : '--', color: '#00d4ff' },
    { icon: Wifi, label: 'Online Devices', value: snapshot ? String(snapshot.onlineDevices) : '--', color: '#00ffcc' },
    { icon: Wrench, label: 'Configured Providers', value: snapshot ? String(snapshot.configuredProviders) : '--', color: '#00d4ff' },
    {
      icon: Shield,
      label: 'Bridge Status',
      value: snapshot ? 'Connected' : 'Waiting',
      color: snapshot ? '#00ff88' : '#ff8c00',
    },
    {
      icon: Lightbulb,
      label: 'Last Sync',
      value: snapshot ? new Date(snapshot.generatedAtUtc).toLocaleTimeString() : '--:--',
      color: '#ff8c00',
    },
  ];

  return (
    <div className="flex gap-4 flex-wrap">
      {stats.map((stat, index) => (
        <motion.div
          key={stat.label}
          className="relative flex-1 min-w-[180px]"
          initial={{ opacity: 0, y: -20 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: index * 0.1 }}
        >
          <div
            className="relative rounded-xl p-4 backdrop-blur-xl"
            style={{
              background: 'rgba(5, 10, 18, 0.6)',
              border: `1px solid ${stat.color}40`,
              boxShadow: `0 0 20px ${stat.color}20, inset 0 0 20px ${stat.color}10`,
            }}
          >
            {/* Corner Decoration */}
            <div className="absolute top-0 right-0 w-12 h-12">
              <div className="absolute top-1 right-1 w-3 h-3 border-t-2 border-r-2 rounded-tr-lg"
                   style={{ borderColor: stat.color, boxShadow: `0 0 5px ${stat.color}` }} />
            </div>

            <div className="flex items-center gap-3">
              <div
                className="w-10 h-10 rounded-lg flex items-center justify-center"
                style={{
                  background: `${stat.color}15`,
                  border: `1px solid ${stat.color}40`,
                  boxShadow: `0 0 15px ${stat.color}30`,
                }}
              >
                <stat.icon className="w-5 h-5" style={{ color: stat.color }} />
              </div>
              <div className="flex-1">
                <p className="text-xs opacity-70" style={{ color: stat.color }}>
                  {stat.label}
                </p>
                <p className="text-lg font-semibold mt-0.5" 
                   style={{ color: stat.color, textShadow: `0 0 10px ${stat.color}80` }}>
                  {stat.value}
                </p>
              </div>
            </div>

            {/* Animated Border */}
            <motion.div
              className="absolute inset-0 rounded-xl pointer-events-none"
              style={{
                border: `1px solid ${stat.color}`,
                opacity: 0,
              }}
              animate={{
                opacity: [0, 0.5, 0],
              }}
              transition={{
                duration: 2,
                repeat: Infinity,
                delay: index * 0.4,
              }}
            />
          </div>
        </motion.div>
      ))}
    </div>
  );
}
