import { useEffect, useState } from 'react';
import {
  X,
  FileText,
  Calendar,
  HardDrive,
  Eye,
  Sparkles,
  Edit3,
  ArrowRightLeft,
  Trash2,
  Ban,
  FolderOpen,
  ExternalLink,
  PackageOpen,
} from 'lucide-react';
import { FileItem, FilePreview } from '../App';

const EXTRACTABLE_EXTENSIONS = new Set(['.zip', '.rar', '.7z']);

interface AIInspectorProps {
  fileItem: FileItem;
  preview: FilePreview | null;
  loading: boolean;
  onPreview: () => void;
  onOpenFile: (path: string) => void;
  onShowInExplorer: (path: string) => void;
  onRenameItem: (path: string, newName: string) => void;
  onCopyItem: (sourcePath: string, destinationDirectoryPath: string) => void;
  onMoveItem: (sourcePath: string, destinationDirectoryPath: string) => void;
  onRequestDelete: (item: FileItem) => void;
  onRequestSmartRename: (item: FileItem, previewText?: string) => void;
  smartRename: { path: string; suggestions?: string; error?: string; provider?: string } | null;
  smartRenameLoading: boolean;
  onAISummarize: (path: string) => void;
  onAIExplain: (path: string) => void;
  renameLoading: boolean;
  copyLoading: boolean;
  moveLoading: boolean;
  aiSummary: { path: string; summary?: string; error?: string; provider?: string } | null;
  aiSummaryLoading: boolean;
  aiExplanation: { path: string; explanation?: string; error?: string; provider?: string } | null;
  aiExplanationLoading: boolean;
  externalAction?: { kind: 'rename' | 'copy' | 'move'; nonce: number } | null;
  onExtractArchive: (archivePath: string, mode: 'new-folder' | 'here') => void;
  extractLoading: boolean;
  onUnavailable: () => void;
  onClose: () => void;
}

const ALLOWED_AI_EXTENSIONS = new Set([
  '.txt', '.md', '.json', '.xml', '.csv', '.log',
  '.cs', '.xaml', '.js', '.ts', '.tsx', '.html', '.css',
  '.py', '.ps1', '.sql', '.yaml', '.yml',
]);

const BLOCKED_SECRET_EXTENSIONS = new Set(['.env', '.key', '.pem', '.pfx', '.cer', '.crt']);
const MAX_AI_FILE_SIZE_BYTES = 512 * 1024;

