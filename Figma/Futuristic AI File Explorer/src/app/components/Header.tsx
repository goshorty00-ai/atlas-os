import { useEffect, useMemo, useRef, useState } from 'react';
import { Search, FolderPlus, ChevronRight, HardDrive, X, Plus, FileText, RefreshCw, Sparkles, Mic, Volume2, VolumeX } from 'lucide-react';

export type AtlasBrainAction =
  | 'project-brain'
  | 'smart-collections'
  | 'safe-share-check'
  | 'cleanup-plan'
  | 'recent-changes'
  | 'what-changed'
  | 'action-plan'
  | 'organize-folder';

interface HeaderProps {
  breadcrumbs: Array<{ label: string; path: string }>;
  currentPath: string;
  searchQuery: string;
  createLoading: boolean;
  voiceNote: string;
  speechVisible: boolean;
  speechEnabled: boolean;
  onSearchQueryChange: (value: string) => void;
  onClearSearch: () => void;
  onCreateFile: (directoryPath: string, fileName: string) => void;
  onCreateFolder: (directoryPath: string, folderName: string) => void;
  onOpenPath: (path: string) => void;
  onRefresh: () => void;
  onMicClick: () => void;
  onToggleSpeech: () => void;
  onOpenAtlasCommand: () => void;
  onToggleBrainPanel: (open: boolean) => void;
}

