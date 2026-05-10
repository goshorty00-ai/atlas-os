import { useEffect, useMemo, useRef, useState } from "react";
import { useNavigate } from "react-router";
import {
  AlignCenter,
  AlertTriangle,
  BrainCircuit,
  CheckCircle2,
  CheckSquare,
  CircleDot,
  Copy,
  Download,
  Eye,
  EyeOff,
  FolderOpen,
  Grid3x3,
  Image as ImageIcon,
  Layers3,
  LoaderCircle,
  Lock,
  MonitorUp,
  Move,
  Mic,
  Music,
  Palette,
  Pin,
  Plus,
  Redo2,
  Rows3,
  RotateCcw,
  Save,
  Sparkles,
  Star,
  Square,
  Trash2,
  Type,
  Undo2,
  Unlock,
  Video,
  Wand2,
  Waves,
} from "lucide-react";
import { AI_PROVIDERS, AI_TASKS, buildAtlasPacket, modelsForProvider, parseAtlasResponse, recommendedProvidersForTask } from "../lib/aiOrchestration";
import { AtlasExecutionError, cancelAtlasBrief, executeAtlasBrief, isAtlasBridgeAvailable, subscribeAtlasBridge } from "../lib/atlasExecutionBridge";
import {
  FORMAT_PRESETS,
  PLATFORM_DEFINITIONS,
  type AIBriefStatus,
  createDraftFromPreset,
  createMediaAsset,
  fileToDataUrl,
  type AIBrief,
  type AIResultGroup,
  type AIResultOption,
  type AIResultSection,
  type AITaskType,
  type BlendMode,
  type DraftLayer,
  type DraftScene,
  type MediaAsset,
  type ModelProviderId,
  type PlatformId,
  type StudioDraft,
  useStudio,
} from "../state/studioStore";

type StudioTool =
  | "select"
  | "text"
  | "image"
  | "video"
  | "audio"
  | "sticker"
  | "shape"
  | "gradient"
  | "overlay"
  | "motion-text";

type AtlasSurface = "create" | "video" | "voice" | "planner" | "library";
type FlyoutPanel = "tools" | "assets" | "scenes" | "atlas" | "closed";
type AtlasTabId = "ideas" | "captions" | "hooks" | "scripts" | "carousels" | "visuals" | "variants" | "adaptation" | "voiceovers";
type AtlasWorkflowStage = "brief" | "generate" | "review" | "apply";
type AtlasApplyMode =
  | "replace-selection"
  | "set-primary-caption"
  | "append-alternate-caption"
  | "save-caption-variant-set"
  | "assign-platform-caption"
  | "set-active-hook"
  | "save-alternate-hooks"
  | "bind-hook-to-scene"
  | "insert-script-scene"
  | "create-scene-blocks"
  | "assign-narration-sections"
  | "generate-slide-sequence"
  | "save-concept-board"
  | "apply-visual-direction"
  | "create-visual-direction"
  | "add-to-notes"
  | "set-platform-adaptation"
  | "compare-platform-adaptation"
  | "create-variation-draft"
  | "create-alternate-scene-sequence"
  | "store-variant-pack"
  | "send-to-voice";
type CanvasGuide = { orientation: "vertical" | "horizontal"; position: number };

type HistoryState = {
  past: StudioDraft[];
  future: StudioDraft[];
};

type DragState = {
  layerId: string;
  pointerId: number;
  startClientX: number;
  startClientY: number;
  startX: number;
  startY: number;
};

type AtlasRequestPayload = {
  providerId: ModelProviderId;
  modelId: string;
  taskType: AITaskType;
  platformId: PlatformId;
  objective: string;
  tone: string;
  contentType: string;
  brief: string;
  requestPacket: string;
  draftId: string;
  targetSurface: AtlasSurface;
  variantsRequested: number;
};

type AtlasSelectOption = {
  value: string;
  label: string;
};

type AtlasBriefingState = {
  ideas: {
    campaignGoal: string;
    targetAudience: string;
    offer: string;
    contentPillar: string;
    desiredOutput: string;
    advancedNotes: string;
  };
  caption: {
    platform: PlatformId;
    tone: string;
    length: string;
    ctaStyle: string;
    audience: string;
    brandVoice: string;
    forbiddenPhrases: string;
    objective: string;
    advancedNotes: string;
  };
  hook: {
    style: string;
    energyLevel: string;
    targetAudience: string;
    contentAngle: string;
    length: string;
    urgencyLevel: string;
    advancedNotes: string;
  };
  script: {
    format: string;
    durationTarget: string;
    tone: string;
    ctaGoal: string;
    speakingStyle: string;
    sceneCount: string;
    audience: string;
    advancedNotes: string;
  };
  carousel: {
    slideCount: string;
    structureType: string;
    teachingAngle: string;
    ctaSlide: string;
    tone: string;
    targetAudience: string;
    advancedNotes: string;
  };
  visual: {
    style: string;
    mood: string;
    colorDirection: string;
    compositionType: string;
    productFocus: string;
    platform: PlatformId;
    shotVibe: string;
    advancedNotes: string;
  };
  adaptation: {
    sourcePlatform: PlatformId;
    targetPlatform: PlatformId;
    preserveTone: boolean;
    lengthMode: "preserve" | "shorten" | "expand";
    ctaPreference: string;
    advancedNotes: string;
  };
  variant: {
    baseConcept: string;
    variationFocus: string;
    toneDirection: string;
    audienceEmphasis: string;
    ctaDirection: string;
    advancedNotes: string;
  };
  voiceover: {
    voiceStyle: string;
    pacing: string;
    emotion: string;
    length: string;
    narrationStyle: string;
    targetFormat: string;
    advancedNotes: string;
  };
};

type AtlasStructuredRequest = {
  objective: string;
  brief: string;
  tone: string;
  platformId: PlatformId;
  variantsRequested: number;
  summary: string[];
};

type AtlasExecutionStepState = "complete" | "current" | "upcoming" | "attention";

const BLEND_MODES: BlendMode[] = ["normal", "multiply", "screen", "overlay", "soft-light", "darken", "lighten"];
const FILTERS = ["none", "warm", "cool", "mono", "dramatic", "vivid"] as const;
const ANIMATIONS = ["none", "fade-in", "slide-up", "pop", "drift", "type-on"] as const;
const TRANSITIONS = ["cut", "fade", "slide", "zoom"] as const;
const BACKGROUND_SWATCHES = [
  "#0f172a",
  "#111827",
  "#1d4ed8",
  "#0f766e",
  "linear-gradient(135deg,#111827 0%,#1d4ed8 100%)",
  "linear-gradient(135deg,#4c1d95 0%,#db2777 100%)",
  "linear-gradient(135deg,#064e3b 0%,#0f766e 100%)",
  "linear-gradient(135deg,#1f2937 0%,#ea580c 100%)",
];

const ATLAS_TABS: Array<{ id: AtlasTabId; label: string; taskType: AITaskType; surface: AtlasSurface }> = [
  { id: "ideas", label: "Ideas", taskType: "ideas", surface: "planner" },
  { id: "captions", label: "Captions", taskType: "caption", surface: "create" },
  { id: "hooks", label: "Hooks", taskType: "hook", surface: "create" },
  { id: "scripts", label: "Scripts", taskType: "script", surface: "video" },
  { id: "carousels", label: "Carousels", taskType: "carousel-structure", surface: "create" },
  { id: "visuals", label: "Visual Concepts", taskType: "visual-concept", surface: "create" },
  { id: "variants", label: "Variants", taskType: "campaign-pack", surface: "planner" },
  { id: "adaptation", label: "Adaptation", taskType: "platform-adaptation", surface: "create" },
  { id: "voiceovers", label: "Voiceovers", taskType: "voiceover", surface: "voice" },
];

const ATLAS_OPTION_COUNT_OPTIONS: AtlasSelectOption[] = [
  { value: "1", label: "1 option" },
  { value: "2", label: "2 options" },
  { value: "3", label: "3 options" },
  { value: "4", label: "4 options" },
  { value: "5", label: "5 options" },
  { value: "6", label: "6 options" },
];

const CREATE_SPEECH_WIRED = false;
const CREATE_MIC_WIRED = false;

const CAPTION_TONE_OPTIONS: AtlasSelectOption[] = [
  { value: "Editorial", label: "Editorial" },
  { value: "Confident", label: "Confident" },
  { value: "Warm", label: "Warm" },
  { value: "Playful", label: "Playful" },
  { value: "Luxury", label: "Luxury" },
  { value: "Direct-response", label: "Direct-response" },
];

const CAPTION_LENGTH_OPTIONS: AtlasSelectOption[] = [
  { value: "Short punchy", label: "Short punchy" },
  { value: "Medium social", label: "Medium social" },
  { value: "Long story-led", label: "Long story-led" },
];

const CTA_STYLE_OPTIONS: AtlasSelectOption[] = [
  { value: "Soft invite", label: "Soft invite" },
  { value: "Hard conversion", label: "Hard conversion" },
  { value: "Comment prompt", label: "Comment prompt" },
  { value: "Save/share", label: "Save/share" },
  { value: "DM trigger", label: "DM trigger" },
];

const HOOK_STYLE_OPTIONS: AtlasSelectOption[] = [
  { value: "Pattern interrupt", label: "Pattern interrupt" },
  { value: "Bold claim", label: "Bold claim" },
  { value: "Curiosity loop", label: "Curiosity loop" },
  { value: "Problem-first", label: "Problem-first" },
  { value: "Authority proof", label: "Authority proof" },
];

const ENERGY_LEVEL_OPTIONS: AtlasSelectOption[] = [
  { value: "Low-key", label: "Low-key" },
  { value: "Balanced", label: "Balanced" },
  { value: "High energy", label: "High energy" },
  { value: "Aggressive", label: "Aggressive" },
];

const HOOK_LENGTH_OPTIONS: AtlasSelectOption[] = [
  { value: "3-5 words", label: "3-5 words" },
  { value: "6-10 words", label: "6-10 words" },
  { value: "11-16 words", label: "11-16 words" },
];

const URGENCY_LEVEL_OPTIONS: AtlasSelectOption[] = [
  { value: "Calm", label: "Calm" },
  { value: "Moderate", label: "Moderate" },
  { value: "High", label: "High" },
  { value: "Now-or-never", label: "Now-or-never" },
];

const SCRIPT_FORMAT_OPTIONS: AtlasSelectOption[] = [
  { value: "Talking head", label: "Talking head" },
  { value: "Founder story", label: "Founder story" },
  { value: "UGC ad", label: "UGC ad" },
  { value: "Explainer", label: "Explainer" },
  { value: "Product demo", label: "Product demo" },
];

const SCRIPT_DURATION_OPTIONS: AtlasSelectOption[] = [
  { value: "15 sec", label: "15 sec" },
  { value: "30 sec", label: "30 sec" },
  { value: "45 sec", label: "45 sec" },
  { value: "60 sec", label: "60 sec" },
];

const SPEAKING_STYLE_OPTIONS: AtlasSelectOption[] = [
  { value: "Conversational", label: "Conversational" },
  { value: "Authority-led", label: "Authority-led" },
  { value: "Fast creator", label: "Fast creator" },
  { value: "Polished presenter", label: "Polished presenter" },
];

const SCENE_COUNT_OPTIONS: AtlasSelectOption[] = [
  { value: "3", label: "3 scenes" },
  { value: "5", label: "5 scenes" },
  { value: "7", label: "7 scenes" },
  { value: "9", label: "9 scenes" },
];

const CAROUSEL_SLIDE_OPTIONS: AtlasSelectOption[] = [
  { value: "5", label: "5 slides" },
  { value: "7", label: "7 slides" },
  { value: "9", label: "9 slides" },
  { value: "10", label: "10 slides" },
];

const CAROUSEL_STRUCTURE_OPTIONS: AtlasSelectOption[] = [
  { value: "Problem -> solution", label: "Problem -> solution" },
  { value: "Myth -> truth", label: "Myth -> truth" },
  { value: "Step-by-step", label: "Step-by-step" },
  { value: "Framework", label: "Framework" },
  { value: "Case study", label: "Case study" },
];

const CTA_SLIDE_OPTIONS: AtlasSelectOption[] = [
  { value: "Final slide", label: "Final slide" },
  { value: "Penultimate slide", label: "Penultimate slide" },
  { value: "Soft CTA throughout", label: "Soft CTA throughout" },
];

const VISUAL_STYLE_OPTIONS: AtlasSelectOption[] = [
  { value: "Luxury editorial", label: "Luxury editorial" },
  { value: "Futurist product", label: "Futurist product" },
  { value: "Lifestyle cinematic", label: "Lifestyle cinematic" },
  { value: "Minimal premium", label: "Minimal premium" },
  { value: "Bold commercial", label: "Bold commercial" },
];

const VISUAL_MOOD_OPTIONS: AtlasSelectOption[] = [
  { value: "Calm", label: "Calm" },
  { value: "Energetic", label: "Energetic" },
  { value: "Aspirational", label: "Aspirational" },
  { value: "Moody", label: "Moody" },
  { value: "Optimistic", label: "Optimistic" },
];

const COLOR_DIRECTION_OPTIONS: AtlasSelectOption[] = [
  { value: "Brand-led", label: "Brand-led" },
  { value: "High contrast", label: "High contrast" },
  { value: "Soft neutrals", label: "Soft neutrals" },
  { value: "Warm cinematic", label: "Warm cinematic" },
  { value: "Cool modern", label: "Cool modern" },
];

const COMPOSITION_OPTIONS: AtlasSelectOption[] = [
  { value: "Centered hero", label: "Centered hero" },
  { value: "Asymmetric editorial", label: "Asymmetric editorial" },
  { value: "Close crop detail", label: "Close crop detail" },
  { value: "Wide cinematic", label: "Wide cinematic" },
];

const SHOT_VIBE_OPTIONS: AtlasSelectOption[] = [
  { value: "Studio crisp", label: "Studio crisp" },
  { value: "Street energy", label: "Street energy" },
  { value: "Luxury lifestyle", label: "Luxury lifestyle" },
  { value: "Behind the scenes", label: "Behind the scenes" },
];

const ADAPTATION_LENGTH_OPTIONS: AtlasSelectOption[] = [
  { value: "preserve", label: "Preserve length" },
  { value: "shorten", label: "Shorten" },
  { value: "expand", label: "Expand" },
];

const VARIATION_FOCUS_OPTIONS: AtlasSelectOption[] = [
  { value: "Angle", label: "Angle" },
  { value: "CTA", label: "CTA" },
  { value: "Tone", label: "Tone" },
  { value: "Audience", label: "Audience" },
  { value: "Layout", label: "Layout" },
];

const VOICE_STYLE_OPTIONS: AtlasSelectOption[] = [
  { value: "Polished narrator", label: "Polished narrator" },
  { value: "Warm guide", label: "Warm guide" },
  { value: "High-conviction seller", label: "High-conviction seller" },
  { value: "Playful creator", label: "Playful creator" },
];

const PACING_OPTIONS: AtlasSelectOption[] = [
  { value: "Slow and deliberate", label: "Slow and deliberate" },
  { value: "Balanced", label: "Balanced" },
  { value: "Fast and punchy", label: "Fast and punchy" },
];

const EMOTION_OPTIONS: AtlasSelectOption[] = [
  { value: "Confident", label: "Confident" },
  { value: "Warm", label: "Warm" },
  { value: "Urgent", label: "Urgent" },
  { value: "Aspirational", label: "Aspirational" },
  { value: "Playful", label: "Playful" },
];

const VOICEOVER_LENGTH_OPTIONS: AtlasSelectOption[] = [
  { value: "10 sec", label: "10 sec" },
  { value: "15 sec", label: "15 sec" },
  { value: "30 sec", label: "30 sec" },
  { value: "45 sec", label: "45 sec" },
];

const NARRATION_STYLE_OPTIONS: AtlasSelectOption[] = [
  { value: "Direct read", label: "Direct read" },
  { value: "Story-led", label: "Story-led" },
  { value: "Authority explainer", label: "Authority explainer" },
  { value: "Conversational creator", label: "Conversational creator" },
];

const TARGET_FORMAT_OPTIONS: AtlasSelectOption[] = [
  { value: "Short-form social", label: "Short-form social" },
  { value: "Product ad", label: "Product ad" },
  { value: "Brand film", label: "Brand film" },
  { value: "Tutorial", label: "Tutorial" },
];

const ATLAS_REQUEST_TIMEOUT_MS = 90000;
const ATLAS_RESULT_PLACEHOLDER_COUNT = 3;

function splitCommaSeparated(value: string) {
  return value
    .split(",")
    .map((entry) => entry.trim())
    .filter(Boolean);
}

function buildBriefSections(sections: Array<{ title: string; lines: string[] } | null>) {
  return sections
    .filter((section): section is { title: string; lines: string[] } => Boolean(section))
    .map((section) => `${section.title}\n${section.lines.filter(Boolean).map((line) => `- ${line}`).join("\n")}`)
    .join("\n\n");
}

function appendNotesBlock(current: string, next: string) {
  return [current.trim(), next.trim()].filter(Boolean).join("\n\n");
}

function createInitialAtlasBriefingState(platformId: PlatformId): AtlasBriefingState {
  return {
    ideas: {
      campaignGoal: "Launch a sharper cross-platform creative concept",
      targetAudience: "High-intent buyers",
      offer: "Core offer or product focus",
      contentPillar: "Transformation and proof",
      desiredOutput: "Campaign-ready idea set",
      advancedNotes: "",
    },
    caption: {
      platform: platformId,
      tone: "Editorial",
      length: "Medium social",
      ctaStyle: "Soft invite",
      audience: "High-intent buyers",
      brandVoice: "Premium, clear, confident",
      forbiddenPhrases: "",
      objective: "Drive engagement while sounding premium and on-brand",
      advancedNotes: "",
    },
    hook: {
      style: "Pattern interrupt",
      energyLevel: "Balanced",
      targetAudience: "High-intent buyers",
      contentAngle: "Strong transformation or payoff",
      length: "6-10 words",
      urgencyLevel: "Moderate",
      advancedNotes: "",
    },
    script: {
      format: "Talking head",
      durationTarget: "30 sec",
      tone: "Confident",
      ctaGoal: "Drive profile visits",
      speakingStyle: "Conversational",
      sceneCount: "5",
      audience: "High-intent buyers",
      advancedNotes: "",
    },
    carousel: {
      slideCount: "7",
      structureType: "Problem -> solution",
      teachingAngle: "Clear practical takeaway",
      ctaSlide: "Final slide",
      tone: "Editorial",
      targetAudience: "High-intent buyers",
      advancedNotes: "",
    },
    visual: {
      style: "Luxury editorial",
      mood: "Aspirational",
      colorDirection: "Brand-led",
      compositionType: "Centered hero",
      productFocus: "Hero product framing",
      platform: platformId,
      shotVibe: "Studio crisp",
      advancedNotes: "",
    },
    adaptation: {
      sourcePlatform: "instagram",
      targetPlatform: platformId,
      preserveTone: true,
      lengthMode: "preserve",
      ctaPreference: "Keep the CTA clear and platform-appropriate",
      advancedNotes: "",
    },
    variant: {
      baseConcept: "Current winning concept",
      variationFocus: "Angle",
      toneDirection: "Keep premium but fresh",
      audienceEmphasis: "Broaden appeal without losing intent",
      ctaDirection: "Test stronger CTA options",
      advancedNotes: "",
    },
    voiceover: {
      voiceStyle: "Polished narrator",
      pacing: "Balanced",
      emotion: "Confident",
      length: "15 sec",
      narrationStyle: "Direct read",
      targetFormat: "Short-form social",
      advancedNotes: "",
    },
  };
}

function buildStructuredAtlasRequest(input: {
  atlasTab: AtlasTabId;
  atlasVariants: number;
  briefing: AtlasBriefingState;
  brandAudience?: string;
  brandTone?: string;
  previewPlatform: PlatformId;
}): AtlasStructuredRequest {
  const { atlasTab, atlasVariants, briefing, brandAudience, brandTone, previewPlatform } = input;

  switch (atlasTab) {
    case "ideas": {
      const data = briefing.ideas;
      return {
        objective: data.campaignGoal.trim() || "Build a stronger campaign concept",
        tone: brandTone || "Editorial",
        platformId: previewPlatform,
        variantsRequested: atlasVariants,
        summary: [data.desiredOutput, data.contentPillar, data.targetAudience],
        brief: buildBriefSections([
          { title: "Creative Goal", lines: [data.campaignGoal, `Desired output: ${data.desiredOutput}`] },
          { title: "Audience and Offer", lines: [`Audience: ${data.targetAudience || brandAudience || "General audience"}`, `Offer: ${data.offer}`] },
          { title: "Direction", lines: [`Content pillar: ${data.contentPillar}`, `Develop ${atlasVariants} distinct creative directions.`] },
          data.advancedNotes ? { title: "Advanced Notes", lines: [data.advancedNotes] } : null,
        ]),
      };
    }
    case "captions": {
      const data = briefing.caption;
      const forbidden = splitCommaSeparated(data.forbiddenPhrases);
      return {
        objective: data.objective.trim() || `Create premium ${data.platform} captions`,
        tone: data.tone,
        platformId: data.platform || previewPlatform,
        variantsRequested: atlasVariants,
        summary: [data.platform, data.tone, data.length, data.ctaStyle],
        brief: buildBriefSections([
          { title: "Caption Goal", lines: [data.objective, `Create ${atlasVariants} options for ${data.platform}.`] },
          { title: "Voice and Audience", lines: [`Tone: ${data.tone}`, `Audience: ${data.audience || brandAudience || "General audience"}`, `Brand voice: ${data.brandVoice || brandTone || "Not specified"}`] },
          { title: "Structure", lines: [`Length: ${data.length}`, `CTA style: ${data.ctaStyle}`] },
          forbidden.length > 0 ? { title: "Guardrails", lines: [`Avoid these phrases: ${forbidden.join("; ")}`] } : null,
          data.advancedNotes ? { title: "Advanced Notes", lines: [data.advancedNotes] } : null,
        ]),
      };
    }
    case "hooks": {
      const data = briefing.hook;
      return {
        objective: `Generate ${atlasVariants} ${data.style.toLowerCase()} hooks for ${data.contentAngle}`,
        tone: `${data.style}, ${data.energyLevel}`,
        platformId: previewPlatform,
        variantsRequested: atlasVariants,
        summary: [data.style, data.energyLevel, data.length, data.urgencyLevel],
        brief: buildBriefSections([
          { title: "Hook Direction", lines: [`Content angle: ${data.contentAngle}`, `Hook style: ${data.style}`, `Energy level: ${data.energyLevel}`] },
          { title: "Audience and Shape", lines: [`Audience: ${data.targetAudience || brandAudience || "General audience"}`, `Length: ${data.length}`, `Urgency: ${data.urgencyLevel}`] },
          data.advancedNotes ? { title: "Advanced Notes", lines: [data.advancedNotes] } : null,
        ]),
      };
    }
    case "scripts": {
      const data = briefing.script;
      return {
        objective: `Build a ${data.durationTarget} ${data.format.toLowerCase()} script`,
        tone: data.tone,
        platformId: previewPlatform,
        variantsRequested: atlasVariants,
        summary: [data.format, data.durationTarget, `${data.sceneCount} scenes`, data.tone],
        brief: buildBriefSections([
          { title: "Script Goal", lines: [`Format: ${data.format}`, `Duration target: ${data.durationTarget}`, `Create ${atlasVariants} script option${atlasVariants === 1 ? "" : "s"}.`] },
          { title: "Performance Direction", lines: [`Tone: ${data.tone}`, `Speaking style: ${data.speakingStyle}`, `Scene count: ${data.sceneCount}`, `CTA goal: ${data.ctaGoal}`] },
          { title: "Audience", lines: [`Target audience: ${data.audience || brandAudience || "General audience"}`] },
          data.advancedNotes ? { title: "Advanced Notes", lines: [data.advancedNotes] } : null,
        ]),
      };
    }
    case "carousels": {
      const data = briefing.carousel;
      return {
        objective: `Create a ${data.slideCount}-slide ${data.structureType.toLowerCase()} carousel`,
        tone: data.tone,
        platformId: previewPlatform,
        variantsRequested: atlasVariants,
        summary: [`${data.slideCount} slides`, data.structureType, data.ctaSlide, data.tone],
        brief: buildBriefSections([
          { title: "Carousel Goal", lines: [`Slide count: ${data.slideCount}`, `Structure: ${data.structureType}`, `Create ${atlasVariants} carousel structure option${atlasVariants === 1 ? "" : "s"}.`] },
          { title: "Teaching Angle", lines: [`Teaching angle: ${data.teachingAngle}`, `CTA slide: ${data.ctaSlide}`, `Tone: ${data.tone}`] },
          { title: "Audience", lines: [`Target audience: ${data.targetAudience || brandAudience || "General audience"}`] },
          data.advancedNotes ? { title: "Advanced Notes", lines: [data.advancedNotes] } : null,
        ]),
      };
    }
    case "visuals": {
      const data = briefing.visual;
      return {
        objective: `Develop a ${data.style.toLowerCase()} visual concept for ${data.platform}`,
        tone: data.mood,
        platformId: data.platform || previewPlatform,
        variantsRequested: atlasVariants,
        summary: [data.style, data.mood, data.colorDirection, data.compositionType],
        brief: buildBriefSections([
          { title: "Visual Direction", lines: [`Style: ${data.style}`, `Mood: ${data.mood}`, `Color direction: ${data.colorDirection}`, `Composition: ${data.compositionType}`] },
          { title: "Focus", lines: [`Product focus: ${data.productFocus}`, `Platform: ${data.platform}`, `Shot vibe: ${data.shotVibe}`] },
          { title: "Output", lines: [`Create ${atlasVariants} visual concept option${atlasVariants === 1 ? "" : "s"}.`] },
          data.advancedNotes ? { title: "Advanced Notes", lines: [data.advancedNotes] } : null,
        ]),
      };
    }
    case "adaptation": {
      const data = briefing.adaptation;
      return {
        objective: `Adapt the current draft from ${data.sourcePlatform} to ${data.targetPlatform}`,
        tone: data.preserveTone ? brandTone || "Preserve current tone" : "Allow tone adjustment",
        platformId: data.targetPlatform || previewPlatform,
        variantsRequested: atlasVariants,
        summary: [data.sourcePlatform, data.targetPlatform, data.lengthMode, data.preserveTone ? "Preserve tone" : "Refresh tone"],
        brief: buildBriefSections([
          { title: "Adaptation Goal", lines: [`Source platform: ${data.sourcePlatform}`, `Target platform: ${data.targetPlatform}`, `Create ${atlasVariants} adaptation option${atlasVariants === 1 ? "" : "s"}.`] },
          { title: "Rules", lines: [data.preserveTone ? "Preserve the current tone and brand feel." : "You may adjust the tone for the new platform.", `Length mode: ${data.lengthMode}`, `CTA preference: ${data.ctaPreference}`] },
          data.advancedNotes ? { title: "Advanced Notes", lines: [data.advancedNotes] } : null,
        ]),
      };
    }
    case "variants": {
      const data = briefing.variant;
      return {
        objective: `Generate ${atlasVariants} creative variants from the base concept`,
        tone: data.toneDirection,
        platformId: previewPlatform,
        variantsRequested: atlasVariants,
        summary: [data.variationFocus, data.toneDirection, data.audienceEmphasis, data.ctaDirection],
        brief: buildBriefSections([
          { title: "Base Concept", lines: [data.baseConcept, `Create ${atlasVariants} distinct variants.`] },
          { title: "What Should Change", lines: [`Variation focus: ${data.variationFocus}`, `Tone direction: ${data.toneDirection}`, `Audience emphasis: ${data.audienceEmphasis}`, `CTA direction: ${data.ctaDirection}`] },
          data.advancedNotes ? { title: "Advanced Notes", lines: [data.advancedNotes] } : null,
        ]),
      };
    }
    case "voiceovers": {
      const data = briefing.voiceover;
      return {
        objective: `Create a ${data.length} ${data.voiceStyle.toLowerCase()} voiceover`,
        tone: data.emotion,
        platformId: previewPlatform,
        variantsRequested: atlasVariants,
        summary: [data.voiceStyle, data.pacing, data.emotion, data.targetFormat],
        brief: buildBriefSections([
          { title: "Voice Direction", lines: [`Voice style: ${data.voiceStyle}`, `Pacing: ${data.pacing}`, `Emotion: ${data.emotion}`] },
          { title: "Narration Shape", lines: [`Length: ${data.length}`, `Narration style: ${data.narrationStyle}`, `Target format: ${data.targetFormat}`] },
          { title: "Output", lines: [`Create ${atlasVariants} voiceover-ready script option${atlasVariants === 1 ? "" : "s"}.`] },
          data.advancedNotes ? { title: "Advanced Notes", lines: [data.advancedNotes] } : null,
        ]),
      };
    }
    default:
      return {
        objective: "Build a premium cross-platform content package",
        tone: brandTone || "Editorial",
        platformId: previewPlatform,
        variantsRequested: atlasVariants,
        summary: [previewPlatform, brandTone || "Editorial"],
        brief: "",
      };
  }
}

type AtlasChoiceFieldProps = {
  label: string;
  value: string;
  options: AtlasSelectOption[];
  onChange: (value: string) => void;
};

function AtlasChoiceField({ label, value, options, onChange }: AtlasChoiceFieldProps) {
  return (
    <div>
      <div className="mb-2 text-xs uppercase tracking-[0.18em] text-gray-500">{label}</div>
      <div className="flex flex-wrap gap-2">
        {options.map((option) => (
          <button
            key={`${label}-${option.value}`}
            type="button"
            onClick={() => onChange(option.value)}
            className={`rounded-full border px-3 py-1.5 text-xs transition ${value === option.value ? "border-cyan-400/30 bg-cyan-500/10 text-cyan-100" : "border-white/10 bg-white/[0.03] text-gray-300 hover:bg-white/[0.06]"}`}
          >
            {option.label}
          </button>
        ))}
      </div>
    </div>
  );
}

type AtlasSelectFieldProps = {
  label: string;
  value: string;
  options: AtlasSelectOption[];
  onChange: (value: string) => void;
};

function AtlasSelectField({ label, value, options, onChange }: AtlasSelectFieldProps) {
  return (
    <label className="block">
      <div className="mb-2 text-xs uppercase tracking-[0.18em] text-gray-500">{label}</div>
      <select value={value} onChange={(event) => onChange(event.target.value)} className="w-full rounded-2xl border border-white/10 bg-black/25 px-4 py-3 text-sm text-white">
        {options.map((option) => (
          <option key={`${label}-${option.value}`} value={option.value}>{option.label}</option>
        ))}
      </select>
    </label>
  );
}

type AtlasTextFieldProps = {
  label: string;
  value: string;
  placeholder?: string;
  onChange: (value: string) => void;
};

