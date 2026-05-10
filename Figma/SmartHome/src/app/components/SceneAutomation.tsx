import { motion } from 'motion/react';
import { useState } from 'react';
import { useSmartHomeContext } from '../SmartHomeContext';

// ── Hue scenes – each bulb gets set to these exact values ────────────────────
const HUE_SCENES = [
  { id: 'relax',      name: 'Relax',       hue: 30,  sat: 80,  bri: 60,  color: '#ff8c00', gradient: ['#ff8c00','#ff4500'] },
  { id: 'read',       name: 'Read',        hue: 40,  sat: 20,  bri: 100, color: '#ffe4b5', gradient: ['#ffe4b5','#ffd700'] },
  { id: 'energize',   name: 'Energize',    hue: 200, sat: 60,  bri: 100, color: '#00d4ff', gradient: ['#00d4ff','#0066ff'] },
  { id: 'focus',      name: 'Focus',       hue: 220, sat: 30,  bri: 90,  color: '#87ceeb', gradient: ['#87ceeb','#00d4ff'] },
  { id: 'dimmed',     name: 'Dimmed',      hue: 30,  sat: 60,  bri: 25,  color: '#8b4513', gradient: ['#8b4513','#5c2d0a'] },
  { id: 'nightlight', name: 'Night Light', hue: 20,  sat: 100, bri: 10,  color: '#ff4500', gradient: ['#ff4500','#8b0000'] },
  { id: 'red',        name: 'Red',         hue: 0,   sat: 100, bri: 80,  color: '#ff0000', gradient: ['#ff0000','#cc0000'] },
  { id: 'green',      name: 'Green',       hue: 120, sat: 100, bri: 80,  color: '#00ff00', gradient: ['#00ff00','#00aa00'] },
  { id: 'blue',       name: 'Blue',        hue: 240, sat: 100, bri: 80,  color: '#0066ff', gradient: ['#0066ff','#0033cc'] },
  { id: 'purple',     name: 'Purple',      hue: 280, sat: 100, bri: 80,  color: '#8b00ff', gradient: ['#8b00ff','#5500cc'] },
  { id: 'pink',       name: 'Pink',        hue: 320, sat: 100, bri: 80,  color: '#ff1493', gradient: ['#ff1493','#cc0066'] },
  { id: 'yellow',     name: 'Yellow',      hue: 60,  sat: 100, bri: 80,  color: '#ffd700', gradient: ['#ffd700','#ffaa00'] },
  { id: 'colorloop',  name: 'Color Loop',  hue: 0,   sat: 100, bri: 80,  color: '#00d4ff', gradient: ['#ff0000','#00ff00','#0066ff'], effect: 'colorloop' },
  { id: 'off',        name: 'Lights Off',  hue: 0,   sat: 0,   bri: 0,   color: '#444',    gradient: ['#333','#111'], off: true },
];

// ── Govee fallback scenes (used when API returns no options) ─────────────────
const GOVEE_FALLBACK = [
  { id: 'music',       name: 'Music Sync',   value: 212, color: '#ff1493', gradient: ['#ff1493','#8b00ff'] },
  { id: 'sunrise',     name: 'Sunrise',      value: 213, color: '#ff8c00', gradient: ['#ff8c00','#ffd700'] },
  { id: 'sunset',      name: 'Sunset',       value: 214, color: '#ff4500', gradient: ['#ff4500','#8b0000'] },
  { id: 'movie',       name: 'Movie',        value: 215, color: '#8b00ff', gradient: ['#8b00ff','#0066ff'] },
  { id: 'romantic',    name: 'Romantic',     value: 216, color: '#ff69b4', gradient: ['#ff69b4','#ff1493'] },
  { id: 'twinkle',     name: 'Twinkle',      value: 217, color: '#00d4ff', gradient: ['#00d4ff','#ffffff'] },
  { id: 'candlelight', name: 'Candlelight',  value: 218, color: '#ff8c00', gradient: ['#ff8c00','#ff4500'] },
  { id: 'rainbow',     name: 'Rainbow',      value: 220, color: '#ffd700', gradient: ['#ff0000','#0066ff'] },
  { id: 'breathe',     name: 'Breathe',      value: 221, color: '#00ff00', gradient: ['#00ff00','#00d4ff'] },
  { id: 'party',       name: 'Party',        value: 223, color: '#ff1493', gradient: ['#ff1493','#ffd700'] },
];

