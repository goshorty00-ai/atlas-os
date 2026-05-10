import { BarChart3, CheckCircle2, Clock3, UploadCloud } from "lucide-react";
import { useStudio } from "../state/studioStore";

function formatPlatformLabel(platformId: string) {
  return platformId
    .split("-")
    .map((segment) => segment.charAt(0).toUpperCase() + segment.slice(1))
    .join(" ");
}

export function Analytics() {
  const {
    state: { drafts, accounts, assets, aiBriefs },
  } = useStudio();

  const publishedDrafts = drafts.filter((draft) => draft.status === "published-manual");
  const scheduledDrafts = drafts.filter((draft) => draft.status === "scheduled");
  const templateDrafts = drafts.filter((draft) => draft.savedAsTemplate);

  const platformBreakdown = Object.entries(
    drafts.reduce<Record<string, number>>((accumulator, draft) => {
      draft.linkedPlatformIds.forEach((platformId) => {
        accumulator[platformId] = (accumulator[platformId] ?? 0) + 1;
      });
      return accumulator;
    }, {}),
  ).sort((left, right) => right[1] - left[1]);

  return (
    <div className="space-y-8">
      <section className="bg-gradient-to-br from-[#1a1a1a] to-[#0f0f0f] border border-white/10 rounded-xl p-6">
        <div className="flex items-start justify-between gap-6">
          <div>
            <div className="inline-flex items-center gap-2 rounded-full border border-cyan-400/20 bg-cyan-400/10 px-3 py-1 text-xs font-medium uppercase tracking-[0.2em] text-cyan-200">
              Honest Analytics
            </div>
            <h1 className="mt-4 text-3xl font-semibold text-white">Studio activity, not fake metrics</h1>
            <p className="mt-3 max-w-3xl text-sm leading-relaxed text-gray-400">
              This page only shows data Atlas can verify locally right now: draft activity, scheduling state,
              configured accounts, stored assets, and AI request volume. Reach, views, and engagement stay empty
              until real platform analytics are wired in.
            </p>
          </div>
          <div className="hidden h-14 w-14 items-center justify-center rounded-2xl border border-cyan-400/20 bg-cyan-400/10 text-cyan-300 md:flex">
            <BarChart3 className="h-7 w-7" />
          </div>
        </div>
      </section>

      <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        <div className="rounded-xl border border-white/10 bg-white/[0.03] p-5">
          <div className="text-xs uppercase tracking-[0.2em] text-gray-500">Published</div>
          <div className="mt-3 text-3xl font-semibold text-white">{publishedDrafts.length}</div>
          <div className="mt-2 text-sm text-gray-400">Drafts marked as published manually from Atlas.</div>
        </div>
        <div className="rounded-xl border border-white/10 bg-white/[0.03] p-5">
          <div className="text-xs uppercase tracking-[0.2em] text-gray-500">Scheduled</div>
          <div className="mt-3 text-3xl font-semibold text-white">{scheduledDrafts.length}</div>
          <div className="mt-2 text-sm text-gray-400">Upcoming items with a real scheduled timestamp.</div>
        </div>
        <div className="rounded-xl border border-white/10 bg-white/[0.03] p-5">
          <div className="text-xs uppercase tracking-[0.2em] text-gray-500">Assets</div>
          <div className="mt-3 text-3xl font-semibold text-white">{assets.length}</div>
          <div className="mt-2 text-sm text-gray-400">Media files currently stored in the workspace library.</div>
        </div>
        <div className="rounded-xl border border-white/10 bg-white/[0.03] p-5">
          <div className="text-xs uppercase tracking-[0.2em] text-gray-500">AI Requests</div>
          <div className="mt-3 text-3xl font-semibold text-white">{aiBriefs.length}</div>
          <div className="mt-2 text-sm text-gray-400">Saved AI generation briefs prepared for Atlas workflows.</div>
        </div>
      </section>

      <section className="grid gap-6 xl:grid-cols-[1.2fr_0.8fr]">
        <div className="rounded-xl border border-white/10 bg-gradient-to-br from-[#1a1a1a] to-[#111111] p-6">
          <div className="flex items-center gap-2">
            <CheckCircle2 className="h-5 w-5 text-cyan-300" />
            <h2 className="text-lg font-semibold text-white">Recent verified activity</h2>
          </div>
          {drafts.length === 0 ? (
            <div className="mt-6 rounded-xl border border-dashed border-white/10 bg-black/20 p-6 text-sm text-gray-400">
              No studio activity yet. Create a draft, upload media, or schedule a post to start generating real analytics.
            </div>
          ) : (
            <div className="mt-6 space-y-3">
              {drafts
                .slice()
                .sort((left, right) => new Date(right.updatedAt).getTime() - new Date(left.updatedAt).getTime())
                .slice(0, 6)
                .map((draft) => (
                  <div key={draft.id} className="rounded-xl border border-white/10 bg-black/20 p-4">
                    <div className="flex items-start justify-between gap-4">
                      <div>
                        <div className="text-sm font-medium text-white">{draft.title}</div>
                        <div className="mt-1 text-xs text-gray-400">
                          Updated {new Date(draft.updatedAt).toLocaleString()}
                        </div>
                      </div>
                      <div className="rounded-full border border-white/10 px-3 py-1 text-xs text-cyan-200">
                        {draft.status}
                      </div>
                    </div>
                    <div className="mt-3 flex flex-wrap gap-2 text-xs text-gray-300">
                      {draft.linkedPlatformIds.length > 0 ? (
                        draft.linkedPlatformIds.map((platformId) => (
                          <span key={platformId} className="rounded-full bg-white/5 px-2 py-1">
                            {formatPlatformLabel(platformId)}
                          </span>
                        ))
                      ) : (
                        <span className="rounded-full bg-white/5 px-2 py-1">No linked platforms</span>
                      )}
                      {draft.scheduledFor ? (
                        <span className="rounded-full bg-emerald-500/10 px-2 py-1 text-emerald-200">
                          Scheduled for {new Date(draft.scheduledFor).toLocaleString()}
                        </span>
                      ) : null}
                      {draft.savedAsTemplate ? (
                        <span className="rounded-full bg-violet-500/10 px-2 py-1 text-violet-200">Saved template</span>
                      ) : null}
                    </div>
                  </div>
                ))}
            </div>
          )}
        </div>

        <div className="space-y-6">
          <div className="rounded-xl border border-white/10 bg-gradient-to-br from-[#1a1a1a] to-[#111111] p-6">
            <div className="flex items-center gap-2">
              <Clock3 className="h-5 w-5 text-violet-300" />
              <h2 className="text-lg font-semibold text-white">Platform usage</h2>
            </div>
            {platformBreakdown.length === 0 ? (
              <div className="mt-6 text-sm text-gray-400">No platforms are linked to drafts yet.</div>
            ) : (
              <div className="mt-6 space-y-3">
                {platformBreakdown.map(([platformId, count]) => (
                  <div key={platformId} className="flex items-center justify-between rounded-xl border border-white/10 bg-black/20 px-4 py-3">
                    <span className="text-sm text-white">{formatPlatformLabel(platformId)}</span>
                    <span className="text-sm text-cyan-300">{count} draft(s)</span>
                  </div>
                ))}
              </div>
            )}
          </div>

          <div className="rounded-xl border border-white/10 bg-gradient-to-br from-[#1a1a1a] to-[#111111] p-6">
            <div className="flex items-center gap-2">
              <UploadCloud className="h-5 w-5 text-emerald-300" />
              <h2 className="text-lg font-semibold text-white">Integration readiness</h2>
            </div>
            <div className="mt-6 space-y-3 text-sm text-gray-300">
              <div className="flex items-center justify-between rounded-xl border border-white/10 bg-black/20 px-4 py-3">
                <span>Configured accounts</span>
                <span className="text-white">{accounts.length}</span>
              </div>
              <div className="flex items-center justify-between rounded-xl border border-white/10 bg-black/20 px-4 py-3">
                <span>Saved templates</span>
                <span className="text-white">{templateDrafts.length}</span>
              </div>
              <div className="flex items-center justify-between rounded-xl border border-white/10 bg-black/20 px-4 py-3">
                <span>Backend analytics state</span>
                <span className="text-amber-300">Pending API integration</span>
              </div>
            </div>
          </div>
        </div>
      </section>
    </div>
  );
}
