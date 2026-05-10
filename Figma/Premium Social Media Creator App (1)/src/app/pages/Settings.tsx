import { Building2, CheckCircle2, Database, ShieldCheck } from "lucide-react";
import { useStudio } from "../state/studioStore";

function completionLabel(value: boolean) {
  return value ? "Ready" : "Not connected";
}

export function Settings() {
  const {
    state: { profile, drafts, accounts, assets, aiBriefs },
    updateProfile,
  } = useStudio();

  const initials =
    [profile.firstName, profile.lastName]
      .filter(Boolean)
      .map((part) => part[0])
      .join("")
      .slice(0, 2)
      .toUpperCase() || "AT";

  const hasIdentity = Boolean(profile.firstName || profile.lastName || profile.company || profile.email);
  const hasAccounts = accounts.length > 0;
  const hasAssets = assets.length > 0;
  const hasDrafts = drafts.length > 0;

  const readiness = [
    { label: "Workspace identity", ready: hasIdentity, detail: "Profile, company, and workspace ownership" },
    { label: "Connected accounts", ready: hasAccounts, detail: "Platform targets Atlas can prepare for publishing" },
    { label: "Media library", ready: hasAssets, detail: "Uploaded assets available to drafts and scenes" },
    { label: "Studio activity", ready: hasDrafts, detail: "Drafts, scenes, and export history" },
  ];

  return (
    <div className="h-full overflow-y-auto bg-[#0a0a0a]">
      <div className="p-8 space-y-6">
        <div>
          <h1 className="text-3xl font-bold text-white mb-2">Settings</h1>
          <p className="text-gray-400">
            This page only manages real profile and workspace configuration. Backend billing, notification delivery,
            and access-control settings stay out of the UI until they are actually connected.
          </p>
        </div>

        <div className="grid grid-cols-[1.15fr_0.85fr] gap-6">
          <div className="space-y-6">
            <section className="bg-gradient-to-br from-[#1a1a1a] to-[#0f0f0f] border border-white/10 rounded-2xl p-6">
              <div className="flex items-start gap-5 mb-6">
                <div className="w-24 h-24 rounded-full bg-gradient-to-br from-cyan-500 to-blue-600 flex items-center justify-center text-2xl font-bold text-white shadow-lg shadow-cyan-500/10">
                  {initials}
                </div>
                <div>
                  <div className="text-lg font-semibold text-white">Workspace Identity</div>
                  <div className="mt-1 text-sm text-gray-400 max-w-xl">
                    Profile changes save directly into local studio state. No fake subscription, notification, or plan settings are shown here.
                  </div>
                </div>
              </div>

              <div className="grid grid-cols-2 gap-4 mb-4">
                <label>
                  <div className="text-sm text-gray-400 mb-2">First Name</div>
                  <input
                    type="text"
                    value={profile.firstName}
                    onChange={(event) => updateProfile({ firstName: event.target.value })}
                    className="w-full bg-[#0f0f0f] border border-white/10 rounded-xl px-4 py-3 text-white focus:outline-none focus:border-cyan-400/40"
                  />
                </label>
                <label>
                  <div className="text-sm text-gray-400 mb-2">Last Name</div>
                  <input
                    type="text"
                    value={profile.lastName}
                    onChange={(event) => updateProfile({ lastName: event.target.value })}
                    className="w-full bg-[#0f0f0f] border border-white/10 rounded-xl px-4 py-3 text-white focus:outline-none focus:border-cyan-400/40"
                  />
                </label>
              </div>

              <div className="grid grid-cols-2 gap-4 mb-4">
                <label>
                  <div className="text-sm text-gray-400 mb-2">Email</div>
                  <input
                    type="email"
                    value={profile.email}
                    onChange={(event) => updateProfile({ email: event.target.value })}
                    className="w-full bg-[#0f0f0f] border border-white/10 rounded-xl px-4 py-3 text-white focus:outline-none focus:border-cyan-400/40"
                  />
                </label>
                <label>
                  <div className="text-sm text-gray-400 mb-2">Role</div>
                  <input
                    type="text"
                    value={profile.role}
                    onChange={(event) => updateProfile({ role: event.target.value })}
                    className="w-full bg-[#0f0f0f] border border-white/10 rounded-xl px-4 py-3 text-white focus:outline-none focus:border-cyan-400/40"
                  />
                </label>
              </div>

              <div className="mb-4">
                <label>
                  <div className="text-sm text-gray-400 mb-2">Company</div>
                  <input
                    type="text"
                    value={profile.company}
                    onChange={(event) => updateProfile({ company: event.target.value })}
                    className="w-full bg-[#0f0f0f] border border-white/10 rounded-xl px-4 py-3 text-white focus:outline-none focus:border-cyan-400/40"
                  />
                </label>
              </div>

              <label>
                <div className="text-sm text-gray-400 mb-2">Bio</div>
                <textarea
                  value={profile.bio}
                  onChange={(event) => updateProfile({ bio: event.target.value })}
                  rows={4}
                  className="w-full bg-[#0f0f0f] border border-white/10 rounded-xl px-4 py-3 text-white focus:outline-none focus:border-cyan-400/40 resize-none"
                />
              </label>
            </section>

            <section className="bg-gradient-to-br from-[#1a1a1a] to-[#0f0f0f] border border-white/10 rounded-2xl p-6">
              <div className="flex items-center gap-2 mb-5">
                <Building2 className="w-5 h-5 text-cyan-300" />
                <h2 className="text-xl font-semibold text-white">Workspace Summary</h2>
              </div>
              <div className="grid grid-cols-4 gap-4">
                <div className="bg-[#0f0f0f] border border-white/10 rounded-xl p-4">
                  <div className="text-xs text-gray-400 mb-1">Drafts</div>
                  <div className="text-2xl font-semibold text-white">{drafts.length}</div>
                </div>
                <div className="bg-[#0f0f0f] border border-white/10 rounded-xl p-4">
                  <div className="text-xs text-gray-400 mb-1">Accounts</div>
                  <div className="text-2xl font-semibold text-white">{accounts.length}</div>
                </div>
                <div className="bg-[#0f0f0f] border border-white/10 rounded-xl p-4">
                  <div className="text-xs text-gray-400 mb-1">Assets</div>
                  <div className="text-2xl font-semibold text-white">{assets.length}</div>
                </div>
                <div className="bg-[#0f0f0f] border border-white/10 rounded-xl p-4">
                  <div className="text-xs text-gray-400 mb-1">AI Briefs</div>
                  <div className="text-2xl font-semibold text-white">{aiBriefs.length}</div>
                </div>
              </div>
            </section>
          </div>

          <div className="space-y-6">
            <section className="bg-gradient-to-br from-[#1a1a1a] to-[#0f0f0f] border border-white/10 rounded-2xl p-6">
              <div className="flex items-center gap-2 mb-5">
                <ShieldCheck className="w-5 h-5 text-emerald-300" />
                <h2 className="text-xl font-semibold text-white">Integration Readiness</h2>
              </div>
              <div className="space-y-3">
                {readiness.map((item) => (
                  <div key={item.label} className="bg-[#0f0f0f] border border-white/10 rounded-xl p-4">
                    <div className="flex items-center justify-between gap-3">
                      <div>
                        <div className="text-sm font-medium text-white">{item.label}</div>
                        <div className="mt-1 text-xs text-gray-400">{item.detail}</div>
                      </div>
                      <div className={`text-xs font-medium px-3 py-1 rounded-full ${item.ready ? "bg-emerald-500/15 text-emerald-300" : "bg-white/5 text-gray-400"}`}>
                        {completionLabel(item.ready)}
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            </section>

            <section className="bg-gradient-to-br from-[#1a1a1a] to-[#0f0f0f] border border-white/10 rounded-2xl p-6">
              <div className="flex items-center gap-2 mb-5">
                <Database className="w-5 h-5 text-cyan-300" />
                <h2 className="text-xl font-semibold text-white">Backend-Dependent Settings</h2>
              </div>
              <div className="rounded-2xl border border-dashed border-white/10 bg-black/20 p-5 text-sm text-gray-300 leading-relaxed">
                Notification delivery, billing, user permissions, audit logs, and cloud storage quotas are intentionally not mocked.
                When those services exist, this page can bind to them cleanly. Until then, Atlas only shows the real profile and workspace state it can actually persist.
              </div>
            </section>

            <section className="bg-gradient-to-br from-cyan-500/10 to-blue-600/10 border border-cyan-400/20 rounded-2xl p-6">
              <div className="flex items-center gap-2 mb-4 text-white font-medium">
                <CheckCircle2 className="w-5 h-5 text-cyan-200" />
                Current Product Boundary
              </div>
              <div className="text-sm text-cyan-50/85 leading-relaxed">
                Atlas Studio is using local state for profile, assets, drafts, accounts, scheduling, and AI request packets.
                Anything beyond that is left as an explicit integration target, not replaced with fake controls or fake values.
              </div>
            </section>
          </div>
        </div>
      </div>
    </div>
  );
}
