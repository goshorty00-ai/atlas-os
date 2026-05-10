import type {
  AICaptionResultSchema,
  AICarouselResultSchema,
  AIHookResultSchema,
  AIResultGroup,
  AIResultOption,
  AIResultOptionKind,
  AIResultSchema,
  AIResultSection,
  AIPlatformAdaptationResultSchema,
  AISurface,
  AITaskType,
  AIScriptResultSchema,
  AIVariantPackResultSchema,
  AIVisualConceptResultSchema,
  AIVoiceoverResultSchema,
  ModelProviderId,
  PlatformId,
  StudioDraft,
} from "../state/studioStore";

export interface ProviderModelDefinition {
  id: string;
  label: string;
  providerId: ModelProviderId;
  bestFor: string[];
}

export interface ProviderDefinition {
  id: ModelProviderId;
  label: string;
  summary: string;
  bestFor: string[];
  models: ProviderModelDefinition[];
}

export interface TaskDefinition {
  id: AITaskType;
  label: string;
  description: string;
  recommendedProviders: ModelProviderId[];
  defaultSurface: AISurface;
}

export const AI_PROVIDERS: ProviderDefinition[] = [
  {
    id: "gpt",
    label: "GPT",
    summary: "Reasoning, hooks, captions, structural rewrites, and fast creative planning.",
    bestFor: ["Captions", "Hooks", "Campaign planning", "Rewrites"],
    models: [
      { id: "gpt-5.4", label: "GPT-5.4", providerId: "gpt", bestFor: ["Reasoning", "Caption systems", "Content planning"] },
      { id: "gpt-5.4-mini", label: "GPT-5.4 Mini", providerId: "gpt", bestFor: ["Fast variants", "Short-form ideation"] },
    ],
  },
  {
    id: "claude",
    label: "Claude",
    summary: "Long-form strategy, script refinement, brand voice calibration, and structured campaign writing.",
    bestFor: ["Scripts", "Strategy", "Brand voice", "Long-form copy"],
    models: [
      { id: "claude-sonnet-4.5", label: "Claude Sonnet 4.5", providerId: "claude", bestFor: ["Scripts", "Strategy", "Campaign packs"] },
      { id: "claude-haiku-4.5", label: "Claude Haiku 4.5", providerId: "claude", bestFor: ["Quick rewrites", "Fast planning passes"] },
    ],
  },
  {
    id: "gemini",
    label: "Gemini",
    summary: "Multimodal scene understanding, visual concepts, image-aware suggestions, and asset-based idea expansion.",
    bestFor: ["Visual concepts", "Scene suggestions", "Asset-aware ideation"],
    models: [
      { id: "gemini-2.5-pro", label: "Gemini 2.5 Pro", providerId: "gemini", bestFor: ["Visual reasoning", "Concept generation", "Multimodal review"] },
      { id: "gemini-2.5-flash", label: "Gemini 2.5 Flash", providerId: "gemini", bestFor: ["Fast visual variants", "Rapid ideation"] },
    ],
  },
  {
    id: "elevenlabs",
    label: "ElevenLabs",
    summary: "Premium voice generation, narration direction, pacing control, and voiceover production.",
    bestFor: ["Voiceovers", "Narration", "Character reads"],
    models: [
      { id: "eleven_multilingual_v2", label: "Eleven Multilingual v2", providerId: "elevenlabs", bestFor: ["Brand voiceover", "Narration"] },
      { id: "eleven_turbo_v2_5", label: "Eleven Turbo v2.5", providerId: "elevenlabs", bestFor: ["Fast previews", "Iteration"] },
    ],
  },
];

export const AI_TASKS: TaskDefinition[] = [
  { id: "caption", label: "Caption Writing", description: "Platform-ready captions with hooks, CTA, and brand framing.", recommendedProviders: ["gpt", "claude"], defaultSurface: "create" },
  { id: "ideas", label: "Content Ideas", description: "Idea generation across campaigns, niches, and content pillars.", recommendedProviders: ["gpt", "claude", "gemini"], defaultSurface: "planner" },
  { id: "hook", label: "Hook Generation", description: "Thumb-stopping opening lines, first-frame copy, and headline angles.", recommendedProviders: ["gpt", "claude"], defaultSurface: "create" },
  { id: "script", label: "Script Writing", description: "Talking-head scripts, promo scripts, and structured video beats.", recommendedProviders: ["claude", "gpt"], defaultSurface: "video" },
  { id: "visual-concept", label: "Visual Concept", description: "Scene treatments, visual directions, and art direction packets.", recommendedProviders: ["gemini", "gpt"], defaultSurface: "create" },
  { id: "carousel-structure", label: "Carousel Structure", description: "Slide-by-slide structure for educational, ad, and authority carousels.", recommendedProviders: ["gpt", "claude"], defaultSurface: "create" },
  { id: "ad-copy", label: "Ad Copy", description: "Paid social headlines, hooks, CTAs, and conversion framing.", recommendedProviders: ["gpt", "claude"], defaultSurface: "create" },
  { id: "thumbnail-brainstorm", label: "Thumbnail Brainstorm", description: "Headline angles, thumbnail concepts, and contrast ideas.", recommendedProviders: ["gpt", "gemini"], defaultSurface: "create" },
  { id: "rewrite-brand-tone", label: "Rewrite In Brand Tone", description: "Rewrites existing copy against brand voice and audience rules.", recommendedProviders: ["claude", "gpt"], defaultSurface: "create" },
  { id: "platform-adaptation", label: "Platform Adaptation", description: "Adapt one concept for different formats, surfaces, and platform expectations.", recommendedProviders: ["gpt", "claude", "gemini"], defaultSurface: "create" },
  { id: "voiceover", label: "Voiceover Generation", description: "Narration, pacing, style direction, and voice packet preparation.", recommendedProviders: ["elevenlabs"], defaultSurface: "voice" },
  { id: "scene-suggestion", label: "Scene Suggestion", description: "Suggest scenes from uploaded assets and current draft structure.", recommendedProviders: ["gemini", "gpt"], defaultSurface: "video" },
  { id: "campaign-pack", label: "Campaign Pack", description: "Turn one brief into platform variations and a publishable content system.", recommendedProviders: ["claude", "gpt", "gemini"], defaultSurface: "planner" },
];

