import { ReactNode, useEffect, useMemo, useState } from 'react';
import {
  AlertCircle,
  Brain,
  CheckCircle2,
  Info,
  ListFilter,
  Shield,
  Sparkles,
  Wrench,
} from 'lucide-react';
import { BrainScanEntry, FileItem, OrganizePlanAction } from '../App';
import { AtlasBrainAction } from './Header';

type BrainCollection = {
  id: string;
  title: string;
  items: FileItem[];
  suggestedAction: string;
  filterQuery: string;
};

type BrainProjectInfo = {
  projectType: string;
  confidence: 'high' | 'medium' | 'low';
  isAtlasWorkspace: boolean;
  likelyEntryFiles: string[];
  buildRunCommands: string[];
  avoidTouching: string[];
  generatedFolders: string[];
  importantFiles: string[];
  nextActions: string[];
};

type ShareCheckResult = {
  doNotShare: FileItem[];
  reviewBeforeSharing: FileItem[];
  safeToInclude: FileItem[];
  excludePatterns: string[];
};

type CleanupGroups = {
  oldInstallers: FileItem[];
  duplicateLikeDownloads: FileItem[];
  largeArchives: FileItem[];
  tempBuildFolders: FileItem[];
  oldScreenshotsVideos: FileItem[];
  sensitiveMoveToSecrets: FileItem[];
  keepList: FileItem[];
};

type RecentResult = {
  changedToday: FileItem[];
  changedWeek: FileItem[];
  newestFiles: FileItem[];
  newestFolders: FileItem[];
  recentInstallers: FileItem[];
  recentProjectFiles: FileItem[];
};

interface SmartCardsProps {
  folderCount: number;
  fileCount: number;
  currentPath: string;
  items: FileItem[];
  selectedBrainAction: AtlasBrainAction | null;
  collectionFilterLabel: string;
  folderBrief: { path: string; brief?: string; error?: string; provider?: string } | null;
  folderBriefLoading: boolean;
  onFolderBrief: () => void;
  folderAnswer: { path: string; answer?: string; error?: string; provider?: string } | null;
  folderAnswerLoading: boolean;
  onFolderQuestion: (question: string) => void;
  folderActions: { path: string; actions?: string; error?: string; provider?: string } | null;
  folderActionsLoading: boolean;
  onSuggestActions: () => void;
  brainProjectScan: { path: string; entries?: BrainScanEntry[]; error?: string } | null;
  brainProjectScanLoading: boolean;
  onRunProjectScan: () => void;
  brainActionPlan: { path: string; plan?: string; error?: string; provider?: string } | null;
  brainActionPlanLoading: boolean;
  onRunActionPlan: (metadata: Record<string, unknown>) => void;
  onShowInList: (path: string) => void;
  onOpenLocation: (path: string) => void;
  onCopyName: (name: string) => void;
  onApplySearch: (query: string) => void;
  onApplyCollectionFilter: (label: string, paths: string[]) => void;
  onClearCollectionFilter: () => void;
  organizePlan: { path: string; planJson?: string; error?: string; provider?: string; repaired?: boolean } | null;
  organizePlanLoading: boolean;
  organizeExecStatuses: Record<string, string>;
  organizeIsExecuting: boolean;
  onRunOrganizePlan: (instruction?: string) => void;
  onApproveOrganizeActions: (actions: OrganizePlanAction[]) => void;
  atlasCommand: string;
  atlasCommandIntent: string;
  onRunAtlasCommand: (command: string) => void;
  onExtractArchive: (archivePath: string, mode: 'new-folder' | 'here') => void;
}

// ─── Organize Plan Types (local parse) ───────────────────────────────────────
type OrganizeGroup = {
  name: string;
  reason: string;
  folderName: string;
  items: string[];
};

type OrganizePlanData = {
  summary: string;
  confidence: 'low' | 'medium' | 'high';
  groups: OrganizeGroup[];
  renameSuggestions: Array<{ item: string; suggestedName: string; reason: string }>;
  warnings: string[];
  actions: OrganizePlanAction[];
};

type OrganizeParseResult = {
  plan: OrganizePlanData | null;
  repaired: boolean;
};

const INSTALLER_EXTENSIONS = new Set(['.exe', '.msi']);
const ARCHIVE_EXTENSIONS = new Set(['.zip', '.7z', '.rar', '.tar', '.gz']);
const DOCUMENT_EXTENSIONS = new Set(['.txt', '.md', '.pdf', '.doc', '.docx', '.rtf', '.xlsx', '.xls', '.ppt', '.pptx']);
const IMAGE_EXTENSIONS = new Set(['.jpg', '.jpeg', '.png', '.gif', '.webp', '.bmp', '.svg']);
const VIDEO_EXTENSIONS = new Set(['.mp4', '.mkv', '.avi', '.mov', '.webm']);
const AUDIO_EXTENSIONS = new Set(['.mp3', '.wav', '.flac', '.m4a', '.ogg']);
const CODE_EXTENSIONS = new Set(['.ts', '.tsx', '.js', '.jsx', '.cs', '.xaml', '.json', '.xml', '.py', '.java', '.cpp', '.h', '.sql', '.yaml', '.yml']);
const GENERATED_FOLDERS = new Set(['bin', 'obj', 'node_modules', 'dist', 'build', '.next', '.cache', 'out', 'coverage', 'logs']);
const LARGE_FILE_BYTES = 1024 * 1024 * 1024;
const MAX_VISIBLE_ACTIONS_DEFAULT = 20;
const MAX_ACTIONS_PER_GROUP_DEFAULT = 8;
const MAX_APPROVE_BATCH = 25;

const ORGANIZE_GROUP_ORDER = [
  'Installers',
  'Archives',
  'Images',
  'Videos',
  'Documents',
  'Code',
  'Projects',
  'Sensitive / Review',
  'Large files',
  'Duplicate-like',
];

function daysSinceUtc(value: string): number {
  if (!value) return 9999;
  const ts = new Date(value).getTime();
  if (Number.isNaN(ts)) return 9999;
  return (Date.now() - ts) / (1000 * 60 * 60 * 24);
}