export function Header({
  breadcrumbs,
  currentPath,
  searchQuery,
  createLoading,
  voiceNote,
  speechVisible,
  speechEnabled,
  onSearchQueryChange,
  onClearSearch,
  onCreateFile,
  onCreateFolder,
  onOpenPath,
  onRefresh,
  onMicClick,
  onToggleSpeech,
  onOpenAtlasCommand,
  onToggleBrainPanel,
}: HeaderProps) {
  const [showNewMenu, setShowNewMenu] = useState(false);
  const [createMode, setCreateMode] = useState<'file' | 'folder' | null>(null);
  const [createName, setCreateName] = useState('');
  const [menuPosition, setMenuPosition] = useState({ top: 0, right: 0 });
  const newButtonWrapperRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    if (createMode === 'file') {
      setCreateName('atlas_test.txt');
      return;
    }

    if (createMode === 'folder') {
      setCreateName('New Folder');
    }
  }, [createMode]);

  const createValidationError = useMemo(
    () => getCreateValidationError(createMode, createName),
    [createMode, createName],
  );

  useEffect(() => {
    if (!showNewMenu) return;

    const updateMenuPosition = () => {
      const rect = newButtonWrapperRef.current?.getBoundingClientRect();
      if (!rect) return;

      setMenuPosition({
        top: rect.bottom + 8,
        right: window.innerWidth - rect.right,
      });
    };

    updateMenuPosition();
    window.addEventListener('resize', updateMenuPosition);
    window.addEventListener('scroll', updateMenuPosition, true);

    return () => {
      window.removeEventListener('resize', updateMenuPosition);
      window.removeEventListener('scroll', updateMenuPosition, true);
    };
  }, [showNewMenu]);

  return (
    <header className="relative z-20 h-16 border-b border-white/5 bg-black/40 backdrop-blur-xl px-6 flex items-center gap-6 overflow-visible">
      <div className="text-xl font-semibold bg-gradient-to-r from-cyan-400 via-blue-400 to-purple-400 bg-clip-text text-transparent">
        Files
      </div>

      <div className="flex items-center gap-2 text-sm text-white/60">
        {breadcrumbs.length === 0 && <span className="text-white/50">No folder selected</span>}
        {breadcrumbs.map((crumb, index) => (
          <div key={crumb.path} className="flex items-center gap-2">
            {index > 0 && <ChevronRight size={14} />}
            <button
              onClick={() => onOpenPath(crumb.path)}
              className={index === breadcrumbs.length - 1 ? 'text-cyan-400' : 'hover:text-white/90 transition-colors'}
            >
              {crumb.label}
            </button>
          </div>
        ))}
      </div>

      <div className="flex-1 max-w-2xl relative">
        <Search className="absolute left-3 top-1/2 -translate-y-1/2 text-white/40" size={18} />
        <input
          type="text"
          value={searchQuery}
          placeholder="Search current folder..."
          className="w-full h-10 pl-10 pr-10 bg-white/5 border border-white/10 rounded-lg text-sm text-white placeholder:text-white/40 focus:outline-none focus:border-cyan-400/50 focus:ring-2 focus:ring-cyan-400/20 transition-all"
          onChange={(event) => onSearchQueryChange(event.target.value)}
        />
        {searchQuery.trim().length > 0 && (
          <button
            onClick={onClearSearch}
            className="absolute right-2 top-1/2 -translate-y-1/2 h-6 w-6 rounded-md border border-white/10 bg-white/5 text-white/55 hover:text-white/85 hover:bg-white/10"
            aria-label="Clear search"
            title="Clear search"
          >
            <X size={13} className="mx-auto" />
          </button>
        )}
      </div>

      <div className="flex items-center gap-2">
        <div ref={newButtonWrapperRef} className="relative overflow-visible">
          <button
            onClick={() => setShowNewMenu((current) => !current)}
            className="group relative h-9 px-3 rounded-lg border transition-all bg-white/5 border-white/10 hover:bg-white/10 hover:border-white/20 inline-flex items-center gap-2"
          >
            <Plus size={16} className="text-white/70 group-hover:text-white" />
            <span className="text-sm text-white/80">New</span>
          </button>
        </div>
        <button
          onClick={onRefresh}
          className="h-9 px-3 rounded-lg border bg-white/5 border-white/10 hover:bg-white/10 hover:border-white/20 inline-flex items-center gap-2"
        >
          <RefreshCw size={15} className="text-white/70" />
          <span className="text-sm text-white/80">Refresh</span>
        </button>
        {speechVisible && (
          <button
            onClick={onToggleSpeech}
            className="h-9 w-9 rounded-lg border bg-white/5 border-white/10 hover:bg-white/10 hover:border-white/20 inline-flex items-center justify-center"
            title={speechEnabled ? 'Speech output on' : 'Speech output off'}
            aria-label={speechEnabled ? 'Speech output on' : 'Speech output off'}
          >
            {speechEnabled ? <Volume2 size={15} className="text-cyan-200" /> : <VolumeX size={15} className="text-white/70" />}
          </button>
        )}
        <button
          onClick={onMicClick}
          className="h-9 w-9 rounded-lg border bg-white/5 border-white/10 hover:bg-white/10 hover:border-white/20 inline-flex items-center justify-center"
          title="Mic"
          aria-label="Mic"
        >
          <Mic size={15} className="text-white/80" />
        </button>
        <button
          onClick={() => {
            onToggleBrainPanel(true);
            onOpenAtlasCommand();
          }}
          className="h-9 px-3 rounded-lg border transition-all bg-gradient-to-r from-cyan-500/20 to-blue-500/20 border-cyan-400/35 hover:from-cyan-500/30 hover:to-blue-500/30 inline-flex items-center gap-2"
        >
          <Sparkles size={15} className="text-cyan-200" />
          <span className="text-sm text-cyan-100">Atlas Command</span>
        </button>
        {voiceNote ? <div className="text-[10px] text-amber-200 pl-1 whitespace-nowrap">{voiceNote}</div> : null}
      </div>

      <div className="flex items-center gap-2 px-4 py-2 bg-gradient-to-r from-cyan-500/10 to-blue-500/10 border border-cyan-400/20 rounded-full">
        <div className="flex items-center gap-1.5">
          <HardDrive size={14} className="text-cyan-400" />
        </div>
        <span className="text-xs text-white/80">Atlas Brain metadata only</span>
      </div>

      {currentPath && <div className="hidden xl:block text-[11px] text-white/45 max-w-[320px] truncate">{currentPath}</div>}

      {showNewMenu && (
        <div className="fixed inset-0 z-[9998]" onClick={() => setShowNewMenu(false)}>
          <div
            className="fixed z-[9999] min-w-[220px] overflow-visible rounded-xl border border-white/10 bg-[#0f0f0f] shadow-2xl shadow-black/50 p-2 space-y-1"
            style={{ top: `${menuPosition.top}px`, right: `${menuPosition.right}px` }}
            onClick={(event) => event.stopPropagation()}
          >
            <button
              onClick={() => {
                setShowNewMenu(false);
                setCreateMode('file');
              }}
              className="w-full flex items-center gap-2 px-3 py-2 rounded-lg text-sm text-white/80 hover:bg-white/10"
            >
              <FileText size={14} className="text-cyan-300" />
              <span>Text File</span>
            </button>
            <button
              onClick={() => {
                setShowNewMenu(false);
                setCreateMode('folder');
              }}
              className="w-full flex items-center gap-2 px-3 py-2 rounded-lg text-sm text-white/80 hover:bg-white/10"
            >
              <FolderPlus size={14} className="text-cyan-300" />
              <span>Folder</span>
            </button>
          </div>
        </div>
      )}

      {createMode && (
        <div className="fixed inset-0 z-[10000] flex items-center justify-center bg-black/55 backdrop-blur-sm px-4">
          <div className="w-full max-w-[360px] rounded-2xl border border-white/10 bg-[#101010] shadow-2xl shadow-black/50 p-4 space-y-3">
            <div className="flex items-start justify-between gap-3">
              <div>
                <div className="text-sm font-medium text-white">{createMode === 'file' ? 'New text file' : 'New folder'}</div>
                <div className="text-xs text-white/45 mt-1 truncate" title={currentPath}>{currentPath || 'No folder selected'}</div>
              </div>
              <button
                onClick={() => setCreateMode(null)}
                className="h-7 w-7 rounded-lg border border-white/10 bg-white/5 text-white/60 hover:bg-white/10"
              >
                <X size={14} className="mx-auto" />
              </button>
            </div>

            <input
              type="text"
              value={createName}
              onChange={(event) => setCreateName(event.target.value)}
              className="w-full h-10 px-3 bg-white/5 border border-white/10 rounded-lg text-sm text-white placeholder:text-white/35 focus:outline-none focus:border-cyan-400/50 focus:ring-2 focus:ring-cyan-400/20"
              placeholder={createMode === 'file' ? 'atlas_test.txt' : 'New Folder'}
            />

            {createValidationError && (
              <div className="text-xs text-amber-200 bg-amber-500/10 border border-amber-300/25 rounded-lg p-2.5">
                {createValidationError}
              </div>
            )}

            <div className="flex items-center justify-end gap-2">
              <button
                onClick={() => setCreateMode(null)}
                className="h-8 px-3 rounded-lg bg-white/5 border border-white/10 text-xs text-white/70 hover:bg-white/10"
              >
                Cancel
              </button>
              <button
                onClick={() => {
                  const trimmedName = createName.trim();
                  if (createMode === 'file') {
                    onCreateFile(currentPath, trimmedName);
                  } else {
                    onCreateFolder(currentPath, trimmedName);
                  }
                  setCreateMode(null);
                }}
                disabled={!currentPath || !!createValidationError || createLoading}
                className="h-8 px-3 rounded-lg bg-cyan-500/20 border border-cyan-400/30 text-xs text-cyan-200 hover:bg-cyan-500/30 disabled:opacity-40 disabled:cursor-not-allowed"
              >
                {createLoading ? 'Creating...' : 'Create'}
              </button>
            </div>
          </div>
        </div>
      )}
    </header>
  );
}

function getCreateValidationError(mode: 'file' | 'folder' | null, rawValue: string): string | null {
  if (!mode) return null;

  const value = rawValue.trim();
  if (!value) return 'Name is required.';
  if (value.includes('..')) return 'Path traversal is not allowed.';
  if (value.includes('/') || value.includes('\\')) return 'Name only. Do not include folder separators.';

  const invalid = ['<', '>', ':', '"', '|', '?', '*'];
  if (invalid.some((ch) => value.includes(ch))) return 'Name contains invalid filename characters.';

  if (mode === 'file') {
    const extension = value.includes('.') ? value.slice(value.lastIndexOf('.')).toLowerCase() : '.txt';
    if (!['.txt', '.md', '.json'].includes(extension)) {
      return 'Only .txt, .md, and .json files are allowed.';
    }
  }

  return null;
}

