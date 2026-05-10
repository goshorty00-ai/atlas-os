import { useState } from 'react';
import { Activity, TrendingUp, AlertCircle, CheckCircle2 } from 'lucide-react';
import { AreaChart, Area, ResponsiveContainer } from 'recharts';
import { postToHost } from '../atlasBridge';

type ApiIntegration = {
  id: string;
  name: string;
  status: 'online' | 'warning' | 'offline' | 'unknown';
  latencyMs?: number;
  requests?: number;
  uptime?: number;
};

interface APICardsProps {
  integrations: ApiIntegration[];
  onConfigure: (id: string) => void;
}

function buildSparkline(latencyMs?: number) {
  const base = Math.max(0, Math.min(9999, Math.floor(latencyMs ?? 0)));
  const jitter = base > 0 ? Math.max(2, Math.floor(base * 0.06)) : 0;
  const values = [];
  for (let i = 0; i < 12; i++) {
    const v = base === 0 ? 0 : base + ((i % 2 === 0 ? -1 : 1) * jitter);
    values.push({ value: Math.max(0, v) });
  }
  return values;
}

export function APICards({ integrations, onConfigure }: APICardsProps) {
  const [editingId, setEditingId] = useState<string | null>(null);
  const [apiKeyDraft, setApiKeyDraft] = useState('');

  const apis = (integrations ?? []).map((x) => ({
    id: x.id,
    name: x.name,
    status: x.status,
    latency: x.latencyMs ?? 0,
    requests: x.requests ?? 0,
    uptime: x.uptime ?? 0,
    data: buildSparkline(x.latencyMs),
  }));

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h2 className="text-xl font-light text-gray-300">
          Active API Integrations
        </h2>
        <div className="flex items-center space-x-2 text-sm text-gray-500">
          <Activity className="w-4 h-4 text-green-400 animate-pulse" />
          <span>Live monitoring</span>
        </div>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        {apis.map((api) => (
          <div
            key={api.id}
            className="group relative rounded-xl border border-blue-500/20 bg-gradient-to-br from-blue-500/5 to-violet-500/5 backdrop-blur-xl p-6 hover:border-blue-400/40 transition-all"
            style={{
              boxShadow: '0 8px 32px 0 rgba(31, 38, 135, 0.15)'
            }}
          >
            {/* Glassmorphism overlay */}
            <div className="absolute inset-0 rounded-xl bg-gradient-to-br from-white/5 to-white/0 pointer-events-none" />
            
            {/* Glowing edge effect on hover */}
            <div className="absolute inset-0 rounded-xl bg-gradient-to-r from-blue-500/0 via-blue-500/10 to-violet-500/0 opacity-0 group-hover:opacity-100 transition-opacity" />

            <div className="relative space-y-4">
              {/* Header */}
              <div className="flex items-start justify-between">
                <div className="space-y-1">
                  <h3 className="text-lg font-medium text-gray-200">{api.name}</h3>
                  <div className="flex items-center space-x-2">
                    {api.status === 'online' ? (
                      <>
                        <CheckCircle2 className="w-3 h-3 text-green-400" />
                        <span className="text-xs text-green-400">Online</span>
                      </>
                    ) : api.status === 'warning' ? (
                      <>
                        <AlertCircle className="w-3 h-3 text-yellow-400" />
                        <span className="text-xs text-yellow-400">Warning</span>
                      </>
                    ) : api.status === 'unknown' ? (
                      <>
                        <AlertCircle className="w-3 h-3 text-gray-400" />
                        <span className="text-xs text-gray-400">Unknown</span>
                      </>
                    ) : (
                      <>
                        <AlertCircle className="w-3 h-3 text-red-400" />
                        <span className="text-xs text-red-400">Offline</span>
                      </>
                    )}
                    <div className="w-1 h-1 rounded-full bg-gray-600" />
                    <span className="text-xs text-gray-500">{api.uptime}% uptime</span>
                  </div>
                </div>
                
                <div className={`px-2 py-1 rounded-md text-xs font-medium ${
                  api.latency < 100 
                    ? 'bg-green-500/20 text-green-300 border border-green-500/30' 
                    : 'bg-blue-500/20 text-blue-300 border border-blue-500/30'
                }`}>
                  {api.latency > 0 ? `${api.latency}ms` : '—'}
                </div>
              </div>

              {/* Stats */}
              <div className="flex items-center space-x-6 text-sm">
                <div className="flex items-center space-x-2">
                  <TrendingUp className="w-4 h-4 text-blue-400" />
                  <span className="text-gray-400">Requests:</span>
                  <span className="text-gray-200 font-medium">{api.requests.toLocaleString()}</span>
                </div>
              </div>

              {/* Activity graph */}
              <div className="h-16 -mx-2">
                <ResponsiveContainer width="100%" height="100%">
                  <AreaChart data={api.data}>
                    <defs>
                      <linearGradient id={`gradient-${api.id}`} x1="0" y1="0" x2="0" y2="1">
                        <stop offset="5%" stopColor={api.status === 'warning' ? '#facc15' : '#3b82f6'} stopOpacity={0.3} />
                        <stop offset="95%" stopColor={api.status === 'warning' ? '#facc15' : '#3b82f6'} stopOpacity={0} />
                      </linearGradient>
                    </defs>
                    <Area
                      type="monotone"
                      dataKey="value"
                      stroke={api.status === 'warning' ? '#facc15' : '#3b82f6'}
                      strokeWidth={2}
                      fill={`url(#gradient-${api.id})`}
                      animationDuration={500}
                    />
                  </AreaChart>
                </ResponsiveContainer>
              </div>

              {/* Divider */}
              <div className="h-[1px] bg-gradient-to-r from-transparent via-blue-500/30 to-transparent" />

              {/* Footer actions */}
              <div className="flex items-center justify-between text-xs">
                <button
                  className="text-blue-400 hover:text-blue-300 transition-colors"
                  onClick={() => postToHost('api.testIntegration', { id: api.id })}
                >
                  Test →
                </button>
                <div className="flex items-center gap-3">
                  <button
                    className="text-red-400 hover:text-red-300 transition-colors"
                    onClick={() => {
                      postToHost('api.removeIntegration', { id: api.id });
                      postToHost('api.getState');
                    }}
                  >
                    Remove
                  </button>
                  <button
                    className="text-gray-500 hover:text-gray-400 transition-colors"
                    onClick={() => {
                      if (api.id === 'elevenlabs') {
                        setEditingId((cur) => (cur === api.id ? null : api.id));
                        setApiKeyDraft('');
                        return;
                      }
                      if (api.id === 'addon_servers' || api.id.startsWith('addon_')) {
                        postToHost('api.openSettings', { id: 'addon_servers' });
                        return;
                      }
                      onConfigure(api.id);
                    }}
                  >
                    Configure
                  </button>
                </div>
              </div>

              {editingId === api.id && api.id === 'elevenlabs' && (
                <div className="pt-4 space-y-3">
                  <div className="h-[1px] bg-gradient-to-r from-transparent via-blue-500/30 to-transparent" />

                  <div className="space-y-2">
                    <label className="text-xs text-gray-400">ElevenLabs API Key</label>
                    <input
                      type="password"
                      placeholder="Paste your xi-api-key…"
                      value={apiKeyDraft}
                      onChange={(e) => setApiKeyDraft(e.target.value)}
                      className="w-full h-10 px-4 bg-gray-900/50 border border-blue-500/20 rounded-lg text-sm text-gray-300 placeholder-gray-600 focus:outline-none focus:border-blue-400/50 focus:bg-gray-900/70 transition-all font-mono"
                    />
                  </div>

                  <div className="flex items-center gap-3">
                    <button
                      className="h-10 px-4 rounded-lg bg-gradient-to-r from-blue-500 to-violet-500 hover:from-blue-400 hover:to-violet-400 text-white font-medium text-sm transition-all shadow-lg shadow-blue-500/30 hover:shadow-blue-500/50"
                      onClick={() => {
                        postToHost('api.setVoiceKey', { provider: 'elevenlabs', apiKey: apiKeyDraft });
                        postToHost('api.getState');
                        setEditingId(null);
                        setApiKeyDraft('');
                      }}
                    >
                      Save
                    </button>
                    <button
                      className="h-10 px-4 rounded-lg border border-blue-500/20 bg-gray-900/30 hover:border-blue-400/30 hover:bg-gray-900/50 text-gray-400 hover:text-gray-300 text-sm transition-all"
                      onClick={() => {
                        setEditingId(null);
                        setApiKeyDraft('');
                      }}
                    >
                      Cancel
                    </button>
                  </div>
                </div>
              )}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