export function modelsForProvider(providerId: ModelProviderId) {
  return AI_PROVIDERS.find((provider) => provider.id === providerId)?.models ?? [];
}

export function recommendedProvidersForTask(taskType: AITaskType) {
  return AI_TASKS.find((task) => task.id === taskType)?.recommendedProviders ?? ["gpt"];
}

export function buildAtlasPacket(input: {
  providerId: ModelProviderId;
  modelId: string;
  taskType: AITaskType;
  tone: string;
  platformId: PlatformId;
  objective: string;
  variants: number;
  targetSurface: AISurface;
  brief: string;
  brandName?: string;
  brandTone?: string;
  audience?: string;
  accountsCount: number;
  assetsCount: number;
  draft?: StudioDraft;
  sceneName?: string;
  selectedLayerText?: string;
  linkedPlatforms?: PlatformId[];
  linkedAssetsCount?: number;
}) {
  const draft = input.draft;
  const outputContract = outputContractForTask(input.taskType);
  return [
    "ATLAS_ORCHESTRATION_PACKET",
    `provider=${input.providerId}`,
    `model=${input.modelId}`,
    `task_type=${input.taskType}`,
    `platform=${input.platformId}`,
    `objective=${input.objective}`,
    `tone=${input.tone}`,
    `variants=${input.variants}`,
    `target_surface=${input.targetSurface}`,
    `brand_name=${input.brandName || "not_set"}`,
    `brand_tone=${input.brandTone || "not_set"}`,
    `audience=${input.audience || "not_set"}`,
    `accounts_count=${input.accountsCount}`,
    `assets_count=${input.assetsCount}`,
    `draft_title=${draft?.title || "none"}`,
    `draft_format=${draft ? `${draft.width}x${draft.height}` : "none"}`,
    `draft_pipeline=${draft?.pipelineStage || "none"}`,
    `draft_campaign=${draft?.campaign || "none"}`,
    `active_scene=${input.sceneName || "none"}`,
    `selected_layer_text=${input.selectedLayerText || "none"}`,
    `linked_platforms=${input.linkedPlatforms?.join(",") || draft?.linkedPlatformIds.join(",") || "none"}`,
    `linked_assets=${input.linkedAssetsCount ?? draft?.linkedAssetIds.length ?? 0}`,
    "output_contract=return valid JSON only",
    ...outputContract,
    "brief=",
    input.brief.trim() || "No brief supplied.",
  ].join("\n");
}

function outputContractForTask(taskType: AITaskType) {
  switch (taskType) {
    case "caption":
      return [
        "json_schema={\"results\":[{\"hook\":\"string\",\"body\":\"string\",\"cta\":\"string\",\"hashtags\":[\"string\"],\"toneNotes\":\"string\",\"audienceNotes\":\"string\"}]}",
      ];
    case "hook":
      return [
        "json_schema={\"results\":[{\"primaryHook\":\"string\",\"alternates\":[\"string\"],\"styleTags\":[\"string\"],\"usageNotes\":\"string\"}]}",
      ];
    case "script":
      return [
        "json_schema={\"results\":[{\"title\":\"string\",\"opening\":\"string\",\"beats\":[\"string\"],\"cta\":\"string\",\"pacingNotes\":\"string\"}]}",
      ];
    case "carousel-structure":
      return [
        "json_schema={\"results\":[{\"title\":\"string\",\"slides\":[{\"title\":\"string\",\"body\":\"string\"}],\"ctaSlide\":\"string\",\"summary\":\"string\"}]}",
      ];
    case "visual-concept":
      return [
        "json_schema={\"results\":[{\"conceptTitle\":\"string\",\"palette\":[\"string\"],\"composition\":\"string\",\"mood\":\"string\",\"references\":[\"string\"],\"shotNotes\":[\"string\"],\"platformNotes\":\"string\"}]}",
      ];
    case "platform-adaptation":
      return [
        "json_schema={\"results\":[{\"sourcePlatform\":\"string\",\"targetPlatform\":\"string\",\"adaptedBody\":\"string\",\"adaptedCta\":\"string\",\"lengthNotes\":\"string\"}]}",
      ];
    case "campaign-pack":
      return [
        "json_schema={\"results\":[{\"variants\":[{\"title\":\"string\",\"body\":\"string\",\"hook\":\"string\",\"cta\":\"string\",\"keyDifference\":\"string\"}],\"keyDifferences\":[\"string\"],\"audienceFit\":\"string\",\"recommendedUse\":\"string\"}]}",
      ];
    case "voiceover":
      return [
        "json_schema={\"results\":[{\"title\":\"string\",\"script\":\"string\",\"pacingNotes\":\"string\",\"pronunciationNotes\":[\"string\"],\"emotion\":\"string\",\"targetFormat\":\"string\"}]}",
      ];
    default:
      return ["json_schema={\"results\":[{\"title\":\"string\",\"body\":\"string\"}]}"];
  }
}

