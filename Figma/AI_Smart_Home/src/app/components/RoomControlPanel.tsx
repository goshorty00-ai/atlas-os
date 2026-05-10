import { motion } from 'motion/react';
import { X, Lightbulb, Thermometer, Power, Cpu } from 'lucide-react';
import { Slider } from './ui/slider';
import { Switch } from './ui/switch';
import { useState } from 'react';
import type { Room } from './HolographicHouse';

interface RoomControlPanelProps {
  room: Room;
  onClose: () => void;
  onUpdate: (updates: Partial<Room>) => void;
}

export function RoomControlPanel({ room, onClose, onUpdate }: RoomControlPanelProps) {
  const [brightness, setBrightness] = useState(room.brightness);
  const [temperature, setTemperature] = useState(room.temperature);
  const [lightsOn, setLightsOn] = useState(room.lightsOn);
  const [selectedColor, setSelectedColor] = useState(room.color);

  const colors = ['#00d4ff', '#ff00ff', '#00ff00', '#ffff00', '#ff8c00', '#ff0000'];

  const handleBrightnessChange = (value: number[]) => {
    setBrightness(value[0]);
    onUpdate({ brightness: value[0] });
  };

  const handleTemperatureChange = (value: number[]) => {
    setTemperature(value[0]);
    onUpdate({ temperature: value[0] });
  };

  const handleLightsToggle = (checked: boolean) => {
    setLightsOn(checked);
    onUpdate({ lightsOn: checked });
  };

  const handleColorChange = (color: string) => {
    setSelectedColor(color);
    onUpdate({ color });
  };

  return (
    <motion.div
      className="fixed top-1/2 right-8 -translate-y-1/2 w-80 z-50"
      initial={{ opacity: 0, x: 100 }}
      animate={{ opacity: 1, x: 0 }}
      exit={{ opacity: 0, x: 100 }}
    >
      {/* Glassmorphic Panel */}
      <div
        className="relative rounded-2xl p-6 backdrop-blur-xl"
        style={{
          background: 'rgba(5, 10, 18, 0.8)',
          border: '1px solid rgba(0, 212, 255, 0.3)',
          boxShadow: '0 0 40px rgba(0, 212, 255, 0.2), inset 0 0 40px rgba(0, 212, 255, 0.05)',
        }}
      >
        {/* Corner Accents */}
        <div className="absolute top-0 left-0 w-16 h-16 border-l-2 border-t-2 border-cyan-400 rounded-tl-2xl" 
             style={{ boxShadow: '0 0 10px #00d4ff' }} />
        <div className="absolute bottom-0 right-0 w-16 h-16 border-r-2 border-b-2 border-cyan-400 rounded-br-2xl" 
             style={{ boxShadow: '0 0 10px #00d4ff' }} />

        {/* Header */}
        <div className="flex items-center justify-between mb-6">
          <div>
            <h3 className="text-lg font-semibold text-cyan-400" style={{ textShadow: '0 0 10px #00d4ff' }}>
              {room.name}
            </h3>
            <p className="text-xs text-cyan-400/60 mt-1">Control Center</p>
          </div>
          <button
            onClick={onClose}
            className="w-8 h-8 rounded-full flex items-center justify-center transition-all"
            style={{
              background: 'rgba(0, 212, 255, 0.1)',
              border: '1px solid rgba(0, 212, 255, 0.3)',
            }}
          >
            <X className="w-4 h-4 text-cyan-400" />
          </button>
        </div>

        {/* Status Indicators */}
        <div className="grid grid-cols-2 gap-3 mb-6">
          <div className="p-3 rounded-lg" style={{ background: 'rgba(0, 212, 255, 0.05)', border: '1px solid rgba(0, 212, 255, 0.2)' }}>
            <div className="flex items-center gap-2 mb-1">
              <Cpu className="w-4 h-4 text-cyan-400" />
              <span className="text-xs text-cyan-400/80">Devices</span>
            </div>
            <p className="text-lg font-semibold text-cyan-400">{room.devicesActive}</p>
          </div>
          <div className="p-3 rounded-lg" style={{ background: 'rgba(0, 212, 255, 0.05)', border: '1px solid rgba(0, 212, 255, 0.2)' }}>
            <div className="flex items-center gap-2 mb-1">
              <Power className="w-4 h-4 text-cyan-400" />
              <span className="text-xs text-cyan-400/80">Status</span>
            </div>
            <p className="text-lg font-semibold text-cyan-400">{lightsOn ? 'Active' : 'Offline'}</p>
          </div>
        </div>

        {/* Lighting Control */}
        <div className="mb-6">
          <div className="flex items-center justify-between mb-3">
            <div className="flex items-center gap-2">
              <Lightbulb className="w-4 h-4 text-cyan-400" />
              <span className="text-sm text-cyan-400">Lighting</span>
            </div>
            <Switch checked={lightsOn} onCheckedChange={handleLightsToggle} />
          </div>

          {lightsOn && (
            <motion.div
              initial={{ opacity: 0, height: 0 }}
              animate={{ opacity: 1, height: 'auto' }}
              className="space-y-4"
            >
              {/* Brightness Slider */}
              <div>
                <div className="flex justify-between mb-2">
                  <span className="text-xs text-cyan-400/80">Brightness</span>
                  <span className="text-xs text-cyan-400 font-medium">{brightness}%</span>
                </div>
                <div className="slider-cyan">
                  <Slider
                    value={[brightness]}
                    onValueChange={handleBrightnessChange}
                    max={100}
                    step={1}
                    className="w-full"
                  />
                </div>
              </div>

              {/* Color Picker */}
              <div>
                <span className="text-xs text-cyan-400/80 block mb-2">RGB Color</span>
                <div className="flex gap-2">
                  {colors.map((color) => (
                    <button
                      key={color}
                      onClick={() => handleColorChange(color)}
                      className="w-8 h-8 rounded-full border-2 transition-all"
                      style={{
                        backgroundColor: color,
                        borderColor: selectedColor === color ? '#ffffff' : 'transparent',
                        boxShadow: selectedColor === color ? `0 0 20px ${color}` : `0 0 10px ${color}80`,
                      }}
                    />
                  ))}
                </div>
              </div>
            </motion.div>
          )}
        </div>

        {/* Temperature Control */}
        <div>
          <div className="flex items-center gap-2 mb-3">
            <Thermometer className="w-4 h-4 text-cyan-400" />
            <span className="text-sm text-cyan-400">Climate Control</span>
          </div>

          {/* Temperature Dial */}
          <div className="relative flex items-center justify-center mb-2">
            <svg className="w-32 h-32" viewBox="0 0 100 100">
              {/* Background Circle */}
              <circle
                cx="50"
                cy="50"
                r="40"
                fill="none"
                stroke="rgba(0, 212, 255, 0.1)"
                strokeWidth="8"
              />
              {/* Progress Circle */}
              <circle
                cx="50"
                cy="50"
                r="40"
                fill="none"
                stroke="url(#tempGradient)"
                strokeWidth="8"
                strokeLinecap="round"
                strokeDasharray={`${((temperature - 15) / 15) * 251.2} 251.2`}
                transform="rotate(-90 50 50)"
                style={{
                  filter: 'drop-shadow(0 0 10px #00d4ff)',
                }}
              />
              <defs>
                <linearGradient id="tempGradient" x1="0%" y1="0%" x2="100%" y2="0%">
                  <stop offset="0%" stopColor="#00d4ff" />
                  <stop offset="100%" stopColor="#ff8c00" />
                </linearGradient>
              </defs>
            </svg>
            <div className="absolute inset-0 flex items-center justify-center flex-col">
              <span className="text-3xl font-bold text-cyan-400" style={{ textShadow: '0 0 20px #00d4ff' }}>
                {temperature}°
              </span>
              <span className="text-xs text-cyan-400/60">Celsius</span>
            </div>
          </div>

          {/* Temperature Slider */}
          <div className="slider-cyan">
            <Slider
              value={[temperature]}
              onValueChange={handleTemperatureChange}
              min={15}
              max={30}
              step={0.5}
              className="w-full"
            />
          </div>
        </div>

        {/* Holographic Scan Line */}
        <motion.div
          className="absolute inset-x-0 h-px bg-gradient-to-r from-transparent via-cyan-400 to-transparent opacity-30"
          animate={{
            top: ['0%', '100%'],
          }}
          transition={{
            duration: 4,
            repeat: Infinity,
            ease: 'linear',
          }}
        />
      </div>
    </motion.div>
  );
}