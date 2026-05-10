import { motion } from "motion/react";
import { Zap, Wifi, WifiOff, Power, Lightbulb, Thermometer, Lock, Camera, Speaker, Tv, Wind, Droplet, Sun, Moon, AlertCircle, CheckCircle, Clock, Activity } from "lucide-react";
import { useSmartHomeContext } from "../SmartHomeContext";
import { useState } from "react";

export function SmartDevices() {
  const { state, executeAction } = useSmartHomeContext();
  const [selectedDevice, setSelectedDevice] = useState<string | null>(null);
  const [filterStatus, setFilterStatus] = useState<'all' | 'online' | 'offline'>('all');
  const [filterType, setFilterType] = useState<string>('all');

  if (!state) {
    return (
      <div className="flex items-center justify-center h-full">
        <div className="text-center">
          <div className="w-16 h-16 border-4 border-cyan-400 border-t-transparent rounded-full animate-spin mx-auto mb-4" />
          <p className="text-cyan-400">Loading smart home devices...</p>
        </div>
      </div>
    );
  }

  const devices = state.devices || [];
  
  const filteredDevices = devices.filter(device => {
    const statusMatch = filterStatus === 'all' || 
                       (filterStatus === 'online' && device.isOnline) ||
                       (filterStatus === 'offline' && !device.isOnline);
    const typeMatch = filterType === 'all' || device.deviceType === filterType;
    return statusMatch && typeMatch;
  });

  const deviceTypes = ['all', ...Array.from(new Set(devices.map(d => d.deviceType)))];
  const totalDevices = devices.length;
  const onlineDevices = devices.filter(d => d.isOnline).length;
  const offlineDevices = devices.filter(d => !d.isOnline).length;
  const activeDevices = devices.filter(d => d.isOnline && d.state?.power === 'on').length;

  const getDeviceIcon = (type: string) => {
    switch (type.toLowerCase()) {
      case 'light':
      case 'bulb':
        return <Lightbulb className="w-5 h-5" />;
      case 'thermostat':
      case 'temperature':
        return <Thermometer className="w-5 h-5" />;
      case 'lock':
        return <Lock className="w-5 h-5" />;
      case 'camera':
        return <Camera className="w-5 h-5" />;
      case 'speaker':
      case 'audio':
        return <Speaker className="w-5 h-5" />;
      case 'tv':
      case 'television':
        return <Tv className="w-5 h-5" />;
      case 'fan':
        return <Wind className="w-5 h-5" />;
      case 'sensor':
        return <Activity className="w-5 h-5" />;
      default:
        return <Zap className="w-5 h-5" />;
    }
  };

  const handleDeviceAction = async (deviceId: string, action: string, value?: any) => {
    try {
      await executeAction({
        providerId: devices.find(d => d.deviceId === deviceId)?.providerId || '',
        deviceId,
        action,
        value
      });
    } catch (error) {
      console.error('Device action failed:', error);
    }
  };

  return (
    <div className="h-full flex flex-col">
      <motion.div className="mb-6" initial={{ opacity: 0, y: -20 }} animate={{ opacity: 1, y: 0 }}>
        <div className="flex items-center gap-3 mb-2">
          <div className="w-2 h-2 rounded-full bg-cyan-400 animate-pulse" style={{ boxShadow: '0 0 10px #00d4ff' }} />
          <h1 className="text-4xl font-bold" style={{ background: 'linear-gradient(135deg, #00d4ff, #0066ff)', WebkitBackgroundClip: 'text', WebkitTextFillColor: 'transparent' }}>Smart Devices</h1>
        </div>
        <p className="text-cyan-400/60 text-sm ml-5">Manage and control all your connected devices</p>
      </motion.div>
      <div className="grid grid-cols-4 gap-4 mb-6">
        <motion.div className="rounded-xl p-4 backdrop-blur-xl" style={{ background: 'rgba(0,212,255,0.08)', border: '1px solid rgba(0,212,255,0.3)', boxShadow: '0 0 20px rgba(0,212,255,0.1)' }} initial={{ opacity: 0, scale: 0.9 }} animate={{ opacity: 1, scale: 1 }} transition={{ delay: 0.1 }}>
          <div className="flex items-center justify-between mb-2">
            <span className="text-xs text-cyan-400/60">Total Devices</span>
            <Zap className="w-4 h-4 text-cyan-400" />
          </div>
          <div className="text-2xl font-bold text-cyan-400">{totalDevices}</div>
        </motion.div>
        <motion.div className="rounded-xl p-4 backdrop-blur-xl" style={{ background: 'rgba(0,255,0,0.08)', border: '1px solid rgba(0,255,0,0.3)', boxShadow: '0 0 20px rgba(0,255,0,0.1)' }} initial={{ opacity: 0, scale: 0.9 }} animate={{ opacity: 1, scale: 1 }} transition={{ delay: 0.2 }}>
          <div className="flex items-center justify-between mb-2">
            <span className="text-xs text-green-400/60">Online</span>
            <CheckCircle className="w-4 h-4 text-green-400" />
          </div>
          <div className="text-2xl font-bold text-green-400">{onlineDevices}</div>
        </motion.div>
        <motion.div className="rounded-xl p-4 backdrop-blur-xl" style={{ background: 'rgba(255,140,0,0.08)', border: '1px solid rgba(255,140,0,0.3)', boxShadow: '0 0 20px rgba(255,140,0,0.1)' }} initial={{ opacity: 0, scale: 0.9 }} animate={{ opacity: 1, scale: 1 }} transition={{ delay: 0.3 }}>
          <div className="flex items-center justify-between mb-2">
            <span className="text-xs text-orange-400/60">Offline</span>
            <AlertCircle className="w-4 h-4 text-orange-400" />
          </div>
          <div className="text-2xl font-bold text-orange-400">{offlineDevices}</div>
        </motion.div>
        <motion.div className="rounded-xl p-4 backdrop-blur-xl" style={{ background: 'rgba(138,43,226,0.08)', border: '1px solid rgba(138,43,226,0.3)', boxShadow: '0 0 20px rgba(138,43,226,0.1)' }} initial={{ opacity: 0, scale: 0.9 }} animate={{ opacity: 1, scale: 1 }} transition={{ delay: 0.4 }}>
          <div className="flex items-center justify-between mb-2">
            <span className="text-xs text-purple-400/60">Active</span>
            <Power className="w-4 h-4 text-purple-400" />
          </div>
          <div className="text-2xl font-bold text-purple-400">{activeDevices}</div>
        </motion.div>
      </div>
      <div className="flex gap-4 mb-6">
        <div className="flex gap-2">
          <button onClick={() => setFilterStatus('all')} className={`px-4 py-2 rounded-lg text-sm transition-all ${filterStatus === 'all' ? 'text-cyan-400' : 'text-white/60'}`} style={{ background: filterStatus === 'all' ? 'rgba(0,212,255,0.2)' : 'rgba(255,255,255,0.05)', border: `1px solid ${filterStatus === 'all' ? 'rgba(0,212,255,0.5)' : 'rgba(255,255,255,0.1)'}` }}>All</button>
          <button onClick={() => setFilterStatus('online')} className={`px-4 py-2 rounded-lg text-sm transition-all ${filterStatus === 'online' ? 'text-green-400' : 'text-white/60'}`} style={{ background: filterStatus === 'online' ? 'rgba(0,255,0,0.2)' : 'rgba(255,255,255,0.05)', border: `1px solid ${filterStatus === 'online' ? 'rgba(0,255,0,0.5)' : 'rgba(255,255,255,0.1)'}` }}><Wifi className="w-4 h-4 inline mr-1" />Online</button>
          <button onClick={() => setFilterStatus('offline')} className={`px-4 py-2 rounded-lg text-sm transition-all ${filterStatus === 'offline' ? 'text-orange-400' : 'text-white/60'}`} style={{ background: filterStatus === 'offline' ? 'rgba(255,140,0,0.2)' : 'rgba(255,255,255,0.05)', border: `1px solid ${filterStatus === 'offline' ? 'rgba(255,140,0,0.5)' : 'rgba(255,255,255,0.1)'}` }}><WifiOff className="w-4 h-4 inline mr-1" />Offline</button>
        </div>
        <select value={filterType} onChange={(e) => setFilterType(e.target.value)} className="px-4 py-2 rounded-lg text-sm bg-transparent outline-none" style={{ background: 'rgba(255,255,255,0.05)', border: '1px solid rgba(0,212,255,0.3)', color: '#00d4ff' }}>
          {deviceTypes.map(type => (<option key={type} value={type} style={{ background: '#0a0f1a', color: '#00d4ff' }}>{type === 'all' ? 'All Types' : type.charAt(0).toUpperCase() + type.slice(1)}</option>))}
        </select>
      </div>
      <div className="flex-1 overflow-y-auto">
        {filteredDevices.length === 0 ? (
          <div className="flex items-center justify-center h-full">
            <div className="text-center">
              <AlertCircle className="w-16 h-16 text-cyan-400/40 mx-auto mb-4" />
              <p className="text-cyan-400/60">No devices found</p>
            </div>
          </div>
        ) : (
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4 pb-6">
            {filteredDevices.map((device, index) => (
              <motion.div key={device.deviceId} className="rounded-xl p-4 backdrop-blur-xl cursor-pointer" style={{ background: device.isOnline ? 'rgba(0,212,255,0.08)' : 'rgba(100,100,100,0.08)', border: `1px solid ${device.isOnline ? 'rgba(0,212,255,0.3)' : 'rgba(100,100,100,0.3)'}`, boxShadow: device.isOnline ? '0 0 20px rgba(0,212,255,0.1)' : 'none' }} initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: index * 0.05 }} onClick={() => setSelectedDevice(selectedDevice === device.deviceId ? null : device.deviceId)} whileHover={{ scale: 1.02 }} whileTap={{ scale: 0.98 }}>
                <div className="flex items-start justify-between mb-3">
                  <div className="flex items-center gap-3">
                    <div className="w-10 h-10 rounded-lg flex items-center justify-center" style={{ background: device.isOnline ? 'rgba(0,212,255,0.2)' : 'rgba(100,100,100,0.2)', border: `1px solid ${device.isOnline ? 'rgba(0,212,255,0.4)' : 'rgba(100,100,100,0.4)'}` }}>
                      <div className={device.isOnline ? 'text-cyan-400' : 'text-gray-400'}>{getDeviceIcon(device.deviceType)}</div>
                    </div>
                    <div>
                      <h3 className={`font-medium ${device.isOnline ? 'text-cyan-400' : 'text-gray-400'}`}>{device.name}</h3>
                      <p className="text-xs text-white/40">{device.deviceType}</p>
                    </div>
                  </div>
                  <div className="flex items-center gap-2">
                    <div className="w-2 h-2 rounded-full" style={{ background: device.isOnline ? '#00ff00' : '#ff8c00', boxShadow: device.isOnline ? '0 0 8px #00ff00' : '0 0 8px #ff8c00' }} />
                  </div>
                </div>
                <div className="space-y-2 text-sm">
                  {device.room && (<div className="flex items-center justify-between"><span className="text-white/40">Room</span><span className="text-cyan-400">{device.room}</span></div>)}
                  {device.state?.power && (<div className="flex items-center justify-between"><span className="text-white/40">Power</span><span className={device.state.power === 'on' ? 'text-green-400' : 'text-gray-400'}>{device.state.power.toUpperCase()}</span></div>)}
                  {device.state?.brightness !== undefined && (<div className="flex items-center justify-between"><span className="text-white/40">Brightness</span><span className="text-cyan-400">{device.state.brightness}%</span></div>)}
                  {device.state?.temperature !== undefined && (<div className="flex items-center justify-between"><span className="text-white/40">Temperature</span><span className="text-cyan-400">{device.state.temperature}°</span></div>)}
                  {device.state?.volume !== undefined && (<div className="flex items-center justify-between"><span className="text-white/40">Volume</span><span className="text-cyan-400">{device.state.volume}%</span></div>)}
                  {device.state?.locked !== undefined && (<div className="flex items-center justify-between"><span className="text-white/40">Status</span><span className={device.state.locked ? 'text-green-400' : 'text-orange-400'}>{device.state.locked ? 'Locked' : 'Unlocked'}</span></div>)}
                </div>
                {selectedDevice === device.deviceId && device.isOnline && (
                  <motion.div className="mt-4 pt-4 border-t border-cyan-400/20 flex gap-2" initial={{ opacity: 0, height: 0 }} animate={{ opacity: 1, height: 'auto' }}>
                    {device.capabilities?.includes('power') && (<button onClick={(e) => { e.stopPropagation(); handleDeviceAction(device.deviceId, 'power', device.state?.power === 'on' ? 'off' : 'on'); }} className="flex-1 px-3 py-2 rounded-lg text-xs font-medium transition-all" style={{ background: 'rgba(0,212,255,0.2)', border: '1px solid rgba(0,212,255,0.4)', color: '#00d4ff' }}><Power className="w-3 h-3 inline mr-1" />{device.state?.power === 'on' ? 'Turn Off' : 'Turn On'}</button>)}
                    {device.capabilities?.includes('brightness') && (<button onClick={(e) => { e.stopPropagation(); handleDeviceAction(device.deviceId, 'brightness', 100); }} className="flex-1 px-3 py-2 rounded-lg text-xs font-medium transition-all" style={{ background: 'rgba(0,212,255,0.2)', border: '1px solid rgba(0,212,255,0.4)', color: '#00d4ff' }}><Sun className="w-3 h-3 inline mr-1" />Max</button>)}
                  </motion.div>
                )}
              </motion.div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}