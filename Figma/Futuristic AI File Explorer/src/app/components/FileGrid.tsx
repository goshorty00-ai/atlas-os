import { useEffect, useRef, useState } from 'react';
import { ArrowUp, Folder, FolderOpen, FileText, FileCode, Image, FileArchive, Files, Eye, ExternalLink, Edit3, Copy, Clipboard, Trash2, Plus, RefreshCw, Scissors, Ban, PackageOpen } from 'lucide-react';
import { FileItem } from '../App';

const EXTRACTABLE_EXTENSIONS = new Set(['.zip', '.rar', '.7z']);

type CopyBuffer = {
  path: string;
  name: string;
  kind: 'file' | 'folder';
  mode: 'copy' | 'cut';
};

interface FileGridProps {
  items: FileItem[];
  totalItems: number;
  searchQuery: string;
  currentPath: string;
  parentPath: string | null;
  isLoading: boolean;
  error: string;
  selectedFile: FileItem | null;
  onOpenParent: () => void;
  onOpenFolder: (path: string) => void;
  onOpenFile: (path: string) => void;
  onPreviewFile: (path: string) => void;
  onShowInExplorer: (path: string) => void;
  onRequestRename: (item: FileItem) => void;
  onCopyToClipboard: (item: FileItem) => void;
  onCutToClipboard: (item: FileItem) => void;
  onPasteHere: (destinationPath: string) => void;
  onRequestDelete: (item: FileItem) => void;
  onExtractArchive: (archivePath: string, mode: 'new-folder' | 'here') => void;
  extractLoading: boolean;
  clipboardBuffer: CopyBuffer | null;
  focusPath: string;
  loadingPath: string;
  onRefresh: () => void;
  onContextCreate: (kind: 'file' | 'folder') => void;
  setSelectedFile: (file: FileItem | null) => void;
}

