import { motion } from 'motion/react';
import { useState } from 'react';
import { Lightbulb, Bell, Wifi, Thermometer, Lock, Plus, Check, ExternalLink } from 'lucide-react';
import { useSmartHomeContext } from '../SmartHomeContext';

interface IntegrationCardProps {
  name: string;
  category: string;
  description: string;
  icon: React.ComponentType<{ className?: string }>;
  iconColor: string;
  devices: number;
  features: string[];
  isConfigured: boolean;
  isAiEnhanced: boolean;
  onConfigure: () => void;
}

function IntegrationCard({
  name,
  category,
  description,
  icon: Icon,
  iconColor,
  devices,
  features,
  isConfigured,
  isAiEnhanced,
  onConfigure,
}: IntegrationCardProps) {
  return (
    <motion.div
      className="relative rounded-2xl p-6 backdrop-blur-xl"
      style={{
        background: 'rgba(5,10,18,0.7)',
        border: `1px solid ${isConfigured ? 'rgba(0,212,255,0.4)' : 'rgba(0,212,255,0.2)'}`,
        boxShadow: isConfigured ? '0 0 30px rgba(0,212,255,0.15)' : '0 0 15px rgba(0,212,255,0.05)',
      }}
      whileHover={{ scale: 1.02, boxShadow: '0 0 40px rgba(0,212,255,0.25)' }}
      transition={{ duration: 0.2 }}
    >
      {/* Header */}
      <div className="flex items-start justify-between mb-4">
        <div className="flex items-center gap-3">
          <div
            className="w-12 h-12 rounded-xl flex items-center justify-center"
            style={{
              background: `${iconColor}22`,
              border: `1px solid ${iconColor}44`,
              boxShadow: `0 0 15px ${iconColor}33`,
            }}
          >
            <Icon className="w-6 h-6" style={{ color: iconColor }} />
          </div>
          <div>
            <h3 className="text-lg font-bold text-cyan-400">{name}</h3>
            <p className="text-xs text-cyan-400/60">{category}</p>
          </div>
        </div>
        {isConfigured && (
          <motion.div
            className="w-8 h-8 rounded-full flex items-center justify-center"
            style={{
              background: 'rgba(0,212,255,0.2)',
              border: '1px solid #00d4ff',
            }}
            animate={{ scale: [1, 1.1, 1] }}
            transition={{ duration: 2, repeat: Infinity }}
          >
            <Check className="w-4 h-4 text-cyan-400" />
          </motion.div>
        )}
      </div>

      {/* Description */}
      <p className="text-sm text-cyan-400/70 mb-4">{description}</p>

      {/* Device Count */}
      <div className="flex items-center justify-between mb-4">
        <span className="text-xs text-cyan-400/60">Devices</span>
        <span className="text-2xl font-bold text-cyan-400">{devices}</span>
      </div>

      {/* Features */}
      <div className="flex flex-wrap gap-2 mb-4">
        {features.map((feature) => (
          <span
            key={feature}
            className="px-2 py-1 rounded-lg text-xs"
            style={{
              background: 'rgba(0,212,255,0.1)',
              border: '1px solid rgba(0,212,255,0.3)',
              color: '#00d4ff',
            }}
          >
            {feature}
          </span>
        ))}
      </div>

      {/* AI Enhanced Badge */}
      {isAiEnhanced && (
        <div className="flex items-center gap-1.5 mb-4">
          <div className="w-1.5 h-1.5 rounded-full bg-yellow-400 animate-pulse" />
          <span className="text-xs text-yellow-400">AI Enhanced</span>
        </div>
      )}

      {/* Configure Button */}
      <motion.button
        onClick={onConfigure}
        className="w-full py-2.5 rounded-xl text-sm font-medium flex items-center justify-center gap-2"
        style={{
          background: isConfigured ? 'rgba(0,212,255,0.15)' : 'rgba(0,212,255,0.1)',
          border: `1px solid ${isConfigured ? '#00d4ff' : 'rgba(0,212,255,0.3)'}`,
          color: '#00d4ff',
        }}
        whileHover={{ scale: 1.02, background: 'rgba(0,212,255,0.2)' }}
        whileTap={{ scale: 0.98 }}
      >
        {isConfigured ? (
          <>
            <ExternalLink className="w-4 h-4" />
            Configure
          </>
        ) : (
          <>
            <Plus className="w-4 h-4" />
            Add Integration
          </>
        )}
      </motion.button>
    </motion.div>
  );
}