function makeId(prefix: string) {
  if (typeof crypto !== "undefined" && "randomUUID" in crypto) {
    return `${prefix}-${crypto.randomUUID()}`;
  }

  return `${prefix}-${Math.random().toString(36).slice(2, 10)}`;
}

function normalizeString(value: unknown) {
  return typeof value === "string" ? value.trim() : String(value ?? "").trim();
}

function normalizeStringArray(value: unknown) {
  return Array.isArray(value) ? value.map((entry) => normalizeString(entry)).filter(Boolean) : [];
}

function asRecord(value: unknown) {
  return value && typeof value === "object" && !Array.isArray(value) ? value as Record<string, unknown> : undefined;
}

function extractJsonPayload(responseText: string): unknown | undefined {
  const candidates = [responseText.trim()];
  const fencedBlocks = responseText.match(/```json\s*([\s\S]*?)```/gi) ?? [];
  fencedBlocks.forEach((block) => {
    const stripped = block.replace(/```json/i, "").replace(/```$/, "").trim();
    if (stripped) {
      candidates.push(stripped);
    }
  });

  for (const candidate of candidates) {
    try {
      return JSON.parse(candidate) as unknown;
    } catch {
      continue;
    }
  }

  return undefined;
}

function cleanLine(line: string) {
  return line.replace(/^[-*\u2022\d.()\s]+/, "").trim();
}

function summarize(text: string, maxLength = 120) {
  const compact = text.replace(/\s+/g, " ").trim();
  if (compact.length <= maxLength) {
    return compact;
  }

  return `${compact.slice(0, maxLength - 1).trimEnd()}...`;
}

function splitParagraphs(text: string) {
  return text
    .replace(/\r/g, "")
    .split(/\n\s*\n+/)
    .map((entry) => entry.trim())
    .filter(Boolean);
}

function splitOptionBlocks(text: string) {
  const normalized = text.replace(/\r/g, "").trim();
  if (!normalized) {
    return [];
  }

  const lines = normalized.split("\n");
  const blocks: string[] = [];
  let current: string[] = [];
  const topLevelPattern = /^(?:option|variant|concept|idea|hook|caption|script|version)\s*\d+\s*[:\-]?/i;
  const numberedPattern = /^\d+[).:-]\s+/;

  const flush = () => {
    const value = current.join("\n").trim();
    if (value) {
      blocks.push(value);
    }
    current = [];
  };

  lines.forEach((rawLine) => {
    const trimmed = rawLine.trim();
    if (!trimmed) {
      if (current.length > 0 && current[current.length - 1] !== "") {
        current.push("");
      }
      return;
    }

    if ((topLevelPattern.test(trimmed) || numberedPattern.test(trimmed)) && current.some((entry) => entry.trim().length > 0)) {
      flush();
    }

    current.push(trimmed.replace(numberedPattern, ""));
  });

  flush();

  if (blocks.length > 1) {
    return blocks;
  }

  return splitParagraphs(normalized);
}

function sectionKindForLabel(label: string): AIResultSection["kind"] {
  const normalized = label.toLowerCase();
  if (normalized.includes("cta") || normalized.includes("call to action")) {
    return "cta";
  }
  if (normalized.includes("slide")) {
    return "slide";
  }
  if (normalized.includes("scene")) {
    return "scene";
  }
  if (normalized.includes("platform") || normalized.includes("instagram") || normalized.includes("tiktok") || normalized.includes("youtube") || normalized.includes("linkedin") || normalized.includes("facebook") || normalized.includes("threads") || normalized.includes("snapchat")) {
    return "platform";
  }
  if (normalized.includes("beat")) {
    return "beat";
  }
  if (normalized.includes("script") || normalized.includes("hook") || normalized.includes("intro") || normalized.includes("outro")) {
    return "script";
  }
  return "text";
}

function parseNamedSections(text: string, allowedLabels: string[]) {
  const allowed = new Set(allowedLabels.map((label) => label.toLowerCase()));
  const lines = text.replace(/\r/g, "").split("\n");
  const sections: AIResultSection[] = [];
  let currentLabel = "Body";
  let currentKind: AIResultSection["kind"] = "text";
  let buffer: string[] = [];

  const flush = () => {
    const content = buffer.join("\n").trim();
    if (!content) {
      buffer = [];
      return;
    }
    sections.push({
      id: makeId("result-section"),
      label: currentLabel,
      kind: currentKind,
      content,
    });
    buffer = [];
  };

  lines.forEach((line) => {
    const trimmed = line.trim();
    const match = /^([A-Za-z][A-Za-z0-9 /&-]{1,24})\s*[:\-]\s*(.*)$/.exec(trimmed);
    if (match && allowed.has(match[1].toLowerCase())) {
      flush();
      currentLabel = match[1];
      currentKind = sectionKindForLabel(match[1]);
      if (match[2].trim()) {
        buffer.push(match[2].trim());
      }
      return;
    }

    if (trimmed) {
      buffer.push(trimmed);
    }
  });

  flush();
  return sections;
}

function extractCta(text: string) {
  const match = /(?:cta|call to action)\s*[:\-]\s*(.+)$/im.exec(text);
  return match?.[1]?.trim();
}