function AtlasTextField({ label, value, placeholder, onChange }: AtlasTextFieldProps) {
  return (
    <label className="block">
      <div className="mb-2 text-xs uppercase tracking-[0.18em] text-gray-500">{label}</div>
      <input value={value} onChange={(event) => onChange(event.target.value)} placeholder={placeholder} className="w-full rounded-2xl border border-white/10 bg-black/25 px-4 py-3 text-sm text-white placeholder:text-gray-500" />
    </label>
  );
}

type AtlasTextAreaFieldProps = {
  label: string;
  value: string;
  rows?: number;
  placeholder?: string;
  onChange: (value: string) => void;
  controls?: React.ReactNode;
  inlineNote?: string;
};

function AtlasTextAreaField({ label, value, rows = 4, placeholder, onChange, controls, inlineNote }: AtlasTextAreaFieldProps) {
  return (
    <label className="block">
      <div className="mb-2 flex items-center justify-between gap-3">
        <div className="text-xs uppercase tracking-[0.18em] text-gray-500">{label}</div>
        {controls ? <div className="flex items-center gap-2">{controls}</div> : null}
      </div>
      <textarea value={value} onChange={(event) => onChange(event.target.value)} rows={rows} placeholder={placeholder} className="w-full rounded-[24px] border border-white/10 bg-black/25 px-4 py-4 text-sm text-white resize-none placeholder:text-gray-500" />
      {inlineNote ? <div className="mt-2 text-[10px] text-cyan-200/75">{inlineNote}</div> : null}
    </label>
  );
}

type AtlasToggleFieldProps = {
  label: string;
  description: string;
  value: boolean;
  onChange: (value: boolean) => void;
};

function AtlasToggleField({ label, description, value, onChange }: AtlasToggleFieldProps) {
  return (
    <button
      type="button"
      onClick={() => onChange(!value)}
      className={`flex w-full items-start justify-between gap-3 rounded-[22px] border px-4 py-4 text-left transition ${value ? "border-cyan-400/30 bg-cyan-500/[0.08]" : "border-white/10 bg-white/[0.03] hover:bg-white/[0.06]"}`}
    >
      <div>
        <div className="text-sm font-medium text-white">{label}</div>
        <div className="mt-1 text-xs leading-relaxed text-gray-400">{description}</div>
      </div>
      <div className={`rounded-full px-3 py-1 text-[11px] uppercase tracking-[0.18em] ${value ? "bg-cyan-500/15 text-cyan-100" : "bg-black/20 text-gray-400"}`}>
        {value ? "On" : "Off"}
      </div>
    </button>
  );
}

function choosePresetForPlatform(platformId: PlatformId, currentDraft: StudioDraft) {
  const recommended = FORMAT_PRESETS.find(
    (preset) => preset.recommendedPlatforms.includes(platformId) && preset.contentType.toLowerCase() === currentDraft.contentType.toLowerCase(),
  );

  if (recommended) {
    return recommended;
  }

  if (platformId === "youtube") {
    return FORMAT_PRESETS.find((preset) => preset.id === "youtube-short") ?? chooseAdaptPreset(currentDraft);
  }

  if (platformId === "linkedin") {
    return FORMAT_PRESETS.find((preset) => preset.id === "linkedin-banner") ?? chooseAdaptPreset(currentDraft);
  }

  if (platformId === "tiktok" || platformId === "snapchat") {
    return FORMAT_PRESETS.find((preset) => preset.id === "short-video") ?? chooseAdaptPreset(currentDraft);
  }

  return chooseAdaptPreset(currentDraft);
}

function splitAtlasSegments(response: string, limit: number) {
  const byParagraph = response
    .split(/\n\s*\n/)
    .map((entry) => entry.trim())
    .filter(Boolean);

  if (byParagraph.length > 1) {
    return byParagraph.slice(0, limit);
  }

  return response
    .split(/\r?\n/)
    .map((entry) => entry.replace(/^[-*\d.\s]+/, "").trim())
    .filter(Boolean)
    .slice(0, limit);
}

function makeId(prefix: string) {
  if (typeof crypto !== "undefined" && "randomUUID" in crypto) {
    return `${prefix}-${crypto.randomUUID()}`;
  }

  return `${prefix}-${Math.random().toString(36).slice(2, 10)}`;
}

function clamp(value: number, min: number, max: number) {
  return Math.min(max, Math.max(min, value));
}

function normalizeDraft(draft: StudioDraft): StudioDraft {
  if (draft.scenes && draft.scenes.length > 0) {
    return {
      ...draft,
      activeSceneId: draft.activeSceneId ?? draft.scenes[0].id,
    };
  }

  const sceneId = makeId("scene");
  return {
    ...draft,
    scenes: [
      {
        id: sceneId,
        name: "Scene 1",
        durationMs: draft.durationMs,
        background: draft.background,
        transition: "cut" as const,
        layerIds: draft.layers.map((layer) => layer.id),
      },
    ],
    activeSceneId: sceneId,
  };
}

function formatDuration(durationMs: number) {
  const seconds = Math.max(1, Math.round(durationMs / 100) / 10);
  return `${seconds.toFixed(1)}s`;
}

function formatRuntimeDuration(durationMs: number) {
  const safeDuration = Math.max(0, durationMs);
  const totalSeconds = Math.max(1, Math.round(safeDuration / 1000));
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;

  if (minutes <= 0) {
    return `${totalSeconds}s`;
  }

  return `${minutes}m ${seconds.toString().padStart(2, "0")}s`;
}

function atlasBriefDurationMs(brief: Pick<AIBrief, "startedAt" | "completedAt" | "lastDurationMs">, now = Date.now()) {
  if (typeof brief.lastDurationMs === "number" && Number.isFinite(brief.lastDurationMs)) {
    return Math.max(0, brief.lastDurationMs);
  }

  if (!brief.startedAt) {
    return 0;
  }

  const startedAt = Date.parse(brief.startedAt);
  const completedAt = brief.completedAt ? Date.parse(brief.completedAt) : now;
  if (!Number.isFinite(startedAt) || !Number.isFinite(completedAt)) {
    return 0;
  }

  return Math.max(0, completedAt - startedAt);
}

function formatRelativeTimestamp(timestamp: string | undefined, now = Date.now()) {
  if (!timestamp) {
    return "Just now";
  }

  const value = Date.parse(timestamp);
  if (!Number.isFinite(value)) {
    return "Just now";
  }

  const deltaSeconds = Math.max(0, Math.round((now - value) / 1000));
  if (deltaSeconds < 10) {
    return "Just now";
  }

  if (deltaSeconds < 60) {
    return `${deltaSeconds}s ago`;
  }

  const minutes = Math.floor(deltaSeconds / 60);
  if (minutes < 60) {
    return `${minutes}m ago`;
  }

  const hours = Math.floor(minutes / 60);
  if (hours < 24) {
    return `${hours}h ago`;
  }

  const days = Math.floor(hours / 24);
  return `${days}d ago`;
}

function atlasBriefStatusLabel(status: AIBriefStatus | undefined) {
  switch (status) {
    case "queued":
      return "Queued";
    case "retrying":
      return "Retrying";
    case "running":
      return "Running";
    case "succeeded":
      return "Ready";
    case "failed":
      return "Needs attention";
    case "timed-out":
      return "Timed out";
    case "cancelled":
      return "Cancelled";
    default:
      return "Idle";
  }
}

function atlasBriefStatusTone(status: AIBriefStatus | undefined) {
  switch (status) {
    case "queued":
      return "border-white/10 bg-white/[0.04] text-gray-200";
    case "retrying":
      return "border-amber-300/25 bg-amber-400/10 text-amber-100";
    case "running":
      return "border-cyan-400/30 bg-cyan-500/10 text-cyan-100";
    case "succeeded":
      return "border-emerald-300/25 bg-emerald-400/10 text-emerald-100";
    case "timed-out":
      return "border-amber-300/25 bg-amber-500/10 text-amber-100";
    case "cancelled":
      return "border-white/10 bg-black/20 text-gray-300";
    case "failed":
    default:
      return "border-rose-300/25 bg-rose-500/10 text-rose-100";
  }
}

function atlasBriefProgressPercent(brief: AIBrief | undefined, now = Date.now()) {
  if (!brief) {
    return 0;
  }

  if (brief.status === "succeeded") {
    return 100;
  }

  if (brief.status === "failed" || brief.status === "timed-out" || brief.status === "cancelled") {
    return 100;
  }

  if (brief.status === "queued") {
    return 14;
  }

  const timeoutMs = Math.max(30000, brief.timeoutMs ?? ATLAS_REQUEST_TIMEOUT_MS);
  const ratio = Math.min(1, atlasBriefDurationMs(brief, now) / timeoutMs);
  return Math.round((brief.status === "retrying" ? 22 : 30) + ratio * 56);
}

function atlasBriefOperationalMessage(brief: AIBrief | undefined, now = Date.now()) {
  if (!brief) {
    return "Shape the brief, then send it into the live ATLAS runtime when you're ready.";
  }

  switch (brief.status) {
    case "queued":
      return "ATLAS accepted the brief and is preparing the live provider handoff.";
    case "retrying":
      return atlasBriefDurationMs(brief, now) > (brief.timeoutMs ?? ATLAS_REQUEST_TIMEOUT_MS) * 0.7
        ? "ATLAS is retrying this run. It is taking longer than usual, but the brief is still active."
        : "ATLAS is retrying the request with the same production brief and runtime profile.";
    case "running":
      return atlasBriefDurationMs(brief, now) > (brief.timeoutMs ?? ATLAS_REQUEST_TIMEOUT_MS) * 0.7
        ? "ATLAS is still generating. This run is taking longer than usual, but it is still live."
        : "ATLAS is generating structured creator assets and preparing them for review.";
    case "succeeded":
      return "The run completed cleanly. Reviewed assets are ready in the Review stage.";
    case "timed-out":
      return "This run took too long, so ATLAS stopped waiting and preserved the brief for a clean retry.";
    case "cancelled":
      return "The live run was stopped without disturbing your brief or previous reviewed assets.";
    case "failed":
    default:
      return brief.errorMessage?.trim() || "ATLAS could not finish this run. Adjust the brief or retry when you're ready.";
  }
}

function atlasBriefRecoveryMessage(brief: AIBrief | undefined) {
  if (!brief) {
    return "";
  }

  switch (brief.status) {
    case "timed-out":
      return "Retry now, or shorten the brief and request fewer variations for a faster pass.";
    case "cancelled":
      return "You can resume by retrying the same brief when you're ready.";
    case "failed":
      return "Retry the same brief, or refine the context before sending a fresh run.";
    default:
      return "";
  }
}

function atlasExecutionSteps(brief: AIBrief | undefined): Array<{ id: string; label: string; note: string; state: AtlasExecutionStepState }> {
  const base = [
    { id: "queued", label: "Queued", note: "Brief accepted", state: "upcoming" as AtlasExecutionStepState },
    { id: "route", label: "Routed", note: "Runtime engaged", state: "upcoming" as AtlasExecutionStepState },
    { id: "generate", label: "Generating", note: "Provider producing assets", state: "upcoming" as AtlasExecutionStepState },
    { id: "ready", label: "Review Ready", note: "Results prepared", state: "upcoming" as AtlasExecutionStepState },
  ];

  if (!brief) {
    return base;
  }

  if (brief.status === "queued") {
    base[0].state = "current";
    return base;
  }

  base[0].state = "complete";
  base[1].state = "complete";

  if (brief.status === "running" || brief.status === "retrying") {
    base[2].state = "current";
    return base;
  }

  if (brief.status === "succeeded") {
    base[2].state = "complete";
    base[3].state = "complete";
    return base;
  }

  base[2].state = "attention";
  return base;
}

function atlasExecutionErrorMessage(error: unknown) {
  if (error instanceof AtlasExecutionError) {
    if (error.code === "timed-out") {
      return {
        status: "timed-out" as AIBriefStatus,
        message: "This run took longer than expected, so ATLAS closed it before a result arrived.",
      };
    }

    if (error.code === "cancelled") {
      return {
        status: "cancelled" as AIBriefStatus,
        message: "The live run was cancelled. Your brief and previous results are still available.",
      };
    }

    return {
      status: "failed" as AIBriefStatus,
      message: error.message || "ATLAS could not finish this run.",
    };
  }

  const fallback = error instanceof Error ? error.message : "ATLAS could not finish this run.";
  if (/timed out/i.test(fallback)) {
    return {
      status: "timed-out" as AIBriefStatus,
      message: "This run took longer than expected, so ATLAS closed it before a result arrived.",
    };
  }

  return {
    status: "failed" as AIBriefStatus,
    message: fallback,
  };
}

function sceneLayers(draft: StudioDraft, sceneId: string) {
  const scene = draft.scenes?.find((item) => item.id === sceneId);
  if (!scene) {
    return draft.layers;
  }

  return scene.layerIds
    .map((layerId) => draft.layers.find((layer) => layer.id === layerId))
    .filter((layer): layer is DraftLayer => Boolean(layer));
}

function findAsset(draft: StudioDraft, assets: MediaAsset[], layer: DraftLayer) {
  return assets.find((asset) => asset.id === layer.assetId) ?? draft.linkedAssetIds.map((id) => assets.find((asset) => asset.id === id)).find(Boolean);
}

function layerLabel(layer: DraftLayer) {
  if (layer.text?.trim()) {
    return layer.text.trim().slice(0, 28);
  }

  return layer.name;
}

function nextSceneName(scenes: DraftScene[]) {
  return `Scene ${scenes.length + 1}`;
}

function duplicateSceneLayers(draft: StudioDraft, sceneId: string) {
  const scene = draft.scenes?.find((item) => item.id === sceneId);
  if (!scene) {
    return draft;
  }

  const nextSceneId = makeId("scene");
  const duplicatedLayers = scene.layerIds
    .map((layerId) => draft.layers.find((layer) => layer.id === layerId))
    .filter((layer): layer is DraftLayer => Boolean(layer))
    .map((layer) => ({
      ...layer,
      id: makeId("layer"),
      sceneId: nextSceneId,
      name: `${layer.name} Copy`,
      x: clamp(layer.x + 24, 0, draft.width - 40),
      y: clamp(layer.y + 24, 0, draft.height - 40),
    }));

  const nextScene: DraftScene = {
    ...scene,
    id: nextSceneId,
    name: `${scene.name} Copy`,
    layerIds: duplicatedLayers.map((layer) => layer.id),
  };

  return {
    ...draft,
    layers: [...draft.layers, ...duplicatedLayers],
    scenes: [...(draft.scenes ?? []), nextScene],
    activeSceneId: nextSceneId,
  };
}

function sceneBackgroundStyle(background: string) {
  return background.startsWith("linear-gradient") ? { backgroundImage: background } : { backgroundColor: background };
}

function chooseAdaptPreset(draft: StudioDraft) {
  const verticalPreset = FORMAT_PRESETS.find((preset) => preset.width === 1080 && preset.height === 1920);
  const squarePreset = FORMAT_PRESETS.find((preset) => preset.width === 1080 && preset.height === 1080);
  const widescreenPreset = FORMAT_PRESETS.find((preset) => preset.width === 1280 && preset.height === 720);

  if (draft.width < draft.height) {
    return squarePreset ?? FORMAT_PRESETS[0];
  }

  if (draft.width === draft.height) {
    return verticalPreset ?? FORMAT_PRESETS[0];
  }

  return widescreenPreset ?? FORMAT_PRESETS[0];
}

function assetPreviewFallback(asset: MediaAsset) {
  if (asset.kind === "video") {
    return "Video preview is stored as metadata in this workspace. Export or open the source clip to review the full media.";
  }

  if (asset.kind === "audio" || asset.kind === "voiceover") {
    return "Audio is linked into the draft. Use Voice Studio or Export to review the full waveform and render path.";
  }

  return "Asset is linked into the draft and ready for placement even when the source media is not embedded locally.";
}

function flattenResultOptions(groups: AIResultGroup[]) {
  return groups.flatMap((group) => group.options);
}

function displaySectionsForOption(option: AIResultOption): AIResultSection[] {
  if (option.sections.length > 0) {
    return option.sections;
  }

  return [{ id: `${option.id}-content`, label: "Asset", kind: "text", content: option.content.trim() }];
}

function formatResultOption(option: AIResultOption) {
  const sections = displaySectionsForOption(option);
  if (sections.length === 0) {
    return option.content.trim();
  }

  return sections
    .map((section) => `${section.label}\n${section.content}`)
    .join("\n\n")
    .trim();
}

function applyTextForOption(option: AIResultOption, mode?: AtlasApplyMode) {
  if (option.schema?.type === "hook") {
    return option.schema.primaryHook || formatResultOption(option);
  }

  if (option.schema?.type === "voiceover" && mode === "send-to-voice") {
    return option.schema.script || formatResultOption(option);
  }

  if (option.schema?.type === "platform-adaptation") {
    return [option.schema.adaptedBody, option.schema.adaptedCta].filter(Boolean).join("\n\n") || formatResultOption(option);
  }

  return formatResultOption(option);
}

function supportsSceneCreation(taskType: AITaskType) {
  return taskType !== "voiceover";
}

function optionMetaEntries(option: AIResultOption) {
  return Object.entries(option.metadata).filter(([, value]) => value.trim().length > 0);
}

function atlasPreviewText(option: AIResultOption, maxLength = 180) {
  const source = option.summary.trim() || displaySectionsForOption(option)[0]?.content.trim() || option.content.trim();
  if (source.length <= maxLength) {
    return source;
  }

  return `${source.slice(0, maxLength - 1).trimEnd()}…`;
}

function atlasCardTheme(taskType: AITaskType) {
  switch (taskType) {
    case "caption":
      return {
        icon: Type,
        assetLabel: "Caption Asset",
        gradient: "linear-gradient(145deg, rgba(92,244,255,0.24) 0%, rgba(17,24,39,0.92) 58%, rgba(7,10,18,1) 100%)",
        accent: "#5CF4FF",
        chipBackground: "rgba(92,244,255,0.12)",
      };
    case "hook":
      return {
        icon: Sparkles,
        assetLabel: "Hook Asset",
        gradient: "linear-gradient(145deg, rgba(255,183,3,0.24) 0%, rgba(31,41,55,0.92) 58%, rgba(7,10,18,1) 100%)",
        accent: "#FFB703",
        chipBackground: "rgba(255,183,3,0.12)",
      };
    case "script":
      return {
        icon: Rows3,
        assetLabel: "Script Flow",
        gradient: "linear-gradient(145deg, rgba(157,140,255,0.24) 0%, rgba(17,24,39,0.92) 58%, rgba(7,10,18,1) 100%)",
        accent: "#9D8CFF",
        chipBackground: "rgba(157,140,255,0.12)",
      };
    case "platform-adaptation":
      return {
        icon: MonitorUp,
        assetLabel: "Adaptation Output",
        gradient: "linear-gradient(145deg, rgba(99,102,241,0.24) 0%, rgba(17,24,39,0.92) 58%, rgba(7,10,18,1) 100%)",
        accent: "#7C8CFF",
        chipBackground: "rgba(124,140,255,0.12)",
      };
    case "visual-concept":
      return {
        icon: Palette,
        assetLabel: "Visual Concept",
        gradient: "linear-gradient(145deg, rgba(244,114,182,0.22) 0%, rgba(17,24,39,0.92) 58%, rgba(7,10,18,1) 100%)",
        accent: "#F472B6",
        chipBackground: "rgba(244,114,182,0.12)",
      };
    case "carousel-structure":
      return {
        icon: Grid3x3,
        assetLabel: "Carousel Structure",
        gradient: "linear-gradient(145deg, rgba(96,165,250,0.24) 0%, rgba(17,24,39,0.92) 58%, rgba(7,10,18,1) 100%)",
        accent: "#60A5FA",
        chipBackground: "rgba(96,165,250,0.12)",
      };
    case "campaign-pack":
      return {
        icon: Layers3,
        assetLabel: "Variant Pack",
        gradient: "linear-gradient(145deg, rgba(167,255,109,0.2) 0%, rgba(17,24,39,0.92) 58%, rgba(7,10,18,1) 100%)",
        accent: "#A7FF6D",
        chipBackground: "rgba(167,255,109,0.12)",
      };
    default:
      return {
        icon: Wand2,
        assetLabel: "Creative Asset",
        gradient: "linear-gradient(145deg, rgba(92,244,255,0.2) 0%, rgba(17,24,39,0.92) 58%, rgba(7,10,18,1) 100%)",
        accent: "#5CF4FF",
        chipBackground: "rgba(92,244,255,0.12)",
      };
  }
}

function atlasCardMetrics(option: AIResultOption) {
  const metrics = [`Source ${option.sourceIndex + 1}`];

  const sections = displaySectionsForOption(option);
  if (sections.length > 0) {
    metrics.push(`${sections.length} blocks`);
  }

  const metaEntry = optionMetaEntries(option)[0];
  if (metaEntry) {
    metrics.push(`${metaEntry[0]}: ${metaEntry[1]}`);
  }

  return metrics.slice(0, 3);
}

function isTextEditableLayer(layer?: DraftLayer) {
  return layer?.type === "text" || layer?.type === "sticker";
}

function dedupeTexts(values: string[]) {
  return Array.from(new Set(values.map((value) => value.trim()).filter(Boolean)));
}

function captionBodyForOption(option: AIResultOption) {
  if (option.schema?.type === "caption") {
    return [option.schema.body, option.schema.cta, option.schema.hashtags.join(" ")].filter(Boolean).join("\n\n").trim();
  }

  return formatResultOption(option);
}

function hookPrimaryForOption(option: AIResultOption) {
  if (option.schema?.type === "hook") {
    return option.schema.primaryHook.trim();
  }

  return applyTextForOption(option, "set-active-hook").trim();
}

function hookAlternatesForOptions(options: AIResultOption[]) {
  return dedupeTexts(options.flatMap((option) => {
    if (option.schema?.type === "hook") {
      return [option.schema.primaryHook, ...option.schema.alternates];
    }

    return [formatResultOption(option)];
  }));
}

type AtlasScenePayload = {
  sceneName: string;
  layerName: string;
  text: string;
  headline?: string;
  body?: string;
  cta?: string;
  hook?: string;
  narration?: string;
  sceneType?: "default" | "script" | "carousel-slide" | "visual-direction" | "voiceover";
  visualDirectionId?: string;
};

function buildScenePayloads(options: AIResultOption[], splitSections: boolean): AtlasScenePayload[] {
  return options.flatMap<AtlasScenePayload>((option) => {
    if (option.schema?.type === "script") {
      const scriptSchema = option.schema;
      const scriptScenes: AtlasScenePayload[] = [
        {
          sceneName: scriptSchema.title || option.label,
          layerName: scriptSchema.title || option.label,
          text: [scriptSchema.opening, ...scriptSchema.beats, scriptSchema.cta].filter(Boolean).join("\n\n"),
          headline: scriptSchema.title || option.label,
          body: scriptSchema.opening,
          cta: scriptSchema.cta,
          narration: [scriptSchema.opening, ...scriptSchema.beats, scriptSchema.cta].filter(Boolean).join("\n\n"),
          hook: scriptSchema.opening,
          sceneType: "script",
        },
        ...scriptSchema.beats.map((beat, index) => ({
          sceneName: `${scriptSchema.title || option.label} · Beat ${index + 1}`,
          layerName: `Beat ${index + 1}`,
          text: beat,
          headline: `Beat ${index + 1}`,
          body: beat,
          cta: index === scriptSchema.beats.length - 1 ? scriptSchema.cta : "",
          narration: beat,
          hook: index === 0 ? scriptSchema.opening : "",
          sceneType: "script" as const,
        })),
      ];

      return splitSections ? scriptScenes : [scriptScenes[0]];
    }

    if (option.schema?.type === "carousel") {
      const carouselSchema = option.schema;
      return carouselSchema.slides.map((slide, index) => ({
        sceneName: `${carouselSchema.title || option.label} · Slide ${index + 1}`,
        layerName: slide.title || `Slide ${index + 1}`,
        text: [slide.title, slide.body, index === carouselSchema.slides.length - 1 ? carouselSchema.ctaSlide : ""].filter(Boolean).join("\n\n"),
        headline: slide.title,
        body: slide.body,
        cta: index === carouselSchema.slides.length - 1 ? carouselSchema.ctaSlide : "",
        sceneType: "carousel-slide" as const,
      }));
    }

    if (option.schema?.type === "voiceover") {
      const voiceSchema = option.schema;
      return [{
        sceneName: voiceSchema.title || option.label,
        layerName: voiceSchema.title || option.label,
        text: voiceSchema.script,
        headline: voiceSchema.title,
        body: voiceSchema.script,
        narration: voiceSchema.script,
        sceneType: "voiceover",
      }];
    }

    const sections = displaySectionsForOption(option);
    if (splitSections && sections.length > 0) {
      return sections.map((section, sectionIndex) => ({
        sceneName: `${option.label} ${sections.length > 1 ? `· ${section.label || `Scene ${sectionIndex + 1}`}` : ""}`.trim(),
        layerName: section.label || option.label,
        text: section.content,
        headline: section.label || option.label,
        body: section.content,
        sceneType: "default",
      }));
    }

    return [
      {
        sceneName: option.label,
        layerName: option.label,
        text: formatResultOption(option),
        headline: option.label,
        body: formatResultOption(option),
        sceneType: "default",
      },
    ];
  });
}

function buildVisualDirectionFromOption(option: AIResultOption, sceneId?: string) {
  if (option.schema?.type === "visual-concept") {
    return {
      id: makeId("visual-direction"),
      title: option.schema.conceptTitle || option.label,
      palette: option.schema.palette,
      mood: option.schema.mood,
      composition: option.schema.composition,
      references: option.schema.references,
      shotNotes: option.schema.shotNotes,
      platformNotes: option.schema.platformNotes,
      appliedToSceneId: sceneId,
      createdAt: new Date().toISOString(),
    };
  }

  return {
    id: makeId("visual-direction"),
    title: option.label,
    palette: [],
    mood: option.metadata.mood ?? "",
    composition: option.metadata.composition ?? "",
    references: [],
    shotNotes: displaySectionsForOption(option).map((section) => section.content),
    platformNotes: option.metadata.platform ?? "",
    appliedToSceneId: sceneId,
    createdAt: new Date().toISOString(),
  };
}

function buildVariantPackFromOption(option: AIResultOption) {
  if (option.schema?.type === "variant-pack") {
    return {
      id: makeId("variant-pack"),
      label: option.label,
      variants: option.schema.variants,
      keyDifferences: option.schema.keyDifferences,
      audienceFit: option.schema.audienceFit,
      recommendedUse: option.schema.recommendedUse,
      createdAt: new Date().toISOString(),
    };
  }

  return {
    id: makeId("variant-pack"),
    label: option.label,
    variants: [{ title: option.label, body: formatResultOption(option), hook: "", cta: "", keyDifference: "" }],
    keyDifferences: [],
    audienceFit: "",
    recommendedUse: "",
    createdAt: new Date().toISOString(),
  };
}

function buildVariationDraftFromOptions(baseDraft: StudioDraft, options: AIResultOption[], taskType: AITaskType, platformId: PlatformId) {
  const primaryText = options.map((option) => formatResultOption(option)).join("\n\n").trim();
  const timestamp = new Date().toISOString();
  const variantIndex = (baseDraft.alternatePostVersions?.length ?? 0) + 1;
  const variationTitle = `${baseDraft.title} Variant ${variantIndex}`;
  const nextDraft: StudioDraft = {
    ...baseDraft,
    id: makeId("draft"),
    title: variationTitle,
    caption: taskType === "caption" ? (options.map((option) => captionBodyForOption(option)).join("\n\n").trim() || baseDraft.caption) : baseDraft.caption,
    primaryHook: taskType === "hook" ? (options.map((option) => hookPrimaryForOption(option)).join("\n\n").trim() || baseDraft.primaryHook) : baseDraft.primaryHook,
    alternateHooks: taskType === "hook"
      ? dedupeTexts([...(baseDraft.alternateHooks ?? []), ...hookAlternatesForOptions(options)])
      : [...(baseDraft.alternateHooks ?? [])],
    captionVariants: taskType === "campaign-pack"
      ? dedupeTexts([...(baseDraft.captionVariants ?? []), ...options.map((option) => formatResultOption(option))])
      : [...(baseDraft.captionVariants ?? [])],
    captionVariantSets: taskType === "caption"
      ? [
        {
          id: makeId("caption-set"),
          label: `${variationTitle} Caption Set`,
          captions: dedupeTexts(options.map((option) => captionBodyForOption(option))),
          platformId,
          createdAt: timestamp,
        },
        ...(baseDraft.captionVariantSets ?? []),
      ]
      : [...(baseDraft.captionVariantSets ?? [])],
    alternatePostVersions: dedupeTexts([...(baseDraft.alternatePostVersions ?? []), primaryText]),
    platformAdaptations: taskType === "platform-adaptation"
      ? { ...(baseDraft.platformAdaptations ?? {}), [platformId]: primaryText }
      : { ...(baseDraft.platformAdaptations ?? {}) },
    platformCaptions: taskType === "caption"
      ? { ...(baseDraft.platformCaptions ?? {}), [platformId]: options.map((option) => captionBodyForOption(option)).join("\n\n").trim() }
      : { ...(baseDraft.platformCaptions ?? {}) },
    platformVersions: taskType === "platform-adaptation"
      ? {
        ...(baseDraft.platformVersions ?? {}),
        [platformId]: {
          platformId,
          primaryCaption: baseDraft.platformVersions?.[platformId]?.primaryCaption ?? "",
          captionAlternates: baseDraft.platformVersions?.[platformId]?.captionAlternates ?? [],
          hook: baseDraft.platformVersions?.[platformId]?.hook ?? baseDraft.primaryHook,
          adaptation: primaryText,
          preservedOriginal: baseDraft.platformVersions?.[platformId]?.preservedOriginal ?? baseDraft.platformAdaptations?.[platformId] ?? "",
          lastComparedCandidate: primaryText,
        },
      }
      : { ...(baseDraft.platformVersions ?? {}) },
    linkedVoiceProjectIds: [...(baseDraft.linkedVoiceProjectIds ?? [])],
    conceptBoard: taskType === "visual-concept"
      ? [
        ...options.map((option) => buildVisualDirectionFromOption(option)),
        ...(baseDraft.conceptBoard ?? []),
      ]
      : [...(baseDraft.conceptBoard ?? [])],
    variantPacks: taskType === "campaign-pack"
      ? [
        ...options.map((option) => buildVariantPackFromOption(option)),
        ...(baseDraft.variantPacks ?? []),
      ]
      : [...(baseDraft.variantPacks ?? [])],
    notes: [
      baseDraft.notes,
      `Variation draft created from ${taskType} output for ${platformId}.`,
      primaryText,
    ].filter(Boolean).join("\n\n"),
    createdAt: timestamp,
    updatedAt: timestamp,
    variationOfDraftId: baseDraft.id,
  };

  if (taskType === "script" || taskType === "ideas" || taskType === "carousel-structure" || taskType === "visual-concept") {
    const scenePayloads = buildScenePayloads(options, taskType === "script");
    const layerRecords: DraftLayer[] = [];
    const sceneRecords: DraftScene[] = scenePayloads.map((payload, index) => {
      const sceneId = makeId("scene");
      const layerId = makeId("layer");
      layerRecords.push({
        id: layerId,
        type: "text",
        name: payload.layerName,
        sceneId,
        visible: true,
        locked: false,
        x: Math.round(baseDraft.width * 0.12),
        y: Math.round(baseDraft.height * 0.18),
        width: Math.round(baseDraft.width * 0.76),
        height: 220,
        rotation: 0,
        opacity: 1,
        color: "#ffffff",
        text: payload.text,
        fontSize: 52,
        fontWeight: 700,
        blendMode: "normal",
        filter: "none",
        animation: "fade-in",
        transition: index === 0 ? "cut" : "fade",
        assetFit: "cover",
        cropX: 0,
        cropY: 0,
        cropZoom: 1,
        cornerRadius: 0,
        startMs: 0,
        endMs: Math.max(3200, Math.round(baseDraft.durationMs / Math.max(scenePayloads.length, 1))),
      });
      return {
        id: sceneId,
        name: payload.sceneName,
        durationMs: Math.max(3200, Math.round(baseDraft.durationMs / Math.max(scenePayloads.length, 1))),
        background: baseDraft.background,
        transition: index === 0 ? "cut" : "fade",
        layerIds: [layerId],
        headline: payload.headline ?? payload.layerName,
        body: payload.body ?? payload.text,
        cta: payload.cta ?? "",
        hook: payload.hook ?? "",
        narration: payload.narration ?? payload.text,
        sceneType: payload.sceneType ?? "default",
        visualDirectionId: payload.visualDirectionId,
      };
    });

    nextDraft.layers = layerRecords;
    nextDraft.scenes = sceneRecords;
    nextDraft.activeSceneId = sceneRecords[0]?.id;
  }

  return normalizeDraft(nextDraft);
}