function formatBytes(bytes: number | null): string {
  if (bytes === null || bytes < 0) return '-';
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`;
}

function normalizeName(rawName: string, kind: 'file' | 'folder'): string {
  let name = kind === 'file' ? rawName.replace(/\.[^.]+$/, '') : rawName;
  name = name.replace(/\s*\(\d+\)$/, '');
  name = name.replace(/^copy\s+of\s+/i, '');
  name = name.replace(/\s*[-_ ]?(copy|final|new|draft|backup|v\d+)$/i, '');
  return name.trim().toLowerCase();
}

function computeDuplicateLike(items: FileItem[]): FileItem[] {
  const groups = new Map<string, FileItem[]>();
  for (const item of items) {
    const key = normalizeName(item.name, item.kind);
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key)!.push(item);
  }

  return Array.from(groups.values())
    .filter((group) => group.length >= 2)
    .flat();
}

function looksSensitiveName(item: FileItem): boolean {
  const lower = item.name.toLowerCase();
  if (/^client_secret.*\.json$/i.test(item.name)) return true;
  if (lower === '.env' || lower.endsWith('.env')) return true;
  return ['secret', 'token', 'credential', 'private', 'key', 'oauth', 'password'].some((term) => lower.includes(term));
}

function buildCollections(items: FileItem[]): BrainCollection[] {
  const projects = items.filter((item) => {
    if (item.kind === 'folder') {
      return ['src', 'modules', 'figma', 'android', 'ios', 'lib'].includes(item.name.toLowerCase());
    }
    const lower = item.name.toLowerCase();
    return ['package.json', 'vite.config.ts', 'vite.config.js', 'atlasai.csproj', 'app.xaml.cs', 'pubspec.yaml', 'requirements.txt', 'pyproject.toml'].includes(lower)
      || CODE_EXTENSIONS.has(item.extension.toLowerCase());
  });

  const installers = items.filter((item) => item.kind === 'file' && INSTALLER_EXTENSIONS.has(item.extension.toLowerCase()));
  const archives = items.filter((item) => item.kind === 'file' && ARCHIVE_EXTENSIONS.has(item.extension.toLowerCase()));
  const documents = items.filter((item) => item.kind === 'file' && DOCUMENT_EXTENSIONS.has(item.extension.toLowerCase()));
  const images = items.filter((item) => item.kind === 'file' && IMAGE_EXTENSIONS.has(item.extension.toLowerCase()));
  const videos = items.filter((item) => item.kind === 'file' && VIDEO_EXTENSIONS.has(item.extension.toLowerCase()));
  const audio = items.filter((item) => item.kind === 'file' && AUDIO_EXTENSIONS.has(item.extension.toLowerCase()));
  const code = items.filter((item) => item.kind === 'file' && CODE_EXTENSIONS.has(item.extension.toLowerCase()));
  const secrets = items.filter((item) => looksSensitiveName(item));
  const large = items.filter((item) => item.kind === 'file' && item.sizeBytes !== null && item.sizeBytes >= LARGE_FILE_BYTES);
  const recent = items.filter((item) => daysSinceUtc(item.modifiedUtc) <= 7);
  const duplicateLike = computeDuplicateLike(items);
  const generatedFolders = items.filter((item) => item.kind === 'folder' && GENERATED_FOLDERS.has(item.name.toLowerCase()));

  return [
    { id: 'projects', title: 'Projects', items: projects, suggestedAction: 'Focus on project roots and entry points.', filterQuery: 'src' },
    { id: 'installers', title: 'Installers', items: installers, suggestedAction: 'Review source and keep only needed installers.', filterQuery: '.exe' },
    { id: 'archives', title: 'Archives', items: archives, suggestedAction: 'Archive or clean stale compressed files.', filterQuery: '.zip' },
    { id: 'documents', title: 'Documents', items: documents, suggestedAction: 'Group active docs and archive old ones.', filterQuery: '.pdf' },
    { id: 'images', title: 'Images', items: images, suggestedAction: 'Sort by date and archive older media.', filterQuery: '.jpg' },
    { id: 'videos', title: 'Videos', items: videos, suggestedAction: 'Review large videos for archive candidates.', filterQuery: '.mp4' },
    { id: 'audio', title: 'Audio', items: audio, suggestedAction: 'Keep active assets, archive stale audio.', filterQuery: '.mp3' },
    { id: 'code', title: 'Code', items: code, suggestedAction: 'Prioritize recently changed source files.', filterQuery: '.cs' },
    { id: 'secrets', title: 'Secrets / Sensitive', items: secrets, suggestedAction: 'Keep private and avoid sharing.', filterQuery: 'secret' },
    { id: 'large', title: 'Large Files', items: large, suggestedAction: 'Review storage-heavy files.', filterQuery: 'gb' },
    { id: 'recent', title: 'Recent', items: recent, suggestedAction: 'Start with what changed this week.', filterQuery: 'recent' },
    { id: 'duplicate-like', title: 'Duplicate-like', items: duplicateLike, suggestedAction: 'Review duplicate-like names manually.', filterQuery: 'copy' },
    { id: 'generated', title: 'Build/Generated folders', items: generatedFolders, suggestedAction: 'Usually exclude from sharing.', filterQuery: 'bin' },
  ];
}

function detectProject(items: FileItem[], scanEntries: BrainScanEntry[]): BrainProjectInfo {
  const names = new Set(items.map((item) => item.name.toLowerCase()));
  const scanNames = new Set(scanEntries.map((entry) => entry.name.toLowerCase()));
  const has = (name: string) => names.has(name) || scanNames.has(name);

  const containsXaml = items.some((item) => item.extension.toLowerCase() === '.xaml') || scanEntries.some((item) => item.extension.toLowerCase() === '.xaml');
  const containsCsproj = items.some((item) => item.name.toLowerCase().endsWith('.csproj')) || scanEntries.some((item) => item.name.toLowerCase().endsWith('.csproj'));
  const containsSln = items.some((item) => item.name.toLowerCase().endsWith('.sln') || item.name.toLowerCase().endsWith('.slnx')) || scanEntries.some((item) => item.name.toLowerCase().endsWith('.sln') || item.name.toLowerCase().endsWith('.slnx'));

  let projectType = 'General folder';
  let confidence: 'high' | 'medium' | 'low' = 'low';

  if (containsCsproj && (containsXaml || has('app.xaml.cs') || has('modules'))) {
    projectType = '.NET/WPF';
    confidence = 'high';
  } else if (has('package.json') && (has('vite.config.ts') || has('vite.config.js')) && (has('src') || has('node_modules'))) {
    projectType = 'React/Vite';
    confidence = 'high';
  } else if (has('theme.liquid') || (has('sections') && has('snippets') && has('templates'))) {
    projectType = 'Shopify';
    confidence = 'high';
  } else if (has('pubspec.yaml') && has('android') && has('ios') && has('lib')) {
    projectType = 'Flutter';
    confidence = 'high';
  } else if (has('pyproject.toml') || has('requirements.txt') || has('venv') || has('.venv')) {
    projectType = 'Python';
    confidence = 'medium';
  } else if (has('package.json') || has('node_modules')) {
    projectType = 'Node';
    confidence = 'medium';
  } else if (/\\downloads(\\|$)/i.test(scanEntries[0]?.path || '') || /\\downloads(\\|$)/i.test(items[0]?.path || '')) {
    projectType = 'Mixed Downloads folder';
    confidence = 'medium';
  }

  const isAtlasWorkspace = has('atlasai.csproj')
    && has('commandcenterwindow.xaml.cs')
    && has('app.xaml.cs')
    && has('modules')
    && has('figma')
    && has('x64');

  const likelyEntryFiles = ['App.xaml.cs', 'MainWindow.xaml.cs', 'CommandCenterWindow.xaml.cs', 'AtlasAI.csproj', 'package.json', 'vite.config.ts', 'pubspec.yaml', 'pyproject.toml']
    .filter((name) => has(name));

  const buildRunCommands = buildCommandHints(projectType, isAtlasWorkspace, has);

  const avoidTouching = ['bin', 'obj', 'node_modules', 'dist', 'build', '.next', '.cache']
    .filter((name) => has(name));

  const generatedFolders = ['bin', 'obj', 'node_modules', 'dist', 'build', '.next', '.cache']
    .filter((name) => has(name));

  const importantFiles = ['AtlasAI.csproj', 'AtlasAI.slnx', 'App.xaml.cs', 'CommandCenterWindow.xaml.cs', 'package.json', 'vite.config.ts', 'pubspec.yaml', 'requirements.txt']
    .filter((name) => has(name));

  const nextActions = isAtlasWorkspace
    ? [
      'Check module host wiring in FileExplorerHostView.xaml.cs.',
      'Verify matching React dist in Figma/Futuristic AI File Explorer/dist.',
      'Run build and inspect bin/x64 outputs.',
      'Review recent changes in Modules and Figma folders.',
      'Check logs/output before changing generated folders.',
    ]
    : [
      'Start from likely entry files.',
      'Review generated folders before cleaning.',
      'Check recent changes first.',
      'Use Safe Share Check before exporting.',
      'Apply Smart Collections to focus the list.',
    ];

  return {
    projectType,
    confidence,
    isAtlasWorkspace,
    likelyEntryFiles,
    buildRunCommands,
    avoidTouching,
    generatedFolders,
    importantFiles,
    nextActions,
  };
}

function buildCommandHints(projectType: string, isAtlasWorkspace: boolean, has: (name: string) => boolean): string[] {
  if (isAtlasWorkspace) {
    return [
      'dotnet build AtlasAI.csproj',
      'npm run build (in Figma/Futuristic AI File Explorer)',
      'dotnet run --project AtlasAI.csproj',
    ];
  }

  if (projectType === '.NET/WPF') return ['dotnet build', 'dotnet run'];
  if (projectType === 'React/Vite') return ['npm install', 'npm run dev', 'npm run build'];
  if (projectType === 'Flutter') return ['flutter pub get', 'flutter run', 'flutter build'];
  if (projectType === 'Python') return [has('pyproject.toml') ? 'poetry install' : 'pip install -r requirements.txt', 'python main.py'];
  if (projectType === 'Node') return ['npm install', 'npm run start'];
  return [];
}

function buildSafeShare(items: FileItem[]): ShareCheckResult {
  const doNotShare = items.filter((item) => {
    const lower = item.name.toLowerCase();
    return looksSensitiveName(item)
      || lower.includes('credential')
      || lower.includes('password')
      || lower.includes('localappdata');
  });

  const reviewBeforeSharing = items.filter((item) => {
    const lower = item.name.toLowerCase();
    return (item.kind === 'folder' && GENERATED_FOLDERS.has(lower))
      || (item.kind === 'file' && ARCHIVE_EXTENSIONS.has(item.extension.toLowerCase()) && (item.sizeBytes ?? 0) > LARGE_FILE_BYTES)
      || lower.includes('backup')
      || lower.includes('log');
  });

  const safeToInclude = items.filter((item) => {
    const lower = item.name.toLowerCase();
    if (looksSensitiveName(item)) return false;
    if (item.kind === 'folder' && GENERATED_FOLDERS.has(lower)) return false;
    return item.kind === 'file' && (CODE_EXTENSIONS.has(item.extension.toLowerCase()) || DOCUMENT_EXTENSIONS.has(item.extension.toLowerCase()));
  }).slice(0, 20);

  const excludePatterns = ['bin/', 'obj/', 'node_modules/', '*.token', 'client_secret*.json', '.env', 'logs/'];

  return { doNotShare, reviewBeforeSharing, safeToInclude, excludePatterns };
}

function buildCleanup(items: FileItem[]): CleanupGroups {
  const duplicateLikeDownloads = computeDuplicateLike(items).filter((item) => /downloads/i.test(item.path));

  return {
    oldInstallers: items.filter((item) => item.kind === 'file' && INSTALLER_EXTENSIONS.has(item.extension.toLowerCase()) && daysSinceUtc(item.modifiedUtc) > 30),
    duplicateLikeDownloads,
    largeArchives: items.filter((item) => item.kind === 'file' && ARCHIVE_EXTENSIONS.has(item.extension.toLowerCase()) && (item.sizeBytes ?? 0) >= LARGE_FILE_BYTES),
    tempBuildFolders: items.filter((item) => item.kind === 'folder' && GENERATED_FOLDERS.has(item.name.toLowerCase())),
    oldScreenshotsVideos: items.filter((item) => {
      if (item.kind !== 'file') return false;
      const lower = item.name.toLowerCase();
      return (lower.includes('screenshot') || VIDEO_EXTENSIONS.has(item.extension.toLowerCase())) && daysSinceUtc(item.modifiedUtc) > 60;
    }),
    sensitiveMoveToSecrets: items.filter((item) => looksSensitiveName(item)),
    keepList: items.filter((item) => daysSinceUtc(item.modifiedUtc) <= 14).slice(0, 20),
  };
}

function buildRecent(items: FileItem[]): RecentResult {
  const sorted = [...items].sort((a, b) => new Date(b.modifiedUtc).getTime() - new Date(a.modifiedUtc).getTime());
  return {
    changedToday: sorted.filter((item) => daysSinceUtc(item.modifiedUtc) <= 1).slice(0, 15),
    changedWeek: sorted.filter((item) => daysSinceUtc(item.modifiedUtc) <= 7).slice(0, 20),
    newestFiles: sorted.filter((item) => item.kind === 'file').slice(0, 15),
    newestFolders: sorted.filter((item) => item.kind === 'folder').slice(0, 10),
    recentInstallers: sorted.filter((item) => item.kind === 'file' && INSTALLER_EXTENSIONS.has(item.extension.toLowerCase())).slice(0, 12),
    recentProjectFiles: sorted.filter((item) => item.kind === 'file' && CODE_EXTENSIONS.has(item.extension.toLowerCase())).slice(0, 15),
  };
}

async function copyText(text: string) {
  if (!text.trim()) return;
  try {
    await navigator.clipboard.writeText(text);
  } catch {
    // no-op
  }
}

function buildLocalOrganizePlan(items: FileItem[], instruction?: string): OrganizePlanData {
  const text = (instruction || '').toLowerCase();
  const files = items.filter((item) => item.kind === 'file');
  const folders = items.filter((item) => item.kind === 'folder');
  const maxGeneratedActions = 40;
  let generatedActions = 0;

  const groupDefinitions: Array<{ name: string; reason: string; exts: Set<string> }> = [
    { name: 'Installers', reason: 'Executable installers should stay together for easy cleanup.', exts: new Set(['.exe', '.msi']) },
    { name: 'Archives', reason: 'Compressed archives are grouped for quick extraction workflows.', exts: new Set(['.zip', '.rar', '.7z']) },
    { name: 'Images', reason: 'Image files are grouped for media triage.', exts: IMAGE_EXTENSIONS },
    { name: 'Videos', reason: 'Video files are grouped to reduce folder noise.', exts: VIDEO_EXTENSIONS },
    { name: 'Documents', reason: 'Documents are grouped for easier review and backup prep.', exts: DOCUMENT_EXTENSIONS },
    { name: 'Code', reason: 'Code/config files are grouped to keep project artifacts together.', exts: CODE_EXTENSIONS },
  ];

  const selectedByOnlyMode = new Set<string>();
  if (text.includes('only')) {
    if (text.includes('installer')) selectedByOnlyMode.add('Installers');
    if (text.includes('archive')) selectedByOnlyMode.add('Archives');
    if (text.includes('image')) selectedByOnlyMode.add('Images');
    if (text.includes('video')) selectedByOnlyMode.add('Videos');
    if (text.includes('document')) selectedByOnlyMode.add('Documents');
    if (text.includes('code')) selectedByOnlyMode.add('Code');
    if (text.includes('project')) selectedByOnlyMode.add('Projects');
  }

  const onlyModeActive = selectedByOnlyMode.size > 0;
  const backupMode = /backup|share|send/.test(text);
  const reviewOnlyCommand = /sensitive|duplicate|review/.test(text) && !/organize|sort|tidy|clean/.test(text);
  const includeGroup = (name: string) => {
    if (onlyModeActive) return selectedByOnlyMode.has(name);
    if (backupMode) return ['Documents', 'Code', 'Archives', 'Sensitive / Review', 'Large files', 'Duplicate-like'].includes(name);
    return true;
  };

  const groups: OrganizeGroup[] = [];
  const actions: OrganizePlanAction[] = [];
  const warnings: string[] = ['This is a local fallback plan. Review each action before approving.'];

  const seenByName = new Set<string>();
  const addAction = (action: OrganizePlanAction) => {
    if (generatedActions >= maxGeneratedActions) return;
    if (action.type === 'move' && (!action.sourceName || !action.targetFolder)) return;
    if (action.type === 'rename' && (!action.sourceName || !action.newName)) return;
    const key = `${action.type}|${action.sourceName}|${action.targetFolder}|${action.newName}`.toLowerCase();
    if (seenByName.has(key)) return;
    seenByName.add(key);
    actions.push(action);
    generatedActions += 1;
  };

  for (const groupDef of groupDefinitions) {
    const matched = files.filter((item) => groupDef.exts.has(item.extension.toLowerCase()));
    if (matched.length === 0) continue;

    groups.push({
      name: groupDef.name,
      reason: groupDef.reason,
      folderName: groupDef.name,
      items: matched.map((item) => item.name),
    });

    if (!includeGroup(groupDef.name) || reviewOnlyCommand) continue;
    if (generatedActions >= maxGeneratedActions) continue;

    addAction({
      type: 'create-folder',
      sourceName: '',
      targetFolder: groupDef.name,
      newName: groupDef.name,
      reason: `Create ${groupDef.name} folder for organized placement.`,
      group: groupDef.name,
    });

    for (const item of matched.slice(0, 12)) {
      addAction({
        type: 'move',
        sourceName: item.name,
        targetFolder: groupDef.name,
        newName: '',
        reason: groupDef.reason,
        group: groupDef.name,
      });
    }
  }

  const projectLikeFolders = folders.filter((item) => {
    const lower = item.name.toLowerCase();
    return lower.includes('project') || lower.includes('repo') || lower.includes('solution') || lower.includes('workspace');
  });
  if (projectLikeFolders.length > 0) {
    groups.push({
      name: 'Projects',
      reason: 'Likely project folders detected by naming patterns.',
      folderName: 'Projects',
      items: projectLikeFolders.map((item) => item.name),
    });

    if (includeGroup('Projects') && (text.includes('project') || text.includes('separate'))) {
      addAction({
        type: 'create-folder',
        sourceName: '',
        targetFolder: 'Projects',
        newName: 'Projects',
        reason: 'Create Projects folder for clear project separation.',
        group: 'Projects',
      });
      for (const item of projectLikeFolders.slice(0, 5)) {
        addAction({
          type: 'move',
          sourceName: item.name,
          targetFolder: 'Projects',
          newName: '',
          reason: 'Likely project folder based on name pattern.',
          group: 'Projects',
        });
      }
    }
  }

  const sensitiveItems = items.filter((item) => looksSensitiveName(item));
  if (sensitiveItems.length > 0) {
    groups.push({
      name: 'Sensitive / Review',
      reason: 'Sensitive-looking names must be manually reviewed; no auto move suggestion.',
      folderName: 'Sensitive / Review',
      items: sensitiveItems.map((item) => item.name),
    });
  }

  const largeFiles = files.filter((item) => (item.sizeBytes ?? 0) >= LARGE_FILE_BYTES);
  if (largeFiles.length > 0) {
    groups.push({
      name: 'Large files',
      reason: 'Large files should be reviewed before any relocation.',
      folderName: 'Large files',
      items: largeFiles.map((item) => item.name),
    });
  }

  const duplicateLike = computeDuplicateLike(files);
  if (duplicateLike.length > 0) {
    groups.push({
      name: 'Duplicate-like',
      reason: 'Name patterns look duplicated; verify manually before moving.',
      folderName: 'Duplicate-like',
      items: duplicateLike.map((item) => item.name),
    });
  }

  if (generatedActions >= maxGeneratedActions) {
    warnings.push(`Action list capped at ${maxGeneratedActions} suggestions for safer review.`);
  }

  const groupedFileCount = groups.reduce((sum, group) => sum + group.items.length, 0);
  const reviewOnlyCount =
    (groups.find((g) => g.name === 'Sensitive / Review')?.items.length ?? 0)
    + (groups.find((g) => g.name === 'Large files')?.items.length ?? 0)
    + (groups.find((g) => g.name === 'Duplicate-like')?.items.length ?? 0);
  const safeMoveSuggestions = actions.filter((action) => !action.reviewOnly && action.type !== 'rename').length;

  const unknownCount = Math.max(0, files.length - groupedFileCount);
  const confidence: 'low' | 'medium' | 'high' = unknownCount > files.length * 0.45
    ? 'low'
    : safeMoveSuggestions >= 12
      ? 'high'
      : 'medium';

  const summary = [
    `Atlas found ${groupedFileCount} useful organization opportunities.`,
    `${safeMoveSuggestions} safe move suggestions.`,
    `${reviewOnlyCount} review-only items.`,
    'Nothing will move until approved.',
  ].join(' ');

  return {
    summary,
    confidence,
    groups,
    renameSuggestions: [],
    warnings,
    actions,
  };
}

function parseOrganizePlanJson(rawPlanJson: string): OrganizeParseResult {
  const trimmed = (rawPlanJson || '').trim();
  if (!trimmed) return { plan: null, repaired: false };

  const strict = tryParseOrganizePlanData(trimmed);
  if (strict) return { plan: strict, repaired: false };

  const noFences = stripMarkdownCodeFence(trimmed);
  const parsedNoFences = tryParseOrganizePlanData(noFences);
  if (parsedNoFences) return { plan: parsedNoFences, repaired: true };

  const extracted = extractFirstJsonObject(noFences) || extractFirstJsonObject(trimmed);
  if (!extracted) return { plan: null, repaired: false };

  const parsedExtracted = tryParseOrganizePlanData(extracted);
  if (parsedExtracted) return { plan: parsedExtracted, repaired: true };

  return { plan: null, repaired: false };
}

function tryParseOrganizePlanData(value: string): OrganizePlanData | null {
  try {
    const parsed = JSON.parse(value) as Partial<OrganizePlanData>;
    if (!parsed || typeof parsed !== 'object') return null;

    const warnings = Array.isArray(parsed.warnings) ? [...parsed.warnings as string[]] : [];
    const parsedActions = Array.isArray(parsed.actions)
      ? parsed.actions
        .map((action) => normalizeOrganizeAction(action))
        .filter((action): action is OrganizePlanAction => !!action)
      : [];

    let actions = parsedActions;
    if (parsedActions.length > 40) {
      actions = parsedActions.slice(0, 20);
      warnings.push(`Action list trimmed to 20 from ${parsedActions.length} for safe review.`);
    }

    return {
      summary: typeof parsed.summary === 'string' ? parsed.summary : 'Organize plan generated from metadata.',
      confidence: parsed.confidence === 'low' || parsed.confidence === 'medium' || parsed.confidence === 'high' ? parsed.confidence : 'medium',
      groups: Array.isArray(parsed.groups) ? parsed.groups as OrganizeGroup[] : [],
      renameSuggestions: Array.isArray(parsed.renameSuggestions) ? parsed.renameSuggestions as Array<{ item: string; suggestedName: string; reason: string }> : [],
      warnings,
      actions,
    };
  } catch {
    return null;
  }
}

function normalizeOrganizeAction(raw: unknown): OrganizePlanAction | null {
  if (!raw || typeof raw !== 'object') return null;
  const value = raw as Record<string, unknown>;
  const type = value.type;
  if (type !== 'create-folder' && type !== 'move' && type !== 'rename') return null;

  return {
    type,
    sourceName: typeof value.sourceName === 'string' ? value.sourceName : '',
    targetFolder: typeof value.targetFolder === 'string' ? value.targetFolder : '',
    newName: typeof value.newName === 'string' ? value.newName : '',
    reason: typeof value.reason === 'string' ? value.reason : 'No reason provided',
    group: typeof value.group === 'string' ? value.group : undefined,
    reviewOnly: Boolean(value.reviewOnly),
  };
}

function stripMarkdownCodeFence(value: string): string {
  const trimmed = value.trim();
  if (!trimmed.startsWith('```')) return trimmed;

  const lines = trimmed.replace(/\r\n/g, '\n').split('\n');
  if (lines.length >= 2 && lines[0].trimStart().startsWith('```') && lines[lines.length - 1].trim().startsWith('```')) {
    return lines.slice(1, -1).join('\n').trim();
  }

  return trimmed.replace(/```json/ig, '').replace(/```/g, '').trim();
}

