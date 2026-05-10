import { useMemo, useRef, useState } from "react";
import { useNavigate } from "react-router";
import { FolderOpen, Grid3x3, Image as ImageIcon, List, Music, Search, Star, Tag, Trash2, Upload, Video, WandSparkles, Waves } from "lucide-react";
import { createMediaAsset, fileToDataUrl, type AssetKind, useStudio } from "../state/studioStore";

function makeId(prefix: string) {
  if (typeof crypto !== "undefined" && "randomUUID" in crypto) {
    return `${prefix}-${crypto.randomUUID()}`;
  }

  return `${prefix}-${Math.random().toString(36).slice(2, 10)}`;
}

function formatBytes(sizeBytes: number) {
  if (sizeBytes < 1024) return `${sizeBytes} B`;
  if (sizeBytes < 1024 * 1024) return `${(sizeBytes / 1024).toFixed(1)} KB`;
  return `${(sizeBytes / (1024 * 1024)).toFixed(1)} MB`;
}

function toKind(mimeType: string): AssetKind {
  if (mimeType.startsWith("video/")) return "video";
  if (mimeType.startsWith("audio/")) return "audio";
  return "image";
}

function iconForKind(kind: AssetKind) {
  switch (kind) {
    case "video":
      return <Video className="w-5 h-5" />;
    case "audio":
    case "voiceover":
      return <Waves className="w-5 h-5" />;
    case "template":
      return <WandSparkles className="w-5 h-5" />;
    default:
      return <ImageIcon className="w-5 h-5" />;
  }
}

