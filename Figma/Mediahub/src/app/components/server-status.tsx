import { Server, HardDrive, AlertTriangle, CheckCircle, RefreshCw, Sparkles } from 'lucide-react';

interface ServerData {
  id: string;
  name: string;
  type: string;
  status: 'online' | 'offline' | 'scanning';
  libraries: number;
  storage: {
    used: number;
    total: number;
  };
  issues: {
    missingArtwork: number;
    brokenLinks: number;
  };
}

const servers: ServerData[] = [
  {
    id: '1',
    name: 'Main Library',
    type: 'Plex',
    status: 'online',
    libraries: 4,
    storage: { used: 8.2, total: 12 },
    issues: { missingArtwork: 12, brokenLinks: 3 },
  },
  {
    id: '2',
    name: '4K Collection',
    type: 'Jellyfin',
    status: 'scanning',
    libraries: 2,
    storage: { used: 15.8, total: 20 },
    issues: { missingArtwork: 5, brokenLinks: 0 },
  },
  {
    id: '3',
    name: 'Local Files',
    type: 'Local',
    status: 'online',
    libraries: 6,
    storage: { used: 2.3, total: 5 },
    issues: { missingArtwork: 28, brokenLinks: 7 },
  },
];

export function ServerStatus() {
  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h3 className="text-slate-200">Media Servers</h3>
        <button className="flex items-center gap-1 px-3 py-1.5 rounded-lg bg-gradient-to-r from-cyan-500 to-purple-500 text-white text-sm hover:shadow-lg hover:shadow-cyan-500/30 transition-all">
          <Sparkles size={14} />
          <span>AI Cleanup</span>
        </button>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        {servers.map((server) => (
          <div
            key={server.id}
            className="relative bg-gradient-to-br from-slate-900/80 to-slate-800/80 backdrop-blur-xl rounded-xl border border-slate-700/50 p-5 space-y-4 hover:border-cyan-500/30 transition-all group"
            style={{
              backdropFilter: 'blur(20px)',
            }}
          >
            {/* Header */}
            <div className="flex items-start justify-between">
              <div className="flex items-center gap-3">
                <div className={`p-2 rounded-lg ${
                  server.status === 'online'
                    ? 'bg-green-500/20'
                    : server.status === 'scanning'
                    ? 'bg-cyan-500/20'
                    : 'bg-red-500/20'
                }`}>
                  <Server size={18} className={
                    server.status === 'online'
                      ? 'text-green-400'
                      : server.status === 'scanning'
                      ? 'text-cyan-400'
                      : 'text-red-400'
                  } />
                </div>
                <div>
                  <h4 className="text-slate-100">{server.name}</h4>
                  <p className="text-xs text-slate-400">{server.type}</p>
                </div>
              </div>
              <div className={`flex items-center gap-1 px-2 py-1 rounded-lg text-xs ${
                server.status === 'online'
                  ? 'bg-green-500/20 text-green-300'
                  : server.status === 'scanning'
                  ? 'bg-cyan-500/20 text-cyan-300'
                  : 'bg-red-500/20 text-red-300'
              }`}>
                <div className={`w-1.5 h-1.5 rounded-full ${
                  server.status === 'online'
                    ? 'bg-green-400 animate-pulse'
                    : server.status === 'scanning'
                    ? 'bg-cyan-400 animate-pulse'
                    : 'bg-red-400'
                }`} />
                <span className="capitalize">{server.status}</span>
              </div>
            </div>

            {/* Stats */}
            <div className="space-y-3">
              {/* Storage */}
              <div>
                <div className="flex items-center justify-between text-xs mb-1.5">
                  <span className="text-slate-400 flex items-center gap-1">
                    <HardDrive size={12} />
                    Storage
                  </span>
                  <span className="text-slate-300">
                    {server.storage.used}TB / {server.storage.total}TB
                  </span>
                </div>
                <div className="h-1.5 bg-slate-950/50 rounded-full overflow-hidden">
                  <div
                    className="h-full bg-gradient-to-r from-cyan-500 to-purple-500"
                    style={{
                      width: `${(server.storage.used / server.storage.total) * 100}%`,
                    }}
                  />
                </div>
              </div>

              {/* Libraries */}
              <div className="flex items-center justify-between text-sm">
                <span className="text-slate-400">Libraries</span>
                <span className="text-slate-200">{server.libraries}</span>
              </div>

              {/* Issues */}
              <div className="space-y-2">
                {server.issues.missingArtwork > 0 && (
                  <div className="flex items-center justify-between text-xs">
                    <span className="text-amber-400 flex items-center gap-1">
                      <AlertTriangle size={12} />
                      Missing Artwork
                    </span>
                    <span className="text-amber-300">{server.issues.missingArtwork}</span>
                  </div>
                )}
                {server.issues.brokenLinks > 0 && (
                  <div className="flex items-center justify-between text-xs">
                    <span className="text-red-400 flex items-center gap-1">
                      <AlertTriangle size={12} />
                      Broken Links
                    </span>
                    <span className="text-red-300">{server.issues.brokenLinks}</span>
                  </div>
                )}
                {server.issues.missingArtwork === 0 && server.issues.brokenLinks === 0 && (
                  <div className="flex items-center gap-1 text-xs text-green-400">
                    <CheckCircle size={12} />
                    <span>All healthy</span>
                  </div>
                )}
              </div>
            </div>

            {/* Actions */}
            <div className="flex gap-2 pt-2 border-t border-slate-700/50">
              <button className="flex-1 flex items-center justify-center gap-1 px-3 py-2 rounded-lg bg-slate-800/50 text-slate-300 hover:text-white hover:bg-slate-700 transition-all text-sm">
                <RefreshCw size={14} />
                <span>Rescan</span>
              </button>
              <button className="flex-1 px-3 py-2 rounded-lg bg-slate-800/50 text-slate-300 hover:text-white hover:bg-slate-700 transition-all text-sm">
                Details
              </button>
            </div>

            {/* Scanning indicator */}
            {server.status === 'scanning' && (
              <div className="absolute bottom-0 left-0 right-0 h-0.5 bg-slate-900 rounded-b-xl overflow-hidden">
                <div
                  className="h-full bg-gradient-to-r from-cyan-500 to-purple-500 animate-pulse"
                  style={{
                    width: '60%',
                    animation: 'scanning 2s ease-in-out infinite',
                  }}
                />
              </div>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}