function extractFirstJsonObject(value: string): string {
  const start = value.indexOf('{');
  if (start < 0) return '';

  let depth = 0;
  let inString = false;
  let escaping = false;

  for (let i = start; i < value.length; i += 1) {
    const ch = value[i];

    if (inString) {
      if (escaping) {
        escaping = false;
      } else if (ch === '\\') {
        escaping = true;
      } else if (ch === '"') {
        inString = false;
      }
      continue;
    }

    if (ch === '"') {
      inString = true;
      continue;
    }

    if (ch === '{') {
      depth += 1;
      continue;
    }

    if (ch === '}') {
      depth -= 1;
      if (depth === 0) {
        return value.slice(start, i + 1).trim();
      }
    }
  }

  return '';
}

export function SmartCards({
  folderCount,
  fileCount,
  currentPath,
  items,
  selectedBrainAction,
  collectionFilterLabel,
  brainProjectScan,
  brainProjectScanLoading,
  onRunProjectScan,
  brainActionPlan,
  brainActionPlanLoading,
  onRunActionPlan,
  onShowInList,
  onOpenLocation,
  onCopyName,
  onApplySearch,
  onApplyCollectionFilter,
  onClearCollectionFilter,
  organizePlan,
  organizePlanLoading,
  organizeExecStatuses,
  organizeIsExecuting,
  onRunOrganizePlan,
  onApproveOrganizeActions,
  atlasCommand,
  atlasCommandIntent,
  onRunAtlasCommand,
  onExtractArchive,
}: SmartCardsProps) {
  const [lastActionPlanKey, setLastActionPlanKey] = useState('');
  const [organizeInstruction, setOrganizeInstruction] = useState('');
  const [parsedPlan, setParsedPlan] = useState<OrganizePlanData | null>(null);
  const [parsedPlanError, setParsedPlanError] = useState('');
  const [checkedActionIndices, setCheckedActionIndices] = useState<Set<number>>(new Set());
  const [useLocalFallback, setUseLocalFallback] = useState(false);
  const [planJsonRepairedClientSide, setPlanJsonRepairedClientSide] = useState(false);
  const [expandedOrganizeGroups, setExpandedOrganizeGroups] = useState<Set<string>>(new Set());
  const [showAllOrganizeActions, setShowAllOrganizeActions] = useState(false);
  const [commandDraft, setCommandDraft] = useState('');

  const scanEntries = useMemo(() => brainProjectScan?.entries ?? [], [brainProjectScan]);
  const collections = useMemo(() => buildCollections(items), [items]);
  const projectInfo = useMemo(() => detectProject(items, scanEntries), [items, scanEntries]);
  const shareCheck = useMemo(() => buildSafeShare(items), [items]);
  const cleanup = useMemo(() => buildCleanup(items), [items]);
  const recent = useMemo(() => buildRecent(items), [items]);

  useEffect(() => {
    if (!atlasCommand) return;
    setCommandDraft(atlasCommand);
    setOrganizeInstruction(atlasCommand);
  }, [atlasCommand]);

  useEffect(() => {
    if (!organizePlan?.planJson) {
      setParsedPlan(null);
      setParsedPlanError('');
      setCheckedActionIndices(new Set());
      setPlanJsonRepairedClientSide(false);
      setExpandedOrganizeGroups(new Set());
      setShowAllOrganizeActions(false);
      return;
    }

    const parsed = parseOrganizePlanJson(organizePlan.planJson);
    if (parsed.plan) {
      setParsedPlan(parsed.plan);
      setParsedPlanError('');
      setCheckedActionIndices(new Set());
      setUseLocalFallback(false);
      setPlanJsonRepairedClientSide(parsed.repaired);
      setExpandedOrganizeGroups(new Set());
      setShowAllOrganizeActions(false);
    } else {
      setParsedPlan(null);
      setParsedPlanError('AI returned invalid JSON. Using local fallback plan automatically.');
      setUseLocalFallback(true);
      setPlanJsonRepairedClientSide(false);
      setExpandedOrganizeGroups(new Set());
      setShowAllOrganizeActions(false);
    }
  }, [organizePlan?.planJson]);

  useEffect(() => {
    if (selectedBrainAction !== 'organize-folder') return;
    if (organizePlanLoading) return;
    if (!organizePlan?.error) return;

    // Automatically fall back so the user always gets a usable plan.
    setUseLocalFallback(true);
  }, [selectedBrainAction, organizePlanLoading, organizePlan?.error]);

  useEffect(() => {
    if (selectedBrainAction === 'project-brain' && !brainProjectScanLoading && !brainProjectScan) {
      onRunProjectScan();
    }
  }, [selectedBrainAction, brainProjectScanLoading, brainProjectScan, onRunProjectScan]);

  useEffect(() => {
    if (selectedBrainAction !== 'action-plan' || !currentPath) return;

    const topItems = items.slice(0, 100).map((item) => ({
      name: item.name,
      path: item.path,
      kind: item.kind,
      extension: item.extension,
      sizeBytes: item.sizeBytes,
      modifiedUtc: item.modifiedUtc,
    }));

    const collectionCounts = Object.fromEntries(collections.map((collection) => [collection.title, collection.items.length]));

    const metadata = {
      currentPath,
      detectedProjectType: projectInfo.projectType,
      collectionCounts,
      topItems,
      recentSummary: {
        changedToday: recent.changedToday.length,
        changedWeek: recent.changedWeek.length,
        newestFiles: recent.newestFiles.slice(0, 10).map((item) => item.name),
      },
      sensitiveNameCount: collections.find((c) => c.title === 'Secrets / Sensitive')?.items.length ?? 0,
      largeFileCount: collections.find((c) => c.title === 'Large Files')?.items.length ?? 0,
    };

    const runKey = JSON.stringify({ currentPath, count: items.length, project: projectInfo.projectType });
    if (lastActionPlanKey === runKey && (brainActionPlan || brainActionPlanLoading)) return;

    setLastActionPlanKey(runKey);
    onRunActionPlan(metadata);
  }, [selectedBrainAction, currentPath, items, collections, projectInfo.projectType, recent, lastActionPlanKey, onRunActionPlan, brainActionPlan, brainActionPlanLoading]);

  const selectedPanelName = selectedBrainAction === 'project-brain'
    ? 'Project Brain'
    : selectedBrainAction === 'smart-collections'
      ? 'Smart Collections'
      : selectedBrainAction === 'safe-share-check'
        ? 'Safe Share Check'
        : selectedBrainAction === 'cleanup-plan'
          ? 'Cleanup Plan'
          : selectedBrainAction === 'recent-changes'
            ? 'Recent Changes'
            : selectedBrainAction === 'what-changed'
              ? 'What Changed'
              : selectedBrainAction === 'action-plan'
                ? 'Action Plan'
                : 'None';

  const selectedPanelSource = selectedBrainAction === 'project-brain'
    ? 'AI provider'
    : selectedBrainAction === 'organize-folder' && useLocalFallback
      ? 'local fallback'
    : selectedBrainAction === 'action-plan'
      ? (brainActionPlan?.provider || 'AI provider')
      : 'local';

  const organizeSourceLabel = useMemo(() => {
    if (useLocalFallback) return 'Local fallback';
    if (!organizePlan) return 'pending';
    if (organizePlan.repaired || planJsonRepairedClientSide || /repaired/i.test(organizePlan.provider || '')) return 'OpenAI repaired';
    if (organizePlan.provider) return 'OpenAI';
    return 'OpenAI';
  }, [useLocalFallback, organizePlan, planJsonRepairedClientSide]);

  const activeOrganizePlan = useMemo(() => {
    if (useLocalFallback) return buildLocalOrganizePlan(items, organizeInstruction);
    return parsedPlan;
  }, [useLocalFallback, items, organizeInstruction, parsedPlan]);

  const organizeActionEntries = useMemo(() => {
    if (!activeOrganizePlan) return [] as Array<{ idx: number; action: OrganizePlanAction; group: string }>;
    return activeOrganizePlan.actions.map((action, idx) => {
      const group = action.group || action.targetFolder || 'General';
      return { idx, action, group };
    });
  }, [activeOrganizePlan]);

  const organizeGroups = useMemo(() => {
    if (!activeOrganizePlan) return [] as Array<{ name: string; reason: string; examples: string[]; actionEntries: Array<{ idx: number; action: OrganizePlanAction; group: string }> }>;

    const map = new Map<string, { name: string; reason: string; examples: string[]; actionEntries: Array<{ idx: number; action: OrganizePlanAction; group: string }> }>();

    for (const group of activeOrganizePlan.groups || []) {
      map.set(group.name, {
        name: group.name,
        reason: group.reason,
        examples: group.items.slice(0, 5),
        actionEntries: [],
      });
    }

    for (const entry of organizeActionEntries) {
      if (!map.has(entry.group)) {
        map.set(entry.group, {
          name: entry.group,
          reason: 'AI suggested this action group.',
          examples: [],
          actionEntries: [],
        });
      }
      map.get(entry.group)!.actionEntries.push(entry);
    }

    const sorted = [...map.values()].sort((a, b) => {
      const ai = ORGANIZE_GROUP_ORDER.indexOf(a.name);
      const bi = ORGANIZE_GROUP_ORDER.indexOf(b.name);
      const aScore = ai >= 0 ? ai : 999;
      const bScore = bi >= 0 ? bi : 999;
      if (aScore !== bScore) return aScore - bScore;
      return a.name.localeCompare(b.name);
    });

    return sorted;
  }, [activeOrganizePlan, organizeActionEntries]);

  const safeActionEntries = useMemo(
    () => organizeActionEntries.filter((entry) => !entry.action.reviewOnly),
    [organizeActionEntries],
  );

  const reviewOnlyItemsCount = useMemo(() => {
    const fromActions = organizeActionEntries.filter((entry) => entry.action.reviewOnly).length;
    const fromGroups = organizeGroups
      .filter((group) => ['Sensitive / Review', 'Large files', 'Duplicate-like'].includes(group.name))
      .reduce((sum, group) => sum + group.examples.length, 0);
    return Math.max(fromActions, fromGroups);
  }, [organizeActionEntries, organizeGroups]);

  const selectedCount = checkedActionIndices.size;
  const applyLimitExceeded = selectedCount > MAX_APPROVE_BATCH;
  const visibleActionCount = useMemo(() => {
    if (!activeOrganizePlan) return 0;

    let visible = 0;
    let remaining = showAllOrganizeActions ? Number.MAX_SAFE_INTEGER : MAX_VISIBLE_ACTIONS_DEFAULT;

    for (const group of organizeGroups) {
      if (!expandedOrganizeGroups.has(group.name)) continue;
      if (remaining <= 0) break;

      let countForGroup = group.actionEntries.length;
      if (!showAllOrganizeActions) {
        countForGroup = Math.min(countForGroup, MAX_ACTIONS_PER_GROUP_DEFAULT, remaining);
      } else {
        countForGroup = Math.min(countForGroup, remaining);
      }

      visible += countForGroup;
      remaining -= countForGroup;
    }

    return visible;
  }, [activeOrganizePlan, showAllOrganizeActions, organizeGroups, expandedOrganizeGroups]);
  const hiddenActionCount = Math.max(0, organizeActionEntries.length - visibleActionCount);
  const commandExecutionState = organizePlanLoading
    ? 'loading'
    : organizePlan?.error && !useLocalFallback
      ? 'error'
      : activeOrganizePlan
        ? (organizeActionEntries.length === 0 && organizeGroups.length === 0 ? 'empty' : 'result')
        : 'idle';

  const smartCollectionsEmpty = collections.every((collection) => collection.items.length === 0);
  const safeShareEmpty = shareCheck.doNotShare.length === 0 && shareCheck.reviewBeforeSharing.length === 0 && shareCheck.safeToInclude.length === 0;
  const cleanupEmpty = cleanup.oldInstallers.length === 0 && cleanup.duplicateLikeDownloads.length === 0 && cleanup.largeArchives.length === 0
    && cleanup.tempBuildFolders.length === 0 && cleanup.oldScreenshotsVideos.length === 0 && cleanup.sensitiveMoveToSecrets.length === 0 && cleanup.keepList.length === 0;
  const recentEmpty = recent.changedToday.length === 0 && recent.changedWeek.length === 0 && recent.newestFiles.length === 0
    && recent.newestFolders.length === 0 && recent.recentInstallers.length === 0 && recent.recentProjectFiles.length === 0;

  return (
    <div className="space-y-3">
      <div className="rounded-xl border border-white/10 bg-[#0e0e0e] p-3">
        <div className="space-y-3 min-w-0">
          {!selectedBrainAction && (
            <Panel title="Atlas Brain" badge="Metadata only / current folder">
              <div className="text-[11px] text-white/45">Folders: {folderCount} | Files: {fileCount} | Filter: {collectionFilterLabel || 'None'}</div>
              <div className="text-sm text-white/90 font-medium">Organize this folder</div>
              <div className="text-xs text-white/55 mt-0.5">
                Let Atlas analyze and propose a safe organization plan — no auto-actions, every step requires your approval.
              </div>
              <button
                onClick={() => onRunOrganizePlan(undefined)}
                disabled={!currentPath}
                className="mt-2 h-8 px-3 rounded-lg bg-cyan-500/20 border border-cyan-400/40 text-xs text-cyan-200 hover:bg-cyan-500/30 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
              >
                Analyze &amp; Plan
              </button>
              <div className="text-xs text-white/35 mt-2">Or choose a tool from Atlas Brain dropdown above.</div>
              <BrainDebugLine panelName={selectedPanelName} itemCount={items.length} source="local" />
            </Panel>
          )}

          {selectedBrainAction === 'smart-collections' && (
            <Panel title="Smart Collections" badge="Virtual collections only - no file moves">
              <PanelPath path={currentPath} />
              <BrainDebugLine panelName={selectedPanelName} itemCount={items.length} source={selectedPanelSource} />
              {smartCollectionsEmpty && <Muted text="No matching items found" />}

              {collectionFilterLabel && (
                <div className="flex items-center gap-2 text-xs text-cyan-200 bg-cyan-500/10 border border-cyan-400/25 rounded-lg p-2">
                  <CheckCircle2 size={13} />
                  Active filter: {collectionFilterLabel}
                  <button className="ml-auto h-6 px-2 rounded border border-white/12 bg-white/5 text-white/70 hover:bg-white/10" onClick={onClearCollectionFilter}>Clear filter</button>
                </div>
              )}

              {collections.map((collection) => (
                <div key={collection.id} className="rounded-lg border border-white/10 bg-white/5 p-2.5 space-y-1.5">
                  <div className="flex items-center justify-between gap-2">
                    <div className="text-xs text-white/80">{collection.title}</div>
                    <div className="text-[11px] text-white/45">{collection.items.length} items</div>
                  </div>
                  <div className="text-[11px] text-white/55">{collection.suggestedAction}</div>
                  <div className="text-[11px] text-white/45 truncate">Examples: {collection.items.slice(0, 5).map((item) => item.name).join(', ') || 'None'}</div>
                  <div className="flex items-center gap-1">
                    <button className="h-5 px-2 rounded text-[10px] border border-white/12 bg-white/5 text-white/70 hover:bg-white/10" onClick={() => onApplyCollectionFilter(collection.title, collection.items.map((item) => item.path))}>
                      Show collection
                    </button>
                    <button className="h-5 px-2 rounded text-[10px] border border-white/12 bg-white/5 text-white/70 hover:bg-white/10" onClick={() => onCopyName(collection.items.map((item) => item.name).join('\n'))}>
                      Copy names
                    </button>
                  </div>
                </div>
              ))}
            </Panel>
          )}

          {selectedBrainAction === 'project-brain' && (
            <Panel title="Project Brain" badge="Project analysis can scan up to 2 levels / 500 metadata entries">
              <PanelPath path={currentPath} />
              <BrainDebugLine panelName={selectedPanelName} itemCount={items.length} source={selectedPanelSource} />
              {projectInfo.isAtlasWorkspace ? (
                <div className="rounded-lg border border-cyan-400/25 bg-cyan-500/10 p-2.5 text-xs text-cyan-100">
                  This looks like an Atlas workspace.
                </div>
              ) : projectInfo.projectType === 'General folder' && projectInfo.confidence === 'low' ? (
                <div className="rounded-lg border border-white/10 bg-white/5 p-2.5 space-y-2">
                  <div className="text-xs text-white/80">This looks like a general folder, not a project workspace.</div>
                  <div className="text-xs text-white/50">No project markers found (.csproj, package.json, pubspec.yaml, requirements.txt, etc.)</div>
                  <div className="text-xs text-white/65 mt-1">Recommended tools:</div>
                  <div className="flex flex-wrap gap-1">
                    {['Organize this folder', 'Cleanup plan', 'Recent changes'].map((tool) => (
                      <span key={tool} className="text-[10px] px-2 py-0.5 rounded-full border border-white/12 bg-white/5 text-white/55">{tool}</span>
                    ))}
                  </div>
                </div>
              ) : (
                <div className="text-xs text-white/70">Detected: {projectInfo.projectType} ({projectInfo.confidence})</div>
              )}

              {brainProjectScanLoading && <Muted text="Analyzing current folder metadata..." />}
              {brainProjectScan?.error && <ErrorBox message={brainProjectScan.error} />}

              {!(projectInfo.projectType === 'General folder' && projectInfo.confidence === 'low' && !projectInfo.isAtlasWorkspace) && (
                <>
                  <ActionableNameList title="Likely entry files" values={projectInfo.likelyEntryFiles} items={items} onShowInList={onShowInList} onOpenLocation={onOpenLocation} onCopyName={onCopyName} onApplySearch={onApplySearch} />
                  <SimpleList title="Build/run commands" values={projectInfo.buildRunCommands} />
                  <SimpleList title="Folders to avoid touching" values={projectInfo.avoidTouching} />
                  <SimpleList title="Generated folders" values={projectInfo.generatedFolders} />
                  <ActionableNameList title="Important files" values={projectInfo.importantFiles} items={items} onShowInList={onShowInList} onOpenLocation={onOpenLocation} onCopyName={onCopyName} onApplySearch={onApplySearch} />
                  <SimpleList title="Suggested next actions" values={projectInfo.nextActions} />
                </>
              )}
            </Panel>
          )}

          {selectedBrainAction === 'safe-share-check' && (
            <Panel title="Safe Share Check" badge="Metadata-only. No file contents read.">
              <PanelPath path={currentPath} />
              <BrainDebugLine panelName={selectedPanelName} itemCount={items.length} source={selectedPanelSource} />
              {safeShareEmpty && <Muted text="No matching items found" />}
              <ActionableGroup title="Do not share" items={shareCheck.doNotShare} onShowInList={onShowInList} onOpenLocation={onOpenLocation} onCopyName={onCopyName} />
              <ActionableGroup title="Review before sharing" items={shareCheck.reviewBeforeSharing} onShowInList={onShowInList} onOpenLocation={onOpenLocation} onCopyName={onCopyName} />
              <ActionableGroup title="Safe to include" items={shareCheck.safeToInclude} onShowInList={onShowInList} onOpenLocation={onOpenLocation} onCopyName={onCopyName} />
              <div className="space-y-1">
                <div className="text-xs text-white/70">Suggested excluded patterns</div>
                {shareCheck.excludePatterns.map((pattern) => (
                  <div key={pattern} className="text-xs text-white/55">- {pattern}</div>
                ))}
              </div>
            </Panel>
          )}

          {selectedBrainAction === 'cleanup-plan' && (
            <Panel title="Cleanup Plan" badge="Review only. No delete or move automation.">
              <PanelPath path={currentPath} />
              <BrainDebugLine panelName={selectedPanelName} itemCount={items.length} source={selectedPanelSource} />
              {cleanupEmpty && <Muted text="No matching items found" />}
              <ActionableGroup title="Old installers" items={cleanup.oldInstallers} onShowInList={onShowInList} onOpenLocation={onOpenLocation} onCopyName={onCopyName} />
              <ActionableGroup title="Duplicate-like downloads" items={cleanup.duplicateLikeDownloads} onShowInList={onShowInList} onOpenLocation={onOpenLocation} onCopyName={onCopyName} />
              <ActionableGroup title="Large archives" items={cleanup.largeArchives} onShowInList={onShowInList} onOpenLocation={onOpenLocation} onCopyName={onCopyName} />
              <ActionableGroup title="Temp/build folders" items={cleanup.tempBuildFolders} onShowInList={onShowInList} onOpenLocation={onOpenLocation} onCopyName={onCopyName} />
              <ActionableGroup title="Old screenshots/videos" items={cleanup.oldScreenshotsVideos} onShowInList={onShowInList} onOpenLocation={onOpenLocation} onCopyName={onCopyName} />
              <ActionableGroup title="Sensitive files to move to Secrets" items={cleanup.sensitiveMoveToSecrets} onShowInList={onShowInList} onOpenLocation={onOpenLocation} onCopyName={onCopyName} />
              <ActionableGroup title="Keep list" items={cleanup.keepList} onShowInList={onShowInList} onOpenLocation={onOpenLocation} onCopyName={onCopyName} />
            </Panel>
          )}

          {selectedBrainAction === 'recent-changes' && (
            <Panel title="Recent Changes Brief" badge="Modified-date metadata only">
              <PanelPath path={currentPath} />
              <BrainDebugLine panelName={selectedPanelName} itemCount={items.length} source={selectedPanelSource} />
              {recentEmpty && <Muted text="No matching items found" />}
              <ActionableGroup title="Changed today" items={recent.changedToday} onShowInList={onShowInList} onOpenLocation={onOpenLocation} onCopyName={onCopyName} />
              <ActionableGroup title="Changed this week" items={recent.changedWeek} onShowInList={onShowInList} onOpenLocation={onOpenLocation} onCopyName={onCopyName} />
              <ActionableGroup title="Newest files" items={recent.newestFiles} onShowInList={onShowInList} onOpenLocation={onOpenLocation} onCopyName={onCopyName} />
              <ActionableGroup title="Newest folders" items={recent.newestFolders} onShowInList={onShowInList} onOpenLocation={onOpenLocation} onCopyName={onCopyName} />
              <ActionableGroup title="Recent installers" items={recent.recentInstallers} onShowInList={onShowInList} onOpenLocation={onOpenLocation} onCopyName={onCopyName} />
              <ActionableGroup title="Recent project files" items={recent.recentProjectFiles} onShowInList={onShowInList} onOpenLocation={onOpenLocation} onCopyName={onCopyName} />
            </Panel>
          )}

          {selectedBrainAction === 'what-changed' && (
            <Panel title="What Broke / What Changed" badge="Likely signals only - check first">
              <PanelPath path={currentPath} />
              <BrainDebugLine panelName={selectedPanelName} itemCount={items.length} source={selectedPanelSource} />
              {projectInfo.projectType === 'General folder' && projectInfo.confidence === 'low' ? (
                <div className="rounded-lg border border-white/10 bg-white/5 p-2.5 text-xs text-white/65">
                  What Changed mode is most useful in project folders. This folder does not look like a project from metadata.
                </div>
              ) : (
                <>
              {brainProjectScanLoading && <Muted text="Analyzing current folder metadata..." />}
              {brainProjectScan?.error && <ErrorBox message={brainProjectScan.error} />}

              <SimpleList
                title="Likely changed files"
                values={recent.recentProjectFiles.slice(0, 12).map((item) => `${item.name} (changed ${Math.max(0, Math.floor(daysSinceUtc(item.modifiedUtc)))}d ago)`) }
              />
              <ActionablePathList
                title="Likely generated files"
                values={scanEntries
                  .filter((entry) => entry.kind === 'file')
                  .filter((entry) => /\\(bin|obj|dist|build)\\/i.test(entry.path) || ['.dll', '.exe', '.pdb', '.map'].includes(entry.extension.toLowerCase()))
                  .slice(0, 12)
                  .map((entry) => entry.path)}
                onShowInList={onShowInList}
                onOpenLocation={onOpenLocation}
                onCopyName={onCopyName}
                onApplySearch={onApplySearch}
              />
              <ActionablePathList
                title="Logs to inspect"
                values={scanEntries
                  .filter((entry) => entry.kind === 'file' && entry.name.toLowerCase().includes('log'))
                  .slice(0, 12)
                  .map((entry) => entry.path)}
                onShowInList={onShowInList}
                onOpenLocation={onOpenLocation}
                onCopyName={onCopyName}
                onApplySearch={onApplySearch}
              />

              {projectInfo.isAtlasWorkspace && (
                <SimpleList
                  title="Atlas workspace checks"
                  values={[
                    'Check build output in bin/x64.',
                    'Check module host mapping in Modules/FileExplorer/FileExplorerHostView.xaml.cs.',
                    'Check matching React dist in Figma/Futuristic AI File Explorer/dist.',
                    'Check logs and recent CommandCenter mapping updates.',
                    'Check first before changing generated folders.',
                  ]}
                />
              )}
                </>
              )}
            </Panel>
          )}

          {selectedBrainAction === 'organize-folder' && (
            <Panel title="Organize Folder" badge="AI proposes — you approve — nothing moves automatically">
              <PanelPath path={currentPath} />
              <BrainDebugLine panelName="Organize Folder" itemCount={items.length} source={organizeSourceLabel} />

              <div className="rounded-lg border border-cyan-400/20 bg-gradient-to-b from-cyan-500/8 to-transparent p-3 space-y-2">
                <div className="text-xs text-white/70">Run Command</div>
                <textarea
                  value={commandDraft}
                  onChange={(event) => {
                    setCommandDraft(event.target.value);
                    setOrganizeInstruction(event.target.value);
                  }}
                  placeholder="Example: organize this Downloads folder, group installers, find sensitive files, extract archives..."
                  className="w-full min-h-[86px] resize-y px-3 py-2 bg-black/30 border border-white/10 rounded-lg text-xs text-white placeholder:text-white/35 focus:outline-none focus:border-cyan-400/50"
                />
                <div className="flex flex-wrap gap-1.5">
                  {['Organize Downloads', 'Group Installers', 'Extract Archives', 'Find Sensitive Files', 'Show Duplicates', 'Prepare for Backup', 'Rename Messy Files'].map((chip) => (
                    <button
                      key={chip}
                      onClick={() => {
                        setCommandDraft(chip);
                        setOrganizeInstruction(chip);
                        onRunAtlasCommand(chip);
                      }}
                      className="h-6 px-2 rounded-full border border-white/12 bg-white/5 text-[10px] text-white/65 hover:bg-white/10"
                    >
                      {chip}
                    </button>
                  ))}
                </div>
                <div className="flex items-center gap-2">
                  <button
                    onClick={() => onRunAtlasCommand(commandDraft.trim() || 'organize this folder')}
                    disabled={!currentPath || organizePlanLoading}
                    className="h-8 px-3 rounded-lg bg-cyan-500/20 border border-cyan-400/40 text-xs text-cyan-200 hover:bg-cyan-500/30 disabled:opacity-40 disabled:cursor-not-allowed"
                  >
                    {organizePlanLoading ? 'Running command...' : 'Run Command'}
                  </button>
                  <div className="text-[11px] text-white/45">State: {commandExecutionState}</div>
                </div>
              </div>

              {!organizePlanLoading && !organizePlan && (
                <div className="space-y-2">
                  <div className="text-xs text-white/70">Idle: run a command or choose a chip to generate a folder plan.</div>
                </div>
              )}

              {organizePlanLoading && (
                <div className="space-y-1">
                  <Muted text="Analyzing folder and generating organize plan..." />
                  <div className="text-xs text-white/35">This may take up to 45 seconds if AI is involved.</div>
                </div>
              )}

              {!organizePlanLoading && organizePlan?.error && (
                <div className="space-y-2">
                  <ErrorBox message={`${organizePlan.error}`} />
                  {useLocalFallback && (
                    <div className="text-xs text-cyan-200 bg-cyan-500/10 border border-cyan-400/25 rounded-lg p-2.5">
                      Local fallback plan - AI output could not be used as valid JSON.
                    </div>
                  )}
                  <button
                    onClick={() => onRunAtlasCommand(commandDraft.trim() || 'organize this folder')}
                    disabled={!currentPath}
                    className="h-7 px-3 rounded-lg bg-cyan-500/20 border border-cyan-400/40 text-xs text-cyan-200 hover:bg-cyan-500/30 disabled:opacity-40"
                  >
                    Retry
                  </button>
                </div>
              )}

              {!organizePlanLoading && organizePlan && (!organizePlan.error || useLocalFallback) && (
                <div className="space-y-3">
                  <div className="rounded-lg border border-white/10 bg-white/5 p-2.5 space-y-1 text-xs">
                    <div className="text-white/75">Command: {atlasCommand || 'organize this folder'}</div>
                    <div className="text-white/45">Result type: plan | intent: {atlasCommandIntent} | source: {organizeSourceLabel}</div>
                    <div className="text-white/45">Items analyzed: {items.length} | metadata only</div>
                  </div>

                  {parsedPlanError && <ErrorBox message={parsedPlanError} />}

                  {!activeOrganizePlan && <Muted text="No matching files found." />}

                  {activeOrganizePlan && (() => {
                    const plan = activeOrganizePlan;
                    const checkedActions = organizeActionEntries.filter((entry) => checkedActionIndices.has(entry.idx)).map((entry) => entry.action);
                    const safeBatch = safeActionEntries.slice(0, MAX_VISIBLE_ACTIONS_DEFAULT).map((entry) => entry.idx);
                    const opportunities = organizeGroups.reduce((sum, group) => sum + Math.max(group.examples.length, group.actionEntries.length), 0);

                    return (
                      <div className="space-y-2">
                        <div className="rounded-lg border border-white/10 bg-white/5 p-2.5 text-xs text-white/75">{plan.summary}</div>
                        <div className="text-[11px] text-white/45">Atlas found {opportunities} useful organization opportunities.</div>
                        <div className="text-[11px] text-white/45">{safeActionEntries.length} safe move suggestions.</div>
                        <div className="text-[11px] text-white/45">{reviewOnlyItemsCount} review-only items.</div>
                        <div className="text-[11px] text-cyan-200">Nothing will move until approved.</div>
                        <div className="text-[11px] text-white/45">Confidence: {plan.confidence}{useLocalFallback ? ' (local fallback)' : ''}</div>

                        {plan.warnings?.map((w, wi) => (
                          <div key={wi} className="text-[11px] text-amber-200 bg-amber-500/10 border border-amber-300/25 rounded px-2 py-1">{w}</div>
                        ))}

                        {hiddenActionCount > 0 && (
                          <div className="text-[11px] text-white/60">{hiddenActionCount} more available - refine or expand group.</div>
                        )}

                        <div className="flex flex-wrap items-center gap-2 pt-1">
                          <button
                            onClick={() => setCheckedActionIndices(new Set())}
                            className="h-6 px-2 rounded text-[10px] border border-white/12 bg-white/5 text-white/60 hover:bg-white/10"
                          >
                            Select none
                          </button>
                          <button
                            onClick={() => setCheckedActionIndices(new Set(safeBatch))}
                            className="h-6 px-2 rounded text-[10px] border border-white/12 bg-white/5 text-white/60 hover:bg-white/10"
                          >
                            Select safe first batch
                          </button>
                          <button
                            onClick={() => setShowAllOrganizeActions((value) => !value)}
                            className="h-6 px-2 rounded text-[10px] border border-white/12 bg-white/5 text-white/60 hover:bg-white/10"
                          >
                            {showAllOrganizeActions ? 'Use default view caps' : 'Show all expanded actions'}
                          </button>
                          <button
                            onClick={() => copyText([
                              `Command: ${atlasCommand || 'organize this folder'}`,
                              `Summary: ${plan.summary}`,
                              `Safe suggestions: ${safeActionEntries.length}`,
                              `Review-only items: ${reviewOnlyItemsCount}`,
                              ...organizeGroups.map((group) => `${group.name}: ${group.examples.join(', ')}`),
                            ].join('\n'))}
                            className="h-6 px-2 rounded text-[10px] border border-white/12 bg-white/5 text-white/60 hover:bg-white/10"
                          >
                            Copy plan
                          </button>
                          <button
                            onClick={() => onApproveOrganizeActions(checkedActions)}
                            disabled={checkedActions.length === 0 || organizeIsExecuting || applyLimitExceeded}
                            className="h-7 px-3 rounded-lg bg-cyan-500/20 border border-cyan-400/40 text-xs text-cyan-200 hover:bg-cyan-500/30 disabled:opacity-40 disabled:cursor-not-allowed"
                          >
                            {organizeIsExecuting ? 'Executing...' : `Apply selected (${checkedActions.length})`}
                          </button>
                        </div>

                        {applyLimitExceeded && (
                          <div className="text-[11px] text-amber-200 bg-amber-500/10 border border-amber-300/25 rounded px-2 py-1">Apply max 25 actions at a time.</div>
                        )}

                        <div className="space-y-2 pt-1">
                          {(() => {
                            let remaining = showAllOrganizeActions ? Number.MAX_SAFE_INTEGER : MAX_VISIBLE_ACTIONS_DEFAULT;

                            return organizeGroups.map((group) => {
                              const isExpanded = expandedOrganizeGroups.has(group.name);
                              const perGroupLimit = showAllOrganizeActions ? Number.MAX_SAFE_INTEGER : MAX_ACTIONS_PER_GROUP_DEFAULT;
                              let visibleEntries = isExpanded ? group.actionEntries.slice(0, perGroupLimit) : [];
                              if (!showAllOrganizeActions) {
                                visibleEntries = visibleEntries.slice(0, remaining);
                                remaining -= visibleEntries.length;
                              }

                              return (
                                <div key={group.name} className="rounded-lg border border-white/10 bg-white/5 p-2 space-y-2">
                                  <div className="flex items-center justify-between gap-2">
                                    <div className="text-xs text-white/80">{group.name}</div>
                                    <div className="text-[10px] text-white/45">{group.actionEntries.length} action(s) | {group.examples.length} example(s)</div>
                                  </div>
                                  <div className="text-[11px] text-white/55">{group.reason}</div>
                                  {group.examples.length > 0 && (
                                    <div className="text-[11px] text-white/45 truncate">Examples: {group.examples.slice(0, 5).join(', ')}</div>
                                  )}
                                  <div className="flex flex-wrap items-center gap-1">
                                    <button
                                      onClick={() => {
                                        const next = new Set(expandedOrganizeGroups);
                                        if (next.has(group.name)) next.delete(group.name); else next.add(group.name);
                                        setExpandedOrganizeGroups(next);
                                      }}
                                      className="h-6 px-2 rounded text-[10px] border border-white/12 bg-white/5 text-white/60 hover:bg-white/10"
                                    >
                                      {isExpanded ? 'Hide group' : 'Preview group'}
                                    </button>
                                    <button
                                      onClick={() => {
                                        const selected = group.actionEntries.filter((entry) => !entry.action.reviewOnly).slice(0, 5).map((entry) => entry.idx);
                                        setCheckedActionIndices((prev) => new Set([...prev, ...selected]));
                                      }}
                                      className="h-6 px-2 rounded text-[10px] border border-white/12 bg-white/5 text-white/60 hover:bg-white/10"
                                    >
                                      Select first 5
                                    </button>
                                    <button
                                      onClick={() => {
                                        const selected = visibleEntries.filter((entry) => !entry.action.reviewOnly).map((entry) => entry.idx);
                                        setCheckedActionIndices((prev) => new Set([...prev, ...selected]));
                                      }}
                                      className="h-6 px-2 rounded text-[10px] border border-white/12 bg-white/5 text-white/60 hover:bg-white/10"
                                    >
                                      Select all visible
                                    </button>
                                    <button
                                      onClick={() => copyText(group.examples.join('\n'))}
                                      className="h-6 px-2 rounded text-[10px] border border-white/12 bg-white/5 text-white/60 hover:bg-white/10"
                                    >
                                      Copy group names
                                    </button>
                                  </div>

                                  {isExpanded && visibleEntries.length === 0 && (
                                    <div className="text-[11px] text-white/45">No visible actions in this group.</div>
                                  )}

                                  {isExpanded && visibleEntries.map((entry) => {
                                    const action = entry.action;
                                    const sourceItem = items.find((item) => item.name === action.sourceName);
                                    const execStatus = organizeExecStatuses[String(entry.idx)];

                                    return (
                                      <div key={`${group.name}-${entry.idx}`} className="rounded-md border border-white/10 bg-white/5 p-2 text-xs space-y-1">
                                        <div className="flex items-start gap-2">
                                          <input
                                            type="checkbox"
                                            checked={checkedActionIndices.has(entry.idx)}
                                            disabled={organizeIsExecuting || action.reviewOnly}
                                            onChange={(event) => {
                                              const next = new Set(checkedActionIndices);
                                              if (event.target.checked) next.add(entry.idx); else next.delete(entry.idx);
                                              setCheckedActionIndices(next);
                                            }}
                                            className="mt-0.5 accent-cyan-400"
                                          />
                                          <div className="flex-1 min-w-0 space-y-0.5">
                                            <div className="text-white/80">{action.type}</div>
                                            <div className="text-white/55">Source: {action.sourceName || '-'}</div>
                                            <div className="text-white/55">Destination: {action.targetFolder || action.newName || '-'}</div>
                                            <div className="text-white/45">Reason: {action.reason}</div>
                                            <div className="text-[10px] text-cyan-200">Group: {group.name}</div>
                                            {action.reviewOnly && <div className="text-[10px] text-amber-200">Review-only item (not auto-selected)</div>}
                                            {execStatus && <div className="text-[10px] text-white/45">Status: {execStatus}</div>}
                                          </div>
                                        </div>
                                        <div className="flex items-center gap-1">
                                          <button
                                            onClick={() => sourceItem && onShowInList(sourceItem.path)}
                                            disabled={!sourceItem}
                                            className="h-5 px-2 rounded text-[10px] border border-white/12 bg-white/5 text-white/65 hover:bg-white/10 disabled:opacity-40"
                                          >
                                            Show in list
                                          </button>
                                          <button
                                            onClick={() => sourceItem && onOpenLocation(sourceItem.path)}
                                            disabled={!sourceItem}
                                            className="h-5 px-2 rounded text-[10px] border border-white/12 bg-white/5 text-white/65 hover:bg-white/10 disabled:opacity-40"
                                          >
                                            Open location
                                          </button>
                                          <button
                                            onClick={() => onCopyName(action.sourceName || action.targetFolder || action.newName)}
                                            className="h-5 px-2 rounded text-[10px] border border-white/12 bg-white/5 text-white/65 hover:bg-white/10"
                                          >
                                            Copy name
                                          </button>
                                          {atlasCommandIntent === 'archives' && sourceItem && sourceItem.kind === 'file' && ['.zip', '.rar', '.7z'].includes(sourceItem.extension.toLowerCase()) && (
                                            <button
                                              onClick={() => onExtractArchive(sourceItem.path, 'new-folder')}
                                              className="h-5 px-2 rounded text-[10px] border border-cyan-400/30 bg-cyan-500/10 text-cyan-200 hover:bg-cyan-500/20"
                                            >
                                              Extract archive
                                            </button>
                                          )}
                                        </div>
                                      </div>
                                    );
                                  })}
                                </div>
                              );
                            });
                          })()}
                        </div>
                      </div>
                    );
                  })()}
                </div>
              )}

              <div className="pt-1 border-t border-white/10 flex gap-2">
                <button
                  onClick={() => onRunAtlasCommand(commandDraft.trim() || 'organize this folder')}
                  disabled={!currentPath || organizePlanLoading}
                  className="h-7 px-3 rounded-lg bg-white/5 border border-white/10 text-xs text-white/60 hover:bg-white/10 disabled:opacity-40"
                >
                  {organizePlanLoading ? 'Running...' : organizePlan ? 'Run Again' : 'Run Command'}
                </button>
              </div>
            </Panel>
          )}

          {selectedBrainAction === 'action-plan' && (
            <Panel title="AI Action Plan" badge="Metadata-only AI call">
              <PanelPath path={currentPath} />
              <BrainDebugLine panelName={selectedPanelName} itemCount={items.length} source={selectedPanelSource} />
              {brainActionPlanLoading && <Muted text="Analyzing current folder metadata..." />}
              {brainActionPlan?.error && (
                <div className="space-y-1">
                  <ErrorBox message={brainActionPlan.error} />
                  <div className="text-xs text-white/45">Atlas AI did not return a result. Check provider settings or runtime logs.</div>
                </div>
              )}
              {brainActionPlan?.plan && <div className="text-sm text-white/85 whitespace-pre-wrap break-words">{brainActionPlan.plan}</div>}
              {!brainActionPlanLoading && !brainActionPlan?.plan && !brainActionPlan?.error && <Muted text="Waiting for AI action plan..." />}
            </Panel>
          )}
        </div>
      </div>
    </div>
  );
}