function createStructuredLayersForScene(baseDraft: StudioDraft, sceneId: string, payload: AtlasScenePayload, durationMs: number) {
  const layers: DraftLayer[] = [];

  if (payload.headline?.trim()) {
    layers.push({
      id: makeId("layer"),
      type: "text",
      name: `${payload.layerName} Headline`,
      sceneId,
      visible: true,
      locked: false,
      x: Math.round(baseDraft.width * 0.1),
      y: Math.round(baseDraft.height * 0.12),
      width: Math.round(baseDraft.width * 0.8),
      height: 96,
      rotation: 0,
      opacity: 1,
      color: "#ffffff",
      text: payload.headline,
      fontSize: 56,
      fontWeight: 700,
      blendMode: "normal",
      filter: "none",
      animation: "fade-in",
      transition: "fade",
      assetFit: "cover",
      cropX: 0,
      cropY: 0,
      cropZoom: 1,
      cornerRadius: 0,
      startMs: 0,
      endMs: durationMs,
    });
  }

  if (payload.body?.trim()) {
    layers.push({
      id: makeId("layer"),
      type: "text",
      name: `${payload.layerName} Body`,
      sceneId,
      visible: true,
      locked: false,
      x: Math.round(baseDraft.width * 0.1),
      y: Math.round(baseDraft.height * 0.28),
      width: Math.round(baseDraft.width * 0.8),
      height: 220,
      rotation: 0,
      opacity: 1,
      color: "#ffffff",
      text: payload.body,
      fontSize: 38,
      fontWeight: 500,
      blendMode: "normal",
      filter: "none",
      animation: "fade-in",
      transition: "fade",
      assetFit: "cover",
      cropX: 0,
      cropY: 0,
      cropZoom: 1,
      cornerRadius: 0,
      startMs: 0,
      endMs: durationMs,
    });
  }

  if (payload.cta?.trim()) {
    layers.push({
      id: makeId("layer"),
      type: "text",
      name: `${payload.layerName} CTA`,
      sceneId,
      visible: true,
      locked: false,
      x: Math.round(baseDraft.width * 0.1),
      y: Math.round(baseDraft.height * 0.78),
      width: Math.round(baseDraft.width * 0.8),
      height: 72,
      rotation: 0,
      opacity: 1,
      color: "#5CF4FF",
      text: payload.cta,
      fontSize: 28,
      fontWeight: 700,
      blendMode: "normal",
      filter: "none",
      animation: "fade-in",
      transition: "fade",
      assetFit: "cover",
      cropX: 0,
      cropY: 0,
      cropZoom: 1,
      cornerRadius: 0,
      startMs: 0,
      endMs: durationMs,
    });
  }

  if (layers.length === 0) {
    layers.push({
      id: makeId("layer"),
      type: "text",
      name: payload.layerName,
      sceneId,
      visible: true,
      locked: false,
      x: Math.round(baseDraft.width * 0.12),
      y: Math.round(baseDraft.height * 0.18),
      width: Math.round(baseDraft.width * 0.76),
      height: 220,
      rotation: 0,
      opacity: 1,
      color: "#ffffff",
      text: payload.text,
      fontSize: 52,
      fontWeight: 700,
      blendMode: "normal",
      filter: "none",
      animation: "fade-in",
      transition: "fade",
      assetFit: "cover",
      cropX: 0,
      cropY: 0,
      cropZoom: 1,
      cornerRadius: 0,
      startMs: 0,
      endMs: durationMs,
    });
  }

  return layers;
}

function appendScenePayloadSequence(baseDraft: StudioDraft, payloads: AtlasScenePayload[], options?: { prefix?: string; activateFirst?: boolean }) {
  const nextScenes = [...(baseDraft.scenes ?? [])];
  const nextLayers = [...baseDraft.layers];
  let firstNewSceneId: string | undefined;

  payloads.forEach((payload, index) => {
    const sceneId = makeId("scene");
    const durationMs = Math.max(3200, Math.round(baseDraft.durationMs / Math.max(payloads.length, 1)));
    const layers = createStructuredLayersForScene(baseDraft, sceneId, payload, durationMs);
    const sceneName = [options?.prefix, payload.sceneName].filter(Boolean).join(" ").trim();

    if (!firstNewSceneId) {
      firstNewSceneId = sceneId;
    }

    nextScenes.push({
      id: sceneId,
      name: sceneName,
      durationMs,
      background: baseDraft.background,
      transition: index === 0 ? "cut" : "fade",
      layerIds: layers.map((layer) => layer.id),
      headline: payload.headline ?? payload.layerName,
      body: payload.body ?? payload.text,
      cta: payload.cta ?? "",
      hook: payload.hook ?? "",
      narration: payload.narration ?? payload.text,
      sceneType: payload.sceneType ?? "default",
      visualDirectionId: payload.visualDirectionId,
    });

    nextLayers.push(...layers);
  });

  return {
    ...baseDraft,
    scenes: nextScenes,
    layers: nextLayers,
    activeSceneId: options?.activateFirst === false ? baseDraft.activeSceneId : firstNewSceneId ?? baseDraft.activeSceneId,
  };
}

function assignPayloadsToExistingScenes(baseDraft: StudioDraft, payloads: AtlasScenePayload[]) {
  const scenes = [...(baseDraft.scenes ?? [])];
  if (scenes.length === 0) {
    return baseDraft;
  }

  return {
    ...baseDraft,
    scenes: scenes.map((scene, index) => {
      const payload = payloads[index];
      if (!payload) {
        return scene;
      }

      return {
        ...scene,
        headline: payload.headline ?? scene.headline,
        body: payload.body ?? payload.text,
        cta: payload.cta ?? scene.cta,
        hook: payload.hook ?? scene.hook,
        narration: payload.narration ?? payload.text,
        sceneType: payload.sceneType ?? scene.sceneType,
        visualDirectionId: payload.visualDirectionId ?? scene.visualDirectionId,
      };
    }),
  };
}

