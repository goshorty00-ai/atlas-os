import { motion } from 'motion/react';
import { Brain, Zap, Activity, Wifi, WifiOff } from 'lucide-react';
import { useSmartHomeContext } from '../SmartHomeContext';

export function AI() {
  const { state, isDeviceOn } = useSmartHomeContext();

  if (!state) {
    return (
      <div className="flex items-center justify-center h-64">
        <p className="text-cyan-400/40 text-sm">Loading device data...</p>
      </div>
    );
  }

  const allDevices = state.providers.flatMap(p => p.devices);
  const onlineDevices = allDevices.filter(d => d.isOnline !== false);
  const offlineDevices = allDevices.filter(d => d.isOnline === false);
  const activeDevices = allDevices.filter(d => isDeviceOn(d));

  const configuredProviders = state.providers.filter(p => p.descriptor.isConfigured);

  return (
    <>
      {/* Header */}
      <motion.div className="mb-8" initial={{ opacity: 0, y: -20 }} animate={{ opacity: 1, y: 0 }}>
        <div className="flex items-center gap-3 mb-2">
          <div className="w-2 h-2 rounded-full bg-cyan-400 animate-pulse"
               style={{ boxShadow: '0 0 10px #00d4ff' }} />
          <h1 className="text-4xl font-bold"
            style={{ background: 'linear-gradient(135deg, #00d4ff, #0066ff)', WebkitBackgroundClip: 'text', WebkitTextFillColor: 'transparent' }}>
            AI Intelligence Hub
          </h1>
        </div>
        <p className="text-cyan-400/60 text-sm ml-5">Smart home overview and device status</p>
      </motion.div>

      {/* Live Stats */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-8">
        {[
          { label: 'Total Devices', value: allDevices.length, icon: Brain, color: '#00d4ff' },
          { label: 'Online', value: onlineDevices.length, icon: Wifi, color: '#4ECDC4' },
          { label: 'Active Now', value: activeDevices.length, icon: Zap, color: '#FFD700' },
          { label: 'Providers', value: configuredProviders.length, icon: Activity, color: '#95E1D3' },
        ].map(stat => (
          <motion.div key={stat.label}
            className="rounded-xl p-5 backdrop-blur-xl"
            style={{ background: 'rgba(5,10,18,0.6)', border: '1px solid rgba(0,212,255,0.3)', boxShadow: '0 0 20px rgba(0,212,255,0.15)' }}
            initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} whileHover={{ scale: 1.02 }}>
            <div className="flex items-center gap-2 mb-2">
              <stat.icon className="w-4 h-4" style={{ color: stat.color }} />
              <p className="text-cyan-400/60 text-xs">{stat.label}</p>
            </div>
            <p className="text-2xl font-bold" style={{ color: stat.color }}>{stat.value}</p>
          </motion.div>
        ))}
      </div>

      {/* Provider Status */}
      <div className="mb-8">
        <h3 className="text-sm text-cyan-400/80 mb-4 flex items-center gap-2">
          <div className="w-1 h-4 bg-cyan-400 rounded-full" style={{ boxShadow: '0 0 10px #00d4ff' }} />
          Connected Providers
        </h3>
        <div className="space-y-3">
          {configuredProviders.map(p => (
            <motion.div key={p.providerId}
              className="rounded-xl p-4 backdrop-blur-xl"
              style={{ background: 'rgba(0,212,255,0.05)', border: '1px solid rgba(0,212,255,0.2)' }}
              initial={{ opacity: 0, x: -20 }} animate={{ opacity: 1, x: 0 }} whileHover={{ x: 5 }}>
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-3">
                  <div className="w-2 h-2 rounded-full"
                    style={{ background: p.descriptor.status === 'Connected' ? '#4ECDC4' : '#ff6b35',
                             boxShadow: `0 0 8px ${p.descriptor.status === 'Connected' ? '#4ECDC4' : '#ff6b35'}` }} />
                  <div>
                    <p className="text-cyan-400 text-sm font-medium">{p.displayName}</p>
                    <p className="text-cyan-400/50 text-xs">{p.descriptor.status} · {p.devices.length} device{p.devices.length !== 1 ? 's' : ''}</p>
                  </div>
                </div>
                <span className="text-xs px-2 py-1 rounded-full"
                  style={{ background: 'rgba(0,212,255,0.1)', border: '1px solid rgba(0,212,255,0.3)', color: '#00d4ff' }}>
                  {p.devices.filter(d => isDeviceOn(d)).length} on
                </span>
              </div>
            </motion.div>
          ))}
          {configuredProviders.length === 0 && (
            <p className="text-cyan-400/40 text-sm text-center py-4">No providers configured. Go to Settings to add integrations.</p>
          )}
        </div>
      </div>

      {/* Active Devices */}
      {activeDevices.length > 0 && (
        <div className="mb-8">
          <h3 className="text-sm text-cyan-400/80 mb-4 flex items-center gap-2">
            <div className="w-1 h-4 bg-cyan-400 rounded-full" style={{ boxShadow: '0 0 10px #00d4ff' }} />
            Active Devices
          </h3>
          <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
            {activeDevices.map(device => {
              const provider = state.providers.find(p => p.devices.some(d => d.deviceId === device.deviceId));
              return (
                <motion.div key={device.deviceId}
                  className="rounded-xl p-4 backdrop-blur-xl"
                  style={{ background: 'rgba(0,212,255,0.08)', border: '1px solid rgba(0,212,255,0.3)', boxShadow: '0 0 15px rgba(0,212,255,0.1)' }}
                  initial={{ opacity: 0, y: 10 }} animate={{ opacity: 1, y: 0 }} whileHover={{ scale: 1.02 }}>
                  <div className="flex items-center gap-3">
                    <motion.div className="w-2 h-2 rounded-full bg-cyan-400"
                      style={{ boxShadow: '0 0 8px #00d4ff' }}
                      animate={{ opacity: [0.5, 1, 0.5] }}
                      transition={{ duration: 2, repeat: Infinity }} />
                    <div>
                      <p className="text-cyan-400 text-sm font-medium">{device.name}</p>
                      <p className="text-cyan-400/50 text-xs">{provider?.displayName ?? ''}</p>
                    </div>
                  </div>
                </motion.div>
              );
            })}
          </div>
        </div>
      )}

      {/* Offline Devices */}
      {offlineDevices.length > 0 && (
        <div>
          <h3 className="text-sm text-cyan-400/80 mb-4 flex items-center gap-2">
            <div className="w-1 h-4 bg-orange-400 rounded-full" style={{ boxShadow: '0 0 10px #ff8c00' }} />
            Offline Devices
          </h3>
          <div className="space-y-2">
            {offlineDevices.map(device => (
              <motion.div key={device.deviceId}
                className="rounded-xl p-4 backdrop-blur-xl"
                style={{ background: 'rgba(255,107,53,0.05)', border: '1px solid rgba(255,107,53,0.2)' }}
                initial={{ opacity: 0, x: -10 }} animate={{ opacity: 1, x: 0 }}>
                <div className="flex items-center gap-3">
                  <WifiOff className="w-4 h-4 text-orange-400/60" />
                  <p className="text-cyan-400/60 text-sm">{device.name}</p>
                </div>
              </motion.div>
            ))}
          </div>
        </div>
      )}
    </>
  );
}