function Panel({ title, badge, children }: { title: string; badge: string; children: ReactNode }) {
  return (
    <div className="rounded-lg border border-white/10 bg-[#111] p-3 space-y-2">
      <div className="flex items-center justify-between gap-2">
        <div className="text-sm text-white">{title}</div>
        <span className="text-[10px] text-white/45 border border-white/12 bg-white/5 px-2 py-0.5 rounded-full">{badge}</span>
      </div>
      {children}
    </div>
  );
}

function PanelPath({ path }: { path: string }) {
  return <div className="text-[11px] text-white/45 break-all">Path: {path || 'No folder selected'}</div>;
}

function BrainDebugLine({ panelName, itemCount, source }: { panelName: string; itemCount: number; source: string }) {
  return (
    <div className="text-[11px] text-white/45 border border-white/10 bg-white/5 rounded-md p-2">
      selected panel: {panelName} | items analyzed: {itemCount} | mode: metadata only | source: {source || 'local'}
    </div>
  );
}

function ActionableNameList({
  title,
  values,
  items,
  onShowInList,
  onOpenLocation,
  onCopyName,
  onApplySearch,
}: {
  title: string;
  values: string[];
  items: FileItem[];
  onShowInList: (path: string) => void;
  onOpenLocation: (path: string) => void;
  onCopyName: (name: string) => void;
  onApplySearch: (query: string) => void;
}) {
  return (
    <div className="space-y-1">
      <div className="text-xs text-white/70">{title}</div>
      {values.length === 0 ? (
        <div className="text-xs text-white/45">None.</div>
      ) : (
        values.slice(0, 12).map((value) => {
          const matched = items.find((item) => item.name.toLowerCase() === value.toLowerCase()) ?? null;
          return (
            <div key={`${title}-${value}`} className="rounded-md border border-white/10 bg-white/5 p-2 text-xs text-white/65 space-y-1">
              <div className="break-all">{value}</div>
              <div className="flex items-center gap-1">
                <button className="h-5 px-2 rounded text-[10px] border border-white/12 bg-white/5 text-white/70 hover:bg-white/10" onClick={() => matched ? onShowInList(matched.path) : onApplySearch(value)}>
                  Show in list
                </button>
                <button className="h-5 px-2 rounded text-[10px] border border-white/12 bg-white/5 text-white/70 hover:bg-white/10" onClick={() => matched ? onOpenLocation(matched.path) : onApplySearch(value)}>
                  Open location
                </button>
                <button className="h-5 px-2 rounded text-[10px] border border-white/12 bg-white/5 text-white/70 hover:bg-white/10" onClick={() => onCopyName(value)}>
                  Copy name
                </button>
              </div>
            </div>
          );
        })
      )}
    </div>
  );
}

