import { FolderOpen } from 'lucide-react';
import { postToHost } from '../atlasBridge';

export function LogsPanel() {
  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h2 className="text-xl font-light text-gray-300">Logs</h2>
        <div className="text-sm text-gray-500">Atlas runtime diagnostics</div>
      </div>

      <div
        className="rounded-xl border border-blue-500/20 bg-gradient-to-br from-blue-500/5 to-violet-500/5 backdrop-blur-xl p-6"
        style={{ boxShadow: '0 8px 32px 0 rgba(31, 38, 135, 0.15)' }}
      >
        <div className="flex items-center justify-between gap-4">
          <div className="space-y-1">
            <div className="text-gray-200 font-medium">Open Logs Folder</div>
            <div className="text-xs text-gray-500">AppData\\Roaming\\AtlasAI</div>
          </div>
          <button
            className="h-10 px-4 rounded-lg bg-gradient-to-r from-blue-500 to-violet-500 hover:from-blue-400 hover:to-violet-400 text-white font-medium text-sm transition-all shadow-lg shadow-blue-500/30 hover:shadow-blue-500/50 inline-flex items-center gap-2"
            onClick={() => postToHost('api.openLogsFolder')}
          >
            <FolderOpen className="w-4 h-4" />
            Open
          </button>
        </div>
      </div>
    </div>
  );
}
