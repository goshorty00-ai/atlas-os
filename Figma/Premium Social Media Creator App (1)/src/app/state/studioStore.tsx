import {
  createContext,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from "react";

export type PlatformId =
  | "instagram"
  | "tiktok"
  | "youtube"
  | "facebook"
  | "twitter"
  | "linkedin"
  | "pinterest"
  | "threads"
  | "snapchat";

export type AssetKind = "image" | "video" | "audio" | "logo" | "sticker" | "overlay" | "template" | "voiceover";

export type LayerType =
  | "text"
  | "shape"
  | "asset"
  | "video"
  | "audio"
  | "sticker"
  | "overlay"
  | "frame"
  | "mask";

export type BlendMode = "normal" | "multiply" | "screen" | "overlay" | "soft-light" | "darken" | "lighten";
export type DraftStatus = "draft" | "scheduled" | "published-manual";
export type DraftPipelineStage = "concept" | "design" | "review" | "approved" | "scheduled" | "published";
export type ModelProviderId = "gpt" | "claude" | "gemini" | "elevenlabs";
export type AISurface = "create" | "video" | "voice" | "planner" | "library";
export type AITaskType =
  | "caption"
  | "ideas"
  | "hook"
  | "script"
  | "visual-concept"
  | "carousel-structure"
  | "ad-copy"
  | "thumbnail-brainstorm"
  | "rewrite-brand-tone"
  | "platform-adaptation"
  | "voiceover"
  | "scene-suggestion"
  | "campaign-pack";

export type AIBriefStatus = "queued" | "retrying" | "running" | "succeeded" | "failed" | "timed-out" | "cancelled";

export type AIResultSectionKind = "text" | "cta" | "scene" | "slide" | "platform" | "beat" | "script";
export type AIResultOptionKind =
  | "caption-candidate"
  | "hook-candidate"
  | "script-section"
  | "platform-rewrite"
  | "scene-group"
  | "visual-concept"
  | "variant"
  | "voiceover-script"
  | "cta-option"
  | "idea";

export interface AICaptionResultSchema {
  type: "caption";
  hook: string;
  body: string;
  cta: string;
  hashtags: string[];
  toneNotes: string;
  audienceNotes: string;
}

export interface AIHookResultSchema {
  type: "hook";
  primaryHook: string;
  alternates: string[];
  styleTags: string[];
  usageNotes: string;
}

export interface AIScriptResultSchema {
  type: "script";
  title: string;
  opening: string;
  beats: string[];
  cta: string;
  pacingNotes: string;
}

export interface AICarouselSlideSchema {
  title: string;
  body: string;
}

export interface AICarouselResultSchema {
  type: "carousel";
  title: string;
  slides: AICarouselSlideSchema[];
  ctaSlide: string;
  summary: string;
}

export interface AIVisualConceptResultSchema {
  type: "visual-concept";
  conceptTitle: string;
  palette: string[];
  composition: string;
  mood: string;
  references: string[];
  shotNotes: string[];
  platformNotes: string;
}

export interface AIPlatformAdaptationResultSchema {
  type: "platform-adaptation";
  sourcePlatform: string;
  targetPlatform: string;
  adaptedBody: string;
  adaptedCta: string;
  lengthNotes: string;
}

export interface AIVariantItemSchema {
  title: string;
  body: string;
  hook: string;
  cta: string;
  keyDifference: string;
}

export interface AIVariantPackResultSchema {
  type: "variant-pack";
  variants: AIVariantItemSchema[];
  keyDifferences: string[];
  audienceFit: string;
  recommendedUse: string;
}

export interface AIVoiceoverResultSchema {
  type: "voiceover";
  title: string;
  script: string;
  pacingNotes: string;
  pronunciationNotes: string[];
  emotion: string;
  targetFormat: string;
}

export type AIResultSchema =
  | AICaptionResultSchema
  | AIHookResultSchema
  | AIScriptResultSchema
  | AICarouselResultSchema
  | AIVisualConceptResultSchema
  | AIPlatformAdaptationResultSchema
  | AIVariantPackResultSchema
  | AIVoiceoverResultSchema;

export interface AIResultSection {
  id: string;
  label: string;
  kind: AIResultSectionKind;
  content: string;
}

export interface AIResultOption {
  id: string;
  kind: AIResultOptionKind;
  label: string;
  summary: string;
  content: string;
  sections: AIResultSection[];
  schema?: AIResultSchema;
  sourceIndex: number;
  isFavorite: boolean;
  isPinned: boolean;
  metadata: Record<string, string>;
}

export interface AIResultGroup {
  id: string;
  label: string;
  kind: AIResultOptionKind;
  description: string;
  sourceTaskType: AITaskType;
  providerId: ModelProviderId;
  modelId: string;
  createdAt: string;
  sourceResponse: string;
  options: AIResultOption[];
}

export interface PlatformDefinition {
  id: PlatformId;
  label: string;
  accent: string;
  supportedTypes: string[];
}

export interface FormatPreset {
  id: string;
  label: string;
  width: number;
  height: number;
  contentType: string;
  recommendedPlatforms: PlatformId[];
  exportFormats: string[];
  motion: boolean;
}

export interface UserProfile {
  firstName: string;
  lastName: string;
  email: string;
  company: string;
  role: string;
  bio: string;
}

export interface BrandColor {
  id: string;
  name: string;
  value: string;
}

export interface BrandFont {
  id: string;
  family: string;
  role: string;
  weight: string;
}

export interface BrandGradient {
  id: string;
  name: string;
  from: string;
  to: string;
}

export interface BrandKit {
  brandName: string;
  industry: string;
  tone: string;
  audience: string;
  voiceNotes: string[];
  colors: BrandColor[];
  fonts: BrandFont[];
  gradients: BrandGradient[];
  logoAssetIds: string[];
  ctaStyles: string[];
  layoutPrinciples: string[];
  watermarkText: string;
  watermarkEnabled: boolean;
}

export interface ConnectedAccount {
  id: string;
  platformId: PlatformId;
  displayName: string;
  handle: string;
  authStatus: "not-configured" | "configured-local" | "connected";
  scopes: string[];
  notes: string;
  supportedContentTypes: string[];
  canPublish: boolean;
  canSchedule: boolean;
  syncHealth: "healthy" | "attention" | "disconnected";
  reconnectState: "idle" | "needs-auth" | "reconnecting";
  lastSyncedAt?: string;
  createdAt: string;
}

export interface MediaAsset {
  id: string;
  name: string;
  kind: AssetKind;
  mimeType: string;
  sizeBytes: number;
  createdAt: string;
  favorite: boolean;
  dataUrl?: string;
  storageMode: "embedded" | "metadata-only";
  folder: string;
  tags: string[];
  source: "upload" | "ai-generated" | "voice-studio" | "template-extract";
  durationMs?: number;
  transcript?: string;
}

export interface DraftCaptionVariantSet {
  id: string;
  label: string;
  captions: string[];
  platformId?: PlatformId;
  createdAt: string;
}

export interface DraftPlatformVersion {
  platformId: PlatformId;
  primaryCaption: string;
  captionAlternates: string[];
  hook: string;
  adaptation: string;
  preservedOriginal?: string;
  lastComparedCandidate?: string;
}

export interface DraftVisualDirection {
  id: string;
  title: string;
  palette: string[];
  mood: string;
  composition: string;
  references: string[];
  shotNotes: string[];
  platformNotes: string;
  appliedToSceneId?: string;
  createdAt: string;
}

export interface DraftVariantPackItem {
  title: string;
  body: string;
  hook: string;
  cta: string;
  keyDifference: string;
}

export interface DraftVariantPack {
  id: string;
  label: string;
  variants: DraftVariantPackItem[];
  keyDifferences: string[];
  audienceFit: string;
  recommendedUse: string;
  createdAt: string;
}

export interface DraftScene {
  id: string;
  name: string;
  durationMs: number;
  background: string;
  transition: "cut" | "fade" | "slide" | "zoom";
  layerIds: string[];
  headline?: string;
  body?: string;
  cta?: string;
  hook?: string;
  narration?: string;
  sceneType?: "default" | "script" | "carousel-slide" | "visual-direction" | "voiceover";
  visualDirectionId?: string;
}

export interface DraftLayer {
  id: string;
  type: LayerType;
  name: string;
  sceneId?: string;
  visible: boolean;
  locked: boolean;
  x: number;
  y: number;
  width: number;
  height: number;
  rotation: number;
  opacity: number;
  color?: string;
  gradient?: string;
  text?: string;
  fontSize?: number;
  fontWeight?: number;
  assetId?: string;
  blendMode?: BlendMode;
  filter?: "none" | "warm" | "cool" | "mono" | "dramatic" | "vivid";
  animation?: "none" | "fade-in" | "slide-up" | "pop" | "drift" | "type-on";
  transition?: "cut" | "fade" | "slide" | "zoom";
  assetFit?: "cover" | "contain" | "fill";
  cropX?: number;
  cropY?: number;
  cropZoom?: number;
  cornerRadius?: number;
  startMs?: number;
  endMs?: number;
}

export interface StudioDraft {
  id: string;
  title: string;
  formatId: string;
  width: number;
  height: number;
  contentType: string;
  background: string;
  durationMs: number;
  caption: string;
  primaryHook: string;
  alternateHooks: string[];
  captionVariants: string[];
  captionVariantSets: DraftCaptionVariantSet[];
  alternatePostVersions: string[];
  platformAdaptations: Partial<Record<PlatformId, string>>;
  platformCaptions: Partial<Record<PlatformId, string>>;
  platformVersions: Partial<Record<PlatformId, DraftPlatformVersion>>;
  notes: string;
  linkedPlatformIds: PlatformId[];
  linkedAssetIds: string[];
  linkedVoiceProjectIds: string[];
  layers: DraftLayer[];
  scenes?: DraftScene[];
  conceptBoard: DraftVisualDirection[];
  variantPacks: DraftVariantPack[];
  activeSceneId?: string;
  status: DraftStatus;
  scheduledFor?: string;
  updatedAt: string;
  createdAt: string;
  variationOfDraftId?: string;
  savedAsTemplate?: boolean;
  tags: string[];
  campaign?: string;
  pipelineStage: DraftPipelineStage;
  version: number;
}

export interface AIBrief {
  id: string;
  providerId: ModelProviderId;
  modelId: string;
  taskType: AITaskType;
  platformId: PlatformId;
  objective: string;
  tone: string;
  contentType: string;
  brief: string;
  requestPacket: string;
  draftId?: string;
  targetSurface: AISurface;
  variantsRequested: number;
  status: AIBriefStatus;
  requestId?: string;
  routeSummary?: string;
  errorMessage?: string;
  startedAt?: string;
  completedAt?: string;
  retryCount?: number;
  timeoutMs?: number;
  lastDurationMs?: number;
  responseText?: string;
  resultGroups?: AIResultGroup[];
  selectedResultIds?: string[];
  createdAt: string;
}

export interface VoiceProject {
  id: string;
  title: string;
  script: string;
  providerId: ModelProviderId;
  modelId: string;
  voiceName: string;
  stylePrompt: string;
  emotion: string;
  speed: number;
  pacing: number;
  linkedDraftId?: string;
  linkedSceneId?: string;
  selectedAssetId?: string;
  narrationSections: string[];
  timelineSceneIds: string[];
  versions: string[];
  status: "draft" | "ready-to-generate" | "waiting-backend";
  createdAt: string;
  updatedAt: string;
}

export interface StudioState {
  profile: UserProfile;
  brandKit: BrandKit;
  accounts: ConnectedAccount[];
  assets: MediaAsset[];
  drafts: StudioDraft[];
  aiBriefs: AIBrief[];
  voiceProjects: VoiceProject[];
  selectedDraftId?: string;
}

const STORAGE_KEY = "atlas.social-studio.v2";

export const PLATFORM_DEFINITIONS: PlatformDefinition[] = [
  {
    id: "instagram",
    label: "Instagram",
    accent: "from-pink-500 to-rose-500",
    supportedTypes: ["Posts", "Carousels", "Stories", "Reels"],
  },
  {
    id: "tiktok",
    label: "TikTok",
    accent: "from-gray-900 to-gray-700",
    supportedTypes: ["Videos", "Stories", "Ads"],
  },
  {
    id: "youtube",
    label: "YouTube",
    accent: "from-red-500 to-rose-500",
    supportedTypes: ["Videos", "Shorts", "Thumbnails"],
  },
  {
    id: "facebook",
    label: "Facebook",
    accent: "from-blue-600 to-blue-500",
    supportedTypes: ["Posts", "Stories", "Reels", "Ads"],
  },
  {
    id: "twitter",
    label: "X / Twitter",
    accent: "from-sky-500 to-cyan-500",
    supportedTypes: ["Posts", "Threads", "Media"],
  },
  {
    id: "linkedin",
    label: "LinkedIn",
    accent: "from-blue-800 to-blue-600",
    supportedTypes: ["Posts", "Articles", "Banners"],
  },
  {
    id: "pinterest",
    label: "Pinterest",
    accent: "from-red-600 to-rose-600",
    supportedTypes: ["Pins", "Boards", "Idea Pins"],
  },
  {
    id: "threads",
    label: "Threads",
    accent: "from-zinc-800 to-zinc-600",
    supportedTypes: ["Posts", "Replies", "Media"],
  },
  {
    id: "snapchat",
    label: "Snapchat",
    accent: "from-yellow-400 to-amber-300",
    supportedTypes: ["Stories", "Spotlight", "Ads"],
  },
];

export const FORMAT_PRESETS: FormatPreset[] = [
  {
    id: "instagram-post",
    label: "Instagram Post",
    width: 1080,
    height: 1080,
    contentType: "Post",
    recommendedPlatforms: ["instagram", "facebook"],
    exportFormats: ["PNG", "JPG"],
    motion: false,
  },
  {
    id: "carousel-slide",
    label: "Carousel Slide",
    width: 1080,
    height: 1080,
    contentType: "Carousel",
    recommendedPlatforms: ["instagram", "facebook", "linkedin"],
    exportFormats: ["PNG", "JPG"],
    motion: false,
  },
  {
    id: "instagram-story",
    label: "Instagram Story",
    width: 1080,
    height: 1920,
    contentType: "Story",
    recommendedPlatforms: ["instagram", "facebook", "threads", "snapchat"],
    exportFormats: ["PNG", "JPG", "MP4"],
    motion: true,
  },
  {
    id: "instagram-reel",
    label: "Instagram Reel",
    width: 1080,
    height: 1920,
    contentType: "Reel",
    recommendedPlatforms: ["instagram", "tiktok"],
    exportFormats: ["MP4"],
    motion: true,
  },
  {
    id: "short-video",
    label: "Short Video",
    width: 1080,
    height: 1920,
    contentType: "Short Video",
    recommendedPlatforms: ["instagram", "tiktok", "youtube", "snapchat"],
    exportFormats: ["MP4"],
    motion: true,
  },
  {
    id: "youtube-short",
    label: "YouTube Short",
    width: 1080,
    height: 1920,
    contentType: "Short",
    recommendedPlatforms: ["youtube", "tiktok"],
    exportFormats: ["MP4"],
    motion: true,
  },
  {
    id: "youtube-thumbnail",
    label: "YouTube Thumbnail",
    width: 1280,
    height: 720,
    contentType: "Thumbnail",
    recommendedPlatforms: ["youtube"],
    exportFormats: ["PNG", "JPG"],
    motion: false,
  },
  {
    id: "ad-creative",
    label: "Ad Creative",
    width: 1080,
    height: 1350,
    contentType: "Ad",
    recommendedPlatforms: ["instagram", "facebook", "linkedin"],
    exportFormats: ["PNG", "JPG", "MP4"],
    motion: true,
  },
  {
    id: "linkedin-banner",
    label: "LinkedIn Banner",
    width: 1584,
    height: 396,
    contentType: "Banner",
    recommendedPlatforms: ["linkedin"],
    exportFormats: ["PNG", "JPG"],
    motion: false,
  },
  {
    id: "promo-banner",
    label: "Promo Banner",
    width: 1600,
    height: 900,
    contentType: "Promo Banner",
    recommendedPlatforms: ["facebook", "linkedin", "twitter"],
    exportFormats: ["PNG", "JPG", "MP4"],
    motion: true,
  },
];

function makeId(prefix: string) {
  if (typeof crypto !== "undefined" && "randomUUID" in crypto) {
    return `${prefix}-${crypto.randomUUID()}`;
  }

  return `${prefix}-${Math.random().toString(36).slice(2, 10)}`;
}

function nowIso() {
  return new Date().toISOString();
}

export function getFormatPreset(formatId: string) {
  return FORMAT_PRESETS.find((preset) => preset.id === formatId) ?? FORMAT_PRESETS[0];
}

export function createDraftFromPreset(formatId = FORMAT_PRESETS[0].id): StudioDraft {
  const preset = getFormatPreset(formatId);
  const id = makeId("draft");
  const sceneId = makeId("scene");
  const timestamp = nowIso();

  return {
    id,
    title: `${preset.label} Draft`,
    formatId: preset.id,
    width: preset.width,
    height: preset.height,
    contentType: preset.contentType,
    background: "#ffffff",
    durationMs: preset.motion ? 15000 : 5000,
    caption: "",
    primaryHook: "",
    alternateHooks: [],
    captionVariants: [],
    captionVariantSets: [],
    alternatePostVersions: [],
    platformAdaptations: {},
    platformCaptions: {},
    platformVersions: {},
    notes: "",
    linkedPlatformIds: [...preset.recommendedPlatforms],
    linkedAssetIds: [],
    linkedVoiceProjectIds: [],
    layers: [],
    scenes: [
      {
        id: sceneId,
        name: "Scene 1",
        durationMs: preset.motion ? 5000 : 3000,
        background: "#ffffff",
        transition: "cut",
        layerIds: [],
        sceneType: "default",
      },
    ],
    conceptBoard: [],
    variantPacks: [],
    activeSceneId: sceneId,
    status: "draft",
    createdAt: timestamp,
    updatedAt: timestamp,
    tags: [],
    pipelineStage: "concept",
    version: 1,
  };
}

const defaultState: StudioState = {
  profile: {
    firstName: "",
    lastName: "",
    email: "",
    company: "",
    role: "",
    bio: "",
  },
  brandKit: {
    brandName: "",
    industry: "",
    tone: "",
    audience: "",
    voiceNotes: [],
    colors: [],
    fonts: [],
    gradients: [],
    logoAssetIds: [],
    ctaStyles: [],
    layoutPrinciples: [],
    watermarkText: "",
    watermarkEnabled: false,
  },
  accounts: [],
  assets: [],
  drafts: [],
  aiBriefs: [],
  voiceProjects: [],
  selectedDraftId: undefined,
};

function normalizeAccount(account: Partial<ConnectedAccount>): ConnectedAccount {
  return {
    id: account.id ?? makeId("account"),
    platformId: account.platformId ?? "instagram",
    displayName: account.displayName ?? "",
    handle: account.handle ?? "",
    authStatus: account.authStatus ?? "configured-local",
    scopes: account.scopes ?? [],
    notes: account.notes ?? "",
    supportedContentTypes: account.supportedContentTypes ?? [],
    canPublish: account.canPublish ?? false,
    canSchedule: account.canSchedule ?? false,
    syncHealth: account.syncHealth ?? "attention",
    reconnectState: account.reconnectState ?? "idle",
    lastSyncedAt: account.lastSyncedAt,
    createdAt: account.createdAt ?? nowIso(),
  };
}

function normalizeAsset(asset: Partial<MediaAsset>): MediaAsset {
  return {
    id: asset.id ?? makeId("asset"),
    name: asset.name ?? "Untitled asset",
    kind: asset.kind ?? "image",
    mimeType: asset.mimeType ?? "application/octet-stream",
    sizeBytes: asset.sizeBytes ?? 0,
    createdAt: asset.createdAt ?? nowIso(),
    favorite: asset.favorite ?? false,
    dataUrl: asset.dataUrl,
    storageMode: asset.storageMode ?? "metadata-only",
    folder: asset.folder ?? "Library",
    tags: asset.tags ?? [],
    source: asset.source ?? "upload",
    durationMs: asset.durationMs,
    transcript: asset.transcript,
  };
}

function normalizeScene(scene: Partial<DraftScene>): DraftScene {
  return {
    id: scene.id ?? makeId("scene"),
    name: scene.name ?? "Scene",
    durationMs: scene.durationMs ?? 3000,
    background: scene.background ?? "#ffffff",
    transition: scene.transition ?? "cut",
    layerIds: scene.layerIds ?? [],
    headline: scene.headline ?? "",
    body: scene.body ?? "",
    cta: scene.cta ?? "",
    hook: scene.hook ?? "",
    narration: scene.narration ?? "",
    sceneType: scene.sceneType ?? "default",
    visualDirectionId: scene.visualDirectionId,
  };
}

function normalizeCaptionVariantSet(set: Partial<DraftCaptionVariantSet>): DraftCaptionVariantSet {
  return {
    id: set.id ?? makeId("caption-set"),
    label: set.label ?? "Caption Set",
    captions: set.captions ?? [],
    platformId: set.platformId,
    createdAt: set.createdAt ?? nowIso(),
  };
}

function normalizePlatformVersion(version: Partial<DraftPlatformVersion>, platformId: PlatformId): DraftPlatformVersion {
  return {
    platformId,
    primaryCaption: version.primaryCaption ?? "",
    captionAlternates: version.captionAlternates ?? [],
    hook: version.hook ?? "",
    adaptation: version.adaptation ?? "",
    preservedOriginal: version.preservedOriginal,
    lastComparedCandidate: version.lastComparedCandidate,
  };
}

function normalizeVisualDirection(direction: Partial<DraftVisualDirection>): DraftVisualDirection {
  return {
    id: direction.id ?? makeId("visual-direction"),
    title: direction.title ?? "Visual Direction",
    palette: direction.palette ?? [],
    mood: direction.mood ?? "",
    composition: direction.composition ?? "",
    references: direction.references ?? [],
    shotNotes: direction.shotNotes ?? [],
    platformNotes: direction.platformNotes ?? "",
    appliedToSceneId: direction.appliedToSceneId,
    createdAt: direction.createdAt ?? nowIso(),
  };
}

function normalizeVariantPack(pack: Partial<DraftVariantPack>): DraftVariantPack {
  return {
    id: pack.id ?? makeId("variant-pack"),
    label: pack.label ?? "Variant Pack",
    variants: pack.variants ?? [],
    keyDifferences: pack.keyDifferences ?? [],
    audienceFit: pack.audienceFit ?? "",
    recommendedUse: pack.recommendedUse ?? "",
    createdAt: pack.createdAt ?? nowIso(),
  };
}

function normalizeDraft(draft: Partial<StudioDraft>): StudioDraft {
  const preset = getFormatPreset(draft.formatId ?? FORMAT_PRESETS[0].id);
  const timestamp = nowIso();
  const sceneId = draft.activeSceneId ?? draft.scenes?.[0]?.id ?? makeId("scene");

  return {
    id: draft.id ?? makeId("draft"),
    title: draft.title ?? `${preset.label} Draft`,
    formatId: draft.formatId ?? preset.id,
    width: draft.width ?? preset.width,
    height: draft.height ?? preset.height,
    contentType: draft.contentType ?? preset.contentType,
    background: draft.background ?? "#ffffff",
    durationMs: draft.durationMs ?? (preset.motion ? 15000 : 5000),
    caption: draft.caption ?? "",
    notes: draft.notes ?? "",
    linkedPlatformIds: draft.linkedPlatformIds ?? [...preset.recommendedPlatforms],
    linkedAssetIds: draft.linkedAssetIds ?? [],
    layers: draft.layers ?? [],
    scenes:
      (draft.scenes?.map(normalizeScene)) ?? [
        {
          id: sceneId,
          name: "Scene 1",
          durationMs: preset.motion ? 5000 : 3000,
          background: draft.background ?? "#ffffff",
          transition: "cut",
          layerIds: [],
          sceneType: "default",
        },
      ],
    activeSceneId: sceneId,
    status: draft.status ?? "draft",
    scheduledFor: draft.scheduledFor,
    createdAt: draft.createdAt ?? timestamp,
    updatedAt: draft.updatedAt ?? timestamp,
    primaryHook: draft.primaryHook ?? "",
    alternateHooks: draft.alternateHooks ?? [],
    captionVariants: draft.captionVariants ?? [],
    captionVariantSets: (draft.captionVariantSets ?? []).map(normalizeCaptionVariantSet),
    alternatePostVersions: draft.alternatePostVersions ?? [],
    platformAdaptations: draft.platformAdaptations ?? {},
    platformCaptions: draft.platformCaptions ?? {},
    platformVersions: Object.fromEntries(
      Object.entries(draft.platformVersions ?? {}).map(([platformId, version]) => [platformId, normalizePlatformVersion(version ?? {}, platformId as PlatformId)]),
    ) as Partial<Record<PlatformId, DraftPlatformVersion>>,
    linkedVoiceProjectIds: draft.linkedVoiceProjectIds ?? [],
    conceptBoard: (draft.conceptBoard ?? []).map(normalizeVisualDirection),
    variantPacks: (draft.variantPacks ?? []).map(normalizeVariantPack),
    variationOfDraftId: draft.variationOfDraftId,
    savedAsTemplate: draft.savedAsTemplate,
    tags: draft.tags ?? [],
    campaign: draft.campaign,
    pipelineStage: draft.pipelineStage ?? "concept",
    version: draft.version ?? 1,
  };
}

function normalizeBrief(brief: Partial<AIBrief>): AIBrief {
  const legacyStatus = typeof (brief as Record<string, unknown>).status === "string"
    ? ((brief as Record<string, unknown>).status as string)
    : undefined;
  const normalizedStatus: AIBriefStatus = legacyStatus === "ready" || legacyStatus === "sent-to-atlas" || legacyStatus === "waiting-backend"
    ? legacyStatus === "ready"
      ? "queued"
      : legacyStatus === "sent-to-atlas"
        ? "running"
        : "queued"
    : brief.status === "retrying" || brief.status === "running" || brief.status === "succeeded" || brief.status === "failed" || brief.status === "timed-out" || brief.status === "cancelled"
      ? brief.status
      : "queued";

  return {
    id: brief.id ?? makeId("brief"),
    providerId: brief.providerId ?? "gpt",
    modelId: brief.modelId ?? "gpt-5.4",
    taskType: brief.taskType ?? "ideas",
    platformId: brief.platformId ?? "instagram",
    objective: brief.objective ?? "Creative request",
    tone: brief.tone ?? "default",
    contentType: brief.contentType ?? "Post",
    brief: brief.brief ?? "",
    requestPacket: brief.requestPacket ?? "",
    draftId: brief.draftId,
    targetSurface: brief.targetSurface ?? "create",
    variantsRequested: brief.variantsRequested ?? 1,
    status: normalizedStatus,
    requestId: brief.requestId,
    routeSummary: brief.routeSummary ?? "",
    errorMessage: brief.errorMessage ?? "",
    startedAt: brief.startedAt,
    completedAt: brief.completedAt,
    retryCount: brief.retryCount ?? 0,
    timeoutMs: brief.timeoutMs ?? 90000,
    lastDurationMs: brief.lastDurationMs,
    responseText: brief.responseText ?? "",
    resultGroups: (brief.resultGroups ?? []).map(normalizeResultGroup),
    selectedResultIds: brief.selectedResultIds ?? [],
    createdAt: brief.createdAt ?? nowIso(),
  };
}

function normalizeResultSection(section: Partial<AIResultSection>): AIResultSection {
  return {
    id: section.id ?? makeId("result-section"),
    label: section.label ?? "Section",
    kind: section.kind ?? "text",
    content: section.content ?? "",
  };
}

function normalizeStringArray(values: unknown) {
  return Array.isArray(values) ? values.map((value) => String(value ?? "").trim()).filter(Boolean) : [];
}

function normalizeResultSchema(schema: unknown): AIResultSchema | undefined {
  if (!schema || typeof schema !== "object") {
    return undefined;
  }

  const record = schema as Record<string, unknown>;

  switch (record.type) {
    case "caption":
      return {
        type: "caption",
        hook: String(record.hook ?? ""),
        body: String(record.body ?? ""),
        cta: String(record.cta ?? ""),
        hashtags: normalizeStringArray(record.hashtags),
        toneNotes: String(record.toneNotes ?? ""),
        audienceNotes: String(record.audienceNotes ?? ""),
      };
    case "hook":
      return {
        type: "hook",
        primaryHook: String(record.primaryHook ?? ""),
        alternates: normalizeStringArray(record.alternates),
        styleTags: normalizeStringArray(record.styleTags),
        usageNotes: String(record.usageNotes ?? ""),
      };
    case "script":
      return {
        type: "script",
        title: String(record.title ?? ""),
        opening: String(record.opening ?? ""),
        beats: normalizeStringArray(record.beats),
        cta: String(record.cta ?? ""),
        pacingNotes: String(record.pacingNotes ?? ""),
      };
    case "carousel":
      return {
        type: "carousel",
        title: String(record.title ?? ""),
        slides: Array.isArray(record.slides)
          ? record.slides.map((slide) => ({
            title: String((slide as Record<string, unknown>)?.title ?? ""),
            body: String((slide as Record<string, unknown>)?.body ?? ""),
          }))
          : [],
        ctaSlide: String(record.ctaSlide ?? ""),
        summary: String(record.summary ?? ""),
      };
    case "visual-concept":
      return {
        type: "visual-concept",
        conceptTitle: String(record.conceptTitle ?? ""),
        palette: normalizeStringArray(record.palette),
        composition: String(record.composition ?? ""),
        mood: String(record.mood ?? ""),
        references: normalizeStringArray(record.references),
        shotNotes: normalizeStringArray(record.shotNotes),
        platformNotes: String(record.platformNotes ?? ""),
      };
    case "platform-adaptation":
      return {
        type: "platform-adaptation",
        sourcePlatform: String(record.sourcePlatform ?? ""),
        targetPlatform: String(record.targetPlatform ?? ""),
        adaptedBody: String(record.adaptedBody ?? ""),
        adaptedCta: String(record.adaptedCta ?? ""),
        lengthNotes: String(record.lengthNotes ?? ""),
      };
    case "variant-pack":
      return {
        type: "variant-pack",
        variants: Array.isArray(record.variants)
          ? record.variants.map((variant) => ({
            title: String((variant as Record<string, unknown>)?.title ?? ""),
            body: String((variant as Record<string, unknown>)?.body ?? ""),
            hook: String((variant as Record<string, unknown>)?.hook ?? ""),
            cta: String((variant as Record<string, unknown>)?.cta ?? ""),
            keyDifference: String((variant as Record<string, unknown>)?.keyDifference ?? ""),
          }))
          : [],
        keyDifferences: normalizeStringArray(record.keyDifferences),
        audienceFit: String(record.audienceFit ?? ""),
        recommendedUse: String(record.recommendedUse ?? ""),
      };
    case "voiceover":
      return {
        type: "voiceover",
        title: String(record.title ?? ""),
        script: String(record.script ?? ""),
        pacingNotes: String(record.pacingNotes ?? ""),
        pronunciationNotes: normalizeStringArray(record.pronunciationNotes),
        emotion: String(record.emotion ?? ""),
        targetFormat: String(record.targetFormat ?? ""),
      };
    default:
      return undefined;
  }
}

function normalizeResultOption(option: Partial<AIResultOption>): AIResultOption {
  return {
    id: option.id ?? makeId("result-option"),
    kind: option.kind ?? "idea",
    label: option.label ?? "Option",
    summary: option.summary ?? "",
    content: option.content ?? "",
    sections: (option.sections ?? []).map(normalizeResultSection),
    schema: normalizeResultSchema(option.schema),
    sourceIndex: option.sourceIndex ?? 0,
    isFavorite: option.isFavorite ?? false,
    isPinned: option.isPinned ?? false,
    metadata: option.metadata ?? {},
  };
}

function normalizeResultGroup(group: Partial<AIResultGroup>): AIResultGroup {
  return {
    id: group.id ?? makeId("result-group"),
    label: group.label ?? "Structured Results",
    kind: group.kind ?? "idea",
    description: group.description ?? "",
    sourceTaskType: group.sourceTaskType ?? "ideas",
    providerId: group.providerId ?? "gpt",
    modelId: group.modelId ?? "gpt-5.4",
    createdAt: group.createdAt ?? nowIso(),
    sourceResponse: group.sourceResponse ?? "",
    options: (group.options ?? []).map(normalizeResultOption),
  };
}

function normalizeVoiceProject(project: Partial<VoiceProject>): VoiceProject {
  const timestamp = nowIso();
  return {
    id: project.id ?? makeId("voice"),
    title: project.title ?? "Voiceover Session",
    script: project.script ?? "",
    providerId: project.providerId ?? "elevenlabs",
    modelId: project.modelId ?? "eleven_multilingual_v2",
    voiceName: project.voiceName ?? "",
    stylePrompt: project.stylePrompt ?? "",
    emotion: project.emotion ?? "Neutral",
    speed: project.speed ?? 1,
    pacing: project.pacing ?? 1,
    linkedDraftId: project.linkedDraftId,
    linkedSceneId: project.linkedSceneId,
    selectedAssetId: project.selectedAssetId,
    narrationSections: project.narrationSections ?? [],
    timelineSceneIds: project.timelineSceneIds ?? [],
    versions: project.versions ?? [],
    status: project.status ?? "draft",
    createdAt: project.createdAt ?? timestamp,
    updatedAt: project.updatedAt ?? timestamp,
  };
}

function loadState(): StudioState {
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (!raw) {
      return defaultState;
    }

    const parsed = JSON.parse(raw) as Partial<StudioState>;
    return {
      ...defaultState,
      ...parsed,
      profile: { ...defaultState.profile, ...(parsed.profile ?? {}) },
      brandKit: { ...defaultState.brandKit, ...(parsed.brandKit ?? {}) },
      accounts: (parsed.accounts ?? []).map(normalizeAccount),
      assets: (parsed.assets ?? []).map(normalizeAsset),
      drafts: (parsed.drafts ?? []).map(normalizeDraft),
      aiBriefs: (parsed.aiBriefs ?? []).map(normalizeBrief),
      voiceProjects: (parsed.voiceProjects ?? []).map(normalizeVoiceProject),
      selectedDraftId: parsed.selectedDraftId,
    };
  } catch {
    return defaultState;
  }
}

