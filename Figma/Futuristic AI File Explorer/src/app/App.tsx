import { useEffect, useMemo, useRef, useState } from 'react';
import { AtlasBrainAction, Header } from './components/Header';
import { Sidebar } from './components/Sidebar';
import type { SidebarTool } from './components/Sidebar';
import { SmartCards } from './components/SmartCards';
import { FileGrid } from './components/FileGrid';
import { AIInspector } from './components/AIInspector';
import { StatusBar } from './components/StatusBar';

export type ExplorerRoot = {
  id: string;
  label: string;
  path: string;
  group: 'known' | 'drive';
};

export type FileItem = {
  name: string;
  path: string;
  kind: 'folder' | 'file';
  extension: string;
  sizeBytes: number | null;
  modifiedUtc: string;
  isHidden: boolean;
  canPreview: boolean;
};

export type FilePreview = {
  ok: boolean;
  path: string;
  previewKind: 'text' | 'image' | 'none';
  text?: string;
  url?: string;
  error?: string;
};

type InspectorAction = {
  kind: 'rename' | 'copy' | 'move';
  nonce: number;
};

type ClipboardBuffer = {
  path: string;
  name: string;
  kind: 'file' | 'folder';
  mode: 'copy' | 'cut';
};

type TransferJob = {
  id: string;
  type: 'copy' | 'move';
  sourcePath: string;
  destinationDirectoryPath: string;
  itemName: string;
  kind: 'file' | 'folder';
  status: 'queued' | 'running' | 'success' | 'failed' | 'cancelled';
  progressPercent: number;
  bytesTotal: number;
  bytesDone: number;
  startedAt?: string;
  finishedAt?: string;
  error?: string;
};

type ToolFolderStatus = {
  label: string;
  path: string;
  exists: boolean;
};

type NetworkDriveStatus = {
  label: string;
  path: string;
};

type SidebarToolStatus = {
  cloudFolders: ToolFolderStatus[];
  vaultFolders: ToolFolderStatus[];
  mappedDrives: NetworkDriveStatus[];
};

type HostMessage =
  | { type: 'file-explorer-roots'; roots?: ExplorerRoot[] }
  | { type: 'file-explorer-directory'; requestId?: string; path?: string; directoryPath?: string; parentPath?: string | null; items?: FileItem[]; folderCount?: number; fileCount?: number; error?: string }
  | { type: 'file-explorer-directory-error'; requestId?: string; path?: string; directoryPath?: string; error?: string }
  | { type: 'file-explorer-preview'; ok?: boolean; path?: string; previewKind?: 'text' | 'image' | 'none'; text?: string; url?: string; error?: string }
  | { type: 'file-explorer-open-result'; ok?: boolean; path?: string; error?: string }
  | { type: 'file-explorer-ai-summary'; ok?: boolean; path?: string; summary?: string; error?: string; provider?: string }
  | { type: 'file-explorer-ai-explanation'; ok?: boolean; path?: string; explanation?: string; error?: string; provider?: string }
  | { type: 'file-explorer-rename-result'; ok?: boolean; oldPath?: string; newPath?: string; error?: string }
  | { type: 'file-explorer-create-result'; ok?: boolean; path?: string; kind?: 'file' | 'folder'; error?: string }
  | { type: 'file-explorer-copy-result'; ok?: boolean; sourcePath?: string; destinationPath?: string; error?: string }
  | { type: 'file-explorer-move-result'; ok?: boolean; sourcePath?: string; destinationPath?: string; error?: string }
  | { type: 'file-explorer-delete-result'; ok?: boolean; path?: string; error?: string }
  | { type: 'file-explorer-folder-brief-result'; ok?: boolean; path?: string; brief?: string; error?: string; provider?: string }
  | { type: 'file-explorer-folder-question-result'; ok?: boolean; path?: string; answer?: string; error?: string; provider?: string }
  | { type: 'file-explorer-folder-actions-result'; ok?: boolean; path?: string; actions?: string; error?: string; provider?: string }
  | { type: 'file-explorer-smart-rename-result'; ok?: boolean; path?: string; suggestions?: string; error?: string; provider?: string }
  | { type: 'file-explorer-atlas-brain-project-result'; ok?: boolean; path?: string; entries?: BrainScanEntry[]; error?: string }
  | { type: 'file-explorer-atlas-brain-action-plan-result'; ok?: boolean; path?: string; plan?: string; error?: string; provider?: string }
  | { type: 'atlas-brain-organize-plan-result'; ok?: boolean; path?: string; planJson?: string; error?: string; provider?: string; repaired?: boolean }
  | { type: 'file-explorer-transfer-update'; jobId?: string; status?: 'queued' | 'running' | 'success' | 'failed' | 'cancelled'; progressPercent?: number; bytesDone?: number; bytesTotal?: number; error?: string }
  | { type: 'file-explorer-sidebar-tools-status'; cloudFolders?: ToolFolderStatus[]; vaultFolders?: ToolFolderStatus[]; mappedDrives?: NetworkDriveStatus[]; error?: string }
  | { type: 'file-explorer-network-open-result'; ok?: boolean; error?: string }
  | { type: 'file-explorer-extract-result'; ok?: boolean; archivePath?: string; destinationPath?: string; error?: string };

export type BrainScanEntry = {
  name: string;
  path: string;
  kind: 'folder' | 'file';
  extension: string;
  sizeBytes: number | null;
  modifiedUtc: string;
  depth: number;
};

export type OrganizePlanAction = {
  type: 'create-folder' | 'move' | 'rename';
  sourceName: string;
  targetFolder: string;
  newName: string;
  reason: string;
  group?: string;
  reviewOnly?: boolean;
};

type OrganizePlanResult = {
  path: string;
  planJson?: string;
  error?: string;
  provider?: string;
  repaired?: boolean;
};

type AtlasCommandIntent =
  | 'organize'
  | 'group-installers'
  | 'archives'
  | 'sensitive-review'
  | 'duplicates'
  | 'safe-share'
  | 'rename-suggestions'
  | 'recent'
  | 'project-review'
  | 'unsupported';

type WebViewBridge = {
  postMessage?: (msg: unknown) => void;
  addEventListener?: (event: string, handler: (event: { data: unknown }) => void) => void;
  removeEventListener?: (event: string, handler: (event: { data: unknown }) => void) => void;
};

declare global {
  interface Window {
    chrome?: {
      webview?: WebViewBridge;
    };
  }
}

const NOT_WIRED_MESSAGE = 'Not wired yet — coming in a later patch.';
const BRAIN_AI_TIMEOUT_MS = 45000;
const BRAIN_AI_TIMEOUT_MESSAGE = 'Atlas did not return a result. Check AI provider/runtime logs.';
const FILE_EXPLORER_SPEECH_WIRED = false;
const FILE_EXPLORER_MIC_WIRED = false;
const DIRECTORY_LOAD_TIMEOUT_MS = 12000;

type DirectoryRequestState = {
  id: string;
  path: string;
};

