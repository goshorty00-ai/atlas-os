import { motion } from 'motion/react';
import { X, Lightbulb, Power, Cpu } from 'lucide-react';
import { useState } from 'react';
import { useSmartHomeContext } from '../SmartHomeContext';
import type { Room, RoomDevice } from './HolographicHouse';

interface RoomControlPanelProps {
  room: Room;
  onClose: () => void;
}

const COLOR_PRESETS = [
  { label: 'Cyan',   hue: 180, sat: 100, bri: 80,  hex: '#00d4ff' },
  { label: 'Purple', hue: 280, sat: 100, bri: 80,  hex: '#8b00ff' },
  { label: 'Green',  hue: 120, sat: 100, bri: 80,  hex: '#00ff00' },
  { label: 'Yellow', hue: 60,  sat: 100, bri: 80,  hex: '#ffd700' },
  { label: 'Orange', hue: 30,  sat: 100, bri: 80,  hex: '#ff8c00' },
  { label: 'Red',    hue: 0,   sat: 100, bri: 80,  hex: '#ff0000' },
];

export function RoomControlPanel({ room, onClose }: RoomControlPanelProps) {
  const { executeAction, isDeviceOn, getDeviceBrightness } = useSmartHomeContext();
  const [selectedColor, setSelectedColor] = useState<string | null>(null);

  const devices: RoomDevice[] = room.devices ?? [];
  const hueDevices = devices.filter(d => d._providerId === 'philips_hue');
  const goveeDevices = devices.filter(d => d._providerId === 'govee');
  const allLights = [...hueDevices, ...goveeDevices];

  const anyOn = allLights.some(d => isDeviceOn(d));

  const toggleAll = () => {
    for (const d of allLights) {
      executeAction(d._providerId, d.deviceId, d.sku, 'devices.capabilities.on_off', 'powerSwitch', !anyOn);
    }
  };

  const applyColor = (preset: typeof COLOR_PRESETS[0]) => {
    setSelectedColor(preset.hex);
    for (const d of hueDevices) {
      executeAction('philips_hue', d.deviceId, d.sku, 'devices.capabilities.mode', 'effectMode', 'none');
      executeAction('philips_hue', d.deviceId, d.sku, 'devices.capabilities.range', 'colorHue', preset.hue);
      executeAction('philips_hue', d.deviceId, d.sku, 'devices.capabilities.range', 'colorSaturation', preset.sat);
      executeAction('philips_hue', d.deviceId, d.sku, 'devices.capabilities.range', 'brightness', preset.bri);
    }
    for (const d of goveeDevices) {
      const colorCap = d.capabilities.find((c: { instance: string; type: string }) => c.instance === 'colorRgb' || c.instance === 'color');
      if (colorCap) {
        // Convert hex to RGB int
        const r = parseInt(preset.hex.slice(1, 3), 16);
        const g = parseInt(preset.hex.slice(3, 5), 16);
        const b = parseInt(preset.hex.slice(5, 7), 16);
        executeAction('govee', d.deviceId, d.sku, colorCap.type, colorCap.instance, { r, g, b });
      }
    }
  };

  const toggleLight = (device: RoomDevice) => {
    const on = isDeviceOn(device);
    executeAction(device._providerId, device.deviceId, device.sku, 'devices.capabilities.on_off', 'powerSwitch', !on);
  };

  return (
    <motion.div className="fixed top-1/2 right-8 -translate-y-1/2 w-80 z-50"
      initial={{ opacity: 0, x: 100 }} animate={{ opacity: 1, x: 0 }} exit={{ opacity: 0, x: 100 }}>
      <div className="relative rounded-2xl p-6 backdrop-blur-xl"
        style={{
          background: 'rgba(5,10,18,0.8)',
          border: '1px solid rgba(0,212,255,0.3)',
          boxShadow: '0 0 40px rgba(0,212,255,0.2), inset 0 0 40px rgba(0,212,255,0.05)',
        }}>
        <div className="absolute top-0 left-0 w-16 h-16 border-l-2 border-t-2 border-cyan-400 rounded-tl-2xl"
             style={{ boxShadow: '0 0 10px #00d4ff' }} />
        <div className="absolute bottom-0 right-0 w-16 h-16 border-r-2 border-b-2 border-cyan-400 rounded-br-2xl"
             style={{ boxShadow: '0 0 10px #00d4ff' }} />

        {/* Header */}
        <div className="flex items-center justify-between mb-5">
          <div>
            <h3 className="text-lg font-semibold text-cyan-400" style={{ textShadow: '0 0 10px #00d4ff' }}>
              {room.name}
            </h3>
            <p className="text-xs text-cyan-400/60 mt-1">{allLights.length} light{allLights.length !== 1 ? 's' : ''}</p>
          </div>
          <button onClick={onClose} className="w-8 h-8 rounded-full flex items-center justify-center"
            style={{ background: 'rgba(0,212,255,0.1)', border: '1px solid rgba(0,212,255,0.3)' }}>
            <X className="w-4 h-4 text-cyan-400" />
          </button>
        </div>

        {/* Stats */}
        <div className="grid grid-cols-2 gap-3 mb-5">
          <div className="p-3 rounded-lg" style={{ background: 'rgba(0,212,255,0.05)', border: '1px solid rgba(0,212,255,0.2)' }}>
            <div className="flex items-center gap-2 mb-1">
              <Cpu className="w-4 h-4 text-cyan-400" />
              <span className="text-xs text-cyan-400/80">Devices</span>
            </div>
            <p className="text-lg font-semibold text-cyan-400">{allLights.length}</p>
          </div>
          <div className="p-3 rounded-lg" style={{ background: 'rgba(0,212,255,0.05)', border: '1px solid rgba(0,212,255,0.2)' }}>
            <div className="flex items-center gap-2 mb-1">
              <Power className="w-4 h-4 text-cyan-400" />
              <span className="text-xs text-cyan-400/80">Status</span>
            </div>
            <p className="text-lg font-semibold text-cyan-400">{anyOn ? 'Active' : 'Off'}</p>
          </div>
        </div>

        {/* All On/Off */}
        <div className="flex items-center justify-between mb-5">
          <div className="flex items-center gap-2">
            <Lightbulb className="w-4 h-4 text-cyan-400" />
            <span className="text-sm text-cyan-400">All Lights</span>
          </div>
          <button onClick={toggleAll}
            className="w-12 h-6 rounded-full relative transition-all"
            style={{ background: anyOn ? 'rgba(0,212,255,0.3)' : 'rgba(100,100,100,0.3)' }}>
            <motion.div className="absolute top-1 w-4 h-4 rounded-full"
              style={{ background: anyOn ? '#00d4ff' : '#666', boxShadow: anyOn ? '0 0 10px #00d4ff' : 'none' }}
              animate={{ left: anyOn ? 'calc(100% - 20px)' : '4px' }}
              transition={{ type: 'spring', stiffness: 500, damping: 30 }} />
          </button>
        </div>

        {/* Colour Presets */}
        {allLights.length > 0 && (
          <div className="mb-5">
            <span className="text-xs text-cyan-400/80 block mb-2">Colour</span>
            <div className="flex gap-2 flex-wrap">
              {COLOR_PRESETS.map(preset => (
                <button key={preset.hex} onClick={() => applyColor(preset)}
                  className="w-8 h-8 rounded-full border-2 transition-all"
                  style={{
                    backgroundColor: preset.hex,
                    borderColor: selectedColor === preset.hex ? '#ffffff' : 'transparent',
                    boxShadow: selectedColor === preset.hex ? `0 0 20px ${preset.hex}` : `0 0 8px ${preset.hex}80`,
                  }} />
              ))}
            </div>
          </div>
        )}

        {/* Individual lights */}
        {allLights.length > 0 && (
          <div className="space-y-2 max-h-48 overflow-y-auto">
            <span className="text-xs text-cyan-400/80 block mb-1">Individual Lights</span>
            {allLights.map(device => {
              const on = isDeviceOn(device);
              const bri = getDeviceBrightness(device);
              return (
                <div key={device.deviceId} className="flex items-center justify-between rounded-lg px-3 py-2"
                  style={{ background: 'rgba(0,212,255,0.05)', border: '1px solid rgba(0,212,255,0.15)' }}>
                  <div className="flex items-center gap-2 flex-1 min-w-0">
                    <div className="w-2 h-2 rounded-full flex-shrink-0"
                      style={{ background: on ? '#00d4ff' : '#444', boxShadow: on ? '0 0 6px #00d4ff' : 'none' }} />
                    <span className="text-xs text-cyan-400 truncate">{device.name}</span>
                    {on && <span className="text-xs text-cyan-400/50 flex-shrink-0">{bri}%</span>}
                  </div>
                  <button onClick={() => toggleLight(device)}
                    className="w-8 h-4 rounded-full relative ml-2 flex-shrink-0"
                    style={{ background: on ? 'rgba(0,212,255,0.3)' : 'rgba(100,100,100,0.3)' }}>
                    <div className="absolute top-0.5 w-3 h-3 rounded-full transition-all"
                      style={{ background: on ? '#00d4ff' : '#666', left: on ? 'calc(100% - 14px)' : '2px' }} />
                  </button>
                </div>
              );
            })}
          </div>
        )}

        {allLights.length === 0 && (
          <p className="text-xs text-cyan-400/40 text-center py-4">No lights in this area</p>
        )}

        <motion.div className="absolute inset-x-0 h-px bg-gradient-to-r from-transparent via-cyan-400 to-transparent opacity-30"
          animate={{ top: ['0%', '100%'] }}
          transition={{ duration: 4, repeat: Infinity, ease: 'linear' }} />
      </div>
    </motion.div>
  );
}