export function CreateStudio() {
  const navigate = useNavigate();
  const {
    state: { assets, accounts, aiBriefs, brandKit, voiceProjects },
    selectedDraft,
    saveDraft,
    selectDraft,
    updateDraft,
    addAssets,
    addAiBrief,
    updateAiBrief,
    addVoiceProject,
    updateVoiceProject,
  } = useStudio();

  const [activeTool, setActiveTool] = useState<StudioTool>("select");
  const [activeFlyout, setActiveFlyout] = useState<FlyoutPanel>("tools");
  const [workingDraft, setWorkingDraft] = useState<StudioDraft | undefined>();
  const [selectedLayerId, setSelectedLayerId] = useState<string | undefined>();
  const [history, setHistory] = useState<HistoryState>({ past: [], future: [] });
  const [previewMode, setPreviewMode] = useState(false);
  const [canvasGuides, setCanvasGuides] = useState<CanvasGuide[]>([]);
  const [assetSearch, setAssetSearch] = useState("");
  const [atlasStatus, setAtlasStatus] = useState("ATLAS is ready to route structured requests into this draft.");
  const [selectedPresetId, setSelectedPresetId] = useState<string | undefined>();
  const [previewPlatform, setPreviewPlatform] = useState<PlatformId>("instagram");
  const [atlasTab, setAtlasTab] = useState<AtlasTabId>("ideas");
  const [atlasResponse, setAtlasResponse] = useState("");
  const [activeAtlasBriefId, setActiveAtlasBriefId] = useState<string | undefined>();
  const [atlasWorkflowStage, setAtlasWorkflowStage] = useState<AtlasWorkflowStage>("brief");
  const [atlasExpandedOptionIds, setAtlasExpandedOptionIds] = useState<string[]>([]);
  const [atlasCompareOptionIds, setAtlasCompareOptionIds] = useState<string[]>([]);
  const [atlasPreviewApplyMode, setAtlasPreviewApplyMode] = useState<AtlasApplyMode | undefined>();
  const [atlasRuntimeNow, setAtlasRuntimeNow] = useState(() => Date.now());
  const [atlasProvider, setAtlasProvider] = useState<ModelProviderId>("gpt");
  const [atlasModel, setAtlasModel] = useState("gpt-5.4");
  const [atlasVariants, setAtlasVariants] = useState(3);
  const [atlasBriefing, setAtlasBriefing] = useState<AtlasBriefingState>(() => createInitialAtlasBriefingState("instagram"));
  const [createVoiceNote, setCreateVoiceNote] = useState("");
  const atlasBridgeAvailable = isAtlasBridgeAvailable();

  const fileInputRef = useRef<HTMLInputElement | null>(null);
  const dragRef = useRef<DragState | null>(null);
  const lastSyncedDraftIdRef = useRef<string | undefined>(undefined);

  useEffect(() => {
    if (!selectedDraft) {
      return;
    }

    const normalized = normalizeDraft(selectedDraft);
    const draftChanged = lastSyncedDraftIdRef.current !== normalized.id || workingDraft?.updatedAt !== normalized.updatedAt;

    if (!draftChanged) {
      return;
    }

    lastSyncedDraftIdRef.current = normalized.id;
    setWorkingDraft(normalized);
    setSelectedLayerId(normalized.layers[0]?.id);
    setSelectedPresetId(normalized.formatId);
    setPreviewPlatform(normalized.linkedPlatformIds[0] ?? "instagram");
    setHistory({ past: [], future: [] });
  }, [selectedDraft, workingDraft?.updatedAt]);

  useEffect(() => {
    setAtlasBriefing((current) => ({
      ...current,
      caption: {
        ...current.caption,
        platform: current.caption.platform || previewPlatform,
        audience: current.caption.audience || brandKit.audience || current.caption.audience,
        brandVoice: current.caption.brandVoice || brandKit.tone || current.caption.brandVoice,
      },
      visual: {
        ...current.visual,
        platform: current.visual.platform || previewPlatform,
      },
      adaptation: {
        ...current.adaptation,
        targetPlatform: current.adaptation.targetPlatform || previewPlatform,
      },
      ideas: {
        ...current.ideas,
        targetAudience: current.ideas.targetAudience || brandKit.audience || current.ideas.targetAudience,
      },
    }));
  }, [brandKit.audience, brandKit.tone, previewPlatform]);

  const atlasTabConfig = ATLAS_TABS.find((tab) => tab.id === atlasTab) ?? ATLAS_TABS[0];
  const atlasTaskMeta = AI_TASKS.find((task) => task.id === atlasTabConfig.taskType) ?? AI_TASKS[0];

  useEffect(() => {
    const recommended = recommendedProvidersForTask(atlasTabConfig.taskType);
    const nextProvider = recommended[0] ?? "gpt";
    const nextModel = modelsForProvider(nextProvider)[0]?.id ?? "gpt-5.4";
    setAtlasProvider(nextProvider);
    setAtlasModel(nextModel);
  }, [atlasTabConfig.taskType]);

  useEffect(() => {
    if (!createVoiceNote) {
      return;
    }

    const timeout = window.setTimeout(() => setCreateVoiceNote(""), 2400);
    return () => window.clearTimeout(timeout);
  }, [createVoiceNote]);

  useEffect(() => {
    return subscribeAtlasBridge((message) => {
      if (message.type !== "create-agent-mic-transcript" && message.type !== "create-mic-transcript") {
        return;
      }

      const payload = (message.payload ?? {}) as { transcript?: string };
      const transcript = typeof payload.transcript === "string" ? payload.transcript.trim() : "";
      if (!transcript) {
        return;
      }

      updateActiveAtlasAdvancedNotes(transcript);
      setCreateVoiceNote("Voice captured. Press Generate.");
    });
  }, [atlasTab]);

  useEffect(() => {
    setAtlasWorkflowStage("brief");
  }, [atlasTab]);

  const draft = workingDraft;
  const scenes = draft?.scenes ?? [];
  const activeScene = draft && scenes.length > 0 ? scenes.find((scene) => scene.id === draft.activeSceneId) ?? scenes[0] : undefined;
  const visibleLayers = useMemo(() => {
    if (!draft || !activeScene) {
      return [];
    }

    return sceneLayers(draft, activeScene.id);
  }, [activeScene, draft]);

  const selectedLayer = useMemo(
    () => visibleLayers.find((layer) => layer.id === selectedLayerId) ?? visibleLayers[visibleLayers.length - 1],
    [selectedLayerId, visibleLayers],
  );

  const filteredAssets = useMemo(
    () =>
      assets.filter((asset) =>
        [asset.name, asset.folder, asset.tags.join(" ")].join(" ").toLowerCase().includes(assetSearch.toLowerCase()),
      ),
    [assetSearch, assets],
  );

  const previewPlatforms = useMemo(() => {
    if (!draft) {
      return ["instagram"] as PlatformId[];
    }

    return Array.from(new Set([...draft.linkedPlatformIds, ...accounts.map((account) => account.platformId), "instagram"])) as PlatformId[];
  }, [accounts, draft]);

  const linkedAccounts = useMemo(
    () => accounts.filter((account) => draft?.linkedPlatformIds.includes(account.platformId)),
    [accounts, draft?.linkedPlatformIds],
  );
  const atlasDraftBriefs = useMemo(
    () => aiBriefs.filter((brief) => brief.draftId === draft?.id).slice(0, 8),
    [aiBriefs, draft?.id],
  );
  const activeAtlasBrief = useMemo(
    () => atlasDraftBriefs.find((brief) => brief.id === activeAtlasBriefId) ?? atlasDraftBriefs[0],
    [activeAtlasBriefId, atlasDraftBriefs],
  );
  const atlasResultGroups = activeAtlasBrief?.resultGroups ?? [];
  const atlasResultOptions = useMemo(() => flattenResultOptions(atlasResultGroups), [atlasResultGroups]);
  const atlasSelectedOptionIds = activeAtlasBrief?.selectedResultIds ?? [];
  const atlasSelectedOptions = useMemo(
    () => atlasResultOptions.filter((option) => atlasSelectedOptionIds.includes(option.id)),
    [atlasResultOptions, atlasSelectedOptionIds],
  );
  const atlasCompareOptions = useMemo(
    () => atlasCompareOptionIds.map((optionId) => atlasResultOptions.find((option) => option.id === optionId)).filter((option): option is AIResultOption => Boolean(option)),
    [atlasCompareOptionIds, atlasResultOptions],
  );
  const atlasInFlightBriefs = useMemo(
    () => atlasDraftBriefs.filter((brief) => brief.status === "queued" || brief.status === "retrying" || brief.status === "running"),
    [atlasDraftBriefs],
  );
  const atlasRecentBriefs = useMemo(() => atlasDraftBriefs.slice(0, 5), [atlasDraftBriefs]);
  const atlasExecutionProgress = useMemo(() => atlasBriefProgressPercent(activeAtlasBrief, atlasRuntimeNow), [activeAtlasBrief, atlasRuntimeNow]);
  const atlasExecutionRuntime = useMemo(() => activeAtlasBrief ? formatRuntimeDuration(atlasBriefDurationMs(activeAtlasBrief, atlasRuntimeNow)) : "0s", [activeAtlasBrief, atlasRuntimeNow]);
  const atlasExecutionTimeline = useMemo(() => atlasExecutionSteps(activeAtlasBrief), [activeAtlasBrief]);
  const atlasActiveStatusLabel = useMemo(() => atlasBriefStatusLabel(activeAtlasBrief?.status), [activeAtlasBrief?.status]);
  const atlasActiveStatusTone = useMemo(() => atlasBriefStatusTone(activeAtlasBrief?.status), [activeAtlasBrief?.status]);
  const atlasOperationalCopy = useMemo(() => atlasBriefOperationalMessage(activeAtlasBrief, atlasRuntimeNow), [activeAtlasBrief, atlasRuntimeNow]);
  const atlasRecoveryCopy = useMemo(() => atlasBriefRecoveryMessage(activeAtlasBrief), [activeAtlasBrief]);
  const atlasCanCancel = Boolean(
    atlasBridgeAvailable
      && activeAtlasBrief?.requestId
      && (activeAtlasBrief.status === "running" || activeAtlasBrief.status === "retrying"),
  );
  const atlasHasRecoverableError = Boolean(
    activeAtlasBrief
      && (activeAtlasBrief.status === "failed" || activeAtlasBrief.status === "timed-out" || activeAtlasBrief.status === "cancelled"),
  );
  const atlasReviewLoading = Boolean(
    activeAtlasBrief
      && (activeAtlasBrief.status === "queued" || activeAtlasBrief.status === "retrying" || activeAtlasBrief.status === "running")
      && atlasResultGroups.length === 0,
  );
  const atlasReviewRefreshing = Boolean(activeAtlasBrief?.status === "retrying" && atlasResultGroups.length > 0);

  useEffect(() => {
    if (atlasInFlightBriefs.length === 0) {
      return;
    }

    const timer = window.setInterval(() => setAtlasRuntimeNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [atlasInFlightBriefs.length]);

  const atlasModels = useMemo(() => modelsForProvider(atlasProvider), [atlasProvider]);
  const atlasPlatformOptions = useMemo(
    () => Array.from(new Set(["instagram", "tiktok", "youtube", "linkedin", ...previewPlatforms]))
      .map((platformId) => ({
        value: platformId,
        label: PLATFORM_DEFINITIONS.find((item) => item.id === platformId)?.label ?? platformId,
      })),
    [previewPlatforms],
  );

  useEffect(() => {
    if (activeAtlasBriefId && atlasDraftBriefs.some((brief) => brief.id === activeAtlasBriefId)) {
      return;
    }

    const nextBrief = atlasDraftBriefs.find((brief) => brief.taskType === atlasTabConfig.taskType) ?? atlasDraftBriefs[0];
    setActiveAtlasBriefId(nextBrief?.id);
  }, [activeAtlasBriefId, atlasDraftBriefs, atlasTabConfig.taskType]);

  useEffect(() => {
    const validOptionIds = new Set(atlasResultOptions.map((option) => option.id));
    setAtlasExpandedOptionIds((current) => current.filter((optionId) => validOptionIds.has(optionId)));
    setAtlasCompareOptionIds((current) => current.filter((optionId) => validOptionIds.has(optionId)).slice(0, 2));
  }, [atlasResultOptions]);

  useEffect(() => {
    if (atlasModels.some((model) => model.id === atlasModel)) {
      return;
    }

    setAtlasModel(atlasModels[0]?.id ?? "gpt-5.4");
  }, [atlasModel, atlasModels]);

  const atlasStructuredRequest = useMemo(() => buildStructuredAtlasRequest({
    atlasTab,
    atlasVariants,
    briefing: atlasBriefing,
    brandAudience: brandKit.audience,
    brandTone: brandKit.tone,
    previewPlatform,
  }), [atlasBriefing, atlasTab, atlasVariants, brandKit.audience, brandKit.tone, previewPlatform]);

  useEffect(() => {
    if (activeAtlasBrief?.status === "queued" || activeAtlasBrief?.status === "retrying" || activeAtlasBrief?.status === "running") {
      setAtlasWorkflowStage((current) => (current === "brief" ? "generate" : current));
      return;
    }

    if (atlasResultOptions.length > 0) {
      setAtlasWorkflowStage((current) => (current === "brief" || current === "generate" ? "review" : current));
    }
  }, [activeAtlasBrief?.status, atlasResultOptions.length]);

  const atlasPacket = useMemo(() => {
    return buildAtlasPacket({
      providerId: atlasProvider,
      modelId: atlasModel,
      taskType: atlasTabConfig.taskType,
      tone: atlasStructuredRequest.tone,
      platformId: atlasStructuredRequest.platformId,
      objective: atlasStructuredRequest.objective,
      variants: atlasStructuredRequest.variantsRequested,
      targetSurface: atlasTabConfig.surface,
      brief: atlasStructuredRequest.brief,
      brandName: brandKit.brandName,
      brandTone: brandKit.tone,
      audience: brandKit.audience,
      accountsCount: accounts.length,
      assetsCount: assets.length,
      draft,
      sceneName: activeScene?.name,
      selectedLayerText: selectedLayer?.text?.trim(),
      linkedPlatforms: draft?.linkedPlatformIds,
      linkedAssetsCount: draft?.linkedAssetIds.length,
    });
  }, [accounts.length, activeScene?.name, assets.length, atlasModel, atlasProvider, atlasStructuredRequest.brief, atlasStructuredRequest.objective, atlasStructuredRequest.platformId, atlasStructuredRequest.tone, atlasStructuredRequest.variantsRequested, atlasTabConfig, brandKit.audience, brandKit.brandName, brandKit.tone, draft, selectedLayer?.text]);

  const atlasResponsePlaceholder = useMemo(() => {
    switch (atlasTab) {
      case "ideas":
        return "Paste the approved idea list or creative directions here. Each paragraph can become a new scene or concept beat.";
      case "captions":
        return "Paste the approved caption options here. Apply one directly to the active draft caption or save it into notes.";
      case "hooks":
        return "Paste the approved hook lines here. Apply one into the selected headline layer or create a new text layer.";
      case "scripts":
        return "Paste the raw script response here, then parse it into structured sections and selectable script cards.";
      case "carousels":
        return "Paste the raw carousel structure here, then parse it into slide groups and carousel options.";
      case "visuals":
        return "Paste the raw visual concept response here, then parse it into concept cards and scene groups.";
      case "variants":
        return "Paste the raw variant response here, then parse it into numbered variants and apply the selected ones.";
      case "adaptation":
        return "Paste the raw platform adaptation response here, then parse it into platform-specific rewrites.";
      case "voiceovers":
        return "Paste the raw voiceover response here, then parse or send the selected script into Voice Studio.";
      default:
        return "Paste the approved model response here.";
    }
  }, [atlasTab]);

  function updateAtlasBriefingSection<TKey extends keyof AtlasBriefingState>(section: TKey, next: Partial<AtlasBriefingState[TKey]>) {
    setAtlasBriefing((current) => ({
      ...current,
      [section]: {
        ...current[section],
        ...next,
      },
    }));
  }

  function updateActiveAtlasAdvancedNotes(nextValue: string) {
    switch (atlasTab) {
      case "ideas":
        updateAtlasBriefingSection("ideas", { advancedNotes: nextValue });
        break;
      case "captions":
        updateAtlasBriefingSection("caption", { advancedNotes: nextValue });
        break;
      case "hooks":
        updateAtlasBriefingSection("hook", { advancedNotes: nextValue });
        break;
      case "scripts":
        updateAtlasBriefingSection("script", { advancedNotes: nextValue });
        break;
      case "carousels":
        updateAtlasBriefingSection("carousel", { advancedNotes: nextValue });
        break;
      case "visuals":
        updateAtlasBriefingSection("visual", { advancedNotes: nextValue });
        break;
      case "variants":
        updateAtlasBriefingSection("variant", { advancedNotes: nextValue });
        break;
      case "adaptation":
        updateAtlasBriefingSection("adaptation", { advancedNotes: nextValue });
        break;
      case "voiceovers":
        updateAtlasBriefingSection("voiceover", { advancedNotes: nextValue });
        break;
      default:
        break;
    }
  }

  function handleCreateMicClick() {
    if (!CREATE_MIC_WIRED) {
      setCreateVoiceNote("Mic not wired");
      return;
    }
  }

  const activeAtlasAdvancedNotes = useMemo(() => {
    switch (atlasTab) {
      case "ideas":
        return atlasBriefing.ideas.advancedNotes;
      case "captions":
        return atlasBriefing.caption.advancedNotes;
      case "hooks":
        return atlasBriefing.hook.advancedNotes;
      case "scripts":
        return atlasBriefing.script.advancedNotes;
      case "carousels":
        return atlasBriefing.carousel.advancedNotes;
      case "visuals":
        return atlasBriefing.visual.advancedNotes;
      case "variants":
        return atlasBriefing.variant.advancedNotes;
      case "adaptation":
        return atlasBriefing.adaptation.advancedNotes;
      case "voiceovers":
        return atlasBriefing.voiceover.advancedNotes;
      default:
        return "";
    }
  }, [atlasBriefing, atlasTab]);

  const atlasApplyActions = useMemo(() => {
    const actions: Array<{ mode: AtlasApplyMode; label: string }> = [];
    const hasTextSelection = isTextEditableLayer(selectedLayer);

    switch (atlasTabConfig.taskType) {
      case "caption":
        actions.push(
          { mode: "set-primary-caption", label: "Set primary caption" },
          { mode: "append-alternate-caption", label: "Append alternate caption" },
          { mode: "save-caption-variant-set", label: "Save caption variant set" },
          { mode: "assign-platform-caption", label: `Assign to ${previewPlatform} version` },
          { mode: "create-variation-draft", label: "New variation draft" },
          { mode: "add-to-notes", label: "Add to notes" },
        );
        break;
      case "hook":
        actions.push(
          { mode: "set-active-hook", label: "Set active hook" },
          { mode: "save-alternate-hooks", label: "Save alternate hooks" },
          { mode: hasTextSelection ? "replace-selection" : "bind-hook-to-scene", label: hasTextSelection ? "Bind to headline layer" : "Bind hook to current scene" },
          { mode: "add-to-notes", label: "Add to notes" },
        );
        break;
      case "script":
        actions.push(
          { mode: "insert-script-scene", label: "Insert scene sequence" },
          { mode: "create-scene-blocks", label: "Create scene blocks" },
          { mode: "assign-narration-sections", label: "Assign narration sections" },
          { mode: "send-to-voice", label: "Send to voice" },
          { mode: "add-to-notes", label: "Add to notes" },
        );
        break;
      case "carousel-structure":
        actions.push(
          { mode: "generate-slide-sequence", label: "Generate slide sequence" },
          { mode: "create-scene-blocks", label: "Create slide blocks" },
          { mode: "create-variation-draft", label: "New variation draft" },
          { mode: "add-to-notes", label: "Add to notes" },
        );
        break;
      case "visual-concept":
        actions.push(
          { mode: "save-concept-board", label: "Save to concept board" },
          { mode: "apply-visual-direction", label: "Apply to active draft" },
          { mode: "create-visual-direction", label: "Create visual direction" },
          { mode: "add-to-notes", label: "Add to notes" },
        );
        break;
      case "ideas":
        actions.push(
          { mode: "create-scene-blocks", label: "Generate new scene/page" },
          { mode: "create-variation-draft", label: "New variation draft" },
          { mode: "add-to-notes", label: "Add to notes" },
        );
        break;
      case "platform-adaptation":
        actions.push(
          { mode: "set-platform-adaptation", label: `Set ${previewPlatform} version` },
          { mode: "compare-platform-adaptation", label: "Compare against current" },
          { mode: "assign-platform-caption", label: `Assign caption to ${previewPlatform}` },
          { mode: "add-to-notes", label: "Add to notes" },
        );
        break;
      case "campaign-pack":
        actions.push(
          { mode: "store-variant-pack", label: "Store variant pack" },
          { mode: "create-variation-draft", label: "New variation draft" },
          { mode: "create-alternate-scene-sequence", label: "Create alternate scene sequence" },
          { mode: "add-to-notes", label: "Add to notes" },
        );
        break;
      case "voiceover":
        actions.push(
          { mode: "send-to-voice", label: "Hydrate voice project" },
          { mode: "create-scene-blocks", label: "Attach to active timeline" },
          { mode: "add-to-notes", label: "Add to notes" },
        );
        break;
      default:
        actions.push({ mode: "add-to-notes", label: "Add to notes" });
        if (supportsSceneCreation(atlasTabConfig.taskType)) {
          actions.push({ mode: "create-scene-blocks", label: "Generate new scene/page" });
        }
        break;
    }

    return actions;
  }, [atlasTabConfig.taskType, previewPlatform, selectedLayer]);
  const atlasActiveApplyMode = atlasPreviewApplyMode ?? atlasApplyActions[0]?.mode;

  useEffect(() => {
    const nextMode = atlasApplyActions[0]?.mode;
    if (!nextMode) {
      setAtlasPreviewApplyMode(undefined);
      return;
    }

    setAtlasPreviewApplyMode((current) => (
      current && atlasApplyActions.some((action) => action.mode === current)
        ? current
        : nextMode
    ));
  }, [atlasApplyActions]);

  const atlasApplyPreview = useMemo(() => {
    if (!atlasActiveApplyMode || !draft) {
      return null;
    }

    const previewOptions = atlasSelectedOptions.length > 0 ? atlasSelectedOptions : atlasResultOptions.slice(0, 1);
    const previewText = previewOptions
      .map((option) => applyTextForOption(option, atlasActiveApplyMode) || formatResultOption(option))
      .filter(Boolean)
      .join("\n\n")
      .trim();
    const selectedCount = previewOptions.length;
    const sceneName = activeScene?.name ?? "current scene";
    const layerName = selectedLayer?.name ?? "selected layer";

    switch (atlasActiveApplyMode) {
      case "replace-selection":
        return {
          title: "Replace selected canvas copy",
          description: `ATLAS will overwrite ${layerName} with the reviewed output instead of creating a side note or detached draft artifact.`,
          targets: ["Selected layer text", `Scene: ${sceneName}`, `Results queued: ${selectedCount}`],
          previewText,
        };
      case "set-primary-caption":
      case "append-alternate-caption":
      case "save-caption-variant-set":
      case "assign-platform-caption":
        return {
          title: "Caption system update",
          description: atlasActiveApplyMode === "set-primary-caption"
            ? "The chosen caption becomes the draft's primary caption and syncs into the active platform version."
            : atlasActiveApplyMode === "append-alternate-caption"
              ? "The caption stays additive, preserving the current live caption while filing alternates into version memory."
              : atlasActiveApplyMode === "save-caption-variant-set"
                ? "ATLAS will create a reusable caption variant set on the draft for later switching and testing."
                : `The selected caption will be assigned specifically to the ${previewPlatform} platform slot.`,
          targets: ["Draft caption", `${previewPlatform} version`, "Caption variant memory"],
          previewText,
        };
      case "set-active-hook":
      case "save-alternate-hooks":
      case "bind-hook-to-scene":
        return {
          title: "Hook structure update",
          description: atlasActiveApplyMode === "set-active-hook"
            ? "ATLAS will promote the chosen hook into the draft headline system and update the active scene hook."
            : atlasActiveApplyMode === "save-alternate-hooks"
              ? "The chosen hooks will be saved as alternates without replacing the live headline."
              : `The hook will bind directly into ${sceneName} so the scene carries a real structural headline instead of loose text.`,
          targets: ["Draft primary hook", `Scene: ${sceneName}`, "Hook variation bank"],
          previewText,
        };
      case "insert-script-scene":
      case "create-scene-blocks":
      case "generate-slide-sequence":
      case "create-alternate-scene-sequence":
      case "assign-narration-sections":
        return {
          title: "Scene timeline mutation",
          description: atlasActiveApplyMode === "assign-narration-sections"
            ? "Existing scenes will keep their structure while narration fields are hydrated from the reviewed output."
            : atlasActiveApplyMode === "generate-slide-sequence"
              ? "The reviewed carousel will generate actual slide scenes with headlines, body copy, and CTA fields."
              : atlasActiveApplyMode === "create-alternate-scene-sequence"
                ? "ATLAS will create a second structural scene path for comparison without destroying the current one."
                : "ATLAS will create real scene objects and layers inside the working draft, not just append notes.",
          targets: ["Scene records", "Timeline order", "Layer payloads"],
          previewText,
        };
      case "save-concept-board":
      case "apply-visual-direction":
      case "create-visual-direction":
        return {
          title: "Visual direction update",
          description: atlasActiveApplyMode === "apply-visual-direction"
            ? "Palette, mood, and composition hints will be applied directly to the active draft and scene."
            : "ATLAS will store the concept as a reusable visual direction entry in the draft concept board.",
          targets: ["Concept board", `Scene: ${sceneName}`, "Draft visual direction"],
          previewText,
        };
      case "set-platform-adaptation":
      case "compare-platform-adaptation":
        return {
          title: "Platform version update",
          description: atlasActiveApplyMode === "compare-platform-adaptation"
            ? `ATLAS will preserve the current ${previewPlatform} version and log the reviewed candidate as a comparison snapshot.`
            : `The ${previewPlatform} slot will become a real platform adaptation while preserving the previous original.`,
          targets: [`${previewPlatform} platform version`, "Preserved original", "Comparison state"],
          previewText,
        };
      case "create-variation-draft":
      case "store-variant-pack":
        return {
          title: "Variation output",
          description: atlasActiveApplyMode === "create-variation-draft"
            ? "A new sibling draft will be created from the reviewed result, preserving the current draft intact."
            : "The structured result will be stored as a variant pack the team can reopen and apply later.",
          targets: ["Alternate draft version", "Variant metadata", "Reusable strategy pack"],
          previewText,
        };
      case "send-to-voice":
        return {
          title: "Voice project hydration",
          description: "ATLAS will create or update a real voice project, link it to this draft, and push narration into the active scene path when possible.",
          targets: ["Voice project", `Scene: ${sceneName}`, "Narration timeline"],
          previewText,
        };
      case "add-to-notes":
      default:
        return {
          title: "Draft notes append",
          description: "This route preserves the draft while storing the reviewed output into draft notes for manual use.",
          targets: ["Draft notes", `Results selected: ${selectedCount}`],
          previewText,
        };
    }
  }, [atlasActiveApplyMode, activeScene?.name, atlasResultOptions, atlasSelectedOptions, draft, previewPlatform, selectedLayer?.name]);

  useEffect(() => {
    if (!activeAtlasBrief) {
      return;
    }

    setAtlasResponse(activeAtlasBrief.responseText ?? "");
  }, [activeAtlasBrief]);

  const scale = useMemo(() => {
    if (!draft) {
      return 1;
    }

    const maxWidth = previewMode ? 1480 : 1160;
    const maxHeight = previewMode ? 860 : 700;
    return Math.min(maxWidth / draft.width, maxHeight / draft.height, 1);
  }, [draft, previewMode]);

  function persistDraft(nextDraft: StudioDraft, options?: { history?: boolean }) {
    const normalized = normalizeDraft(nextDraft);
    if (draft && options?.history !== false) {
      setHistory((current) => ({ past: [...current.past, draft], future: [] }));
    }

    setWorkingDraft(normalized);
    updateDraft(normalized.id, () => normalized);
    lastSyncedDraftIdRef.current = normalized.id;
    setSelectedPresetId(normalized.formatId);
  }

  function mutateDraft(mutator: (current: StudioDraft) => StudioDraft, options?: { history?: boolean }) {
    if (!draft) {
      return;
    }

    persistDraft(mutator(draft), options);
  }

  function ensureDraftExists(formatId = FORMAT_PRESETS[0].id) {
    if (draft) {
      return draft;
    }

    const nextDraft = normalizeDraft(createDraftFromPreset(formatId));
    saveDraft(nextDraft);
    selectDraft(nextDraft.id);
    setWorkingDraft(nextDraft);
    setSelectedLayerId(undefined);
    setSelectedPresetId(nextDraft.formatId);
    return nextDraft;
  }

  function updateScene(sceneId: string, updater: (scene: DraftScene) => DraftScene) {
    mutateDraft((current) => ({
      ...current,
      scenes: (current.scenes ?? []).map((scene) => (scene.id === sceneId ? updater(scene) : scene)),
    }));
  }

  function updateLayer(layerId: string, updater: (layer: DraftLayer) => DraftLayer, options?: { history?: boolean }) {
    mutateDraft(
      (current) => ({
        ...current,
        layers: current.layers.map((layer) => (layer.id === layerId ? updater(layer) : layer)),
      }),
      options,
    );
  }

  function createLayer(type: DraftLayer["type"], overrides?: Partial<DraftLayer>) {
    const baseDraft = ensureDraftExists();
    const targetScene = baseDraft.scenes?.find((scene) => scene.id === baseDraft.activeSceneId) ?? baseDraft.scenes?.[0];
    if (!targetScene) {
      return;
    }

    const layerId = makeId("layer");
    const layer: DraftLayer = {
      id: layerId,
      type,
      sceneId: targetScene.id,
      name:
        type === "text"
          ? "Text"
          : type === "shape"
            ? "Shape"
            : type === "sticker"
              ? "Sticker"
              : type === "overlay"
                ? "Overlay"
                : type === "audio"
                  ? "Audio"
                  : type === "video"
                    ? "Video"
                    : "Media",
      visible: true,
      locked: false,
      x: Math.round(baseDraft.width * 0.18),
      y: Math.round(baseDraft.height * 0.18),
      width: type === "text" ? Math.round(baseDraft.width * 0.52) : Math.round(baseDraft.width * 0.34),
      height: type === "text" ? 100 : Math.round(baseDraft.height * 0.24),
      rotation: 0,
      opacity: type === "overlay" ? 0.45 : 1,
      color: type === "text" ? "#ffffff" : "#38bdf8",
      gradient: type === "overlay" ? "linear-gradient(135deg,rgba(15,23,42,0.1),rgba(168,85,247,0.45))" : undefined,
      text: type === "text" ? "Add your message" : type === "sticker" ? "NEW" : undefined,
      fontSize: type === "text" ? 56 : type === "sticker" ? 30 : undefined,
      fontWeight: type === "text" ? 700 : 600,
      assetId: undefined,
      blendMode: type === "overlay" ? "overlay" : "normal",
      filter: "none",
      animation: type === "text" ? "fade-in" : "none",
      transition: targetScene.transition,
      assetFit: "cover",
      cropX: 0,
      cropY: 0,
      cropZoom: 1,
      cornerRadius: type === "shape" ? 24 : 0,
      startMs: 0,
      endMs: targetScene.durationMs,
      ...overrides,
    };

    persistDraft({
      ...baseDraft,
      layers: [...baseDraft.layers, layer],
      scenes: (baseDraft.scenes ?? []).map((scene) =>
        scene.id === targetScene.id ? { ...scene, layerIds: [...scene.layerIds, layerId] } : scene,
      ),
      activeSceneId: targetScene.id,
    });
    setSelectedLayerId(layerId);
  }

  function addScene() {
    const baseDraft = ensureDraftExists(selectedPresetId);
    const sceneId = makeId("scene");
    persistDraft({
      ...baseDraft,
      scenes: [
        ...(baseDraft.scenes ?? []),
        {
          id: sceneId,
          name: nextSceneName(baseDraft.scenes ?? []),
          durationMs: Math.max(3000, Math.round(baseDraft.durationMs / Math.max(1, (baseDraft.scenes ?? []).length || 1))),
          background: baseDraft.background,
          transition: "fade" as const,
          layerIds: [],
        },
      ],
      activeSceneId: sceneId,
    });
    setSelectedLayerId(undefined);
  }

  function duplicateScene() {
    if (!draft || !activeScene) {
      return;
    }

    persistDraft(duplicateSceneLayers(draft, activeScene.id));
  }

  function removeScene(sceneId: string) {
    if (!draft || !draft.scenes || draft.scenes.length <= 1) {
      return;
    }

    const scene = draft.scenes.find((item) => item.id === sceneId);
    if (!scene) {
      return;
    }

    const nextScenes = draft.scenes.filter((item) => item.id !== sceneId);
    const nextLayers = draft.layers.filter((layer) => !scene.layerIds.includes(layer.id));
    const nextActiveSceneId = draft.activeSceneId === sceneId ? nextScenes[0]?.id : draft.activeSceneId;
    persistDraft({
      ...draft,
      scenes: nextScenes,
      layers: nextLayers,
      activeSceneId: nextActiveSceneId,
      linkedAssetIds: draft.linkedAssetIds,
    });
    setSelectedLayerId(undefined);
  }

  function reorderLayer(layerId: string, direction: "up" | "down") {
    if (!draft || !activeScene) {
      return;
    }

    const index = activeScene.layerIds.indexOf(layerId);
    if (index === -1) {
      return;
    }

    const nextIndex = direction === "up" ? index + 1 : index - 1;
    if (nextIndex < 0 || nextIndex >= activeScene.layerIds.length) {
      return;
    }

    const layerIds = [...activeScene.layerIds];
    const [moved] = layerIds.splice(index, 1);
    layerIds.splice(nextIndex, 0, moved);
    updateScene(activeScene.id, (scene) => ({ ...scene, layerIds }));
  }

  function removeLayer(layerId: string) {
    if (!draft || !activeScene) {
      return;
    }

    persistDraft({
      ...draft,
      layers: draft.layers.filter((layer) => layer.id !== layerId),
      scenes: (draft.scenes ?? []).map((scene) =>
        scene.id === activeScene.id
          ? { ...scene, layerIds: scene.layerIds.filter((id) => id !== layerId) }
          : scene,
      ),
    });
    setSelectedLayerId(undefined);
  }

  function duplicateLayer(layerId: string) {
    if (!draft || !activeScene) {
      return;
    }

    const sourceLayer = draft.layers.find((layer) => layer.id === layerId);
    if (!sourceLayer) {
      return;
    }

    const duplicateId = makeId("layer");
    const duplicate = {
      ...sourceLayer,
      id: duplicateId,
      name: `${sourceLayer.name} Copy`,
      x: clamp(sourceLayer.x + 28, 0, draft.width - sourceLayer.width),
      y: clamp(sourceLayer.y + 28, 0, draft.height - sourceLayer.height),
    };

    persistDraft({
      ...draft,
      layers: [...draft.layers, duplicate],
      scenes: (draft.scenes ?? []).map((scene) =>
        scene.id === activeScene.id
          ? { ...scene, layerIds: [...scene.layerIds, duplicateId] }
          : scene,
      ),
    });
    setSelectedLayerId(duplicateId);
  }

  function applyPreset(formatId: string) {
    const baseDraft = ensureDraftExists(formatId);
    const preset = FORMAT_PRESETS.find((item) => item.id === formatId);
    if (!preset) {
      return;
    }

    persistDraft({
      ...baseDraft,
      formatId: preset.id,
      width: preset.width,
      height: preset.height,
      contentType: preset.contentType,
      durationMs: preset.motion ? Math.max(baseDraft.durationMs, 12000) : Math.min(baseDraft.durationMs, 6000),
      linkedPlatformIds: preset.recommendedPlatforms,
      scenes: (baseDraft.scenes ?? []).map((scene) => ({
        ...scene,
        durationMs: preset.motion ? Math.max(scene.durationMs, 4000) : Math.min(scene.durationMs, 5000),
      })),
      layers: baseDraft.layers.map((layer) => ({
        ...layer,
        x: Math.round((layer.x / Math.max(1, baseDraft.width)) * preset.width),
        y: Math.round((layer.y / Math.max(1, baseDraft.height)) * preset.height),
        width: Math.round((layer.width / Math.max(1, baseDraft.width)) * preset.width),
        height: Math.round((layer.height / Math.max(1, baseDraft.height)) * preset.height),
      })),
    });
    setPreviewPlatform(preset.recommendedPlatforms[0] ?? "instagram");
    setAtlasStatus(`Resized the draft for ${preset.label}.`);
  }

  async function uploadAssets(files: FileList | null) {
    if (!files || files.length === 0) {
      return;
    }

    const createdAssets = await Promise.all(
      Array.from(files).map(async (file) => {
        const kind = file.type.startsWith("video/") ? "video" : file.type.startsWith("audio/") ? "audio" : "image";
        const shouldEmbed = (kind === "image" || kind === "video") && file.size <= 8 * 1024 * 1024;
        const dataUrl = shouldEmbed ? await fileToDataUrl(file) : undefined;
        return createMediaAsset({
          name: file.name,
          kind,
          mimeType: file.type,
          sizeBytes: file.size,
          dataUrl,
          storageMode: dataUrl ? "embedded" : "metadata-only",
          folder: "Create Studio",
          tags: [draft?.contentType.toLowerCase() ?? "studio"],
          source: "upload",
        });
      }),
    );

    addAssets(createdAssets);
    setActiveFlyout("assets");
    setAtlasStatus(`Imported ${createdAssets.length} asset${createdAssets.length === 1 ? "" : "s"} into the workspace.`);
  }

  function placeAsset(asset: MediaAsset) {
    const type = asset.kind === "audio" ? "audio" : asset.kind === "video" ? "video" : "asset";
    createLayer(type, {
      name: asset.name,
      assetId: asset.id,
      width: type === "audio" ? 360 : 420,
      height: type === "audio" ? 72 : 240,
      type,
    });
    mutateDraft((current) => ({
      ...current,
      linkedAssetIds: current.linkedAssetIds.includes(asset.id) ? current.linkedAssetIds : [...current.linkedAssetIds, asset.id],
    }), { history: false });
    setAtlasStatus(`${asset.name} is now linked into the active scene.`);
  }

  async function copyAtlasPacket() {
    try {
      await navigator.clipboard.writeText(atlasPacket);
      setAtlasStatus("Copied the ATLAS request packet to the clipboard.");
    } catch {
      setAtlasStatus("ATLAS packet is ready, but clipboard access is unavailable in this host.");
    }
  }

  function saveAtlasPacketToLibrary() {
    addAssets([
      createMediaAsset({
        name: `${atlasStructuredRequest.objective.replace(/\s+/g, " ").trim() || "atlas-request"}.atlas.txt`,
        kind: "template",
        mimeType: "text/plain",
        sizeBytes: atlasPacket.length,
        storageMode: "metadata-only",
        folder: "ATLAS Packets",
        tags: [atlasTabConfig.taskType, atlasProvider, previewPlatform],
        source: "ai-generated",
        transcript: atlasPacket,
      }),
    ]);
    setAtlasStatus("Saved the ATLAS packet into Media Library for reuse.");
  }

  function seedAtlasBriefFromDraft(source: "selection" | "draft" | "scene") {
    if (!draft) {
      return;
    }

    const lines = [
      `Draft: ${draft.title}`,
      `Format: ${draft.width}x${draft.height} ${draft.contentType}`,
      `Platform: ${previewPlatform}`,
    ];

    if (source === "selection" && selectedLayer?.text?.trim()) {
      lines.push(`Selected layer text: ${selectedLayer.text.trim()}`);
    }

    if (source === "draft") {
      if (draft.caption.trim()) {
        lines.push(`Current caption: ${draft.caption.trim()}`);
      }
      if (draft.notes.trim()) {
        lines.push(`Draft notes: ${draft.notes.trim()}`);
      }
    }

    if (source === "scene" && activeScene) {
      lines.push(`Active scene: ${activeScene.name}`);
      lines.push(`Scene duration: ${formatDuration(activeScene.durationMs)}`);
    }

    updateActiveAtlasAdvancedNotes(appendNotesBlock(activeAtlasAdvancedNotes, lines.join("\n")));
    setAtlasStatus("Pulled the current draft context into the ATLAS brief.");
  }

  function buildAtlasRequestPayload(overrides?: Partial<AtlasRequestPayload>) {
    if (!draft) {
      return null;
    }

    const payload: AtlasRequestPayload = {
      providerId: overrides?.providerId ?? atlasProvider,
      modelId: overrides?.modelId ?? atlasModel,
      taskType: overrides?.taskType ?? atlasTabConfig.taskType,
      platformId: overrides?.platformId ?? atlasStructuredRequest.platformId,
      objective: overrides?.objective ?? atlasStructuredRequest.objective,
      tone: overrides?.tone ?? atlasStructuredRequest.tone,
      contentType: overrides?.contentType ?? draft.contentType,
      brief: overrides?.brief ?? atlasStructuredRequest.brief,
      requestPacket: overrides?.requestPacket ?? "",
      draftId: overrides?.draftId ?? draft.id,
      targetSurface: overrides?.targetSurface ?? atlasTabConfig.surface,
      variantsRequested: overrides?.variantsRequested ?? atlasStructuredRequest.variantsRequested,
    };

    payload.requestPacket = overrides?.requestPacket ?? buildAtlasPacket({
      providerId: payload.providerId,
      modelId: payload.modelId,
      taskType: payload.taskType,
      tone: payload.tone,
      platformId: payload.platformId,
      objective: payload.objective,
      variants: payload.variantsRequested,
      targetSurface: payload.targetSurface,
      brief: payload.brief,
      brandName: brandKit.brandName,
      brandTone: brandKit.tone,
      audience: brandKit.audience,
      accountsCount: accounts.length,
      assetsCount: assets.length,
      draft,
      sceneName: activeScene?.name,
      selectedLayerText: selectedLayer?.text?.trim(),
      linkedPlatforms: draft.linkedPlatformIds,
      linkedAssetsCount: draft.linkedAssetIds.length,
    });

    return payload;
  }

  function hydrateStructuredBriefingFromStoredBrief(brief: AIBrief) {
    switch (brief.taskType) {
      case "caption":
        updateAtlasBriefingSection("caption", {
          platform: brief.platformId,
          tone: brief.tone,
          objective: brief.objective,
          advancedNotes: brief.brief,
        });
        break;
      case "hook":
        updateAtlasBriefingSection("hook", {
          contentAngle: brief.objective,
          advancedNotes: brief.brief,
        });
        break;
      case "script":
        updateAtlasBriefingSection("script", {
          tone: brief.tone,
          audience: brandKit.audience || atlasBriefing.script.audience,
          advancedNotes: brief.brief,
        });
        break;
      case "carousel-structure":
        updateAtlasBriefingSection("carousel", {
          tone: brief.tone,
          advancedNotes: brief.brief,
        });
        break;
      case "visual-concept":
        updateAtlasBriefingSection("visual", {
          platform: brief.platformId,
          mood: brief.tone,
          advancedNotes: brief.brief,
        });
        break;
      case "platform-adaptation":
        updateAtlasBriefingSection("adaptation", {
          targetPlatform: brief.platformId,
          advancedNotes: brief.brief,
        });
        break;
      case "campaign-pack":
        updateAtlasBriefingSection("variant", {
          baseConcept: brief.objective,
          toneDirection: brief.tone,
          advancedNotes: brief.brief,
        });
        break;
      case "voiceover":
        updateAtlasBriefingSection("voiceover", {
          emotion: brief.tone,
          advancedNotes: brief.brief,
        });
        break;
      case "ideas":
      default:
        updateAtlasBriefingSection("ideas", {
          campaignGoal: brief.objective,
          advancedNotes: brief.brief,
        });
        break;
    }
  }

  function atlasRequestPayloadFromBrief(brief: AIBrief): AtlasRequestPayload {
    return {
      providerId: brief.providerId,
      modelId: brief.modelId,
      taskType: brief.taskType,
      platformId: brief.platformId,
      objective: brief.objective,
      tone: brief.tone,
      contentType: brief.contentType,
      brief: brief.brief,
      requestPacket: brief.requestPacket,
      draftId: brief.draftId ?? draft?.id ?? "",
      targetSurface: brief.targetSurface,
      variantsRequested: brief.variantsRequested,
    };
  }

  function updateStructuredBrief(briefId: string, updater: (brief: AIBrief) => AIBrief) {
    updateAiBrief(briefId, updater);
    setActiveAtlasBriefId(briefId);
  }

  function reconcileBriefResults(briefId: string, request: AtlasRequestPayload, responseText: string, providerId: ModelProviderId, modelId: string) {
    const groups = parseAtlasResponse({
      taskType: request.taskType,
      providerId,
      modelId,
      responseText,
    });
    const firstOptionId = groups[0]?.options[0]?.id;

    updateStructuredBrief(briefId, (brief) => ({
      ...brief,
      providerId,
      modelId,
      taskType: request.taskType,
      platformId: request.platformId,
      objective: request.objective,
      tone: request.tone,
      contentType: request.contentType,
      brief: request.brief,
      requestPacket: request.requestPacket,
      draftId: request.draftId,
      targetSurface: request.targetSurface,
      variantsRequested: request.variantsRequested,
      responseText,
      resultGroups: groups,
      selectedResultIds: firstOptionId ? [firstOptionId] : [],
    }));

    return groups;
  }

  function cancelActiveAtlasExecution() {
    if (!activeAtlasBrief?.requestId || !atlasCanCancel) {
      return;
    }

    cancelAtlasBrief({
      requestId: activeAtlasBrief.requestId,
      briefId: activeAtlasBrief.id,
      providerId: activeAtlasBrief.providerId,
      modelId: activeAtlasBrief.modelId,
    });
    setAtlasStatus("Stopping the live ATLAS run and preserving the brief state.");
  }

  async function runAtlasBriefExecution(briefId: string, retry = false, requestOverride?: AtlasRequestPayload) {
    if (!draft) {
      return;
    }

    const request = requestOverride
      ?? (activeAtlasBrief?.id === briefId ? atlasRequestPayloadFromBrief(activeAtlasBrief) : buildAtlasRequestPayload() ?? undefined);

    if (!request) {
      return;
    }

    if (!atlasBridgeAvailable) {
      updateStructuredBrief(briefId, (brief) => ({
        ...brief,
        status: "failed",
        errorMessage: "ATLAS execution bridge is unavailable in this host.",
        completedAt: new Date().toISOString(),
      }));
      setAtlasStatus("ATLAS execution is unavailable because the desktop host bridge is not connected.");
      return;
    }

    const requestId = makeId("atlas-request");
    const startedAt = new Date().toISOString();
    const timeoutMs = activeAtlasBrief?.id === briefId ? activeAtlasBrief.timeoutMs ?? ATLAS_REQUEST_TIMEOUT_MS : ATLAS_REQUEST_TIMEOUT_MS;
    updateStructuredBrief(briefId, (brief) => ({
      ...brief,
      providerId: request.providerId,
      modelId: request.modelId,
      taskType: request.taskType,
      platformId: request.platformId,
      objective: request.objective,
      tone: request.tone,
      contentType: request.contentType,
      brief: request.brief,
      requestPacket: request.requestPacket,
      draftId: request.draftId,
      targetSurface: request.targetSurface,
      variantsRequested: request.variantsRequested,
      requestId,
      status: retry ? "retrying" : "running",
      startedAt,
      completedAt: undefined,
      timeoutMs,
      lastDurationMs: undefined,
      errorMessage: "",
      routeSummary: "",
      retryCount: retry ? (brief.retryCount ?? 0) + 1 : brief.retryCount ?? 0,
    }));
    setAtlasWorkflowStage("generate");
    setAtlasStatus(
      retry
        ? `Retrying ${atlasTabConfig.label.toLowerCase()} through the ATLAS runtime for ${previewPlatform}.`
        : `Running ${atlasTabConfig.label.toLowerCase()} through the ATLAS backend for ${previewPlatform}.`,
    );

    try {
      const result = await executeAtlasBrief(
        {
          requestId,
          briefId,
          providerId: request.providerId,
          modelId: request.modelId,
          taskType: request.taskType,
          objective: request.objective,
          brief: request.brief,
          requestPacket: request.requestPacket,
          platformId: request.platformId,
          targetSurface: request.targetSurface,
          variantsRequested: request.variantsRequested,
          draftId: request.draftId,
          draftTitle: draft.title,
          contentType: request.contentType,
          sceneName: activeScene?.name,
          selectedLayerText: selectedLayer?.text?.trim(),
        },
        {
          timeoutMs,
          onStarted: (payload) => {
            updateStructuredBrief(briefId, (brief) => ({
              ...brief,
              requestId: payload.requestId,
              startedAt: payload.startedAt,
              status: "running",
              routeSummary: "runtime-engaged",
            }));
          },
        },
      );

      const groups = reconcileBriefResults(briefId, request, result.responseText, result.providerId, result.modelId);
      const completedAt = result.completedAt;
      updateStructuredBrief(briefId, (brief) => ({
        ...brief,
        providerId: result.providerId,
        modelId: result.modelId,
        requestId: result.requestId,
        routeSummary: result.routeSummary ?? "",
        status: "succeeded",
        completedAt,
        lastDurationMs: atlasBriefDurationMs({ ...brief, startedAt: brief.startedAt ?? startedAt, completedAt }),
        errorMessage: "",
      }));
      setAtlasResponse(result.responseText);
      setAtlasStatus(`ATLAS returned ${flattenResultOptions(groups).length} structured result${flattenResultOptions(groups).length === 1 ? "" : "s"} from ${result.providerId}/${result.modelId}. Ready for review.`);
    } catch (error) {
      const resolvedError = atlasExecutionErrorMessage(error);
      const completedAt = error instanceof AtlasExecutionError && error.completedAt ? error.completedAt : new Date().toISOString();
      updateStructuredBrief(briefId, (brief) => ({
        ...brief,
        requestId,
        status: resolvedError.status,
        errorMessage: resolvedError.message,
        routeSummary: error instanceof AtlasExecutionError ? error.routeSummary ?? brief.routeSummary ?? "" : brief.routeSummary ?? "",
        completedAt,
        lastDurationMs: atlasBriefDurationMs({ ...brief, startedAt: brief.startedAt ?? startedAt, completedAt }),
      }));
      setAtlasStatus(resolvedError.message);
    }
  }

  function queueAtlasRequest(overrides?: Partial<AtlasRequestPayload> & { syncInputs?: boolean; statusMessage?: string }) {
    const request = buildAtlasRequestPayload(overrides);
    if (!request) {
      return;
    }

    if (overrides?.syncInputs !== false) {
      setAtlasProvider(request.providerId);
      setAtlasModel(request.modelId);
      setAtlasVariants(request.variantsRequested);
    }

    const briefId = addAiBrief({
      ...request,
      status: "queued",
      requestId: "",
      routeSummary: "",
      errorMessage: "",
      startedAt: undefined,
      completedAt: undefined,
      retryCount: 0,
      timeoutMs: ATLAS_REQUEST_TIMEOUT_MS,
      lastDurationMs: undefined,
      responseText: "",
      resultGroups: [],
      selectedResultIds: [],
    });
    setActiveAtlasBriefId(briefId);

    mutateDraft((current) => ({
      ...current,
      notes: [current.notes, request.requestPacket].filter(Boolean).join("\n\n"),
    }), { history: false });

    setAtlasStatus(overrides?.statusMessage ?? `Queued a ${atlasTabConfig.label.toLowerCase()} request for ${request.platformId}. Sending it to the ATLAS backend now.`);
    void runAtlasBriefExecution(briefId, false, request);
  }

  function toggleAtlasOptionSelection(optionId: string) {
    if (!activeAtlasBrief) {
      return;
    }

    updateStructuredBrief(activeAtlasBrief.id, (brief) => ({
      ...brief,
      selectedResultIds: brief.selectedResultIds?.includes(optionId)
        ? (brief.selectedResultIds ?? []).filter((id) => id !== optionId)
        : [...(brief.selectedResultIds ?? []), optionId],
    }));
  }

  function updateAtlasOption(optionId: string, updater: (option: AIResultOption) => AIResultOption) {
    if (!activeAtlasBrief) {
      return;
    }

    updateStructuredBrief(activeAtlasBrief.id, (brief) => ({
      ...brief,
      resultGroups: (brief.resultGroups ?? []).map((group) => ({
        ...group,
        options: group.options.map((option) => (option.id === optionId ? updater(option) : option)),
      })),
    }));
  }

  function toggleAtlasOptionExpansion(optionId: string) {
    setAtlasExpandedOptionIds((current) => current.includes(optionId) ? current.filter((id) => id !== optionId) : [...current, optionId]);
  }

  function toggleAtlasOptionCompare(optionId: string) {
    setAtlasCompareOptionIds((current) => {
      if (current.includes(optionId)) {
        return current.filter((id) => id !== optionId);
      }

      if (current.length >= 2) {
        return [current[1], optionId];
      }

      return [...current, optionId];
    });
  }

  function saveAtlasOptionToLibrary(group: AIResultGroup, option: AIResultOption) {
    const fullText = formatResultOption(option);
    addAssets([
      createMediaAsset({
        name: `${option.label.replace(/\s+/g, " ").trim() || "atlas-result"}.atlas.txt`,
        kind: "template",
        mimeType: "text/plain",
        sizeBytes: fullText.length,
        storageMode: "metadata-only",
        folder: "ATLAS Results",
        tags: [group.sourceTaskType, group.providerId, group.modelId, previewPlatform, option.kind],
        source: "ai-generated",
        transcript: [option.label, fullText].filter(Boolean).join("\n\n"),
      }),
    ]);
    setAtlasStatus(`Saved ${option.label} to Media Library as a reusable ATLAS result asset.`);
  }

  function regenerateFromAtlasOption(option: AIResultOption, mode: "similar" | "stronger") {
    const request = buildAtlasRequestPayload({
      objective: mode === "stronger"
        ? `${atlasStructuredRequest.objective.replace(/\s+/g, " ").trim()} · stronger variation`
        : `${atlasStructuredRequest.objective.replace(/\s+/g, " ").trim()} · similar exploration`,
      brief: [
        atlasStructuredRequest.brief.trim(),
        `Reference result: ${option.label}`,
        `Preview: ${option.summary || atlasPreviewText(option, 220)}`,
        `Reference output:\n${formatResultOption(option)}`,
        mode === "stronger"
          ? "Generate a stronger, bolder, more differentiated creative asset. Increase contrast, specificity, and originality while staying on-strategy."
          : "Generate a close sibling of this creative asset. Preserve the core angle, structure, and usefulness, but make it fresh and not a copy.",
      ].filter(Boolean).join("\n\n"),
      variantsRequested: mode === "stronger" ? Math.max(2, atlasVariants) : atlasVariants,
    });

    if (!request) {
      return;
    }

    setAtlasVariants(request.variantsRequested);
    queueAtlasRequest({
      ...request,
      syncInputs: false,
      statusMessage: mode === "stronger"
        ? `Queued a stronger regeneration from ${option.label}.`
        : `Queued a similar regeneration from ${option.label}.`,
    });
  }

  function applyPrimaryAtlasAction(optionId: string) {
    const primaryAction = atlasApplyActions[0];
    if (!primaryAction) {
      setAtlasStatus("No ATLAS apply action is available for this result.");
      return;
    }

    applyStructuredOptions(primaryAction.mode, [optionId]);
  }

  function applyStructuredOptions(mode: AtlasApplyMode, optionIds = atlasSelectedOptionIds) {
    if (!draft || !activeAtlasBrief) {
      return;
    }

    const options = atlasResultOptions.filter((option) => optionIds.includes(option.id));
    if (options.length === 0) {
      return;
    }

    const formattedBlocks = options.map((option) => `${option.label}\n${formatResultOption(option)}`.trim());
    const combinedText = formattedBlocks.join("\n\n").trim();
    const metadataHeader = `${activeAtlasBrief.taskType.toUpperCase()} · ${activeAtlasBrief.providerId}/${activeAtlasBrief.modelId} · ${activeAtlasBrief.platformId}`;
    const notePayload = [metadataHeader, combinedText].join("\n");
    const modeSpecificTexts = options.map((option) => applyTextForOption(option, mode)).filter(Boolean);
    const optionTexts = options.map((option) => formatResultOption(option)).filter(Boolean);
    const primaryText = modeSpecificTexts.join("\n\n").trim();
    const captionTexts = dedupeTexts(options.map((option) => captionBodyForOption(option)).filter(Boolean));
    const hookTexts = hookAlternatesForOptions(options);
    const scenePayloads = buildScenePayloads(options, true);
    const platformSlot = previewPlatform;
    const firstOption = options[0];

    if (mode === "add-to-notes") {
      mutateDraft((current) => ({
        ...current,
        notes: [current.notes, notePayload].filter(Boolean).join("\n\n"),
      }));
      setAtlasStatus(`Saved ${options.length} structured result${options.length === 1 ? "" : "s"} into draft notes.`);
      return;
    }

    if (mode === "send-to-voice") {
      const narrationSections = dedupeTexts(scenePayloads.map((payload) => payload.narration || payload.text).filter(Boolean));
      const scriptText = narrationSections.join("\n\n") || primaryText || combinedText;
      const existingProject = voiceProjects.find((project) => project.linkedDraftId === draft.id && project.linkedSceneId === activeScene?.id);

      if (existingProject) {
        updateVoiceProject(existingProject.id, (project) => ({
          ...project,
          title: firstOption?.label || project.title,
          script: scriptText,
          narrationSections,
          timelineSceneIds: dedupeTexts([...(project.timelineSceneIds ?? []), ...(activeScene?.id ? [activeScene.id] : [])]),
          linkedDraftId: draft.id,
          linkedSceneId: activeScene?.id,
          versions: dedupeTexts([...(project.versions ?? []), scriptText]),
          status: "ready-to-generate",
        }));
      } else {
        const projectId = addVoiceProject({
          title: firstOption?.label || `${draft.title} Voice` ,
          script: scriptText,
          providerId: "elevenlabs",
          modelId: "eleven_multilingual_v2",
          voiceName: atlasBriefing.voiceover.voiceStyle,
          stylePrompt: atlasBriefing.voiceover.narrationStyle,
          emotion: atlasBriefing.voiceover.emotion,
          speed: 1,
          pacing: 1,
          linkedDraftId: draft.id,
          linkedSceneId: activeScene?.id,
          selectedAssetId: undefined,
          narrationSections,
          timelineSceneIds: activeScene?.id ? [activeScene.id] : [],
          versions: [scriptText],
          status: "ready-to-generate",
        });

        mutateDraft((current) => ({
          ...current,
          linkedVoiceProjectIds: dedupeTexts([...(current.linkedVoiceProjectIds ?? []), projectId]),
          scenes: (current.scenes ?? []).map((scene) =>
            scene.id === activeScene?.id
              ? { ...scene, narration: scriptText, sceneType: scene.sceneType === "default" ? "voiceover" : scene.sceneType }
              : scene,
          ),
          notes: [current.notes, `VOICEOVER\n${scriptText}`].filter(Boolean).join("\n\n"),
        }));
      }

      setAtlasStatus("Hydrated a real voice project and linked it to the active draft.");
      navigate("/voice-studio");
      return;
    }

    if (mode === "replace-selection") {
      if (!isTextEditableLayer(selectedLayer)) {
        setAtlasStatus("Select a text or sticker layer before replacing canvas copy with an ATLAS result.");
        return;
      }

      updateLayer(selectedLayer.id, (layer) => ({
        ...layer,
        text: primaryText || combinedText,
      }));
      setAtlasStatus(`Replaced the selected text layer with ${options.length} ATLAS result${options.length === 1 ? "" : "s"}.`);
      return;
    }

    if (mode === "set-primary-caption") {
      const nextCaption = captionTexts.join("\n\n") || primaryText || combinedText;
      mutateDraft((current) => ({
        ...current,
        caption: nextCaption,
        platformCaptions: { ...(current.platformCaptions ?? {}), [platformSlot]: nextCaption },
        platformVersions: {
          ...(current.platformVersions ?? {}),
          [platformSlot]: {
            platformId: platformSlot,
            primaryCaption: nextCaption,
            captionAlternates: current.platformVersions?.[platformSlot]?.captionAlternates ?? [],
            hook: current.platformVersions?.[platformSlot]?.hook ?? current.primaryHook,
            adaptation: current.platformVersions?.[platformSlot]?.adaptation ?? current.platformAdaptations?.[platformSlot] ?? "",
            preservedOriginal: current.platformVersions?.[platformSlot]?.preservedOriginal,
            lastComparedCandidate: current.platformVersions?.[platformSlot]?.lastComparedCandidate,
          },
        },
      }));
      setAtlasStatus(`Set the primary caption and synced the ${platformSlot} version.`);
      return;
    }

    if (mode === "append-alternate-caption") {
      mutateDraft((current) => ({
        ...current,
        captionVariants: dedupeTexts([...(current.captionVariants ?? []), ...captionTexts]),
        alternatePostVersions: dedupeTexts([...(current.alternatePostVersions ?? []), ...captionTexts]),
        platformVersions: {
          ...(current.platformVersions ?? {}),
          [platformSlot]: {
            platformId: platformSlot,
            primaryCaption: current.platformVersions?.[platformSlot]?.primaryCaption ?? current.platformCaptions?.[platformSlot] ?? current.caption,
            captionAlternates: dedupeTexts([...(current.platformVersions?.[platformSlot]?.captionAlternates ?? []), ...captionTexts]),
            hook: current.platformVersions?.[platformSlot]?.hook ?? current.primaryHook,
            adaptation: current.platformVersions?.[platformSlot]?.adaptation ?? current.platformAdaptations?.[platformSlot] ?? "",
            preservedOriginal: current.platformVersions?.[platformSlot]?.preservedOriginal,
            lastComparedCandidate: current.platformVersions?.[platformSlot]?.lastComparedCandidate,
          },
        },
      }));
      setAtlasStatus(`Saved alternate caption options into the draft and ${platformSlot} version slot.`);
      return;
    }

    if (mode === "save-caption-variant-set") {
      mutateDraft((current) => ({
        ...current,
        captionVariants: dedupeTexts([...(current.captionVariants ?? []), ...captionTexts]),
        captionVariantSets: [
          {
            id: makeId("caption-set"),
            label: `${firstOption?.label || activeAtlasBrief.objective} Variant Set`,
            captions: captionTexts,
            platformId: platformSlot,
            createdAt: new Date().toISOString(),
          },
          ...(current.captionVariantSets ?? []),
        ],
      }));
      setAtlasStatus("Stored a real caption variant set on the draft.");
      return;
    }

    if (mode === "assign-platform-caption") {
      const nextCaption = captionTexts.join("\n\n") || primaryText || combinedText;
      mutateDraft((current) => ({
        ...current,
        platformCaptions: { ...(current.platformCaptions ?? {}), [platformSlot]: nextCaption },
        platformVersions: {
          ...(current.platformVersions ?? {}),
          [platformSlot]: {
            platformId: platformSlot,
            primaryCaption: nextCaption,
            captionAlternates: current.platformVersions?.[platformSlot]?.captionAlternates ?? [],
            hook: current.platformVersions?.[platformSlot]?.hook ?? current.primaryHook,
            adaptation: current.platformVersions?.[platformSlot]?.adaptation ?? current.platformAdaptations?.[platformSlot] ?? "",
            preservedOriginal: current.platformVersions?.[platformSlot]?.preservedOriginal,
            lastComparedCandidate: current.platformVersions?.[platformSlot]?.lastComparedCandidate,
          },
        },
      }));
      setAtlasStatus(`Assigned the caption output to the ${platformSlot} platform slot.`);
      return;
    }

    if (mode === "set-active-hook") {
      const hookText = hookPrimaryForOption(firstOption) || primaryText || combinedText;
      mutateDraft((current) => ({
        ...current,
        primaryHook: hookText,
        scenes: (current.scenes ?? []).map((scene) =>
          scene.id === activeScene?.id
            ? { ...scene, hook: hookText, headline: scene.headline || hookText }
            : scene,
        ),
        platformVersions: {
          ...(current.platformVersions ?? {}),
          [platformSlot]: {
            platformId: platformSlot,
            primaryCaption: current.platformVersions?.[platformSlot]?.primaryCaption ?? current.platformCaptions?.[platformSlot] ?? current.caption,
            captionAlternates: current.platformVersions?.[platformSlot]?.captionAlternates ?? [],
            hook: hookText,
            adaptation: current.platformVersions?.[platformSlot]?.adaptation ?? current.platformAdaptations?.[platformSlot] ?? "",
            preservedOriginal: current.platformVersions?.[platformSlot]?.preservedOriginal,
            lastComparedCandidate: current.platformVersions?.[platformSlot]?.lastComparedCandidate,
          },
        },
      }));

      if (isTextEditableLayer(selectedLayer)) {
        updateLayer(selectedLayer.id, (layer) => ({
          ...layer,
          text: hookText,
        }), { history: false });
      }

      setAtlasStatus("Set the active hook and bound it to the draft state.");
      return;
    }

    if (mode === "save-alternate-hooks") {
      mutateDraft((current) => ({
        ...current,
        alternateHooks: dedupeTexts([...(current.alternateHooks ?? []), ...hookTexts]),
      }));
      setAtlasStatus("Saved alternate hooks on the draft.");
      return;
    }

    if (mode === "bind-hook-to-scene") {
      const hookText = hookPrimaryForOption(firstOption) || primaryText || combinedText;
      mutateDraft((current) => ({
        ...current,
        scenes: (current.scenes ?? []).map((scene) =>
          scene.id === activeScene?.id
            ? { ...scene, hook: hookText, headline: hookText }
            : scene,
        ),
      }));

      if (isTextEditableLayer(selectedLayer)) {
        updateLayer(selectedLayer.id, (layer) => ({ ...layer, text: hookText }), { history: false });
      }

      setAtlasStatus("Bound the hook to the current scene headline structure.");
      return;
    }

    if (mode === "set-platform-adaptation") {
      const nextAdaptation = primaryText || combinedText;
      mutateDraft((current) => ({
        ...current,
        platformAdaptations: {
          ...(current.platformAdaptations ?? {}),
          [platformSlot]: nextAdaptation,
        },
        platformVersions: {
          ...(current.platformVersions ?? {}),
          [platformSlot]: {
            platformId: platformSlot,
            primaryCaption: current.platformVersions?.[platformSlot]?.primaryCaption ?? current.platformCaptions?.[platformSlot] ?? current.caption,
            captionAlternates: current.platformVersions?.[platformSlot]?.captionAlternates ?? [],
            hook: current.platformVersions?.[platformSlot]?.hook ?? current.primaryHook,
            adaptation: nextAdaptation,
            preservedOriginal: current.platformVersions?.[platformSlot]?.preservedOriginal ?? current.platformAdaptations?.[platformSlot] ?? "",
            lastComparedCandidate: nextAdaptation,
          },
        },
      }));

      const nextPreset = choosePresetForPlatform(platformSlot, draft);
      applyPreset(nextPreset.id);
      setAtlasStatus(`Saved the ${platformSlot} adaptation while preserving the original version.`);
      return;
    }

    if (mode === "compare-platform-adaptation") {
      const candidate = primaryText || combinedText;
      mutateDraft((current) => ({
        ...current,
        platformVersions: {
          ...(current.platformVersions ?? {}),
          [platformSlot]: {
            platformId: platformSlot,
            primaryCaption: current.platformVersions?.[platformSlot]?.primaryCaption ?? current.platformCaptions?.[platformSlot] ?? current.caption,
            captionAlternates: current.platformVersions?.[platformSlot]?.captionAlternates ?? [],
            hook: current.platformVersions?.[platformSlot]?.hook ?? current.primaryHook,
            adaptation: current.platformVersions?.[platformSlot]?.adaptation ?? current.platformAdaptations?.[platformSlot] ?? "",
            preservedOriginal: current.platformVersions?.[platformSlot]?.preservedOriginal ?? current.platformAdaptations?.[platformSlot] ?? "",
            lastComparedCandidate: candidate,
          },
        },
        notes: [current.notes, `ADAPTATION COMPARE · ${platformSlot}\nCurrent: ${current.platformVersions?.[platformSlot]?.adaptation || current.platformAdaptations?.[platformSlot] || "none"}\nCandidate: ${candidate}`].filter(Boolean).join("\n\n"),
      }));
      setAtlasStatus(`Stored a comparison snapshot for the ${platformSlot} platform version.`);
      return;
    }

    if (mode === "create-variation-draft") {
      const variationDraft = buildVariationDraftFromOptions(draft, options, activeAtlasBrief.taskType, previewPlatform);
      saveDraft(variationDraft);
      selectDraft(variationDraft.id);
      setWorkingDraft(variationDraft);
      setSelectedLayerId(undefined);
      setSelectedPresetId(variationDraft.formatId);
      setHistory({ past: [], future: [] });
      setAtlasStatus(`Created a new variation draft from ${options.length} selected ATLAS result${options.length === 1 ? "" : "s"}.`);
      return;
    }

    if (mode === "insert-script-scene" || mode === "create-scene-blocks" || mode === "generate-slide-sequence" || mode === "create-alternate-scene-sequence") {
      mutateDraft((current) => appendScenePayloadSequence(normalizeDraft(current), scenePayloads, {
        prefix: mode === "create-alternate-scene-sequence" ? "Alt" : undefined,
      }));
      setAtlasStatus(mode === "generate-slide-sequence" ? "Generated a real slide sequence in the draft." : mode === "create-alternate-scene-sequence" ? "Created an alternate scene sequence on the draft." : "Built structured scene blocks directly into the draft.");
      return;
    }

    if (mode === "assign-narration-sections") {
      mutateDraft((current) => assignPayloadsToExistingScenes(normalizeDraft(current), scenePayloads));
      setAtlasStatus("Assigned narration sections into the existing scene sequence.");
      return;
    }

    if (mode === "save-concept-board" || mode === "create-visual-direction") {
      const directions = options.map((option) => buildVisualDirectionFromOption(option, activeScene?.id));
      mutateDraft((current) => ({
        ...current,
        conceptBoard: [...directions, ...(current.conceptBoard ?? [])],
      }));
      setAtlasStatus(mode === "save-concept-board" ? "Saved the concept card to the project concept board." : "Created a new visual direction entry on the draft.");
      return;
    }

    if (mode === "apply-visual-direction") {
      const direction = buildVisualDirectionFromOption(firstOption, activeScene?.id);
      mutateDraft((current) => ({
        ...current,
        background: direction.palette[0] || current.background,
        conceptBoard: [direction, ...(current.conceptBoard ?? [])],
        notes: [current.notes, `VISUAL DIRECTION\n${direction.title}\nMood: ${direction.mood}\nComposition: ${direction.composition}`].filter(Boolean).join("\n\n"),
        scenes: (current.scenes ?? []).map((scene) =>
          scene.id === activeScene?.id
            ? {
              ...scene,
              visualDirectionId: direction.id,
              sceneType: "visual-direction",
              background: direction.palette[0] || scene.background,
            }
            : scene,
        ),
      }));
      setAtlasStatus("Applied palette, mood, and layout hints to the active draft.");
      return;
    }

    if (mode === "store-variant-pack") {
      const packs = options.map((option) => buildVariantPackFromOption(option));
      mutateDraft((current) => ({
        ...current,
        variantPacks: [...packs, ...(current.variantPacks ?? [])],
      }));
      setAtlasStatus("Stored the structured variant pack on the draft.");
      return;
    }
  }

  function loadDraftBrief(briefId: string) {
    const brief = atlasDraftBriefs.find((item) => item.id === briefId);
    if (!brief) {
      return;
    }

    const nextTab = ATLAS_TABS.find((tab) => tab.taskType === brief.taskType);
    if (nextTab) {
      setAtlasTab(nextTab.id);
    }
    setActiveAtlasBriefId(brief.id);
    setAtlasProvider(brief.providerId);
    setAtlasModel(brief.modelId);
    setPreviewPlatform(brief.platformId);
    setAtlasVariants(brief.variantsRequested);
    hydrateStructuredBriefingFromStoredBrief(brief);
    setAtlasResponse(brief.responseText ?? "");
    setAtlasWorkflowStage(
      brief.status === "queued" || brief.status === "retrying" || brief.status === "running" || brief.status === "failed" || brief.status === "timed-out" || brief.status === "cancelled"
        ? "generate"
        : flattenResultOptions(brief.resultGroups ?? []).length > 0
          ? "review"
          : "brief",
    );
    setAtlasStatus("Loaded a stored draft-scoped ATLAS workflow with its structured results.");
  }

  function handleUndo() {
    if (!draft || history.past.length === 0) {
      return;
    }

    const previous = history.past[history.past.length - 1];
    setHistory((current) => ({
      past: current.past.slice(0, -1),
      future: [draft, ...current.future],
    }));
    setWorkingDraft(previous);
    updateDraft(previous.id, () => previous);
  }

  function handleRedo() {
    if (!draft || history.future.length === 0) {
      return;
    }

    const next = history.future[0];
    setHistory((current) => ({
      past: [...current.past, draft],
      future: current.future.slice(1),
    }));
    setWorkingDraft(next);
    updateDraft(next.id, () => next);
  }

  function startDrag(layer: DraftLayer, event: React.PointerEvent<HTMLDivElement>) {
    if (!draft || layer.locked) {
      return;
    }

    dragRef.current = {
      layerId: layer.id,
      pointerId: event.pointerId,
      startClientX: event.clientX,
      startClientY: event.clientY,
      startX: layer.x,
      startY: layer.y,
    };

    (event.currentTarget as HTMLDivElement).setPointerCapture(event.pointerId);
    setSelectedLayerId(layer.id);
  }

  function applySnap(layer: DraftLayer, rawX: number, rawY: number) {
    if (!draft) {
      return { x: rawX, y: rawY, guides: [] as CanvasGuide[] };
    }

    const snapThreshold = 18;
    const guides: CanvasGuide[] = [];
    const layerCenterX = rawX + layer.width / 2;
    const layerCenterY = rawY + layer.height / 2;
    let nextX = rawX;
    let nextY = rawY;

    const verticalTargets = [0, draft.width / 2, draft.width - layer.width];
    const horizontalTargets = [0, draft.height / 2, draft.height - layer.height];

    verticalTargets.forEach((target) => {
      const compare = target === draft.width / 2 ? layerCenterX : rawX;
      const desired = target === draft.width / 2 ? draft.width / 2 : target;
      if (Math.abs(compare - desired) <= snapThreshold) {
        nextX = target === draft.width / 2 ? Math.round(draft.width / 2 - layer.width / 2) : Math.round(target);
        guides.push({ orientation: "vertical", position: target === draft.width / 2 ? draft.width / 2 : target === 0 ? 0 : draft.width });
      }
    });

    horizontalTargets.forEach((target) => {
      const compare = target === draft.height / 2 ? layerCenterY : rawY;
      const desired = target === draft.height / 2 ? draft.height / 2 : target;
      if (Math.abs(compare - desired) <= snapThreshold) {
        nextY = target === draft.height / 2 ? Math.round(draft.height / 2 - layer.height / 2) : Math.round(target);
        guides.push({ orientation: "horizontal", position: target === draft.height / 2 ? draft.height / 2 : target === 0 ? 0 : draft.height });
      }
    });

    return {
      x: clamp(nextX, 0, Math.max(0, draft.width - layer.width)),
      y: clamp(nextY, 0, Math.max(0, draft.height - layer.height)),
      guides,
    };
  }

  function handleCanvasPointerMove(event: React.PointerEvent<HTMLDivElement>) {
    if (!draft || !dragRef.current) {
      return;
    }

    const dragging = dragRef.current;
    const layer = draft.layers.find((item) => item.id === dragging.layerId);
    if (!layer) {
      return;
    }

    const deltaX = Math.round((event.clientX - dragging.startClientX) / scale);
    const deltaY = Math.round((event.clientY - dragging.startClientY) / scale);
    const snapped = applySnap(layer, dragging.startX + deltaX, dragging.startY + deltaY);
    setCanvasGuides(snapped.guides);
    mutateDraft(
      (current) => ({
        ...current,
        layers: current.layers.map((item) =>
          item.id === layer.id ? { ...item, x: snapped.x, y: snapped.y } : item,
        ),
      }),
      { history: false },
    );
  }

  function stopDrag() {
    if (!dragRef.current) {
      return;
    }

    dragRef.current = null;
    setCanvasGuides([]);
    if (draft) {
      setHistory((current) => ({ past: [...current.past, draft], future: [] }));
    }
  }

  useEffect(() => {
    function handleWindowPointerUp() {
      stopDrag();
    }

    window.addEventListener("pointerup", handleWindowPointerUp);
    return () => window.removeEventListener("pointerup", handleWindowPointerUp);
  });

  const tools = [
    { id: "select", label: "Select", icon: Move },
    { id: "text", label: "Text", icon: Type },
    { id: "image", label: "Image", icon: ImageIcon },
    { id: "video", label: "Video", icon: Video },
    { id: "audio", label: "Audio", icon: Music },
    { id: "sticker", label: "Sticker", icon: Sparkles },
    { id: "shape", label: "Shape", icon: Square },
    { id: "gradient", label: "Gradient", icon: Palette },
    { id: "overlay", label: "Overlay", icon: Layers3 },
    { id: "motion-text", label: "Motion", icon: Wand2 },
  ] as const;

  function handleToolAction(tool: StudioTool) {
    setActiveTool(tool);
    setActiveFlyout("tools");

    switch (tool) {
      case "text":
        createLayer("text");
        break;
      case "image":
        fileInputRef.current?.click();
        break;
      case "video":
        createLayer("video", { width: 420, height: 240 });
        break;
      case "audio":
        createLayer("audio", { width: 360, height: 72 });
        break;
      case "sticker":
        createLayer("sticker", { text: "SALE", width: 180, height: 84, color: "#f97316", cornerRadius: 999 });
        break;
      case "shape":
        createLayer("shape", { width: 260, height: 260, color: "#38bdf8" });
        break;
      case "gradient":
        if (activeScene) {
          const index = BACKGROUND_SWATCHES.indexOf(activeScene.background);
          updateScene(activeScene.id, (scene) => ({
            ...scene,
            background: BACKGROUND_SWATCHES[(index + 1 + BACKGROUND_SWATCHES.length) % BACKGROUND_SWATCHES.length],
          }));
        }
        break;
      case "overlay":
        createLayer("overlay", { width: ensureDraftExists().width, height: ensureDraftExists().height, x: 0, y: 0, locked: false });
        break;
      case "motion-text":
        createLayer("text", { text: "Animated headline", animation: "type-on", fontSize: 64 });
        break;
      default:
        break;
    }
  }

  function renderAtlasBriefingUi() {
    switch (atlasTab) {
      case "ideas":
        return (
          <div className="grid gap-4 md:grid-cols-2">
            <AtlasTextField label="Campaign goal" value={atlasBriefing.ideas.campaignGoal} onChange={(value) => updateAtlasBriefingSection("ideas", { campaignGoal: value })} />
            <AtlasTextField label="Target audience" value={atlasBriefing.ideas.targetAudience} onChange={(value) => updateAtlasBriefingSection("ideas", { targetAudience: value })} />
            <AtlasTextField label="Offer" value={atlasBriefing.ideas.offer} onChange={(value) => updateAtlasBriefingSection("ideas", { offer: value })} />
            <AtlasTextField label="Content pillar" value={atlasBriefing.ideas.contentPillar} onChange={(value) => updateAtlasBriefingSection("ideas", { contentPillar: value })} />
            <div className="md:col-span-2">
              <AtlasTextField label="Desired output" value={atlasBriefing.ideas.desiredOutput} onChange={(value) => updateAtlasBriefingSection("ideas", { desiredOutput: value })} />
            </div>
          </div>
        );
      case "captions":
        return (
          <div className="space-y-4">
            <div className="grid gap-4 md:grid-cols-2">
              <AtlasSelectField label="Platform" value={atlasBriefing.caption.platform} options={atlasPlatformOptions} onChange={(value) => updateAtlasBriefingSection("caption", { platform: value as PlatformId })} />
              <AtlasTextField label="Objective" value={atlasBriefing.caption.objective} onChange={(value) => updateAtlasBriefingSection("caption", { objective: value })} />
            </div>
            <AtlasChoiceField label="Tone" value={atlasBriefing.caption.tone} options={CAPTION_TONE_OPTIONS} onChange={(value) => updateAtlasBriefingSection("caption", { tone: value })} />
            <div className="grid gap-4 md:grid-cols-2">
              <AtlasChoiceField label="Length" value={atlasBriefing.caption.length} options={CAPTION_LENGTH_OPTIONS} onChange={(value) => updateAtlasBriefingSection("caption", { length: value })} />
              <AtlasChoiceField label="CTA style" value={atlasBriefing.caption.ctaStyle} options={CTA_STYLE_OPTIONS} onChange={(value) => updateAtlasBriefingSection("caption", { ctaStyle: value })} />
            </div>
            <div className="grid gap-4 md:grid-cols-2">
              <AtlasTextField label="Audience" value={atlasBriefing.caption.audience} onChange={(value) => updateAtlasBriefingSection("caption", { audience: value })} />
              <AtlasTextField label="Brand voice" value={atlasBriefing.caption.brandVoice} onChange={(value) => updateAtlasBriefingSection("caption", { brandVoice: value })} />
            </div>
            <AtlasTextField label="Forbidden phrases" value={atlasBriefing.caption.forbiddenPhrases} placeholder="Comma-separated phrases to avoid" onChange={(value) => updateAtlasBriefingSection("caption", { forbiddenPhrases: value })} />
          </div>
        );
      case "hooks":
        return (
          <div className="space-y-4">
            <AtlasChoiceField label="Style" value={atlasBriefing.hook.style} options={HOOK_STYLE_OPTIONS} onChange={(value) => updateAtlasBriefingSection("hook", { style: value })} />
            <div className="grid gap-4 md:grid-cols-2">
              <AtlasChoiceField label="Energy level" value={atlasBriefing.hook.energyLevel} options={ENERGY_LEVEL_OPTIONS} onChange={(value) => updateAtlasBriefingSection("hook", { energyLevel: value })} />
              <AtlasChoiceField label="Length" value={atlasBriefing.hook.length} options={HOOK_LENGTH_OPTIONS} onChange={(value) => updateAtlasBriefingSection("hook", { length: value })} />
            </div>
            <div className="grid gap-4 md:grid-cols-2">
              <AtlasTextField label="Target audience" value={atlasBriefing.hook.targetAudience} onChange={(value) => updateAtlasBriefingSection("hook", { targetAudience: value })} />
              <AtlasTextField label="Content angle" value={atlasBriefing.hook.contentAngle} onChange={(value) => updateAtlasBriefingSection("hook", { contentAngle: value })} />
            </div>
            <AtlasChoiceField label="Urgency level" value={atlasBriefing.hook.urgencyLevel} options={URGENCY_LEVEL_OPTIONS} onChange={(value) => updateAtlasBriefingSection("hook", { urgencyLevel: value })} />
          </div>
        );
      case "scripts":
        return (
          <div className="space-y-4">
            <div className="grid gap-4 md:grid-cols-2">
              <AtlasChoiceField label="Format" value={atlasBriefing.script.format} options={SCRIPT_FORMAT_OPTIONS} onChange={(value) => updateAtlasBriefingSection("script", { format: value })} />
              <AtlasChoiceField label="Duration target" value={atlasBriefing.script.durationTarget} options={SCRIPT_DURATION_OPTIONS} onChange={(value) => updateAtlasBriefingSection("script", { durationTarget: value })} />
            </div>
            <div className="grid gap-4 md:grid-cols-2">
              <AtlasChoiceField label="Tone" value={atlasBriefing.script.tone} options={CAPTION_TONE_OPTIONS} onChange={(value) => updateAtlasBriefingSection("script", { tone: value })} />
              <AtlasTextField label="CTA goal" value={atlasBriefing.script.ctaGoal} onChange={(value) => updateAtlasBriefingSection("script", { ctaGoal: value })} />
            </div>
            <div className="grid gap-4 md:grid-cols-2">
              <AtlasChoiceField label="Speaking style" value={atlasBriefing.script.speakingStyle} options={SPEAKING_STYLE_OPTIONS} onChange={(value) => updateAtlasBriefingSection("script", { speakingStyle: value })} />
              <AtlasChoiceField label="Scene count" value={atlasBriefing.script.sceneCount} options={SCENE_COUNT_OPTIONS} onChange={(value) => updateAtlasBriefingSection("script", { sceneCount: value })} />
            </div>
            <AtlasTextField label="Audience" value={atlasBriefing.script.audience} onChange={(value) => updateAtlasBriefingSection("script", { audience: value })} />
          </div>
        );
      case "carousels":
        return (
          <div className="space-y-4">
            <div className="grid gap-4 md:grid-cols-2">
              <AtlasChoiceField label="Slide count" value={atlasBriefing.carousel.slideCount} options={CAROUSEL_SLIDE_OPTIONS} onChange={(value) => updateAtlasBriefingSection("carousel", { slideCount: value })} />
              <AtlasChoiceField label="CTA slide" value={atlasBriefing.carousel.ctaSlide} options={CTA_SLIDE_OPTIONS} onChange={(value) => updateAtlasBriefingSection("carousel", { ctaSlide: value })} />
            </div>
            <AtlasChoiceField label="Structure type" value={atlasBriefing.carousel.structureType} options={CAROUSEL_STRUCTURE_OPTIONS} onChange={(value) => updateAtlasBriefingSection("carousel", { structureType: value })} />
            <div className="grid gap-4 md:grid-cols-2">
              <AtlasTextField label="Teaching angle" value={atlasBriefing.carousel.teachingAngle} onChange={(value) => updateAtlasBriefingSection("carousel", { teachingAngle: value })} />
              <AtlasTextField label="Target audience" value={atlasBriefing.carousel.targetAudience} onChange={(value) => updateAtlasBriefingSection("carousel", { targetAudience: value })} />
            </div>
            <AtlasChoiceField label="Tone" value={atlasBriefing.carousel.tone} options={CAPTION_TONE_OPTIONS} onChange={(value) => updateAtlasBriefingSection("carousel", { tone: value })} />
          </div>
        );
      case "visuals":
        return (
          <div className="space-y-4">
            <div className="grid gap-4 md:grid-cols-2">
              <AtlasSelectField label="Platform" value={atlasBriefing.visual.platform} options={atlasPlatformOptions} onChange={(value) => updateAtlasBriefingSection("visual", { platform: value as PlatformId })} />
              <AtlasTextField label="Product focus" value={atlasBriefing.visual.productFocus} onChange={(value) => updateAtlasBriefingSection("visual", { productFocus: value })} />
            </div>
            <AtlasChoiceField label="Style" value={atlasBriefing.visual.style} options={VISUAL_STYLE_OPTIONS} onChange={(value) => updateAtlasBriefingSection("visual", { style: value })} />
            <div className="grid gap-4 md:grid-cols-2">
              <AtlasChoiceField label="Mood" value={atlasBriefing.visual.mood} options={VISUAL_MOOD_OPTIONS} onChange={(value) => updateAtlasBriefingSection("visual", { mood: value })} />
              <AtlasChoiceField label="Color direction" value={atlasBriefing.visual.colorDirection} options={COLOR_DIRECTION_OPTIONS} onChange={(value) => updateAtlasBriefingSection("visual", { colorDirection: value })} />
            </div>
            <div className="grid gap-4 md:grid-cols-2">
              <AtlasChoiceField label="Composition" value={atlasBriefing.visual.compositionType} options={COMPOSITION_OPTIONS} onChange={(value) => updateAtlasBriefingSection("visual", { compositionType: value })} />
              <AtlasChoiceField label="Shot vibe" value={atlasBriefing.visual.shotVibe} options={SHOT_VIBE_OPTIONS} onChange={(value) => updateAtlasBriefingSection("visual", { shotVibe: value })} />
            </div>
          </div>
        );
      case "adaptation":
        return (
          <div className="space-y-4">
            <div className="grid gap-4 md:grid-cols-2">
              <AtlasSelectField label="Source platform" value={atlasBriefing.adaptation.sourcePlatform} options={atlasPlatformOptions} onChange={(value) => updateAtlasBriefingSection("adaptation", { sourcePlatform: value as PlatformId })} />
              <AtlasSelectField label="Target platform" value={atlasBriefing.adaptation.targetPlatform} options={atlasPlatformOptions} onChange={(value) => updateAtlasBriefingSection("adaptation", { targetPlatform: value as PlatformId })} />
            </div>
            <AtlasToggleField label="Preserve tone" description="Keep the current brand voice and emotional feel as much as possible." value={atlasBriefing.adaptation.preserveTone} onChange={(value) => updateAtlasBriefingSection("adaptation", { preserveTone: value })} />
            <div className="grid gap-4 md:grid-cols-2">
              <AtlasChoiceField label="Length mode" value={atlasBriefing.adaptation.lengthMode} options={ADAPTATION_LENGTH_OPTIONS} onChange={(value) => updateAtlasBriefingSection("adaptation", { lengthMode: value as "preserve" | "shorten" | "expand" })} />
              <AtlasTextField label="CTA preference" value={atlasBriefing.adaptation.ctaPreference} onChange={(value) => updateAtlasBriefingSection("adaptation", { ctaPreference: value })} />
            </div>
          </div>
        );
      case "variants":
        return (
          <div className="space-y-4">
            <AtlasTextField label="Base concept" value={atlasBriefing.variant.baseConcept} onChange={(value) => updateAtlasBriefingSection("variant", { baseConcept: value })} />
            <div className="grid gap-4 md:grid-cols-2">
              <AtlasChoiceField label="Variation focus" value={atlasBriefing.variant.variationFocus} options={VARIATION_FOCUS_OPTIONS} onChange={(value) => updateAtlasBriefingSection("variant", { variationFocus: value })} />
              <AtlasTextField label="Tone direction" value={atlasBriefing.variant.toneDirection} onChange={(value) => updateAtlasBriefingSection("variant", { toneDirection: value })} />
            </div>
            <div className="grid gap-4 md:grid-cols-2">
              <AtlasTextField label="Audience emphasis" value={atlasBriefing.variant.audienceEmphasis} onChange={(value) => updateAtlasBriefingSection("variant", { audienceEmphasis: value })} />
              <AtlasTextField label="CTA direction" value={atlasBriefing.variant.ctaDirection} onChange={(value) => updateAtlasBriefingSection("variant", { ctaDirection: value })} />
            </div>
          </div>
        );
      case "voiceovers":
        return (
          <div className="space-y-4">
            <div className="grid gap-4 md:grid-cols-2">
              <AtlasChoiceField label="Voice style" value={atlasBriefing.voiceover.voiceStyle} options={VOICE_STYLE_OPTIONS} onChange={(value) => updateAtlasBriefingSection("voiceover", { voiceStyle: value })} />
              <AtlasChoiceField label="Pacing" value={atlasBriefing.voiceover.pacing} options={PACING_OPTIONS} onChange={(value) => updateAtlasBriefingSection("voiceover", { pacing: value })} />
            </div>
            <div className="grid gap-4 md:grid-cols-2">
              <AtlasChoiceField label="Emotion" value={atlasBriefing.voiceover.emotion} options={EMOTION_OPTIONS} onChange={(value) => updateAtlasBriefingSection("voiceover", { emotion: value })} />
              <AtlasChoiceField label="Length" value={atlasBriefing.voiceover.length} options={VOICEOVER_LENGTH_OPTIONS} onChange={(value) => updateAtlasBriefingSection("voiceover", { length: value })} />
            </div>
            <div className="grid gap-4 md:grid-cols-2">
              <AtlasChoiceField label="Narration style" value={atlasBriefing.voiceover.narrationStyle} options={NARRATION_STYLE_OPTIONS} onChange={(value) => updateAtlasBriefingSection("voiceover", { narrationStyle: value })} />
              <AtlasChoiceField label="Target format" value={atlasBriefing.voiceover.targetFormat} options={TARGET_FORMAT_OPTIONS} onChange={(value) => updateAtlasBriefingSection("voiceover", { targetFormat: value })} />
            </div>
          </div>
        );
      default:
        return null;
    }
  }

  function openAtlasApplyStage(optionIds?: string[]) {
    if (activeAtlasBrief && optionIds && optionIds.length > 0) {
      updateStructuredBrief(activeAtlasBrief.id, (brief) => ({
        ...brief,
        selectedResultIds: optionIds,
      }));
    }

    setAtlasWorkflowStage("apply");
  }

  function stageToneForAtlasStep(stage: AtlasWorkflowStage) {
    if (stage === atlasWorkflowStage) {
      return "border-cyan-400/30 bg-cyan-500/12 text-cyan-100";
    }

    if (stage === "review" && atlasResultOptions.length > 0) {
      return "border-emerald-400/20 bg-emerald-500/10 text-emerald-100";
    }

    if (stage === "apply" && atlasSelectedOptionIds.length > 0) {
      return "border-blue-400/20 bg-blue-500/10 text-blue-100";
    }

    return "border-white/10 bg-white/[0.03] text-gray-300 hover:bg-white/[0.06]";
  }

  const railButtons: Array<{ id: FlyoutPanel; label: string; icon: typeof Layers3 }> = [
    { id: "tools", label: "Tools", icon: Layers3 },
    { id: "assets", label: "Assets", icon: FolderOpen },
    { id: "scenes", label: "Scenes", icon: Rows3 },
    { id: "atlas", label: "ATLAS", icon: BrainCircuit },
  ];

  function renderFlyout() {
    if (activeFlyout === "closed") {
      return null;
    }

    if (activeFlyout === "tools") {
      return (
        <aside className="min-h-0 border-r border-white/10 bg-[#0d1017] flex flex-col">
          <div className="border-b border-white/10 px-5 py-4">
            <div className="text-sm font-medium text-white">Editor Toolkit</div>
            <div className="mt-1 text-xs text-gray-400">Add and arrange visual elements without crowding the canvas.</div>
          </div>
          <div className="min-h-0 flex-1 overflow-y-auto px-5 py-5 space-y-5">
            <div className="grid grid-cols-2 gap-3">
              {tools.map((tool) => {
                const Icon = tool.icon;
                return (
                  <button
                    key={tool.id}
                    onClick={() => handleToolAction(tool.id)}
                    className={`rounded-2xl border p-4 text-left transition ${activeTool === tool.id ? "border-cyan-400/30 bg-cyan-500/10" : "border-white/10 bg-white/[0.03] hover:bg-white/[0.06]"}`}
                  >
                    <Icon className="h-5 w-5 text-cyan-200" />
                    <div className="mt-4 text-sm font-medium text-white">{tool.label}</div>
                  </button>
                );
              })}
            </div>

            <div className="rounded-[24px] border border-white/10 bg-black/20 p-4">
              <div className="text-xs uppercase tracking-[0.2em] text-gray-500">Quick Layout</div>
              <div className="mt-3 grid gap-2">
                <button onClick={() => selectedLayer && updateLayer(selectedLayer.id, (layer) => ({ ...layer, x: Math.round((draft!.width - layer.width) / 2) }))} className="rounded-xl border border-white/10 bg-white/[0.03] px-3 py-2 text-left text-sm text-gray-200 hover:bg-white/[0.06]">Center selected layer horizontally</button>
                <button onClick={() => selectedLayer && updateLayer(selectedLayer.id, (layer) => ({ ...layer, y: Math.round((draft!.height - layer.height) / 2) }))} className="rounded-xl border border-white/10 bg-white/[0.03] px-3 py-2 text-left text-sm text-gray-200 hover:bg-white/[0.06]">Center selected layer vertically</button>
                <button onClick={duplicateScene} className="rounded-xl border border-white/10 bg-white/[0.03] px-3 py-2 text-left text-sm text-gray-200 hover:bg-white/[0.06]">Duplicate active scene</button>
                <button onClick={() => applyPreset(chooseAdaptPreset(draft!).id)} className="rounded-xl border border-white/10 bg-white/[0.03] px-3 py-2 text-left text-sm text-gray-200 hover:bg-white/[0.06]">Adapt to another platform ratio</button>
              </div>
            </div>
          </div>
        </aside>
      );
    }

    if (activeFlyout === "assets") {
      return (
        <aside className="min-h-0 border-r border-white/10 bg-[#0d1017] flex flex-col">
          <div className="border-b border-white/10 px-5 py-4">
            <div className="flex items-center justify-between gap-3">
              <div>
                <div className="text-sm font-medium text-white">Media Flyout</div>
                <div className="mt-1 text-xs text-gray-400">Search, place, and reuse real workspace assets in the current draft.</div>
              </div>
              <button onClick={() => fileInputRef.current?.click()} className="rounded-xl border border-white/10 bg-white/[0.03] px-3 py-2 text-xs text-gray-200 hover:bg-white/[0.06]">Upload</button>
            </div>
            <input
              value={assetSearch}
              onChange={(event) => setAssetSearch(event.target.value)}
              placeholder="Search assets, folders, or tags"
              className="mt-4 w-full rounded-2xl border border-white/10 bg-white/[0.03] px-4 py-3 text-sm text-white placeholder:text-gray-500 focus:border-cyan-400/30 focus:outline-none"
            />
          </div>
          <div className="min-h-0 flex-1 overflow-y-auto px-5 py-5 space-y-5">
            <div>
              <div className="mb-3 flex items-center justify-between text-xs uppercase tracking-[0.2em] text-gray-500">
                <span>Recent Assets</span>
                <span>{filteredAssets.length}</span>
              </div>
              <div className="grid gap-3">
                {filteredAssets.slice(0, 10).map((asset) => {
                  const Icon = asset.kind === "video" ? Video : asset.kind === "audio" || asset.kind === "voiceover" ? Waves : ImageIcon;
                  return (
                    <button
                      key={asset.id}
                      onClick={() => placeAsset(asset)}
                      className="rounded-2xl border border-white/10 bg-white/[0.03] p-3 text-left transition hover:border-cyan-400/30 hover:bg-white/[0.06]"
                    >
                      <div className="flex items-center gap-3">
                        <div className="rounded-2xl bg-[#0b0f16] p-3 text-cyan-200"><Icon className="h-4 w-4" /></div>
                        <div className="min-w-0 flex-1">
                          <div className="truncate text-sm font-medium text-white">{asset.name}</div>
                          <div className="mt-1 text-xs text-gray-400">{asset.folder} · {asset.tags.join(", ") || asset.kind}</div>
                        </div>
                      </div>
                    </button>
                  );
                })}
              </div>
            </div>

            <button onClick={() => navigate("/media")} className="w-full rounded-2xl border border-white/10 bg-black/20 px-4 py-3 text-sm text-white hover:bg-white/[0.06]">
              Open full Media Library
            </button>
          </div>
        </aside>
      );
    }

    if (activeFlyout === "scenes") {
      return (
        <aside className="min-h-0 border-r border-white/10 bg-[#0d1017] flex flex-col">
          <div className="border-b border-white/10 px-5 py-4">
            <div className="text-sm font-medium text-white">Scenes and Destinations</div>
            <div className="mt-1 text-xs text-gray-400">Manage story flow, active scenes, and the platforms attached to this draft.</div>
          </div>
          <div className="min-h-0 flex-1 overflow-y-auto px-5 py-5 space-y-5">
            <div className="flex gap-2">
              <button onClick={addScene} className="flex-1 rounded-2xl border border-white/10 bg-white/[0.03] px-4 py-3 text-sm text-white hover:bg-white/[0.06]">Add scene</button>
              <button onClick={duplicateScene} className="flex-1 rounded-2xl border border-white/10 bg-white/[0.03] px-4 py-3 text-sm text-white hover:bg-white/[0.06]">Duplicate</button>
            </div>

            <div className="space-y-3">
              {scenes.map((scene, index) => (
                <button
                  key={scene.id}
                  onClick={() => mutateDraft((current) => ({ ...current, activeSceneId: scene.id }), { history: false })}
                  className={`w-full rounded-[22px] border p-4 text-left transition ${draft!.activeSceneId === scene.id ? "border-cyan-400/30 bg-cyan-500/10" : "border-white/10 bg-white/[0.03] hover:bg-white/[0.06]"}`}
                >
                  <div className="flex items-start justify-between gap-3">
                    <div>
                      <div className="text-sm font-medium text-white">{index + 1}. {scene.name}</div>
                      <div className="mt-1 text-xs text-gray-400">{formatDuration(scene.durationMs)} · {scene.transition} · {scene.layerIds.length} layers</div>
                    </div>
                    {scenes.length > 1 ? (
                      <span
                        onClick={(event) => {
                          event.stopPropagation();
                          removeScene(scene.id);
                        }}
                        className="rounded-xl border border-white/10 p-2 text-gray-400 hover:text-white"
                      >
                        <Trash2 className="h-4 w-4" />
                      </span>
                    ) : null}
                  </div>
                </button>
              ))}
            </div>

            <div className="rounded-[24px] border border-white/10 bg-black/20 p-4">
              <div className="text-xs uppercase tracking-[0.2em] text-gray-500">Preview Destinations</div>
              <div className="mt-3 flex flex-wrap gap-2">
                {previewPlatforms.map((platformId) => {
                  const platform = PLATFORM_DEFINITIONS.find((item) => item.id === platformId);
                  const linked = draft!.linkedPlatformIds.includes(platformId);
                  return (
                    <button
                      key={platformId}
                      onClick={() => {
                        setPreviewPlatform(platformId);
                        mutateDraft((current) => ({
                          ...current,
                          linkedPlatformIds: linked ? current.linkedPlatformIds : [...current.linkedPlatformIds, platformId],
                        }), { history: false });
                      }}
                      className={`rounded-full border px-3 py-1.5 text-xs ${previewPlatform === platformId ? "border-cyan-400/30 bg-cyan-500/10 text-cyan-100" : "border-white/10 bg-white/[0.03] text-gray-300"}`}
                    >
                      {platform?.label ?? platformId}
                    </button>
                  );
                })}
              </div>

              <div className="mt-4 space-y-2 text-xs text-gray-400">
                {linkedAccounts.length === 0 ? (
                  <div className="rounded-2xl border border-dashed border-white/10 bg-white/[0.02] px-4 py-3">No connected publish targets are attached to this draft yet.</div>
                ) : (
                  linkedAccounts.map((account) => (
                    <div key={account.id} className="rounded-2xl border border-white/10 bg-white/[0.03] px-4 py-3">
                      <div className="text-sm text-white">{account.displayName}</div>
                      <div className="mt-1">{account.handle} · {account.syncHealth} · {account.canPublish ? "publish-ready" : "manual only"}</div>
                    </div>
                  ))
                )}
              </div>
            </div>
          </div>
        </aside>
      );
    }

    return (
      <aside className="min-h-0 border-r border-white/10 bg-[#0d1017] flex flex-col">
        <div className="border-b border-white/10 px-5 py-4">
          <div className="text-sm font-medium text-white">ATLAS Workflow</div>
          <div className="mt-1 text-xs text-gray-400">Move from guided briefing to execution, review, and draft-native apply without living inside one AI console stack.</div>
        </div>
        <div className="min-h-0 flex-1 overflow-y-auto px-5 py-5 space-y-6">
          <div className="flex flex-wrap gap-2">
            {ATLAS_TABS.map((tab) => (
              <button
                key={tab.id}
                onClick={() => setAtlasTab(tab.id)}
                className={`rounded-full border px-3 py-1.5 text-xs ${atlasTab === tab.id ? "border-cyan-400/30 bg-cyan-500/10 text-cyan-100" : "border-white/10 bg-white/[0.03] text-gray-300"}`}
              >
                {tab.label}
              </button>
            ))}
          </div>

          <div className="rounded-[24px] border border-cyan-400/20 bg-cyan-500/[0.07] p-4">
            <div className="flex items-start justify-between gap-3">
              <div>
                <div className="text-xs uppercase tracking-[0.18em] text-cyan-200/80">Current Task</div>
                <div className="mt-2 text-sm font-medium text-white">{atlasTaskMeta.label}</div>
              </div>
              <div className="rounded-full border border-cyan-300/20 bg-black/20 px-3 py-1 text-[11px] uppercase tracking-[0.18em] text-cyan-100/80">
                {atlasTabConfig.surface}
              </div>
            </div>
            <div className="mt-3 text-xs leading-relaxed text-cyan-50/75">{atlasTaskMeta.description}</div>
          </div>

          <div className="rounded-[24px] border border-white/10 bg-black/20 p-4">
            <div className="text-xs uppercase tracking-[0.18em] text-gray-500">Workflow Stages</div>
            <div className="mt-4 grid gap-2 sm:grid-cols-2 xl:grid-cols-4">
              {[
                { id: "brief", label: "Brief", icon: Sparkles, note: "Shape the request", badge: atlasStructuredRequest.summary.filter(Boolean).length > 0 ? `${atlasStructuredRequest.summary.filter(Boolean).length} cues` : "Ready" },
                { id: "generate", label: "Generate", icon: Wand2, note: "Queue and run", badge: atlasInFlightBriefs.length > 0 ? `${atlasInFlightBriefs.length} live` : atlasActiveStatusLabel },
                { id: "review", label: "Review", icon: Grid3x3, note: "Compare outputs", badge: `${atlasResultOptions.length} assets` },
                { id: "apply", label: "Apply", icon: CheckSquare, note: "Mutate the draft", badge: atlasSelectedOptionIds.length > 0 ? `${atlasSelectedOptionIds.length} selected` : "Pick results" },
              ].map((stage) => {
                const Icon = stage.icon;
                return (
                  <button
                    key={stage.id}
                    onClick={() => setAtlasWorkflowStage(stage.id as AtlasWorkflowStage)}
                    className={`rounded-[22px] border p-4 text-left transition ${stageToneForAtlasStep(stage.id as AtlasWorkflowStage)}`}
                  >
                    <div className="flex items-start justify-between gap-3">
                      <div className="rounded-2xl border border-white/10 bg-black/20 p-3 text-white">
                        <Icon className="h-4 w-4" />
                      </div>
                      <div className="rounded-full border border-white/10 bg-black/20 px-2.5 py-1 text-[10px] uppercase tracking-[0.18em] text-gray-300">{stage.badge}</div>
                    </div>
                    <div className="mt-4 text-sm font-medium text-white">{stage.label}</div>
                    <div className="mt-1 text-xs text-gray-400">{stage.note}</div>
                  </button>
                );
              })}
            </div>
          </div>

          {atlasWorkflowStage === "brief" ? (
            <>
              <div className="rounded-[24px] border border-white/10 bg-[radial-gradient(circle_at_top_left,rgba(92,244,255,0.09),transparent_35%),linear-gradient(180deg,rgba(255,255,255,0.04),rgba(255,255,255,0.02))] p-4">
                <div className="flex items-start justify-between gap-4">
                  <div>
                    <div className="text-xs uppercase tracking-[0.18em] text-gray-500">Guided Brief</div>
                    <div className="mt-1 text-sm text-white">Shape the request through task-specific controls instead of building a raw AI prompt.</div>
                  </div>
                  <div className="min-w-[180px]">
                    <AtlasChoiceField label={atlasTab === "variants" ? "Variant count" : "Output count"} value={String(atlasVariants)} options={ATLAS_OPTION_COUNT_OPTIONS} onChange={(value) => setAtlasVariants(Number(value))} />
                  </div>
                </div>
                <div className="mt-4 flex flex-wrap gap-2">
                  <button onClick={() => seedAtlasBriefFromDraft("draft")} className="rounded-full border border-white/10 bg-white/[0.05] px-3 py-1.5 text-[11px] text-white hover:bg-white/[0.09]">Use draft context</button>
                  <button onClick={() => seedAtlasBriefFromDraft("scene")} className="rounded-full border border-white/10 bg-white/[0.05] px-3 py-1.5 text-[11px] text-white hover:bg-white/[0.09]">Use active scene</button>
                  <button onClick={() => seedAtlasBriefFromDraft("selection")} disabled={!selectedLayer?.text?.trim()} className="rounded-full border border-white/10 bg-white/[0.05] px-3 py-1.5 text-[11px] text-white enabled:hover:bg-white/[0.09] disabled:cursor-not-allowed disabled:opacity-40">Use selected text</button>
                </div>
                <div className="mt-5">{renderAtlasBriefingUi()}</div>
                <div className="mt-5">
                  <AtlasTextAreaField
                    label="Advanced notes"
                    value={activeAtlasAdvancedNotes}
                    rows={5}
                    placeholder="Optional nuance, brand rules, references, pronunciation notes, or draft context you want ATLAS to respect."
                    onChange={updateActiveAtlasAdvancedNotes}
                    controls={
                      <>
                        {CREATE_SPEECH_WIRED ? null : null}
                        <button
                          type="button"
                          onClick={handleCreateMicClick}
                          className="h-8 w-8 rounded-lg border border-white/10 bg-white/[0.04] text-cyan-100 hover:bg-white/[0.08]"
                          aria-label="Create mic"
                          title="Create mic"
                        >
                          <Mic className="mx-auto h-4 w-4" />
                        </button>
                      </>
                    }
                    inlineNote={createVoiceNote}
                  />
                </div>
              </div>

              <div className="grid gap-4 xl:grid-cols-[1.1fr_0.9fr]">
                <div className="rounded-[24px] border border-white/10 bg-black/20 p-4">
                  <div className="flex items-start justify-between gap-3">
                    <div>
                      <div className="text-xs uppercase tracking-[0.18em] text-gray-500">Context Summary</div>
                      <div className="mt-1 text-xs text-gray-400">ATLAS will use this context when the request is promoted into execution.</div>
                    </div>
                    <div className="rounded-full border border-white/10 bg-white/[0.04] px-3 py-1 text-[11px] text-gray-400">{atlasStructuredRequest.variantsRequested} outputs</div>
                  </div>
                  <div className="mt-4 flex flex-wrap gap-2 text-[11px] text-gray-400">
                    <span className="rounded-full border border-white/10 bg-white/[0.04] px-3 py-1">Platform: {atlasStructuredRequest.platformId}</span>
                    <span className="rounded-full border border-white/10 bg-white/[0.04] px-3 py-1">Surface: {atlasTabConfig.surface}</span>
                    <span className="rounded-full border border-white/10 bg-white/[0.04] px-3 py-1">Linked assets: {draft?.linkedAssetIds.length ?? 0}</span>
                    {atlasStructuredRequest.summary.filter(Boolean).slice(0, 4).map((item) => (
                      <span key={`summary-${item}`} className="rounded-full border border-white/10 bg-white/[0.04] px-3 py-1">{item}</span>
                    ))}
                  </div>
                  <div className="mt-4 rounded-[20px] border border-white/10 bg-white/[0.03] p-4">
                    <div className="text-sm font-medium text-white">{atlasStructuredRequest.objective}</div>
                    <div className="mt-2 text-sm leading-relaxed text-gray-300">{atlasStructuredRequest.brief}</div>
                  </div>
                </div>

                <div className="rounded-[24px] border border-white/10 bg-black/20 p-4">
                  <div className="text-xs uppercase tracking-[0.18em] text-gray-500">Execution Profile</div>
                  <div className="mt-1 text-xs text-gray-400">Provider and model stay secondary until the brief is shaped.</div>
                  <div className="mt-4 grid grid-cols-2 gap-3">
                    <label className="block">
                      <div className="mb-2 text-xs uppercase tracking-[0.18em] text-gray-500">Provider</div>
                      <select value={atlasProvider} onChange={(event) => setAtlasProvider(event.target.value as ModelProviderId)} className="w-full rounded-2xl border border-white/10 bg-black/25 px-4 py-3 text-white">
                        {AI_PROVIDERS.map((provider) => (
                          <option key={provider.id} value={provider.id}>{provider.label}</option>
                        ))}
                      </select>
                    </label>
                    <label className="block">
                      <div className="mb-2 text-xs uppercase tracking-[0.18em] text-gray-500">Model</div>
                      <select value={atlasModel} onChange={(event) => setAtlasModel(event.target.value)} className="w-full rounded-2xl border border-white/10 bg-black/25 px-4 py-3 text-white">
                        {atlasModels.map((model) => (
                          <option key={model.id} value={model.id}>{model.label}</option>
                        ))}
                      </select>
                    </label>
                  </div>
                  <div className="mt-4 grid grid-cols-3 gap-2">
                    <button onClick={saveAtlasPacketToLibrary} className="rounded-2xl border border-white/10 bg-white/[0.03] px-4 py-3 text-sm text-white hover:bg-white/[0.06]">Save packet</button>
                    <button onClick={() => void copyAtlasPacket()} className="rounded-2xl border border-white/10 bg-white/[0.03] px-4 py-3 text-sm text-white hover:bg-white/[0.06]">Copy packet</button>
                    <button onClick={() => setAtlasWorkflowStage("generate")} className="rounded-2xl bg-gradient-to-r from-cyan-500 to-blue-600 px-4 py-3 text-sm font-medium text-[#06121e]">Continue</button>
                  </div>
                </div>
              </div>

              {atlasDraftBriefs.length > 0 ? (
                <div className="rounded-[24px] border border-white/10 bg-black/20 p-4">
                  <div className="flex items-center justify-between gap-3">
                    <div>
                      <div className="text-xs uppercase tracking-[0.18em] text-gray-500">Draft AI History</div>
                      <div className="mt-1 text-xs text-gray-400">Recent ATLAS workflows already linked to this draft.</div>
                    </div>
                    <div className="text-xs text-gray-500">{atlasDraftBriefs.length}</div>
                  </div>
                  <div className="mt-4 space-y-2">
                    {atlasDraftBriefs.slice(0, 4).map((brief) => {
                      const briefTask = AI_TASKS.find((task) => task.id === brief.taskType);
                      const resultCount = flattenResultOptions(brief.resultGroups ?? []).length;
                      return (
                        <button
                          key={brief.id}
                          onClick={() => loadDraftBrief(brief.id)}
                          className={`w-full rounded-2xl border p-3 text-left transition ${activeAtlasBrief?.id === brief.id ? "border-cyan-400/30 bg-cyan-500/10" : "border-white/10 bg-white/[0.03] hover:border-cyan-400/30 hover:bg-white/[0.06]"}`}
                        >
                          <div className="flex items-start justify-between gap-3">
                            <div className="min-w-0 flex-1">
                              <div className="truncate text-sm font-medium text-white">{brief.objective}</div>
                              <div className="mt-1 text-xs text-gray-400">{briefTask?.label ?? brief.taskType} · {brief.platformId} · {brief.providerId}/{brief.modelId}</div>
                              <div className="mt-2 flex flex-wrap gap-2 text-[10px] uppercase tracking-[0.18em] text-gray-500">
                                <span>{resultCount} results</span>
                                <span>{brief.retryCount ? `${brief.retryCount} retry${brief.retryCount === 1 ? "" : "ies"}` : "First pass"}</span>
                                <span>{brief.startedAt ? formatRuntimeDuration(atlasBriefDurationMs(brief, atlasRuntimeNow)) : formatRelativeTimestamp(brief.createdAt, atlasRuntimeNow)}</span>
                              </div>
                            </div>
                            <div className={`rounded-full border px-2.5 py-1 text-[10px] uppercase tracking-[0.18em] ${atlasBriefStatusTone(brief.status)}`}>{atlasBriefStatusLabel(brief.status)}</div>
                          </div>
                        </button>
                      );
                    })}
                  </div>
                </div>
              ) : null}
            </>
          ) : null}

          {atlasWorkflowStage === "generate" ? (
            <>
              <div className="rounded-[24px] border border-white/10 bg-black/20 p-4">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <div className="text-xs uppercase tracking-[0.18em] text-gray-500">Execution Packet</div>
                    <div className="mt-1 text-xs text-gray-400">ATLAS sends a structured production brief through the desktop runtime, then returns review-ready creator assets.</div>
                  </div>
                  <div className="rounded-full border border-white/10 bg-white/[0.03] px-3 py-1 text-[11px] text-gray-400">{atlasStructuredRequest.variantsRequested} outputs</div>
                </div>
                <div className="mt-4 rounded-[20px] border border-white/10 bg-white/[0.03] p-4">
                  <div className="text-sm font-medium text-white">{atlasStructuredRequest.objective}</div>
                  <pre className="mt-3 max-h-48 overflow-y-auto whitespace-pre-wrap text-xs leading-relaxed text-gray-300">{atlasPacket}</pre>
                </div>
                <div className="mt-4 grid grid-cols-2 gap-2 xl:grid-cols-4">
                  <button onClick={() => queueAtlasRequest()} disabled={!atlasBridgeAvailable} className="rounded-2xl bg-gradient-to-r from-cyan-500 to-blue-600 px-4 py-3 text-sm font-medium text-[#06121e] disabled:cursor-not-allowed disabled:opacity-40">Generate now</button>
                  <button onClick={() => activeAtlasBrief && void runAtlasBriefExecution(activeAtlasBrief.id, true, atlasRequestPayloadFromBrief(activeAtlasBrief))} disabled={!activeAtlasBrief || atlasCanCancel || !atlasBridgeAvailable} className="rounded-2xl border border-white/10 bg-white/[0.03] px-4 py-3 text-sm text-white enabled:hover:bg-white/[0.06] disabled:cursor-not-allowed disabled:opacity-40">Retry run</button>
                  <button onClick={cancelActiveAtlasExecution} disabled={!atlasCanCancel} className="rounded-2xl border border-white/10 bg-white/[0.03] px-4 py-3 text-sm text-white enabled:hover:bg-white/[0.06] disabled:cursor-not-allowed disabled:opacity-40">Cancel run</button>
                  <button onClick={() => setAtlasWorkflowStage(atlasResultOptions.length > 0 ? "review" : "brief")} className="rounded-2xl border border-white/10 bg-white/[0.03] px-4 py-3 text-sm text-white hover:bg-white/[0.06]">{atlasResultOptions.length > 0 ? "Go to review" : "Back to brief"}</button>
                </div>
              </div>

              <div className="grid gap-4 xl:grid-cols-[1.05fr_0.95fr]">
                <div className="rounded-[24px] border border-white/10 bg-black/20 p-4">
                  <div className="flex items-start justify-between gap-3">
                    <div>
                      <div className="text-xs uppercase tracking-[0.18em] text-gray-500">Live Runtime State</div>
                      <div className="mt-1 text-xs text-gray-400">Track the live run, see where it is in the workflow, and recover cleanly if it stalls or fails.</div>
                    </div>
                    <div className={`rounded-full border px-3 py-1 text-[11px] ${atlasActiveStatusTone}`}>{atlasActiveStatusLabel}</div>
                  </div>
                  <div className="mt-4 rounded-[20px] border border-white/10 bg-[radial-gradient(circle_at_top_left,rgba(92,244,255,0.12),transparent_35%),linear-gradient(180deg,rgba(255,255,255,0.04),rgba(255,255,255,0.02))] p-4">
                    <div className="flex flex-wrap items-center gap-2 text-[11px] uppercase tracking-[0.18em] text-gray-500">
                      <span className="rounded-full border border-white/10 bg-black/20 px-3 py-1">Status: {atlasActiveStatusLabel}</span>
                      {activeAtlasBrief?.providerId ? <span className="rounded-full border border-white/10 bg-black/20 px-3 py-1">Provider: {activeAtlasBrief.providerId}</span> : null}
                      {activeAtlasBrief?.modelId ? <span className="rounded-full border border-white/10 bg-black/20 px-3 py-1">Model: {activeAtlasBrief.modelId}</span> : null}
                      {activeAtlasBrief ? <span className="rounded-full border border-white/10 bg-black/20 px-3 py-1">Runtime: {atlasExecutionRuntime}</span> : null}
                      {activeAtlasBrief?.retryCount ? <span className="rounded-full border border-white/10 bg-black/20 px-3 py-1">Retries: {activeAtlasBrief.retryCount}</span> : null}
                    </div>
                    <div className="mt-4 h-2 overflow-hidden rounded-full bg-white/[0.06]">
                      <div
                        className={`h-full rounded-full transition-[width] duration-500 ${activeAtlasBrief?.status === "failed" ? "bg-gradient-to-r from-rose-400 to-red-500" : activeAtlasBrief?.status === "timed-out" ? "bg-gradient-to-r from-amber-300 to-amber-500" : activeAtlasBrief?.status === "cancelled" ? "bg-gradient-to-r from-gray-500 to-gray-400" : "bg-gradient-to-r from-cyan-400 to-blue-500"} ${(activeAtlasBrief?.status === "running" || activeAtlasBrief?.status === "retrying") ? "animate-pulse" : ""}`}
                        style={{ width: `${atlasExecutionProgress}%` }}
                      />
                    </div>
                    <div className="mt-4 text-lg font-medium text-white">
                      {atlasActiveStatusLabel}
                    </div>
                    <div className="mt-2 text-sm leading-relaxed text-gray-300">{atlasBridgeAvailable ? atlasOperationalCopy : "This host does not expose the ATLAS execution bridge, so direct backend execution is unavailable."}</div>
                    {activeAtlasBrief?.routeSummary ? <div className="mt-3 text-xs leading-relaxed text-gray-500">Runtime route: {activeAtlasBrief.routeSummary}</div> : null}
                    {atlasRecoveryCopy ? <div className="mt-3 rounded-2xl border border-white/10 bg-black/20 px-4 py-3 text-xs leading-relaxed text-gray-300">{atlasRecoveryCopy}</div> : null}
                    {atlasHasRecoverableError && activeAtlasBrief ? (
                      <div className="mt-3 grid gap-2 sm:grid-cols-2">
                        <button onClick={() => void runAtlasBriefExecution(activeAtlasBrief.id, true, atlasRequestPayloadFromBrief(activeAtlasBrief))} className="rounded-2xl bg-gradient-to-r from-cyan-500 to-blue-600 px-4 py-3 text-sm font-medium text-[#06121e]">Retry this run</button>
                        <button onClick={() => setAtlasWorkflowStage(atlasResultOptions.length > 0 ? "review" : "brief")} className="rounded-2xl border border-white/10 bg-white/[0.03] px-4 py-3 text-sm text-white hover:bg-white/[0.06]">{atlasResultOptions.length > 0 ? "Review current results" : "Refine the brief"}</button>
                      </div>
                    ) : null}
                    <div className="mt-4 grid gap-2 sm:grid-cols-2 xl:grid-cols-4">
                      {atlasExecutionTimeline.map((step) => (
                        <div key={step.id} className={`rounded-2xl border px-3 py-3 ${step.state === "complete" ? "border-emerald-300/20 bg-emerald-400/10" : step.state === "current" ? "border-cyan-400/30 bg-cyan-500/10" : step.state === "attention" ? "border-amber-300/25 bg-amber-500/10" : "border-white/10 bg-white/[0.03]"}`}>
                          <div className="flex items-center gap-2 text-sm font-medium text-white">
                            {step.state === "complete" ? <CheckCircle2 className="h-4 w-4 text-emerald-200" /> : step.state === "current" ? <LoaderCircle className="h-4 w-4 animate-spin text-cyan-200" /> : step.state === "attention" ? <AlertTriangle className="h-4 w-4 text-amber-200" /> : <CircleDot className="h-4 w-4 text-gray-500" />}
                            <span>{step.label}</span>
                          </div>
                          <div className="mt-1 text-xs text-gray-400">{step.note}</div>
                        </div>
                      ))}
                    </div>
                  </div>
                  <div className="mt-3 grid grid-cols-2 gap-2">
                    <button onClick={() => setAtlasResponse(activeAtlasBrief?.responseText ?? "")} disabled={!activeAtlasBrief?.responseText} className="rounded-xl border border-white/10 bg-white/[0.03] px-3 py-2 text-xs text-white hover:bg-white/[0.06] disabled:cursor-not-allowed disabled:opacity-40">View raw response</button>
                    <button onClick={() => setAtlasWorkflowStage(atlasResultOptions.length > 0 ? "review" : "brief")} className="rounded-xl border border-white/10 bg-white/[0.03] px-3 py-2 text-xs text-white hover:bg-white/[0.06]">{atlasResultOptions.length > 0 ? "Open review" : "Refine brief"}</button>
                  </div>
                  <textarea value={atlasResponse} readOnly rows={6} className="mt-3 w-full rounded-[20px] border border-white/10 bg-black/25 px-4 py-4 text-white/85 resize-none" placeholder={atlasResponsePlaceholder} />
                </div>

                <div className="rounded-[24px] border border-white/10 bg-black/20 p-4">
                  <div className="flex items-center justify-between gap-3">
                    <div>
                      <div className="text-xs uppercase tracking-[0.18em] text-gray-500">Request History</div>
                      <div className="mt-1 text-xs text-gray-400">See what is live, what completed cleanly, and what needs another pass.</div>
                    </div>
                    <div className="text-xs text-gray-500">{atlasRecentBriefs.length}</div>
                  </div>
                  <div className="mt-4 space-y-2">
                    {atlasRecentBriefs.length === 0 ? (
                      <div className="rounded-2xl border border-dashed border-white/10 bg-white/[0.02] px-4 py-6 text-sm text-gray-400">No ATLAS jobs have been queued for this draft yet.</div>
                    ) : (
                      atlasRecentBriefs.map((brief) => {
                        const briefTask = AI_TASKS.find((task) => task.id === brief.taskType);
                        const resultCount = flattenResultOptions(brief.resultGroups ?? []).length;
                        return (
                          <button key={brief.id} onClick={() => loadDraftBrief(brief.id)} className={`w-full rounded-2xl border p-3 text-left transition ${activeAtlasBrief?.id === brief.id ? "border-cyan-400/30 bg-cyan-500/10" : "border-white/10 bg-white/[0.03] hover:border-cyan-400/30 hover:bg-white/[0.06]"}`}>
                            <div className="flex items-start justify-between gap-3">
                              <div className="min-w-0 flex-1">
                                <div className="truncate text-sm font-medium text-white">{brief.objective}</div>
                                <div className="mt-1 text-xs text-gray-400">{briefTask?.label ?? brief.taskType} · {brief.platformId} · {brief.providerId}/{brief.modelId}</div>
                                <div className="mt-2 flex flex-wrap gap-2 text-[10px] uppercase tracking-[0.18em] text-gray-500">
                                  <span>{resultCount} results</span>
                                  <span>{brief.startedAt ? formatRuntimeDuration(atlasBriefDurationMs(brief, atlasRuntimeNow)) : formatRelativeTimestamp(brief.createdAt, atlasRuntimeNow)}</span>
                                  <span>{brief.retryCount ? `${brief.retryCount} retries` : "First pass"}</span>
                                </div>
                                {brief.errorMessage ? <div className="mt-2 text-xs leading-relaxed text-gray-400">{brief.errorMessage}</div> : null}
                              </div>
                              <div className="flex flex-col items-end gap-2">
                                <div className={`rounded-full border px-2.5 py-1 text-[10px] uppercase tracking-[0.18em] ${atlasBriefStatusTone(brief.status)}`}>{atlasBriefStatusLabel(brief.status)}</div>
                                <div className="text-[11px] text-gray-500">{formatRelativeTimestamp(brief.completedAt ?? brief.startedAt ?? brief.createdAt, atlasRuntimeNow)}</div>
                              </div>
                            </div>
                          </button>
                        );
                      })
                    )}
                  </div>
                </div>
              </div>
            </>
          ) : null}

          {atlasWorkflowStage === "review" ? (
            <>
              <div className="rounded-[24px] border border-white/10 bg-black/20 p-4">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <div className="text-xs uppercase tracking-[0.18em] text-gray-500">Review Workspace</div>
                    <div className="mt-1 text-xs text-gray-400">Keep the premium result cards central, inspect structured fields, compare options, and choose the best candidate before applying.</div>
                  </div>
                  <div className="rounded-full border border-white/10 bg-white/[0.03] px-3 py-1 text-[11px] text-gray-400">{atlasResultOptions.length} assets</div>
                </div>

                {atlasReviewLoading ? (
                  <div className="mt-4 rounded-[20px] border border-cyan-400/20 bg-cyan-500/[0.08] p-4">
                    <div className="flex items-start gap-3">
                      <LoaderCircle className="mt-0.5 h-4 w-4 animate-spin text-cyan-200" />
                      <div>
                        <div className="text-sm font-medium text-white">ATLAS is preparing review assets</div>
                        <div className="mt-1 text-xs leading-relaxed text-cyan-50/80">The run is still live. Result cards will appear here as soon as the provider finishes and the response is shaped into review-ready assets.</div>
                      </div>
                    </div>
                  </div>
                ) : null}

                {atlasReviewRefreshing ? (
                  <div className="mt-4 rounded-[20px] border border-amber-300/20 bg-amber-500/10 p-4">
                    <div className="flex items-start gap-3">
                      <RotateCcw className="mt-0.5 h-4 w-4 text-amber-200" />
                      <div>
                        <div className="text-sm font-medium text-white">Refreshing result set</div>
                        <div className="mt-1 text-xs leading-relaxed text-amber-50/80">Previous cards stay visible while ATLAS retries the run, so you do not lose the current review context.</div>
                      </div>
                    </div>
                  </div>
                ) : null}

                <div className="mt-4 flex flex-wrap gap-2">
                  <button
                    onClick={() => activeAtlasBrief && updateStructuredBrief(activeAtlasBrief.id, (brief) => ({ ...brief, selectedResultIds: atlasResultOptions.map((option) => option.id) }))}
                    disabled={!activeAtlasBrief || atlasResultOptions.length === 0}
                    className="rounded-full border border-white/10 bg-white/[0.03] px-3 py-1.5 text-[11px] text-white enabled:hover:bg-white/[0.06] disabled:cursor-not-allowed disabled:opacity-40"
                  >
                    Select all
                  </button>
                  <button
                    onClick={() => activeAtlasBrief && updateStructuredBrief(activeAtlasBrief.id, (brief) => ({ ...brief, selectedResultIds: [] }))}
                    disabled={!activeAtlasBrief || atlasSelectedOptionIds.length === 0}
                    className="rounded-full border border-white/10 bg-white/[0.03] px-3 py-1.5 text-[11px] text-white enabled:hover:bg-white/[0.06] disabled:cursor-not-allowed disabled:opacity-40"
                  >
                    Clear selection
                  </button>
                  <button
                    onClick={() => setAtlasCompareOptionIds([])}
                    disabled={atlasCompareOptionIds.length === 0}
                    className="rounded-full border border-white/10 bg-white/[0.03] px-3 py-1.5 text-[11px] text-white enabled:hover:bg-white/[0.06] disabled:cursor-not-allowed disabled:opacity-40"
                  >
                    Clear compare
                  </button>
                  <button onClick={() => setAtlasWorkflowStage("generate")} className="rounded-full border border-white/10 bg-white/[0.03] px-3 py-1.5 text-[11px] text-white hover:bg-white/[0.06]">Back to generate</button>
                </div>

                {atlasSelectedOptions.length > 0 ? (
                  <div className="mt-4 rounded-[20px] border border-cyan-400/20 bg-cyan-500/[0.08] p-4">
                    <div className="flex items-center justify-between gap-3">
                      <div>
                        <div className="text-sm font-medium text-white">{atlasSelectedOptions.length} picked for apply</div>
                        <div className="mt-1 text-xs text-cyan-50/80">Move to the Apply stage to preview what will change in the draft before mutation.</div>
                      </div>
                      <button onClick={() => setAtlasWorkflowStage("apply")} className="rounded-2xl bg-gradient-to-r from-cyan-500 to-blue-600 px-4 py-2.5 text-sm font-medium text-[#06121e]">Open Apply Stage</button>
                    </div>
                  </div>
                ) : null}
              </div>

              {atlasCompareOptions.length > 0 ? (
                <div className="rounded-[24px] border border-white/10 bg-[radial-gradient(circle_at_top_left,rgba(92,244,255,0.12),transparent_35%),linear-gradient(180deg,rgba(255,255,255,0.04),rgba(255,255,255,0.02))] p-4">
                  <div className="flex items-start justify-between gap-3">
                    <div>
                      <div className="text-xs uppercase tracking-[0.18em] text-gray-500">Compare Variants</div>
                      <div className="mt-1 text-xs text-gray-400">{atlasCompareOptions.length < 2 ? "Select one more result card to compare two assets side by side." : "Review two candidate assets side by side before choosing what moves into Apply."}</div>
                    </div>
                    <div className="rounded-full border border-white/10 bg-black/20 px-3 py-1 text-[11px] text-gray-400">{atlasCompareOptions.length}/2</div>
                  </div>
                  <div className={`mt-4 grid gap-3 ${atlasCompareOptions.length === 1 ? "md:grid-cols-1" : "md:grid-cols-2"}`}>
                    {atlasCompareOptions.map((option) => {
                      const theme = atlasCardTheme(activeAtlasBrief?.taskType ?? atlasTabConfig.taskType);
                      const Icon = theme.icon;
                      return (
                        <div key={`compare-${option.id}`} className="rounded-[22px] border border-white/10 bg-black/25 p-4">
                          <div className="rounded-[18px] border border-white/10 p-4" style={{ backgroundImage: theme.gradient }}>
                            <div className="flex items-start justify-between gap-3">
                              <div className="flex min-w-0 items-center gap-3">
                                <div className="rounded-2xl border border-white/10 bg-black/30 p-3 text-white"><Icon className="h-4 w-4" /></div>
                                <div className="min-w-0">
                                  <div className="truncate text-sm font-medium text-white">{option.label}</div>
                                  <div className="mt-1 text-[11px] uppercase tracking-[0.18em] text-white/70">{theme.assetLabel}</div>
                                </div>
                              </div>
                              <button onClick={() => toggleAtlasOptionCompare(option.id)} className="rounded-xl border border-white/10 bg-black/25 px-3 py-2 text-[11px] text-white hover:bg-black/40">Remove</button>
                            </div>
                            <div className="mt-4 text-base font-medium leading-relaxed text-white">{atlasPreviewText(option, 220)}</div>
                          </div>
                          <div className="mt-4 flex flex-wrap gap-2 text-[11px] text-gray-400">
                            <span className="rounded-full border border-white/10 bg-white/[0.04] px-3 py-1">{activeAtlasBrief?.providerId}/{activeAtlasBrief?.modelId}</span>
                            {atlasCardMetrics(option).map((metric) => (
                              <span key={`${option.id}-${metric}`} className="rounded-full border border-white/10 bg-white/[0.04] px-3 py-1">{metric}</span>
                            ))}
                          </div>
                          <div className="mt-4 space-y-2">
                            {displaySectionsForOption(option).slice(0, 3).map((section) => (
                              <div key={section.id} className="rounded-2xl border border-white/10 bg-white/[0.03] px-3 py-3">
                                <div className="text-[11px] uppercase tracking-[0.18em] text-gray-500">{section.label}</div>
                                <div className="mt-2 text-sm leading-relaxed text-gray-200">{section.content}</div>
                              </div>
                            ))}
                          </div>
                        </div>
                      );
                    })}
                  </div>
                </div>
              ) : null}

              <div className="space-y-4">
                {atlasReviewLoading ? (
                  <div className="grid gap-4 xl:grid-cols-2">
                    {Array.from({ length: ATLAS_RESULT_PLACEHOLDER_COUNT }).map((_, index) => (
                      <div key={`atlas-placeholder-${index}`} className="overflow-hidden rounded-[24px] border border-white/10 bg-black/20 animate-pulse">
                        <div className="border-b border-white/10 p-5 bg-[linear-gradient(135deg,rgba(92,244,255,0.12),rgba(37,99,235,0.08))]">
                          <div className="h-4 w-24 rounded-full bg-white/10" />
                          <div className="mt-4 h-7 w-2/3 rounded-full bg-white/10" />
                          <div className="mt-3 h-4 w-full rounded-full bg-white/10" />
                          <div className="mt-2 h-4 w-4/5 rounded-full bg-white/10" />
                        </div>
                        <div className="p-5">
                          <div className="space-y-3">
                            <div className="rounded-[20px] border border-white/10 bg-white/[0.03] p-4">
                              <div className="h-3 w-20 rounded-full bg-white/10" />
                              <div className="mt-3 h-4 w-full rounded-full bg-white/10" />
                              <div className="mt-2 h-4 w-5/6 rounded-full bg-white/10" />
                            </div>
                            <div className="rounded-[20px] border border-white/10 bg-white/[0.03] p-4">
                              <div className="h-3 w-16 rounded-full bg-white/10" />
                              <div className="mt-3 h-4 w-full rounded-full bg-white/10" />
                              <div className="mt-2 h-4 w-2/3 rounded-full bg-white/10" />
                            </div>
                          </div>
                          <div className="mt-5 flex gap-2">
                            <div className="h-10 flex-1 rounded-2xl bg-white/10" />
                            <div className="h-10 flex-1 rounded-2xl bg-white/10" />
                          </div>
                        </div>
                      </div>
                    ))}
                  </div>
                ) : atlasResultGroups.length === 0 ? (
                  <div className="rounded-[20px] border border-dashed border-white/10 bg-white/[0.02] px-4 py-6 text-sm leading-relaxed text-gray-400">
                    Run an ATLAS request and the backend response will be transformed into premium creator assets, ready for review, compare, and staged application into the working draft.
                  </div>
                ) : (
                  atlasResultGroups.map((group) => {
                    const sortedOptions = [...group.options].sort((left, right) => Number(right.isPinned) - Number(left.isPinned) || Number(right.isFavorite) - Number(left.isFavorite) || left.sourceIndex - right.sourceIndex);
                    const theme = atlasCardTheme(group.sourceTaskType);
                    const GroupIcon = theme.icon;
                    return (
                      <div key={group.id} className="rounded-[26px] border border-white/10 bg-[linear-gradient(180deg,rgba(255,255,255,0.04),rgba(255,255,255,0.02))] p-4">
                        <div className="flex items-start justify-between gap-3 rounded-[20px] border border-white/10 p-4" style={{ backgroundImage: theme.gradient }}>
                          <div className="flex min-w-0 items-start gap-4">
                            <div className="rounded-[20px] border border-white/10 bg-black/25 p-3 text-white">
                              <GroupIcon className="h-5 w-5" />
                            </div>
                            <div>
                              <div className="text-[11px] uppercase tracking-[0.2em] text-white/65">{theme.assetLabel}</div>
                              <div className="mt-2 text-lg font-medium text-white">{group.label}</div>
                              <div className="mt-1 max-w-3xl text-sm text-white/75">{group.description}</div>
                            </div>
                          </div>
                          <div className="flex flex-wrap items-center justify-end gap-2 text-[11px] text-white/75">
                            <span className="rounded-full border border-white/10 bg-black/20 px-3 py-1">{group.providerId}/{group.modelId}</span>
                            <span className="rounded-full border border-white/10 bg-black/20 px-3 py-1">{group.options.length} assets</span>
                          </div>
                        </div>
                        <div className="mt-4 grid gap-4 xl:grid-cols-2">
                          {sortedOptions.map((option) => {
                            const isSelected = atlasSelectedOptionIds.includes(option.id);
                            const isExpanded = atlasExpandedOptionIds.includes(option.id);
                            const isCompared = atlasCompareOptionIds.includes(option.id);
                            const cardPreview = atlasPreviewText(option, isExpanded ? 360 : 180);
                            const allSections = displaySectionsForOption(option);
                            const quickSections = allSections.slice(0, isExpanded ? allSections.length : Math.min(allSections.length, 3));
                            return (
                              <article key={option.id} className={`overflow-hidden rounded-[24px] border transition ${isSelected ? "border-cyan-400/30 bg-cyan-500/[0.05]" : "border-white/10 bg-black/20 hover:border-white/20"}`}>
                                <div className="border-b border-white/10 p-5" style={{ backgroundImage: theme.gradient }}>
                                  <div className="flex items-start justify-between gap-4">
                                    <div className="min-w-0 flex-1">
                                      <div className="flex flex-wrap items-center gap-2">
                                        <button onClick={() => toggleAtlasOptionSelection(option.id)} className={`rounded-full border px-2.5 py-1 text-[11px] ${isSelected ? "border-cyan-400/30 bg-cyan-500/10 text-cyan-100" : "border-white/10 bg-black/20 text-white/80"}`}>
                                          <CheckSquare className="mr-1 inline h-3.5 w-3.5" />
                                          {isSelected ? "Selected" : "Select"}
                                        </button>
                                        <span className="rounded-full border border-white/10 px-2.5 py-1 text-[10px] uppercase tracking-[0.18em] text-white/75">{theme.assetLabel}</span>
                                        {option.isPinned ? <span className="rounded-full border border-amber-300/20 bg-amber-400/10 px-2 py-0.5 text-[10px] uppercase tracking-[0.18em] text-amber-100">Pinned</span> : null}
                                        {option.isFavorite ? <span className="rounded-full border border-pink-300/20 bg-pink-400/10 px-2 py-0.5 text-[10px] uppercase tracking-[0.18em] text-pink-100">Favorite</span> : null}
                                        {isCompared ? <span className="rounded-full border border-blue-300/20 bg-blue-400/10 px-2 py-0.5 text-[10px] uppercase tracking-[0.18em] text-blue-100">Comparing</span> : null}
                                      </div>
                                      <div className="mt-4 text-lg font-medium text-white">{option.label}</div>
                                      <div className="mt-3 max-w-2xl text-sm leading-relaxed text-white/80">{cardPreview}</div>
                                    </div>
                                    <div className="flex items-center gap-2">
                                      <button onClick={() => toggleAtlasOptionExpansion(option.id)} className="rounded-xl border border-white/10 bg-black/20 p-2 text-white/80 hover:bg-black/35">
                                        {isExpanded ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                                      </button>
                                      <button onClick={() => updateAtlasOption(option.id, (item) => ({ ...item, isPinned: !item.isPinned }))} className={`rounded-xl border p-2 ${option.isPinned ? "border-amber-300/30 bg-amber-400/10 text-amber-100" : "border-white/10 bg-black/20 text-white/80"}`}><Pin className="h-4 w-4" /></button>
                                      <button onClick={() => updateAtlasOption(option.id, (item) => ({ ...item, isFavorite: !item.isFavorite }))} className={`rounded-xl border p-2 ${option.isFavorite ? "border-pink-300/30 bg-pink-400/10 text-pink-100" : "border-white/10 bg-black/20 text-white/80"}`}><Star className="h-4 w-4" /></button>
                                    </div>
                                  </div>
                                  <div className="mt-4 flex flex-wrap gap-2 text-[11px] text-white/75">
                                    <span className="rounded-full border border-white/10 bg-black/20 px-3 py-1">{group.providerId}/{group.modelId}</span>
                                    {atlasCardMetrics(option).map((metric) => (
                                      <span key={`${option.id}-${metric}`} className="rounded-full border border-white/10 bg-black/20 px-3 py-1">{metric}</span>
                                    ))}
                                  </div>
                                </div>

                                <div className="p-5">
                                  <div className="grid gap-3">
                                    {quickSections.map((section) => (
                                      <div key={section.id} className="rounded-[20px] border border-white/10 px-4 py-4" style={{ background: theme.chipBackground }}>
                                        <div className="text-[11px] uppercase tracking-[0.18em] text-gray-500">{section.label}</div>
                                        <div className="mt-2 text-sm leading-relaxed text-gray-100">{section.content}</div>
                                      </div>
                                    ))}
                                  </div>

                                  {optionMetaEntries(option).length > 0 ? (
                                    <div className="mt-4 flex flex-wrap gap-2 text-[11px] text-gray-400">
                                      {optionMetaEntries(option).map(([key, value]) => (
                                        <span key={`${option.id}-${key}`} className="rounded-full border border-white/10 bg-white/[0.04] px-3 py-1">{key}: {value}</span>
                                      ))}
                                    </div>
                                  ) : null}

                                  <div className="mt-5 flex flex-wrap gap-2">
                                    <button onClick={() => openAtlasApplyStage([option.id])} className="rounded-2xl bg-gradient-to-r from-cyan-500 to-blue-600 px-4 py-2.5 text-sm font-medium text-[#06121e]">Prepare Apply</button>
                                    <button onClick={() => saveAtlasOptionToLibrary(group, option)} className="rounded-2xl border border-white/10 bg-white/[0.03] px-4 py-2.5 text-sm text-white hover:bg-white/[0.06]"><Save className="mr-2 inline h-4 w-4" />Save</button>
                                    <button onClick={() => regenerateFromAtlasOption(option, "similar")} className="rounded-2xl border border-white/10 bg-white/[0.03] px-4 py-2.5 text-sm text-white hover:bg-white/[0.06]"><Wand2 className="mr-2 inline h-4 w-4" />Regenerate similar</button>
                                    <button onClick={() => regenerateFromAtlasOption(option, "stronger")} className="rounded-2xl border border-white/10 bg-white/[0.03] px-4 py-2.5 text-sm text-white hover:bg-white/[0.06]"><BrainCircuit className="mr-2 inline h-4 w-4" />Regenerate stronger</button>
                                    <button onClick={() => toggleAtlasOptionCompare(option.id)} className={`rounded-2xl border px-4 py-2.5 text-sm ${isCompared ? "border-blue-300/30 bg-blue-400/10 text-blue-100" : "border-white/10 bg-white/[0.03] text-white hover:bg-white/[0.06]"}`}><Grid3x3 className="mr-2 inline h-4 w-4" />Compare</button>
                                  </div>
                                </div>
                              </article>
                            );
                          })}
                        </div>
                      </div>
                    );
                  })
                )}
              </div>
            </>
          ) : null}

          {atlasWorkflowStage === "apply" ? (
            <>
              {atlasResultOptions.length === 0 ? (
                <div className="rounded-[24px] border border-dashed border-white/10 bg-white/[0.02] px-4 py-8 text-sm leading-relaxed text-gray-400">
                  There are no reviewed ATLAS results yet. Run a request first, then come back to Apply once you have structured output to route into the draft.
                </div>
              ) : (
                <>
                  <div className="rounded-[24px] border border-white/10 bg-black/20 p-4">
                    <div className="flex items-start justify-between gap-3">
                      <div>
                        <div className="text-xs uppercase tracking-[0.18em] text-gray-500">Apply Selection</div>
                        <div className="mt-1 text-sm text-white">Choose the exact draft mutation path instead of applying directly from the review stack.</div>
                      </div>
                      <div className="rounded-full border border-white/10 bg-white/[0.03] px-3 py-1 text-[11px] text-gray-400">{atlasSelectedOptionIds.length > 0 ? `${atlasSelectedOptionIds.length} selected` : "Using first result"}</div>
                    </div>
                    <div className="mt-4 flex flex-wrap gap-2 text-[11px] text-gray-400">
                      {(atlasSelectedOptions.length > 0 ? atlasSelectedOptions : atlasResultOptions.slice(0, 1)).map((option) => (
                        <span key={`apply-${option.id}`} className="rounded-full border border-white/10 bg-white/[0.04] px-3 py-1">{option.label}</span>
                      ))}
                    </div>
                    <div className="mt-4 grid gap-2 sm:grid-cols-2">
                      {atlasApplyActions.map((action) => (
                        <button
                          key={action.mode}
                          onClick={() => setAtlasPreviewApplyMode(action.mode)}
                          className={`rounded-xl border px-3 py-3 text-left text-sm transition ${atlasActiveApplyMode === action.mode ? "border-cyan-400/30 bg-cyan-500/10 text-cyan-100" : "border-white/10 bg-white/[0.03] text-white hover:bg-white/[0.06]"}`}
                        >
                          {action.label}
                        </button>
                      ))}
                    </div>
                  </div>

                  {atlasApplyPreview ? (
                    <div className="rounded-[24px] border border-cyan-400/20 bg-[radial-gradient(circle_at_top_left,rgba(92,244,255,0.12),transparent_35%),linear-gradient(180deg,rgba(255,255,255,0.04),rgba(255,255,255,0.02))] p-4">
                      <div className="flex items-start justify-between gap-3">
                        <div>
                          <div className="text-xs uppercase tracking-[0.18em] text-cyan-100/80">Change Preview</div>
                          <div className="mt-2 text-lg font-medium text-white">{atlasApplyPreview.title}</div>
                          <div className="mt-2 text-sm leading-relaxed text-cyan-50/80">{atlasApplyPreview.description}</div>
                        </div>
                        <button onClick={() => setAtlasWorkflowStage("review")} className="rounded-2xl border border-white/10 bg-black/20 px-4 py-2.5 text-sm text-white hover:bg-black/35">Back to review</button>
                      </div>
                      <div className="mt-4 flex flex-wrap gap-2 text-[11px] text-cyan-50/80">
                        {atlasApplyPreview.targets.map((target) => (
                          <span key={`target-${target}`} className="rounded-full border border-white/10 bg-black/20 px-3 py-1">{target}</span>
                        ))}
                      </div>
                      <div className="mt-4 rounded-[20px] border border-white/10 bg-black/20 p-4">
                        <div className="text-[11px] uppercase tracking-[0.18em] text-gray-500">Previewed payload</div>
                        <div className="mt-3 whitespace-pre-wrap text-sm leading-relaxed text-white/85">{atlasApplyPreview.previewText || "No textual preview is available for this route, but the structured draft mutation target is ready."}</div>
                      </div>
                      <div className="mt-4 grid grid-cols-3 gap-2">
                        <button onClick={() => atlasActiveApplyMode && applyStructuredOptions(atlasActiveApplyMode)} className="rounded-2xl bg-gradient-to-r from-cyan-500 to-blue-600 px-4 py-3 text-sm font-medium text-[#06121e]">Apply to draft</button>
                        <button onClick={() => setAtlasWorkflowStage("review")} className="rounded-2xl border border-white/10 bg-white/[0.03] px-4 py-3 text-sm text-white hover:bg-white/[0.06]">Review more</button>
                        <button onClick={() => activeAtlasBrief && updateStructuredBrief(activeAtlasBrief.id, (brief) => ({ ...brief, selectedResultIds: [] }))} className="rounded-2xl border border-white/10 bg-white/[0.03] px-4 py-3 text-sm text-white hover:bg-white/[0.06]">Clear selection</button>
                      </div>
                    </div>
                  ) : null}
                </>
              )}
            </>
          ) : null}
        </div>
      </aside>
    );
  }

  if (!draft) {
    return (
      <div className="h-full bg-[#0a0a0a] text-white p-8 flex items-center justify-center overflow-y-auto">
        <div className="max-w-6xl w-full rounded-[36px] border border-white/10 bg-[radial-gradient(circle_at_top_left,rgba(56,189,248,0.16),transparent_38%),radial-gradient(circle_at_bottom_right,rgba(168,85,247,0.12),transparent_32%),linear-gradient(180deg,#12151f_0%,#090b12_100%)] p-10">
          <div className="flex flex-wrap items-start justify-between gap-8">
            <div className="max-w-3xl">
              <div className="inline-flex items-center gap-2 rounded-full border border-cyan-400/20 bg-cyan-400/10 px-3 py-1 text-xs uppercase tracking-[0.2em] text-cyan-200">
                Create Studio
              </div>
              <h1 className="mt-5 text-5xl font-semibold leading-tight">Start a serious working draft.</h1>
              <p className="mt-4 text-lg leading-relaxed text-gray-300">
                Create Studio is the main production surface for posts, stories, reels, shorts, carousels, thumbnails, promo banners, and ad creative.
                Choose a preset and ATLAS will keep the draft, scenes, layers, assets, timeline, and AI requests in one real working file.
              </p>
            </div>

            <div className="min-w-[280px] rounded-[28px] border border-white/10 bg-black/20 p-6">
              <div className="text-sm uppercase tracking-[0.2em] text-gray-500">What opens next</div>
              <div className="mt-4 space-y-3 text-sm text-gray-300">
                <div className="rounded-2xl border border-white/10 bg-white/[0.03] px-4 py-3">Canvas-first editor with scene navigation and snapping</div>
                <div className="rounded-2xl border border-white/10 bg-white/[0.03] px-4 py-3">ATLAS packet workflow for ideas, captions, scripts, and voiceovers</div>
                <div className="rounded-2xl border border-white/10 bg-white/[0.03] px-4 py-3">Direct asset insertion from the workspace library and platform preview modes</div>
              </div>
            </div>
          </div>

          <div className="mt-10 grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            {FORMAT_PRESETS.slice(0, 8).map((preset) => (
              <button
                key={preset.id}
                onClick={() => {
                  const nextDraft = normalizeDraft(createDraftFromPreset(preset.id));
                  saveDraft(nextDraft);
                  selectDraft(nextDraft.id);
                  setWorkingDraft(nextDraft);
                  setSelectedPresetId(preset.id);
                  setPreviewPlatform(nextDraft.linkedPlatformIds[0] ?? "instagram");
                }}
                className="rounded-[26px] border border-white/10 bg-white/[0.03] p-5 text-left transition hover:border-cyan-400/30 hover:bg-white/[0.05]"
              >
                <div className="text-xs uppercase tracking-[0.2em] text-cyan-200">{preset.contentType}</div>
                <div className="mt-2 text-lg font-medium text-white">{preset.label}</div>
                <div className="mt-2 text-sm text-gray-400">{preset.width} x {preset.height}</div>
                <div className="mt-4 text-xs text-gray-500">{preset.motion ? "Motion-ready" : "Static-first"} · {preset.exportFormats.join(" / ")}</div>
              </button>
            ))}
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="h-full bg-[#0a0d14] text-white overflow-hidden">
      <input
        ref={fileInputRef}
        type="file"
        multiple
        className="hidden"
        accept="image/*,video/*,audio/*"
        onChange={(event) => void uploadAssets(event.target.files)}
      />

      <div className="h-full grid grid-rows-[auto_1fr_auto]">
        <div className="border-b border-white/10 bg-[linear-gradient(180deg,rgba(13,16,23,0.96),rgba(10,13,20,0.92))] backdrop-blur px-6 py-4">
          <div className="flex flex-wrap items-start justify-between gap-5">
            <div className="min-w-0 flex-1">
              <div className="flex flex-wrap items-center gap-3">
                <h1 className="text-2xl font-semibold">Create Studio</h1>
                <span className="rounded-full border border-cyan-400/20 bg-cyan-400/10 px-3 py-1 text-xs uppercase tracking-[0.2em] text-cyan-200">{draft.contentType}</span>
                <span className="rounded-full border border-white/10 px-3 py-1 text-xs text-gray-300">{draft.status}</span>
                <span className="rounded-full border border-white/10 px-3 py-1 text-xs text-gray-300">{scenes.length} scenes</span>
              </div>
              <div className="mt-2 text-sm text-gray-400">{draft.title} · {draft.width} x {draft.height} · {visibleLayers.length} active layer{visibleLayers.length === 1 ? "" : "s"}</div>
              <div className="mt-4 flex flex-wrap items-center gap-3">
                <select value={draft.formatId} onChange={(event) => applyPreset(event.target.value)} className="rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-2.5 text-sm text-white">
                  {FORMAT_PRESETS.map((preset) => (
                    <option key={preset.id} value={preset.id}>{preset.label}</option>
                  ))}
                </select>
                <select value={previewPlatform} onChange={(event) => setPreviewPlatform(event.target.value as PlatformId)} className="rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-2.5 text-sm text-white">
                  {previewPlatforms.map((platformId) => {
                    const platform = PLATFORM_DEFINITIONS.find((item) => item.id === platformId);
                    return <option key={platformId} value={platformId}>{platform?.label ?? platformId}</option>;
                  })}
                </select>
                <button onClick={() => setPreviewMode((current) => !current)} className="inline-flex items-center gap-2 rounded-2xl border border-white/10 bg-white/[0.03] px-4 py-2.5 text-sm text-gray-200 hover:bg-white/[0.06]">
                  <MonitorUp className="h-4 w-4" />
                  {previewMode ? "Exit Preview" : "Preview"}
                </button>
              </div>
            </div>

            <div className="flex flex-wrap items-center gap-2">
              <button onClick={handleUndo} className="rounded-2xl border border-white/10 bg-white/[0.03] p-3 text-gray-300 transition hover:bg-white/[0.06] disabled:opacity-40" disabled={history.past.length === 0}><Undo2 className="h-4 w-4" /></button>
              <button onClick={handleRedo} className="rounded-2xl border border-white/10 bg-white/[0.03] p-3 text-gray-300 transition hover:bg-white/[0.06] disabled:opacity-40" disabled={history.future.length === 0}><Redo2 className="h-4 w-4" /></button>
              <button onClick={() => persistDraft(draft)} className="inline-flex items-center gap-2 rounded-2xl border border-white/10 bg-white/[0.03] px-4 py-3 text-sm text-gray-200 transition hover:bg-white/[0.06]"><Save className="h-4 w-4" />Save</button>
              <button onClick={() => navigate("/export")} className="inline-flex items-center gap-2 rounded-2xl bg-gradient-to-r from-cyan-500 to-blue-500 px-4 py-3 text-sm font-medium text-[#06121e] transition hover:brightness-110"><Download className="h-4 w-4" />Export</button>
            </div>
          </div>
        </div>

        <div className={`grid min-h-0 ${previewMode ? "grid-cols-[1fr]" : selectedLayer ? "grid-cols-[72px_320px_1fr_340px]" : activeFlyout === "closed" ? "grid-cols-[72px_1fr]" : "grid-cols-[72px_320px_1fr]"}`}>
          {!previewMode ? (
            <aside className="border-r border-white/10 bg-[#0b0f16] px-2 py-4">
              <div className="flex h-full flex-col items-center gap-2">
                {railButtons.map((item) => {
                  const Icon = item.icon;
                  const active = activeFlyout === item.id;
                  return (
                    <button
                      key={item.id}
                      onClick={() => setActiveFlyout((current) => current === item.id ? "closed" : item.id)}
                      className={`w-full rounded-2xl border px-2 py-3 text-center transition ${active ? "border-cyan-400/30 bg-cyan-500/10 text-cyan-200" : "border-white/10 bg-white/[0.02] text-gray-400 hover:text-white"}`}
                    >
                      <Icon className="mx-auto h-5 w-5" />
                      <div className="mt-2 text-[10px] uppercase tracking-[0.22em]">{item.label}</div>
                    </button>
                  );
                })}
                <div className="mt-auto w-full rounded-2xl border border-white/10 bg-cyan-500/10 px-2 py-3 text-center text-[10px] uppercase tracking-[0.22em] text-cyan-100">
                  {previewPlatform}
                </div>
              </div>
            </aside>
          ) : null}

          {!previewMode ? renderFlyout() : null}

          <main className="min-h-0 bg-[radial-gradient(circle_at_top,rgba(34,211,238,0.08),transparent_26%),linear-gradient(180deg,#090c13_0%,#0b0f16_100%)]">
            <div className="h-full grid grid-rows-[auto_1fr]">
              <div className="border-b border-white/5 px-6 py-3">
                <div className="flex flex-wrap items-center justify-between gap-3 text-xs uppercase tracking-[0.2em] text-gray-500">
                  <div className="flex items-center gap-2">
                    <Grid3x3 className="h-4 w-4" />
                    Canvas Focus
                  </div>
                  <div className="flex flex-wrap items-center gap-2 text-[11px]">
                    {draft.linkedPlatformIds.length === 0 ? (
                      <span className="rounded-full border border-white/10 px-3 py-1 text-gray-400">No destinations attached yet</span>
                    ) : (
                      draft.linkedPlatformIds.map((platformId) => {
                        const platform = PLATFORM_DEFINITIONS.find((item) => item.id === platformId);
                        return <span key={platformId} className={`rounded-full border px-3 py-1 ${previewPlatform === platformId ? "border-cyan-400/30 bg-cyan-500/10 text-cyan-100" : "border-white/10 text-gray-300"}`}>{platform?.label ?? platformId}</span>;
                      })
                    )}
                  </div>
                </div>
              </div>

              <div className="min-h-0 overflow-auto px-6 py-6" onPointerMove={handleCanvasPointerMove} onPointerUp={stopDrag}>
                <div className="flex min-h-full items-center justify-center">
                  <div className="relative flex items-center justify-center rounded-[36px] border border-white/10 bg-[#080b12] p-8 shadow-[0_40px_120px_rgba(0,0,0,0.52)]">
                    <div
                      className="relative overflow-hidden rounded-[28px] border border-white/10 shadow-[0_40px_100px_rgba(6,18,30,0.45)]"
                      style={{ width: draft.width * scale, height: draft.height * scale }}
                    >
                      <div className="absolute inset-0" style={{ ...sceneBackgroundStyle(activeScene?.background ?? draft.background), backgroundSize: "cover", backgroundPosition: "center" }} />
                      <div className="absolute inset-0 opacity-20" style={{ backgroundImage: "linear-gradient(rgba(255,255,255,0.08) 1px, transparent 1px), linear-gradient(90deg, rgba(255,255,255,0.08) 1px, transparent 1px)", backgroundSize: `${48 * scale}px ${48 * scale}px` }} />

                      {canvasGuides.map((guide, index) => (
                        <div
                          key={`${guide.orientation}-${index}`}
                          className="absolute bg-cyan-300/80 pointer-events-none"
                          style={guide.orientation === "vertical"
                            ? { left: guide.position * scale, top: 0, bottom: 0, width: 1 }
                            : { top: guide.position * scale, left: 0, right: 0, height: 1 }}
                        />
                      ))}

                      {visibleLayers.map((layer) => {
                        const asset = findAsset(draft, assets, layer);
                        const isSelected = selectedLayer?.id === layer.id;
                        const hidden = !layer.visible;
                        return (
                          <div
                            key={layer.id}
                            onPointerDown={(event) => startDrag(layer, event)}
                            onClick={() => setSelectedLayerId(layer.id)}
                            className={`absolute overflow-hidden transition ${hidden ? "opacity-40" : ""} ${isSelected ? "ring-2 ring-cyan-300" : "ring-1 ring-white/10"}`}
                            style={{
                              left: layer.x * scale,
                              top: layer.y * scale,
                              width: Math.max(20, layer.width * scale),
                              height: Math.max(20, layer.height * scale),
                              opacity: layer.opacity,
                              transform: `rotate(${layer.rotation}deg)`,
                              borderRadius: (layer.cornerRadius ?? 0) * scale,
                              mixBlendMode: layer.blendMode,
                              background: layer.gradient || (layer.type === "shape" || layer.type === "overlay" ? layer.color ?? "rgba(56,189,248,0.3)" : "transparent"),
                              border: layer.type === "audio" ? "1px solid rgba(255,255,255,0.18)" : undefined,
                            }}
                          >
                            {layer.type === "text" || layer.type === "sticker" ? (
                              <div
                                className="flex h-full w-full items-center px-4"
                                style={{
                                  color: layer.color ?? "#ffffff",
                                  fontSize: Math.max(14, (layer.fontSize ?? 44) * scale),
                                  fontWeight: layer.fontWeight ?? 700,
                                  letterSpacing: layer.animation === "type-on" ? `${1.2 * scale}px` : undefined,
                                  textTransform: layer.type === "sticker" ? "uppercase" : undefined,
                                }}
                              >
                                {layer.text || "Text"}
                              </div>
                            ) : null}

                            {(layer.type === "asset" || layer.type === "video") && asset ? (
                              asset.dataUrl ? (
                                asset.kind === "video" ? (
                                  <video src={asset.dataUrl} className="h-full w-full object-cover" muted playsInline loop autoPlay />
                                ) : (
                                  <img src={asset.dataUrl} alt={asset.name} className="h-full w-full object-cover" style={{ filter: layer.filter === "none" ? "none" : `saturate(${layer.filter === "vivid" ? 1.35 : layer.filter === "mono" ? 0 : 1}) brightness(${layer.filter === "dramatic" ? 0.8 : 1})` }} />
                                )
                              ) : (
                                <div className="flex h-full w-full items-center justify-center bg-black/45 px-5 text-center text-xs leading-relaxed text-gray-200">
                                  {assetPreviewFallback(asset)}
                                </div>
                              )
                            ) : null}

                            {layer.type === "audio" ? (
                              <div className="flex h-full items-center justify-between gap-3 px-4 text-xs text-white/85">
                                <div>
                                  <div className="font-medium">{asset?.name ?? "Audio track"}</div>
                                  <div className="mt-1 text-[11px] text-white/50">Trim {formatDuration((layer.endMs ?? activeScene?.durationMs ?? 4000) - (layer.startMs ?? 0))}</div>
                                </div>
                                <Music className="h-4 w-4 text-cyan-200" />
                              </div>
                            ) : null}

                            {layer.type === "overlay" ? <div className="absolute inset-0 bg-black/10" /> : null}
                          </div>
                        );
                      })}
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </main>

          {!previewMode && selectedLayer ? (
            <aside className="min-h-0 border-l border-white/10 bg-[#0d1017] grid grid-rows-[auto_1fr]">
              <div className="border-b border-white/10 px-5 py-4">
                <div className="flex items-center justify-between gap-3">
                  <div>
                    <div className="text-sm font-medium text-white">Contextual Properties</div>
                    <div className="mt-1 text-xs text-gray-400">Only the active scene and selected layer stay expanded while you edit.</div>
                  </div>
                  <button onClick={() => setSelectedLayerId(undefined)} className="rounded-xl border border-white/10 bg-white/[0.03] px-3 py-2 text-xs text-gray-200 hover:bg-white/[0.06]">Hide</button>
                </div>
              </div>

              <div className="min-h-0 overflow-y-auto px-5 py-5 space-y-5">
                {activeScene ? (
                  <section className="rounded-[24px] border border-white/10 bg-black/20 p-4">
                    <div className="text-xs uppercase tracking-[0.18em] text-gray-500">Scene</div>
                    <div className="mt-3 grid gap-3">
                      <label>
                        <div className="mb-2 text-xs uppercase tracking-[0.18em] text-gray-500">Transition</div>
                        <select value={activeScene.transition} onChange={(event) => updateScene(activeScene.id, (scene) => ({ ...scene, transition: event.target.value as DraftScene["transition"] }))} className="w-full rounded-xl border border-white/10 bg-black/25 px-3 py-2 text-white">
                          {TRANSITIONS.map((transition) => <option key={transition} value={transition}>{transition}</option>)}
                        </select>
                      </label>
                      <div>
                        <div className="mb-2 text-xs uppercase tracking-[0.18em] text-gray-500">Background</div>
                        <div className="grid grid-cols-4 gap-2">
                          {BACKGROUND_SWATCHES.map((swatch) => (
                            <button key={swatch} onClick={() => updateScene(activeScene.id, (scene) => ({ ...scene, background: swatch }))} className={`h-12 rounded-xl border ${activeScene.background === swatch ? "border-cyan-300" : "border-white/10"}`} style={sceneBackgroundStyle(swatch)} />
                          ))}
                        </div>
                      </div>
                    </div>
                  </section>
                ) : null}

                <section className="rounded-[24px] border border-white/10 bg-black/20 p-4">
                  <div className="flex items-center justify-between gap-3 mb-4">
                    <div>
                      <div className="text-sm font-medium text-white">{layerLabel(selectedLayer)}</div>
                      <div className="mt-1 text-xs text-gray-400">{selectedLayer.type} · {Math.round(selectedLayer.width)} x {Math.round(selectedLayer.height)}</div>
                    </div>
                    <div className="flex items-center gap-2 text-gray-400">
                      <button onClick={() => updateLayer(selectedLayer.id, (item) => ({ ...item, visible: !item.visible }))}>{selectedLayer.visible ? <Eye className="h-4 w-4" /> : <EyeOff className="h-4 w-4" />}</button>
                      <button onClick={() => updateLayer(selectedLayer.id, (item) => ({ ...item, locked: !item.locked }))}>{selectedLayer.locked ? <Lock className="h-4 w-4" /> : <Unlock className="h-4 w-4" />}</button>
                    </div>
                  </div>

                  <div className="space-y-4 text-sm">
                    {(selectedLayer.type === "text" || selectedLayer.type === "sticker") ? (
                      <label className="block">
                        <div className="mb-2 text-xs uppercase tracking-[0.18em] text-gray-500">Text</div>
                        <textarea value={selectedLayer.text ?? ""} onChange={(event) => updateLayer(selectedLayer.id, (layer) => ({ ...layer, text: event.target.value }))} rows={3} className="w-full rounded-xl border border-white/10 bg-black/25 px-3 py-2 text-white" />
                      </label>
                    ) : null}

                    <div className="grid grid-cols-2 gap-3">
                      <label>
                        <div className="mb-2 text-xs uppercase tracking-[0.18em] text-gray-500">X</div>
                        <input type="number" value={Math.round(selectedLayer.x)} onChange={(event) => updateLayer(selectedLayer.id, (layer) => ({ ...layer, x: Number(event.target.value) }))} className="w-full rounded-xl border border-white/10 bg-black/25 px-3 py-2 text-white" />
                      </label>
                      <label>
                        <div className="mb-2 text-xs uppercase tracking-[0.18em] text-gray-500">Y</div>
                        <input type="number" value={Math.round(selectedLayer.y)} onChange={(event) => updateLayer(selectedLayer.id, (layer) => ({ ...layer, y: Number(event.target.value) }))} className="w-full rounded-xl border border-white/10 bg-black/25 px-3 py-2 text-white" />
                      </label>
                      <label>
                        <div className="mb-2 text-xs uppercase tracking-[0.18em] text-gray-500">Width</div>
                        <input type="number" value={Math.round(selectedLayer.width)} onChange={(event) => updateLayer(selectedLayer.id, (layer) => ({ ...layer, width: Math.max(40, Number(event.target.value)) }))} className="w-full rounded-xl border border-white/10 bg-black/25 px-3 py-2 text-white" />
                      </label>
                      <label>
                        <div className="mb-2 text-xs uppercase tracking-[0.18em] text-gray-500">Height</div>
                        <input type="number" value={Math.round(selectedLayer.height)} onChange={(event) => updateLayer(selectedLayer.id, (layer) => ({ ...layer, height: Math.max(24, Number(event.target.value)) }))} className="w-full rounded-xl border border-white/10 bg-black/25 px-3 py-2 text-white" />
                      </label>
                    </div>

                    <div className="grid grid-cols-2 gap-3">
                      <label>
                        <div className="mb-2 text-xs uppercase tracking-[0.18em] text-gray-500">Opacity</div>
                        <input type="range" min="0" max="1" step="0.01" value={selectedLayer.opacity} onChange={(event) => updateLayer(selectedLayer.id, (layer) => ({ ...layer, opacity: Number(event.target.value) }))} className="w-full" />
                      </label>
                      <label>
                        <div className="mb-2 text-xs uppercase tracking-[0.18em] text-gray-500">Blend</div>
                        <select value={selectedLayer.blendMode ?? "normal"} onChange={(event) => updateLayer(selectedLayer.id, (layer) => ({ ...layer, blendMode: event.target.value as BlendMode }))} className="w-full rounded-xl border border-white/10 bg-black/25 px-3 py-2 text-white">
                          {BLEND_MODES.map((mode) => <option key={mode} value={mode}>{mode}</option>)}
                        </select>
                      </label>
                    </div>

                    <div className="grid grid-cols-2 gap-3">
                      <label>
                        <div className="mb-2 text-xs uppercase tracking-[0.18em] text-gray-500">Filter</div>
                        <select value={selectedLayer.filter ?? "none"} onChange={(event) => updateLayer(selectedLayer.id, (layer) => ({ ...layer, filter: event.target.value as DraftLayer["filter"] }))} className="w-full rounded-xl border border-white/10 bg-black/25 px-3 py-2 text-white">
                          {FILTERS.map((filter) => <option key={filter} value={filter}>{filter}</option>)}
                        </select>
                      </label>
                      <label>
                        <div className="mb-2 text-xs uppercase tracking-[0.18em] text-gray-500">Animation</div>
                        <select value={selectedLayer.animation ?? "none"} onChange={(event) => updateLayer(selectedLayer.id, (layer) => ({ ...layer, animation: event.target.value as DraftLayer["animation"] }))} className="w-full rounded-xl border border-white/10 bg-black/25 px-3 py-2 text-white">
                          {ANIMATIONS.map((animation) => <option key={animation} value={animation}>{animation}</option>)}
                        </select>
                      </label>
                    </div>

                    <div className="grid grid-cols-2 gap-3">
                      <label>
                        <div className="mb-2 text-xs uppercase tracking-[0.18em] text-gray-500">Trim Start</div>
                        <input type="number" value={selectedLayer.startMs ?? 0} onChange={(event) => updateLayer(selectedLayer.id, (layer) => ({ ...layer, startMs: Number(event.target.value) }))} className="w-full rounded-xl border border-white/10 bg-black/25 px-3 py-2 text-white" />
                      </label>
                      <label>
                        <div className="mb-2 text-xs uppercase tracking-[0.18em] text-gray-500">Trim End</div>
                        <input type="number" value={selectedLayer.endMs ?? activeScene?.durationMs ?? draft.durationMs} onChange={(event) => updateLayer(selectedLayer.id, (layer) => ({ ...layer, endMs: Number(event.target.value) }))} className="w-full rounded-xl border border-white/10 bg-black/25 px-3 py-2 text-white" />
                      </label>
                    </div>

                    <div className="flex flex-wrap gap-2">
                      <button onClick={() => updateLayer(selectedLayer.id, (layer) => ({ ...layer, x: Math.round((draft.width - layer.width) / 2) }))} className="inline-flex items-center gap-2 rounded-xl border border-white/10 bg-white/[0.03] px-3 py-2 text-xs text-gray-200 hover:bg-white/[0.06]"><AlignCenter className="h-4 w-4" />Center X</button>
                      <button onClick={() => updateLayer(selectedLayer.id, (layer) => ({ ...layer, y: Math.round((draft.height - layer.height) / 2) }))} className="inline-flex items-center gap-2 rounded-xl border border-white/10 bg-white/[0.03] px-3 py-2 text-xs text-gray-200 hover:bg-white/[0.06]"><AlignCenter className="h-4 w-4" />Center Y</button>
                      <button onClick={() => duplicateLayer(selectedLayer.id)} className="inline-flex items-center gap-2 rounded-xl border border-white/10 bg-white/[0.03] px-3 py-2 text-xs text-gray-200 hover:bg-white/[0.06]"><Copy className="h-4 w-4" />Duplicate</button>
                      <button onClick={() => removeLayer(selectedLayer.id)} className="inline-flex items-center gap-2 rounded-xl border border-red-500/20 bg-red-500/10 px-3 py-2 text-xs text-red-100 hover:bg-red-500/16"><Trash2 className="h-4 w-4" />Delete</button>
                    </div>
                  </div>
                </section>
              </div>
            </aside>
          ) : null}
        </div>

        <div className="border-t border-white/10 bg-[#0c1016] px-6 py-4">
          <div className="grid gap-4 lg:grid-cols-[280px_1fr_280px]">
            <div className="rounded-[24px] border border-white/10 bg-white/[0.02] p-4">
              <div className="text-xs uppercase tracking-[0.2em] text-gray-500 mb-3">Scene Navigator</div>
              <div className="space-y-2 max-h-40 overflow-y-auto">
                {scenes.map((scene, index) => (
                  <button key={scene.id} onClick={() => mutateDraft((current) => ({ ...current, activeSceneId: scene.id }), { history: false })} className={`w-full rounded-xl px-3 py-2 text-left transition ${scene.id === draft.activeSceneId ? "bg-cyan-400/12 text-cyan-100" : "bg-white/[0.02] text-gray-300 hover:bg-white/[0.05]"}`}>
                    <div className="flex items-center justify-between">
                      <span>{index + 1}. {scene.name}</span>
                      <span className="text-xs text-gray-400">{scene.transition}</span>
                    </div>
                    <div className="mt-1 text-xs text-gray-500">{formatDuration(scene.durationMs)}</div>
                  </button>
                ))}
              </div>
            </div>

            <div className="rounded-[24px] border border-white/10 bg-white/[0.02] p-4">
              <div className="flex items-center justify-between gap-3 mb-3">
                <div>
                  <div className="text-sm font-medium text-white">Layer Timing</div>
                  <div className="mt-1 text-xs text-gray-400">Trim windows and scene coverage for the active composition.</div>
                </div>
                {activeScene ? (
                  <div className="flex items-center gap-2 text-xs text-gray-400">
                    <button onClick={() => updateScene(activeScene.id, (scene) => ({ ...scene, durationMs: Math.max(2000, scene.durationMs - 1000) }))} className="rounded-lg border border-white/10 px-3 py-1.5 hover:text-white">-1s</button>
                    <button onClick={() => updateScene(activeScene.id, (scene) => ({ ...scene, durationMs: scene.durationMs + 1000 }))} className="rounded-lg border border-white/10 px-3 py-1.5 hover:text-white">+1s</button>
                  </div>
                ) : null}
              </div>
              <div className="grid gap-3">
                {visibleLayers.map((layer) => {
                  const total = Math.max(activeScene?.durationMs ?? draft.durationMs, 1);
                  const start = ((layer.startMs ?? 0) / total) * 100;
                  const end = ((layer.endMs ?? total) / total) * 100;
                  return (
                    <div key={layer.id} className="grid grid-cols-[190px_1fr] items-center gap-3">
                      <button onClick={() => setSelectedLayerId(layer.id)} className="truncate text-left text-sm text-gray-300 hover:text-white">{layerLabel(layer)}</button>
                      <div className="relative h-8 rounded-full bg-black/35">
                        <div className="absolute inset-y-1 rounded-full bg-gradient-to-r from-cyan-400/80 to-blue-500/80" style={{ left: `${start}%`, width: `${Math.max(10, end - start)}%` }} />
                      </div>
                    </div>
                  );
                })}
              </div>
            </div>

            <div className="rounded-[24px] border border-cyan-400/20 bg-cyan-500/10 p-4 text-sm leading-relaxed text-cyan-100">
              <div className="flex items-center gap-2 font-medium mb-2"><Sparkles className="h-4 w-4" />Studio Status</div>
              <div>{atlasStatus}</div>
              <div className="mt-3 text-xs text-cyan-50/80">Stored AI requests: {aiBriefs.length}</div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
