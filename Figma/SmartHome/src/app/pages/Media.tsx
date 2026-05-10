import { motion } from 'motion/react';
import { Play, Pause, Volume2, Tv, VolumeX } from 'lucide-react';
import { useSmartHomeContext } from '../SmartHomeContext';
import type { SmartHomeDevice } from '../useSmartHome';

function getTvPower(device: SmartHomeDevice): boolean {
  const cap = device.capabilities.find(c =>
    c.type === 'devices.capabilities.on_off' && c.instance === 'powerSwitch'
  );
  if (!cap?.hasState) return false;
  const v = cap.stateValue as any;
  return v === true || v === 1 || v?.value === true || v?.value === 1;
}

function getTvVolume(device: SmartHomeDevice): number {
  const cap = device.capabilities.find(c =>
    c.type === 'devices.capabilities.range' && c.instance === 'volume'
  );
  if (!cap?.hasState) return 0;
  const v = cap.stateValue as any;
  return typeof v === 'number' ? v : v?.value ?? 0;
}

function getTvMute(device: SmartHomeDevice): boolean {
  const cap = device.capabilities.find(c =>
    c.type === 'devices.capabilities.toggle' && c.instance === 'mute'
  );
  if (!cap?.hasState) return false;
  const v = cap.stateValue as any;
  return v === true || v === 1 || v?.value === true || v?.value === 1;
}

export function Media() {
  const { state, executeAction, getDeviceVolume } = useSmartHomeContext();

  const lgProvider = state?.providers.find(p => p.providerId === 'lg_webos');
  const tvDevices = lgProvider?.devices ?? [];

  const togglePower = (device: SmartHomeDevice) => {
    const on = getTvPower(device);
    executeAction('lg_webos', device.deviceId, device.sku, 'devices.capabilities.on_off', 'powerSwitch', !on);
  };

  const toggleMute = (device: SmartHomeDevice) => {
    const muted = getTvMute(device);
    executeAction('lg_webos', device.deviceId, device.sku, 'devices.capabilities.toggle', 'mute', !muted);
  };

  const setVolume = (device: SmartHomeDevice, vol: number) => {
    executeAction('lg_webos', device.deviceId, device.sku, 'devices.capabilities.range', 'volume', vol);
  };

  return (
    <>
      <motion.div className="mb-8" initial={{ opacity: 0, y: -20 }} animate={{ opacity: 1, y: 0 }}>
        <div className="flex items-center gap-3 mb-2">
          <div className="w-2 h-2 rounded-full bg-cyan-400 animate-pulse" style={{ boxShadow: '0 0 10px #00d4ff' }} />
          <h1 className="text-4xl font-bold"
              style={{ background: 'linear-gradient(135deg, #00d4ff, #0066ff)', WebkitBackgroundClip: 'text', WebkitTextFillColor: 'transparent' }}>
            Media Control Centre
          </h1>
        </div>
        <p className="text-cyan-400/60 text-sm ml-5">Control all entertainment devices</p>
      </motion.div>

      {/* LG TV Devices */}
      {tvDevices.length === 0 ? (
        <div className="rounded-xl p-8 text-center backdrop-blur-xl"
             style={{ background: 'rgba(5,10,18,0.6)', border: '1px solid rgba(0,212,255,0.2)' }}>
          <Tv className="w-12 h-12 text-cyan-400/30 mx-auto mb-3" />
          <p className="text-cyan-400/60">
            {lgProvider?.descriptor.isConfigured === false
              ? 'LG TV not connected. Go to Settings to configure.'
              : state === null ? 'Loading...' : 'No LG TV devices found.'}
          </p>
        </div>
      ) : (
        <div className="space-y-6">
          <h3 className="text-sm text-cyan-400/80 mb-3 flex items-center gap-2">
            <div className="w-1 h-4 bg-cyan-400 rounded-full" style={{ boxShadow: '0 0 10px #00d4ff' }} />
            LG WebOS TVs
          </h3>
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            {tvDevices.map(device => {
              const on = getTvPower(device);
              const vol = getTvVolume(device);
              const muted = getTvMute(device);

              return (
                <motion.div key={device.deviceId}
                  className="rounded-xl p-6 backdrop-blur-xl"
                  style={{
                    background: on ? 'rgba(0,212,255,0.1)' : 'rgba(5,10,18,0.6)',
                    border: `1px solid ${on ? 'rgba(0,212,255,0.4)' : 'rgba(0,212,255,0.2)'}`,
                    boxShadow: on ? '0 0 20px rgba(0,212,255,0.2)' : '0 0 10px rgba(0,212,255,0.1)',
                  }}
                  initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }}>

                  <div className="flex items-start gap-4 mb-4">
                    <div className="w-14 h-14 rounded-xl flex items-center justify-center flex-shrink-0"
                         style={{ background: 'rgba(0,212,255,0.1)', border: '1px solid rgba(0,212,255,0.3)' }}>
                      <Tv className="w-7 h-7 text-cyan-400" />
                    </div>
                    <div className="flex-1">
                      <h4 className="text-cyan-400 font-medium mb-1">{device.name}</h4>
                      <p className="text-cyan-400/60 text-xs mb-1">LG WebOS</p>
                      <p className="text-cyan-400/80 text-sm">{on ? 'On' : 'Standby'}</p>
                    </div>
                    <div className="w-3 h-3 rounded-full"
                         style={{ background: on ? '#00d4ff' : 'rgba(100,100,100,0.5)', boxShadow: on ? '0 0 10px #00d4ff' : 'none' }} />
                  </div>

                  {/* Controls */}
                  <div className="flex items-center gap-3 mb-4">
                    <motion.button onClick={() => togglePower(device)}
                      className="w-10 h-10 rounded-full flex items-center justify-center"
                      style={{ background: on ? 'rgba(0,212,255,0.2)' : 'rgba(0,212,255,0.1)', border: '1px solid rgba(0,212,255,0.3)' }}
                      whileHover={{ scale: 1.1 }} whileTap={{ scale: 0.95 }}>
                      {on ? <Pause className="w-5 h-5 text-cyan-400" /> : <Play className="w-5 h-5 text-cyan-400 ml-0.5" />}
                    </motion.button>

                    <motion.button onClick={() => toggleMute(device)}
                      className="w-10 h-10 rounded-full flex items-center justify-center"
                      style={{ background: muted ? 'rgba(255,70,70,0.2)' : 'rgba(0,212,255,0.1)', border: '1px solid rgba(0,212,255,0.3)' }}
                      whileHover={{ scale: 1.1 }} whileTap={{ scale: 0.95 }}>
                      {muted ? <VolumeX className="w-5 h-5 text-red-400" /> : <Volume2 className="w-5 h-5 text-cyan-400" />}
                    </motion.button>

                    <Volume2 className="w-4 h-4 text-cyan-400" />
                    <input type="range" min={0} max={100} value={vol}
                      onChange={e => setVolume(device, Number(e.target.value))}
                      className="flex-1 accent-cyan-400" />
                    <span className="text-cyan-400 text-xs w-10 text-right">{vol}%</span>
                  </div>
                </motion.div>
              );
            })}
          </div>
        </div>
      )}
    </>
  );
}