function schemaSections(schema: AIResultSchema): AIResultSection[] {
  switch (schema.type) {
    case "caption":
      return [
        { id: makeId("result-section"), label: "Hook", kind: "text", content: schema.hook },
        { id: makeId("result-section"), label: "Body", kind: "text", content: schema.body },
        { id: makeId("result-section"), label: "CTA", kind: "cta", content: schema.cta },
        ...(schema.hashtags.length > 0 ? [{ id: makeId("result-section"), label: "Hashtags", kind: "text" as const, content: schema.hashtags.join(" ") }] : []),
      ].filter((section) => section.content.trim().length > 0);
    case "hook":
      return [
        { id: makeId("result-section"), label: "Primary Hook", kind: "text", content: schema.primaryHook },
        ...schema.alternates.map((alternate, index) => ({ id: makeId("result-section"), label: `Alternate ${index + 1}`, kind: "text" as const, content: alternate })),
        ...(schema.usageNotes ? [{ id: makeId("result-section"), label: "Usage Notes", kind: "text" as const, content: schema.usageNotes }] : []),
      ];
    case "script":
      return [
        ...(schema.opening ? [{ id: makeId("result-section"), label: "Opening", kind: "script" as const, content: schema.opening }] : []),
        ...schema.beats.map((beat, index) => ({ id: makeId("result-section"), label: `Beat ${index + 1}`, kind: "beat" as const, content: beat })),
        ...(schema.cta ? [{ id: makeId("result-section"), label: "CTA", kind: "cta" as const, content: schema.cta }] : []),
        ...(schema.pacingNotes ? [{ id: makeId("result-section"), label: "Pacing Notes", kind: "text" as const, content: schema.pacingNotes }] : []),
      ];
    case "carousel":
      return [
        ...schema.slides.map((slide, index) => ({ id: makeId("result-section"), label: `Slide ${index + 1}: ${slide.title || `Slide ${index + 1}`}`, kind: "slide" as const, content: slide.body })),
        ...(schema.ctaSlide ? [{ id: makeId("result-section"), label: "CTA Slide", kind: "cta" as const, content: schema.ctaSlide }] : []),
        ...(schema.summary ? [{ id: makeId("result-section"), label: "Summary", kind: "text" as const, content: schema.summary }] : []),
      ];
    case "visual-concept":
      return [
        ...(schema.composition ? [{ id: makeId("result-section"), label: "Composition", kind: "text" as const, content: schema.composition }] : []),
        ...(schema.mood ? [{ id: makeId("result-section"), label: "Mood", kind: "text" as const, content: schema.mood }] : []),
        ...(schema.shotNotes.length > 0 ? [{ id: makeId("result-section"), label: "Shot Notes", kind: "scene" as const, content: schema.shotNotes.join("\n") }] : []),
        ...(schema.platformNotes ? [{ id: makeId("result-section"), label: "Platform Notes", kind: "platform" as const, content: schema.platformNotes }] : []),
      ];
    case "platform-adaptation":
      return [
        { id: makeId("result-section"), label: "Adapted Body", kind: "platform", content: schema.adaptedBody },
        { id: makeId("result-section"), label: "Adapted CTA", kind: "cta", content: schema.adaptedCta },
        ...(schema.lengthNotes ? [{ id: makeId("result-section"), label: "Length Notes", kind: "text" as const, content: schema.lengthNotes }] : []),
      ].filter((section) => section.content.trim().length > 0);
    case "variant-pack":
      return [
        ...schema.variants.map((variant, index) => ({
          id: makeId("result-section"),
          label: variant.title || `Variant ${index + 1}`,
          kind: "text" as const,
          content: [variant.hook, variant.body, variant.cta && `CTA: ${variant.cta}`, variant.keyDifference && `Difference: ${variant.keyDifference}`].filter(Boolean).join("\n\n"),
        })),
        ...(schema.recommendedUse ? [{ id: makeId("result-section"), label: "Recommended Use", kind: "text" as const, content: schema.recommendedUse }] : []),
      ];
    case "voiceover":
      return [
        { id: makeId("result-section"), label: "Script", kind: "script", content: schema.script },
        ...(schema.pacingNotes ? [{ id: makeId("result-section"), label: "Pacing Notes", kind: "text" as const, content: schema.pacingNotes }] : []),
        ...(schema.pronunciationNotes.length > 0 ? [{ id: makeId("result-section"), label: "Pronunciation Notes", kind: "text" as const, content: schema.pronunciationNotes.join("\n") }] : []),
      ].filter((section) => section.content.trim().length > 0);
  }
}

function schemaMetadata(schema: AIResultSchema): Record<string, string> {
  switch (schema.type) {
    case "caption":
      return { hashtags: schema.hashtags.join(" "), tone: schema.toneNotes, audience: schema.audienceNotes };
    case "hook":
      return { styles: schema.styleTags.join(", ") };
    case "script":
      return { title: schema.title, pacing: schema.pacingNotes };
    case "carousel":
      return { slides: String(schema.slides.length), ctaSlide: schema.ctaSlide };
    case "visual-concept":
      return { palette: schema.palette.join(", "), mood: schema.mood };
    case "platform-adaptation":
      return { source: schema.sourcePlatform, target: schema.targetPlatform };
    case "variant-pack":
      return { variants: String(schema.variants.length), audience: schema.audienceFit };
    case "voiceover":
      return { emotion: schema.emotion, format: schema.targetFormat };
  }
}

