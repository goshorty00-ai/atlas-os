import { useState } from 'react';
import { Search, Sparkles, Mic, Heart, Wand2, Eraser, Subtitles, PartyPopper, Music } from 'lucide-react';

const quickActions = [
  { icon: Heart, label: 'Mood Search', color: 'from-pink-500 to-rose-500' },
  { icon: Wand2, label: 'Auto Shelf', color: 'from-cyan-500 to-blue-500' },
  { icon: Mic, label: 'Voice Control', color: 'from-purple-500 to-violet-500' },
  { icon: Eraser, label: 'Clean Library', color: 'from-green-500 to-emerald-500' },
  { icon: Subtitles, label: 'Subtitle Finder', color: 'from-orange-500 to-amber-500' },
  { icon: PartyPopper, label: 'Party Mode', color: 'from-fuchsia-500 to-pink-500' },
  { icon: Music, label: 'Karaoke Mode', color: 'from-indigo-500 to-purple-500' },
];

export function AICommandBar() {
  const [focused, setFocused] = useState(false);
  const [query, setQuery] = useState('');

  return (
    <div className="space-y-4">
      {/* Search Bar */}
      <div
        className={`relative bg-gradient-to-r from-slate-900/80 to-slate-800/80 backdrop-blur-xl rounded-2xl border transition-all duration-300 ${
          focused
            ? 'border-cyan-500/50 shadow-lg shadow-cyan-500/20 scale-[1.01]'
            : 'border-slate-700/50'
        }`}
        style={{
          backdropFilter: 'blur(20px)',
        }}
      >
        <div className="flex items-center gap-3 px-5 py-4">
          <Sparkles
            size={22}
            className={`transition-colors ${
              focused ? 'text-cyan-400' : 'text-slate-500'
            }`}
          />
          <input
            type="text"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            onFocus={() => setFocused(true)}
            onBlur={() => setFocused(false)}
            placeholder="Ask AI anything... 'Find something like Interstellar but darker'"
            className="flex-1 bg-transparent text-slate-200 placeholder:text-slate-500 outline-none"
          />
          <button className="p-2 rounded-lg bg-gradient-to-r from-cyan-500 to-purple-500 text-white hover:shadow-lg hover:shadow-cyan-500/30 transition-all">
            <Search size={18} />
          </button>
        </div>

        {/* Glow effect */}
        {focused && (
          <div
            className="absolute -inset-0.5 bg-gradient-to-r from-cyan-500 to-purple-500 rounded-2xl -z-10 blur opacity-20"
          />
        )}
      </div>

      {/* Quick Action Chips */}
      <div className="flex flex-wrap gap-2">
        {quickActions.map((action) => {
          const Icon = action.icon;
          return (
            <button
              key={action.label}
              className="group relative px-4 py-2 rounded-xl bg-slate-900/50 backdrop-blur-sm border border-slate-700/50 hover:border-slate-600 text-slate-300 hover:text-white transition-all hover:scale-105"
            >
              <div className="flex items-center gap-2">
                <Icon size={16} className="group-hover:animate-pulse" />
                <span className="text-sm">{action.label}</span>
              </div>

              {/* Hover gradient background */}
              <div
                className={`absolute inset-0 rounded-xl bg-gradient-to-r ${action.color} opacity-0 group-hover:opacity-10 transition-opacity -z-10`}
              />
            </button>
          );
        })}
      </div>
    </div>
  );
}
