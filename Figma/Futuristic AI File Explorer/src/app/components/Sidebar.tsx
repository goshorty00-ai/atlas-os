import {
  Home,
  Monitor,
  Download,
  FileText,
  Image,
  Video,
  Music,
  FolderCode,
  Cloud,
  Network,
  Lock,
  ArrowUpDown,
} from 'lucide-react';
import { ExplorerRoot } from '../App';

export type SidebarTool = 'transfers' | 'cloud-sync' | 'network' | 'secure-vault';

interface SidebarProps {
  roots: ExplorerRoot[];
  currentRootId: string;
  activeTool: SidebarTool | null;
  onOpenRoot: (root: ExplorerRoot) => void;
  onOpenTool: (tool: SidebarTool) => void;
  activeTransferCount: number;
}

const toolItems: Array<{ id: SidebarTool; icon: typeof Cloud; label: string; caption: string }> = [
  { id: 'transfers', icon: ArrowUpDown, label: 'Transfers', caption: 'Queue' },
  { id: 'cloud-sync', icon: Cloud, label: 'Cloud Sync', caption: 'Local cloud folders' },
  { id: 'network', icon: Network, label: 'Network', caption: 'Windows network tools' },
  { id: 'secure-vault', icon: Lock, label: 'Secure Vault', caption: 'Secrets tools' },
];

export function Sidebar({ roots, currentRootId, activeTool, onOpenRoot, onOpenTool, activeTransferCount }: SidebarProps) {
  return (
    <aside className="w-64 border-r border-white/5 bg-black/20 backdrop-blur-xl p-4 flex flex-col gap-2 overflow-y-auto [&::-webkit-scrollbar]:w-1 [&::-webkit-scrollbar-track]:bg-transparent [&::-webkit-scrollbar-thumb]:bg-white/10 [&::-webkit-scrollbar-thumb]:rounded-full">
      <div className="text-xs text-white/45 uppercase tracking-wider px-2 pb-1">Local Filesystem</div>

      {roots.map((root) => {
        const Icon = rootIcon(root.id);
        const isActive = root.id === currentRootId;
        return (
          <button
            key={`${root.id}-${root.path}`}
            onClick={() => onOpenRoot(root)}
            className={`
              group relative flex items-center gap-3 px-4 py-2.5 rounded-lg transition-all border
              ${isActive
                ? 'bg-gradient-to-r from-cyan-500/20 to-blue-500/20 border-cyan-400/30 shadow-lg shadow-cyan-500/10'
                : 'border-transparent hover:bg-white/5 hover:border-white/10'
              }
            `}
          >
            <Icon size={18} className={isActive ? 'text-cyan-400' : 'text-white/60 group-hover:text-white/90'} />
            <span className={isActive ? 'text-sm text-white font-medium' : 'text-sm text-white/70 group-hover:text-white/90'}>
              {root.label}
            </span>
            {root.group === 'drive' && <span className="ml-auto text-[10px] text-white/45">Drive</span>}
            {isActive && <div className="absolute left-0 top-1/2 -translate-y-1/2 w-1 h-8 bg-gradient-to-b from-cyan-400 to-blue-500 rounded-r-full" />}
          </button>
        );
      })}

      <div className="h-px bg-white/5 my-2" />

      <div className="text-xs text-white/45 uppercase tracking-wider px-2 pb-1">Tools</div>
      {toolItems.map((item) => {
        const Icon = item.icon;
        const isActive = activeTool === item.id;
        return (
          <button
            key={item.label}
            onClick={() => onOpenTool(item.id)}
            className={`group relative flex items-center gap-3 px-4 py-2.5 rounded-lg transition-all border ${isActive
              ? 'bg-gradient-to-r from-cyan-500/20 to-blue-500/20 border-cyan-400/30 shadow-lg shadow-cyan-500/10'
              : 'border-white/10 bg-white/[0.03] hover:bg-white/8 hover:border-white/20'}`}
          >
            <Icon size={18} className={isActive ? 'text-cyan-300' : 'text-white/70'} />
            <div className="min-w-0 text-left">
              <div className={isActive ? 'text-sm text-white font-medium' : 'text-sm text-white/85'}>{item.label}</div>
              <div className="text-[10px] text-white/45">{item.caption}</div>
            </div>
            {item.id === 'transfers' && activeTransferCount > 0 && (
              <span className="ml-auto h-5 min-w-5 px-1.5 rounded-full border border-cyan-400/25 bg-cyan-500/15 text-[10px] text-cyan-200 inline-flex items-center justify-center">
                {activeTransferCount}
              </span>
            )}
            {isActive && <div className="absolute left-0 top-1/2 -translate-y-1/2 w-1 h-8 bg-gradient-to-b from-cyan-400 to-blue-500 rounded-r-full" />}
          </button>
        );
      })}
    </aside>
  );
}

function rootIcon(rootId: string) {
  const id = rootId.toLowerCase();
  if (id === 'home') return Home;
  if (id === 'desktop') return Monitor;
  if (id === 'downloads') return Download;
  if (id === 'documents') return FileText;
  if (id === 'pictures') return Image;
  if (id === 'videos') return Video;
  if (id === 'music') return Music;
  if (id.startsWith('drive-')) return FolderCode;
  return Home;
}