function ActionablePathList({
  title,
  values,
  onShowInList,
  onOpenLocation,
  onCopyName,
  onApplySearch,
}: {
  title: string;
  values: string[];
  onShowInList: (path: string) => void;
  onOpenLocation: (path: string) => void;
  onCopyName: (name: string) => void;
  onApplySearch: (query: string) => void;
}) {
  return (
    <div className="space-y-1">
      <div className="text-xs text-white/70">{title}</div>
      {values.length === 0 ? (
        <div className="text-xs text-white/45">None.</div>
      ) : (
        values.slice(0, 12).map((value) => (
          <div key={`${title}-${value}`} className="rounded-md border border-white/10 bg-white/5 p-2 text-xs text-white/65 space-y-1">
            <div className="break-all">{value}</div>
            <div className="flex items-center gap-1">
              <button className="h-5 px-2 rounded text-[10px] border border-white/12 bg-white/5 text-white/70 hover:bg-white/10" onClick={() => onShowInList(value)}>
                Show in list
              </button>
              <button className="h-5 px-2 rounded text-[10px] border border-white/12 bg-white/5 text-white/70 hover:bg-white/10" onClick={() => onOpenLocation(value)}>
                Open location
              </button>
              <button className="h-5 px-2 rounded text-[10px] border border-white/12 bg-white/5 text-white/70 hover:bg-white/10" onClick={() => onCopyName(value)}>
                Copy name
              </button>
            </div>
          </div>
        ))
      )}
      {values.length > 0 && (
        <button className="h-5 px-2 rounded text-[10px] border border-white/12 bg-white/5 text-white/70 hover:bg-white/10" onClick={() => onApplySearch('log')}>
          Apply filter
        </button>
      )}
    </div>
  );
}