interface AccountInput {
  id?: string;
  platformId: PlatformId;
  displayName: string;
  handle: string;
  authStatus: ConnectedAccount["authStatus"];
  scopes: string[];
  notes: string;
  supportedContentTypes: string[];
  canPublish: boolean;
  canSchedule: boolean;
  syncHealth: ConnectedAccount["syncHealth"];
  reconnectState: ConnectedAccount["reconnectState"];
  lastSyncedAt?: string;
}

interface StudioContextValue {
  state: StudioState;
  selectedDraft?: StudioDraft;
  updateProfile: (updates: Partial<UserProfile>) => void;
  updateBrandKit: (updates: Partial<BrandKit>) => void;
  addBrandColor: (name: string, value: string) => void;
  removeBrandColor: (colorId: string) => void;
  addBrandFont: (family: string, role: string, weight: string) => void;
  removeBrandFont: (fontId: string) => void;
  addBrandGradient: (name: string, from: string, to: string) => void;
  removeBrandGradient: (gradientId: string) => void;
  createDraft: (formatId?: string) => string;
  updateDraft: (draftId: string, updater: (draft: StudioDraft) => StudioDraft) => void;
  saveDraft: (draft: StudioDraft) => void;
  removeDraft: (draftId: string) => void;
  selectDraft: (draftId?: string) => void;
  upsertAccount: (account: AccountInput) => void;
  removeAccount: (accountId: string) => void;
  addAssets: (assets: MediaAsset[]) => void;
  updateAsset: (assetId: string, updates: Partial<MediaAsset>) => void;
  removeAsset: (assetId: string) => void;
  addAiBrief: (brief: Omit<AIBrief, "id" | "createdAt">) => string;
  updateAiBrief: (briefId: string, updater: (brief: AIBrief) => AIBrief) => void;
  removeAiBrief: (briefId: string) => void;
  addVoiceProject: (project: Omit<VoiceProject, "id" | "createdAt" | "updatedAt">) => string;
  updateVoiceProject: (projectId: string, updater: (project: VoiceProject) => VoiceProject) => void;
  removeVoiceProject: (projectId: string) => void;
}

