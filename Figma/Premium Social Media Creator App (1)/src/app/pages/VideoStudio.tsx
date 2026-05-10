import { useMemo, useState } from "react";
import { useNavigate } from "react-router";
import { Clapperboard, Plus, Scissors, Subtitles, Volume2 } from "lucide-react";
import { getFormatPreset, type DraftLayer, type DraftScene, useStudio } from "../state/studioStore";

function makeId(prefix: string) {
  if (typeof crypto !== "undefined" && "randomUUID" in crypto) {
    return `${prefix}-${crypto.randomUUID()}`;
  }

  return `${prefix}-${Math.random().toString(36).slice(2, 10)}`;
}

function sceneLayers(layers: DraftLayer[], sceneId?: string) {
  return layers.filter((layer) => layer.sceneId === sceneId);
}

function reorder<T>(items: T[], fromIndex: number, toIndex: number) {
  const copy = [...items];
  const [item] = copy.splice(fromIndex, 1);
  copy.splice(toIndex, 0, item);
  return copy;
}

export function VideoStudio() {
  const navigate = useNavigate();
  const {
    state: { drafts, assets, selectedDraftId },
    updateDraft,
    selectDraft,
  } = useStudio();

  const videoDrafts = useMemo(
    () => drafts.filter((draft) => draft.durationMs > 5000 || draft.layers.some((layer) => layer.type === "video" || layer.type === "audio") || draft.scenes && draft.scenes.length > 1),
    [drafts],
  );

  const draft = videoDrafts.find((item) => item.id === selectedDraftId) ?? videoDrafts[0];
  const [selectedSceneId, setSelectedSceneId] = useState<string | undefined>(draft?.activeSceneId ?? draft?.scenes?.[0]?.id);

  const scenes = draft?.scenes ?? [];
  const activeScene = scenes.find((scene) => scene.id === selectedSceneId) ?? scenes[0];
  const activeLayers = draft ? sceneLayers(draft.layers, activeScene?.id) : [];
  const audioAssets = assets.filter((asset) => asset.kind === "audio" || asset.kind === "voiceover");
  const preset = draft ? getFormatPreset(draft.formatId) : undefined;

  function mutateScenes(nextScenes: DraftScene[]) {
    if (!draft) return;
    updateDraft(draft.id, (current) => ({
      ...current,
      scenes: nextScenes,
      activeSceneId: nextScenes[0]?.id,
      pipelineStage: current.pipelineStage === "concept" ? "design" : current.pipelineStage,
    }));
    setSelectedSceneId(nextScenes[0]?.id);
  }

  function updateScene(sceneId: string, updater: (scene: DraftScene) => DraftScene) {
    if (!draft) return;
    updateDraft(draft.id, (current) => ({
      ...current,
      scenes: (current.scenes ?? []).map((scene) => (scene.id === sceneId ? updater(scene) : scene)),
      pipelineStage: "design",
    }));
  }

  function splitScene(sceneId: string) {
    if (!draft) return;
    const sceneIndex = scenes.findIndex((scene) => scene.id === sceneId);
    const scene = scenes[sceneIndex];
    if (!scene) return;

    const firstDuration = Math.max(1000, Math.round(scene.durationMs / 2));
    const secondSceneId = makeId("scene");
    const secondScene: DraftScene = {
      ...scene,
      id: secondSceneId,
      name: `${scene.name} B`,
      durationMs: scene.durationMs - firstDuration,
      layerIds: [...scene.layerIds],
    };

    const nextScenes = [...scenes];
    nextScenes.splice(sceneIndex, 1, { ...scene, durationMs: firstDuration }, secondScene);
    mutateScenes(nextScenes);
  }

  function addScene() {
    if (!draft) return;
    const nextScene: DraftScene = {
      id: makeId("scene"),
      name: `Scene ${(draft.scenes?.length ?? 0) + 1}`,
      durationMs: 4000,
      background: draft.background,
      transition: "cut",
      layerIds: [],
    };

    mutateScenes([...(draft.scenes ?? []), nextScene]);
  }

  function addCaptionLayer() {
    if (!draft || !activeScene) return;
    const layerId = makeId("layer");
    updateDraft(draft.id, (current) => ({
      ...current,
      layers: [
        ...current.layers,
        {
          id: layerId,
          type: "text",
          name: `Caption ${activeLayers.filter((layer) => layer.type === "text").length + 1}`,
          sceneId: activeScene.id,
          visible: true,
          locked: false,
          x: 84,
          y: current.height - 260,
          width: current.width - 168,
          height: 120,
          rotation: 0,
          opacity: 100,
          text: "Add caption or subtitle line",
          fontSize: 44,
          fontWeight: 700,
          color: "#ffffff",
          animation: "type-on",
          startMs: 0,
          endMs: activeScene.durationMs,
        },
      ],
      pipelineStage: "design",
    }));
  }

  function attachAudio(assetId: string) {
    if (!draft || !activeScene) return;
    const asset = audioAssets.find((item) => item.id === assetId);
    if (!asset) return;

    updateDraft(draft.id, (current) => ({
      ...current,
      linkedAssetIds: current.linkedAssetIds.includes(asset.id) ? current.linkedAssetIds : [...current.linkedAssetIds, asset.id],
      layers: [
        ...current.layers,
        {
          id: makeId("layer"),
          type: "audio",
          name: asset.name,
          sceneId: activeScene.id,
          visible: true,
          locked: false,
          x: 40,
          y: current.height - 120,
          width: current.width - 80,
          height: 52,
          rotation: 0,
          opacity: 100,
          assetId: asset.id,
          startMs: 0,
          endMs: activeScene.durationMs,
        },
      ],
      pipelineStage: "design",
    }));
  }

  function updateLayerTiming(layerId: string, field: "startMs" | "endMs", value: number) {
    if (!draft) return;
    updateDraft(draft.id, (current) => ({
      ...current,
      layers: current.layers.map((layer) =>
        layer.id === layerId ? { ...layer, [field]: value } : layer,
      ),
      pipelineStage: "design",
    }));
  }

  if (!draft) {
    return (
      <div className="h-full overflow-y-auto bg-[#07080c]">
        <div className="p-8">
          <div className="rounded-[28px] border border-dashed border-white/10 bg-black/20 p-10 text-center text-gray-400">
            No motion draft is ready for Video Studio yet. Open Create Studio first and save a reel, short, story video, or multi-scene draft.
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="h-full overflow-y-auto bg-[#07080c]">
      <div className="p-8 space-y-6">
        <div className="flex items-start justify-between gap-6">
          <div>
            <h1 className="text-3xl font-bold text-white mb-2">Video Studio</h1>
            <p className="text-gray-400">CapCut-style scene editing with a simpler Atlas workflow: timeline, captions, audio attachment, transitions, and safe-format control.</p>
          </div>
          <div className="flex gap-3">
            <select value={draft.id} onChange={(event) => selectDraft(event.target.value)} className="rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white">
              {videoDrafts.map((item) => (
                <option key={item.id} value={item.id}>{item.title}</option>
              ))}
            </select>
            <button onClick={() => navigate("/export")} className="rounded-2xl border border-white/10 bg-white/5 px-4 py-3 text-white text-sm font-medium">Send To Export</button>
          </div>
        </div>

        <div className="grid grid-cols-[280px_1fr_340px] gap-6">
          <section className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-5">
            <div className="flex items-center justify-between mb-4">
              <h2 className="text-lg font-semibold text-white">Scenes</h2>
              <button onClick={addScene} className="w-9 h-9 rounded-xl bg-white/5 border border-white/10 flex items-center justify-center text-white"><Plus className="w-4 h-4" /></button>
            </div>
            <div className="space-y-3">
              {scenes.map((scene, index) => (
                <button key={scene.id} onClick={() => setSelectedSceneId(scene.id)} className={`w-full rounded-2xl border p-4 text-left transition-all ${selectedSceneId === scene.id ? "border-cyan-400/30 bg-cyan-500/10" : "border-white/10 bg-black/20 hover:bg-white/5"}`}>
                  <div className="flex items-center justify-between gap-3 mb-2">
                    <div className="text-sm font-medium text-white">{scene.name}</div>
                    <div className="text-xs text-gray-400">{Math.round(scene.durationMs / 100) / 10}s</div>
                  </div>
                  <div className="text-xs text-gray-400 mb-3">Transition: {scene.transition}</div>
                  <div className="flex gap-2 text-xs">
                    <button onClick={(event) => { event.stopPropagation(); splitScene(scene.id); }} className="rounded-xl border border-white/10 bg-white/5 px-2 py-1 text-gray-300 flex items-center gap-1"><Scissors className="w-3 h-3" />Split</button>
                    {index > 0 && <button onClick={(event) => { event.stopPropagation(); mutateScenes(reorder(scenes, index, index - 1)); }} className="rounded-xl border border-white/10 bg-white/5 px-2 py-1 text-gray-300">Up</button>}
                    {index < scenes.length - 1 && <button onClick={(event) => { event.stopPropagation(); mutateScenes(reorder(scenes, index, index + 1)); }} className="rounded-xl border border-white/10 bg-white/5 px-2 py-1 text-gray-300">Down</button>}
                  </div>
                </button>
              ))}
            </div>
          </section>

          <section className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-6">
            <div className="flex items-center justify-between mb-5">
              <div>
                <div className="text-xs uppercase tracking-[0.2em] text-gray-500 mb-1">Viewport</div>
                <div className="text-lg font-semibold text-white">{draft.title}</div>
              </div>
              <div className="text-sm text-gray-400">{preset?.label} · {draft.width} × {draft.height}</div>
            </div>

            <div className="rounded-[28px] border border-white/10 bg-black/30 p-6 mb-6">
              <div className="mx-auto rounded-[24px] border border-cyan-400/20 bg-gradient-to-br from-[#0f1220] to-[#0a0d14] flex items-center justify-center text-center text-gray-400" style={{ width: 320, aspectRatio: `${draft.width}/${draft.height}` }}>
                <div>
                  <Clapperboard className="w-8 h-8 text-cyan-300 mx-auto mb-3" />
                  <div className="text-white font-medium">{activeScene?.name || "Scene"}</div>
                  <div className="text-sm mt-2">{activeLayers.length} layers on this scene</div>
                </div>
              </div>
            </div>

            <div className="space-y-3">
              <div className="flex items-center justify-between">
                <h2 className="text-lg font-semibold text-white">Track Rack</h2>
                <button onClick={addCaptionLayer} className="rounded-2xl border border-white/10 bg-white/5 px-3 py-2 text-sm text-white flex items-center gap-2"><Subtitles className="w-4 h-4" />Add Caption</button>
              </div>
              {activeLayers.length === 0 ? (
                <div className="rounded-2xl border border-dashed border-white/10 bg-black/20 p-5 text-sm text-gray-400">No layers are assigned to this scene yet.</div>
              ) : (
                activeLayers.map((layer) => (
                  <div key={layer.id} className="rounded-2xl border border-white/10 bg-black/20 p-4">
                    <div className="flex items-center justify-between gap-4 mb-3">
                      <div>
                        <div className="text-sm font-medium text-white">{layer.name}</div>
                        <div className="text-xs text-gray-400">{layer.type} · animation {layer.animation || "none"}</div>
                      </div>
                      <div className="text-xs text-gray-500">blend {layer.blendMode || "normal"}</div>
                    </div>
                    <div className="grid grid-cols-2 gap-3 text-sm">
                      <label className="text-gray-400">
                        Start
                        <input type="number" value={layer.startMs ?? 0} onChange={(event) => updateLayerTiming(layer.id, "startMs", Number(event.target.value))} className="mt-2 w-full rounded-xl border border-white/10 bg-[#0c1016] px-3 py-2 text-white" />
                      </label>
                      <label className="text-gray-400">
                        End
                        <input type="number" value={layer.endMs ?? activeScene?.durationMs ?? draft.durationMs} onChange={(event) => updateLayerTiming(layer.id, "endMs", Number(event.target.value))} className="mt-2 w-full rounded-xl border border-white/10 bg-[#0c1016] px-3 py-2 text-white" />
                      </label>
                    </div>
                  </div>
                ))
              )}
            </div>
          </section>

          <section className="space-y-6">
            <div className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-6">
              <h2 className="text-lg font-semibold text-white mb-4">Scene Controls</h2>
              {activeScene && (
                <div className="space-y-4">
                  <label className="text-sm text-gray-400 block">
                    Scene Name
                    <input value={activeScene.name} onChange={(event) => updateScene(activeScene.id, (scene) => ({ ...scene, name: event.target.value }))} className="mt-2 w-full rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white" />
                  </label>
                  <label className="text-sm text-gray-400 block">
                    Duration (ms)
                    <input type="number" value={activeScene.durationMs} onChange={(event) => updateScene(activeScene.id, (scene) => ({ ...scene, durationMs: Number(event.target.value) }))} className="mt-2 w-full rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white" />
                  </label>
                  <label className="text-sm text-gray-400 block">
                    Transition
                    <select value={activeScene.transition} onChange={(event) => updateScene(activeScene.id, (scene) => ({ ...scene, transition: event.target.value as DraftScene["transition"] }))} className="mt-2 w-full rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white">
                      <option value="cut">cut</option>
                      <option value="fade">fade</option>
                      <option value="slide">slide</option>
                      <option value="zoom">zoom</option>
                    </select>
                  </label>
                </div>
              )}
            </div>

            <div className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-6">
              <div className="flex items-center gap-2 mb-4">
                <Volume2 className="w-5 h-5 text-cyan-300" />
                <h2 className="text-lg font-semibold text-white">Attach Audio</h2>
              </div>
              <div className="space-y-3 max-h-[260px] overflow-y-auto pr-1">
                {audioAssets.length === 0 && <div className="rounded-2xl border border-dashed border-white/10 bg-black/20 p-4 text-sm text-gray-400">Upload audio or create a voice session first.</div>}
                {audioAssets.map((asset) => (
                  <button key={asset.id} onClick={() => attachAudio(asset.id)} className="w-full rounded-2xl border border-white/10 bg-black/20 p-4 text-left hover:bg-white/5 transition-colors">
                    <div className="text-sm font-medium text-white">{asset.name}</div>
                    <div className="text-xs text-gray-400 mt-1">{asset.folder} · {asset.source}</div>
                  </button>
                ))}
              </div>
            </div>
          </section>
        </div>
      </div>
    </div>
  );
}