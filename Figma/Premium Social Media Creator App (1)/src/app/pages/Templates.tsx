import { useMemo, useState } from "react";
import { useNavigate } from "react-router";
import { Filter, LayoutTemplate, Search, Sparkles } from "lucide-react";
import { FORMAT_PRESETS, createDraftFromPreset, useStudio } from "../state/studioStore";

export function Templates() {
  const navigate = useNavigate();
  const {
    state: { drafts },
    saveDraft,
    selectDraft,
  } = useStudio();

  const [searchQuery, setSearchQuery] = useState("");
  const [platformFilter, setPlatformFilter] = useState("all");
  const [contentTypeFilter, setContentTypeFilter] = useState("all");
  const [motionFilter, setMotionFilter] = useState<"all" | "motion" | "static">("all");

  const savedTemplates = drafts.filter((draft) => draft.savedAsTemplate);
  const availablePlatforms = Array.from(new Set(savedTemplates.flatMap((draft) => draft.linkedPlatformIds))).sort();
  const availableContentTypes = Array.from(new Set(savedTemplates.map((draft) => draft.contentType))).sort();

  const filteredTemplates = useMemo(
    () =>
      savedTemplates.filter((template) => {
        const matchesSearch =
          template.title.toLowerCase().includes(searchQuery.toLowerCase()) ||
          template.tags.some((tag) => tag.toLowerCase().includes(searchQuery.toLowerCase())) ||
          (template.campaign || "").toLowerCase().includes(searchQuery.toLowerCase());
        const matchesPlatform = platformFilter === "all" || template.linkedPlatformIds.includes(platformFilter as never);
        const matchesType = contentTypeFilter === "all" || template.contentType === contentTypeFilter;
        const matchesMotion =
          motionFilter === "all" ||
          (motionFilter === "motion" ? template.durationMs > 5000 : template.durationMs <= 5000);
        return matchesSearch && matchesPlatform && matchesType && matchesMotion;
      }),
    [contentTypeFilter, motionFilter, platformFilter, savedTemplates, searchQuery],
  );

  function openStarter(formatId: string) {
    const nextDraft = createDraftFromPreset(formatId);
    saveDraft(nextDraft);
    selectDraft(nextDraft.id);
    navigate(nextDraft.durationMs > 5000 ? "/video-studio" : "/create");
  }

  function openTemplate(templateId: string) {
    const template = savedTemplates.find((draft) => draft.id === templateId);
    if (!template) return;

    const duplicate = {
      ...template,
      id: `${template.id}-copy-${Date.now()}`,
      title: `${template.title} Copy`,
      savedAsTemplate: false,
      status: "draft" as const,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      scheduledFor: undefined,
      version: template.version + 1,
    };

    saveDraft(duplicate);
    selectDraft(duplicate.id);
    navigate(duplicate.durationMs > 5000 ? "/video-studio" : "/create");
  }

  return (
    <div className="h-full overflow-y-auto bg-[#07080c]">
      <div className="p-8 space-y-6">
        <div>
          <h1 className="text-3xl font-bold text-white mb-2">Templates</h1>
          <p className="text-gray-400">Use real format starters and studio-saved templates. Filters operate on actual platform, campaign, type, and motion metadata.</p>
        </div>

        <section className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-6">
          <div className="grid grid-cols-[1fr_180px_180px_180px] gap-3 mb-5">
            <div className="relative">
              <Search className="absolute left-4 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
              <input value={searchQuery} onChange={(event) => setSearchQuery(event.target.value)} placeholder="Search templates by title, tag, or campaign" className="w-full rounded-2xl border border-white/10 bg-[#0c1016] pl-11 pr-4 py-3 text-white" />
            </div>
            <select value={platformFilter} onChange={(event) => setPlatformFilter(event.target.value)} className="rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white">
              <option value="all">All platforms</option>
              {availablePlatforms.map((platform) => <option key={platform} value={platform}>{platform}</option>)}
            </select>
            <select value={contentTypeFilter} onChange={(event) => setContentTypeFilter(event.target.value)} className="rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white">
              <option value="all">All types</option>
              {availableContentTypes.map((type) => <option key={type} value={type}>{type}</option>)}
            </select>
            <select value={motionFilter} onChange={(event) => setMotionFilter(event.target.value as typeof motionFilter)} className="rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white">
              <option value="all">Any motion state</option>
              <option value="motion">Motion</option>
              <option value="static">Static</option>
            </select>
          </div>

          <div className="grid grid-cols-5 gap-4">
            {FORMAT_PRESETS.map((preset) => (
              <button key={preset.id} onClick={() => openStarter(preset.id)} className="rounded-2xl border border-white/10 bg-black/20 p-5 text-left hover:border-cyan-400/25 transition-colors">
                <div className="flex items-center gap-2 text-cyan-300 text-xs uppercase tracking-[0.2em] mb-3"><Sparkles className="w-3.5 h-3.5" />Starter</div>
                <div className="text-lg font-semibold text-white mb-2">{preset.label}</div>
                <div className="text-sm text-gray-400 mb-3">{preset.width} × {preset.height}</div>
                <div className="flex flex-wrap gap-2">
                  {preset.recommendedPlatforms.map((platform) => (
                    <span key={platform} className="px-2 py-1 rounded-full border border-white/10 bg-white/5 text-xs text-gray-300">{platform}</span>
                  ))}
                </div>
              </button>
            ))}
          </div>
        </section>

        <section className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-6">
          <div className="flex items-center gap-2 mb-5">
            <LayoutTemplate className="w-5 h-5 text-cyan-300" />
            <h2 className="text-lg font-semibold text-white">Saved Studio Templates</h2>
            <div className="ml-auto text-sm text-gray-400 flex items-center gap-2"><Filter className="w-4 h-4" />{filteredTemplates.length} visible</div>
          </div>

          {filteredTemplates.length === 0 ? (
            <div className="rounded-2xl border border-dashed border-white/10 bg-black/20 p-10 text-center text-gray-400">
              No saved templates match the current filters.
            </div>
          ) : (
            <div className="grid grid-cols-4 gap-4">
              {filteredTemplates.map((template) => (
                <button key={template.id} onClick={() => openTemplate(template.id)} className="rounded-2xl overflow-hidden border border-white/10 bg-black/20 text-left hover:border-cyan-400/25 transition-colors">
                  <div className="aspect-[4/5] bg-[linear-gradient(145deg,rgba(34,211,238,0.12),rgba(217,70,239,0.12))] p-5 flex flex-col justify-between">
                    <div className="text-xs uppercase tracking-[0.2em] text-cyan-300">{template.contentType}</div>
                    <div>
                      <div className="text-2xl font-semibold text-white mb-2">{template.title}</div>
                      <div className="text-sm text-gray-300">{template.width} × {template.height}</div>
                    </div>
                  </div>
                  <div className="p-4 space-y-2">
                    <div className="text-xs text-gray-400">{template.campaign || "Independent template"}</div>
                    <div className="flex flex-wrap gap-2">
                      {template.linkedPlatformIds.map((platform) => <span key={platform} className="px-2 py-1 rounded-full border border-white/10 bg-white/5 text-xs text-gray-300">{platform}</span>)}
                    </div>
                    {template.tags.length > 0 && <div className="text-xs text-gray-500">{template.tags.join(" · ")}</div>}
                  </div>
                </button>
              ))}
            </div>
          )}
        </section>
      </div>
    </div>
  );
}