export function MediaLibrary() {
  const navigate = useNavigate();
  const {
    state: { assets, drafts, selectedDraftId },
    addAssets,
    updateAsset,
    removeAsset,
    updateDraft,
  } = useStudio();

  const [viewMode, setViewMode] = useState<"grid" | "list">("grid");
  const [selectedFilter, setSelectedFilter] = useState<"all" | AssetKind>("all");
  const [selectedSource, setSelectedSource] = useState<"all" | "upload" | "ai-generated" | "voice-studio" | "template-extract">("all");
  const [selectedAssetId, setSelectedAssetId] = useState<string | undefined>(assets[0]?.id);
  const [searchQuery, setSearchQuery] = useState("");
  const [uploadFolder, setUploadFolder] = useState("Library");
  const [uploadTags, setUploadTags] = useState("");
  const inputRef = useRef<HTMLInputElement | null>(null);

  const folders = useMemo(
    () => Array.from(new Set(assets.map((asset) => asset.folder))).sort(),
    [assets],
  );

  const filteredMedia = useMemo(
    () =>
      assets.filter((item) => {
        const matchesFilter = selectedFilter === "all" || item.kind === selectedFilter;
        const matchesSource = selectedSource === "all" || item.source === selectedSource;
        const matchesSearch =
          item.name.toLowerCase().includes(searchQuery.toLowerCase()) ||
          item.tags.some((tag) => tag.toLowerCase().includes(searchQuery.toLowerCase())) ||
          item.folder.toLowerCase().includes(searchQuery.toLowerCase());
        return matchesFilter && matchesSource && matchesSearch;
      }),
    [assets, searchQuery, selectedFilter, selectedSource],
  );

  const recentAssets = useMemo(
    () => [...assets].sort((left, right) => right.createdAt.localeCompare(left.createdAt)).slice(0, 6),
    [assets],
  );

  const selectedAsset = assets.find((asset) => asset.id === selectedAssetId) ?? filteredMedia[0];
  const activeDraft = drafts.find((draft) => draft.id === selectedDraftId);

  function insertIntoActiveDraft() {
    if (!selectedAsset) {
      return;
    }

    if (!activeDraft) {
      navigate("/create");
      return;
    }

    updateDraft(activeDraft.id, (current) => {
      const sceneId = current.activeSceneId ?? current.scenes?.[0]?.id ?? makeId("scene");
      const insertedLayerId = makeId("layer");
      const scenes = current.scenes?.length
        ? current.scenes.map((scene) =>
            scene.id === sceneId ? { ...scene, layerIds: [...scene.layerIds, insertedLayerId] } : scene,
          )
        : [
            {
              id: sceneId,
              name: "Scene 1",
              durationMs: current.durationMs,
              background: current.background,
              transition: "cut" as const,
              layerIds: [insertedLayerId],
            },
          ];
      const type = selectedAsset.kind === "audio" ? "audio" : selectedAsset.kind === "video" ? "video" : "asset";

      return {
        ...current,
        activeSceneId: sceneId,
        linkedAssetIds: current.linkedAssetIds.includes(selectedAsset.id) ? current.linkedAssetIds : [...current.linkedAssetIds, selectedAsset.id],
        scenes,
        layers: [
          ...current.layers,
          {
            id: insertedLayerId,
            type,
            name: selectedAsset.name,
            sceneId,
            visible: true,
            locked: false,
            x: Math.round(current.width * 0.18),
            y: Math.round(current.height * 0.18),
            width: type === "audio" ? 360 : Math.round(current.width * 0.38),
            height: type === "audio" ? 72 : Math.round(current.height * 0.28),
            rotation: 0,
            opacity: 1,
            assetId: selectedAsset.id,
            blendMode: "normal",
            filter: "none",
            animation: "none",
            transition: "cut",
            assetFit: "cover",
            cropX: 0,
            cropY: 0,
            cropZoom: 1,
            startMs: 0,
            endMs: current.durationMs,
          },
        ],
      };
    });

    navigate("/create");
  }

  async function handleUpload(files: FileList | null) {
    if (!files || files.length === 0) return;

    const tags = uploadTags.split(",").map((tag) => tag.trim()).filter(Boolean);
    const nextAssets = await Promise.all(
      Array.from(files).map(async (file) => {
        const kind = toKind(file.type);
          const shouldEmbed = (kind === "image" && file.size <= 2 * 1024 * 1024) || (kind === "audio" && file.size <= 8 * 1024 * 1024);
        const dataUrl = shouldEmbed ? await fileToDataUrl(file) : undefined;
        return createMediaAsset({
          name: file.name,
          kind,
          mimeType: file.type,
          sizeBytes: file.size,
          dataUrl,
          storageMode: dataUrl ? "embedded" : "metadata-only",
          folder: uploadFolder.trim() || "Library",
          tags,
          source: "upload",
        });
      }),
    );

    addAssets(nextAssets);
    setSelectedAssetId(nextAssets[0]?.id);
  }

  return (
    <div className="h-full overflow-y-auto bg-[#07080c]">
      <div className="p-8 space-y-6">
        <div>
          <h1 className="text-3xl font-bold text-white mb-2">Media Library</h1>
          <p className="text-gray-400">Manage uploaded media, ATLAS packets, voice direction files, and reusable template assets in one searchable production library.</p>
        </div>

        <div className="grid grid-cols-4 gap-4">
          {["image", "video", "audio", "voiceover"].map((kind) => (
            <div key={kind} className="rounded-2xl border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-5">
              <div className="flex items-center gap-3 mb-3">
                <div className="w-10 h-10 rounded-2xl bg-cyan-500/10 flex items-center justify-center text-cyan-300">{iconForKind(kind as AssetKind)}</div>
                <div className="text-2xl font-semibold text-white">{assets.filter((asset) => asset.kind === kind).length}</div>
              </div>
              <div className="text-sm text-gray-400 capitalize">{kind}</div>
            </div>
          ))}
        </div>

        {recentAssets.length > 0 && (
          <div className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-6">
            <div className="flex items-center justify-between gap-3 mb-4">
              <div>
                <h2 className="text-lg font-semibold text-white">Recent Assets</h2>
                <div className="text-sm text-gray-400">The most recent uploads and generated files in your workspace.</div>
              </div>
              <button onClick={() => navigate("/create")} className="rounded-2xl border border-white/10 bg-white/5 px-4 py-3 text-sm text-white">Open Create Studio</button>
            </div>
            <div className="grid grid-cols-6 gap-3">
              {recentAssets.map((asset) => (
                <button key={asset.id} onClick={() => setSelectedAssetId(asset.id)} className="rounded-2xl border border-white/10 bg-black/20 p-3 text-left hover:border-cyan-400/30">
                  <div className="text-sm font-medium text-white truncate">{asset.name}</div>
                  <div className="mt-1 text-xs text-gray-400 truncate">{asset.folder}</div>
                </button>
              ))}
            </div>
          </div>
        )}

        <div className="grid grid-cols-[1fr_340px] gap-6">
          <section className="space-y-6">
            <div className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-6">
              <div className="grid grid-cols-[1fr_auto_auto] gap-3 mb-4">
                <div className="relative">
                  <Search className="absolute left-4 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
                  <input value={searchQuery} onChange={(event) => setSearchQuery(event.target.value)} placeholder="Search by asset name, folder, or tag" className="w-full rounded-2xl border border-white/10 bg-[#0c1016] pl-11 pr-4 py-3 text-white" />
                </div>
                <select value={selectedFilter} onChange={(event) => setSelectedFilter(event.target.value as "all" | AssetKind)} className="rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white">
                  <option value="all">All kinds</option>
                  <option value="image">Images</option>
                  <option value="video">Videos</option>
                  <option value="audio">Audio</option>
                  <option value="voiceover">Voiceover</option>
                  <option value="template">Template assets</option>
                </select>
                <select value={selectedSource} onChange={(event) => setSelectedSource(event.target.value as typeof selectedSource)} className="rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white">
                  <option value="all">All sources</option>
                  <option value="upload">Uploads</option>
                  <option value="ai-generated">ATLAS</option>
                  <option value="voice-studio">Voice Studio</option>
                  <option value="template-extract">Template</option>
                </select>
              </div>

              <div className="grid grid-cols-[1fr_220px_220px_auto] gap-3 mb-4">
                <input value={uploadFolder} onChange={(event) => setUploadFolder(event.target.value)} placeholder="Upload folder" className="rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white" />
                <input value={uploadTags} onChange={(event) => setUploadTags(event.target.value)} placeholder="tag1, tag2" className="rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white" />
                <div className="rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-sm text-gray-400 flex items-center gap-2"><FolderOpen className="w-4 h-4" />{folders.length} folders</div>
                <div className="flex items-center gap-2 justify-end">
                  <div className="flex rounded-2xl border border-white/10 bg-[#0c1016] p-1">
                    <button onClick={() => setViewMode("grid")} className={`p-2 rounded-xl ${viewMode === "grid" ? "bg-cyan-500/10 text-cyan-300" : "text-gray-400"}`}><Grid3x3 className="w-4 h-4" /></button>
                    <button onClick={() => setViewMode("list")} className={`p-2 rounded-xl ${viewMode === "list" ? "bg-cyan-500/10 text-cyan-300" : "text-gray-400"}`}><List className="w-4 h-4" /></button>
                  </div>
                  <input ref={inputRef} type="file" multiple accept="image/*,video/*,audio/*" className="hidden" onChange={(event) => void handleUpload(event.target.files)} />
                  <button onClick={() => inputRef.current?.click()} className="rounded-2xl bg-gradient-to-r from-cyan-500 to-blue-600 px-4 py-3 text-sm font-medium text-white flex items-center gap-2"><Upload className="w-4 h-4" />Upload</button>
                </div>
              </div>

              {viewMode === "grid" ? (
                <div className="grid grid-cols-4 gap-4">
                  {filteredMedia.map((item) => (
                    <button key={item.id} onClick={() => setSelectedAssetId(item.id)} className={`rounded-2xl overflow-hidden border transition-all text-left ${selectedAsset?.id === item.id ? "border-cyan-400/30 bg-cyan-500/10" : "border-white/10 bg-black/20 hover:bg-white/5"}`}>
                      <div className="aspect-square bg-[#0c1016] relative flex items-center justify-center overflow-hidden">
                        {item.dataUrl ? <img src={item.dataUrl} alt={item.name} className="w-full h-full object-cover" /> : <div className="text-gray-500">{iconForKind(item.kind)}</div>}
                        <button onClick={(event) => { event.stopPropagation(); updateAsset(item.id, { favorite: !item.favorite }); }} className="absolute top-3 right-3 w-8 h-8 rounded-full bg-black/50 flex items-center justify-center text-white"> <Star className={`w-4 h-4 ${item.favorite ? "fill-white text-yellow-300" : ""}`} /></button>
                      </div>
                      <div className="p-4">
                        <div className="text-sm font-medium text-white truncate mb-1">{item.name}</div>
                        <div className="text-xs text-gray-400">{item.folder} · {item.source}</div>
                      </div>
                    </button>
                  ))}
                </div>
              ) : (
                <div className="rounded-2xl border border-white/10 overflow-hidden">
                  {filteredMedia.map((item) => (
                    <button key={item.id} onClick={() => setSelectedAssetId(item.id)} className="w-full grid grid-cols-[52px_1fr_140px_120px] gap-4 items-center px-5 py-3 text-left border-b border-white/5 last:border-b-0 hover:bg-white/5">
                      <div className="w-10 h-10 rounded-xl bg-[#0c1016] flex items-center justify-center text-gray-400">{iconForKind(item.kind)}</div>
                      <div>
                        <div className="text-sm font-medium text-white">{item.name}</div>
                        <div className="text-xs text-gray-400">{item.folder} · {item.tags.join(", ") || "No tags"}</div>
                      </div>
                      <div className="text-sm text-gray-400">{formatBytes(item.sizeBytes)}</div>
                      <div className="text-sm text-gray-400">{item.source}</div>
                    </button>
                  ))}
                </div>
              )}

              {filteredMedia.length === 0 && <div className="rounded-2xl border border-dashed border-white/10 bg-black/20 p-10 text-center text-gray-400">No assets match the current filters.</div>}
            </div>
          </section>

          <aside className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-6 h-fit">
            <div className="flex items-center gap-2 mb-4">
              <Tag className="w-5 h-5 text-cyan-300" />
              <h2 className="text-lg font-semibold text-white">Asset Inspector</h2>
            </div>
            {!selectedAsset ? (
              <div className="text-sm text-gray-400">Select an asset to inspect metadata.</div>
            ) : (
              <div className="space-y-4">
                <div className="rounded-2xl border border-white/10 bg-[#0c1016] p-4">
                  <div className="text-sm font-medium text-white mb-1">{selectedAsset.name}</div>
                  <div className="text-xs text-gray-400">{selectedAsset.kind} · {selectedAsset.mimeType}</div>
                </div>
                <input value={selectedAsset.folder} onChange={(event) => updateAsset(selectedAsset.id, { folder: event.target.value })} className="w-full rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white" />
                <textarea value={selectedAsset.tags.join(", ")} onChange={(event) => updateAsset(selectedAsset.id, { tags: event.target.value.split(",").map((tag) => tag.trim()).filter(Boolean) })} className="w-full h-24 rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white resize-none" />
                {selectedAsset.transcript && <div className="rounded-2xl border border-white/10 bg-[#0c1016] p-4 text-sm text-gray-300 whitespace-pre-wrap max-h-[220px] overflow-y-auto">{selectedAsset.transcript}</div>}
                <div className="grid grid-cols-2 gap-3 text-sm">
                  <div className="rounded-2xl border border-white/10 bg-[#0c1016] p-4 text-gray-300">{formatBytes(selectedAsset.sizeBytes)}</div>
                  <div className="rounded-2xl border border-white/10 bg-[#0c1016] p-4 text-gray-300">{selectedAsset.source}</div>
                </div>
                <div className="flex gap-3">
                  <button onClick={insertIntoActiveDraft} className="flex-1 rounded-2xl border border-cyan-400/20 bg-cyan-500/10 px-4 py-3 text-sm text-cyan-100 flex items-center justify-center gap-2">Insert To Active Draft</button>
                  <button onClick={() => updateAsset(selectedAsset.id, { favorite: !selectedAsset.favorite })} className="flex-1 rounded-2xl border border-white/10 bg-white/5 px-4 py-3 text-sm text-white flex items-center justify-center gap-2"><Star className="w-4 h-4" />Favorite</button>
                  <button onClick={() => removeAsset(selectedAsset.id)} className="flex-1 rounded-2xl border border-red-400/20 bg-red-500/10 px-4 py-3 text-sm text-red-200 flex items-center justify-center gap-2"><Trash2 className="w-4 h-4" />Delete</button>
                </div>
              </div>
            )}
          </aside>
        </div>
      </div>
    </div>
  );
}
