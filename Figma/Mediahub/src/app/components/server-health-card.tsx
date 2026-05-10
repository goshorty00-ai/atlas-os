import { Server, HardDrive, Film, Tv, Music, Gamepad2, AlertTriangle, Image, FileQuestion, Copy, Sparkles, RefreshCw, ExternalLink } from 'lucide-react';

export interface ServerHealth {
  name: string;
  status: 'online' | 'offline' | 'scanning';
  storageUsed: string;
  moviesCount: number;
  tvCount: number;
  musicCount?: number;
  gamesCount?: number;
  lastScan: string;
  scanProgress?: number;
  missingMetadata: number;
  missingArtwork: number;
  brokenLinks: number;
  duplicates: number;
}

interface ServerHealthCardProps {
  server: ServerHealth;
  onRescan: () => void;
  onAIFix: () => void;
  onOpenLibrary: () => void;
}

export function ServerHealthCard({ server, onRescan, onAIFix, onOpenLibrary }: ServerHealthCardProps) {
  const getStatusColor = () => {
    switch (server.status) {
      case 'online':
        return 'from-green-500/20 to-emerald-500/20 border-green-500/30';
      case 'offline':
        return 'from-red-500/20 to-rose-500/20 border-red-500/30';
      case 'scanning':
        return 'from-cyan-500/20 to-blue-500/20 border-cyan-500/30';
      default:
        return 'from-slate-800/80 to-slate-900/80 border-slate-700/30';
    }
  };

  const getStatusText = () => {
    switch (server.status) {
      case 'online':
        return { text: 'Online', color: 'text-green-300' };
      case 'offline':
        return { text: 'Offline', color: 'text-red-300' };
      case 'scanning':
        return { text: 'Scanning...', color: 'text-cyan-300' };
      default:
        return { text: 'Unknown', color: 'text-slate-400' };
    }
  };

  const statusInfo = getStatusText();

  return (
    <div className={`bg-gradient-to-br ${getStatusColor()} backdrop-blur-xl rounded-xl border p-6 space-y-4`}>
      {/* Header */}
      <div className="flex items-start justify-between">
        <div className="flex items-center gap-3">
          <div className={`p-2 rounded-lg ${
            server.status === 'online' ? 'bg-green-500/20' :
            server.status === 'offline' ? 'bg-red-500/20' :
            'bg-cyan-500/20'
          }`}>
            <Server size={20} className={statusInfo.color} />
          </div>
          <div>
            <h3 className="text-slate-100">{server.name}</h3>
            <p className={`text-sm ${statusInfo.color}`}>{statusInfo.text}</p>
          </div>
        </div>
        <div className={`w-3 h-3 rounded-full ${
          server.status === 'online' ? 'bg-green-500' :
          server.status === 'offline' ? 'bg-red-500' :
          'bg-cyan-500 animate-pulse'
        }`} />
      </div>

      {/* Storage */}
      <div className="flex items-center gap-2 text-slate-300">
        <HardDrive size={16} />
        <span className="text-sm">{server.storageUsed} used</span>
      </div>

      {/* Library Counts */}
      <div className="grid grid-cols-2 gap-3">
        <div className="flex items-center gap-2 text-slate-300">
          <Film size={14} />
          <span className="text-sm">{server.moviesCount} Movies</span>
        </div>
        <div className="flex items-center gap-2 text-slate-300">
          <Tv size={14} />
          <span className="text-sm">{server.tvCount} TV Shows</span>
        </div>
        {server.musicCount !== undefined && (
          <div className="flex items-center gap-2 text-slate-300">
            <Music size={14} />
            <span className="text-sm">{server.musicCount} Songs</span>
          </div>
        )}
        {server.gamesCount !== undefined && (
          <div className="flex items-center gap-2 text-slate-300">
            <Gamepad2 size={14} />
            <span className="text-sm">{server.gamesCount} Games</span>
          </div>
        )}
      </div>

      {/* Last Scan */}
      <div className="text-xs text-slate-400">
        Last scan: {server.lastScan}
      </div>

      {/* Scan Progress */}
      {server.status === 'scanning' && server.scanProgress !== undefined && (
        <div>
          <div className="flex items-center justify-between text-xs text-slate-400 mb-2">
            <span>Scanning library...</span>
            <span>{server.scanProgress}%</span>
          </div>
          <div className="h-2 bg-slate-950/50 rounded-full overflow-hidden">
            <div
              className="h-full bg-gradient-to-r from-cyan-500 to-blue-500 transition-all duration-300"
              style={{ width: `${server.scanProgress}%` }}
            />
          </div>
        </div>
      )}

      {/* Issues */}
      <div className="grid grid-cols-2 gap-2">
        {server.missingMetadata > 0 && (
          <div className="flex items-center gap-2 px-2 py-1.5 rounded bg-red-500/20 text-red-300 text-xs">
            <FileQuestion size={12} />
            <span>{server.missingMetadata} No Meta</span>
          </div>
        )}
        {server.missingArtwork > 0 && (
          <div className="flex items-center gap-2 px-2 py-1.5 rounded bg-amber-500/20 text-amber-300 text-xs">
            <Image size={12} />
            <span>{server.missingArtwork} No Art</span>
          </div>
        )}
        {server.brokenLinks > 0 && (
          <div className="flex items-center gap-2 px-2 py-1.5 rounded bg-red-500/20 text-red-300 text-xs">
            <AlertTriangle size={12} />
            <span>{server.brokenLinks} Broken</span>
          </div>
        )}
        {server.duplicates > 0 && (
          <div className="flex items-center gap-2 px-2 py-1.5 rounded bg-purple-500/20 text-purple-300 text-xs">
            <Copy size={12} />
            <span>{server.duplicates} Dupes</span>
          </div>
        )}
      </div>

      {/* Actions */}
      <div className="flex gap-2 pt-2">
        <button
          onClick={onRescan}
          disabled={server.status === 'scanning'}
          className="flex-1 flex items-center justify-center gap-2 px-3 py-2 rounded-lg bg-slate-800/50 text-slate-300 text-sm hover:bg-slate-700/50 transition-colors border border-slate-700/30 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          <RefreshCw size={14} className={server.status === 'scanning' ? 'animate-spin' : ''} />
          Rescan
        </button>
        <button
          onClick={onAIFix}
          className="flex-1 flex items-center justify-center gap-2 px-3 py-2 rounded-lg bg-gradient-to-r from-purple-500 to-pink-500 text-white text-sm hover:shadow-lg transition-all"
        >
          <Sparkles size={14} />
          AI Fix
        </button>
        <button
          onClick={onOpenLibrary}
          className="flex items-center justify-center px-3 py-2 rounded-lg bg-cyan-500/20 text-cyan-300 text-sm hover:bg-cyan-500/30 transition-colors border border-cyan-500/30"
        >
          <ExternalLink size={14} />
        </button>
      </div>
    </div>
  );
}