export function FileGrid({
  items,
  totalItems,
  searchQuery,
  currentPath,
  parentPath,
  isLoading,
  error,
  selectedFile,
  onOpenParent,
  onOpenFolder,
  onOpenFile,
  onPreviewFile,
  onShowInExplorer,
  onRequestRename,
  onCopyToClipboard,
  onCutToClipboard,
  onPasteHere,
  onRequestDelete,
  onExtractArchive,
  extractLoading,
  clipboardBuffer,
  focusPath,
  loadingPath,
  onRefresh,
  onContextCreate,
  setSelectedFile,
}: FileGridProps) {
  const isSearchActive = searchQuery.trim().length > 0;
  const [contextMenu, setContextMenu] = useState<{
    mode: 'row' | 'empty';
    x: number;
    y: number;
    item?: FileItem;
  } | null>(null);
  const menuRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    if (!focusPath) return;
    const escapedPath = typeof CSS !== 'undefined' && typeof CSS.escape === 'function'
      ? CSS.escape(focusPath)
      : focusPath.replace(/"/g, '\\"');
    const row = document.querySelector(`[data-file-path="${escapedPath}"]`) as HTMLElement | null;
    if (!row) return;
    row.scrollIntoView({ behavior: 'smooth', block: 'center' });
  }, [focusPath]);

  useEffect(() => {
    if (!contextMenu) return;

    const onMouseDown = (event: MouseEvent) => {
      if (!menuRef.current) return;
      if (menuRef.current.contains(event.target as Node)) return;
      setContextMenu(null);
    };

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setContextMenu(null);
      }
    };

    document.addEventListener('mousedown', onMouseDown);
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('mousedown', onMouseDown);
      document.removeEventListener('keydown', onKeyDown);
    };
  }, [contextMenu]);

  const openContextMenu = (x: number, y: number, mode: 'row' | 'empty', item?: FileItem) => {
    const menuWidth = 260;
    const menuHeight = mode === 'row' ? 310 : 190;
    const clampedX = Math.max(8, Math.min(x, window.innerWidth - menuWidth - 8));
    const clampedY = Math.max(8, Math.min(y, window.innerHeight - menuHeight - 8));
    setContextMenu({ mode, x: clampedX, y: clampedY, item });
  };

  const handleContextAction = (action: () => void) => {
    action();
    setContextMenu(null);
  };

  return (
    <div className="h-full min-h-0 flex flex-col gap-2">
      <div className="flex items-center gap-2">
        <button
          onClick={onOpenParent}
          disabled={!parentPath || isLoading}
          className="h-8 px-3 rounded-lg border border-white/10 bg-white/5 text-xs text-white/75 disabled:opacity-40 disabled:cursor-not-allowed hover:bg-white/10"
        >
          <span className="inline-flex items-center gap-1.5"><ArrowUp size={13} /> Up</span>
        </button>
        {isSearchActive && (
          <div className="h-8 px-3 rounded-lg border border-cyan-400/25 bg-cyan-500/10 text-xs text-cyan-200 inline-flex items-center">
            Showing {items.length} of {totalItems} items
          </div>
        )}
        {selectedFile && (
          <div className="ml-auto flex items-center gap-1.5">
            {selectedFile.kind === 'file' && (
              <button
                onClick={() => onOpenFile(selectedFile.path)}
                className="h-7 px-2.5 rounded-md bg-cyan-500/15 border border-cyan-400/30 text-xs text-cyan-300 hover:bg-cyan-500/25 transition-colors"
              >
                Open
              </button>
            )}
            {selectedFile.kind === 'folder' && (
              <button
                onClick={() => onOpenFolder(selectedFile.path)}
                className="h-7 px-2.5 rounded-md bg-cyan-500/15 border border-cyan-400/30 text-xs text-cyan-300 hover:bg-cyan-500/25 transition-colors"
              >
                Open Folder
              </button>
            )}
            <button
              onClick={() => onShowInExplorer(selectedFile.path)}
              className="h-7 px-2.5 rounded-md bg-white/5 border border-white/10 text-xs text-white/60 hover:bg-white/10 transition-colors"
            >
              Show in Explorer
            </button>
            <button
              onClick={() => setSelectedFile(null)}
              className="h-7 px-2 rounded-md bg-white/5 border border-white/10 text-xs text-white/40 hover:bg-white/10 transition-colors"
            >
              ✕
            </button>
          </div>
        )}
      </div>

      {error && (
        <div className="flex items-center gap-3 p-3 bg-red-500/10 border border-red-400/30 rounded-xl">
          <Ban className="text-red-300 shrink-0" size={18} />
          <div>
            <div className="text-sm font-medium text-red-200">Directory Error</div>
            <div className="text-xs text-white/70">{error}</div>
          </div>
        </div>
      )}

      <div
        className="relative flex-1 min-h-0 rounded-xl border border-white/10 bg-white/[0.03] overflow-y-auto overflow-x-hidden [&::-webkit-scrollbar]:w-1 [&::-webkit-scrollbar-track]:bg-transparent [&::-webkit-scrollbar-thumb]:bg-white/10 [&::-webkit-scrollbar-thumb]:rounded-full"
        onContextMenu={(event) => {
          if (event.target !== event.currentTarget) return;
          event.preventDefault();
          openContextMenu(event.clientX, event.clientY, 'empty');
        }}
      >
        {/* Sticky column header */}
        <div className="sticky top-0 z-10 grid grid-cols-[minmax(0,2.4fr)_minmax(0,0.7fr)_minmax(0,1.1fr)_minmax(0,0.7fr)] gap-3 px-4 py-2 bg-[#111111] border-b border-white/10 text-[10px] uppercase tracking-widest text-white/35 font-semibold">
          <div>Name</div>
          <div>Type</div>
          <div>Modified</div>
          <div className="text-right">Size</div>
        </div>

        {isLoading && (
          <div className="absolute inset-x-3 top-12 z-20 px-4 py-3 text-sm text-white/80 text-center space-y-1 rounded-lg border border-cyan-400/25 bg-[#0a1118]/90 backdrop-blur-sm">
            <div>Loading folder...</div>
            {loadingPath && <div className="text-xs text-white/35 break-all">{loadingPath}</div>}
          </div>
        )}

        {!isLoading && !error && items.length === 0 && !isSearchActive && (
          <div className="flex flex-col items-center justify-center gap-3 py-14 text-center">
            <div className="p-4 rounded-2xl bg-white/[0.04] border border-white/10">
              <Files size={32} className="text-white/25" />
            </div>
            <div>
              <div className="text-sm font-medium text-white/60">No files here</div>
              <div className="text-xs text-white/35 mt-1">Choose another folder from the sidebar</div>
            </div>
          </div>
        )}

        {!isLoading && !error && items.length === 0 && isSearchActive && (
          <div className="flex flex-col items-center justify-center gap-3 py-14 text-center">
            <div className="p-4 rounded-2xl bg-cyan-500/[0.06] border border-cyan-400/20">
              <Files size={32} className="text-cyan-200/60" />
            </div>
            <div>
              <div className="text-sm font-medium text-white/75">No matches in this folder</div>
              <div className="text-xs text-white/45 mt-1">Try another name or clear search.</div>
            </div>
          </div>
        )}

        <div className={isLoading ? 'opacity-55 transition-opacity' : 'transition-opacity'}>
          {items.map((file) => {
          const isSelected = selectedFile?.path === file.path;
          const iconEl = getFileIconEl(file);
          return (
            <button
              key={file.path}
              data-file-path={file.path}
              onClick={() => setSelectedFile(isSelected ? null : file)}
              onContextMenu={(event) => {
                event.preventDefault();
                event.stopPropagation();
                setSelectedFile(file);
                openContextMenu(event.clientX, event.clientY, 'row', file);
              }}
              onDoubleClick={() => {
                if (file.kind === 'folder') onOpenFolder(file.path);
                else onOpenFile(file.path);
              }}
              title={`${file.name}\n${file.path}`}
              className={`
                w-full grid grid-cols-[minmax(0,2.4fr)_minmax(0,0.7fr)_minmax(0,1.1fr)_minmax(0,0.7fr)] gap-3 px-4 py-1.5 border-t border-white/5 text-left transition-colors
                ${isSelected
                  ? 'bg-cyan-500/[0.08] border-l-2 border-l-cyan-400/60'
                  : file.kind === 'folder'
                    ? 'hover:bg-cyan-500/[0.04]'
                    : 'hover:bg-white/[0.04]'
                }
              `}
            >
              <div className="flex items-center gap-2.5 min-w-0">
                {iconEl}
                <span className={`truncate text-sm ${file.kind === 'folder' ? 'text-white/90 font-medium' : 'text-white/80'}`}>{file.name}</span>
                {file.isHidden && <span className="shrink-0 text-[9px] px-1 py-0.5 rounded bg-white/10 text-white/40">Hidden</span>}
              </div>
              <div className="text-xs text-white/45 truncate self-center">{file.kind === 'folder' ? 'Folder' : (file.extension || '—')}</div>
              <div className="text-xs text-white/45 truncate self-center">{formatDate(file.modifiedUtc)}</div>
              <div className="text-xs text-white/45 text-right self-center">{file.kind === 'folder' ? '—' : formatBytes(file.sizeBytes)}</div>
            </button>
          );
          })}
        </div>
      </div>

      {contextMenu && (
        <div
          ref={menuRef}
          className="fixed z-[10040] w-[260px] rounded-xl border border-cyan-400/20 bg-[#0c1116]/95 backdrop-blur-lg shadow-2xl shadow-black/70 ring-1 ring-white/10 p-1.5"
          style={{ left: `${contextMenu.x}px`, top: `${contextMenu.y}px` }}
        >
          {contextMenu.mode === 'row' && contextMenu.item && (
            <div className="space-y-1">
              {contextMenu.item.kind === 'file' ? (
                <>
                  <ContextMenuButton icon={FolderOpen} label="Open" onClick={() => handleContextAction(() => onOpenFile(contextMenu.item!.path))} />
                  <ContextMenuButton icon={Eye} label="Preview" onClick={() => handleContextAction(() => onPreviewFile(contextMenu.item!.path))} />
                  <ContextMenuButton icon={ExternalLink} label="Show in Explorer" onClick={() => handleContextAction(() => onShowInExplorer(contextMenu.item!.path))} />
                  <ContextMenuButton icon={Edit3} label="Rename" onClick={() => handleContextAction(() => onRequestRename(contextMenu.item!))} />
                  <ContextMenuButton icon={Copy} label="Copy to clipboard" onClick={() => handleContextAction(() => onCopyToClipboard(contextMenu.item!))} />
                  <ContextMenuButton icon={Scissors} label="Cut" onClick={() => handleContextAction(() => onCutToClipboard(contextMenu.item!))} />
                  <ContextMenuButton
                    icon={Clipboard}
                    label={clipboardBuffer ? (clipboardBuffer.mode === 'cut' ? 'Move here' : 'Paste here') : 'Paste here - copy something first'}
                    disabled={!clipboardBuffer}
                    onClick={() => handleContextAction(() => onPasteHere(currentPath))}
                  />
                  {EXTRACTABLE_EXTENSIONS.has(contextMenu.item.extension.toLowerCase()) && (
                    <>
                      <div className="h-px bg-white/10 my-1" />
                      <ContextMenuButton
                        icon={PackageOpen}
                        label={extractLoading ? 'Extracting...' : 'Extract to new folder'}
                        disabled={extractLoading}
                        onClick={() => handleContextAction(() => onExtractArchive(contextMenu.item!.path, 'new-folder'))}
                      />
                      <ContextMenuButton
                        icon={PackageOpen}
                        label={extractLoading ? 'Extracting...' : 'Extract here'}
                        disabled={extractLoading}
                        onClick={() => handleContextAction(() => onExtractArchive(contextMenu.item!.path, 'here'))}
                      />
                    </>
                  )}
                </>
              ) : (
                <>
                  <ContextMenuButton icon={FolderOpen} label="Open folder" onClick={() => handleContextAction(() => onOpenFolder(contextMenu.item!.path))} />
                  <ContextMenuButton icon={ExternalLink} label="Show in Explorer" onClick={() => handleContextAction(() => onShowInExplorer(contextMenu.item!.path))} />
                  <ContextMenuButton icon={Edit3} label="Rename" onClick={() => handleContextAction(() => onRequestRename(contextMenu.item!))} />
                  <ContextMenuButton icon={Copy} label="Copy to clipboard" onClick={() => handleContextAction(() => onCopyToClipboard(contextMenu.item!))} />
                  <ContextMenuButton icon={Scissors} label="Cut" onClick={() => handleContextAction(() => onCutToClipboard(contextMenu.item!))} />
                  <ContextMenuButton
                    icon={Clipboard}
                    label={clipboardBuffer ? (clipboardBuffer.mode === 'cut' ? 'Move here' : 'Paste here') : 'Paste here - copy something first'}
                    disabled={!clipboardBuffer}
                    onClick={() => handleContextAction(() => onPasteHere(contextMenu.item!.path))}
                  />
                </>
              )}

              <div className="h-px bg-white/10 my-1" />
              <ContextMenuButton icon={Trash2} label="Delete (Recycle Bin)" onClick={() => handleContextAction(() => onRequestDelete(contextMenu.item!))} />
            </div>
          )}

          {contextMenu.mode === 'empty' && (
            <div className="space-y-1">
              <ContextMenuButton icon={Plus} label="New Text File" onClick={() => handleContextAction(() => onContextCreate('file'))} />
              <ContextMenuButton icon={Folder} label="New Folder" onClick={() => handleContextAction(() => onContextCreate('folder'))} />
              <ContextMenuButton
                icon={Clipboard}
                label={clipboardBuffer ? (clipboardBuffer.mode === 'cut' ? 'Move here' : 'Paste here') : 'Paste here - copy something first'}
                disabled={!clipboardBuffer}
                onClick={() => handleContextAction(() => onPasteHere(currentPath))}
              />
              <div className="h-px bg-white/10 my-1" />
              <ContextMenuButton icon={RefreshCw} label="Refresh" onClick={() => handleContextAction(onRefresh)} />
            </div>
          )}
        </div>
      )}
    </div>
  );
}

