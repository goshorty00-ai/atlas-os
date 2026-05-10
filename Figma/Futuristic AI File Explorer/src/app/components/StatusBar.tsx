import { HardDrive, CheckCircle, ArrowUpDown, AlertTriangle } from 'lucide-react';

type ClipboardStatus = {
  path: string;
  name: string;
  kind: 'file' | 'folder';
  mode: 'copy' | 'cut';
} | null;

interface StatusBarProps {
  currentPath: string;
  folderCount: number;
  fileCount: number;
  clipboardBuffer: ClipboardStatus;
  activeTransferCount: number;
  completedTransferCount: number;
  failedTransferCount: number;
  onOpenTransfers: () => void;
}

export function StatusBar({ currentPath, folderCount, fileCount, clipboardBuffer, activeTransferCount, completedTransferCount, failedTransferCount, onOpenTransfers }: StatusBarProps) {
  const chips = [
    'Local browsing',
    'Safe actions',
    'Metadata only',
    clipboardBuffer ? `Clipboard: ${clipboardBuffer.mode}` : 'Clipboard: empty',
    'Current folder only',
  ];

  return (
    <footer className="h-12 border-t border-white/5 bg-black/60 backdrop-blur-xl px-6 flex items-center justify-between text-xs">
      <div className="flex items-center gap-6">
        <div className="flex items-center gap-2">
          <HardDrive size={14} className="text-cyan-400" />
          <div className="flex items-center gap-2 text-white/70">
            <span>{folderCount} folders</span>
            <span className="text-white/40">•</span>
            <span className="text-orange-300">{fileCount} files</span>
            <span className="text-white/40">•</span>
            <span className="text-green-400">Local filesystem bridge active</span>
          </div>
        </div>

        <div className="max-w-[400px] truncate text-white/45">{currentPath || 'No folder selected'}</div>
      </div>

      <div className="flex items-center gap-2">
        <button
          onClick={onOpenTransfers}
          className="h-8 px-3 rounded-full border border-cyan-400/25 bg-cyan-500/10 text-[10px] text-cyan-100 inline-flex items-center gap-2 hover:bg-cyan-500/20 transition-colors"
        >
          <ArrowUpDown size={13} className="text-cyan-300" />
          <span>{activeTransferCount === 0 ? '0 transfers' : activeTransferCount === 1 ? '1 active transfer' : `${activeTransferCount} active transfers`}</span>
          <span className="text-white/40">|</span>
          <span>{completedTransferCount} completed</span>
          {failedTransferCount > 0 && (
            <span className="inline-flex items-center gap-1 text-amber-200">
              <AlertTriangle size={11} />
              {failedTransferCount} failed
            </span>
          )}
        </button>

        {chips.map((chip) => (
          <span key={chip} className="h-6 px-2.5 rounded-full border border-white/12 bg-white/5 text-[10px] text-white/65 inline-flex items-center">
            {chip}
          </span>
        ))}

        <div className="flex items-center gap-2 px-3 py-1 bg-green-500/10 border border-green-400/20 rounded-full shrink-0">
          <CheckCircle size={14} className="text-green-400" />
          <span className="text-green-400">Safe preview rules enabled</span>
        </div>
      </div>
    </footer>
  );
}
