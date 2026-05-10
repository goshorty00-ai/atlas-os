import { useState } from 'react';
import { NavLink } from 'react-router';
import {
  Server, Film, Tv, Music, Gamepad2, Mic2,
  Wrench, MessageSquare, Settings, ChevronLeft, ChevronRight, Home, Grid3x3, Plug
} from 'lucide-react';

interface NavItem {
  icon: React.ElementType;
  label: string;
  path?: string;
}

const navItems: NavItem[] = [
  { icon: Home, label: 'Discovery', path: '/' },
  { icon: Server, label: 'Streams', path: '/servers' },
  { icon: Film, label: 'Movies', path: '/movies' },
  { icon: Tv, label: 'TV', path: '/tv' },
  { icon: Music, label: 'Music', path: '/music' },
  { icon: Gamepad2, label: 'Games', path: '/games' },
  { icon: Grid3x3, label: 'Apps', path: '/apps' },
  { icon: Mic2, label: 'AI Karaoke', path: '/karaoke' },
  { icon: Wrench, label: 'Shelf Creator', path: '/shelf-creator' },
  { icon: Plug, label: 'Addon Manager' },
  { icon: MessageSquare, label: 'AI Chat', path: '/chat' },
  { icon: Settings, label: 'Settings', path: '/settings' },
];

export function Sidebar() {
  const [collapsed, setCollapsed] = useState(false);

  return (
    <div
      className={`h-full bg-gradient-to-b from-slate-950/95 to-slate-900/95 backdrop-blur-xl border-r border-cyan-500/20 transition-all duration-300 ${
        collapsed ? 'w-16' : 'w-56'
      }`}
      style={{
        background: 'linear-gradient(to bottom, rgba(2, 6, 23, 0.95), rgba(15, 23, 42, 0.95))',
        backdropFilter: 'blur(20px)',
        boxShadow: '0 0 40px rgba(6, 182, 212, 0.1)'
      }}
    >
      <div className="flex flex-col h-full">
        {/* Header */}
        <div className="p-4 flex items-center justify-between border-b border-cyan-500/20">
          {!collapsed && (
            <div className="flex items-center gap-2">
              <div className="w-2 h-2 rounded-full bg-cyan-400 animate-pulse" />
              <span className="text-cyan-100 tracking-wide">AI MEDIA</span>
            </div>
          )}
          <button
            onClick={() => setCollapsed(!collapsed)}
            className="p-1.5 rounded-lg hover:bg-cyan-500/10 text-cyan-400 transition-all"
          >
            {collapsed ? <ChevronRight size={18} /> : <ChevronLeft size={18} />}
          </button>
        </div>

        {/* Navigation Items */}
        <div className="flex-1 overflow-y-auto py-4 px-2 scrollbar-hide">
          <div className="space-y-1">
            {navItems.map((item) => {
              const Icon = item.icon;

              if (item.path) {
                return (
                  <NavLink
                    key={item.label}
                    to={item.path}
                    end={item.path === '/'}
                    className={({ isActive }) =>
                      `w-full flex items-center gap-3 px-3 py-2.5 rounded-lg transition-all group relative ${
                        isActive
                          ? 'bg-gradient-to-r from-cyan-500/20 to-purple-500/20 text-cyan-100 shadow-lg shadow-cyan-500/20'
                          : 'text-slate-400 hover:text-cyan-300 hover:bg-slate-800/50'
                      }`
                    }
                    title={collapsed ? item.label : undefined}
                  >
                    {({ isActive }) => (
                      <>
                        {/* Active indicator */}
                        {isActive && (
                          <div
                            className="absolute left-0 top-1/2 -translate-y-1/2 w-1 h-8 bg-gradient-to-b from-cyan-400 to-purple-500 rounded-r-full"
                            style={{
                              boxShadow: '0 0 10px rgba(6, 182, 212, 0.5)'
                            }}
                          />
                        )}

                        <Icon size={20} className={isActive ? 'text-cyan-400' : ''} />

                        {!collapsed && (
                          <span className="flex-1 text-left">{item.label}</span>
                        )}

                        {/* Hover glow effect */}
                        {!isActive && (
                          <div className="absolute inset-0 rounded-lg bg-gradient-to-r from-cyan-500/0 to-purple-500/0 group-hover:from-cyan-500/5 group-hover:to-purple-500/5 transition-all" />
                        )}
                      </>
                    )}
                  </NavLink>
                );
              }

              return (
                <button
                  key={item.label}
                  className="w-full flex items-center gap-3 px-3 py-2.5 rounded-lg transition-all group relative text-slate-400 hover:text-cyan-300 hover:bg-slate-800/50"
                  title={collapsed ? item.label : undefined}
                  onClick={() => {
                    if (item.label === 'Addon Manager') {
                      try {
                        const clickLog = '[AddonNavTest] frontend.click type=servers.openAddonManager';
                        console.log(clickLog);
                        (window as any).chrome?.webview?.postMessage({
                          type: 'servers.clientError',
                          payload: {
                            message: clickLog,
                            source: 'sidebar.tsx'
                          }
                        });
                        (window as any).chrome?.webview?.postMessage({ type: 'servers.openAddonManager' });
                      } catch {}
                    }
                  }}
                >
                  <Icon size={20} />

                  {!collapsed && (
                    <span className="flex-1 text-left">{item.label}</span>
                  )}

                  <div className="absolute inset-0 rounded-lg bg-gradient-to-r from-cyan-500/0 to-purple-500/0 group-hover:from-cyan-500/5 group-hover:to-purple-500/5 transition-all" />
                </button>
              );
            })}
          </div>
        </div>
      </div>
    </div>
  );
}