function ContextMenuButton({
  icon: Icon,
  label,
  disabled,
  onClick,
}: {
  icon: any;
  label: string;
  disabled?: boolean;
  onClick?: () => void;
}) {
  return (
    <button
      onClick={onClick}
      disabled={disabled}
      className="w-full h-8 px-2.5 rounded-lg text-left inline-flex items-center gap-2 text-xs border border-transparent hover:border-cyan-400/25 hover:bg-cyan-500/12 text-white/85 disabled:text-white/35 disabled:hover:border-transparent disabled:hover:bg-transparent disabled:cursor-not-allowed"
    >
      <Icon size={14} className={disabled ? 'text-white/35' : 'text-cyan-300'} />
      <span>{label}</span>
    </button>
  );
}

function getFileIconEl(file: FileItem): JSX.Element {
  const imgCls = 'shrink-0 w-[15px] h-[15px] object-contain';

  if (file.kind === 'folder') {
    return <img src="/icons/file_explorer_folder.png" width={15} height={15} className={imgCls} alt="" />;
  }

  const ext = file.extension.toLowerCase();

  if (['.mp4', '.mkv', '.avi', '.mov', '.webm', '.wmv', '.flv', '.m4v'].includes(ext)) {
    return <img src="/icons/file_explorer_movies.png" width={15} height={15} className={imgCls} alt="" />;
  }
  if (['.mp3', '.wav', '.flac', '.aac', '.ogg', '.m4a', '.wma', '.opus'].includes(ext)) {
    return <img src="/icons/file_explorer_music.png" width={15} height={15} className={imgCls} alt="" />;
  }
  if (['.txt', '.md', '.log', '.nfo'].includes(ext)) {
    return <img src="/icons/file_explorer_txt.png" width={15} height={15} className={imgCls} alt="" />;
  }

  // Lucide fallbacks for types not yet covered by custom icons
  if (['.png', '.jpg', '.jpeg', '.webp', '.gif', '.svg', '.bmp'].includes(ext)) return <Image size={15} className="shrink-0 text-white/50" />;
  if (['.ts', '.tsx', '.js', '.jsx', '.cs', '.xaml', '.html', '.css', '.json', '.xml'].includes(ext)) return <FileCode size={15} className="shrink-0 text-white/50" />;
  if (['.zip', '.rar', '.7z', '.tar', '.gz'].includes(ext)) return <FileArchive size={15} className="shrink-0 text-white/50" />;
  return <FileText size={15} className="shrink-0 text-white/50" />;
}

function formatBytes(sizeBytes: number | null) {
  if (sizeBytes === null || sizeBytes < 0) return '-';
  if (sizeBytes < 1024) return `${sizeBytes} B`;
  if (sizeBytes < 1024 * 1024) return `${(sizeBytes / 1024).toFixed(1)} KB`;
  if (sizeBytes < 1024 * 1024 * 1024) return `${(sizeBytes / (1024 * 1024)).toFixed(1)} MB`;
  return `${(sizeBytes / (1024 * 1024 * 1024)).toFixed(2)} GB`;
}

function formatDate(value: string) {
  if (!value) return '-';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  // Show as local-friendly short date
  return date.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
}