const StudioContext = createContext<StudioContextValue | null>(null);

export function StudioProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<StudioState>(() => loadState());

  useEffect(() => {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(state));
  }, [state]);

  const value = useMemo<StudioContextValue>(() => {
    const selectedDraft = state.drafts.find((draft) => draft.id === state.selectedDraftId) ?? state.drafts[0];

    return {
      state,
      selectedDraft,
      updateProfile(updates) {
        setState((current) => ({
          ...current,
          profile: { ...current.profile, ...updates },
        }));
      },
      updateBrandKit(updates) {
        setState((current) => ({
          ...current,
          brandKit: { ...current.brandKit, ...updates },
        }));
      },
      addBrandColor(name, value) {
        setState((current) => ({
          ...current,
          brandKit: {
            ...current.brandKit,
            colors: [...current.brandKit.colors, { id: makeId("color"), name, value }],
          },
        }));
      },
      removeBrandColor(colorId) {
        setState((current) => ({
          ...current,
          brandKit: {
            ...current.brandKit,
            colors: current.brandKit.colors.filter((color) => color.id !== colorId),
          },
        }));
      },
      addBrandFont(family, role, weight) {
        setState((current) => ({
          ...current,
          brandKit: {
            ...current.brandKit,
            fonts: [...current.brandKit.fonts, { id: makeId("font"), family, role, weight }],
          },
        }));
      },
      removeBrandFont(fontId) {
        setState((current) => ({
          ...current,
          brandKit: {
            ...current.brandKit,
            fonts: current.brandKit.fonts.filter((font) => font.id !== fontId),
          },
        }));
      },
      addBrandGradient(name, from, to) {
        setState((current) => ({
          ...current,
          brandKit: {
            ...current.brandKit,
            gradients: [...current.brandKit.gradients, { id: makeId("gradient"), name, from, to }],
          },
        }));
      },
      removeBrandGradient(gradientId) {
        setState((current) => ({
          ...current,
          brandKit: {
            ...current.brandKit,
            gradients: current.brandKit.gradients.filter((gradient) => gradient.id !== gradientId),
          },
        }));
      },
      createDraft(formatId) {
        const draft = createDraftFromPreset(formatId);
        setState((current) => ({
          ...current,
          drafts: [draft, ...current.drafts],
          selectedDraftId: draft.id,
        }));
        return draft.id;
      },
      updateDraft(draftId, updater) {
        setState((current) => ({
          ...current,
          drafts: current.drafts.map((draft) =>
            draft.id === draftId ? { ...updater(draft), updatedAt: nowIso() } : draft,
          ),
        }));
      },
      saveDraft(draft) {
        setState((current) => {
          const normalized = normalizeDraft({ ...draft, updatedAt: nowIso() });
          const exists = current.drafts.some((item) => item.id === draft.id);
          return {
            ...current,
            drafts: exists
              ? current.drafts.map((item) => (item.id === normalized.id ? normalized : item))
              : [normalized, ...current.drafts],
            selectedDraftId: normalized.id,
          };
        });
      },
      removeDraft(draftId) {
        setState((current) => {
          const nextDrafts = current.drafts.filter((draft) => draft.id !== draftId);
          return {
            ...current,
            drafts: nextDrafts,
            selectedDraftId:
              current.selectedDraftId === draftId ? nextDrafts[0]?.id : current.selectedDraftId,
          };
        });
      },
      selectDraft(draftId) {
        setState((current) => ({ ...current, selectedDraftId: draftId }));
      },
      upsertAccount(account) {
        setState((current) => {
          const existing = account.id ? current.accounts.find((item) => item.id === account.id) : undefined;
          const next: ConnectedAccount = normalizeAccount({
            ...account,
            id: account.id ?? makeId("account"),
            createdAt: existing?.createdAt ?? nowIso(),
          });

          return {
            ...current,
            accounts: current.accounts.some((item) => item.id === next.id)
              ? current.accounts.map((item) => (item.id === next.id ? next : item))
              : [next, ...current.accounts],
          };
        });
      },
      removeAccount(accountId) {
        setState((current) => ({
          ...current,
          accounts: current.accounts.filter((account) => account.id !== accountId),
        }));
      },
      addAssets(assets) {
        setState((current) => ({
          ...current,
          assets: [...assets.map(normalizeAsset), ...current.assets],
        }));
      },
      updateAsset(assetId, updates) {
        setState((current) => ({
          ...current,
          assets: current.assets.map((asset) =>
            asset.id === assetId ? normalizeAsset({ ...asset, ...updates }) : asset,
          ),
        }));
      },
      removeAsset(assetId) {
        setState((current) => ({
          ...current,
          assets: current.assets.filter((asset) => asset.id !== assetId),
          drafts: current.drafts.map((draft) => ({
            ...draft,
            linkedAssetIds: draft.linkedAssetIds.filter((id) => id !== assetId),
            layers: draft.layers.filter((layer) => layer.assetId !== assetId),
          })),
          voiceProjects: current.voiceProjects.map((project) =>
            project.selectedAssetId === assetId ? { ...project, selectedAssetId: undefined } : project,
          ),
        }));
      },
      addAiBrief(brief) {
        const next = normalizeBrief({ ...brief, id: makeId("brief"), createdAt: nowIso() });
        setState((current) => ({
          ...current,
          aiBriefs: [next, ...current.aiBriefs],
        }));
        return next.id;
      },
      updateAiBrief(briefId, updater) {
        setState((current) => ({
          ...current,
          aiBriefs: current.aiBriefs.map((brief) =>
            brief.id === briefId ? normalizeBrief(updater(brief)) : brief,
          ),
        }));
      },
      removeAiBrief(briefId) {
        setState((current) => ({
          ...current,
          aiBriefs: current.aiBriefs.filter((brief) => brief.id !== briefId),
        }));
      },
      addVoiceProject(project) {
        const next = normalizeVoiceProject({ ...project, id: makeId("voice") });
        setState((current) => ({
          ...current,
          voiceProjects: [next, ...current.voiceProjects],
        }));
        return next.id;
      },
      updateVoiceProject(projectId, updater) {
        setState((current) => ({
          ...current,
          voiceProjects: current.voiceProjects.map((project) =>
            project.id === projectId ? { ...updater(project), updatedAt: nowIso() } : project,
          ),
        }));
      },
      removeVoiceProject(projectId) {
        setState((current) => ({
          ...current,
          voiceProjects: current.voiceProjects.filter((project) => project.id !== projectId),
        }));
      },
    };
  }, [state]);

  return <StudioContext.Provider value={value}>{children}</StudioContext.Provider>;
}

export function useStudio() {
  const context = useContext(StudioContext);
  if (!context) {
    throw new Error("useStudio must be used inside StudioProvider");
  }

  return context;
}

export function createMediaAsset(input: {
  name: string;
  kind: AssetKind;
  mimeType: string;
  sizeBytes: number;
  dataUrl?: string;
  storageMode?: "embedded" | "metadata-only";
  folder?: string;
  tags?: string[];
  source?: MediaAsset["source"];
  durationMs?: number;
  transcript?: string;
}): MediaAsset {
  return normalizeAsset({
    id: makeId("asset"),
    createdAt: nowIso(),
    favorite: false,
    storageMode: input.storageMode ?? (input.dataUrl ? "embedded" : "metadata-only"),
    folder: input.folder ?? "Library",
    tags: input.tags ?? [],
    source: input.source ?? "upload",
    ...input,
  });
}

export async function fileToDataUrl(file: File) {
  return await new Promise<string>((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result));
    reader.onerror = () => reject(reader.error);
    reader.readAsDataURL(file);
  });
}
