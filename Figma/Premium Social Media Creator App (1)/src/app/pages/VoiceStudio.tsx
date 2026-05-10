import { useMemo, useState } from "react";
import { useNavigate } from "react-router";
import { AudioLines, Copy, Download, Plus, Upload, Waves } from "lucide-react";
import { AI_PROVIDERS, buildAtlasPacket, modelsForProvider } from "../lib/aiOrchestration";
import { createMediaAsset, useStudio } from "../state/studioStore";

const emotionOptions = ["Neutral", "Warm", "Confident", "Luxury", "Urgent", "Playful"];

export function VoiceStudio() {
  const navigate = useNavigate();
  const {
    state: { voiceProjects, drafts, assets, selectedDraftId, brandKit },
    addAssets,
    addAiBrief,
    addVoiceProject,
    selectDraft,
    updateDraft,
  } = useStudio();

  const [providerId, setProviderId] = useState<"elevenlabs">("elevenlabs");
  const [modelId, setModelId] = useState("eleven_multilingual_v2");
  const [title, setTitle] = useState("Primary brand narration");
  const [voiceName, setVoiceName] = useState("");
  const [stylePrompt, setStylePrompt] = useState("");
  const [emotion, setEmotion] = useState("Neutral");
  const [speed, setSpeed] = useState(1);
  const [pacing, setPacing] = useState(1);
  const [script, setScript] = useState("");

  const currentDraft = drafts.find((draft) => draft.id === selectedDraftId) ?? drafts[0];
  const models = modelsForProvider(providerId);
  const audioAssets = assets.filter((asset) => asset.kind === "audio" || asset.kind === "voiceover");
  const [selectedAudioId, setSelectedAudioId] = useState<string | undefined>(audioAssets[0]?.id);
  const selectedAudio = audioAssets.find((asset) => asset.id === selectedAudioId) ?? audioAssets[0];

  const packet = useMemo(
    () =>
      buildAtlasPacket({
        providerId,
        modelId,
        taskType: "voiceover",
        tone: emotion,
        platformId: currentDraft?.linkedPlatformIds[0] ?? "instagram",
        objective: title,
        variants: 1,
        targetSurface: "voice",
        brief: [stylePrompt, script].filter(Boolean).join("\n\n"),
        brandName: brandKit.brandName,
        brandTone: brandKit.tone,
        audience: brandKit.audience,
        accountsCount: 0,
        assetsCount: audioAssets.length,
        draft: currentDraft,
      }),
    [audioAssets.length, brandKit.audience, brandKit.brandName, brandKit.tone, currentDraft, emotion, modelId, providerId, script, stylePrompt, title],
  );

  function saveVoiceSession() {
    const projectId = addVoiceProject({
      title,
      script,
      providerId,
      modelId,
      voiceName,
      stylePrompt,
      emotion,
      speed,
      pacing,
      linkedDraftId: currentDraft?.id,
      selectedAssetId: selectedAudio?.id,
      versions: [script],
      status: "waiting-backend",
    });

    addAiBrief({
      providerId,
      modelId,
      taskType: "voiceover",
      platformId: currentDraft?.linkedPlatformIds[0] ?? "instagram",
      objective: title,
      tone: emotion,
      contentType: currentDraft?.contentType ?? "Voiceover",
      brief: script,
      requestPacket: packet,
      draftId: currentDraft?.id,
      targetSurface: "voice",
      variantsRequested: 1,
      status: "queued",
    });

    if (currentDraft) {
      updateDraft(currentDraft.id, (draft) => ({
        ...draft,
        notes: [draft.notes, `Voice session linked: ${projectId}`, packet].filter(Boolean).join("\n\n"),
      }));
    }
  }

  function saveDirectionToLibrary() {
    addAssets([
      createMediaAsset({
        name: `${title.replace(/\s+/g, "-").toLowerCase()}-direction.txt`,
        kind: "voiceover",
        mimeType: "text/plain",
        sizeBytes: packet.length,
        storageMode: "metadata-only",
        folder: "Voice Studio",
        tags: [emotion, providerId, "voiceover-request"],
        source: "voice-studio",
        transcript: packet,
      }),
    ]);
  }

  function attachSelectedAudioToDraft() {
    if (!currentDraft || !selectedAudio) {
      return;
    }

    updateDraft(currentDraft.id, (draft) => ({
      ...draft,
      linkedAssetIds: draft.linkedAssetIds.includes(selectedAudio.id) ? draft.linkedAssetIds : [...draft.linkedAssetIds, selectedAudio.id],
      layers: [
        ...draft.layers,
        {
          id: `layer-audio-${selectedAudio.id}-${Date.now()}`,
          type: "audio",
          name: selectedAudio.name,
          sceneId: draft.activeSceneId,
          visible: true,
          locked: false,
          x: 40,
          y: draft.height - 100,
          width: draft.width - 80,
          height: 52,
          rotation: 0,
          opacity: 100,
          assetId: selectedAudio.id,
          startMs: 0,
          endMs: draft.durationMs,
        },
      ],
    }));

    navigate("/video-studio");
  }

  function exportSelectedAudio() {
    if (!selectedAudio?.dataUrl) {
      return;
    }

    const anchor = document.createElement("a");
    anchor.href = selectedAudio.dataUrl;
    anchor.download = selectedAudio.name;
    anchor.click();
  }

  async function copyPacket() {
    try {
      await navigator.clipboard.writeText(packet);
    } catch {
      // Clipboard may be unavailable.
    }
  }

  return (
    <div className="h-full overflow-y-auto bg-[#07080c]">
      <div className="p-8 space-y-6">
        <div className="flex items-start justify-between gap-6">
          <div>
            <h1 className="text-3xl font-bold text-white mb-2">Voice Studio</h1>
            <p className="text-gray-400">Prepare premium ElevenLabs-ready narration packets, compare script versions, and attach real audio assets directly to video drafts.</p>
          </div>
          <button onClick={() => navigate("/video-studio")} className="rounded-2xl border border-white/10 bg-white/5 px-4 py-3 text-sm font-medium text-white">Open Video Studio</button>
        </div>

        <div className="grid grid-cols-[360px_1fr_340px] gap-6">
          <section className="space-y-6">
            <div className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-6">
              <div className="flex items-center gap-2 mb-4">
                <Waves className="w-5 h-5 text-cyan-300" />
                <h2 className="text-lg font-semibold text-white">Voice Engine</h2>
              </div>
              <div className="space-y-4">
                <select value={providerId} onChange={(event) => setProviderId(event.target.value as "elevenlabs")} className="w-full rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white">
                  {AI_PROVIDERS.filter((provider) => provider.id === "elevenlabs").map((provider) => (
                    <option key={provider.id} value={provider.id}>{provider.label}</option>
                  ))}
                </select>
                <select value={modelId} onChange={(event) => setModelId(event.target.value)} className="w-full rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white">
                  {models.map((model) => (
                    <option key={model.id} value={model.id}>{model.label}</option>
                  ))}
                </select>
                <input value={voiceName} onChange={(event) => setVoiceName(event.target.value)} placeholder="Voice or actor name" className="w-full rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white" />
                <select value={emotion} onChange={(event) => setEmotion(event.target.value)} className="w-full rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white">
                  {emotionOptions.map((option) => <option key={option} value={option}>{option}</option>)}
                </select>
              </div>
            </div>

            <div className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-6">
              <div className="text-sm text-gray-400 mb-3">Playback Direction</div>
              <div className="space-y-4">
                <label className="text-sm text-gray-400 block">Speed {speed.toFixed(1)}
                  <input type="range" min="0.7" max="1.4" step="0.1" value={speed} onChange={(event) => setSpeed(Number(event.target.value))} className="mt-2 w-full" />
                </label>
                <label className="text-sm text-gray-400 block">Pacing {pacing.toFixed(1)}
                  <input type="range" min="0.7" max="1.4" step="0.1" value={pacing} onChange={(event) => setPacing(Number(event.target.value))} className="mt-2 w-full" />
                </label>
              </div>
            </div>
          </section>

          <section className="space-y-6">
            <div className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-6">
              <input value={title} onChange={(event) => setTitle(event.target.value)} className="w-full rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white mb-4" />
              <textarea value={stylePrompt} onChange={(event) => setStylePrompt(event.target.value)} placeholder="Delivery direction, pronunciation notes, pace, emphasis, and emotional framing." className="w-full h-28 rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white resize-none mb-4" />
              <textarea value={script} onChange={(event) => setScript(event.target.value)} placeholder="Write the real narration or voiceover script here." className="w-full h-56 rounded-[24px] border border-white/10 bg-[#0c1016] px-4 py-4 text-white resize-none" />

              <div className="mt-5 flex flex-wrap gap-3">
                <button onClick={saveVoiceSession} className="rounded-2xl bg-gradient-to-r from-cyan-500 to-blue-600 px-5 py-3 text-sm font-medium text-white flex items-center gap-2"><Plus className="w-4 h-4" />Save Voice Session</button>
                <button onClick={saveDirectionToLibrary} className="rounded-2xl border border-white/10 bg-white/5 px-5 py-3 text-sm font-medium text-white flex items-center gap-2"><Upload className="w-4 h-4" />Save Direction To Library</button>
              </div>
            </div>

            <div className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-6">
              <div className="flex items-center justify-between mb-4">
                <h2 className="text-lg font-semibold text-white">Voice Packet</h2>
                <button onClick={() => void copyPacket()} className="text-sm text-cyan-300 flex items-center gap-2"><Copy className="w-4 h-4" />Copy</button>
              </div>
              <div className="rounded-[24px] border border-white/10 bg-[#0c1016] p-5">
                <pre className="text-sm whitespace-pre-wrap text-gray-300 leading-relaxed">{packet}</pre>
              </div>
            </div>
          </section>

          <section className="space-y-6">
            <div className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-6">
              <div className="flex items-center gap-2 mb-4">
                <AudioLines className="w-5 h-5 text-cyan-300" />
                <h2 className="text-lg font-semibold text-white">Recent Voice Sessions</h2>
              </div>
              <div className="space-y-3 max-h-[320px] overflow-y-auto pr-1">
                {voiceProjects.length === 0 && <div className="rounded-2xl border border-dashed border-white/10 bg-black/20 p-4 text-sm text-gray-400">No voice sessions saved yet.</div>}
                {voiceProjects.map((project) => (
                  <button key={project.id} onClick={() => project.linkedDraftId && selectDraft(project.linkedDraftId)} className="w-full rounded-2xl border border-white/10 bg-black/20 p-4 text-left hover:bg-white/5">
                    <div className="text-sm font-medium text-white">{project.title}</div>
                    <div className="mt-1 text-xs text-gray-400">{project.voiceName || "Voice TBD"} · {project.status}</div>
                  </button>
                ))}
              </div>
            </div>

            <div className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-6">
              <div className="flex items-center gap-2 mb-4">
                <Download className="w-5 h-5 text-cyan-300" />
                <h2 className="text-lg font-semibold text-white">Available Audio Assets</h2>
              </div>
              <div className="space-y-3 max-h-[220px] overflow-y-auto pr-1">
                {audioAssets.length === 0 && <div className="rounded-2xl border border-dashed border-white/10 bg-black/20 p-4 text-sm text-gray-400">No audio assets in the library yet.</div>}
                {audioAssets.map((asset) => (
                  <button key={asset.id} onClick={() => setSelectedAudioId(asset.id)} className={`w-full rounded-2xl border p-4 text-left ${selectedAudio?.id === asset.id ? "border-cyan-400/30 bg-cyan-500/10" : "border-white/10 bg-black/20"}`}>
                    <div className="text-sm font-medium text-white">{asset.name}</div>
                    <div className="text-xs text-gray-400 mt-1">{asset.folder} · {asset.source}</div>
                  </button>
                ))}
              </div>
              {selectedAudio && (
                <div className="mt-4 rounded-2xl border border-white/10 bg-[#0c1016] p-4 space-y-3">
                  <div className="text-sm font-medium text-white">{selectedAudio.name}</div>
                  {selectedAudio.dataUrl ? (
                    <audio controls className="w-full">
                      <source src={selectedAudio.dataUrl} type={selectedAudio.mimeType} />
                    </audio>
                  ) : (
                    <div className="text-xs text-gray-400">This asset is metadata-only, so live preview requires the source file to be embedded.</div>
                  )}
                  <div className="flex gap-3">
                    <button onClick={attachSelectedAudioToDraft} className="flex-1 rounded-2xl bg-gradient-to-r from-cyan-500 to-blue-600 px-4 py-3 text-sm font-medium text-white">Send To Video Draft</button>
                    <button onClick={exportSelectedAudio} disabled={!selectedAudio.dataUrl} className="flex-1 rounded-2xl border border-white/10 bg-white/5 px-4 py-3 text-sm font-medium text-white disabled:opacity-50">Export Audio</button>
                  </div>
                </div>
              )}
            </div>
          </section>
        </div>
      </div>
    </div>
  );
}
