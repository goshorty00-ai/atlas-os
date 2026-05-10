import { useMemo, useState } from "react";
import { useNavigate } from "react-router";
import { BrainCircuit, Copy, FileText, FolderPlus, Send, Sparkles } from "lucide-react";
import { AI_PROVIDERS, AI_TASKS, buildAtlasPacket, modelsForProvider, recommendedProvidersForTask } from "../lib/aiOrchestration";
import { createDraftFromPreset, createMediaAsset, getFormatPreset, useStudio, type AISurface, type AITaskType, type ModelProviderId, type PlatformId } from "../state/studioStore";

const surfaces: Array<{ id: AISurface; label: string }> = [
  { id: "create", label: "Create Studio" },
  { id: "video", label: "Video Studio" },
  { id: "voice", label: "Voice Studio" },
  { id: "planner", label: "Content Planner" },
  { id: "library", label: "Media Library" },
];

const tones = ["Editorial", "Luxury", "Direct", "Playful", "Cinematic", "Authority"];

function inferFormatId(platformId: PlatformId) {
  if (platformId === "youtube") return "youtube-short";
  if (platformId === "linkedin") return "linkedin-banner";
  if (platformId === "tiktok" || platformId === "snapchat") return "short-video";
  return "instagram-post";
}

export function AICreator() {
  const navigate = useNavigate();
  const {
    state: { aiBriefs, brandKit, selectedDraftId, drafts, accounts, assets },
    addAiBrief,
    addAssets,
    saveDraft,
    selectDraft,
    updateDraft,
  } = useStudio();

  const [taskType, setTaskType] = useState<AITaskType>("ideas");
  const [selectedProvider, setSelectedProvider] = useState<ModelProviderId>("gpt");
  const [selectedModel, setSelectedModel] = useState("gpt-5.4");
  const [selectedPlatform, setSelectedPlatform] = useState<PlatformId>("instagram");
  const [selectedSurface, setSelectedSurface] = useState<AISurface>("create");
  const [selectedTone, setSelectedTone] = useState("Editorial");
  const [objective, setObjective] = useState("High-converting campaign concept");
  const [variantsRequested, setVariantsRequested] = useState(3);
  const [brief, setBrief] = useState("");

  const currentDraft = drafts.find((draft) => draft.id === selectedDraftId);
  const models = useMemo(() => modelsForProvider(selectedProvider), [selectedProvider]);
  const selectedTask = AI_TASKS.find((task) => task.id === taskType) ?? AI_TASKS[0];

  const packet = useMemo(
    () =>
      buildAtlasPacket({
        providerId: selectedProvider,
        modelId: selectedModel,
        taskType,
        tone: selectedTone,
        platformId: selectedPlatform,
        objective,
        variants: variantsRequested,
        targetSurface: selectedSurface,
        brief,
        brandName: brandKit.brandName,
        brandTone: brandKit.tone,
        audience: brandKit.audience,
        accountsCount: accounts.length,
        assetsCount: assets.length,
        draft: currentDraft,
      }),
    [accounts.length, assets.length, brief, brandKit.audience, brandKit.brandName, brandKit.tone, currentDraft, objective, selectedModel, selectedPlatform, selectedProvider, selectedSurface, selectedTone, taskType, variantsRequested],
  );

  function applyTaskRecommendation(nextTask: AITaskType) {
    const recommended = recommendedProvidersForTask(nextTask);
    const nextProvider = recommended[0] ?? "gpt";
    const nextModels = modelsForProvider(nextProvider);
    const defaultSurface = AI_TASKS.find((task) => task.id === nextTask)?.defaultSurface ?? "create";
    setTaskType(nextTask);
    setSelectedProvider(nextProvider);
    setSelectedModel(nextModels[0]?.id ?? "gpt-5.4");
    setSelectedSurface(defaultSurface);
  }

  function saveVariants() {
    for (let index = 1; index <= variantsRequested; index += 1) {
      addAiBrief({
        providerId: selectedProvider,
        modelId: selectedModel,
        taskType,
        platformId: selectedPlatform,
        objective: `${objective} · Variant ${index}`,
        tone: selectedTone,
        contentType: getFormatPreset(inferFormatId(selectedPlatform)).contentType,
        brief,
        requestPacket: `${packet}\nvariant_index=${index}`,
        draftId: currentDraft?.id,
        targetSurface: selectedSurface,
        variantsRequested,
        status: "queued",
      });
    }
  }

  function pushToStudio() {
    if (selectedSurface === "voice") {
      navigate("/voice-studio");
      return;
    }

    if (currentDraft) {
      updateDraft(currentDraft.id, (draft) => ({
        ...draft,
        notes: [draft.notes, packet].filter(Boolean).join("\n\n"),
        caption: taskType === "caption" ? [draft.caption, brief].filter(Boolean).join("\n\n") : draft.caption,
      }));
      navigate(selectedSurface === "video" ? "/video-studio" : selectedSurface === "planner" ? "/planner" : "/create");
      return;
    }

    const nextDraft = createDraftFromPreset(inferFormatId(selectedPlatform));
    nextDraft.title = `${selectedTask.label} · ${selectedPlatform}`;
    nextDraft.notes = packet;
    nextDraft.caption = taskType === "caption" ? brief : nextDraft.caption;
    saveDraft(nextDraft);
    selectDraft(nextDraft.id);
    navigate(selectedSurface === "video" ? "/video-studio" : "/create");
  }

  function savePacketToLibrary() {
    addAssets([
      createMediaAsset({
        name: `${objective.replace(/\s+/g, " ").trim() || "atlas-packet"}.atlas.txt`,
        kind: "template",
        mimeType: "text/plain",
        sizeBytes: packet.length,
        storageMode: "metadata-only",
        folder: "ATLAS Packets",
        tags: [taskType, selectedProvider, selectedPlatform],
        source: "ai-generated",
        transcript: packet,
      }),
    ]);
  }

  async function copyPacket() {
    try {
      await navigator.clipboard.writeText(packet);
    } catch {
      // Embedded host may not allow clipboard access.
    }
  }

  return (
    <div className="h-full overflow-y-auto bg-[#07080c]">
      <div className="p-8 space-y-6">
        <section className="grid grid-cols-[340px_1fr] gap-6">
          <div className="space-y-6">
            <div className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-6">
              <div className="flex items-center gap-3 mb-4">
                <div className="w-10 h-10 rounded-2xl bg-gradient-to-br from-violet-500 to-fuchsia-500 flex items-center justify-center">
                  <BrainCircuit className="w-5 h-5 text-white" />
                </div>
                <div>
                  <div className="text-lg font-semibold text-white">ATLAS AI Creator</div>
                  <div className="text-sm text-gray-400">Multi-provider orchestration for creation, video, planning, and voice.</div>
                </div>
              </div>

              <div className="space-y-4">
                <div>
                  <div className="text-sm text-gray-400 mb-2">Task Type</div>
                  <div className="space-y-2 max-h-[300px] overflow-y-auto pr-1">
                    {AI_TASKS.map((task) => (
                      <button
                        key={task.id}
                        onClick={() => applyTaskRecommendation(task.id)}
                        className={`w-full rounded-2xl border p-3 text-left transition-all ${
                          taskType === task.id
                            ? "border-cyan-400/30 bg-cyan-500/10"
                            : "border-white/10 bg-white/5 hover:bg-white/10"
                        }`}
                      >
                        <div className="text-sm font-medium text-white">{task.label}</div>
                        <div className="mt-1 text-xs text-gray-400">{task.description}</div>
                      </button>
                    ))}
                  </div>
                </div>

                <div>
                  <div className="text-sm text-gray-400 mb-2">Provider</div>
                  <div className="grid grid-cols-2 gap-2">
                    {AI_PROVIDERS.map((provider) => (
                      <button
                        key={provider.id}
                        onClick={() => {
                          setSelectedProvider(provider.id);
                          setSelectedModel(provider.models[0]?.id ?? "gpt-5.4");
                        }}
                        className={`rounded-2xl border p-3 text-left transition-all ${
                          selectedProvider === provider.id
                            ? "border-cyan-400/30 bg-cyan-500/10"
                            : "border-white/10 bg-white/5 hover:bg-white/10"
                        }`}
                      >
                        <div className="text-sm font-medium text-white">{provider.label}</div>
                        <div className="mt-1 text-[11px] text-gray-400">{provider.bestFor[0]}</div>
                      </button>
                    ))}
                  </div>
                </div>

                <div>
                  <label className="text-sm text-gray-400 mb-2 block">Model</label>
                  <select
                    value={selectedModel}
                    onChange={(event) => setSelectedModel(event.target.value)}
                    className="w-full rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white"
                  >
                    {models.map((model) => (
                      <option key={model.id} value={model.id}>
                        {model.label}
                      </option>
                    ))}
                  </select>
                </div>
              </div>
            </div>

            <div className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-6">
              <div className="text-sm text-gray-400 mb-3">Routing Targets</div>
              <div className="space-y-3">
                <select
                  value={selectedPlatform}
                  onChange={(event) => setSelectedPlatform(event.target.value as PlatformId)}
                  className="w-full rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white"
                >
                  {Array.from(new Set(accounts.map((account) => account.platformId).concat([selectedPlatform]))).map((platformId) => (
                    <option key={platformId} value={platformId}>
                      {platformId}
                    </option>
                  ))}
                </select>
                <select
                  value={selectedSurface}
                  onChange={(event) => setSelectedSurface(event.target.value as AISurface)}
                  className="w-full rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white"
                >
                  {surfaces.map((surface) => (
                    <option key={surface.id} value={surface.id}>
                      {surface.label}
                    </option>
                  ))}
                </select>
                <select
                  value={selectedTone}
                  onChange={(event) => setSelectedTone(event.target.value)}
                  className="w-full rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white"
                >
                  {tones.map((tone) => (
                    <option key={tone} value={tone}>
                      {tone}
                    </option>
                  ))}
                </select>
              </div>
            </div>
          </div>

          <div className="space-y-6">
            <div className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-6">
              <div className="grid grid-cols-4 gap-4 mb-6">
                <div className="rounded-2xl border border-white/10 bg-black/20 p-4">
                  <div className="text-xs text-gray-400 mb-1">Current Draft</div>
                  <div className="text-sm font-medium text-white">{currentDraft?.title || "No draft selected"}</div>
                </div>
                <div className="rounded-2xl border border-white/10 bg-black/20 p-4">
                  <div className="text-xs text-gray-400 mb-1">Accounts</div>
                  <div className="text-sm font-medium text-white">{accounts.length}</div>
                </div>
                <div className="rounded-2xl border border-white/10 bg-black/20 p-4">
                  <div className="text-xs text-gray-400 mb-1">Assets</div>
                  <div className="text-sm font-medium text-white">{assets.length}</div>
                </div>
                <div className="rounded-2xl border border-white/10 bg-black/20 p-4">
                  <div className="text-xs text-gray-400 mb-1">Saved Packets</div>
                  <div className="text-sm font-medium text-white">{aiBriefs.length}</div>
                </div>
              </div>

              <div className="grid grid-cols-[1fr_140px] gap-4 mb-4">
                <input
                  value={objective}
                  onChange={(event) => setObjective(event.target.value)}
                  className="rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white"
                />
                <div className="rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3">
                  <div className="text-xs text-gray-400 mb-1">Variants</div>
                  <input
                    type="range"
                    min="1"
                    max="6"
                    value={variantsRequested}
                    onChange={(event) => setVariantsRequested(Number(event.target.value))}
                    className="w-full"
                  />
                  <div className="text-sm text-white">{variantsRequested}</div>
                </div>
              </div>

              <textarea
                value={brief}
                onChange={(event) => setBrief(event.target.value)}
                placeholder="Describe the product, offer, audience, constraints, campaign goal, assets in hand, and the type of content package you need ATLAS to orchestrate."
                className="w-full h-40 rounded-[24px] border border-white/10 bg-[#0c1016] px-4 py-4 text-white placeholder-gray-500 resize-none"
              />

              <div className="mt-5 flex flex-wrap gap-3">
                <button onClick={saveVariants} className="px-5 py-3 rounded-2xl bg-gradient-to-r from-violet-500 to-fuchsia-500 text-white text-sm font-medium flex items-center gap-2">
                  <Send className="w-4 h-4" />
                  Save Variant Packets
                </button>
                <button onClick={pushToStudio} className="px-5 py-3 rounded-2xl bg-white/5 border border-white/10 text-white text-sm font-medium flex items-center gap-2">
                  <FolderPlus className="w-4 h-4" />
                  Push To Workflow Surface
                </button>
                <button onClick={savePacketToLibrary} className="px-5 py-3 rounded-2xl bg-white/5 border border-white/10 text-white text-sm font-medium flex items-center gap-2">
                  <FileText className="w-4 h-4" />
                  Save Packet To Library
                </button>
              </div>
            </div>

            <div className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-6">
              <div className="flex items-center justify-between mb-4">
                <h2 className="text-lg font-semibold text-white">ATLAS Orchestration Packet</h2>
                <button onClick={() => void copyPacket()} className="text-sm text-cyan-300 flex items-center gap-2">
                  <Copy className="w-4 h-4" />
                  Copy
                </button>
              </div>
              <div className="rounded-[24px] border border-white/10 bg-[#0c1016] p-5 mb-4">
                <pre className="text-sm whitespace-pre-wrap text-gray-300 leading-relaxed">{packet}</pre>
              </div>
              <div className="rounded-2xl border border-cyan-400/20 bg-cyan-500/10 p-4 text-sm text-gray-300">
                Build provider-routed request packets here, compare variants, and push them into the right production surface as your live model integrations come online.
              </div>
            </div>

            <div className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-6">
              <div className="flex items-center justify-between mb-4">
                <h2 className="text-lg font-semibold text-white">Recent Request Variants</h2>
                <div className="text-sm text-gray-400">{aiBriefs.length} stored</div>
              </div>
              {aiBriefs.length === 0 ? (
                <div className="rounded-2xl border border-dashed border-white/10 bg-black/20 p-6 text-sm text-gray-400">
                  No request variants saved yet.
                </div>
              ) : (
                <div className="space-y-3">
                  {aiBriefs.slice(0, 6).map((request) => (
                    <div key={request.id} className="rounded-2xl border border-white/10 bg-black/20 p-4">
                      <div className="flex items-start justify-between gap-4 mb-2">
                        <div>
                          <div className="text-sm font-medium text-white">{request.objective}</div>
                          <div className="text-xs text-gray-400 mt-1">{request.providerId} · {request.modelId} · {request.taskType}</div>
                        </div>
                        <span className="text-xs uppercase tracking-[0.18em] text-cyan-300">{request.platformId}</span>
                      </div>
                      <div className="text-xs text-gray-500">{request.targetSurface} · {request.status} · {request.variantsRequested} variants requested</div>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>
        </section>
      </div>
    </div>
  );
}