function schemaContent(schema: AIResultSchema) {
  switch (schema.type) {
    case "caption":
      return [schema.hook, schema.body, schema.cta, schema.hashtags.join(" ")].filter(Boolean).join("\n\n");
    case "hook":
      return [schema.primaryHook, ...schema.alternates.map((item) => `Alternate: ${item}`), schema.usageNotes && `Usage Notes: ${schema.usageNotes}`].filter(Boolean).join("\n\n");
    case "script":
      return [schema.title, schema.opening, ...schema.beats, schema.cta && `CTA: ${schema.cta}`, schema.pacingNotes && `Pacing: ${schema.pacingNotes}`].filter(Boolean).join("\n\n");
    case "carousel":
      return [schema.title, ...schema.slides.map((slide, index) => `Slide ${index + 1}: ${slide.title}\n${slide.body}`), schema.ctaSlide && `CTA Slide: ${schema.ctaSlide}`, schema.summary].filter(Boolean).join("\n\n");
    case "visual-concept":
      return [schema.conceptTitle, `Palette: ${schema.palette.join(", ")}`, `Composition: ${schema.composition}`, `Mood: ${schema.mood}`, schema.references.length > 0 ? `References: ${schema.references.join(", ")}` : "", schema.shotNotes.length > 0 ? `Shot Notes: ${schema.shotNotes.join(" | ")}` : "", schema.platformNotes].filter(Boolean).join("\n\n");
    case "platform-adaptation":
      return [`${schema.sourcePlatform} -> ${schema.targetPlatform}`, schema.adaptedBody, schema.adaptedCta && `CTA: ${schema.adaptedCta}`, schema.lengthNotes && `Length Notes: ${schema.lengthNotes}`].filter(Boolean).join("\n\n");
    case "variant-pack":
      return [
        ...schema.variants.map((variant, index) => [variant.title || `Variant ${index + 1}`, variant.hook, variant.body, variant.cta && `CTA: ${variant.cta}`, variant.keyDifference && `Difference: ${variant.keyDifference}`].filter(Boolean).join("\n")),
        schema.keyDifferences.length > 0 ? `Key Differences: ${schema.keyDifferences.join("; ")}` : "",
        schema.audienceFit && `Audience Fit: ${schema.audienceFit}`,
        schema.recommendedUse && `Recommended Use: ${schema.recommendedUse}`,
      ].filter(Boolean).join("\n\n");
    case "voiceover":
      return [schema.title, schema.script, schema.pacingNotes && `Pacing: ${schema.pacingNotes}`, schema.pronunciationNotes.length > 0 ? `Pronunciation: ${schema.pronunciationNotes.join(", ")}` : "", schema.emotion && `Emotion: ${schema.emotion}`, schema.targetFormat && `Target Format: ${schema.targetFormat}`].filter(Boolean).join("\n\n");
  }
}

function schemaSummary(schema: AIResultSchema) {
  switch (schema.type) {
    case "caption":
      return summarize([schema.hook, schema.body].filter(Boolean).join(" "));
    case "hook":
      return summarize(schema.primaryHook);
    case "script":
      return summarize([schema.title, schema.opening].filter(Boolean).join(" - "));
    case "carousel":
      return summarize([schema.title, schema.summary].filter(Boolean).join(" - "));
    case "visual-concept":
      return summarize([schema.conceptTitle, schema.mood, schema.composition].filter(Boolean).join(" - "));
    case "platform-adaptation":
      return summarize(schema.adaptedBody);
    case "variant-pack":
      return summarize(schema.variants[0]?.body || schema.recommendedUse);
    case "voiceover":
      return summarize([schema.title, schema.script].filter(Boolean).join(" - "));
  }
}

function createOption(input: {
  kind: AIResultOptionKind;
  label: string;
  content: string;
  sourceIndex: number;
  sections?: AIResultSection[];
  metadata?: Record<string, string>;
  summary?: string;
  schema?: AIResultSchema;
}) {
  const sections = input.sections?.filter((section) => section.content.trim()) ?? [];
  return {
    id: makeId("result-option"),
    kind: input.kind,
    label: input.label,
    summary: input.summary ?? summarize(input.content),
    content: input.content.trim(),
    sections,
    schema: input.schema,
    sourceIndex: input.sourceIndex,
    isFavorite: false,
    isPinned: false,
    metadata: input.metadata ?? {},
  } satisfies AIResultOption;
}

function createSchemaOption(input: {
  kind: AIResultOptionKind;
  label: string;
  schema: AIResultSchema;
  sourceIndex: number;
}) {
  return createOption({
    kind: input.kind,
    label: input.label,
    content: schemaContent(input.schema),
    sourceIndex: input.sourceIndex,
    sections: schemaSections(input.schema),
    metadata: schemaMetadata(input.schema),
    summary: schemaSummary(input.schema),
    schema: input.schema,
  });
}

function createGroup(input: {
  label: string;
  kind: AIResultOptionKind;
  description: string;
  taskType: AITaskType;
  providerId: ModelProviderId;
  modelId: string;
  responseText: string;
  options: AIResultOption[];
}) {
  return {
    id: makeId("result-group"),
    label: input.label,
    kind: input.kind,
    description: input.description,
    sourceTaskType: input.taskType,
    providerId: input.providerId,
    modelId: input.modelId,
    createdAt: new Date().toISOString(),
    sourceResponse: input.responseText,
    options: input.options,
  } satisfies AIResultGroup;
}

function unwrapResultsPayload(parsed: unknown) {
  if (Array.isArray(parsed)) {
    return parsed;
  }

  const record = asRecord(parsed);
  if (!record) {
    return [] as unknown[];
  }

  if (Array.isArray(record.results)) {
    return record.results;
  }

  if (Array.isArray(record.options)) {
    return record.options;
  }

  return [record];
}

