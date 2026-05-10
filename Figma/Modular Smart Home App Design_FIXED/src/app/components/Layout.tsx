import { Outlet, Link, useLocation } from "react-router-dom";
import {
  Home,
  Cpu,
  PlusCircle,
  Grid3x3,
  Camera,
  Shield,
  Sparkles,
  Workflow,
  MessageSquareText,
  Bell,
  Gauge,
  DoorOpen,
  Bot,
} from "lucide-react";

const navigation = [
  { name: "Overview", path: "/", icon: Home },
  { name: "Devices", path: "/devices", icon: Cpu },
  { name: "Device Setup", path: "/device-setup", icon: PlusCircle },
  { name: "Rooms", path: "/rooms", icon: Grid3x3 },
  { name: "Cameras", path: "/cameras", icon: Camera },
  { name: "Security", path: "/security", icon: Shield },
  { name: "AI Scenes", path: "/ai-scenes", icon: Sparkles },
  { name: "Automations", path: "/automations", icon: Workflow },
  { name: "Custom Commands", path: "/custom-commands", icon: MessageSquareText },
  { name: "Alerts", path: "/alerts", icon: Bell },
  { name: "Climate & Energy", path: "/climate-energy", icon: Gauge },
  { name: "Access", path: "/access", icon: DoorOpen },
  { name: "AI Assistant", path: "/ai-assistant", icon: Bot },
];

export function Layout() {
  const location = useLocation();

  return (
    <div className="flex h-screen bg-black text-gray-100">
      {/* Left Sidebar */}
      <aside className="w-64 bg-gradient-to-b from-gray-950 via-gray-900 to-black border-r border-gray-800/50 flex flex-col">
        <div className="p-6">
          <div className="text-2xl font-bold bg-gradient-to-r from-cyan-400 to-blue-500 bg-clip-text text-transparent">
            SMARTCORE
          </div>
          <div className="text-xs text-gray-500 mt-1">Command Center</div>
        </div>

        <nav className="flex-1 px-3 py-2 overflow-y-auto">
          {navigation.map((item) => {
            const Icon = item.icon;
            const isActive = location.pathname === item.path;

            return (
              <Link
                key={item.path}
                to={item.path}
                className={`
                  flex items-center gap-3 px-3 py-2.5 rounded-lg mb-1 transition-all
                  ${
                    isActive
                      ? "bg-cyan-500/10 text-cyan-400 border border-cyan-500/30"
                      : "text-gray-400 hover:bg-gray-800/50 hover:text-gray-200"
                  }
                `}
              >
                <Icon className="w-5 h-5 flex-shrink-0" />
                <span className="text-sm font-medium">{item.name}</span>
              </Link>
            );
          })}
        </nav>

        <div className="p-4 border-t border-gray-800/50">
          <div className="text-xs text-gray-500">System Status</div>
          <div className="flex items-center gap-2 mt-2">
            <div className="w-2 h-2 bg-green-500 rounded-full animate-pulse" />
            <span className="text-xs text-gray-400">All Systems Online</span>
          </div>
        </div>
      </aside>

      {/* Main Content Area */}
      <main className="flex-1 overflow-auto">
        <Outlet />
      </main>
    </div>
  );
}
