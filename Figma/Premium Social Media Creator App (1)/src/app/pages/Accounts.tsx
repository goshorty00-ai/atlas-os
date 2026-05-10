import { useMemo, useState } from "react";
import { useNavigate } from "react-router";
import { Activity, CheckCircle2, Link2, Plus, RefreshCcw, ShieldCheck, Trash2 } from "lucide-react";
import { PLATFORM_DEFINITIONS, type ConnectedAccount, type PlatformId, useStudio } from "../state/studioStore";

const syncHealthOptions: ConnectedAccount["syncHealth"][] = ["healthy", "attention", "disconnected"];
const reconnectStates: ConnectedAccount["reconnectState"][] = ["idle", "needs-auth", "reconnecting"];

export function Accounts() {
  const navigate = useNavigate();
  const {
    state: { accounts, drafts, selectedDraftId },
    upsertAccount,
    removeAccount,
    updateDraft,
  } = useStudio();

  const [platformId, setPlatformId] = useState<PlatformId>("instagram");
  const [displayName, setDisplayName] = useState("");
  const [handle, setHandle] = useState("");
  const [scopes, setScopes] = useState("");
  const [notes, setNotes] = useState("");
  const [supportedTypes, setSupportedTypes] = useState("");
  const [canPublish, setCanPublish] = useState(true);
  const [canSchedule, setCanSchedule] = useState(true);
  const [syncHealth, setSyncHealth] = useState<ConnectedAccount["syncHealth"]>("attention");
  const [reconnectState, setReconnectState] = useState<ConnectedAccount["reconnectState"]>("idle");

  const selectedPlatform = PLATFORM_DEFINITIONS.find((platform) => platform.id === platformId) ?? PLATFORM_DEFINITIONS[0];
  const configuredAccounts = useMemo(
    () => accounts.map((account) => ({ ...account, platform: PLATFORM_DEFINITIONS.find((item) => item.id === account.platformId) })),
    [accounts],
  );
  const activeDraft = drafts.find((draft) => draft.id === selectedDraftId);

  function resetForm(nextPlatform: PlatformId) {
    setPlatformId(nextPlatform);
    setDisplayName("");
    setHandle("");
    setScopes("");
    setNotes("");
    setSupportedTypes("");
    setCanPublish(true);
    setCanSchedule(true);
    setSyncHealth("attention");
    setReconnectState("idle");
  }

  function handleSubmit() {
    upsertAccount({
      platformId,
      displayName: displayName.trim(),
      handle: handle.trim(),
      authStatus: reconnectState === "needs-auth" ? "configured-local" : "connected",
      scopes: scopes.split(",").map((item) => item.trim()).filter(Boolean),
      notes: notes.trim(),
      supportedContentTypes: supportedTypes.split(",").map((item) => item.trim()).filter(Boolean),
      canPublish,
      canSchedule,
      syncHealth,
      reconnectState,
      lastSyncedAt: reconnectState === "idle" ? new Date().toISOString() : undefined,
    });

    const nextPlatform = PLATFORM_DEFINITIONS.find((platform) => platform.id !== platformId)?.id ?? "instagram";
    resetForm(nextPlatform);
  }

  return (
    <div className="h-full overflow-y-auto bg-[#07080c]">
      <div className="p-8 space-y-6">
        <div>
          <h1 className="text-3xl font-bold text-white mb-2">Accounts</h1>
          <p className="text-gray-400">
            Configure real social destinations, capability scopes, schedule support, and reconnect health. This surface is ready for backend OAuth without pretending network auth has already happened.
          </p>
        </div>

        <div className="grid grid-cols-4 gap-4">
          <div className="rounded-2xl border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-5">
            <div className="text-sm text-gray-400 mb-1">Configured Accounts</div>
            <div className="text-3xl font-semibold text-white">{accounts.length}</div>
          </div>
          <div className="rounded-2xl border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-5">
            <div className="text-sm text-gray-400 mb-1">Healthy Connections</div>
            <div className="text-3xl font-semibold text-white">{accounts.filter((account) => account.syncHealth === "healthy").length}</div>
          </div>
          <div className="rounded-2xl border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-5">
            <div className="text-sm text-gray-400 mb-1">Publish Enabled</div>
            <div className="text-3xl font-semibold text-white">{accounts.filter((account) => account.canPublish).length}</div>
          </div>
          <div className="rounded-2xl border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-5">
            <div className="text-sm text-gray-400 mb-1">Schedule Enabled</div>
            <div className="text-3xl font-semibold text-white">{accounts.filter((account) => account.canSchedule).length}</div>
          </div>
        </div>

        <div className="grid grid-cols-[420px_1fr] gap-6">
          <section className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-6 h-fit">
            <div className="flex items-center gap-2 mb-5">
              <Plus className="w-5 h-5 text-cyan-300" />
              <h2 className="text-lg font-semibold text-white">Add Destination</h2>
            </div>

            <div className="space-y-4">
              <select value={platformId} onChange={(event) => setPlatformId(event.target.value as PlatformId)} className="w-full rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white">
                {PLATFORM_DEFINITIONS.map((platform) => (
                  <option key={platform.id} value={platform.id}>{platform.label}</option>
                ))}
              </select>
              <input value={displayName} onChange={(event) => setDisplayName(event.target.value)} placeholder="Workspace display label" className="w-full rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white" />
              <input value={handle} onChange={(event) => setHandle(event.target.value)} placeholder="Account handle or channel" className="w-full rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white" />
              <input value={supportedTypes} onChange={(event) => setSupportedTypes(event.target.value)} placeholder={selectedPlatform.supportedTypes.join(", ")} className="w-full rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white" />
              <input value={scopes} onChange={(event) => setScopes(event.target.value)} placeholder="publish, schedule, insights, comments" className="w-full rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white" />

              <div className="grid grid-cols-2 gap-3">
                <select value={syncHealth} onChange={(event) => setSyncHealth(event.target.value as ConnectedAccount["syncHealth"])} className="rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white">
                  {syncHealthOptions.map((option) => <option key={option} value={option}>{option}</option>)}
                </select>
                <select value={reconnectState} onChange={(event) => setReconnectState(event.target.value as ConnectedAccount["reconnectState"])} className="rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white">
                  {reconnectStates.map((option) => <option key={option} value={option}>{option}</option>)}
                </select>
              </div>

              <div className="grid grid-cols-2 gap-3 text-sm">
                <label className="rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-gray-300 flex items-center justify-between">
                  Publish Enabled
                  <input type="checkbox" checked={canPublish} onChange={(event) => setCanPublish(event.target.checked)} />
                </label>
                <label className="rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-gray-300 flex items-center justify-between">
                  Schedule Enabled
                  <input type="checkbox" checked={canSchedule} onChange={(event) => setCanSchedule(event.target.checked)} />
                </label>
              </div>

              <textarea value={notes} onChange={(event) => setNotes(event.target.value)} placeholder="Tokens, webhooks, moderation limits, or setup notes" className="w-full h-28 rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white resize-none" />

              <button onClick={handleSubmit} disabled={!displayName.trim() || !handle.trim()} className="w-full rounded-2xl bg-gradient-to-r from-cyan-500 to-blue-600 px-4 py-3 text-sm font-medium text-white disabled:opacity-50">
                Save Destination
              </button>
            </div>
          </section>

          <div className="space-y-6">
            <section className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-6">
              <div className="flex items-center gap-2 mb-5">
                <Link2 className="w-5 h-5 text-cyan-300" />
                <h2 className="text-lg font-semibold text-white">Connected Platform State</h2>
              </div>

              {configuredAccounts.length === 0 ? (
                <div className="rounded-2xl border border-dashed border-white/10 bg-black/20 p-8 text-center text-gray-400">
                  No destinations configured yet.
                </div>
              ) : (
                <div className="grid grid-cols-2 gap-4">
                  {configuredAccounts.map((account) => (
                    <div key={account.id} className="rounded-2xl border border-white/10 bg-black/20 p-5">
                      <div className="flex items-start justify-between gap-4 mb-4">
                        <div>
                          <div className="text-lg font-semibold text-white">{account.platform?.label}</div>
                          <div className="text-sm text-gray-400">{account.displayName}</div>
                          <div className="text-xs text-cyan-300 mt-1">{account.handle}</div>
                        </div>
                        <div className={`px-3 py-1 rounded-full text-xs font-medium ${
                          account.syncHealth === "healthy"
                            ? "bg-emerald-500/15 text-emerald-300"
                            : account.syncHealth === "attention"
                              ? "bg-amber-500/15 text-amber-300"
                              : "bg-red-500/15 text-red-300"
                        }`}>
                          {account.syncHealth}
                        </div>
                      </div>

                      <div className="grid grid-cols-2 gap-2 mb-4 text-xs">
                        <div className="rounded-xl border border-white/10 bg-[#0c1016] px-3 py-2 text-gray-300">Auth: {account.authStatus}</div>
                        <div className="rounded-xl border border-white/10 bg-[#0c1016] px-3 py-2 text-gray-300">Reconnect: {account.reconnectState}</div>
                        <div className="rounded-xl border border-white/10 bg-[#0c1016] px-3 py-2 text-gray-300">Publish: {account.canPublish ? "yes" : "no"}</div>
                        <div className="rounded-xl border border-white/10 bg-[#0c1016] px-3 py-2 text-gray-300">Schedule: {account.canSchedule ? "yes" : "no"}</div>
                      </div>

                      <div className="mb-4 flex flex-wrap gap-2">
                        {account.supportedContentTypes.map((type) => (
                          <span key={type} className="px-2 py-1 rounded-full border border-white/10 bg-white/5 text-xs text-gray-300">{type}</span>
                        ))}
                      </div>

                      <div className="mb-4 flex flex-wrap gap-2">
                        {account.scopes.map((scope) => (
                          <span key={scope} className="px-2 py-1 rounded-full border border-cyan-400/20 bg-cyan-500/10 text-xs text-cyan-100">{scope}</span>
                        ))}
                      </div>

                      {account.notes && <div className="text-sm text-gray-400 mb-4">{account.notes}</div>}

                      <div className="flex items-center justify-between gap-3">
                        <div className="flex items-center gap-2 text-sm text-gray-400">
                          <ShieldCheck className="w-4 h-4 text-cyan-300" />
                          {account.lastSyncedAt ? `Synced ${new Date(account.lastSyncedAt).toLocaleDateString()}` : "Awaiting verified sync"}
                        </div>
                        <div className="flex items-center gap-2">
                          <button
                            onClick={() => {
                              if (!activeDraft) {
                                navigate("/create");
                                return;
                              }

                              updateDraft(activeDraft.id, (draft) => ({
                                ...draft,
                                linkedPlatformIds: draft.linkedPlatformIds.includes(account.platformId)
                                  ? draft.linkedPlatformIds
                                  : [...draft.linkedPlatformIds, account.platformId],
                              }));
                              navigate("/create");
                            }}
                            className="rounded-xl border border-cyan-400/20 bg-cyan-500/10 px-3 py-2 text-xs text-cyan-100"
                          >
                            Use In Active Draft
                          </button>
                          <button onClick={() => removeAccount(account.id)} className="w-9 h-9 rounded-xl bg-white/5 hover:bg-red-500/15 text-gray-400 hover:text-red-300 flex items-center justify-center">
                            <Trash2 className="w-4 h-4" />
                          </button>
                        </div>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </section>

            <section className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-6">
              <div className="flex items-center gap-2 mb-5">
                <Activity className="w-5 h-5 text-cyan-300" />
                <h2 className="text-lg font-semibold text-white">Capability Matrix</h2>
              </div>
              <div className="grid grid-cols-3 gap-4">
                {PLATFORM_DEFINITIONS.map((platform) => (
                  <div key={platform.id} className="rounded-2xl border border-white/10 bg-black/20 p-4">
                    <div className="text-sm font-semibold text-white mb-2">{platform.label}</div>
                    <div className="flex flex-wrap gap-2 mb-3">
                      {platform.supportedTypes.map((type) => (
                        <span key={type} className="px-2 py-1 rounded-full border border-white/10 bg-white/5 text-xs text-gray-300">{type}</span>
                      ))}
                    </div>
                    <div className="text-xs text-gray-500">Map auth state, publish access, schedule access, and health to this row.</div>
                  </div>
                ))}
              </div>
            </section>

            <section className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-6">
              <div className="flex items-center gap-2 mb-3 text-white font-medium">
                <RefreshCcw className="w-4 h-4 text-cyan-300" />
                Reconnect Logic
              </div>
              <div className="text-sm text-gray-300 leading-relaxed">
                Accounts now persist reconnect state and sync health in real workspace state. When OAuth and sync jobs are wired, the UI already has a place to reflect verification, refresh needs, and publishing readiness.
              </div>
            </section>
          </div>
        </div>
      </div>
    </div>
  );
}