function parseCaptionSchemaResults(responseText: string) {
  const results = unwrapResultsPayload(extractJsonPayload(responseText));
  return results.map((entry, index) => {
    const record = asRecord(entry);
    if (!record) {
      return undefined;
    }
    const schema: AICaptionResultSchema = {
      type: "caption",
      hook: normalizeString(record.hook),
      body: normalizeString(record.body),
      cta: normalizeString(record.cta),
      hashtags: normalizeStringArray(record.hashtags),
      toneNotes: normalizeString(record.toneNotes),
      audienceNotes: normalizeString(record.audienceNotes),
    };
    if (![schema.hook, schema.body, schema.cta].some(Boolean)) {
      return undefined;
    }
    return createSchemaOption({ kind: "caption-candidate", label: schema.hook || `Caption ${index + 1}`, schema, sourceIndex: index });
  }).filter((option): option is AIResultOption => Boolean(option));
}

function parseHookSchemaResults(responseText: string) {
  const results = unwrapResultsPayload(extractJsonPayload(responseText));
  return results.map((entry, index) => {
    const record = asRecord(entry);
    if (!record) {
      return undefined;
    }
    const schema: AIHookResultSchema = {
      type: "hook",
      primaryHook: normalizeString(record.primaryHook),
      alternates: normalizeStringArray(record.alternates),
      styleTags: normalizeStringArray(record.styleTags),
      usageNotes: normalizeString(record.usageNotes),
    };
    if (!schema.primaryHook) {
      return undefined;
    }
    return createSchemaOption({ kind: "hook-candidate", label: schema.primaryHook, schema, sourceIndex: index });
  }).filter((option): option is AIResultOption => Boolean(option));
}

function parseScriptSchemaResults(responseText: string) {
  const results = unwrapResultsPayload(extractJsonPayload(responseText));
  return results.map((entry, index) => {
    const record = asRecord(entry);
    if (!record) {
      return undefined;
    }
    const schema: AIScriptResultSchema = {
      type: "script",
      title: normalizeString(record.title),
      opening: normalizeString(record.opening),
      beats: normalizeStringArray(record.beats),
      cta: normalizeString(record.cta),
      pacingNotes: normalizeString(record.pacingNotes),
    };
    if (![schema.title, schema.opening].some(Boolean) && schema.beats.length === 0) {
      return undefined;
    }
    return createSchemaOption({ kind: "script-section", label: schema.title || `Script ${index + 1}`, schema, sourceIndex: index });
  }).filter((option): option is AIResultOption => Boolean(option));
}

function parseCarouselSchemaResults(responseText: string) {
  const results = unwrapResultsPayload(extractJsonPayload(responseText));
  return results.map((entry, index) => {
    const record = asRecord(entry);
    if (!record) {
      return undefined;
    }
    const slides = Array.isArray(record.slides)
      ? record.slides.map((slide) => {
        const item = asRecord(slide);
        return { title: normalizeString(item?.title), body: normalizeString(item?.body) };
      }).filter((slide) => slide.title || slide.body)
      : [];
    const schema: AICarouselResultSchema = {
      type: "carousel",
      title: normalizeString(record.title),
      slides,
      ctaSlide: normalizeString(record.ctaSlide),
      summary: normalizeString(record.summary),
    };
    if (!schema.title && schema.slides.length === 0) {
      return undefined;
    }
    return createSchemaOption({ kind: "scene-group", label: schema.title || `Carousel ${index + 1}`, schema, sourceIndex: index });
  }).filter((option): option is AIResultOption => Boolean(option));
}

function parseVisualConceptSchemaResults(responseText: string) {
  const results = unwrapResultsPayload(extractJsonPayload(responseText));
  return results.map((entry, index) => {
    const record = asRecord(entry);
    if (!record) {
      return undefined;
    }
    const schema: AIVisualConceptResultSchema = {
      type: "visual-concept",
      conceptTitle: normalizeString(record.conceptTitle),
      palette: normalizeStringArray(record.palette),
      composition: normalizeString(record.composition),
      mood: normalizeString(record.mood),
      references: normalizeStringArray(record.references),
      shotNotes: normalizeStringArray(record.shotNotes),
      platformNotes: normalizeString(record.platformNotes),
    };
    if (!schema.conceptTitle && !schema.composition && schema.shotNotes.length === 0) {
      return undefined;
    }
    return createSchemaOption({ kind: "visual-concept", label: schema.conceptTitle || `Concept ${index + 1}`, schema, sourceIndex: index });
  }).filter((option): option is AIResultOption => Boolean(option));
}

function parsePlatformAdaptationSchemaResults(responseText: string) {
  const results = unwrapResultsPayload(extractJsonPayload(responseText));
  return results.map((entry, index) => {
    const record = asRecord(entry);
    if (!record) {
      return undefined;
    }
    const schema: AIPlatformAdaptationResultSchema = {
      type: "platform-adaptation",
      sourcePlatform: normalizeString(record.sourcePlatform),
      targetPlatform: normalizeString(record.targetPlatform),
      adaptedBody: normalizeString(record.adaptedBody),
      adaptedCta: normalizeString(record.adaptedCta),
      lengthNotes: normalizeString(record.lengthNotes),
    };
    if (!schema.adaptedBody && !schema.adaptedCta) {
      return undefined;
    }
    return createSchemaOption({ kind: "platform-rewrite", label: `${schema.targetPlatform || "Platform"} Adaptation ${index + 1}`, schema, sourceIndex: index });
  }).filter((option): option is AIResultOption => Boolean(option));
}

