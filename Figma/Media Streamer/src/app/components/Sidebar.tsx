import { useState } from 'react';

interface SidebarProps {
  activeSection: string;
  onNavigate: (section: string) => void;
}

export function Sidebar({ activeSection, onNavigate }: SidebarProps) {
  const [isCollapsed, setIsCollapsed] = useState(false);

  const navItems = [
    { id: 'home', label: 'Home', icon: '🏠' },
    { id: 'movies', label: 'Movies', icon: '🎬' },
    { id: 'series', label: 'TV Series', icon: '📺' },
    { id: 'library', label: 'Library', icon: '📚' },
    { id: 'search', label: 'Search', icon: '🔍' },
    { id: 'downloads', label: 'Downloads', icon: '⬇️' },
    { id: 'settings', label: 'Settings', icon: '⚙️' },
  ];

  return (
    <div
      className={`h-full bg-slate-950/50 backdrop-blur-md border-r border-white/5 transition-all duration-300 ${
        isCollapsed ? 'w-20' : 'w-64'
      }`}
    >
      <div className="flex flex-col h-full">
        <div className="p-6 border-b border-white/5">
          <div className="flex items-center justify-between">
            {!isCollapsed && (
              <h1 className="bg-gradient-to-r from-purple-400 to-blue-400 bg-clip-text text-transparent font-bold text-xl">
                StreamAI
              </h1>
            )}
            <button
              onClick={() => setIsCollapsed(!isCollapsed)}
              className="text-gray-400 hover:text-white transition-colors"
            >
              {isCollapsed ? '→' : '←'}
            </button>
          </div>
        </div>

        <nav className="flex-1 p-4 space-y-2">
          {navItems.map((item) => (
            <button
              key={item.id}
              onClick={() => onNavigate(item.id)}
              className={`w-full flex items-center gap-3 px-4 py-3 rounded-lg transition-all duration-200 ${
                activeSection === item.id
                  ? 'bg-gradient-to-r from-purple-500/20 to-blue-500/20 text-white shadow-[0_0_20px_rgba(139,92,246,0.3)]'
                  : 'text-gray-400 hover:text-white hover:bg-white/5'
              }`}
            >
              <span className="text-xl">{item.icon}</span>
              {!isCollapsed && <span className="font-medium">{item.label}</span>}
            </button>
          ))}
        </nav>
      </div>
    </div>
  );
}