export function AIInspector({
  fileItem,
  preview,
  loading,
  onPreview,
  onOpenFile,
  onShowInExplorer,
  onRenameItem,
  onCopyItem,
  onMoveItem,
  onRequestDelete,
  onRequestSmartRename,
  smartRename,
  smartRenameLoading,
  onAISummarize,
  onAIExplain,
  renameLoading,
  copyLoading,
  moveLoading,
  aiSummary,
  aiSummaryLoading,
  aiExplanation,
  aiExplanationLoading,
  externalAction,
  onExtractArchive,
  extractLoading,
  onUnavailable,
  onClose,
}: AIInspectorProps) {
  const [showRenameCard, setShowRenameCard] = useState(false);
  const [renameValue, setRenameValue] = useState('');
  const [showCopyCard, setShowCopyCard] = useState(false);
  const [copyDestination, setCopyDestination] = useState('');
  const [showMoveCard, setShowMoveCard] = useState(false);
  const [moveDestination, setMoveDestination] = useState('');

  const aiEligibilityError = getAIEligibilityError(fileItem);
  const aiFileEligible = !aiEligibilityError;
  const renameValidationError = getRenameValidationError(fileItem.name, renameValue);
  const canSubmitRename = !renameValidationError && !renameLoading;
  const copyValidationError = getCopyDestinationValidationError(copyDestination);
  const canSubmitCopy = !copyValidationError && !copyLoading;
  const moveValidationError = getCopyDestinationValidationError(moveDestination);
  const canSubmitMove = !moveValidationError && !moveLoading;
  const currentLocation = parentFolder(fileItem.path);
  const quickDestinations = getQuickDestinations(fileItem.path);

  useEffect(() => {
    setShowRenameCard(false);
    setRenameValue(fileItem.name);
    setShowCopyCard(false);
    setCopyDestination(currentLocation !== '-' ? currentLocation : '');
    setShowMoveCard(false);
    setMoveDestination(currentLocation !== '-' ? currentLocation : '');
  }, [fileItem.path, fileItem.name]);

  useEffect(() => {
    if (!externalAction) return;

    if (externalAction.kind === 'rename') {
      setRenameValue(fileItem.name);
      setShowRenameCard(true);
      setShowCopyCard(false);
      setShowMoveCard(false);
      return;
    }

    if (externalAction.kind === 'move') {
      setMoveDestination(currentLocation !== '-' ? currentLocation : '');
      setShowMoveCard(true);
      setShowCopyCard(false);
      setShowRenameCard(false);
      return;
    }

    setCopyDestination(currentLocation !== '-' ? currentLocation : '');
    setShowCopyCard(true);
    setShowRenameCard(false);
    setShowMoveCard(false);
  }, [externalAction?.nonce, externalAction?.kind, fileItem.path, fileItem.name, currentLocation]);

  return (
    <aside className="w-96 border-l border-white/5 bg-black/40 backdrop-blur-xl p-5 flex flex-col gap-5 overflow-y-auto">
      <div className="flex items-start justify-between">
        <div className="flex items-center gap-3">
          <div className="p-2 rounded-lg bg-gradient-to-br from-cyan-500/20 to-purple-500/20">
            <Sparkles className="text-cyan-400" size={18} />
          </div>
          <div>
            <div className="text-sm font-semibold text-white">AI File Assistant</div>
            <div className="text-xs text-white/50">Preview and metadata</div>
          </div>
        </div>
        <button
          onClick={onClose}
          className="p-2 rounded-lg border border-white/10 hover:bg-white/10 transition-all"
        >
          <X size={16} className="text-white/70" />
        </button>
      </div>

      {/* Metadata */}
      <div className="p-4 rounded-xl bg-white/5 border border-white/10 space-y-3">
        <div className="flex items-center gap-3">
          <FileText className="text-cyan-400 shrink-0" size={22} />
          <div className="flex-1 min-w-0">
            <div className="text-sm font-medium text-white truncate" title={fileItem.name}>{fileItem.name}</div>
            <div className="text-xs text-white/50 capitalize">{fileItem.kind}{fileItem.extension ? ` · ${fileItem.extension}` : ''}</div>
          </div>
        </div>

        <div className="space-y-1.5 text-xs">
          <InfoRow icon={HardDrive} label="Size" value={formatBytes(fileItem.sizeBytes)} />
          <InfoRow icon={Calendar} label="Modified" value={formatUtc(fileItem.modifiedUtc)} />
          <InfoRow icon={HardDrive} label="Path" value={fileItem.path} />
        </div>
      </div>

      {/* Quick actions */}
      {fileItem.kind === 'file' && (
        <div className="flex gap-2">
          <button
            onClick={() => onOpenFile(fileItem.path)}
            className="flex-1 flex items-center justify-center gap-1.5 h-8 rounded-lg bg-cyan-500/15 border border-cyan-400/30 text-xs text-cyan-300 hover:bg-cyan-500/25 transition-colors"
          >
            <FolderOpen size={13} /> Open
          </button>
          <button
            onClick={() => onShowInExplorer(fileItem.path)}
            className="flex-1 flex items-center justify-center gap-1.5 h-8 rounded-lg bg-white/5 border border-white/10 text-xs text-white/70 hover:bg-white/10 transition-colors"
          >
            <ExternalLink size={13} /> Show in Explorer
          </button>
          <button
            onClick={() => {
              setRenameValue(fileItem.name);
              setShowRenameCard(true);
              setShowCopyCard(false);
              setShowMoveCard(false);
            }}
            className="flex-1 flex items-center justify-center gap-1.5 h-8 rounded-lg bg-white/5 border border-white/10 text-xs text-white/70 hover:bg-white/10 transition-colors"
          >
            <Edit3 size={13} /> Rename
          </button>
          <button
            onClick={() => {
              setCopyDestination(currentLocation !== '-' ? currentLocation : '');
              setShowCopyCard(true);
              setShowRenameCard(false);
              setShowMoveCard(false);
            }}
            className="flex-1 flex items-center justify-center gap-1.5 h-8 rounded-lg bg-white/5 border border-white/10 text-xs text-white/70 hover:bg-white/10 transition-colors"
          >
            <FileText size={13} /> Copy
          </button>
          <button
            onClick={() => {
              setMoveDestination(currentLocation !== '-' ? currentLocation : '');
              setShowMoveCard(true);
              setShowCopyCard(false);
              setShowRenameCard(false);
            }}
            className="flex-1 flex items-center justify-center gap-1.5 h-8 rounded-lg bg-white/5 border border-white/10 text-xs text-white/70 hover:bg-white/10 transition-colors"
          >
            <ArrowRightLeft size={13} /> Move
          </button>
        </div>
      )}
      {fileItem.kind === 'folder' && (
        <div className="flex gap-2">
          <button
            onClick={() => onShowInExplorer(fileItem.path)}
            className="flex-1 flex items-center justify-center gap-1.5 h-8 rounded-lg bg-white/5 border border-white/10 text-xs text-white/70 hover:bg-white/10 transition-colors"
          >
            <ExternalLink size={13} /> Open in Explorer
          </button>
          <button
            onClick={() => {
              setRenameValue(fileItem.name);
              setShowRenameCard(true);
              setShowCopyCard(false);
              setShowMoveCard(false);
            }}
            className="flex-1 flex items-center justify-center gap-1.5 h-8 rounded-lg bg-white/5 border border-white/10 text-xs text-white/70 hover:bg-white/10 transition-colors"
          >
            <Edit3 size={13} /> Rename
          </button>
          <button
            onClick={() => {
              setCopyDestination(currentLocation !== '-' ? currentLocation : '');
              setShowCopyCard(true);
              setShowRenameCard(false);
              setShowMoveCard(false);
            }}
            className="flex-1 flex items-center justify-center gap-1.5 h-8 rounded-lg bg-white/5 border border-white/10 text-xs text-white/70 hover:bg-white/10 transition-colors"
          >
            <FileText size={13} /> Copy
          </button>
          <button
            onClick={() => {
              setMoveDestination(currentLocation !== '-' ? currentLocation : '');
              setShowMoveCard(true);
              setShowCopyCard(false);
              setShowRenameCard(false);
            }}
            className="flex-1 flex items-center justify-center gap-1.5 h-8 rounded-lg bg-white/5 border border-white/10 text-xs text-white/70 hover:bg-white/10 transition-colors"
          >
            <ArrowRightLeft size={13} /> Move
          </button>
        </div>
      )}

      {showRenameCard && (
        <div className="p-4 rounded-xl bg-white/5 border border-white/10 space-y-3">
          <div className="text-sm font-medium text-white">Rename item</div>
          <div className="space-y-1 text-xs text-white/65">
            <div><span className="text-white/45">Current name:</span> {fileItem.name}</div>
            <div className="truncate" title={parentFolder(fileItem.path)}><span className="text-white/45">Parent folder:</span> {parentFolder(fileItem.path)}</div>
          </div>
          <input
            type="text"
            value={renameValue}
            onChange={(event) => setRenameValue(event.target.value)}
            className="w-full h-9 px-3 bg-black/30 border border-white/10 rounded-lg text-sm text-white placeholder:text-white/35 focus:outline-none focus:border-cyan-400/50"
            placeholder="Enter new name"
          />
          <div className="text-[11px] text-amber-200/80">Rename only. No moving or overwriting.</div>
          {renameValidationError && (
            <div className="text-xs text-amber-200 bg-amber-500/10 border border-amber-300/25 rounded-lg p-2.5">
              {renameValidationError}
            </div>
          )}
          <div className="flex items-center justify-end gap-2">
            <button
              onClick={() => {
                setShowRenameCard(false);
                setRenameValue(fileItem.name);
              }}
              className="h-8 px-3 rounded-lg bg-white/5 border border-white/10 text-xs text-white/70 hover:bg-white/10"
            >
              Cancel
            </button>
            <button
              onClick={() => {
                onRenameItem(fileItem.path, renameValue.trim());
                setShowRenameCard(false);
              }}
              disabled={!canSubmitRename}
              className="h-8 px-3 rounded-lg bg-cyan-500/20 border border-cyan-400/30 text-xs text-cyan-200 hover:bg-cyan-500/30 disabled:opacity-40 disabled:cursor-not-allowed"
            >
              {renameLoading ? 'Renaming...' : 'Rename'}
            </button>
          </div>
        </div>
      )}

      {showCopyCard && (
        <div className="p-4 rounded-xl bg-white/5 border border-white/10 space-y-3">
          <div className="text-sm font-medium text-white">Copy item</div>
          <div className="space-y-1 text-xs text-white/65">
            <div><span className="text-white/45">Item:</span> {fileItem.name}</div>
            <div className="truncate" title={currentLocation}><span className="text-white/45">Current location:</span> {currentLocation}</div>
          </div>

          <div className="flex flex-wrap gap-2">
            {quickDestinations.map((quick) => (
              <button
                key={quick.label}
                onClick={() => setCopyDestination(quick.path)}
                className="h-7 px-2.5 rounded-md bg-cyan-500/12 border border-cyan-400/25 text-xs text-cyan-200 hover:bg-cyan-500/20"
              >
                {quick.label}
              </button>
            ))}
          </div>

          <input
            type="text"
            value={copyDestination}
            onChange={(event) => setCopyDestination(event.target.value)}
            className="w-full h-9 px-3 bg-black/30 border border-white/10 rounded-lg text-sm text-white placeholder:text-white/35 focus:outline-none focus:border-cyan-400/50"
            placeholder="Destination folder path"
          />

          {copyValidationError && (
            <div className="text-xs text-amber-200 bg-amber-500/10 border border-amber-300/25 rounded-lg p-2.5">
              {copyValidationError}
            </div>
          )}

          <div className="flex items-center justify-end gap-2">
            <button
              onClick={() => {
                setShowCopyCard(false);
                setCopyDestination(currentLocation !== '-' ? currentLocation : '');
              }}
              className="h-8 px-3 rounded-lg bg-white/5 border border-white/10 text-xs text-white/70 hover:bg-white/10"
            >
              Cancel
            </button>
            <button
              onClick={() => {
                onCopyItem(fileItem.path, copyDestination.trim());
                setShowCopyCard(false);
              }}
              disabled={!canSubmitCopy}
              className="h-8 px-3 rounded-lg bg-cyan-500/20 border border-cyan-400/30 text-xs text-cyan-200 hover:bg-cyan-500/30 disabled:opacity-40 disabled:cursor-not-allowed"
            >
              {copyLoading ? 'Copying...' : 'Copy'}
            </button>
          </div>
        </div>
      )}

      {showMoveCard && (
        <div className="p-4 rounded-xl bg-white/5 border border-white/10 space-y-3">
          <div className="text-sm font-medium text-white">Move item</div>
          <div className="space-y-1 text-xs text-white/65">
            <div><span className="text-white/45">Item:</span> {fileItem.name}</div>
            <div className="truncate" title={currentLocation}><span className="text-white/45">Current location:</span> {currentLocation}</div>
          </div>

          <div className="flex flex-wrap gap-2">
            {quickDestinations.map((quick) => (
              <button
                key={`move-${quick.label}`}
                onClick={() => setMoveDestination(quick.path)}
                className="h-7 px-2.5 rounded-md bg-cyan-500/12 border border-cyan-400/25 text-xs text-cyan-200 hover:bg-cyan-500/20"
              >
                {quick.label}
              </button>
            ))}
          </div>

          <input
            type="text"
            value={moveDestination}
            onChange={(event) => setMoveDestination(event.target.value)}
            className="w-full h-9 px-3 bg-black/30 border border-white/10 rounded-lg text-sm text-white placeholder:text-white/35 focus:outline-none focus:border-cyan-400/50"
            placeholder="Destination folder path"
          />

          <div className="text-[11px] text-amber-200/80">Move removes the item from the current folder after success.</div>

          {moveValidationError && (
            <div className="text-xs text-amber-200 bg-amber-500/10 border border-amber-300/25 rounded-lg p-2.5">
              {moveValidationError}
            </div>
          )}

          <div className="flex items-center justify-end gap-2">
            <button
              onClick={() => {
                setShowMoveCard(false);
                setMoveDestination(currentLocation !== '-' ? currentLocation : '');
              }}
              className="h-8 px-3 rounded-lg bg-white/5 border border-white/10 text-xs text-white/70 hover:bg-white/10"
            >
              Cancel
            </button>
            <button
              onClick={() => {
                onMoveItem(fileItem.path, moveDestination.trim());
                setShowMoveCard(false);
              }}
              disabled={!canSubmitMove}
              className="h-8 px-3 rounded-lg bg-cyan-500/20 border border-cyan-400/30 text-xs text-cyan-200 hover:bg-cyan-500/30 disabled:opacity-40 disabled:cursor-not-allowed"
            >
              {moveLoading ? 'Moving...' : 'Move'}
            </button>
          </div>
        </div>
      )}

      {/* Preview */}
      <div className="p-4 rounded-xl bg-gradient-to-br from-purple-500/10 to-cyan-500/10 border border-purple-400/20 space-y-3">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <Eye className="text-purple-400" size={15} />
            <span className="text-sm font-medium text-white">Preview</span>
          </div>
          {fileItem.kind === 'file' && (
            <button
              onClick={onPreview}
              disabled={!fileItem.canPreview || loading}
              className="h-7 px-2.5 rounded-md bg-white/5 border border-white/10 text-xs text-white/60 hover:bg-white/10 disabled:opacity-35 disabled:cursor-not-allowed"
            >
              {loading ? 'Loading…' : 'Refresh'}
            </button>
          )}
        </div>

        {fileItem.kind === 'folder' && (
          <p className="text-xs text-white/60 leading-relaxed">Folder selected. Open a file to preview its contents.</p>
        )}

        {fileItem.kind === 'file' && (
          <div>
            {/* Text preview */}
            {preview?.path === fileItem.path && preview.ok && preview.previewKind === 'text' && (
              <pre className="max-h-72 overflow-auto text-[11px] leading-5 font-mono bg-black/40 border border-white/10 rounded-lg p-3 text-white/80 whitespace-pre-wrap break-all">
                {preview.text}
              </pre>
            )}

            {/* Image preview */}
            {preview?.path === fileItem.path && preview.ok && preview.previewKind === 'image' && preview.url && (
              <div
                className="max-h-64 rounded-lg border border-white/10 overflow-hidden flex items-center justify-center"
                style={{ background: 'repeating-conic-gradient(#1e1e1e 0% 25%, #2a2a2a 0% 50%) 0 0 / 16px 16px' }}
              >
                <img
                  src={preview.url}
                  alt={fileItem.name}
                  className="max-h-64 max-w-full object-contain"
                />
              </div>
            )}

            {/* No preview */}
            {preview?.path === fileItem.path && preview.previewKind === 'none' && (
              <div className="text-xs text-white/50 bg-white/5 border border-white/10 rounded-lg p-3 text-center">
                Preview not available for this file type.
                {preview.error && <div className="mt-1 text-amber-200/70">{preview.error}</div>}
              </div>
            )}

            {/* Error */}
            {preview?.path === fileItem.path && !preview.ok && preview.previewKind !== 'none' && (
              <div className="text-xs text-amber-200 bg-amber-500/10 border border-amber-300/25 rounded-lg p-3">
                {preview.error || 'Preview unavailable.'}
              </div>
            )}

            {/* Not yet loaded */}
            {(!preview || preview.path !== fileItem.path) && !loading && (
              <div className="text-xs text-white/40 text-center py-2">
                {fileItem.canPreview ? 'Click Refresh to preview.' : 'Preview not available for this file type.'}
              </div>
            )}
          </div>
        )}
      </div>

      {/* AI Summary */}
      {fileItem.kind === 'file' && (
        <div className="p-4 rounded-xl bg-gradient-to-br from-amber-500/10 to-orange-500/10 border border-amber-400/20 space-y-3">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-2">
              <Sparkles className="text-amber-400" size={15} />
              <span className="text-sm font-medium text-white">AI Summary</span>
            </div>
            {!aiSummaryLoading && !aiSummary && (
              <button
                onClick={() => onAISummarize(fileItem.path)}
                disabled={!aiFileEligible}
                className="h-7 px-2.5 rounded-md bg-amber-500/20 border border-amber-400/30 text-xs text-amber-300 hover:bg-amber-500/30 transition-colors disabled:opacity-40 disabled:cursor-not-allowed"
              >
                Summarize
              </button>
            )}
          </div>

          {aiSummaryLoading && (
            <div className="text-xs text-white/60 text-center py-2">Reading file…</div>
          )}

          {aiSummary && aiSummary.path === fileItem.path && aiSummary.summary && (
            <div className="space-y-2">
              <div className="text-xs text-white/80 leading-relaxed whitespace-pre-wrap break-words bg-black/30 p-3 rounded-lg border border-white/10 max-h-48 overflow-y-auto">
                {aiSummary.summary}
              </div>
              {aiSummary.provider && (
                <div className="text-[10px] text-white/40 text-right">via {aiSummary.provider}</div>
              )}
            </div>
          )}

          {aiSummary && aiSummary.path === fileItem.path && aiSummary.error && (
            <div className="text-xs text-amber-200 bg-amber-500/10 border border-amber-300/25 rounded-lg p-3">
              {aiSummary.error}
            </div>
          )}

          {!aiSummaryLoading && !aiSummary && aiEligibilityError && (
            <div className="text-xs text-amber-200 bg-amber-500/10 border border-amber-300/25 rounded-lg p-3">
              {aiEligibilityError}
            </div>
          )}

          {!aiSummaryLoading && !aiSummary && (
            <div className="text-xs text-white/40 text-center py-2">
              {aiEligibilityError ? 'Choose an eligible file to use AI summary' : 'Click Summarize to analyze this file'}
            </div>
          )}
        </div>
      )}

      {/* AI Explanation */}
      {fileItem.kind === 'file' && (
        <div className="p-4 rounded-xl bg-gradient-to-br from-cyan-500/10 to-blue-500/10 border border-cyan-400/20 space-y-3">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-2">
              <Sparkles className="text-cyan-300" size={15} />
              <span className="text-sm font-medium text-white">AI Explanation</span>
            </div>
            {!aiExplanationLoading && !aiExplanation && (
              <button
                onClick={() => onAIExplain(fileItem.path)}
                disabled={!aiFileEligible}
                className="h-7 px-2.5 rounded-md bg-cyan-500/20 border border-cyan-400/30 text-xs text-cyan-200 hover:bg-cyan-500/30 transition-colors disabled:opacity-40 disabled:cursor-not-allowed"
              >
                Explain File
              </button>
            )}
          </div>

          {aiExplanationLoading && (
            <div className="text-xs text-white/60 text-center py-2">Explaining file...</div>
          )}

          {aiExplanation && aiExplanation.path === fileItem.path && aiExplanation.explanation && (
            <div className="space-y-2">
              <div className="text-xs text-white/80 leading-relaxed whitespace-pre-wrap break-words bg-black/30 p-3 rounded-lg border border-white/10 max-h-56 overflow-y-auto">
                {aiExplanation.explanation}
              </div>
              {aiExplanation.provider && (
                <div className="text-[10px] text-white/40 text-right">via {aiExplanation.provider}</div>
              )}
            </div>
          )}

          {aiExplanation && aiExplanation.path === fileItem.path && aiExplanation.error && (
            <div className="text-xs text-amber-200 bg-amber-500/10 border border-amber-300/25 rounded-lg p-3">
              {aiExplanation.error}
            </div>
          )}

          {!aiExplanationLoading && !aiExplanation && aiEligibilityError && (
            <div className="text-xs text-amber-200 bg-amber-500/10 border border-amber-300/25 rounded-lg p-3">
              {aiEligibilityError}
            </div>
          )}

          {!aiExplanationLoading && !aiExplanation && (
            <div className="text-xs text-white/40 text-center py-2">
              {aiEligibilityError ? 'Choose an eligible file to use AI explanation' : 'Click Explain File for a deeper analysis'}
            </div>
          )}
        </div>
      )}

      {/* Archive Tools */}
      {fileItem.kind === 'file' && EXTRACTABLE_EXTENSIONS.has((fileItem.extension || '').toLowerCase()) && (
        <div className="p-4 rounded-xl bg-gradient-to-br from-violet-500/10 to-indigo-500/10 border border-violet-400/20 space-y-3">
          <div className="flex items-center gap-2">
            <PackageOpen className="text-violet-400" size={15} />
            <span className="text-sm font-medium text-white">Archive Tools</span>
            <span className="text-[10px] px-1.5 py-0.5 rounded-full border border-violet-400/30 bg-violet-500/10 text-violet-300 ml-auto">{(fileItem.extension || '').toUpperCase()}</span>
          </div>
          <div className="text-xs text-white/55 space-y-0.5">
            <div>Size: {fileItem.sizeBytes !== null ? formatBytes(fileItem.sizeBytes) : '—'}</div>
            <div className="text-white/35">{(fileItem.extension || '').toLowerCase() === '.zip' ? 'Built-in .zip extraction' : 'Requires 7-Zip (7z.exe)'}</div>
          </div>
          <div className="text-xs text-white/45">Suggested: Extract to inspect contents. Keep if installer/source needed.</div>
          <div className="flex gap-2">
            <button
              onClick={() => onExtractArchive(fileItem.path, 'new-folder')}
              disabled={extractLoading}
              className="flex-1 flex items-center justify-center gap-1.5 h-8 rounded-lg bg-violet-500/20 border border-violet-400/30 text-xs text-violet-200 hover:bg-violet-500/30 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
            >
              <PackageOpen size={13} /> {extractLoading ? 'Extracting...' : 'Extract to new folder'}
            </button>
            <button
              onClick={() => onExtractArchive(fileItem.path, 'here')}
              disabled={extractLoading}
              className="flex-1 flex items-center justify-center gap-1.5 h-8 rounded-lg bg-white/5 border border-white/10 text-xs text-white/70 hover:bg-white/10 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
            >
              <PackageOpen size={13} /> Extract here
            </button>
          </div>
          <button
            onClick={() => onShowInExplorer(fileItem.path)}
            className="w-full flex items-center justify-center gap-1.5 h-7 rounded-lg bg-white/5 border border-white/10 text-xs text-white/55 hover:bg-white/10 transition-colors"
          >
            <ExternalLink size={12} /> Show in Explorer
          </button>
        </div>
      )}

      {/* Item actions */}
      <div className="space-y-2">
        <div className="text-xs font-medium text-white/40 uppercase tracking-wide">Item Actions</div>
        <div className="grid grid-cols-2 gap-2">
          <ActionButton icon={Trash2} label="Delete" onClick={() => onRequestDelete(fileItem)} />
          <ActionButton
            icon={Sparkles}
            label="Suggest Names"
            onClick={() => {
              const previewTextForRename =
                preview?.ok && preview.path === fileItem.path && preview.previewKind === 'text'
                  ? preview.text?.slice(0, 20480)
                  : undefined;
              onRequestSmartRename(fileItem, previewTextForRename);
            }}
          />
        </div>
      </div>

      {/* Smart Rename results */}
      {smartRename?.path === fileItem.path && (
        <div className="rounded-xl border border-purple-400/15 bg-purple-500/5 p-3.5 space-y-2.5">
          <div className="flex items-center gap-2 text-xs font-medium text-purple-300">
            <Sparkles size={13} />
            Smart Rename Suggestions
            {smartRename.provider && <span className="ml-auto text-[10px] text-white/30">via {smartRename.provider}</span>}
          </div>
          {smartRenameLoading && (
            <div className="flex items-center gap-2 text-xs text-white/45 py-1">
              <span className="inline-block w-3 h-3 rounded-full border-2 border-white/20 border-t-purple-400 animate-spin" />
              Getting rename suggestions…
            </div>
          )}
          {!smartRenameLoading && smartRename.error && (
            <div className="text-xs text-amber-200 bg-amber-500/10 border border-amber-300/25 rounded-lg p-2.5">{smartRename.error}</div>
          )}
          {!smartRenameLoading && smartRename.suggestions && (
            <div className="space-y-2">
              {parseSuggestions(smartRename.suggestions).map((s, i) => (
                <div key={i} className="flex items-start gap-2 p-2.5 rounded-lg bg-white/5 border border-white/8">
                  <div className="min-w-0 flex-1 space-y-0.5">
                    <div className="text-xs font-medium text-white truncate">{s.name}</div>
                    <div className="text-[11px] text-white/50">{s.reason}</div>
                  </div>
                  <div className="flex flex-col gap-1 shrink-0">
                    <button
                      onClick={() => navigator.clipboard.writeText(s.name)}
                      className="h-6 px-2 rounded text-[10px] bg-white/8 border border-white/12 text-white/60 hover:bg-white/15 hover:text-white"
                      title="Copy name to clipboard"
                    >
                      Copy
                    </button>
                    <button
                      onClick={() => onRenameItem(fileItem.path, s.name)}
                      className="h-6 px-2 rounded text-[10px] bg-purple-500/20 border border-purple-400/30 text-purple-200 hover:bg-purple-500/30"
                      title="Rename to this"
                    >
                      Rename
                    </button>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      <div className="pt-3 border-t border-white/5">
        <div className="flex items-center gap-2 text-xs text-white/40">
          <Ban size={13} className="text-amber-300/70 shrink-0" />
          Delete moves items to Recycle Bin only. AI summary is basic and honest.
        </div>
      </div>
    </aside>
  );
}

function getAIEligibilityError(fileItem: FileItem): string | null {
  if (fileItem.kind !== 'file') return 'Folders are not supported for this AI action.';

  const extension = (fileItem.extension || '').toLowerCase();
  const fileName = (fileItem.name || '').toLowerCase();
  const sizeBytes = fileItem.sizeBytes ?? -1;

  if (!ALLOWED_AI_EXTENSIONS.has(extension)) {
    return 'File type not supported for AI actions.';
  }

  if (BLOCKED_SECRET_EXTENSIONS.has(extension) || looksSecretLike(fileName)) {
    return 'This file is blocked for security reasons.';
  }

  if (sizeBytes > MAX_AI_FILE_SIZE_BYTES) {
    return `File is too large (${formatBytes(sizeBytes)}). Max size: 512 KB.`;
  }

  return null;
}

function looksSecretLike(fileName: string): boolean {
  const terms = ['secret', 'token', 'password', 'private', 'credential', 'api-key', 'apikey'];
  return terms.some((term) => fileName.includes(term));
}

function parentFolder(path: string): string {
  if (!path) return '-';
  const normalized = path.replace(/\/+$/g, '');
  const index = normalized.lastIndexOf('\\');
  return index > 0 ? normalized.substring(0, index) : '-';
}

function getRenameValidationError(currentName: string, nextName: string): string | null {
  const value = nextName.trim();
  if (!value) return 'New name is required.';
  if (value === currentName) return 'New name must be different from current name.';
  if (value.includes('..')) return 'Path traversal is not allowed.';
  if (value.includes('/') || value.includes('\\')) return 'Name only. Do not include folder separators.';
  if (value.indexOf('.') === 0 && value.length === 1) return 'Invalid name.';

  const invalid = ['<', '>', ':', '"', '|', '?', '*'];
  if (invalid.some((ch) => value.includes(ch))) return 'Name contains invalid filename characters.';

  return null;
}

function getCopyDestinationValidationError(destination: string): string | null {
  const value = destination.trim();
  if (!value) return 'Destination folder path is required.';
  if (value.includes('*') || value.includes('?') || value.includes('"') || value.includes('<') || value.includes('>') || value.includes('|')) {
    return 'Destination path contains invalid characters.';
  }
  return null;
}

function getQuickDestinations(itemPath: string): Array<{ label: string; path: string }> {
  const normalized = itemPath.replace(/\//g, '\\');
  const match = normalized.match(/^([a-zA-Z]:\\Users\\[^\\]+)/);
  if (!match) return [];

  const userHome = match[1];
  return [
    { label: 'Desktop', path: `${userHome}\\Desktop` },
    { label: 'Documents', path: `${userHome}\\Documents` },
    { label: 'Downloads', path: `${userHome}\\Downloads` },
  ];
}

function InfoRow({
  icon: Icon,
  label,
  value,
}: {
  icon: any;
  label: string;
  value: string;
}) {
  return (
    <div className="flex items-center gap-2">
      <Icon size={14} className="text-white/40" />
      <span className="text-white/50">{label}:</span>
      <span className="ml-auto font-medium text-white truncate max-w-[60%] text-right" title={value}>
        {value}
      </span>
    </div>
  );
}

function parseSuggestions(text: string): { name: string; reason: string }[] {
  return text
    .split('\n')
    .map((line) => {
      const match = line.match(/^\d+\.\s+(.+?)\s*\|\s*(.+)$/);
      if (!match) return null;
      return { name: match[1].trim(), reason: match[2].trim() };
    })
    .filter((s): s is { name: string; reason: string } => s !== null);
}

function ActionButton({ icon: Icon, label, onClick }: { icon: any; label: string; onClick?: () => void }) {
  return (
    <button onClick={onClick} className="flex items-center justify-center gap-2 p-3 rounded-lg bg-white/5 border border-white/10 hover:bg-white/10 hover:border-amber-300/40 transition-all group">
      <Icon size={14} className="text-white/60 group-hover:text-amber-200 transition-colors" />
      <span className="text-xs text-white/70 group-hover:text-white transition-colors">
        {label}
      </span>
    </button>
  );
}

function formatBytes(sizeBytes: number | null) {
  if (sizeBytes === null || sizeBytes < 0) return '-';
  if (sizeBytes < 1024) return `${sizeBytes} B`;
  if (sizeBytes < 1024 * 1024) return `${(sizeBytes / 1024).toFixed(1)} KB`;
  if (sizeBytes < 1024 * 1024 * 1024) return `${(sizeBytes / (1024 * 1024)).toFixed(1)} MB`;
  return `${(sizeBytes / (1024 * 1024 * 1024)).toFixed(2)} GB`;
}

function formatUtc(value: string) {
  if (!value) return '-';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toISOString().replace('T', ' ').replace('Z', '');
}
