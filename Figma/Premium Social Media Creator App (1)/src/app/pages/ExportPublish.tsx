import { useEffect, useMemo, useState } from "react";
import { CheckCircle2, Clock, Download, Sparkles } from "lucide-react";
import { FORMAT_PRESETS, useStudio } from "../state/studioStore";

const exportPresets = [
  { id: 1, name: "Platform Native", description: "Uses the current draft format and output recommendations." },
  { id: 2, name: "High Fidelity Review", description: "Exports a structured package for review and QA." },
  { id: 3, name: "Handoff Bundle", description: "Exports draft metadata for downstream publishing services." },
];

function defaultScheduleParts() {
  const nextHour = new Date(Date.now() + 1000 * 60 * 60);
  const date = nextHour.toISOString().slice(0, 10);
  const time = nextHour.toTimeString().slice(0, 5);
  return { date, time };
}

export function ExportPublish() {
  const {
    state: { drafts, accounts, selectedDraftId },
    updateDraft,
    selectDraft,
  } = useStudio();

  const initialSchedule = defaultScheduleParts();
  const [selectedPreset, setSelectedPreset] = useState(exportPresets[0].id);
  const [selectedPlatforms, setSelectedPlatforms] = useState<string[]>([]);
  const [publishType, setPublishType] = useState<"now" | "schedule">("now");
  const [scheduleDate, setScheduleDate] = useState(initialSchedule.date);
  const [scheduleTime, setScheduleTime] = useState(initialSchedule.time);

  const selectedDraft = drafts.find((draft) => draft.id === selectedDraftId) ?? drafts[0];
  const caption = selectedDraft?.caption ?? "";
  const activePreset = useMemo(
    () => FORMAT_PRESETS.find((preset) => preset.id === selectedDraft?.formatId) ?? FORMAT_PRESETS[0],
    [selectedDraft?.formatId],
  );
  const configuredAccounts = accounts.filter((account) => account.authStatus !== "not-configured");

  useEffect(() => {
    if (!selectedDraft) {
      setSelectedPlatforms([]);
      return;
    }

    setSelectedPlatforms(selectedDraft.linkedPlatformIds);
  }, [selectedDraft?.id, selectedDraft?.linkedPlatformIds]);

  function togglePlatform(platform: string) {
    setSelectedPlatforms((previous) =>
      previous.includes(platform)
        ? previous.filter((item) => item !== platform)
        : [...previous, platform],
    );
  }

  function saveSchedule() {
    if (!selectedDraft) return;
    updateDraft(selectedDraft.id, (draft) => ({
      ...draft,
      linkedPlatformIds: selectedPlatforms as never,
      status: "scheduled",
      scheduledFor: new Date(`${scheduleDate}T${scheduleTime}:00`).toISOString(),
    }));
  }

  function markPublished() {
    if (!selectedDraft) return;
    updateDraft(selectedDraft.id, (draft) => ({
      ...draft,
      linkedPlatformIds: selectedPlatforms as never,
      status: "published-manual",
      scheduledFor: undefined,
    }));
  }

  function exportDraft() {
    if (!selectedDraft) return;
    const payload = {
      draft: selectedDraft,
      exportPreset: selectedPreset,
      exportFormats: activePreset.exportFormats,
      selectedPlatforms,
      generatedAt: new Date().toISOString(),
    };

    const blob = new Blob([JSON.stringify(payload, null, 2)], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = `${selectedDraft.title.replace(/\s+/g, "-").toLowerCase()}-export.json`;
    anchor.click();
    URL.revokeObjectURL(url);
  }

  return (
    <div className="h-full overflow-y-auto bg-[#0a0a0a]">
      <div className="p-8 space-y-6">
        <div>
          <h1 className="text-3xl font-bold text-white mb-2">Export & Publish</h1>
          <p className="text-gray-400">
            Export real draft packages, attach real schedules, and track manual publishing without pretending Atlas already sent the post.
          </p>
        </div>

        {!selectedDraft && (
          <div className="rounded-xl border border-dashed border-white/10 bg-black/20 p-6 text-gray-400">
            No draft selected. Open Create Studio first and save a draft to export or schedule.
          </div>
        )}

        <div className="grid grid-cols-[1fr_400px] gap-6">
          <div className="space-y-6">
            <section className="bg-gradient-to-br from-[#1a1a1a] to-[#0f0f0f] border border-white/10 rounded-xl p-6">
              <h2 className="text-lg font-semibold text-white mb-4">Active Draft</h2>
              {selectedDraft ? (
                <div className="grid grid-cols-2 gap-4">
                  <div className="bg-black/20 border border-white/10 rounded-xl p-4">
                    <div className="text-xs text-gray-400 mb-1">Title</div>
                    <div className="text-white font-medium">{selectedDraft.title}</div>
                  </div>
                  <div className="bg-black/20 border border-white/10 rounded-xl p-4">
                    <div className="text-xs text-gray-400 mb-1">Format</div>
                    <div className="text-white font-medium">{activePreset.label}</div>
                  </div>
                  <div className="bg-black/20 border border-white/10 rounded-xl p-4">
                    <div className="text-xs text-gray-400 mb-1">Scene Count</div>
                    <div className="text-white font-medium">{selectedDraft.scenes?.length ?? 0}</div>
                  </div>
                  <div className="bg-black/20 border border-white/10 rounded-xl p-4">
                    <div className="text-xs text-gray-400 mb-1">Layer Count</div>
                    <div className="text-white font-medium">{selectedDraft.layers.length}</div>
                  </div>
                </div>
              ) : (
                <div className="text-sm text-gray-400">No draft selected yet.</div>
              )}
            </section>

            <section className="bg-gradient-to-br from-[#1a1a1a] to-[#0f0f0f] border border-white/10 rounded-xl p-6">
              <h2 className="text-lg font-semibold text-white mb-4">Export Presets</h2>
              <div className="grid grid-cols-3 gap-3">
                {exportPresets.map((presetItem) => (
                  <button
                    key={presetItem.id}
                    onClick={() => setSelectedPreset(presetItem.id)}
                    className={`p-4 rounded-lg text-left transition-all ${
                      selectedPreset === presetItem.id
                        ? "bg-violet-500/20 border-2 border-violet-500/50"
                        : "bg-white/5 border border-white/10 hover:border-white/20"
                    }`}
                  >
                    <span className="text-sm font-medium text-white block mb-2">{presetItem.name}</span>
                    <span className="text-xs text-gray-400 leading-relaxed">{presetItem.description}</span>
                  </button>
                ))}
              </div>
            </section>

            <section className="bg-gradient-to-br from-[#1a1a1a] to-[#0f0f0f] border border-white/10 rounded-xl p-6">
              <h2 className="text-lg font-semibold text-white mb-4">Select Publish Targets</h2>
              <div className="grid grid-cols-3 gap-3">
                {configuredAccounts.map((account) => (
                  <button
                    key={account.id}
                    onClick={() => togglePlatform(account.platformId)}
                    className={`p-4 rounded-lg transition-all ${
                      selectedPlatforms.includes(account.platformId)
                        ? "bg-violet-500/20 border-2 border-violet-500/50"
                        : "bg-white/5 border border-white/10 hover:border-white/20"
                    }`}
                  >
                    <div className="text-sm font-medium text-white">{account.displayName}</div>
                    <div className="text-xs text-gray-400 mt-1">{account.handle}</div>
                    {selectedPlatforms.includes(account.platformId) && (
                      <CheckCircle2 className="w-4 h-4 text-green-400 mx-auto mt-3" />
                    )}
                  </button>
                ))}
                {configuredAccounts.length === 0 && (
                  <div className="col-span-3 rounded-lg border border-dashed border-white/10 bg-black/20 p-4 text-sm text-gray-400">
                    Configure platform accounts first in Accounts before attaching publish targets.
                  </div>
                )}
              </div>
            </section>

            <section className="bg-gradient-to-br from-[#1a1a1a] to-[#0f0f0f] border border-white/10 rounded-xl p-6">
              <div className="flex items-center justify-between mb-4">
                <h2 className="text-lg font-semibold text-white">Caption & Description</h2>
                <div className="px-3 py-1.5 bg-violet-500/20 text-violet-400 rounded-lg text-xs font-medium border border-violet-500/30 flex items-center gap-2">
                  <Sparkles className="w-3.5 h-3.5" />
                  Refine in ATLAS AI when ready
                </div>
              </div>
              <textarea
                value={caption}
                onChange={(event) => selectedDraft && updateDraft(selectedDraft.id, (draft) => ({ ...draft, caption: event.target.value }))}
                placeholder="Write your caption here if this draft needs one."
                className="w-full h-32 bg-[#0f0f0f] border border-white/10 rounded-lg px-4 py-3 text-white placeholder-gray-500 focus:outline-none focus:border-violet-500/50 resize-none"
              />
              <div className="text-sm text-gray-400 mt-3">{caption.length} characters</div>
            </section>
          </div>

          <div className="space-y-6">
            <section className="bg-gradient-to-br from-[#1a1a1a] to-[#0f0f0f] border border-white/10 rounded-xl p-6">
              <h2 className="text-lg font-semibold text-white mb-4">Export Summary</h2>
              {selectedDraft ? (
                <div className="space-y-3 text-sm">
                  <div className="flex items-center justify-between bg-[#0f0f0f] border border-white/10 rounded-lg px-4 py-3">
                    <span className="text-gray-400">Content Type</span>
                    <span className="text-white font-medium">{selectedDraft.contentType}</span>
                  </div>
                  <div className="flex items-center justify-between bg-[#0f0f0f] border border-white/10 rounded-lg px-4 py-3">
                    <span className="text-gray-400">Output Size</span>
                    <span className="text-white font-medium">{selectedDraft.width} × {selectedDraft.height}</span>
                  </div>
                  <div className="flex items-center justify-between bg-[#0f0f0f] border border-white/10 rounded-lg px-4 py-3">
                    <span className="text-gray-400">Supported Formats</span>
                    <span className="text-white font-medium">{activePreset.exportFormats.join(", ")}</span>
                  </div>
                  <div className="flex items-center justify-between bg-[#0f0f0f] border border-white/10 rounded-lg px-4 py-3">
                    <span className="text-gray-400">Selected Targets</span>
                    <span className="text-white font-medium">{selectedPlatforms.length}</span>
                  </div>
                </div>
              ) : (
                <div className="text-sm text-gray-400">Select a draft to see the export summary.</div>
              )}
            </section>

            <section className="bg-gradient-to-br from-[#1a1a1a] to-[#0f0f0f] border border-white/10 rounded-xl p-6">
              <h2 className="text-lg font-semibold text-white mb-4">Publish Options</h2>

              <div className="space-y-3 mb-6">
                <button
                  onClick={() => setPublishType("now")}
                  className={`w-full p-4 rounded-lg text-left transition-all ${
                    publishType === "now"
                      ? "bg-violet-500/20 border-2 border-violet-500/50"
                      : "bg-white/5 border border-white/10 hover:border-white/20"
                  }`}
                >
                  <div className="text-sm font-medium text-white">Manual Publish Tracking</div>
                  <div className="text-xs text-gray-400 mt-1">Use this after the post is published outside Atlas and you want the record tracked here.</div>
                </button>

                <button
                  onClick={() => setPublishType("schedule")}
                  className={`w-full p-4 rounded-lg text-left transition-all ${
                    publishType === "schedule"
                      ? "bg-violet-500/20 border-2 border-violet-500/50"
                      : "bg-white/5 border border-white/10 hover:border-white/20"
                  }`}
                >
                  <div className="text-sm font-medium text-white">Schedule Draft</div>
                  <div className="text-xs text-gray-400 mt-1">Attach a real date and time without pretending the publish API already succeeded.</div>
                </button>
              </div>

              {publishType === "schedule" && (
                <div className="space-y-3 mb-6 p-4 bg-[#0f0f0f] border border-white/10 rounded-lg">
                  <div>
                    <label className="text-sm text-gray-400 mb-2 block">Date</label>
                    <input
                      type="date"
                      value={scheduleDate}
                      onChange={(event) => setScheduleDate(event.target.value)}
                      className="w-full bg-[#0a0a0a] border border-white/10 rounded-lg px-3 py-2 text-white"
                    />
                  </div>
                  <div>
                    <label className="text-sm text-gray-400 mb-2 block">Time</label>
                    <input
                      type="time"
                      value={scheduleTime}
                      onChange={(event) => setScheduleTime(event.target.value)}
                      className="w-full bg-[#0a0a0a] border border-white/10 rounded-lg px-3 py-2 text-white"
                    />
                  </div>
                </div>
              )}

              <div className="space-y-3">
                <button
                  onClick={() => (publishType === "schedule" ? saveSchedule() : markPublished())}
                  disabled={!selectedDraft || selectedPlatforms.length === 0}
                  className="w-full bg-gradient-to-r from-violet-500 to-fuchsia-500 text-white px-6 py-3 rounded-lg font-medium disabled:opacity-50 flex items-center justify-center gap-2"
                >
                  {publishType === "now" ? (
                    <>
                      <CheckCircle2 className="w-5 h-5" />
                      Mark As Manually Published
                    </>
                  ) : (
                    <>
                      <Clock className="w-5 h-5" />
                      Save Schedule
                    </>
                  )}
                </button>

                <button
                  onClick={exportDraft}
                  disabled={!selectedDraft}
                  className="w-full bg-white/5 border border-white/10 text-white px-6 py-3 rounded-lg font-medium hover:bg-white/10 transition-all flex items-center justify-center gap-2 disabled:opacity-50"
                >
                  <Download className="w-5 h-5" />
                  Export Draft Package
                </button>
              </div>
            </section>

            <section className="bg-gradient-to-br from-violet-500/10 to-fuchsia-500/10 border border-violet-500/30 rounded-xl p-6">
              <h3 className="text-sm font-semibold text-white mb-3">Readiness Checklist</h3>
              <div className="space-y-2">
                {[
                  { label: "Draft selected", checked: Boolean(selectedDraft) },
                  { label: "Targets selected", checked: selectedPlatforms.length > 0 },
                  { label: "Caption added", checked: caption.length > 0 },
                  { label: "Format available", checked: Boolean(activePreset) },
                ].map((item) => (
                  <div key={item.label} className="flex items-center gap-2 text-sm">
                    <CheckCircle2 className={`w-4 h-4 ${item.checked ? "text-green-400" : "text-gray-600"}`} />
                    <span className={item.checked ? "text-gray-300" : "text-gray-500"}>{item.label}</span>
                  </div>
                ))}
              </div>
            </section>

            <section className="bg-[#10131b] border border-white/10 rounded-xl p-4">
              <label className="text-sm text-gray-400 mb-2 block">Switch Draft</label>
              <select
                value={selectedDraft?.id ?? ""}
                onChange={(event) => selectDraft(event.target.value)}
                className="w-full bg-[#0a0a0a] border border-white/10 rounded-lg px-3 py-2 text-white"
              >
                {drafts.map((draft) => (
                  <option key={draft.id} value={draft.id}>
                    {draft.title}
                  </option>
                ))}
              </select>
            </section>
          </div>
        </div>
      </div>
    </div>
  );
}
