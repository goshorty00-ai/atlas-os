import { Link, Outlet, useLocation } from "react-router";
import {
  BrainCircuit,
  Calendar,
  Clapperboard,
  FolderKanban,
  Image,
  LayoutDashboard,
  LayoutTemplate,
  LineChart,
  Palette,
  Settings as SettingsIcon,
  Sparkles,
  Upload,
  Users,
  Waves,
} from "lucide-react";
import { useStudio } from "../state/studioStore";

const navGroups = [
  {
    label: "Studio",
    items: [
      { path: "/", icon: LayoutDashboard, label: "Dashboard" },
      { path: "/create", icon: Sparkles, label: "Create Studio" },
      { path: "/video-studio", icon: Clapperboard, label: "Video Studio" },
      { path: "/voice-studio", icon: Waves, label: "Voice Studio" },
    ],
  },
  {
    label: "Systems",
    items: [
      { path: "/templates", icon: LayoutTemplate, label: "Templates" },
      { path: "/media", icon: Image, label: "Media Library" },
      { path: "/brand-kit", icon: Palette, label: "Brand Kit" },
      { path: "/ai-creator", icon: BrainCircuit, label: "ATLAS AI" },
      { path: "/planner", icon: Calendar, label: "Content Planner" },
      { path: "/accounts", icon: Users, label: "Accounts" },
      { path: "/analytics", icon: LineChart, label: "Analytics" },
      { path: "/export", icon: Upload, label: "Export / Publish" },
      { path: "/settings", icon: SettingsIcon, label: "Settings" },
    ],
  },
];

export function Layout() {
  const location = useLocation();
  const {
    state: { profile, drafts, accounts, assets, aiBriefs, voiceProjects },
  } = useStudio();
  const isEditorRoute = location.pathname === "/create" || location.pathname === "/video-studio" || location.pathname === "/voice-studio";

  const displayName = [profile.firstName, profile.lastName].filter(Boolean).join(" ") || profile.company || "ATLAS Creator";
  const initials =
    [profile.firstName, profile.lastName]
      .filter(Boolean)
      .map((part) => part[0])
      .join("")
      .slice(0, 2)
      .toUpperCase() || "AT";

  return (
    <div className="flex h-screen bg-[#07080c] text-gray-100">
      <aside className={`${isEditorRoute ? "w-24" : "w-80"} bg-[radial-gradient(circle_at_top,_rgba(0,229,255,0.14),_transparent_38%),radial-gradient(circle_at_bottom,_rgba(255,82,160,0.12),_transparent_30%),linear-gradient(180deg,#111420_0%,#090b11_100%)] border-r border-white/5 flex flex-col transition-all duration-300`}>
        <div className={`${isEditorRoute ? "p-4" : "p-6"} border-b border-white/5`}>
          <div className={`flex items-center ${isEditorRoute ? "justify-center" : "gap-3"}`}>
            <div className="w-11 h-11 rounded-2xl bg-gradient-to-br from-cyan-400 via-blue-500 to-fuchsia-500 flex items-center justify-center shadow-lg shadow-cyan-500/10">
              <Sparkles className="w-5 h-5 text-white" />
            </div>
            {!isEditorRoute ? (
              <div>
                <div className="font-semibold text-white tracking-wide">ATLAS Studio</div>
                <div className="text-xs text-cyan-200/70">AI-native social content operating system</div>
              </div>
            ) : null}
          </div>

          {!isEditorRoute ? (
            <div className="grid grid-cols-2 gap-3 mt-5">
              <div className="rounded-2xl bg-white/5 border border-white/10 px-4 py-3">
                <div className="text-xs text-gray-400">Open Drafts</div>
                <div className="text-lg font-semibold text-white">{drafts.length}</div>
              </div>
              <div className="rounded-2xl bg-white/5 border border-white/10 px-4 py-3">
                <div className="text-xs text-gray-400">AI Requests</div>
                <div className="text-lg font-semibold text-white">{aiBriefs.length}</div>
              </div>
              <div className="rounded-2xl bg-white/5 border border-white/10 px-4 py-3">
                <div className="text-xs text-gray-400">Library Assets</div>
                <div className="text-lg font-semibold text-white">{assets.length}</div>
              </div>
              <div className="rounded-2xl bg-white/5 border border-white/10 px-4 py-3">
                <div className="text-xs text-gray-400">Voice Sessions</div>
                <div className="text-lg font-semibold text-white">{voiceProjects.length}</div>
              </div>
            </div>
          ) : null}
        </div>

        <nav className={`flex-1 overflow-y-auto ${isEditorRoute ? "px-2 py-4" : "px-3 py-5"} space-y-5`}>
          {navGroups.map((group) => (
            <div key={group.label}>
              {!isEditorRoute ? <div className="px-3 mb-2 text-[11px] uppercase tracking-[0.24em] text-gray-500">{group.label}</div> : null}
              {group.items.map((item) => {
                const Icon = item.icon;
                const isActive = location.pathname === item.path;
                return (
                  <Link
                    key={item.path}
                    to={item.path}
                    title={item.label}
                    className={`flex items-center ${isEditorRoute ? "justify-center px-0 py-3" : "gap-3 px-4 py-3"} rounded-2xl mb-1.5 transition-all duration-200 ${
                      isActive
                        ? "bg-gradient-to-r from-cyan-500/15 to-fuchsia-500/15 text-white border border-cyan-400/20"
                        : "text-gray-400 hover:text-white hover:bg-white/5"
                    }`}
                  >
                    <Icon className={`w-5 h-5 ${isActive ? "text-cyan-300" : ""}`} />
                    {!isEditorRoute ? <span className="text-sm font-medium">{item.label}</span> : null}
                  </Link>
                );
              })}
            </div>
          ))}
        </nav>

        <div className={`${isEditorRoute ? "p-3" : "p-4"} border-t border-white/5 space-y-3`}>
          {!isEditorRoute ? (
            <div className="rounded-2xl bg-white/5 border border-white/10 px-4 py-3">
              <div className="flex items-center justify-between gap-3 mb-2">
                <div className="text-sm font-medium text-white">Workspace Readiness</div>
                <FolderKanban className="w-4 h-4 text-cyan-300" />
              </div>
              <div className="space-y-2 text-xs text-gray-400">
                <div className="flex items-center justify-between"><span>Accounts</span><span>{accounts.length}</span></div>
                <div className="flex items-center justify-between"><span>Assets</span><span>{assets.length}</span></div>
                <div className="flex items-center justify-between"><span>Drafts</span><span>{drafts.length}</span></div>
              </div>
            </div>
          ) : null}

          <div className={`flex items-center ${isEditorRoute ? "justify-center" : "gap-3 px-2 py-2"} rounded-2xl bg-white/5 border border-white/10`}>
            <div className="w-11 h-11 rounded-full bg-gradient-to-br from-cyan-500 to-blue-600 flex items-center justify-center text-sm font-semibold">
              {initials}
            </div>
            {!isEditorRoute ? (
              <div className="flex-1 min-w-0">
                <div className="text-sm font-medium text-white truncate">{displayName}</div>
                <div className="text-xs text-gray-400 truncate">
                  {profile.company || "Configure brand identity and publish targets"}
                </div>
              </div>
            ) : null}
          </div>
        </div>
      </aside>

      <main className="flex-1 overflow-hidden">
        <Outlet />
      </main>
    </div>
  );
}