export function Integrations() {
  const { state, openExternalUrl } = useSmartHomeContext();
  const [filter, setFilter] = useState<string>('All');

  const categories = ['All', 'Lighting', 'Security', 'Climate', 'Audio', 'Hub', 'Assistant', 'Automation', 'Access', 'Outdoor'];

  const getProviderInfo = (providerId: string) => {
    const provider = state?.providers.find((p) => p.providerId === providerId);
    return {
      isConfigured: provider?.descriptor.isConfigured ?? false,
      deviceCount: provider?.devices.length ?? 0,
      error: provider?.error,
    };
  };

  const philipsHue = getProviderInfo('philips_hue');
  const ring = getProviderInfo('ring');
  const govee = getProviderInfo('govee');
  const lgWebOs = getProviderInfo('lg_webos');

  const integrations = [
    {
      id: 'philips_hue',
      name: 'Philips Hue',
      category: 'Lighting',
      description: 'Smart LED lighting system with millions of colors',
      icon: Lightbulb,
      iconColor: '#ffd700',
      devices: philipsHue.deviceCount,
      features: ['Color Control', 'Scenes', 'Schedules'],
      isConfigured: philipsHue.isConfigured,
      isAiEnhanced: true,
      onConfigure: () => openExternalUrl('/settings'),
    },
    {
      id: 'ring',
      name: 'Ring',
      category: 'Security',
      description: 'Video doorbells and security cameras',
      icon: Bell,
      iconColor: '#00caff',
      devices: ring.deviceCount,
      features: ['Live View', 'Motion Detection', 'Two-Way Talk'],
      isConfigured: ring.isConfigured,
      isAiEnhanced: true,
      onConfigure: () => openExternalUrl('/settings'),
    },
    {
      id: 'govee',
      name: 'Govee',
      category: 'Lighting',
      description: 'Smart RGB LED strips and ambient lighting',
      icon: Wifi,
      iconColor: '#ff6b35',
      devices: govee.deviceCount,
      features: ['RGB Control', 'Music Sync', 'Effects'],
      isConfigured: govee.isConfigured,
      isAiEnhanced: true,
      onConfigure: () => openExternalUrl('/settings'),
    },
    {
      id: 'google_nest',
      name: 'Google Nest',
      category: 'Climate',
      description: 'Smart thermostats and home security',
      icon: Thermometer,
      iconColor: '#4ade80',
      devices: 4,
      features: ['Thermostats', 'Cameras', 'Doorbells'],
      isConfigured: false,
      isAiEnhanced: false,
      onConfigure: () => {},
    },
    {
      id: 'ecobee',
      name: 'Ecobee',
      category: 'Climate',
      description: 'Smart thermostats with room sensors',
      icon: Thermometer,
      iconColor: '#8b5cf6',
      devices: 0,
      features: ['Room Sensors', 'Alexa Built-in'],
      isConfigured: false,
      isAiEnhanced: false,
      onConfigure: () => {},
    },
    {
      id: 'august',
      name: 'August',
      category: 'Security',
      description: 'Smart locks and access control',
      icon: Lock,
      iconColor: '#00d4ff',
      devices: 2,
      features: ['Smart Locks', 'Remote Access', 'Activity Log'],
      isConfigured: true,
      isAiEnhanced: false,
      onConfigure: () => {},
    },
  ];

  const filteredIntegrations = filter === 'All'
    ? integrations
    : integrations.filter((i) => i.category === filter);

  return (
    <>
      <motion.div className="mb-8" initial={{ opacity: 0, y: -20 }} animate={{ opacity: 1, y: 0 }}>
        <div className="flex items-center gap-3 mb-2">
          <div className="w-2 h-2 rounded-full bg-cyan-400 animate-pulse" style={{ boxShadow: '0 0 10px #00d4ff' }} />
          <h1
            className="text-4xl font-bold"
            style={{
              background: 'linear-gradient(135deg, #00d4ff, #0066ff)',
              WebkitBackgroundClip: 'text',
              WebkitTextFillColor: 'transparent',
            }}
          >
            Integrations
          </h1>
        </div>
        <p className="text-cyan-400/60 text-sm ml-5">
          All 17 connected brands are now controllable. Atlas automatically syncs and optimizes your devices.
        </p>
      </motion.div>

      {/* Category Filter */}
      <div className="flex gap-2 mb-6 flex-wrap">
        {categories.map((cat) => (
          <motion.button
            key={cat}
            onClick={() => setFilter(cat)}
            className="px-4 py-2 rounded-xl text-sm font-medium"
            style={{
              background: filter === cat ? 'rgba(0,212,255,0.2)' : 'rgba(0,212,255,0.05)',
              border: `1px solid ${filter === cat ? '#00d4ff' : 'rgba(0,212,255,0.2)'}`,
              color: '#00d4ff',
            }}
            whileHover={{ scale: 1.05 }}
            whileTap={{ scale: 0.95 }}
          >
            {cat}
          </motion.button>
        ))}
      </div>

      {/* Integration Cards Grid */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
        {filteredIntegrations.map((integration) => (
          <IntegrationCard key={integration.id} {...integration} />
        ))}
      </div>
    </>
  );
}
