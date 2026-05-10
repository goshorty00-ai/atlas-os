import { motion } from 'motion/react';
import { Lightbulb, Thermometer, Cloud, Shield, Zap } from 'lucide-react';
import { useSmartHomeContext } from '../SmartHomeContext';

export function StatusHUD() {
  const { state, isDeviceOn, getAllDevices } = useSmartHomeContext();

  const allDevices = getAllDevices();
  const lights = allDevices.filter(d =>
    d.deviceType?.toLowerCase().includes('light') ||
    d.deviceType?.toLowerCase().includes('bulb') ||
    d.deviceType?.toLowerCase().includes('strip')
  );
  const activeLights = lights.filter(d => isDeviceOn(d)).length;

  const ringProvider = state?.providers.find(p => p.providerId === 'ring');
  const ringConfigured = ringProvider?.descriptor.isConfigured ?? false;

  const stats = [
    {
      icon: Lightbulb,
      label: 'Active Lights',
      value: state ? `${activeLights}/${lights.length}` : '—',
      color: '#00d4ff',
    },
    {
      icon: Thermometer,
      label: 'Devices Online',
      value: state ? `${state.onlineDevices}/${state.totalDevices}` : '—',
      color: '#00d4ff',
    },
    {
      icon: Cloud,
      label: 'Providers',
      value: state ? `${state.configuredProviders} Active` : '—',
      color: '#00d4ff',
    },
    {
      icon: Shield,
      label: 'Security',
      value: ringConfigured ? 'Armed' : 'Not Set',
      color: ringConfigured ? '#00ff00' : '#ff8c00',
    },
    {
      icon: Zap,
      label: 'Total Devices',
      value: state ? String(state.totalDevices) : '—',
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

            <motion.div
              className="absolute inset-0 rounded-xl pointer-events-none"
              style={{ border: `1px solid ${stat.color}`, opacity: 0 }}
              animate={{ opacity: [0, 0.5, 0] }}
              transition={{ duration: 2, repeat: Infinity, delay: index * 0.4 }}
            />
          </div>
        </motion.div>
      ))}
    </div>
  );
}
