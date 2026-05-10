import { motion } from 'motion/react';
import { useState } from 'react';
import { Lightbulb, Tv, Bell, Shield, Network, RefreshCw, Check, AlertCircle, DollarSign, Settings as SettingsIcon } from 'lucide-react';
import { useSmartHomeContext } from '../SmartHomeContext';
import { onHostMessage } from '../bridge';
import { useEffect } from 'react';
import { UsageDashboard } from '../components/UsageDashboard';
import { useAtlasAI } from '../ai/useAtlasAI';

export function Settings() {
  const { state, saveSettings, linkHueBridge, linkLgTv, discoverLgTv, loginRing, saveAgentSettings } = useSmartHomeContext();
  const atlasAI = useAtlasAI();

  const [activeTab, setActiveTab] = useState<'integrations' | 'usage'>('integrations');
  const [feedback, setFeedback] = useState('');
  const [feedbackOk, setFeedbackOk] = useState(true);
  const [aiStatus, setAIStatus] = useState<any>(null);

  // Hue
  const [hueBridgeIp, setHueBridgeIp] = useState('');
  // LG
  const [lgHost, setLgHost] = useState('');
  // Ring
  const [ringEmail, setRingEmail] = useState('');
  const [ringPassword, setRingPassword] = useState('');
  const [ringCode, setRingCode] = useState('');
  const [ringNeedsMfa, setRingNeedsMfa] = useState(false);
  // Govee
  const [goveeApiKey, setGoveeApiKey] = useState('');

  // Load AI status when usage tab is active
  useEffect(() => {
    console.log('[Settings] Tab changed to:', activeTab);
    console.log('[Settings] atlasAI.isReady:', atlasAI.isReady);
    console.log('[Settings] atlasAI.getAIStatus exists:', !!atlasAI.getAIStatus);
    
    if (activeTab === 'usage' && atlasAI.isReady) {
      const status = atlasAI.getAIStatus?.();
      console.log('[Settings] AI Status:', status);
      setAIStatus(status || null);
    }
  }, [activeTab, atlasAI.isReady, atlasAI.messages.length]); // Refresh when messages change

  useEffect(() => {
    const unsub = onHostMessage((type, payload) => {
      const p = payload as any;
      if (type === 'smart-home.actionResult') {
        setFeedbackOk(true);
        setFeedback(p?.message ?? 'Saved');
        if (p?.message?.toLowerCase().includes('two-factor') || p?.message?.toLowerCase().includes('2fa') || p?.message?.toLowerCase().includes('code')) {
          setRingNeedsMfa(true);
        }
        setTimeout(() => setFeedback(''), 4000);
      } else if (type === 'smart-home.error') {
        setFeedbackOk(false);
        setFeedback(p?.message ?? 'Error');
        setTimeout(() => setFeedback(''), 5000);
      } else if (type === 'smart-home.settingsSaved') {
        setFeedbackOk(true);
        setFeedback('Settings saved.');
        setTimeout(() => setFeedback(''), 3000);
      }
    });
    return unsub;
  }, []);

  const hue = state?.providers.find(p => p.providerId === 'philips_hue');
  const ring = state?.providers.find(p => p.providerId === 'ring');
  const lg = state?.providers.find(p => p.providerId === 'lg_webos');
  const govee = state?.providers.find(p => p.providerId === 'govee');
  const agent = state?.agentSettings;

  const providerCard = (
    title: string,
    icon: React.ReactNode,
    configured: boolean,
    status: string,
    detail: string,
    children: React.ReactNode
  ) => (
    <motion.div className="rounded-xl p-6 backdrop-blur-xl mb-4"
      style={{
        background: configured ? 'rgba(0,212,255,0.08)' : 'rgba(5,10,18,0.6)',
        border: `1px solid ${configured ? 'rgba(0,212,255,0.4)' : 'rgba(0,212,255,0.2)'}`,
        boxShadow: configured ? '0 0 20px rgba(0,212,255,0.15)' : 'none',
      }}
      initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }}>
      <div className="flex items-center gap-3 mb-4">
        <div className="w-10 h-10 rounded-lg flex items-center justify-center"
             style={{ background: 'rgba(0,212,255,0.1)', border: '1px solid rgba(0,212,255,0.3)' }}>
          {icon}
        </div>
        <div className="flex-1">
          <h4 className="text-cyan-400 font-medium">{title}</h4>
          <p className="text-xs text-cyan-400/60">{detail || status}</p>
        </div>
        <div className="flex items-center gap-2">
          <div className="w-2 h-2 rounded-full"
               style={{ background: configured ? '#00ff00' : '#ff8c00', boxShadow: configured ? '0 0 8px #00ff00' : '0 0 8px #ff8c00' }} />
          <span className="text-xs" style={{ color: configured ? '#4ade80' : '#ff8c00' }}>
            {configured ? 'Connected' : 'Not Connected'}
          </span>
        </div>
      </div>
      {children}
    </motion.div>
  );

  return (
    <>
      <motion.div className="mb-8" initial={{ opacity: 0, y: -20 }} animate={{ opacity: 1, y: 0 }}>
        <div className="flex items-center gap-3 mb-2">
          <div className="w-2 h-2 rounded-full bg-cyan-400 animate-pulse" style={{ boxShadow: '0 0 10px #00d4ff' }} />
          <h1 className="text-4xl font-bold"
              style={{ background: 'linear-gradient(135deg, #00d4ff, #0066ff)', WebkitBackgroundClip: 'text', WebkitTextFillColor: 'transparent' }}>
            System Settings
          </h1>
        </div>
        <p className="text-cyan-400/60 text-sm ml-5">Configure Atlas Smart Home integrations</p>
      </motion.div>

      {/* Tabs */}
      <div className="flex gap-2 mb-6">
        <button
          onClick={() => setActiveTab('integrations')}
          className={`px-4 py-2 rounded-lg text-sm font-medium transition-all ${
            activeTab === 'integrations' ? 'text-cyan-400' : 'text-white/60'
          }`}
          style={{
            background: activeTab === 'integrations' ? 'rgba(0,212,255,0.2)' : 'rgba(255,255,255,0.05)',
            border: `1px solid ${activeTab === 'integrations' ? 'rgba(0,212,255,0.5)' : 'rgba(255,255,255,0.1)'}`
          }}
        >
          <SettingsIcon className="w-4 h-4 inline mr-2" />
          Integrations
        </button>
        <button
          onClick={() => setActiveTab('usage')}
          className={`px-4 py-2 rounded-lg text-sm font-medium transition-all ${
            activeTab === 'usage' ? 'text-cyan-400' : 'text-white/60'
          }`}
          style={{
            background: activeTab === 'usage' ? 'rgba(0,212,255,0.2)' : 'rgba(255,255,255,0.05)',
            border: `1px solid ${activeTab === 'usage' ? 'rgba(0,212,255,0.5)' : 'rgba(255,255,255,0.1)'}`
          }}
        >
          <DollarSign className="w-4 h-4 inline mr-2" />
          AI Usage & Budget
        </button>
      </div>

      {/* Feedback */}
      {feedback && (
        <motion.div className="mb-6 rounded-xl p-4 flex items-center gap-3"
          style={{
            background: feedbackOk ? 'rgba(0,212,255,0.1)' : 'rgba(255,70,70,0.1)',
            border: `1px solid ${feedbackOk ? 'rgba(0,212,255,0.4)' : 'rgba(255,70,70,0.4)'}`,
          }}
          initial={{ opacity: 0, y: -10 }} animate={{ opacity: 1, y: 0 }}>
          {feedbackOk ? <Check className="w-5 h-5 text-cyan-400" /> : <AlertCircle className="w-5 h-5 text-red-400" />}
          <p className="text-sm" style={{ color: feedbackOk ? '#00d4ff' : '#f87171' }}>{feedback}</p>
        </motion.div>
      )}

      {/* Tab Content */}
      {activeTab === 'usage' && (
        <div className="space-y-6">
          <motion.div className="rounded-xl p-6 backdrop-blur-xl"
            style={{ background: 'rgba(5,10,18,0.6)', border: '1px solid rgba(0,212,255,0.2)' }}
            initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }}>
            <h4 className="text-cyan-400 font-medium mb-4">AI Usage & Budget</h4>
            
            {!atlasAI.isReady && (
              <p className="text-white/40 text-sm">AI system initializing...</p>
            )}
            
            {atlasAI.isReady && !aiStatus && (
              <p className="text-white/40 text-sm">Loading usage data...</p>
            )}
            
            {atlasAI.isReady && aiStatus && (
              <>
                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <p className="text-xs text-white/40 mb-1">Current Provider</p>
                    <p className="text-white">{aiStatus.provider}</p>
                  </div>
                  <div>
                    <p className="text-xs text-white/40 mb-1">Current Model</p>
                    <p className="text-white">{aiStatus.model}</p>
                  </div>
                  <div>
                    <p className="text-xs text-white/40 mb-1">Spend Today</p>
                    <p className="text-cyan-400">
                      {aiStatus.spendToday > 0 ? `$${aiStatus.spendToday.toFixed(4)}` : 'No live data yet'}
                    </p>
                  </div>
                  <div>
                    <p className="text-xs text-white/40 mb-1">Spend This Month</p>
                    <p className="text-cyan-400">
                      {aiStatus.spendThisMonth > 0 ? `$${aiStatus.spendThisMonth.toFixed(4)}` : 'No live data yet'}
                    </p>
                  </div>
                  <div>
                    <p className="text-xs text-white/40 mb-1">Remaining Budget</p>
                    <p className="text-green-400">${aiStatus.remainingBudget.toFixed(2)}</p>
                  </div>
                  <div>
                    <p className="text-xs text-white/40 mb-1">Current Cost Mode</p>
                    <p className="text-white capitalize">{aiStatus.costMode}</p>
                  </div>
                </div>
                
                {aiStatus.lastRequest && (
                  <div className="mt-6 pt-6 border-t border-cyan-400/20">
                    <h5 className="text-cyan-400 text-sm font-medium mb-3">Last Request</h5>
                    <div className="grid grid-cols-2 gap-3 text-sm">
                      <div>
                        <p className="text-xs text-white/40 mb-1">Provider</p>
                        <p className="text-white">{aiStatus.lastRequest.provider}</p>
                      </div>
                      <div>
                        <p className="text-xs text-white/40 mb-1">Model</p>
                        <p className="text-white">{aiStatus.lastRequest.model}</p>
                      </div>
                      <div>
                        <p className="text-xs text-white/40 mb-1">Input Tokens</p>
                        <p className="text-white">{aiStatus.lastRequest.inputTokens}</p>
                      </div>
                      <div>
                        <p className="text-xs text-white/40 mb-1">Output Tokens</p>
                        <p className="text-white">{aiStatus.lastRequest.outputTokens}</p>
                      </div>
                      <div>
                        <p className="text-xs text-white/40 mb-1">Total Tokens</p>
                        <p className="text-white">{aiStatus.lastRequest.totalTokens}</p>
                      </div>
                      <div>
                        <p className="text-xs text-white/40 mb-1">Estimated Cost</p>
                        <p className="text-cyan-400">${aiStatus.lastRequest.estimatedCost.toFixed(6)}</p>
                      </div>
                      <div>
                        <p className="text-xs text-white/40 mb-1">Timestamp</p>
                        <p className="text-white text-xs">
                          {new Date(aiStatus.lastRequest.timestamp).toLocaleTimeString()}
                        </p>
                      </div>
                      <div>
                        <p className="text-xs text-white/40 mb-1">Status</p>
                        <p className={aiStatus.lastRequest.success ? 'text-green-400' : 'text-red-400'}>
                          {aiStatus.lastRequest.success ? 'Success' : 'Failed'}
                        </p>
                      </div>
                    </div>
                  </div>
                )}
                
                {!aiStatus.lastRequest && (
                  <p className="text-xs text-white/40 mt-4">
                    No AI requests yet. Send a query in the chat to see usage data.
                  </p>
                )}
              </>
            )}
          </motion.div>
        </div>
      )}

      {activeTab === 'integrations' && (
        <>
      {/* Philips Hue */}
      {providerCard('Philips Hue', <Lightbulb className="w-5 h-5 text-cyan-400" />,
        hue?.descriptor.isConfigured ?? false, hue?.descriptor.status ?? '', hue?.descriptor.detail ?? '',
        <div className="space-y-3">
          <div className="flex gap-2">
            <input value={hueBridgeIp} onChange={e => setHueBridgeIp(e.target.value)}
              placeholder={hue?.savedSettings.bridgeIp || 'Bridge IP (e.g. 192.168.1.100)'}
              className="flex-1 px-3 py-2 rounded-lg text-sm bg-transparent outline-none placeholder:text-cyan-400/30"
              style={{ border: '1px solid rgba(0,212,255,0.3)', color: '#00d4ff' }} />
            <motion.button onClick={() => linkHueBridge(hueBridgeIp || hue?.savedSettings.bridgeIp || '')}
              className="px-4 py-2 rounded-lg text-sm"
              style={{ background: 'rgba(0,212,255,0.2)', border: '1px solid rgba(0,212,255,0.5)', color: '#00d4ff' }}
              whileHover={{ scale: 1.02 }} whileTap={{ scale: 0.98 }}>
              {hue?.descriptor.isConfigured ? 'Re-link' : 'Link Bridge'}
            </motion.button>
          </div>
          {hue?.devices && hue.devices.length > 0 && (
            <p className="text-xs text-cyan-400/60">{hue.devices.length} lights connected</p>
          )}
        </div>
      )}

      {/* Ring */}
      {providerCard('Ring', <Shield className="w-5 h-5 text-cyan-400" />,
        ring?.descriptor.isConfigured ?? false, ring?.descriptor.status ?? '', ring?.descriptor.detail ?? '',
        <div className="space-y-3">
          {!ring?.descriptor.isConfigured && (
            <>
              <div className="grid grid-cols-2 gap-2">
                <input value={ringEmail} onChange={e => setRingEmail(e.target.value)}
                  placeholder="Ring email" type="email"
                  className="px-3 py-2 rounded-lg text-sm bg-transparent outline-none placeholder:text-cyan-400/30"
                  style={{ border: '1px solid rgba(0,212,255,0.3)', color: '#00d4ff' }} />
                <input value={ringPassword} onChange={e => setRingPassword(e.target.value)}
                  placeholder="Password" type="password"
                  className="px-3 py-2 rounded-lg text-sm bg-transparent outline-none placeholder:text-cyan-400/30"
                  style={{ border: '1px solid rgba(0,212,255,0.3)', color: '#00d4ff' }} />
              </div>
              {ringNeedsMfa && (
                <input value={ringCode} onChange={e => setRingCode(e.target.value)}
                  placeholder="2FA code from email/SMS"
                  className="w-full px-3 py-2 rounded-lg text-sm bg-transparent outline-none placeholder:text-cyan-400/30"
                  style={{ border: '1px solid rgba(255,140,0,0.5)', color: '#ff8c00' }} />
              )}
              <motion.button onClick={() => loginRing(ringEmail, ringPassword, ringCode)}
                className="w-full px-4 py-2 rounded-lg text-sm"
                style={{ background: 'rgba(0,212,255,0.2)', border: '1px solid rgba(0,212,255,0.5)', color: '#00d4ff' }}
                whileHover={{ scale: 1.02 }} whileTap={{ scale: 0.98 }}>
                Sign In to Ring
              </motion.button>
            </>
          )}
          {ring?.descriptor.isConfigured && ring.devices.length > 0 && (
            <p className="text-xs text-cyan-400/60">{ring.devices.length} cameras/devices connected</p>
          )}
        </div>
      )}

      {/* LG WebOS */}
      {providerCard('LG WebOS TV', <Tv className="w-5 h-5 text-cyan-400" />,
        lg?.descriptor.isConfigured ?? false, lg?.descriptor.status ?? '', lg?.descriptor.detail ?? '',
        <div className="space-y-3">
          <div className="flex gap-2">
            <input value={lgHost} onChange={e => setLgHost(e.target.value)}
              placeholder={lg?.savedSettings.host || 'TV IP address'}
              className="flex-1 px-3 py-2 rounded-lg text-sm bg-transparent outline-none placeholder:text-cyan-400/30"
              style={{ border: '1px solid rgba(0,212,255,0.3)', color: '#00d4ff' }} />
            <motion.button onClick={() => discoverLgTv()}
              className="px-3 py-2 rounded-lg text-sm flex items-center gap-1"
              style={{ background: 'rgba(0,212,255,0.1)', border: '1px solid rgba(0,212,255,0.3)', color: '#00d4ff' }}
              whileHover={{ scale: 1.02 }}>
              <RefreshCw className="w-4 h-4" /> Discover
            </motion.button>
            <motion.button onClick={() => linkLgTv(lgHost || lg?.savedSettings.host || '')}
              className="px-4 py-2 rounded-lg text-sm"
              style={{ background: 'rgba(0,212,255,0.2)', border: '1px solid rgba(0,212,255,0.5)', color: '#00d4ff' }}
              whileHover={{ scale: 1.02 }} whileTap={{ scale: 0.98 }}>
              {lg?.descriptor.isConfigured ? 'Re-link' : 'Link TV'}
            </motion.button>
          </div>
          {lg?.devices && lg.devices.length > 0 && (
            <p className="text-xs text-cyan-400/60">{lg.devices.length} TV(s) connected</p>
          )}
        </div>
      )}

      {/* Govee */}
      {providerCard('Govee', <Network className="w-5 h-5 text-cyan-400" />,
        govee?.descriptor.isConfigured ?? false, govee?.descriptor.status ?? '', govee?.descriptor.detail ?? '',
        <div className="flex gap-2">
          <input value={goveeApiKey} onChange={e => setGoveeApiKey(e.target.value)}
            placeholder="Govee API Key"
            className="flex-1 px-3 py-2 rounded-lg text-sm bg-transparent outline-none placeholder:text-cyan-400/30"
            style={{ border: '1px solid rgba(0,212,255,0.3)', color: '#00d4ff' }} />
          <motion.button onClick={() => saveSettings('govee', { apiKey: goveeApiKey, enabled: true })}
            className="px-4 py-2 rounded-lg text-sm"
            style={{ background: 'rgba(0,212,255,0.2)', border: '1px solid rgba(0,212,255,0.5)', color: '#00d4ff' }}
            whileHover={{ scale: 1.02 }} whileTap={{ scale: 0.98 }}>
            Save
          </motion.button>
        </div>
      )}

      {/* Agent Settings */}
      {agent && (
        <motion.div className="rounded-xl p-6 backdrop-blur-xl"
          style={{ background: 'rgba(5,10,18,0.6)', border: '1px solid rgba(0,212,255,0.2)' }}
          initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }}>
          <h4 className="text-cyan-400 font-medium mb-4 flex items-center gap-2">
            <div className="w-1 h-4 bg-cyan-400 rounded-full" style={{ boxShadow: '0 0 10px #00d4ff' }} />
            Agent Settings
          </h4>
          <div className="space-y-4">
            {[
              { label: 'Voice Commands', key: 'voiceCommandsEnabled' as const, value: agent.voiceCommandsEnabled },
              { label: 'Device Shortcuts in Sidebar', key: 'showDeviceShortcutsInSidebar' as const, value: agent.showDeviceShortcutsInSidebar },
            ].map(s => (
              <div key={s.key} className="flex items-center justify-between">
                <span className="text-sm text-cyan-400">{s.label}</span>
                <button onClick={() => saveAgentSettings({ [s.key]: !s.value })}
                  className="w-12 h-6 rounded-full relative transition-all"
                  style={{ background: s.value ? 'rgba(0,212,255,0.3)' : 'rgba(100,100,100,0.3)' }}>
                  <motion.div className="absolute top-1 w-4 h-4 rounded-full"
                    style={{ background: s.value ? '#00d4ff' : '#666', boxShadow: s.value ? '0 0 10px #00d4ff' : 'none' }}
                    animate={{ left: s.value ? 'calc(100% - 20px)' : '4px' }}
                    transition={{ type: 'spring', stiffness: 500, damping: 30 }} />
                </button>
              </div>
            ))}
            <div className="flex items-center justify-between">
              <span className="text-sm text-cyan-400">Default Volume Step</span>
              <div className="flex items-center gap-2">
                <button onClick={() => saveAgentSettings({ defaultVolumeStep: Math.max(1, agent.defaultVolumeStep - 1) })}
                  className="w-7 h-7 rounded-lg text-cyan-400 flex items-center justify-center"
                  style={{ background: 'rgba(0,212,255,0.1)', border: '1px solid rgba(0,212,255,0.3)' }}>−</button>
                <span className="text-cyan-400 w-6 text-center">{agent.defaultVolumeStep}</span>
                <button onClick={() => saveAgentSettings({ defaultVolumeStep: Math.min(25, agent.defaultVolumeStep + 1) })}
                  className="w-7 h-7 rounded-lg text-cyan-400 flex items-center justify-center"
                  style={{ background: 'rgba(0,212,255,0.1)', border: '1px solid rgba(0,212,255,0.3)' }}>+</button>
              </div>
            </div>
          </div>
        </motion.div>
      )}
        </>
      )}
    </>
  );
}
