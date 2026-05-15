import { Settings, Server, Palette, Bell, Shield, Database, Sparkles, Film, FolderOpen } from 'lucide-react';
import { useState } from 'react';

export function SettingsPage() {
  const [notifications, setNotifications] = useState(true);
  const [autoScan, setAutoScan] = useState(true);
  const [aiRecommendations, setAiRecommendations] = useState(true);

  return (
    <div className="space-y-8">
      {/* Header */}
      <div className="flex items-center gap-4">
        <div className="p-3 rounded-xl bg-gradient-to-r from-slate-600 to-slate-700">
          <Settings size={28} className="text-white" />
        </div>
        <div>
          <h1 className="text-slate-100 text-3xl">Settings</h1>
          <p className="text-slate-400">Configure your AI Media Centre</p>
        </div>
      </div>

      {/* Settings Sections */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        {/* General */}
        <div className="bg-gradient-to-br from-slate-900/80 to-slate-800/80 backdrop-blur-xl rounded-xl border border-slate-700/50 p-6">
          <div className="flex items-center gap-3 mb-6">
            <div className="p-2 rounded-lg bg-cyan-500/20">
              <Settings size={20} className="text-cyan-300" />
            </div>
            <h3 className="text-slate-100">General</h3>
          </div>
          <div className="space-y-4">
            <div className="flex items-center justify-between">
              <div>
                <h4 className="text-slate-200 text-sm">Enable Notifications</h4>
                <p className="text-xs text-slate-400">Get notified about new content</p>
              </div>
              <button
                onClick={() => setNotifications(!notifications)}
                className={`relative w-11 h-6 rounded-full transition-colors ${
                  notifications ? 'bg-cyan-500' : 'bg-slate-700'
                }`}
              >
                <div
                  className={`absolute top-1 w-4 h-4 rounded-full bg-white transition-transform ${
                    notifications ? 'translate-x-6' : 'translate-x-1'
                  }`}
                />
              </button>
            </div>

            <div className="flex items-center justify-between">
              <div>
                <h4 className="text-slate-200 text-sm">Auto Library Scan</h4>
                <p className="text-xs text-slate-400">Automatically scan for new media</p>
              </div>
              <button
                onClick={() => setAutoScan(!autoScan)}
                className={`relative w-11 h-6 rounded-full transition-colors ${
                  autoScan ? 'bg-cyan-500' : 'bg-slate-700'
                }`}
              >
                <div
                  className={`absolute top-1 w-4 h-4 rounded-full bg-white transition-transform ${
                    autoScan ? 'translate-x-6' : 'translate-x-1'
                  }`}
                />
              </button>
            </div>

            <div className="flex items-center justify-between">
              <div>
                <h4 className="text-slate-200 text-sm">AI Recommendations</h4>
                <p className="text-xs text-slate-400">Personalized content suggestions</p>
              </div>
              <button
                onClick={() => setAiRecommendations(!aiRecommendations)}
                className={`relative w-11 h-6 rounded-full transition-colors ${
                  aiRecommendations ? 'bg-cyan-500' : 'bg-slate-700'
                }`}
              >
                <div
                  className={`absolute top-1 w-4 h-4 rounded-full bg-white transition-transform ${
                    aiRecommendations ? 'translate-x-6' : 'translate-x-1'
                  }`}
                />
              </button>
            </div>
          </div>
        </div>

        {/* Servers */}
        <div className="bg-gradient-to-br from-slate-900/80 to-slate-800/80 backdrop-blur-xl rounded-xl border border-slate-700/50 p-6">
          <div className="flex items-center gap-3 mb-6">
            <div className="p-2 rounded-lg bg-purple-500/20">
              <Server size={20} className="text-purple-300" />
            </div>
            <h3 className="text-slate-100">Servers</h3>
          </div>
          <div className="space-y-3">
            {[
              { name: 'Plex Main', status: 'Connected', color: 'green' },
              { name: 'Jellyfin 4K', status: 'Connected', color: 'green' },
              { name: 'Local NAS', status: 'Offline', color: 'red' },
            ].map((server) => (
              <div
                key={server.name}
                className="flex items-center justify-between p-3 rounded-lg bg-slate-950/50 border border-slate-700/30"
              >
                <div>
                  <h4 className="text-slate-200 text-sm">{server.name}</h4>
                  <p className={`text-xs ${
                    server.color === 'green' ? 'text-green-400' : 'text-red-400'
                  }`}>
                    {server.status}
                  </p>
                </div>
                <button className="px-3 py-1.5 rounded-lg bg-slate-800/50 text-slate-300 hover:text-white hover:bg-slate-700 transition-all text-sm">
                  Configure
                </button>
              </div>
            ))}
            <button className="w-full px-4 py-2.5 rounded-lg border border-dashed border-slate-600 text-slate-400 hover:text-slate-200 hover:border-slate-500 transition-all text-sm">
              + Add Server
            </button>
          </div>
        </div>

        {/* Appearance */}
        <div className="bg-gradient-to-br from-slate-900/80 to-slate-800/80 backdrop-blur-xl rounded-xl border border-slate-700/50 p-6">
          <div className="flex items-center gap-3 mb-6">
            <div className="p-2 rounded-lg bg-pink-500/20">
              <Palette size={20} className="text-pink-300" />
            </div>
            <h3 className="text-slate-100">Appearance</h3>
          </div>
          <div className="space-y-4">
            <div>
              <label className="text-sm text-slate-300 mb-2 block">Theme</label>
              <select className="w-full bg-slate-950/50 text-slate-200 rounded-lg border border-slate-700 focus:border-cyan-500/50 p-2.5 outline-none">
                <option>Dark (Default)</option>
                <option>Darker</option>
                <option>OLED Black</option>
              </select>
            </div>
            <div>
              <label className="text-sm text-slate-300 mb-2 block">Accent Color</label>
              <div className="flex gap-2">
                {['cyan', 'purple', 'pink', 'green', 'amber'].map((color) => (
                  <button
                    key={color}
                    className={`w-10 h-10 rounded-lg bg-${color}-500 hover:scale-110 transition-transform`}
                  />
                ))}
              </div>
            </div>
          </div>
        </div>

        {/* Privacy & Security */}
        <div className="bg-gradient-to-br from-slate-900/80 to-slate-800/80 backdrop-blur-xl rounded-xl border border-slate-700/50 p-6">
          <div className="flex items-center gap-3 mb-6">
            <div className="p-2 rounded-lg bg-amber-500/20">
              <Shield size={20} className="text-amber-300" />
            </div>
            <h3 className="text-slate-100">Privacy & Security</h3>
          </div>
          <div className="space-y-3">
            <button className="w-full text-left px-4 py-3 rounded-lg bg-slate-950/50 border border-slate-700/30 hover:border-slate-600 text-slate-200 text-sm transition-all">
              Clear Watch History
            </button>
            <button className="w-full text-left px-4 py-3 rounded-lg bg-slate-950/50 border border-slate-700/30 hover:border-slate-600 text-slate-200 text-sm transition-all">
              Manage AI Data
            </button>
            <button className="w-full text-left px-4 py-3 rounded-lg bg-slate-950/50 border border-slate-700/30 hover:border-slate-600 text-slate-200 text-sm transition-all">
              Export Settings
            </button>
          </div>
        </div>

        {/* Library Management */}
        <div className="bg-gradient-to-br from-slate-900/80 to-slate-800/80 backdrop-blur-xl rounded-xl border border-slate-700/50 p-6">
          <div className="flex items-center gap-3 mb-6">
            <div className="p-2 rounded-lg bg-green-500/20">
              <Database size={20} className="text-green-300" />
            </div>
            <h3 className="text-slate-100">Library Management</h3>
          </div>
          <div className="space-y-3">
            <button className="w-full text-left px-4 py-3 rounded-lg bg-slate-950/50 border border-slate-700/30 hover:border-slate-600 text-slate-200 text-sm transition-all">
              Scan All Libraries
            </button>
            <button className="w-full text-left px-4 py-3 rounded-lg bg-slate-950/50 border border-slate-700/30 hover:border-slate-600 text-slate-200 text-sm transition-all">
              Clean Up Metadata
            </button>
            <button className="w-full text-left px-4 py-3 rounded-lg bg-slate-950/50 border border-slate-700/30 hover:border-slate-600 text-slate-200 text-sm transition-all">
              Optimize Database
            </button>
          </div>
        </div>

        {/* AI Settings */}
        <div className="bg-gradient-to-br from-slate-900/80 to-slate-800/80 backdrop-blur-xl rounded-xl border border-slate-700/50 p-6">
          <div className="flex items-center gap-3 mb-6">
            <div className="p-2 rounded-lg bg-violet-500/20">
              <Sparkles size={20} className="text-violet-300" />
            </div>
            <h3 className="text-slate-100">AI Settings</h3>
          </div>
          <div className="space-y-4">
            <div>
              <label className="text-sm text-slate-300 mb-2 block">Recommendation Strength</label>
              <input
                type="range"
                min="0"
                max="100"
                defaultValue="75"
                className="w-full h-2 rounded-full appearance-none cursor-pointer"
                style={{
                  background: 'linear-gradient(to right, rgb(168, 85, 247) 75%, rgb(51, 65, 85) 75%)',
                }}
              />
            </div>
            <div>
              <label className="text-sm text-slate-300 mb-2 block">Content Discovery</label>
              <select className="w-full bg-slate-950/50 text-slate-200 rounded-lg border border-slate-700 focus:border-cyan-500/50 p-2.5 outline-none">
                <option>Balanced</option>
                <option>Explore New</option>
                <option>Stay Familiar</option>
              </select>
            </div>
          </div>
        </div>
      </div>

        {/* Media Player */}
        <div className="bg-gradient-to-br from-slate-900/80 to-slate-800/80 backdrop-blur-xl rounded-xl border border-slate-700/50 p-6">
          <div className="flex items-center gap-3 mb-6">
            <div className="p-2 rounded-lg bg-cyan-500/20">
              <Film size={20} className="text-cyan-300" />
            </div>
            <h3 className="text-slate-100">Media Player</h3>
          </div>
          <div className="space-y-3">
            <button
              onClick={() => (window as any).chrome?.webview?.postMessage(JSON.stringify({ type: 'servers.openLocalFile' }))}
              className="w-full flex items-center gap-3 px-4 py-3 rounded-lg bg-slate-950/50 border border-slate-700/30 hover:border-cyan-500/50 text-slate-200 text-sm transition-all group"
            >
              <FolderOpen size={16} className="text-cyan-400 group-hover:text-cyan-300 shrink-0" />
              <div className="text-left">
                <div className="font-medium">Open Local File</div>
                <div className="text-xs text-slate-400">Play a video or music file from your PC</div>
              </div>
            </button>
            <button
              onClick={() => (window as any).chrome?.webview?.postMessage(JSON.stringify({ type: 'servers.openDefaultApps' }))}
              className="w-full flex items-center gap-3 px-4 py-3 rounded-lg bg-slate-950/50 border border-slate-700/30 hover:border-purple-500/50 text-slate-200 text-sm transition-all group"
            >
              <Film size={16} className="text-purple-400 group-hover:text-purple-300 shrink-0" />
              <div className="text-left">
                <div className="font-medium">Set as Default Player</div>
                <div className="text-xs text-slate-400">Open Windows Default Apps to register Atlas for video &amp; audio files</div>
              </div>
            </button>
          </div>
        </div>

      <div className="h-8" />
    </div>
  );
}