// Scene card colours for API-sourced Govee scenes
const SCENE_COLORS = [
  ['#ff1493','#8b00ff'], ['#ff8c00','#ffd700'], ['#00d4ff','#0066ff'],
  ['#8b00ff','#ff1493'], ['#ffd700','#ff8c00'], ['#00ff00','#00d4ff'],
  ['#ff4500','#ff0000'], ['#87ceeb','#00d4ff'], ['#ff69b4','#ff1493'],
  ['#00d4ff','#00ff00'], ['#ffd700','#00ff00'], ['#ff0000','#ff8c00'],
];

function SceneCard({
  name, gradient, isActive, onClick,
}: {
  name: string;
  gradient: string[];
  isActive: boolean;
  onClick: () => void;
}) {
  const bg = gradient.length >= 2
    ? `linear-gradient(135deg, ${gradient[0]}, ${gradient[1]})`
    : gradient[0];

  return (
    <motion.button
      onClick={onClick}
      className="relative rounded-2xl overflow-hidden cursor-pointer text-left"
      style={{ minHeight: 90 }}
      whileHover={{ scale: 1.04 }}
      whileTap={{ scale: 0.97 }}
    >
      {/* Colour fill */}
      <div className="absolute inset-0" style={{ background: bg, opacity: isActive ? 1 : 0.75 }} />

      {/* Dark overlay for text legibility */}
      <div className="absolute inset-0" style={{ background: 'linear-gradient(to top, rgba(0,0,0,0.6) 0%, rgba(0,0,0,0.1) 60%)' }} />

      {/* Active glow border */}
      {isActive && (
        <motion.div
          className="absolute inset-0 rounded-2xl"
          style={{ border: `2px solid ${gradient[0]}`, boxShadow: `0 0 20px ${gradient[0]}80` }}
          animate={{ opacity: [0.6, 1, 0.6] }}
          transition={{ duration: 1.5, repeat: Infinity }}
        />
      )}

      {/* Active dot */}
      {isActive && (
        <motion.div
          className="absolute top-2 right-2 w-2 h-2 rounded-full bg-white"
          animate={{ opacity: [1, 0.3, 1] }}
          transition={{ duration: 1, repeat: Infinity }}
        />
      )}

      {/* Label */}
      <div className="absolute bottom-0 left-0 right-0 p-3">
        <p className="text-white text-xs font-semibold leading-tight drop-shadow-lg">{name}</p>
      </div>
    </motion.button>
  );
}