function SimpleList({ title, values }: { title: string; values: string[] }) {
  return (
    <div className="space-y-1">
      <div className="text-xs text-white/70">{title}</div>
      {values.length === 0 ? (
        <div className="text-xs text-white/45">None.</div>
      ) : (
        values.slice(0, 12).map((value) => (
          <div key={`${title}-${value}`} className="text-xs text-white/55 break-all">- {value}</div>
        ))
      )}
    </div>
  );
}

function ActionableGroup({
  title,
  items,
  onShowInList,
  onOpenLocation,
  onCopyName,
}: {
  title: string;
  items: FileItem[];
  onShowInList: (path: string) => void;
  onOpenLocation: (path: string) => void;
  onCopyName: (name: string) => void;
}) {
  return (
    <div className="space-y-1.5">
      <div className="text-xs text-white/70">{title}</div>
      {items.length === 0 ? (
        <div className="text-xs text-white/45">None.</div>
      ) : (
        items.slice(0, 20).map((item) => (
          <div key={`${title}-${item.path}`} className="rounded-md border border-white/10 bg-white/5 p-2 text-xs text-white/75 space-y-1">
            <div className="flex items-center gap-2">
              <span className="truncate">{item.name}</span>
              <span className="ml-auto text-[10px] text-white/45">{formatBytes(item.sizeBytes)}</span>
            </div>
            <div className="flex items-center gap-1">
              <button className="h-5 px-2 rounded text-[10px] border border-white/12 bg-white/5 text-white/70 hover:bg-white/10" onClick={() => onShowInList(item.path)}>
                Show in list
              </button>
              <button className="h-5 px-2 rounded text-[10px] border border-white/12 bg-white/5 text-white/70 hover:bg-white/10" onClick={() => onOpenLocation(item.path)}>
                Open location
              </button>
              <button className="h-5 px-2 rounded text-[10px] border border-white/12 bg-white/5 text-white/70 hover:bg-white/10" onClick={() => onCopyName(item.name)}>
                Copy name
              </button>
            </div>
          </div>
        ))
      )}
    </div>
  );
}

function ErrorBox({ message }: { message: string }) {
  return <div className="text-xs text-amber-200 bg-amber-500/10 border border-amber-300/25 rounded-lg p-2.5">{message}</div>;
}

function Muted({ text }: { text: string }) {
  return <div className="text-xs text-white/50">{text}</div>;
}