export default function App() {
  const [roots, setRoots] = useState<ExplorerRoot[]>([]);
  const [currentPath, setCurrentPath] = useState('');
  const [parentPath, setParentPath] = useState<string | null>(null);
  const [items, setItems] = useState<FileItem[]>([]);
  const [selectedFile, setSelectedFile] = useState<FileItem | null>(null);
  const [preview, setPreview] = useState<FilePreview | null>(null);
  const [showAIPanel, setShowAIPanel] = useState(false);
  const [showBrainPanel, setShowBrainPanel] = useState(false);
  const [directoryLoading, setDirectoryLoading] = useState(false);
  const [previewLoading, setPreviewLoading] = useState(false);
  const [directoryError, setDirectoryError] = useState('');
  const [toast, setToast] = useState('');
  const [currentRootId, setCurrentRootId] = useState('');
  const [aiSummary, setAiSummary] = useState<{ path: string; summary?: string; error?: string; provider?: string } | null>(null);
  const [aiSummaryLoading, setAiSummaryLoading] = useState(false);
  const [aiExplanation, setAiExplanation] = useState<{ path: string; explanation?: string; error?: string; provider?: string } | null>(null);
  const [aiExplanationLoading, setAiExplanationLoading] = useState(false);
  const [renameLoading, setRenameLoading] = useState(false);
  const [createLoading, setCreateLoading] = useState(false);
  const [copyLoading, setCopyLoading] = useState(false);
  const [moveLoading, setMoveLoading] = useState(false);
  const [deleteLoading, setDeleteLoading] = useState(false);
  const [pendingSelectionPath, setPendingSelectionPath] = useState('');
  const [searchQuery, setSearchQuery] = useState('');
  const [inspectorAction, setInspectorAction] = useState<InspectorAction | null>(null);
  const [contextCreateMode, setContextCreateMode] = useState<'file' | 'folder' | null>(null);
  const [contextCreateName, setContextCreateName] = useState('');
  const [clipboardBuffer, setClipboardBuffer] = useState<ClipboardBuffer | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<FileItem | null>(null);
  const [folderBrief, setFolderBrief] = useState<{ path: string; brief?: string; error?: string; provider?: string } | null>(null);
  const [folderBriefLoading, setFolderBriefLoading] = useState(false);
  const [folderAnswer, setFolderAnswer] = useState<{ path: string; answer?: string; error?: string; provider?: string } | null>(null);
  const [folderAnswerLoading, setFolderAnswerLoading] = useState(false);
  const [folderActions, setFolderActions] = useState<{ path: string; actions?: string; error?: string; provider?: string } | null>(null);
  const [folderActionsLoading, setFolderActionsLoading] = useState(false);
  const [smartRename, setSmartRename] = useState<{ path: string; suggestions?: string; error?: string; provider?: string } | null>(null);
  const [smartRenameLoading, setSmartRenameLoading] = useState(false);
  const [showInListFocusPath, setShowInListFocusPath] = useState('');
  const [selectedBrainAction, setSelectedBrainAction] = useState<AtlasBrainAction | null>(null);
  const [atlasCommandInput, setAtlasCommandInput] = useState('');
  const [atlasCommandLast, setAtlasCommandLast] = useState('');
  const [atlasCommandIntent, setAtlasCommandIntent] = useState<AtlasCommandIntent>('organize');
  const [activeSidebarTool, setActiveSidebarTool] = useState<SidebarTool | null>(null);
  const [transferJobs, setTransferJobs] = useState<TransferJob[]>([]);
  const [sidebarToolStatus, setSidebarToolStatus] = useState<SidebarToolStatus>({ cloudFolders: [], vaultFolders: [], mappedDrives: [] });
  const [sidebarToolStatusLoading, setSidebarToolStatusLoading] = useState(false);
  const [sidebarToolStatusError, setSidebarToolStatusError] = useState('');
  const [collectionFilter, setCollectionFilter] = useState<{ label: string; paths: Set<string> } | null>(null);
  const [brainProjectScan, setBrainProjectScan] = useState<{ path: string; entries?: BrainScanEntry[]; error?: string } | null>(null);
  const [brainProjectScanLoading, setBrainProjectScanLoading] = useState(false);
  const [brainActionPlan, setBrainActionPlan] = useState<{ path: string; plan?: string; error?: string; provider?: string } | null>(null);
  const [brainActionPlanLoading, setBrainActionPlanLoading] = useState(false);
  const [extractLoading, setExtractLoading] = useState(false);
  const [fileExplorerVoiceNote, setFileExplorerVoiceNote] = useState('');
  const [fileExplorerSpeechEnabled, setFileExplorerSpeechEnabled] = useState(true);
  const rootsRef = useRef<ExplorerRoot[]>([]);
  const currentPathRef = useRef('');
  const selectedFileRef = useRef<FileItem | null>(null);
  const clipboardBufferRef = useRef<ClipboardBuffer | null>(null);
  const transferJobsRef = useRef<TransferJob[]>([]);
  const isPasteOperationRef = useRef(false);
  const brainProjectScanTimeoutRef = useRef<number | null>(null);
  const brainActionPlanTimeoutRef = useRef<number | null>(null);
  const [organizePlan, setOrganizePlan] = useState<OrganizePlanResult | null>(null);
  const [organizePlanLoading, setOrganizePlanLoading] = useState(false);
  const [organizeExecStatuses, setOrganizeExecStatuses] = useState<Record<string, string>>({});
  const [organizeIsExecuting, setOrganizeIsExecuting] = useState(false);
  const organizePlanTimeoutRef = useRef<number | null>(null);
  const organizeResolveRef = useRef<((ok: boolean, error?: string) => void) | null>(null);
  const organizeActionTimeoutRef = useRef<number | null>(null);
  const directoryLoadTimeoutRef = useRef<number | null>(null);
  const directoryRequestCounterRef = useRef(0);
  const activeDirectoryRequestRef = useRef<DirectoryRequestState | null>(null);
  const lastRequestedDirectoryPathRef = useRef('');
  const directoryLoadingRef = useRef(false);
  const [directoryLoadingPath, setDirectoryLoadingPath] = useState('');

  useEffect(() => {
    if (!fileExplorerVoiceNote) return;
    const timeout = window.setTimeout(() => setFileExplorerVoiceNote(''), 2400);
    return () => window.clearTimeout(timeout);
  }, [fileExplorerVoiceNote]);

  const clearDirectoryLoadTimeout = () => {
    if (directoryLoadTimeoutRef.current !== null) {
      window.clearTimeout(directoryLoadTimeoutRef.current);
      directoryLoadTimeoutRef.current = null;
    }
  };

  const handleFileExplorerMicClick = () => {
    if (!FILE_EXPLORER_MIC_WIRED) {
      setFileExplorerVoiceNote('Mic not wired');
      return;
    }
  };

  const handleFileExplorerSpeechToggle = () => {
    if (!FILE_EXPLORER_SPEECH_WIRED) return;
    setFileExplorerSpeechEnabled((current) => !current);
  };

  useEffect(() => {
    if (contextCreateMode === 'file') {
      setContextCreateName('atlas_context.txt');
      return;
    }

    if (contextCreateMode === 'folder') {
      setContextCreateName('New Folder');
    }
  }, [contextCreateMode]);

  const contextCreateValidationError = useMemo(
    () => getCreateValidationError(contextCreateMode, contextCreateName),
    [contextCreateMode, contextCreateName],
  );

  useEffect(() => {
    currentPathRef.current = currentPath;
  }, [currentPath]);

  useEffect(() => {
    directoryLoadingRef.current = directoryLoading;
  }, [directoryLoading]);

  useEffect(() => {
    selectedFileRef.current = selectedFile;
  }, [selectedFile]);

  useEffect(() => {
    clipboardBufferRef.current = clipboardBuffer;
  }, [clipboardBuffer]);

  useEffect(() => {
    transferJobsRef.current = transferJobs;
  }, [transferJobs]);

  useEffect(() => {
    setCopyLoading(transferJobs.some((job) => job.type === 'copy' && (job.status === 'queued' || job.status === 'running')));
    setMoveLoading(transferJobs.some((job) => job.type === 'move' && (job.status === 'queued' || job.status === 'running')));
  }, [transferJobs]);

  useEffect(() => {
    if (activeSidebarTool === 'cloud-sync' || activeSidebarTool === 'network' || activeSidebarTool === 'secure-vault') {
      requestSidebarToolStatus();
    }
  }, [activeSidebarTool]);

  const postToHost = (payload: unknown) => {
    const bridge = window.chrome?.webview;
    if (bridge?.postMessage) {
      bridge.postMessage(payload);
      return true;
    }

    setDirectoryError('Atlas host bridge is unavailable. Open this module inside Atlas Command Center.');
    return false;
  };

  const showToast = (message: string) => {
    setToast(message);
    window.setTimeout(() => {
      setToast((current) => (current === message ? '' : current));
    }, 2800);
  };

  const requestRoots = () => {
    postToHost({ type: 'list-roots' });
  };

  const requestDirectory = (path: string) => {
    const targetPath = path.trim();
    if (!targetPath) {
      setDirectoryError('Folder path is required.');
      return;
    }

    const requestId = `dir-${Date.now()}-${(directoryRequestCounterRef.current += 1)}`;
    activeDirectoryRequestRef.current = { id: requestId, path: targetPath };
    lastRequestedDirectoryPathRef.current = targetPath;

    clearDirectoryLoadTimeout();
    setDirectoryLoading(true);
    setDirectoryLoadingPath(targetPath);
    setDirectoryError('');
    console.debug(`[FileExplorerReact] list-directory request path=${targetPath} requestId=${requestId}`);

    const sent = postToHost({ type: 'list-directory', path: targetPath, directoryPath: targetPath, requestId });
    if (!sent) {
      setDirectoryLoading(false);
      setDirectoryLoadingPath('');
      activeDirectoryRequestRef.current = null;
      setDirectoryError(`Failed to open ${safeDebugPath(targetPath)}.`);
      return;
    }

    directoryLoadTimeoutRef.current = window.setTimeout(() => {
      const active = activeDirectoryRequestRef.current;
      if (!active || active.id !== requestId || !directoryLoadingRef.current) {
        return;
      }

      console.debug(`[FileExplorerReact] directory timeout path=${active.path} requestId=${requestId}`);
      setDirectoryLoading(false);
      setDirectoryError(`Failed to open ${safeDebugPath(active.path)}. Request timed out.`);
      setDirectoryLoadingPath('');
      activeDirectoryRequestRef.current = null;
      directoryLoadTimeoutRef.current = null;
    }, DIRECTORY_LOAD_TIMEOUT_MS);
  };

  const requestPreview = (path: string) => {
    if (!path) {
      setPreview({ ok: false, path, previewKind: 'none', error: 'File path is required.' });
      return;
    }

    setPreviewLoading(true);
    postToHost({ type: 'preview-file', path });
  };

  const requestOpenFile = (path: string) => {
    if (!path) return;
    postToHost({ type: 'open-file', path });
  };

  const requestShowInExplorer = (filePath: string) => {
    if (!filePath) return;

    const matched = items.find((item) => item.path.toLowerCase() === filePath.toLowerCase())
      ?? (selectedFile?.path.toLowerCase() === filePath.toLowerCase() ? selectedFile : null);

    const explorerTarget = matched?.kind === 'folder'
      ? filePath
      : (filePath.includes('\\') ? filePath.substring(0, filePath.lastIndexOf('\\')) : filePath);

    showToast('Opening location...');
    postToHost({ type: 'open-folder-external', path: explorerTarget || filePath });
  };

  const requestCopyBrainName = async (name: string) => {
    if (!name.trim()) return;

    try {
      await navigator.clipboard.writeText(name);
      showToast('Copied name');
    } catch {
      showToast('Copy failed: clipboard is unavailable.');
    }
  };

  const requestSidebarToolStatus = () => {
    setSidebarToolStatusLoading(true);
    setSidebarToolStatusError('');
    if (!postToHost({ type: 'file-explorer-sidebar-tools-status' })) {
      setSidebarToolStatusLoading(false);
      setSidebarToolStatusError('Atlas host bridge is unavailable.');
    }
  };

  const requestOpenNetworkTools = () => {
    postToHost({ type: 'open-network-external' });
  };

  const updateTransferJob = (jobId: string, updater: (job: TransferJob) => TransferJob) => {
    setTransferJobs((current) => current.map((job) => (job.id === jobId ? updater(job) : job)));
  };

  const enqueueTransferJob = (type: 'copy' | 'move', sourcePath: string, destinationDirectoryPath: string) => {
    const source = resolveClipboardItem(sourcePath);
    const jobId = `${type}-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
    const job: TransferJob = {
      id: jobId,
      type,
      sourcePath,
      destinationDirectoryPath,
      itemName: source.name,
      kind: source.kind,
      status: 'queued',
      progressPercent: 0,
      bytesDone: 0,
      bytesTotal: 0,
    };

    setTransferJobs((current) => [job, ...current]);
    setActiveSidebarTool('transfers');

    if (type === 'copy') {
      if (!postToHost({ type: 'transfer-copy', jobId, sourcePath, destinationDirectoryPath })) {
        setTransferJobs((current) => current.map((entry) => entry.id === jobId ? { ...entry, status: 'failed', error: 'Atlas host bridge is unavailable.' } : entry));
      }
    } else {
      if (!postToHost({ type: 'transfer-move', jobId, sourcePath, destinationDirectoryPath })) {
        setTransferJobs((current) => current.map((entry) => entry.id === jobId ? { ...entry, status: 'failed', error: 'Atlas host bridge is unavailable.' } : entry));
      }
    }
  };

  const retryTransferJob = (job: TransferJob) => {
    enqueueTransferJob(job.type, job.sourcePath, job.destinationDirectoryPath);
  };

  const clearCompletedTransfers = () => {
    setTransferJobs((current) => current.filter((job) => job.status !== 'success' && job.status !== 'cancelled'));
  };

  const copyTransferReport = async () => {
    const text = transferJobs.map((job) => `${job.type.toUpperCase()} ${job.itemName} | ${job.status} | ${job.progressPercent}% | ${job.destinationDirectoryPath}${job.error ? ` | ${job.error}` : ''}`).join('\n');
    if (!text) {
      showToast('No transfers to copy.');
      return;
    }

    try {
      await navigator.clipboard.writeText(text);
      showToast('Copied transfer report');
    } catch {
      showToast('Copy failed: clipboard is unavailable.');
    }
  };

  const clearBrainProjectScanTimeout = () => {
    if (brainProjectScanTimeoutRef.current !== null) {
      window.clearTimeout(brainProjectScanTimeoutRef.current);
      brainProjectScanTimeoutRef.current = null;
    }
  };

  const clearBrainActionPlanTimeout = () => {
    if (brainActionPlanTimeoutRef.current !== null) {
      window.clearTimeout(brainActionPlanTimeoutRef.current);
      brainActionPlanTimeoutRef.current = null;
    }
  };

  const clearOrganizePlanTimeout = () => {
    if (organizePlanTimeoutRef.current !== null) {
      window.clearTimeout(organizePlanTimeoutRef.current);
      organizePlanTimeoutRef.current = null;
    }
  };

  const requestOrganizePlan = (path: string, instruction?: string) => {
    if (!path) return;
    clearOrganizePlanTimeout();
    setOrganizePlanLoading(true);
    setOrganizePlan(null);
    setOrganizeExecStatuses({});

    const payload = items.slice(0, 300).map((item) => ({
      name: item.name,
      path: item.path,
      kind: item.kind,
      extension: item.extension,
      size: item.sizeBytes,
      modifiedUtc: item.modifiedUtc,
    }));

    postToHost({
      type: 'atlas-brain-organize-plan',
      path,
      items: payload,
      maxItems: 300,
      instruction: instruction && instruction.trim() ? instruction.trim() : undefined,
    });

    organizePlanTimeoutRef.current = window.setTimeout(() => {
      setOrganizePlanLoading(false);
      setOrganizePlan({ path, error: BRAIN_AI_TIMEOUT_MESSAGE });
      showToast(BRAIN_AI_TIMEOUT_MESSAGE);
      organizePlanTimeoutRef.current = null;
    }, BRAIN_AI_TIMEOUT_MS);
  };

  const runOrganizeActionsSequentially = (actions: OrganizePlanAction[], idx: number, allItems: FileItem[]) => {
    if (idx >= actions.length) {
      setOrganizeIsExecuting(false);
      requestDirectory(currentPathRef.current);
      const successCount = Object.values({}).length;
      showToast(`Organize: ${actions.length} action(s) submitted.`);
      return;
    }

    const action = actions[idx];
    setOrganizeExecStatuses((prev) => ({ ...prev, [String(idx)]: 'running' }));

    if (organizeActionTimeoutRef.current !== null) {
      window.clearTimeout(organizeActionTimeoutRef.current);
    }

    organizeResolveRef.current = (ok: boolean, error?: string) => {
      if (organizeActionTimeoutRef.current !== null) {
        window.clearTimeout(organizeActionTimeoutRef.current);
        organizeActionTimeoutRef.current = null;
      }
      setOrganizeExecStatuses((prev) => ({
        ...prev,
        [String(idx)]: ok ? 'success' : `failed: ${error || 'Unknown error'}`,
      }));
      runOrganizeActionsSequentially(actions, idx + 1, allItems);
    };

    organizeActionTimeoutRef.current = window.setTimeout(() => {
      organizeActionTimeoutRef.current = null;
      if (organizeResolveRef.current) {
        const fn = organizeResolveRef.current;
        organizeResolveRef.current = null;
        fn(false, 'Action timed out');
      }
    }, 15000);

    const sep = '\\';
    if (action.type === 'create-folder') {
      const folderName = (action.targetFolder || action.newName || '').trim();
      if (!folderName) {
        const fn = organizeResolveRef.current!;
        organizeResolveRef.current = null;
        fn(false, 'Folder name is empty');
        return;
      }
      postToHost({ type: 'create-folder', directoryPath: currentPathRef.current, folderName });
    } else if (action.type === 'rename') {
      const item = allItems.find((it) => it.name.toLowerCase() === (action.sourceName || '').toLowerCase());
      if (!item) {
        const fn = organizeResolveRef.current!;
        organizeResolveRef.current = null;
        fn(false, `"${action.sourceName}" not found`);
        return;
      }
      postToHost({ type: 'rename-item', path: item.path, newName: action.newName });
    } else if (action.type === 'move') {
      const item = allItems.find((it) => it.name.toLowerCase() === (action.sourceName || '').toLowerCase());
      if (!item) {
        const fn = organizeResolveRef.current!;
        organizeResolveRef.current = null;
        fn(false, `"${action.sourceName}" not found`);
        return;
      }
      const base = currentPathRef.current.replace(/[/\\]+$/, '');
      const targetDir = `${base}${sep}${action.targetFolder}`;
      postToHost({ type: 'move-item', sourcePath: item.path, destinationDirectoryPath: targetDir });
    } else {
      const fn = organizeResolveRef.current!;
      organizeResolveRef.current = null;
      fn(false, `Unknown action type: ${(action as OrganizePlanAction).type}`);
    }
  };

  const approveOrganizeActions = (approvedActions: OrganizePlanAction[]) => {
    if (approvedActions.length === 0 || !currentPath) return;
    const sorted = [
      ...approvedActions.filter((a) => a.type === 'create-folder'),
      ...approvedActions.filter((a) => a.type === 'rename'),
      ...approvedActions.filter((a) => a.type === 'move'),
    ];
    setOrganizeIsExecuting(true);
    const initial: Record<string, string> = {};
    sorted.forEach((_, i) => { initial[String(i)] = 'pending'; });
    setOrganizeExecStatuses(initial);
    runOrganizeActionsSequentially(sorted, 0, [...items]);
  };

  const requestAISummarize = (filePath: string) => {
    if (!filePath) return;
    setAiSummaryLoading(true);
    setAiSummary(null);
    postToHost({ type: 'ai-summarize-file', path: filePath });
  };

  const requestAIExplain = (filePath: string) => {
    if (!filePath) return;
    setAiExplanationLoading(true);
    setAiExplanation(null);
    postToHost({ type: 'ai-explain-file', path: filePath });
  };

  const requestRenameItem = (itemPath: string, newName: string) => {
    if (!itemPath || !newName) return;
    setRenameLoading(true);
    postToHost({ type: 'rename-item', path: itemPath, newName });
  };

  const requestCreateFile = (directoryPath: string, fileName: string) => {
    if (!directoryPath || !fileName) return;
    setDirectoryError('');
    setPendingSelectionPath('');
    setCreateLoading(true);
    postToHost({ type: 'create-file', directoryPath, fileName });
  };

  const requestCreateFolder = (directoryPath: string, folderName: string) => {
    if (!directoryPath || !folderName) return;
    setDirectoryError('');
    setPendingSelectionPath('');
    setCreateLoading(true);
    postToHost({ type: 'create-folder', directoryPath, folderName });
  };

  const stageClipboardBuffer = (item: { path: string; name: string; kind: 'file' | 'folder' }, mode: 'copy' | 'cut') => {
    setClipboardBuffer({ path: item.path, name: item.name, kind: item.kind, mode });
    showToast(`${mode === 'cut' ? 'Cut' : 'Copied'} to Atlas clipboard: ${item.name}`);
  };

  const resolveClipboardItem = (path: string): Omit<ClipboardBuffer, 'mode'> => {
    const selected = selectedFile && selectedFile.path === path ? selectedFile : null;
    const fromItems = items.find((item) => item.path === path) ?? null;
    const source = selected ?? fromItems;

    if (source) {
      return {
        path: source.path,
        name: source.name,
        kind: source.kind,
      };
    }

    return {
      path,
      name: getLeafName(path),
      kind: 'file',
    };
  };

  const requestCopyItem = (sourcePath: string, destinationDirectoryPath: string) => {
    if (!sourcePath || !destinationDirectoryPath) return;
    stageClipboardBuffer(resolveClipboardItem(sourcePath), 'copy');
    isPasteOperationRef.current = false;
    enqueueTransferJob('copy', sourcePath, destinationDirectoryPath);
  };

  const requestMoveItem = (sourcePath: string, destinationDirectoryPath: string) => {
    if (!sourcePath || !destinationDirectoryPath) return;
    isPasteOperationRef.current = false;
    enqueueTransferJob('move', sourcePath, destinationDirectoryPath);
  };

  const requestPasteHere = (destinationDirectoryPath: string) => {
    if (!clipboardBuffer) {
      showToast('Paste here - copy something first');
      return;
    }

    if (!destinationDirectoryPath) {
      showToast('Paste failed: destination folder is required.');
      return;
    }

    isPasteOperationRef.current = true;
    if (clipboardBuffer.mode === 'cut') {
      enqueueTransferJob('move', clipboardBuffer.path, destinationDirectoryPath);
      return;
    }

    enqueueTransferJob('copy', clipboardBuffer.path, destinationDirectoryPath);
  };

  const requestDeleteItem = (path: string) => {
    if (!path) return;
    setDeleteLoading(true);
    postToHost({ type: 'delete-item', path });
  };

  const requestFolderBrief = (path: string) => {
    if (!path) return;
    setFolderBriefLoading(true);
    setFolderBrief(null);
    postToHost({ type: 'ai-folder-brief', path });
  };

  const requestExtractArchive = (archivePath: string, mode: 'new-folder' | 'here') => {
    if (!archivePath) return;
    const ext = archivePath.toLowerCase().substring(archivePath.lastIndexOf('.'));
    if (ext !== '.zip' && ext !== '.rar' && ext !== '.7z') {
      showToast(`Extract not supported for ${ext || 'this file type'}.`);
      return;
    }
    const parent = archivePath.includes('\\')
      ? archivePath.substring(0, archivePath.lastIndexOf('\\'))
      : currentPath;
    const baseName = archivePath.includes('\\')
      ? archivePath.substring(archivePath.lastIndexOf('\\') + 1)
      : archivePath;
    const nameWithoutExt = baseName.includes('.')
      ? baseName.substring(0, baseName.lastIndexOf('.'))
      : baseName;
    const destinationDirectoryPath = mode === 'new-folder'
      ? `${parent}\\${nameWithoutExt}`
      : parent;
    setExtractLoading(true);
    showToast('Extracting archive...');
    postToHost({ type: 'extract-archive', archivePath, destinationDirectoryPath, mode });
  };

  const requestFolderQuestion = (path: string, question: string) => {
    if (!path || !question.trim()) return;
    setFolderAnswerLoading(true);
    setFolderAnswer(null);
    postToHost({ type: 'ai-folder-question', path, question: question.trim() });
  };

  const requestFolderActions = (path: string) => {
    if (!path) return;
    setFolderActionsLoading(true);
    setFolderActions(null);
    postToHost({ type: 'ai-folder-actions', path });
  };

  const requestSmartRename = (filePath: string, previewText?: string) => {
    if (!filePath) return;
    setSmartRenameLoading(true);
    setSmartRename(null);
    const msg: Record<string, unknown> = { type: 'ai-smart-rename', path: filePath };
    if (previewText) msg.previewText = previewText;
    postToHost(msg);
  };

  const requestAtlasBrainProjectScan = (path: string) => {
    if (!path) return;
    clearBrainProjectScanTimeout();
    setBrainProjectScanLoading(true);
    setBrainProjectScan(null);
    postToHost({ type: 'atlas-brain-project-scan', path });

    brainProjectScanTimeoutRef.current = window.setTimeout(() => {
      setBrainProjectScanLoading(false);
      setBrainProjectScan({ path, error: BRAIN_AI_TIMEOUT_MESSAGE });
      showToast(BRAIN_AI_TIMEOUT_MESSAGE);
      brainProjectScanTimeoutRef.current = null;
    }, BRAIN_AI_TIMEOUT_MS);
  };

  const requestAtlasBrainActionPlan = (path: string, metadata: Record<string, unknown>) => {
    if (!path) return;
    clearBrainActionPlanTimeout();
    setBrainActionPlanLoading(true);
    setBrainActionPlan(null);
    postToHost({ type: 'atlas-brain-action-plan', path, metadata });

    brainActionPlanTimeoutRef.current = window.setTimeout(() => {
      setBrainActionPlanLoading(false);
      setBrainActionPlan({ path, error: BRAIN_AI_TIMEOUT_MESSAGE });
      showToast(BRAIN_AI_TIMEOUT_MESSAGE);
      brainActionPlanTimeoutRef.current = null;
    }, BRAIN_AI_TIMEOUT_MS);
  };

  const triggerInspectorAction = (item: FileItem, kind: 'rename' | 'copy' | 'move') => {
    setSelectedFile(item);
    setShowAIPanel(true);

    if (item.kind === 'file' && item.canPreview) {
      requestPreview(item.path);
    }

    setInspectorAction({ kind, nonce: Date.now() });
  };

  const showItemInList = (itemPath: string) => {
    const target = items.find((item) => item.path.toLowerCase() === itemPath.toLowerCase()) ?? null;
    if (!target) {
      showToast('Item is not in the current folder list.');
      return;
    }

    setSearchQuery('');
    setSelectedFile(target);
    setShowAIPanel(true);
    setInspectorAction(null);
    setShowInListFocusPath(target.path);

    if (target.kind === 'file' && target.canPreview) {
      requestPreview(target.path);
    }

    showToast(`Selected ${target.name}`);

    window.setTimeout(() => {
      setShowInListFocusPath((current) => (current === target.path ? '' : current));
    }, 1200);
  };

  const openBrainPanel = (action: AtlasBrainAction) => {
    setShowBrainPanel(true);
    setSelectedBrainAction(action);
    if (action === 'project-brain' && currentPath) {
      requestAtlasBrainProjectScan(currentPath);
      return;
    }

    if (action === 'organize-folder') {
      setOrganizePlan(null);
      setOrganizeExecStatuses({});
      return;
    }

    if (action === 'action-plan' && currentPath) {
      const topItems = items.slice(0, 100).map((item) => ({
        name: item.name,
        path: item.path,
        kind: item.kind,
        extension: item.extension,
        sizeBytes: item.sizeBytes,
        modifiedUtc: item.modifiedUtc,
      }));

      requestAtlasBrainActionPlan(currentPath, {
        currentPath,
        topItems,
        itemCount: items.length,
      });
    }
  };

  const submitAtlasCommand = (rawCommand?: string) => {
    const command = (rawCommand ?? atlasCommandInput).trim();
    if (!command) return;

    const intent = parseAtlasCommandIntent(command);
    setAtlasCommandInput(command);
    setAtlasCommandLast(command);
    setAtlasCommandIntent(intent);
    setShowBrainPanel(true);
    setSelectedBrainAction('organize-folder');

    if (!currentPath) {
      showToast('Open a folder first.');
      return;
    }

    if (intent === 'unsupported') {
      setOrganizePlanLoading(false);
      setOrganizePlan({ path: currentPath, error: "I can't do that safely yet.", provider: 'local fallback' });
      return;
    }

    requestOrganizePlan(currentPath, command);
  };

  useEffect(() => {
    if (!showBrainPanel) return;

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setShowBrainPanel(false);
      }
    };

    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [showBrainPanel]);

  useEffect(() => {
    const bridge = window.chrome?.webview;
    if (!bridge?.addEventListener) {
      setDirectoryError('Atlas host bridge is unavailable. Open this module inside Atlas Command Center.');
      return;
    }

    const onMessage = (event: { data: unknown }) => {
      const message = event?.data as HostMessage | undefined;
      if (!message || typeof message !== 'object' || !('type' in message)) {
        return;
      }

      if (message.type === 'file-explorer-roots') {
        const incomingRoots = Array.isArray(message.roots) ? message.roots : [];
        setRoots(incomingRoots);
        rootsRef.current = incomingRoots;

        if (!currentPath) {
          const preferred = incomingRoots.find((root) => root.id === 'home')
            ?? incomingRoots.find((root) => root.id === 'documents')
            ?? incomingRoots[0];
          if (preferred?.path) {
            setCurrentRootId(preferred.id);
            requestDirectory(preferred.path);
          }
        }
        return;
      }

      if (message.type === 'file-explorer-directory' || message.type === 'file-explorer-directory-error') {
        const incomingRequestId = typeof message.requestId === 'string' ? message.requestId : '';
        const responsePath = getDirectoryResponsePath(message);
        const activeRequest = activeDirectoryRequestRef.current;
        const activePath = activeRequest?.path ?? '';
        const normalizedCurrentPath = normalizePathForCompare(currentPathRef.current);
        const normalizedResponsePath = normalizePathForCompare(responsePath);
        const normalizedActivePath = normalizePathForCompare(activePath);

        let ignoreReason = '';
        if (incomingRequestId) {
          if (!activeRequest || incomingRequestId !== activeRequest.id) {
            ignoreReason = 'requestId-mismatch';
          }
        } else if (activeRequest) {
          if (!normalizedResponsePath || normalizedResponsePath !== normalizedActivePath) {
            ignoreReason = 'missing-requestId-path-mismatch';
          }
        } else if (normalizedCurrentPath && normalizedResponsePath === normalizedCurrentPath) {
          ignoreReason = '';
        } else {
          ignoreReason = 'missing-requestId-no-active-request';
        }

        if (ignoreReason) {
          console.debug(`[FileExplorerReact] directory response ignored reason=${ignoreReason} path=${responsePath || '(none)'} requestId=${incomingRequestId || '(none)'}`);
          return;
        }

        clearDirectoryLoadTimeout();
        activeDirectoryRequestRef.current = null;
        setDirectoryLoading(false);
        setDirectoryLoadingPath('');
        const nextPath = responsePath;
        const nextItems: FileItem[] = message.type === 'file-explorer-directory' && Array.isArray(message.items)
          ? message.items
          : [];
        const responseError = typeof message.error === 'string' ? message.error.trim() : '';

        if (message.type === 'file-explorer-directory-error' || responseError) {
          const failedPath = nextPath || activePath || currentPathRef.current;
          setDirectoryError(`Failed to open ${safeDebugPath(failedPath)}${responseError ? `: ${responseError}` : '.'}`);
          console.debug(`[FileExplorerReact] directory response accepted path=${failedPath || '(none)'} requestId=${incomingRequestId || '(none)'} count=${items.length}`);
          return;
        }

        setCurrentPath(nextPath);
        setParentPath(typeof message.parentPath === 'string' ? message.parentPath : null);
        setItems(nextItems);
        setDirectoryError('');
        setFolderBrief(null);
        setFolderAnswer(null);
        setFolderActions(null);
        setCollectionFilter(null);
        setBrainProjectScan(null);
        setBrainActionPlan(null);
        setOrganizePlan(null);
        setOrganizeExecStatuses({});
        console.debug(`[FileExplorerReact] directory response accepted path=${nextPath || '(none)'} requestId=${incomingRequestId || '(none)'} count=${nextItems.length}`);

        if (nextPath && rootsRef.current.length > 0) {
          const derived = deriveRootId(nextPath, rootsRef.current);
          if (derived) setCurrentRootId(derived);
        }

        if (selectedFile && nextPath && !selectedFile.path.startsWith(nextPath)) {
          setSelectedFile(null);
          setShowAIPanel(false);
          setPreview(null);
        }

        if (pendingSelectionPath) {
          const matched = nextItems.find((item) => item.path === pendingSelectionPath) ?? null;
          setPendingSelectionPath('');
          if (matched) {
            setSelectedFile(matched);
            setShowAIPanel(true);
          }
        }
        return;
      }

      if (message.type === 'file-explorer-preview') {
        setPreviewLoading(false);
        setPreview({
          ok: Boolean(message.ok),
          path: typeof message.path === 'string' ? message.path : '',
          previewKind: message.previewKind === 'text' || message.previewKind === 'image' ? message.previewKind : 'none',
          text: typeof message.text === 'string' ? message.text : undefined,
          url: typeof message.url === 'string' ? message.url : undefined,
          error: typeof message.error === 'string' ? message.error : undefined,
        });
        return;
      }

      if (message.type === 'file-explorer-open-result') {
        if (!message.ok && message.error) {
          showToast(`Cannot open: ${message.error}`);
        }
        return;
      }

      if (message.type === 'file-explorer-ai-summary') {
        setAiSummaryLoading(false);
        setAiSummary({
          path: typeof message.path === 'string' ? message.path : '',
          summary: message.ok ? message.summary : undefined,
          error: message.error,
          provider: message.provider,
        });
        if (!message.ok && message.error) {
          showToast(`Summary error: ${message.error}`);
        }
        return;
      }

      if (message.type === 'file-explorer-ai-explanation') {
        setAiExplanationLoading(false);
        setAiExplanation({
          path: typeof message.path === 'string' ? message.path : '',
          explanation: message.ok ? message.explanation : undefined,
          error: message.error,
          provider: message.provider,
        });
        if (!message.ok && message.error) {
          showToast(`Explanation error: ${message.error}`);
        }
        return;
      }

      if (message.type === 'file-explorer-rename-result') {
        setRenameLoading(false);

        if (organizeResolveRef.current) {
          const fn = organizeResolveRef.current;
          organizeResolveRef.current = null;
          fn(message.ok === true, typeof message.error === 'string' ? message.error : undefined);
          return;
        }

        if (message.ok) {
          showToast('Renamed successfully');
          setPreview(null);
          setAiSummary(null);
          setAiExplanation(null);

          const nextPath = typeof message.newPath === 'string' ? message.newPath : '';
          const parentFolder = nextPath.includes('\\')
            ? nextPath.substring(0, nextPath.lastIndexOf('\\'))
            : currentPathRef.current;
          requestDirectory(parentFolder || currentPathRef.current);

          setSelectedFile(null);
          setShowAIPanel(false);
          return;
        }

        showToast(`Rename failed: ${message.error || 'Unable to rename item.'}`);
        return;
      }

      if (message.type === 'file-explorer-create-result') {
        setCreateLoading(false);

        if (organizeResolveRef.current) {
          const fn = organizeResolveRef.current;
          organizeResolveRef.current = null;
          fn(message.ok === true, typeof message.error === 'string' ? message.error : undefined);
          return;
        }

        if (message.ok === true && typeof message.path === 'string' && message.path) {
          const createdPath = message.path;
          const createdKind = message.kind === 'folder' ? 'folder' : 'file';
          showToast(`Created ${createdKind}`);
          setDirectoryError('');
          setPendingSelectionPath(createdPath);

          const parentFolder = createdPath.includes('\\')
            ? createdPath.substring(0, createdPath.lastIndexOf('\\'))
            : currentPathRef.current;
          requestDirectory(parentFolder || currentPathRef.current);
          return;
        }

        const createError = message.error || 'Unable to create item.';
        setDirectoryError(createError);
        showToast(`Create failed: ${createError}`);
        return;
      }

      if (message.type === 'file-explorer-copy-result') {
        setCopyLoading(false);

        if (message.ok) {
          showToast(isPasteOperationRef.current ? 'Pasted successfully' : 'Copied successfully');
          isPasteOperationRef.current = false;

          const destinationPath = typeof message.destinationPath === 'string' ? message.destinationPath : '';
          const destinationParent = destinationPath.includes('\\')
            ? destinationPath.substring(0, destinationPath.lastIndexOf('\\'))
            : '';

          if (destinationParent && destinationParent.toLowerCase() === currentPathRef.current.toLowerCase()) {
            requestDirectory(currentPathRef.current);
          }
          return;
        }

        showToast(`Copy failed: ${message.error || 'Unable to copy item.'}`);
        isPasteOperationRef.current = false;
        return;
      }

      if (message.type === 'file-explorer-move-result') {
        setMoveLoading(false);

        if (organizeResolveRef.current) {
          const fn = organizeResolveRef.current;
          organizeResolveRef.current = null;
          fn(message.ok === true, typeof message.error === 'string' ? message.error : undefined);
          return;
        }

        if (message.ok) {
          showToast('Moved successfully');
          requestDirectory(currentPathRef.current);

          const movedSourcePath = typeof message.sourcePath === 'string' ? message.sourcePath : '';
          if (selectedFileRef.current && movedSourcePath && selectedFileRef.current.path.toLowerCase() === movedSourcePath.toLowerCase()) {
            setSelectedFile(null);
            setShowAIPanel(false);
            setPreview(null);
            setAiSummary(null);
            setAiExplanation(null);
          }

          if (clipboardBufferRef.current && clipboardBufferRef.current.mode === 'cut' && movedSourcePath
            && clipboardBufferRef.current.path.toLowerCase() === movedSourcePath.toLowerCase()) {
            setClipboardBuffer(null);
          }
          isPasteOperationRef.current = false;
          return;
        }

        showToast(`Move failed: ${message.error || 'Unable to move item.'}`);
        isPasteOperationRef.current = false;
        return;
      }

      if (message.type === 'file-explorer-delete-result') {
        setDeleteLoading(false);

        if (message.ok) {
          showToast('Moved to Recycle Bin');
          requestDirectory(currentPathRef.current);

          const deletedPath = typeof message.path === 'string' ? message.path : '';
          if (selectedFileRef.current && deletedPath && isSameOrChildPath(selectedFileRef.current.path, deletedPath)) {
            setSelectedFile(null);
            setShowAIPanel(false);
            setPreview(null);
            setAiSummary(null);
            setAiExplanation(null);
          }

          if (clipboardBufferRef.current && deletedPath && isSameOrChildPath(clipboardBufferRef.current.path, deletedPath)) {
            setClipboardBuffer(null);
          }

          setDeleteTarget(null);
          return;
        }

        showToast(`Delete failed: ${message.error || 'Unable to move item to Recycle Bin.'}`);
        setDeleteTarget(null);
      }

      if (message.type === 'file-explorer-folder-brief-result') {
        setFolderBriefLoading(false);
        setFolderBrief({
          path: typeof message.path === 'string' ? message.path : '',
          brief: message.ok ? message.brief : undefined,
          error: message.error,
          provider: message.provider,
        });
        if (!message.ok && message.error) {
          showToast(`Folder Brief error: ${message.error}`);
        }
        return;
      }

      if (message.type === 'file-explorer-folder-question-result') {
        setFolderAnswerLoading(false);
        setFolderAnswer({
          path: typeof message.path === 'string' ? message.path : '',
          answer: message.ok ? message.answer : undefined,
          error: message.error,
          provider: message.provider,
        });
        if (!message.ok && message.error) {
          showToast(`Ask Folder error: ${message.error}`);
        }
        return;
      }

      if (message.type === 'file-explorer-folder-actions-result') {
        setFolderActionsLoading(false);
        setFolderActions({
          path: typeof message.path === 'string' ? message.path : '',
          actions: message.ok ? message.actions : undefined,
          error: message.error,
          provider: message.provider,
        });
        if (!message.ok && message.error) {
          showToast(`Suggest Actions error: ${message.error}`);
        }
        return;
      }

      if (message.type === 'file-explorer-smart-rename-result') {
        setSmartRenameLoading(false);
        setSmartRename({
          path: typeof message.path === 'string' ? message.path : '',
          suggestions: message.ok ? message.suggestions : undefined,
          error: message.error,
          provider: message.provider,
        });
        if (!message.ok && message.error) {
          showToast(`Smart Rename error: ${message.error}`);
        }
        return;
      }

      if (message.type === 'file-explorer-atlas-brain-project-result') {
        clearBrainProjectScanTimeout();
        setBrainProjectScanLoading(false);
        setBrainProjectScan({
          path: typeof message.path === 'string' ? message.path : '',
          entries: message.ok ? (Array.isArray(message.entries) ? message.entries : []) : undefined,
          error: message.error,
        });
        if (!message.ok && message.error) {
          showToast(`Project Brain error: ${message.error}`);
        }
        return;
      }

      if (message.type === 'file-explorer-atlas-brain-action-plan-result') {
        clearBrainActionPlanTimeout();
        setBrainActionPlanLoading(false);
        setBrainActionPlan({
          path: typeof message.path === 'string' ? message.path : '',
          plan: message.ok ? message.plan : undefined,
          error: message.error,
          provider: message.provider,
        });
        if (!message.ok && message.error) {
          showToast(`Action Plan error: ${message.error}`);
        }
        return;
      }

      if (message.type === 'atlas-brain-organize-plan-result') {
        clearOrganizePlanTimeout();
        setOrganizePlanLoading(false);
        setOrganizePlan({
          path: typeof message.path === 'string' ? message.path : '',
          planJson: message.ok ? message.planJson : undefined,
          error: message.error,
          provider: message.provider,
          repaired: Boolean(message.repaired),
        });
        if (!message.ok && message.error) {
          showToast(`Organize Plan error: ${message.error}`);
        }
        return;
      }

      if (message.type === 'file-explorer-transfer-update') {
        const jobId = typeof message.jobId === 'string' ? message.jobId : '';
        if (!jobId) return;

        updateTransferJob(jobId, (job) => {
          const nextStatus = message.status ?? job.status;
          return {
            ...job,
            status: nextStatus,
            progressPercent: typeof message.progressPercent === 'number' ? message.progressPercent : job.progressPercent,
            bytesDone: typeof message.bytesDone === 'number' ? message.bytesDone : job.bytesDone,
            bytesTotal: typeof message.bytesTotal === 'number' ? message.bytesTotal : job.bytesTotal,
            error: typeof message.error === 'string' ? message.error : undefined,
            startedAt: job.startedAt ?? (nextStatus === 'running' ? new Date().toISOString() : job.startedAt),
            finishedAt: nextStatus === 'success' || nextStatus === 'failed' || nextStatus === 'cancelled' ? new Date().toISOString() : job.finishedAt,
          };
        });

        if (message.status === 'success') {
          requestDirectory(currentPathRef.current);
          const matchedJob = transferJobsRef.current.find((job) => job.id === jobId);
          if (matchedJob?.type === 'move' && clipboardBufferRef.current?.path.toLowerCase() === matchedJob.sourcePath.toLowerCase()) {
            setClipboardBuffer(null);
          }
        }

        if (message.status === 'failed' && message.error) {
          showToast(`Transfer failed: ${message.error}`);
        }

        return;
      }

      if (message.type === 'file-explorer-sidebar-tools-status') {
        setSidebarToolStatusLoading(false);
        setSidebarToolStatus({
          cloudFolders: Array.isArray(message.cloudFolders) ? message.cloudFolders : [],
          vaultFolders: Array.isArray(message.vaultFolders) ? message.vaultFolders : [],
          mappedDrives: Array.isArray(message.mappedDrives) ? message.mappedDrives : [],
        });
        setSidebarToolStatusError(typeof message.error === 'string' ? message.error : '');
        return;
      }

      if (message.type === 'file-explorer-network-open-result') {
        if (!message.ok && message.error) {
          showToast(`Network tools error: ${message.error}`);
        }
        return;
      }

      if (message.type === 'file-explorer-extract-result') {
        setExtractLoading(false);
        if (message.ok) {
          const dest = typeof message.destinationPath === 'string' ? message.destinationPath : '';
          showToast(`Extracted to ${dest || 'destination folder'}`);
          requestDirectory(currentPathRef.current);
          if (dest) {
            setPendingSelectionPath(dest);
          }
        } else {
          const err = typeof message.error === 'string' ? message.error : 'Extract failed.';
          showToast(`Extract failed: ${err}`);
        }
        return;
      }

    };

    bridge.addEventListener('message', onMessage);
    requestRoots();

    return () => {
      bridge.removeEventListener?.('message', onMessage);
    };
  }, []);

  useEffect(() => () => {
    clearDirectoryLoadTimeout();
    clearBrainProjectScanTimeout();
    clearBrainActionPlanTimeout();
    clearOrganizePlanTimeout();
    if (organizeActionTimeoutRef.current !== null) window.clearTimeout(organizeActionTimeoutRef.current);
  }, []);

  const breadcrumbs = useMemo(() => {
    if (!currentPath) {
      return [] as Array<{ label: string; path: string }>;
    }

    const normalized = currentPath.replace(/\//g, '\\');
    const parts = normalized.split('\\').filter(Boolean);
    if (parts.length === 0) {
      return [] as Array<{ label: string; path: string }>;
    }

    const crumbs: Array<{ label: string; path: string }> = [];
    let running = '';

    if (/^[a-zA-Z]:$/.test(parts[0])) {
      running = `${parts[0]}\\`;
      crumbs.push({ label: `${parts[0]}\\`, path: running });
      for (let i = 1; i < parts.length; i += 1) {
        running = `${running}${parts[i]}\\`;
        crumbs.push({ label: parts[i], path: running });
      }
      return crumbs;
    }

    running = '';
    for (let i = 0; i < parts.length; i += 1) {
      running = i === 0 ? parts[i] : `${running}\\${parts[i]}`;
      crumbs.push({ label: parts[i], path: running });
    }
    return crumbs;
  }, [currentPath]);

  const filteredItems = useMemo(() => {
    const baseItems = collectionFilter
      ? items.filter((item) => collectionFilter.paths.has(item.path.toLowerCase()))
      : items;

    const query = searchQuery.trim().toLowerCase();
    if (!query) return baseItems;

    return baseItems.filter((item) => {
      const name = item.name.toLowerCase();
      const extension = (item.extension || '').toLowerCase();
      const extensionNoDot = extension.startsWith('.') ? extension.slice(1) : extension;
      const kind = item.kind.toLowerCase();

      return name.includes(query)
        || extension.includes(query)
        || extensionNoDot.includes(query)
        || kind.includes(query);
    });
  }, [items, searchQuery, collectionFilter]);

  useEffect(() => {
    if (!selectedFile) return;
    const stillVisible = filteredItems.some((item) => item.path === selectedFile.path);
    if (stillVisible) return;

    setSelectedFile(null);
    setShowAIPanel(false);
    setPreview(null);
    setAiSummary(null);
    setAiExplanation(null);
  }, [filteredItems, selectedFile]);

  const folderCount = items.filter((item) => item.kind === 'folder').length;
  const fileCount = items.filter((item) => item.kind === 'file').length;
  const activeTransferCount = transferJobs.filter((job) => job.status === 'queued' || job.status === 'running').length;
  const completedTransferCount = transferJobs.filter((job) => job.status === 'success').length;
  const failedTransferCount = transferJobs.filter((job) => job.status === 'failed').length;

  return (
    <div className="size-full bg-[#0a0a0a] text-white flex flex-col overflow-hidden">
      <div className="flex-1 flex flex-col min-h-0">
        <Header
          breadcrumbs={breadcrumbs}
          currentPath={currentPath}
          searchQuery={searchQuery}
          createLoading={createLoading}
          voiceNote={fileExplorerVoiceNote}
          speechVisible={FILE_EXPLORER_SPEECH_WIRED}
          speechEnabled={fileExplorerSpeechEnabled}
          onSearchQueryChange={setSearchQuery}
          onClearSearch={() => setSearchQuery('')}
          onCreateFile={requestCreateFile}
          onCreateFolder={requestCreateFolder}
          onOpenPath={requestDirectory}
          onRefresh={() => {
            const activeTarget = activeDirectoryRequestRef.current?.path
              || lastRequestedDirectoryPathRef.current
              || currentPathRef.current;
            if (activeTarget) requestDirectory(activeTarget);
          }}
          onMicClick={handleFileExplorerMicClick}
          onToggleSpeech={handleFileExplorerSpeechToggle}
          onOpenAtlasCommand={() => {
            setShowBrainPanel(true);
            setSelectedBrainAction('organize-folder');
          }}
          onToggleBrainPanel={setShowBrainPanel}
        />

        <div className="flex-1 flex min-h-0">
          <Sidebar
            roots={roots}
            currentRootId={currentRootId}
            activeTool={activeSidebarTool}
            onOpenRoot={(root) => {
              setActiveSidebarTool(null);
              setCurrentRootId(root.id);
              requestDirectory(root.path);
            }}
            onOpenTool={(tool) => setActiveSidebarTool(tool)}
            activeTransferCount={activeTransferCount}
          />

          <div className="flex-1 flex flex-col min-h-0 p-3 gap-3">
            {activeSidebarTool && (
              <SidebarToolPanel
                activeTool={activeSidebarTool}
                currentPath={currentPath}
                items={items}
                transferJobs={transferJobs}
                sidebarToolStatus={sidebarToolStatus}
                sidebarToolStatusLoading={sidebarToolStatusLoading}
                sidebarToolStatusError={sidebarToolStatusError}
                onClose={() => setActiveSidebarTool(null)}
                onOpenFolder={requestDirectory}
                onShowInExplorer={requestShowInExplorer}
                onOpenNetworkTools={requestOpenNetworkTools}
                onRetryTransfer={retryTransferJob}
                onClearCompletedTransfers={clearCompletedTransfers}
                onCopyTransferReport={copyTransferReport}
              />
            )}
            <div className="flex items-center gap-2 px-2 py-1 text-xs">
              <span className="h-6 px-2.5 rounded-full border border-white/12 bg-white/5 text-white/75">Folders: {folderCount}</span>
              <span className="h-6 px-2.5 rounded-full border border-white/12 bg-white/5 text-white/75">Files: {fileCount}</span>
              <span className="h-6 px-2.5 rounded-full border border-white/12 bg-white/5 text-white/75">Filter: {collectionFilter?.label ?? 'None'}</span>
              <span className="h-6 px-2.5 rounded-full border border-white/12 bg-white/5 text-white/75 truncate max-w-[280px]">Selected: {selectedFile?.name ?? 'None'}</span>
            </div>

            <div className="flex-1 min-h-0">
              <FileGrid
                items={filteredItems}
                totalItems={items.length}
                searchQuery={searchQuery}
                currentPath={currentPath}
                parentPath={parentPath}
                isLoading={directoryLoading}
                error={directoryError}
                selectedFile={selectedFile}
                onOpenParent={() => parentPath && requestDirectory(parentPath)}
                onOpenFolder={(path) => requestDirectory(path)}
                onOpenFile={requestOpenFile}
                onPreviewFile={requestPreview}
                onShowInExplorer={requestShowInExplorer}
                onRequestRename={(item) => triggerInspectorAction(item, 'rename')}
                onCopyToClipboard={(item) => stageClipboardBuffer(item, 'copy')}
                onCutToClipboard={(item) => stageClipboardBuffer(item, 'cut')}
                onPasteHere={requestPasteHere}
                onRequestDelete={(item) => setDeleteTarget(item)}
                onExtractArchive={requestExtractArchive}
                extractLoading={extractLoading}
                clipboardBuffer={clipboardBuffer}
                focusPath={showInListFocusPath}
                loadingPath={directoryLoadingPath}
                onRefresh={() => {
                  const activeTarget = activeDirectoryRequestRef.current?.path
                    || lastRequestedDirectoryPathRef.current
                    || currentPathRef.current;
                  if (activeTarget) requestDirectory(activeTarget);
                }}
                onContextCreate={(mode) => setContextCreateMode(mode)}
                setSelectedFile={(file) => {
                  setSelectedFile(file);
                  setShowAIPanel(!!file);
                  setInspectorAction(null);
                  setPreview(null);
                  if (file && file.kind === 'file' && file.canPreview) {
                    requestPreview(file.path);
                  }
                }}
              />
            </div>
          </div>

          {showBrainPanel && (
            <div className="fixed inset-0 z-[10035] bg-black/45 backdrop-blur-[1px]" onClick={() => setShowBrainPanel(false)}>
              <aside
                className="absolute right-0 top-0 h-full w-[520px] max-w-[90vw] border-l border-white/10 bg-[#0b0b0d] p-4 overflow-y-auto shadow-2xl shadow-black/80 [&::-webkit-scrollbar]:w-1 [&::-webkit-scrollbar-track]:bg-transparent [&::-webkit-scrollbar-thumb]:bg-white/10 [&::-webkit-scrollbar-thumb]:rounded-full"
                onClick={(event) => event.stopPropagation()}
              >
                <div className="flex items-start justify-between gap-3 mb-3">
                  <div>
                    <div className="text-base text-white/95 font-semibold">Atlas Command</div>
                    <div className="text-xs text-white/55 mt-0.5">Tell Atlas what to do with this folder. Nothing changes until you approve.</div>
                  </div>
                  <button
                    onClick={() => setShowBrainPanel(false)}
                    className="h-7 px-2.5 rounded-md bg-white/5 border border-white/10 text-xs text-white/65 hover:bg-white/10"
                  >
                    Close
                  </button>
                </div>

                <SmartCards
                  folderCount={folderCount}
                  fileCount={fileCount}
                  currentPath={currentPath}
                  items={items}
                  selectedBrainAction={selectedBrainAction}
                  collectionFilterLabel={collectionFilter?.label ?? ''}
                  folderBrief={folderBrief}
                  folderBriefLoading={folderBriefLoading}
                  onFolderBrief={() => currentPath && requestFolderBrief(currentPath)}
                  folderAnswer={folderAnswer}
                  folderAnswerLoading={folderAnswerLoading}
                  onFolderQuestion={(q) => currentPath && requestFolderQuestion(currentPath, q)}
                  folderActions={folderActions}
                  folderActionsLoading={folderActionsLoading}
                  onSuggestActions={() => currentPath && requestFolderActions(currentPath)}
                  brainProjectScan={brainProjectScan}
                  brainProjectScanLoading={brainProjectScanLoading}
                  onRunProjectScan={() => currentPath && requestAtlasBrainProjectScan(currentPath)}
                  brainActionPlan={brainActionPlan}
                  brainActionPlanLoading={brainActionPlanLoading}
                  onRunActionPlan={(metadata) => currentPath && requestAtlasBrainActionPlan(currentPath, metadata)}
                  onShowInList={showItemInList}
                  onOpenLocation={(path) => requestShowInExplorer(path)}
                  onCopyName={requestCopyBrainName}
                  onApplySearch={(query) => setSearchQuery(query)}
                  onApplyCollectionFilter={(label, paths) => {
                    setCollectionFilter({
                      label,
                      paths: new Set(paths.map((p) => p.toLowerCase())),
                    });
                  }}
                  onClearCollectionFilter={() => setCollectionFilter(null)}
                  organizePlan={organizePlan}
                  organizePlanLoading={organizePlanLoading}
                  organizeExecStatuses={organizeExecStatuses}
                  organizeIsExecuting={organizeIsExecuting}
                  onRunOrganizePlan={(instruction) => currentPath && requestOrganizePlan(currentPath, instruction)}
                  onApproveOrganizeActions={approveOrganizeActions}
                  atlasCommand={atlasCommandLast}
                  atlasCommandIntent={atlasCommandIntent}
                  onRunAtlasCommand={(command) => submitAtlasCommand(command)}
                  onExtractArchive={requestExtractArchive}
                />
              </aside>
            </div>
          )}

          {showAIPanel && selectedFile && (
            <AIInspector
              fileItem={selectedFile}
              preview={preview}
              loading={previewLoading}
              onPreview={() => requestPreview(selectedFile.path)}
              onOpenFile={requestOpenFile}
              onShowInExplorer={requestShowInExplorer}
              onAISummarize={requestAISummarize}
              onAIExplain={requestAIExplain}
              onRenameItem={requestRenameItem}
              onCopyItem={requestCopyItem}
              onMoveItem={requestMoveItem}
              onRequestDelete={(item) => setDeleteTarget(item)}
              onRequestSmartRename={(item, previewText) => requestSmartRename(item.path, previewText)}
              onExtractArchive={requestExtractArchive}
              extractLoading={extractLoading}
              externalAction={inspectorAction}
              renameLoading={renameLoading}
              copyLoading={copyLoading}
              moveLoading={moveLoading}
              aiSummary={aiSummary}
              aiSummaryLoading={aiSummaryLoading}
              aiExplanation={aiExplanation}
              aiExplanationLoading={aiExplanationLoading}
              smartRename={smartRename}
              smartRenameLoading={smartRenameLoading}
              onUnavailable={() => showToast(NOT_WIRED_MESSAGE)}
              onClose={() => {
                setShowAIPanel(false);
                setSelectedFile(null);
                setPreview(null);
                setAiSummary(null);
                setAiExplanation(null);
                setSmartRename(null);
              }}
            />
          )}
        </div>

        <StatusBar
          currentPath={currentPath}
          folderCount={folderCount}
          fileCount={fileCount}
          clipboardBuffer={clipboardBuffer}
          activeTransferCount={activeTransferCount}
          completedTransferCount={completedTransferCount}
          failedTransferCount={failedTransferCount}
          onOpenTransfers={() => setActiveSidebarTool('transfers')}
        />
      </div>

      {toast && (
        <div className="absolute bottom-5 right-5 px-4 py-2 rounded-lg border border-cyan-400/30 bg-black/85 text-sm text-cyan-200 shadow-lg shadow-cyan-500/20">
          {toast}
        </div>
      )}

      {clipboardBuffer && (
        <div className="absolute bottom-5 left-5 z-[10030] max-w-[420px] px-3 py-2 rounded-lg border border-cyan-400/25 bg-black/85 text-xs text-cyan-200 shadow-lg shadow-cyan-500/20 flex items-center gap-3">
          <span className="truncate">Atlas clipboard: {clipboardBuffer.name} ({clipboardBuffer.mode === 'cut' ? 'cut' : 'copy'})</span>
          <button
            onClick={() => setClipboardBuffer(null)}
            className="h-6 px-2 rounded-md border border-white/15 bg-white/5 text-[11px] text-white/75 hover:bg-white/10"
          >
            Clear
          </button>
        </div>
      )}

      {contextCreateMode && (
        <div className="fixed inset-0 z-[10020] flex items-center justify-center bg-black/55 backdrop-blur-sm px-4">
          <div className="w-full max-w-[360px] rounded-2xl border border-white/10 bg-[#101010] shadow-2xl shadow-black/60 p-4 space-y-3">
            <div>
              <div className="text-sm font-medium text-white">{contextCreateMode === 'file' ? 'New text file' : 'New folder'}</div>
              <div className="text-xs text-white/45 mt-1 truncate" title={currentPath || 'No folder selected'}>{currentPath || 'No folder selected'}</div>
            </div>

            <input
              type="text"
              value={contextCreateName}
              onChange={(event) => setContextCreateName(event.target.value)}
              className="w-full h-10 px-3 bg-white/5 border border-white/10 rounded-lg text-sm text-white placeholder:text-white/35 focus:outline-none focus:border-cyan-400/50 focus:ring-2 focus:ring-cyan-400/20"
              placeholder={contextCreateMode === 'file' ? 'atlas_context.txt' : 'New Folder'}
            />

            {contextCreateValidationError && (
              <div className="text-xs text-amber-200 bg-amber-500/10 border border-amber-300/25 rounded-lg p-2.5">
                {contextCreateValidationError}
              </div>
            )}

            <div className="flex items-center justify-end gap-2">
              <button
                onClick={() => setContextCreateMode(null)}
                className="h-8 px-3 rounded-lg bg-white/5 border border-white/10 text-xs text-white/70 hover:bg-white/10"
              >
                Cancel
              </button>
              <button
                onClick={() => {
                  const trimmedName = contextCreateName.trim();
                  if (contextCreateMode === 'file') {
                    requestCreateFile(currentPathRef.current, trimmedName);
                  } else {
                    requestCreateFolder(currentPathRef.current, trimmedName);
                  }
                  setContextCreateMode(null);
                }}
                disabled={!currentPathRef.current || !!contextCreateValidationError || createLoading}
                className="h-8 px-3 rounded-lg bg-cyan-500/20 border border-cyan-400/30 text-xs text-cyan-200 hover:bg-cyan-500/30 disabled:opacity-40 disabled:cursor-not-allowed"
              >
                {createLoading ? 'Creating...' : 'Create'}
              </button>
            </div>
          </div>
        </div>
      )}

      {deleteTarget && (
        <div className="fixed inset-0 z-[10025] flex items-center justify-center bg-black/55 backdrop-blur-sm px-4">
          <div className="w-full max-w-[520px] rounded-2xl border border-white/10 bg-[#101010] shadow-2xl shadow-black/60 p-4 space-y-3">
            <div>
              <div className="text-sm font-medium text-white">Move to Recycle Bin?</div>
              <div className="text-xs text-white/60 mt-1">
                This moves the selected item to the Windows Recycle Bin. It is not permanently deleted.
              </div>
            </div>

            <div className="rounded-lg border border-white/10 bg-white/[0.03] p-3 space-y-1.5 text-xs text-white/75">
              <div><span className="text-white/45">Item:</span> {deleteTarget.name}</div>
              <div><span className="text-white/45">Type:</span> {deleteTarget.kind}</div>
              <div className="break-all"><span className="text-white/45">Path:</span> {deleteTarget.path}</div>
            </div>

            <div className="flex items-center justify-end gap-2">
              <button
                onClick={() => {
                  if (!deleteLoading) setDeleteTarget(null);
                }}
                disabled={deleteLoading}
                className="h-8 px-3 rounded-lg bg-white/5 border border-white/10 text-xs text-white/70 hover:bg-white/10 disabled:opacity-40 disabled:cursor-not-allowed"
              >
                Cancel
              </button>
              <button
                onClick={() => requestDeleteItem(deleteTarget.path)}
                disabled={deleteLoading}
                className="h-8 px-3 rounded-lg bg-rose-500/20 border border-rose-400/30 text-xs text-rose-200 hover:bg-rose-500/30 disabled:opacity-40 disabled:cursor-not-allowed"
              >
                {deleteLoading ? 'Moving...' : 'Move to Recycle Bin'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

function SidebarToolPanel({
  activeTool,
  currentPath,
  items,
  transferJobs,
  sidebarToolStatus,
  sidebarToolStatusLoading,
  sidebarToolStatusError,
  onClose,
  onOpenFolder,
  onShowInExplorer,
  onOpenNetworkTools,
  onRetryTransfer,
  onClearCompletedTransfers,
  onCopyTransferReport,
}: {
  activeTool: SidebarTool;
  currentPath: string;
  items: FileItem[];
  transferJobs: TransferJob[];
  sidebarToolStatus: SidebarToolStatus;
  sidebarToolStatusLoading: boolean;
  sidebarToolStatusError: string;
  onClose: () => void;
  onOpenFolder: (path: string) => void;
  onShowInExplorer: (path: string) => void;
  onOpenNetworkTools: () => void;
  onRetryTransfer: (job: TransferJob) => void;
  onClearCompletedTransfers: () => void;
  onCopyTransferReport: () => void;
}) {
  const activeTransfers = transferJobs.filter((job) => job.status === 'queued' || job.status === 'running');
  const completedTransfers = transferJobs.filter((job) => job.status === 'success' || job.status === 'cancelled');
  const failedTransfers = transferJobs.filter((job) => job.status === 'failed');
  const syncScan = scanCurrentFolderForSync(items);
  const sensitiveItems = scanCurrentFolderForSensitiveNames(items);

  const title = activeTool === 'transfers'
    ? 'Transfers'
    : activeTool === 'cloud-sync'
      ? 'Cloud Sync'
      : activeTool === 'network'
        ? 'Network'
        : 'Secure Vault';

  const subtitle = activeTool === 'transfers'
    ? 'Futuristic transfer manager with queue, history, and safe progress tracking.'
    : activeTool === 'cloud-sync'
      ? 'Cloud sync is not connected yet.'
      : activeTool === 'network'
        ? 'Network locations are not wired yet.'
        : 'Secure Vault not configured.';

  return (
    <section className="rounded-2xl border border-cyan-400/18 bg-[radial-gradient(circle_at_top_left,rgba(34,211,238,0.12),transparent_38%),linear-gradient(180deg,rgba(16,16,20,0.96),rgba(11,11,15,0.98))] shadow-[0_0_40px_rgba(34,211,238,0.08)] p-4 space-y-4">
      <div className="flex items-start justify-between gap-4">
        <div>
          <div className="text-lg text-white font-semibold">{title}</div>
          <div className="text-xs text-white/55 mt-1 max-w-3xl">{subtitle}</div>
          <div className="text-[11px] text-white/35 mt-1 break-all">{currentPath || 'No folder selected'}</div>
        </div>
        <button onClick={onClose} className="h-8 px-3 rounded-lg border border-white/10 bg-white/5 text-xs text-white/70 hover:bg-white/10">Close</button>
      </div>

      {activeTool === 'transfers' && (
        <>
          <div className="flex items-center gap-2 text-[11px] text-cyan-100">
            <span className="px-2 py-1 rounded-full border border-cyan-400/25 bg-cyan-500/10">Safe transfer mode</span>
            <span className="px-2 py-1 rounded-full border border-white/10 bg-white/5 text-white/65">Progress estimate unavailable when Windows fast-move is used</span>
          </div>

          <div className="flex items-center gap-2">
            <button onClick={onClearCompletedTransfers} className="h-8 px-3 rounded-lg border border-white/10 bg-white/5 text-xs text-white/75 hover:bg-white/10">Clear completed</button>
            <button onClick={onCopyTransferReport} className="h-8 px-3 rounded-lg border border-white/10 bg-white/5 text-xs text-white/75 hover:bg-white/10">Copy report</button>
          </div>

          {transferJobs.length === 0 && (
            <div className="rounded-xl border border-white/10 bg-white/[0.03] p-6 text-center space-y-2">
              <div className="text-base text-white/90">No transfers yet</div>
              <div className="text-xs text-white/50">Copy, paste, or move files and Atlas will track them here.</div>
            </div>
          )}

          {activeTransfers.length > 0 && (
            <TransferSection title="Active queue" jobs={activeTransfers} onOpenFolder={onOpenFolder} onShowInExplorer={onShowInExplorer} />
          )}
          {failedTransfers.length > 0 && (
            <TransferSection title="Failed jobs" jobs={failedTransfers} onOpenFolder={onOpenFolder} onShowInExplorer={onShowInExplorer} onRetryTransfer={onRetryTransfer} />
          )}
          {completedTransfers.length > 0 && (
            <TransferSection title="Completed history" jobs={completedTransfers} onOpenFolder={onOpenFolder} onShowInExplorer={onShowInExplorer} />
          )}
        </>
      )}

      {activeTool === 'cloud-sync' && (
        <>
          <HonestStatusRow loading={sidebarToolStatusLoading} error={sidebarToolStatusError} honestText="No fake sync status. Atlas only checks common local cloud folders." />
          <ToolFolderSection title="Detected local cloud folders" folders={sidebarToolStatus.cloudFolders} onOpenFolder={onOpenFolder} onShowInExplorer={onShowInExplorer} />
          <div className="rounded-xl border border-white/10 bg-white/[0.03] p-3 space-y-2">
            <div className="text-sm text-white/85">Current folder sync scan</div>
            <div className="text-xs text-white/55">Metadata only. Large files, installers, archives, and obvious secrets are flagged as risky to sync broadly.</div>
            <SimpleScanGroup title="Looks cloud-safe" items={syncScan.safe} />
            <SimpleScanGroup title="Review before sync" items={syncScan.risky} />
          </div>
          <div className="rounded-xl border border-white/10 bg-white/[0.03] p-3 text-xs text-white/60">
            Connect cloud later: wire a real provider bridge, token flow, and conflict rules before showing sync state.
          </div>
        </>
      )}

      {activeTool === 'network' && (
        <>
          <HonestStatusRow loading={sidebarToolStatusLoading} error={sidebarToolStatusError} honestText="SMB and network-share browsing are planned, but not wired into Atlas yet." />
          <div className="flex items-center gap-2">
            <button onClick={onOpenNetworkTools} className="h-8 px-3 rounded-lg border border-white/10 bg-white/5 text-xs text-white/75 hover:bg-white/10">Open Windows Network</button>
          </div>
          <ToolFolderSection title="Mapped drives" folders={sidebarToolStatus.mappedDrives.map((drive) => ({ label: drive.label, path: drive.path, exists: true }))} onOpenFolder={onOpenFolder} onShowInExplorer={onShowInExplorer} emptyLabel="No mapped network drives detected." />
        </>
      )}

      {activeTool === 'secure-vault' && (
        <>
          <HonestStatusRow loading={sidebarToolStatusLoading} error={sidebarToolStatusError} honestText="No encryption claims. Atlas only surfaces likely local Secrets folders and metadata-based sensitive-name warnings." />
          <ToolFolderSection title="Detected Secrets folders" folders={sidebarToolStatus.vaultFolders} onOpenFolder={onOpenFolder} onShowInExplorer={onShowInExplorer} />
          <div className="rounded-xl border border-white/10 bg-white/[0.03] p-3 space-y-2">
            <div className="text-sm text-white/85">Sensitive name scan</div>
            <div className="text-xs text-white/55">Current folder only. No file contents were read.</div>
            <SimpleScanGroup title="Potentially sensitive" items={sensitiveItems} />
          </div>
          <div className="rounded-xl border border-white/10 bg-white/[0.03] p-3 text-xs text-white/60">
            Safe storage rules: keep secrets out of public share folders, avoid renaming tokens into ordinary names, and review vault destinations manually before moving anything.
          </div>
        </>
      )}
    </section>
  );
}

function HonestStatusRow({ loading, error, honestText }: { loading: boolean; error: string; honestText: string }) {
  return (
    <div className="rounded-xl border border-white/10 bg-white/[0.03] p-3 text-xs space-y-1">
      <div className="text-white/85">{loading ? 'Checking local status...' : honestText}</div>
      {error && <div className="text-amber-200">{error}</div>}
    </div>
  );
}

function ToolFolderSection({
  title,
  folders,
  onOpenFolder,
  onShowInExplorer,
  emptyLabel,
}: {
  title: string;
  folders: ToolFolderStatus[];
  onOpenFolder: (path: string) => void;
  onShowInExplorer: (path: string) => void;
  emptyLabel?: string;
}) {
  return (
    <div className="space-y-2">
      <div className="text-sm text-white/85">{title}</div>
      {folders.length === 0 && <div className="text-xs text-white/45">{emptyLabel || 'No folders detected.'}</div>}
      {folders.map((folder) => (
        <div key={`${folder.label}-${folder.path}`} className="rounded-xl border border-white/10 bg-white/[0.03] p-3 flex items-center gap-3">
          <div className="min-w-0 flex-1">
            <div className="text-sm text-white/85">{folder.label}</div>
            <div className="text-[11px] text-white/45 break-all">{folder.path}</div>
          </div>
          <span className={`h-6 px-2 rounded-full border text-[10px] inline-flex items-center ${folder.exists ? 'border-emerald-400/25 bg-emerald-500/10 text-emerald-200' : 'border-white/10 bg-white/5 text-white/45'}`}>
            {folder.exists ? 'Found' : 'Not found'}
          </span>
          {folder.exists && (
            <>
              <button onClick={() => onOpenFolder(folder.path)} className="h-8 px-3 rounded-lg border border-white/10 bg-white/5 text-xs text-white/75 hover:bg-white/10">Open folder</button>
              <button onClick={() => onShowInExplorer(folder.path)} className="h-8 px-3 rounded-lg border border-white/10 bg-white/5 text-xs text-white/75 hover:bg-white/10">Show in Explorer</button>
            </>
          )}
        </div>
      ))}
    </div>
  );
}

function TransferSection({
  title,
  jobs,
  onOpenFolder,
  onShowInExplorer,
  onRetryTransfer,
}: {
  title: string;
  jobs: TransferJob[];
  onOpenFolder: (path: string) => void;
  onShowInExplorer: (path: string) => void;
  onRetryTransfer?: (job: TransferJob) => void;
}) {
  return (
    <div className="space-y-2">
      <div className="text-sm text-white/85">{title}</div>
      {jobs.map((job) => (
        <div key={job.id} className="rounded-2xl border border-white/10 bg-[linear-gradient(135deg,rgba(255,255,255,0.05),rgba(255,255,255,0.02))] p-3 space-y-3">
          <div className="flex items-center gap-3">
            <div className="min-w-0 flex-1">
              <div className="text-sm text-white truncate">{job.itemName}</div>
              <div className="text-[11px] text-white/45 break-all">{job.destinationDirectoryPath}</div>
            </div>
            <span className={`h-6 px-2 rounded-full border text-[10px] inline-flex items-center ${transferStatusClass(job.status)}`}>{job.status}</span>
          </div>
          <div className="space-y-1">
            <div className="h-2 rounded-full bg-white/8 overflow-hidden">
              <div className="h-full rounded-full bg-gradient-to-r from-cyan-400 via-sky-400 to-emerald-300 transition-all duration-300" style={{ width: `${Math.max(4, job.progressPercent)}%` }} />
            </div>
            <div className="flex items-center justify-between text-[11px] text-white/45">
              <span>{job.type.toUpperCase()} · {job.kind}</span>
              <span>{job.progressPercent}%</span>
            </div>
            <div className="text-[11px] text-white/35">{job.bytesTotal > 0 ? `${formatTransferBytes(job.bytesDone)} / ${formatTransferBytes(job.bytesTotal)}` : 'Progress estimate unavailable'}</div>
            {job.error && <div className="text-[11px] text-amber-200">{job.error}</div>}
          </div>
          <div className="flex items-center gap-2">
            <button onClick={() => onOpenFolder(job.destinationDirectoryPath)} className="h-8 px-3 rounded-lg border border-white/10 bg-white/5 text-xs text-white/75 hover:bg-white/10">Open destination</button>
            <button onClick={() => onShowInExplorer(job.destinationDirectoryPath)} className="h-8 px-3 rounded-lg border border-white/10 bg-white/5 text-xs text-white/75 hover:bg-white/10">Show in Explorer</button>
            {job.status === 'failed' && onRetryTransfer && (
              <button onClick={() => onRetryTransfer(job)} className="h-8 px-3 rounded-lg border border-cyan-400/30 bg-cyan-500/10 text-xs text-cyan-200 hover:bg-cyan-500/20">Retry failed</button>
            )}
          </div>
        </div>
      ))}
    </div>
  );
}

function SimpleScanGroup({ title, items }: { title: string; items: FileItem[] }) {
  return (
    <div className="space-y-1.5">
      <div className="text-xs text-white/70">{title}</div>
      {items.length === 0 ? (
        <div className="text-xs text-white/45">None.</div>
      ) : (
        items.slice(0, 12).map((item) => (
          <div key={`${title}-${item.path}`} className="text-xs text-white/60 break-all">{item.name}</div>
        ))
      )}
    </div>
  );
}

function scanCurrentFolderForSensitiveNames(items: FileItem[]): FileItem[] {
  const riskyTerms = ['secret', 'token', 'credential', 'private', 'password', 'client_secret', '.env', 'apikey'];
  return items.filter((item) => riskyTerms.some((term) => item.name.toLowerCase().includes(term)));
}

function scanCurrentFolderForSync(items: FileItem[]): { safe: FileItem[]; risky: FileItem[] } {
  const riskyExtensions = new Set(['.exe', '.msi', '.zip', '.rar', '.7z', '.iso', '.dll', '.pfx', '.key', '.pem']);
  const risky = items.filter((item) => {
    const lower = item.name.toLowerCase();
    return riskyExtensions.has(item.extension.toLowerCase())
      || lower.includes('secret')
      || lower.includes('token')
      || lower.includes('password')
      || (item.sizeBytes ?? 0) > 250 * 1024 * 1024;
  });
  const riskyPaths = new Set(risky.map((item) => item.path));
  const safe = items.filter((item) => !riskyPaths.has(item.path)).slice(0, 12);
  return { safe, risky: risky.slice(0, 12) };
}

function transferStatusClass(status: TransferJob['status']): string {
  if (status === 'success') return 'border-emerald-400/25 bg-emerald-500/10 text-emerald-200';
  if (status === 'failed') return 'border-amber-400/25 bg-amber-500/10 text-amber-200';
  if (status === 'running') return 'border-cyan-400/25 bg-cyan-500/10 text-cyan-200';
  if (status === 'queued') return 'border-white/10 bg-white/5 text-white/65';
  return 'border-white/10 bg-white/5 text-white/45';
}

function formatTransferBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`;
}

function getLeafName(path: string): string {
  if (!path) return 'item';
  const normalized = path.replace(/[\\/]+$/g, '');
  const lastSlash = Math.max(normalized.lastIndexOf('\\'), normalized.lastIndexOf('/'));
  return lastSlash >= 0 ? normalized.slice(lastSlash + 1) : normalized;
}

function deriveRootId(path: string, rootList: ExplorerRoot[]): string {
  if (!path || rootList.length === 0) return '';
  const norm = (s: string) => s.replace(/\//g, '\\').replace(/\\+$/, '').toLowerCase();
  const normPath = norm(path);
  let bestId = '';
  let bestLen = -1;
  for (const root of rootList) {
    const normRoot = norm(root.path);
    if (normPath === normRoot || normPath.startsWith(normRoot + '\\')) {
      if (normRoot.length > bestLen) {
        bestLen = normRoot.length;
        bestId = root.id;
      }
    }
  }
  return bestId;
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

function isSameOrChildPath(candidatePath: string, parentPath: string): boolean {
  const normalize = (value: string) => value.replace(/\//g, '\\').replace(/[\\/]+$/g, '').toLowerCase();
  const candidate = normalize(candidatePath);
  const parent = normalize(parentPath);

  if (!candidate || !parent) return false;
  if (candidate === parent) return true;
  return candidate.startsWith(parent + '\\');
}

function normalizePathForCompare(path: string): string {
  if (!path) return '';
  return path.replace(/\//g, '\\').replace(/[\\/]+$/g, '').trim().toLowerCase();
}

function getDirectoryResponsePath(message: { path?: string; directoryPath?: string }): string {
  if (typeof message.path === 'string' && message.path.trim()) {
    return message.path;
  }

  if (typeof message.directoryPath === 'string' && message.directoryPath.trim()) {
    return message.directoryPath;
  }

  return '';
}

function parseAtlasCommandIntent(command: string): AtlasCommandIntent {
  const text = command.toLowerCase();

  if (/delete|wipe|format|remove everything|erase drive/.test(text)) return 'unsupported';

  if (/organize|sort|tidy|clean/.test(text)) return 'organize';
  if (/installer|\.exe|\.msi/.test(text)) return 'group-installers';
  if (/archive|zip|rar|7z|extract/.test(text)) return 'archives';
  if (/secret|token|key|credential|password|env/.test(text)) return 'sensitive-review';
  if (/duplicate|similar/.test(text)) return 'duplicates';
  if (/backup|share|send/.test(text)) return 'safe-share';
  if (/rename|messy/.test(text)) return 'rename-suggestions';
  if (/recent|changed/.test(text)) return 'recent';
  if (/project|build|code/.test(text)) return 'project-review';

  return 'organize';
}

function safeDebugPath(path: string): string {
  if (!path) return '(unknown path)';
  const trimmed = path.trim();
  if (!trimmed) return '(unknown path)';
  if (trimmed.length <= 100) return `"${trimmed}"`;
  return `"${trimmed.slice(0, 97)}..."`;
}