export function SceneAutomation() {
  const { state, executeAction } = useSmartHomeContext();
  const [activeScene, setActiveScene] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState<'hue' | 'govee'>('hue');

  const hueLights = state?.providers.find(p => p.providerId === 'philips_hue')?.devices ?? [];
  const goveeDevices = state?.providers.find(p => p.providerId === 'govee')?.devices ?? [];

  // Find the best scene capability across all Govee devices
  const findGoveeSceneCap = (device: typeof goveeDevices[0]) =>
    device.capabilities.find(c =>
      c.type?.includes('dynamic_scene') ||
      c.type?.includes('music_setting') ||
      c.instance === 'lightScene' ||
      c.instance === 'diyScene' ||
      c.instance === 'musicMode' ||
      c.instance?.toLowerCase().includes('scene')
    );

  // Collect real scene options from the API
  const goveeApiScenes = (() => {
    for (const device of goveeDevices) {
      const cap = findGoveeSceneCap(device);
      if (cap?.options?.length) return cap.options;
    }
    return [];
  })();

  const applyHueScene = (scene: typeof HUE_SCENES[0]) => {
    setActiveScene(scene.id);
    for (const light of hueLights) {
      if (scene.off) {
        executeAction('philips_hue', light.deviceId, light.sku, 'devices.capabilities.on_off', 'powerSwitch', false);
        continue;
      }
      if (scene.effect === 'colorloop') {
        executeAction('philips_hue', light.deviceId, light.sku, 'devices.capabilities.mode', 'effectMode', 'colorloop');
        executeAction('philips_hue', light.deviceId, light.sku, 'devices.capabilities.range', 'brightness', scene.bri);
        continue;
      }
      executeAction('philips_hue', light.deviceId, light.sku, 'devices.capabilities.mode', 'effectMode', 'none');
      executeAction('philips_hue', light.deviceId, light.sku, 'devices.capabilities.range', 'colorHue', scene.hue);
      executeAction('philips_hue', light.deviceId, light.sku, 'devices.capabilities.range', 'colorSaturation', scene.sat);
      executeAction('philips_hue', light.deviceId, light.sku, 'devices.capabilities.range', 'brightness', scene.bri);
    }
  };

  const applyGoveeApiScene = (option: { name: string; value: unknown }, idx: number) => {
    const id = `govee-${idx}`;
    setActiveScene(id);
    for (const device of goveeDevices) {
      const cap = findGoveeSceneCap(device);
      if (cap) executeAction('govee', device.deviceId, device.sku, cap.type, cap.instance, option.value);
    }
  };

  const applyGoveeFallback = (scene: typeof GOVEE_FALLBACK[0]) => {
    setActiveScene(scene.id);
    for (const device of goveeDevices) {
      const cap = findGoveeSceneCap(device);
      if (cap) executeAction('govee', device.deviceId, device.sku, cap.type, cap.instance, scene.value);
    }
  };

  const hasHue = hueLights.length > 0;
  const hasGovee = goveeDevices.length > 0;

  if (!hasHue && !hasGovee) return null;

  return (
    <div className="mt-8">
      <h3 className="text-sm text-cyan-400/80 mb-4 flex items-center gap-2">
        <div className="w-1 h-4 bg-cyan-400 rounded-full" style={{ boxShadow: '0 0 10px #00d4ff' }} />
        Light Scenes & Effects
        <span className="text-cyan-400/40 text-xs ml-1">
          {hasHue && `${hueLights.length} Hue bulbs`}
          {hasHue && hasGovee && ' · '}
          {hasGovee && `${goveeDevices.length} Govee`}
        </span>
      </h3>

      {/* Tab selector */}
      {hasHue && hasGovee && (
        <div className="flex gap-2 mb-5">
          {(['hue', 'govee'] as const).map(tab => (
            <button key={tab} onClick={() => setActiveTab(tab)}
              className="px-4 py-2 rounded-lg text-sm font-medium"
              style={{
                background: activeTab === tab ? 'rgba(0,212,255,0.2)' : 'rgba(0,212,255,0.05)',
                border: `1px solid ${activeTab === tab ? '#00d4ff' : 'rgba(0,212,255,0.15)'}`,
                color: '#00d4ff',
              }}>
              {tab === 'hue' ? 'Philips Hue' : 'Govee'}
            </button>
          ))}
        </div>
      )}

      {/* ── Hue scene cards ── */}
      {(activeTab === 'hue' || !hasGovee) && hasHue && (
        <div className="grid grid-cols-3 sm:grid-cols-4 md:grid-cols-5 lg:grid-cols-7 gap-3">
          {HUE_SCENES.map(scene => (
            <SceneCard
              key={scene.id}
              name={scene.name}
              gradient={scene.gradient}
              isActive={activeScene === scene.id}
              onClick={() => applyHueScene(scene)}
            />
          ))}
        </div>
      )}

      {/* ── Govee scene cards ── */}
      {(activeTab === 'govee' || !hasHue) && hasGovee && (
        <div className="grid grid-cols-3 sm:grid-cols-4 md:grid-cols-5 lg:grid-cols-6 gap-3">
          {goveeApiScenes.length > 0
            ? goveeApiScenes.map((opt, idx) => {
                const [c1, c2] = SCENE_COLORS[idx % SCENE_COLORS.length];
                return (
                  <SceneCard
                    key={idx}
                    name={opt.name}
                    gradient={[c1, c2]}
                    isActive={activeScene === `govee-${idx}`}
                    onClick={() => applyGoveeApiScene(opt, idx)}
                  />
                );
              })
            : GOVEE_FALLBACK.map(scene => (
                <SceneCard
                  key={scene.id}
                  name={scene.name}
                  gradient={scene.gradient}
                  isActive={activeScene === scene.id}
                  onClick={() => applyGoveeFallback(scene)}
                />
              ))
          }
        </div>
      )}
    </div>
  );
}
