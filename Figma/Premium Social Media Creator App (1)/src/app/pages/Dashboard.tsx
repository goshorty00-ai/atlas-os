import { useMemo } from "react";
import { useNavigate } from "react-router";
import {
  ArrowRight,
  BrainCircuit,
  Calendar,
  CheckCircle2,
  Clapperboard,
  Layers3,
  Sparkles,
  Upload,
  Waves,
} from "lucide-react";
import { useStudio } from "../state/studioStore";

function formatDate(value?: string) {
  if (!value) {
    return "Not scheduled";
  }

  return new Date(value).toLocaleString([], {
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
  });
}

export function Dashboard() {
  const navigate = useNavigate();
  const {
    state: { drafts, assets, accounts, aiBriefs, profile, voiceProjects },
    createDraft,
    selectDraft,
  } = useStudio();

  const displayName = profile.firstName || profile.company || "creator";
  const scheduledDrafts = drafts.filter((draft) => draft.status === "scheduled");
  const latestDrafts = [...drafts].sort((left, right) => right.updatedAt.localeCompare(left.updatedAt)).slice(0, 4);
  const nextBriefs = aiBriefs.slice(0, 4);
  const campaignCount = new Set(drafts.map((draft) => draft.campaign).filter(Boolean)).size;
  const readiness = useMemo(
    () => [
      { label: "Identity configured", done: Boolean(profile.firstName || profile.company) },
      { label: "Publish accounts ready", done: accounts.length > 0 },
      { label: "Media library started", done: assets.length > 0 },
      { label: "Draft system active", done: drafts.length > 0 },
      { label: "Voice workflow active", done: voiceProjects.length > 0 },
    ],
    [accounts.length, assets.length, drafts.length, profile.company, profile.firstName, voiceProjects.length],
  );

  return (
    <div className="h-full overflow-y-auto bg-[#07080c]">
      <div className="p-8 space-y-8">
        <section className="grid grid-cols-[1.1fr_0.9fr] gap-6">
          <div className="rounded-[28px] border border-white/10 bg-[radial-gradient(circle_at_top_left,_rgba(34,211,238,0.18),_transparent_35%),linear-gradient(180deg,#161b29_0%,#0d1017_100%)] p-8">
            <div className="inline-flex items-center gap-2 rounded-full border border-cyan-400/20 bg-cyan-500/10 px-3 py-1 text-xs uppercase tracking-[0.24em] text-cyan-200">
              <Sparkles className="w-3.5 h-3.5" />
              Atlas Premium Workflow
            </div>
            <h1 className="mt-5 text-4xl font-semibold text-white leading-tight">
              Build, edit, voice, adapt, schedule, and publish from one studio.
            </h1>
            <p className="mt-4 max-w-2xl text-base text-gray-300 leading-relaxed">
              Welcome back, {displayName}. This workspace is now centered on real creative workflows: live drafts, real media assets,
              multi-provider AI routing, voice sessions, and platform-aware publishing across one connected studio.
            </p>
            <div className="mt-6 flex flex-wrap gap-3">
              <button
                onClick={() => {
                  const draftId = createDraft();
                  selectDraft(draftId);
                  navigate("/create");
                }}
                className="px-5 py-3 rounded-2xl bg-gradient-to-r from-cyan-500 to-blue-600 text-white text-sm font-medium flex items-center gap-2"
              >
                <Layers3 className="w-4 h-4" />
                Launch Create Studio
              </button>
              <button
                onClick={() => navigate("/video-studio")}
                className="px-5 py-3 rounded-2xl bg-white/5 border border-white/10 text-white text-sm font-medium flex items-center gap-2"
              >
                <Clapperboard className="w-4 h-4" />
                Open Video Studio
              </button>
              <button
                onClick={() => navigate("/ai-creator")}
                className="px-5 py-3 rounded-2xl bg-white/5 border border-white/10 text-white text-sm font-medium flex items-center gap-2"
              >
                <BrainCircuit className="w-4 h-4" />
                Open ATLAS AI
              </button>
            </div>
          </div>

          <div className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-6">
            <div className="text-sm uppercase tracking-[0.22em] text-gray-500 mb-5">Studio Readiness</div>
            <div className="space-y-3 mb-6">
              {readiness.map((item) => (
                <div key={item.label} className="flex items-center gap-3 rounded-2xl bg-white/5 border border-white/10 px-4 py-3">
                  <CheckCircle2 className={`w-4 h-4 ${item.done ? "text-emerald-400" : "text-gray-600"}`} />
                  <span className={item.done ? "text-gray-200" : "text-gray-400"}>{item.label}</span>
                </div>
              ))}
            </div>
            <button
              onClick={() => navigate("/settings")}
              className="w-full px-4 py-3 rounded-2xl bg-cyan-500/10 border border-cyan-400/20 text-cyan-100 text-sm font-medium"
            >
              Finish Workspace Setup
            </button>
          </div>
        </section>

        <section className="grid grid-cols-5 gap-4">
          {[
            { label: "Drafts", value: drafts.length },
            { label: "Campaigns", value: campaignCount },
            { label: "AI Requests", value: aiBriefs.length },
            { label: "Voice Sessions", value: voiceProjects.length },
            { label: "Assets", value: assets.length },
          ].map((card) => (
            <div key={card.label} className="rounded-2xl border border-white/10 bg-gradient-to-br from-[#141822] to-[#0d1017] p-5">
              <div className="text-sm text-gray-400 mb-1">{card.label}</div>
              <div className="text-3xl font-semibold text-white">{card.value}</div>
            </div>
          ))}
        </section>

        <section className="grid grid-cols-[1.2fr_0.8fr] gap-6">
          <div className="space-y-6">
            <div className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-6">
              <div className="flex items-center justify-between mb-5">
                <h2 className="text-xl font-semibold text-white">Recent Drafts</h2>
                <button onClick={() => navigate("/create")} className="text-sm text-cyan-300 flex items-center gap-2">
                  Open Studio
                  <ArrowRight className="w-4 h-4" />
                </button>
              </div>

              {latestDrafts.length === 0 ? (
                <div className="rounded-2xl border border-dashed border-white/10 bg-black/20 p-8 text-center text-gray-400">
                  No drafts yet. Start a real post, story, reel, or ad from Create Studio.
                </div>
              ) : (
                <div className="grid grid-cols-2 gap-4">
                  {latestDrafts.map((draft) => (
                    <button
                      key={draft.id}
                      onClick={() => {
                        selectDraft(draft.id);
                        navigate(draft.durationMs > 5000 ? "/video-studio" : "/create");
                      }}
                      className="text-left rounded-2xl border border-white/10 bg-black/20 p-4 hover:border-cyan-400/25 transition-colors"
                    >
                      <div className="flex items-center justify-between gap-3 mb-2">
                        <div className="text-sm font-medium text-white truncate">{draft.title}</div>
                        <span className="text-[11px] uppercase tracking-[0.2em] text-cyan-300">v{draft.version}</span>
                      </div>
                      <div className="text-xs text-gray-400 mb-3">
                        {draft.contentType} · {draft.pipelineStage} · {draft.width} × {draft.height}
                      </div>
                      <div className="flex items-center justify-between text-xs text-gray-500">
                        <span>{draft.campaign || "Independent draft"}</span>
                        <span>{formatDate(draft.updatedAt)}</span>
                      </div>
                    </button>
                  ))}
                </div>
              )}
            </div>

            <div className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-6">
              <div className="flex items-center justify-between mb-5">
                <h2 className="text-xl font-semibold text-white">ATLAS Orchestration Queue</h2>
                <button onClick={() => navigate("/ai-creator")} className="text-sm text-cyan-300">Open ATLAS AI</button>
              </div>
              {nextBriefs.length === 0 ? (
                <div className="rounded-2xl border border-dashed border-white/10 bg-black/20 p-6 text-sm text-gray-400">
                  No request packets saved yet. Build provider-routed caption, script, voice, or campaign requests from ATLAS AI.
                </div>
              ) : (
                <div className="space-y-3">
                  {nextBriefs.map((brief) => (
                    <div key={brief.id} className="rounded-2xl border border-white/10 bg-black/20 p-4">
                      <div className="flex items-center justify-between gap-4 mb-2">
                        <div className="text-sm font-medium text-white">{brief.objective}</div>
                        <div className="text-xs uppercase tracking-[0.18em] text-cyan-300">{brief.providerId}</div>
                      </div>
                      <div className="text-sm text-gray-400 mb-2">{brief.taskType} · {brief.modelId}</div>
                      <div className="text-xs text-gray-500">{brief.platformId} · {brief.targetSurface} · {brief.status}</div>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>

          <div className="space-y-6">
            <div className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-6">
              <div className="flex items-center gap-2 mb-4">
                <Calendar className="w-5 h-5 text-cyan-300" />
                <h2 className="text-lg font-semibold text-white">Publishing Pipeline</h2>
              </div>
              <div className="space-y-3 text-sm mb-5">
                <div className="flex items-center justify-between"><span className="text-gray-400">Scheduled drafts</span><span className="text-white">{scheduledDrafts.length}</span></div>
                <div className="flex items-center justify-between"><span className="text-gray-400">Connected accounts</span><span className="text-white">{accounts.length}</span></div>
                <div className="flex items-center justify-between"><span className="text-gray-400">Assets ready</span><span className="text-white">{assets.length}</span></div>
              </div>
              <button
                onClick={() => navigate("/export")}
                className="w-full px-4 py-3 rounded-2xl bg-white/5 border border-white/10 text-white text-sm font-medium flex items-center justify-center gap-2"
              >
                <Upload className="w-4 h-4" />
                Open Export / Publish
              </button>
            </div>

            <div className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-6">
              <div className="flex items-center gap-2 mb-4">
                <Waves className="w-5 h-5 text-cyan-300" />
                <h2 className="text-lg font-semibold text-white">Voice Workflow</h2>
              </div>
              {voiceProjects.length === 0 ? (
                <div className="text-sm text-gray-400 mb-4">
                  No voice sessions created yet. Build a narration script, choose an ElevenLabs model, and attach it to a draft.
                </div>
              ) : (
                <div className="space-y-3 mb-4">
                  {voiceProjects.slice(0, 3).map((project) => (
                    <div key={project.id} className="rounded-2xl border border-white/10 bg-black/20 p-4">
                      <div className="text-sm font-medium text-white mb-1">{project.title}</div>
                      <div className="text-xs text-gray-400">{project.voiceName || "Voice not selected"} · {project.status}</div>
                    </div>
                  ))}
                </div>
              )}
              <button onClick={() => navigate("/voice-studio")} className="text-sm text-cyan-300 flex items-center gap-2">
                Open Voice Studio
                <ArrowRight className="w-4 h-4" />
              </button>
            </div>
          </div>
        </section>
      </div>
    </div>
  );
}