function parseVariantPackSchemaResults(responseText: string) {
  const results = unwrapResultsPayload(extractJsonPayload(responseText));
  return results.map((entry, index) => {
    const record = asRecord(entry);
    if (!record) {
      return undefined;
    }
    const variants = Array.isArray(record.variants)
      ? record.variants.map((variant) => {
        const item = asRecord(variant);
        return {
          title: normalizeString(item?.title),
          body: normalizeString(item?.body),
          hook: normalizeString(item?.hook),
          cta: normalizeString(item?.cta),
          keyDifference: normalizeString(item?.keyDifference),
        };
      }).filter((variant) => variant.title || variant.body || variant.hook)
      : [];
    const schema: AIVariantPackResultSchema = {
      type: "variant-pack",
      variants,
      keyDifferences: normalizeStringArray(record.keyDifferences),
      audienceFit: normalizeString(record.audienceFit),
      recommendedUse: normalizeString(record.recommendedUse),
    };
    if (schema.variants.length === 0) {
      return undefined;
    }
    return createSchemaOption({ kind: "variant", label: `Variant Pack ${index + 1}`, schema, sourceIndex: index });
  }).filter((option): option is AIResultOption => Boolean(option));
}

function parseVoiceoverSchemaResults(responseText: string) {
  const results = unwrapResultsPayload(extractJsonPayload(responseText));
  return results.map((entry, index) => {
    const record = asRecord(entry);
    if (!record) {
      return undefined;
    }
    const schema: AIVoiceoverResultSchema = {
      type: "voiceover",
      title: normalizeString(record.title),
      script: normalizeString(record.script),
      pacingNotes: normalizeString(record.pacingNotes),
      pronunciationNotes: normalizeStringArray(record.pronunciationNotes),
      emotion: normalizeString(record.emotion),
      targetFormat: normalizeString(record.targetFormat),
    };
    if (!schema.script) {
      return undefined;
    }
    return createSchemaOption({ kind: "voiceover-script", label: schema.title || `Voiceover ${index + 1}`, schema, sourceIndex: index });
  }).filter((option): option is AIResultOption => Boolean(option));
}

function parsePlatformAdaptations(responseText: string) {
  const paragraphs = splitParagraphs(responseText);
  const knownPlatforms = ["instagram", "tiktok", "youtube", "facebook", "linkedin", "threads", "snapchat", "x / twitter", "twitter", "pinterest"];
  const options = paragraphs.flatMap((block, index) => {
    const sections = parseNamedSections(block, knownPlatforms);
    if (sections.length > 0) {
      return sections.map((section, sectionIndex) =>
        createOption({
          kind: "platform-rewrite",
          label: section.label,
          content: section.content,
          sourceIndex: index * 10 + sectionIndex,
          sections: [section],
          metadata: { platform: section.label },
        }),
      );
    }

    return [
      createOption({
        kind: "platform-rewrite",
        label: `Platform Rewrite ${index + 1}`,
        content: block,
        sourceIndex: index,
      }),
    ];
  });

  return options;
}

function parseSceneDrivenOptions(responseText: string, labels: string[], optionKind: AIResultOptionKind, labelPrefix: string) {
  const blocks = splitOptionBlocks(responseText);
  return blocks.map((block, index) => {
    const sections = parseNamedSections(block, labels);
    return createOption({
      kind: optionKind,
      label: `${labelPrefix} ${index + 1}`,
      content: block,
      sourceIndex: index,
      sections: sections.length > 0 ? sections : splitParagraphs(block).map((paragraph, paragraphIndex) => ({
        id: makeId("result-section"),
        label: `${labelPrefix} Beat ${paragraphIndex + 1}`,
        kind: optionKind === "scene-group" ? "scene" : "script",
        content: paragraph,
      })),
    });
  });
}

function parseShortFormOptions(responseText: string, optionKind: AIResultOptionKind, labelPrefix: string) {
  const blocks = splitOptionBlocks(responseText);
  return blocks.map((block, index) => {
    const cta = extractCta(block);
    const sections: AIResultSection[] = [
      {
        id: makeId("result-section"),
        label: labelPrefix,
        kind: "text",
        content: block,
      },
    ];
    if (cta) {
      sections.push({
        id: makeId("result-section"),
        label: "CTA",
        kind: "cta",
        content: cta,
      });
    }

    return createOption({
      kind: optionKind,
      label: `${labelPrefix} ${index + 1}`,
      content: block,
      sourceIndex: index,
      sections,
    });
  });
}

