import { motion } from 'motion/react';
import { Camera, Radio, RefreshCw, ExternalLink } from 'lucide-react';
import { useState, useEffect } from 'react';
import { useSmartHomeContext } from '../SmartHomeContext';
import { onHostMessage } from '../bridge';

export function SecurityCameras() {
  const { state, startRingManagedLiveView, stopRingManagedLiveView, refresh, openExternalUrl } = useSmartHomeContext();
  const [activeCameraId, setActiveCameraId] = useState<string | null>(null);
  const [loadingId, setLoadingId] = useState<string | null>(null);

  // Listen for managed live view events
  useEffect(() => {
    const unsub = onHostMessage((type, payload) => {
      const p = payload as any;
      if (type === 'smart-home.ringManagedLiveViewStarted') {
        setLoadingId(null);
        setActiveCameraId(p?.cameraId ?? null);
      } else if (type === 'smart-home.ringManagedLiveViewFailed') {
        setLoadingId(null);
      } else if (type === 'smart-home.ringManagedLiveViewStopped') {
        setActiveCameraId(null);
      }
    });
    return unsub;
  }, []);

  const ringProvider = state?.providers.find(p => p.providerId === 'ring');
  const ringError = (ringProvider as any)?.error as string | undefined;
  // All Ring devices are cameras/doorbells - show them all
  const cameras = ringProvider?.devices ?? [];

  // Debug: also check for any provider with 'ring' in the name
  const ringProviderAlt = !ringProvider
    ? state?.providers.find(p => p.providerId.toLowerCase().includes('ring') || p.displayName.toLowerCase().includes('ring'))
    : null;
  const allCameras = cameras.length > 0 ? cameras : (ringProviderAlt?.devices ?? []);

  const handleView = (deviceId: string, externalUrl?: string) => {
    console.log('[SecurityCameras] handleView called:', deviceId, 'active:', activeCameraId);
    if (activeCameraId === deviceId) {
      console.log('[SecurityCameras] Stopping camera:', deviceId);
      stopRingManagedLiveView(`stop-${deviceId}`, deviceId);
      return;
    }
    // Ring cameras need managed live view, not external URL
    console.log('[SecurityCameras] Starting camera:', deviceId);
    setLoadingId(deviceId);
    startRingManagedLiveView(`view-${deviceId}`, deviceId);
  };

  // Only show real Ring cameras - no dummy fallback
  // Ring's alerts.connection field is unreliable; treat null/undefined as online
  const displayCameras = allCameras.map(c => ({
    id: c.deviceId,
    name: c.name,
    status: c.isOnline === false ? 'offline' : 'active',
    externalUrl: c.externalUrl,
    previewImageUrl: c.previewImageUrl,
    previewVideoUrl: c.previewVideoUrl,
  }));

  const effectiveProvider = ringProvider ?? ringProviderAlt;

  return (
    <div className="mt-8">
      <h3 className="text-sm text-cyan-400/80 mb-4 flex items-center gap-2">
        <div className="w-1 h-4 bg-cyan-400 rounded-full" style={{ boxShadow: '0 0 10px #00d4ff' }} />
        Security Cameras
        {ringProvider && !ringProvider.descriptor.isConfigured && (
          <span className="ml-2 text-xs text-orange-400">(Ring not connected — go to Settings)</span>
        )}
      </h3>

      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        {displayCameras.length === 0 && (
          <div className="col-span-3 rounded-xl p-8 text-center backdrop-blur-xl"
               style={{ background: 'rgba(5,10,18,0.6)', border: '1px solid rgba(0,212,255,0.2)' }}>
            <p className="text-cyan-400/60 text-sm mb-3">
              {state === null
                ? 'Loading...'
                : effectiveProvider
                  ? ringError
                    ? `Ring: ${ringError}`
                    : `Ring connected but returned no devices. Try refreshing.`
                  : `Ring not found. Providers: ${state.providers.map(p => p.providerId).join(', ') || 'none'}`}
            </p>
            <div className="flex gap-3 justify-center flex-wrap">
              {effectiveProvider && (
                <motion.button onClick={refresh}
                  className="px-4 py-2 rounded-lg text-xs flex items-center gap-2"
                  style={{ background: 'rgba(0,212,255,0.1)', border: '1px solid rgba(0,212,255,0.3)', color: '#00d4ff' }}
                  whileHover={{ scale: 1.05 }} whileTap={{ scale: 0.95 }}>
                  <RefreshCw className="w-3 h-3" /> Refresh
                </motion.button>
              )}
              {effectiveProvider && (
                <motion.button onClick={() => openExternalUrl('https://account.ring.com/account/dashboard')}
                  className="px-4 py-2 rounded-lg text-xs flex items-center gap-2"
                  style={{ background: 'rgba(0,212,255,0.1)', border: '1px solid rgba(0,212,255,0.3)', color: '#00d4ff' }}
                  whileHover={{ scale: 1.05 }} whileTap={{ scale: 0.95 }}>
                  <ExternalLink className="w-3 h-3" /> Open Ring Dashboard
                </motion.button>
              )}
            </div>
          </div>
        )}
        {displayCameras.map((camera, index) => {
          const isActive = activeCameraId === camera.id;

          return (
            <motion.div
              key={camera.id}
              className="relative group cursor-pointer"
              initial={{ opacity: 0, scale: 0.9 }}
              animate={{ opacity: 1, scale: 1 }}
              transition={{ delay: index * 0.1 }}
              whileHover={{ scale: 1.02 }}
            >
              <div
                className="relative rounded-xl overflow-hidden backdrop-blur-xl"
                style={{
                  background: 'rgba(5, 10, 18, 0.6)',
                  border: `1px solid ${isActive ? 'rgba(255,140,0,0.5)' : 'rgba(0, 212, 255, 0.3)'}`,
                  boxShadow: isActive
                    ? '0 0 20px rgba(255,140,0,0.3)'
                    : '0 0 20px rgba(0, 212, 255, 0.15), inset 0 0 20px rgba(0, 212, 255, 0.05)',
                }}
              >
                {/* Camera Feed Placeholder */}
                <div className="relative aspect-video bg-gradient-to-br from-gray-900 to-gray-800">
                  <div
                    className="absolute inset-0 opacity-20"
                    style={{
                      backgroundImage: 'linear-gradient(0deg, transparent 24%, rgba(0, 212, 255, 0.3) 25%, rgba(0, 212, 255, 0.3) 26%, transparent 27%, transparent 74%, rgba(0, 212, 255, 0.3) 75%, rgba(0, 212, 255, 0.3) 76%, transparent 77%, transparent), linear-gradient(90deg, transparent 24%, rgba(0, 212, 255, 0.3) 25%, rgba(0, 212, 255, 0.3) 26%, transparent 27%, transparent 74%, rgba(0, 212, 255, 0.3) 75%, rgba(0, 212, 255, 0.3) 76%, transparent 77%, transparent)',
                      backgroundSize: '30px 30px',
                    }}
                  />

                  <motion.div
                    className="absolute inset-x-0 h-1 bg-gradient-to-r from-transparent via-cyan-400 to-transparent"
                    style={{ boxShadow: '0 0 20px rgba(0, 212, 255, 0.8)' }}
                    animate={{ top: ['0%', '100%'] }}
                    transition={{ duration: 3, repeat: Infinity, ease: 'linear' }}
                  />

                  <div className="absolute inset-0 flex items-center justify-center">
                    <Camera className="w-16 h-16 text-cyan-400/30" />
                    <div className="absolute inset-0 flex items-center justify-center">
                      <div className="relative w-24 h-24">
                        <div className="absolute top-0 left-1/2 w-px h-6 bg-cyan-400/50" />
                        <div className="absolute bottom-0 left-1/2 w-px h-6 bg-cyan-400/50" />
                        <div className="absolute left-0 top-1/2 w-6 h-px bg-cyan-400/50" />
                        <div className="absolute right-0 top-1/2 w-6 h-px bg-cyan-400/50" />
                        <div className="absolute top-0 left-0 w-4 h-4 border-l-2 border-t-2 border-cyan-400/50" />
                        <div className="absolute top-0 right-0 w-4 h-4 border-r-2 border-t-2 border-cyan-400/50" />
                        <div className="absolute bottom-0 left-0 w-4 h-4 border-l-2 border-b-2 border-cyan-400/50" />
                        <div className="absolute bottom-0 right-0 w-4 h-4 border-r-2 border-b-2 border-cyan-400/50" />
                      </div>
                    </div>
                  </div>

                  <div className="absolute top-2 left-2 flex items-center gap-2">
                    <motion.div
                      className="w-2 h-2 rounded-full bg-red-500"
                      animate={{ opacity: [1, 0.3, 1], boxShadow: ['0 0 5px #ff0000', '0 0 15px #ff0000', '0 0 5px #ff0000'] }}
                      transition={{ duration: 2, repeat: Infinity }}
                    />
                    <span className="text-xs text-red-500 font-mono">REC</span>
                  </div>

                  <div className="absolute top-2 right-2">
                    <span className="text-xs text-cyan-400/80 font-mono">
                      {new Date().toLocaleTimeString()}
                    </span>
                  </div>

                  <div className="absolute bottom-0 left-0 right-0 p-2 bg-gradient-to-t from-black/80 to-transparent">
                    <div className="flex items-center justify-between">
                      <div className="flex items-center gap-2">
                        <Radio className="w-3 h-3 text-cyan-400" />
                        <span className="text-xs text-cyan-400 font-mono">LIVE</span>
                      </div>
                      <div className="flex items-center gap-2">
                        <div className="w-1 h-1 rounded-full"
                             style={{
                               background: camera.status === 'active' ? '#00ff00' : '#ff4500',
                               boxShadow: camera.status === 'active' ? '0 0 5px #00ff00' : '0 0 5px #ff4500',
                             }} />
                        <span className="text-xs uppercase"
                              style={{ color: camera.status === 'active' ? '#4ade80' : '#ff4500' }}>
                          {camera.status}
                        </span>
                      </div>
                    </div>
                  </div>
                </div>

                {/* Camera Name */}
                <div className="p-3 border-t" style={{ borderColor: 'rgba(0, 212, 255, 0.2)' }}>
                  <div className="flex items-center justify-between">
                    <div className="flex items-center gap-2">
                      <Camera className="w-4 h-4 text-cyan-400" />
                      <span className="text-sm text-cyan-400 font-medium">{camera.name}</span>
                    </div>
                    <motion.button
                      onClick={() => handleView(camera.id, camera.externalUrl)}
                      className="px-3 py-1 rounded-lg text-xs transition-all"
                      style={{
                        background: isActive ? 'rgba(255,140,0,0.2)' : 'rgba(0, 212, 255, 0.1)',
                        border: `1px solid ${isActive ? 'rgba(255,140,0,0.5)' : 'rgba(0, 212, 255, 0.3)'}`,
                        color: isActive ? '#ff8c00' : '#00d4ff',
                      }}
                      whileHover={{ background: 'rgba(0, 212, 255, 0.2)', boxShadow: '0 0 15px rgba(0, 212, 255, 0.3)' }}
                    >
                      {isActive ? 'Stop' : 'View'}
                    </motion.button>
                  </div>
                </div>

                <motion.div
                  className="absolute inset-x-0 h-px bg-gradient-to-r from-transparent via-cyan-400 to-transparent opacity-30 pointer-events-none"
                  animate={{ top: ['0%', '100%'] }}
                  transition={{ duration: 4, repeat: Infinity, ease: 'linear', delay: index * 0.5 }}
                />
              </div>
            </motion.div>
          );
        })}
      </div>
    </div>
  );
}