export function parseAtlasResponse(input: {
  taskType: AITaskType;
  providerId: ModelProviderId;
  modelId: string;
  responseText: string;
}) {
  const responseText = input.responseText.trim();
  if (!responseText) {
    return [] as AIResultGroup[];
  }

  const captionSchemaResults = input.taskType === "caption" ? parseCaptionSchemaResults(responseText) : [];
  const hookSchemaResults = input.taskType === "hook" ? parseHookSchemaResults(responseText) : [];
  const scriptSchemaResults = input.taskType === "script" ? parseScriptSchemaResults(responseText) : [];
  const adaptationSchemaResults = input.taskType === "platform-adaptation" ? parsePlatformAdaptationSchemaResults(responseText) : [];
  const carouselSchemaResults = input.taskType === "carousel-structure" ? parseCarouselSchemaResults(responseText) : [];
  const visualSchemaResults = input.taskType === "visual-concept" ? parseVisualConceptSchemaResults(responseText) : [];
  const variantSchemaResults = input.taskType === "campaign-pack" ? parseVariantPackSchemaResults(responseText) : [];
  const voiceoverSchemaResults = input.taskType === "voiceover" ? parseVoiceoverSchemaResults(responseText) : [];

  switch (input.taskType) {
    case "caption":
      return [
        createGroup({
          label: "Caption Candidates",
          kind: "caption-candidate",
          description: "Selectable captions with CTA-aware summaries.",
          taskType: input.taskType,
          providerId: input.providerId,
          modelId: input.modelId,
          responseText,
          options: captionSchemaResults.length > 0 ? captionSchemaResults : parseShortFormOptions(responseText, "caption-candidate", "Caption"),
        }),
      ];
    case "hook":
      return [
        createGroup({
          label: "Hook Candidates",
          kind: "hook-candidate",
          description: "Opening lines and first-frame hooks parsed into selectable cards.",
          taskType: input.taskType,
          providerId: input.providerId,
          modelId: input.modelId,
          responseText,
          options: hookSchemaResults.length > 0 ? hookSchemaResults : parseShortFormOptions(responseText, "hook-candidate", "Hook"),
        }),
      ];
    case "script":
      return [
        createGroup({
          label: "Script Sections",
          kind: "script-section",
          description: "Structured script options broken into hooks, beats, scenes, and CTA blocks.",
          taskType: input.taskType,
          providerId: input.providerId,
          modelId: input.modelId,
          responseText,
          options: scriptSchemaResults.length > 0 ? scriptSchemaResults : parseSceneDrivenOptions(responseText, ["hook", "intro", "scene 1", "scene 2", "scene 3", "scene", "beat 1", "beat 2", "beat 3", "beat", "body", "cta", "outro", "close"], "script-section", "Script"),
        }),
      ];
    case "platform-adaptation":
      return [
        createGroup({
          label: "Platform Rewrites",
          kind: "platform-rewrite",
          description: "Platform-specific rewrites and adaptation cards.",
          taskType: input.taskType,
          providerId: input.providerId,
          modelId: input.modelId,
          responseText,
          options: adaptationSchemaResults.length > 0 ? adaptationSchemaResults : parsePlatformAdaptations(responseText),
        }),
      ];
    case "carousel-structure":
      return [
        createGroup({
          label: "Carousel Structures",
          kind: "scene-group",
          description: "Slide-by-slide structures grouped into reusable carousel options.",
          taskType: input.taskType,
          providerId: input.providerId,
          modelId: input.modelId,
          responseText,
          options: carouselSchemaResults.length > 0 ? carouselSchemaResults : parseSceneDrivenOptions(responseText, ["cover", "slide 1", "slide 2", "slide 3", "slide 4", "slide 5", "slide", "cta", "close"], "scene-group", "Carousel"),
        }),
      ];
    case "visual-concept":
      return [
        createGroup({
          label: "Visual Concepts",
          kind: "visual-concept",
          description: "Visual directions, scene beats, palette notes, and art direction grouped into concept cards.",
          taskType: input.taskType,
          providerId: input.providerId,
          modelId: input.modelId,
          responseText,
          options: visualSchemaResults.length > 0 ? visualSchemaResults : parseSceneDrivenOptions(responseText, ["concept", "look", "palette", "camera", "scene 1", "scene 2", "scene", "beat 1", "beat 2", "beat", "cta"], "visual-concept", "Concept"),
        }),
      ];
    case "campaign-pack":
      return [
        createGroup({
          label: "Content Variants",
          kind: "variant",
          description: "Variant candidates with grouped content blocks for quick application into scenes or notes.",
          taskType: input.taskType,
          providerId: input.providerId,
          modelId: input.modelId,
          responseText,
          options: variantSchemaResults.length > 0 ? variantSchemaResults : parseSceneDrivenOptions(responseText, ["variant 1", "variant 2", "variant 3", "variant", "angle", "caption", "hook", "cta"], "variant", "Variant"),
        }),
      ];
    case "voiceover":
      return [
        createGroup({
          label: "Voiceover Scripts",
          kind: "voiceover-script",
          description: "Voiceover-ready scripts with pacing, emotion, and pronunciation guidance.",
          taskType: input.taskType,
          providerId: input.providerId,
          modelId: input.modelId,
          responseText,
          options: voiceoverSchemaResults.length > 0 ? voiceoverSchemaResults : parseSceneDrivenOptions(responseText, ["title", "script", "pacing", "pronunciation", "emotion", "format"], "voiceover-script", "Voiceover"),
        }),
      ];
    case "ideas":
      return [
        createGroup({
          label: "Idea Options",
          kind: "idea",
          description: "Idea cards ready to be pinned, selected, and turned into scenes.",
          taskType: input.taskType,
          providerId: input.providerId,
          modelId: input.modelId,
          responseText,
          options: splitOptionBlocks(responseText).map((block, index) =>
            createOption({
              kind: "idea",
              label: `Idea ${index + 1}`,
              content: block,
              sourceIndex: index,
            }),
          ),
        }),
      ];
    default:
      return [
        createGroup({
          label: "Structured Results",
          kind: "idea",
          description: "Generic structured results parsed from the latest response.",
          taskType: input.taskType,
          providerId: input.providerId,
          modelId: input.modelId,
          responseText,
          options: splitOptionBlocks(responseText).map((block, index) =>
            createOption({
              kind: "idea",
              label: `Result ${index + 1}`,
              content: block,
              sourceIndex: index,
            }),
          ),
        }),
      ];
  }
}