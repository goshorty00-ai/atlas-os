import { Search, Bell, Settings, User, Mic } from 'lucide-react';
import { useRef, useState } from 'react';
import { postToHost } from '../atlasBridge';

interface HeaderProps {
  activeSection: string;
  searchQuery: string;
  onSearchQueryChange: (value: string) => void;
  onOpenIntegrations: () => void;
  onOpenLogs: () => void;
}

export function Header({ activeSection, searchQuery, onSearchQueryChange, onOpenIntegrations, onOpenLogs }: HeaderProps) {
  const [micNote, setMicNote] = useState('');
  const micTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  const handleMicClick = () => {
    // IsMicWired("ApiManagement") = false — mic is not wired; never auto-search.
    setMicNote('Mic not wired');
    if (micTimer.current) clearTimeout(micTimer.current);
    micTimer.current = setTimeout(() => setMicNote(''), 2400);
  };

  return (
    <header className="h-20 border-b border-blue-500/20 bg-[#0a0a0f]/80 backdrop-blur-xl px-8 flex items-center justify-between relative">
      {/* Glowing bottom edge */}
      <div className="absolute bottom-0 left-0 right-0 h-[1px] bg-gradient-to-r from-transparent via-blue-400/50 to-transparent" />
      
      <div className="flex items-center space-x-8">
        <div>
          <h1 className="text-2xl font-light tracking-wider">
            <span className="bg-gradient-to-r from-blue-400 to-violet-400 bg-clip-text text-transparent">
              ATLAS
            </span>
          </h1>
          <p className="text-xs text-gray-500 tracking-wide uppercase mt-0.5">
            Infrastructure Control
          </p>
        </div>
        
        <div className="h-8 w-[1px] bg-gradient-to-b from-transparent via-blue-500/30 to-transparent" />
        
        <span className="text-sm text-gray-400 capitalize font-light">
          {activeSection}
        </span>
      </div>

      <div className="flex items-center space-x-4">
        {/* Search */}
        <div className="flex flex-col items-end">
          <div className="relative group flex items-center gap-1">
            <div className="relative">
              <input
                type="text"
                placeholder="Search..."
                value={searchQuery}
                onChange={(e) => onSearchQueryChange(e.target.value)}
                className="w-64 h-10 pl-10 pr-4 bg-blue-500/5 border border-blue-500/20 rounded-lg text-sm text-gray-300 placeholder-gray-600 focus:outline-none focus:border-blue-400/50 focus:bg-blue-500/10 transition-all"
              />
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-500 group-focus-within:text-blue-400 transition-colors" />
            </div>
            <button
              type="button"
              onClick={handleMicClick}
              title="Search mic"
              className="w-10 h-10 rounded-lg bg-blue-500/5 border border-blue-500/20 hover:border-blue-400/50 hover:bg-blue-500/10 flex items-center justify-center transition-all flex-shrink-0"
            >
              <Mic className="w-4 h-4 text-gray-400 hover:text-blue-400 transition-colors" />
            </button>
          </div>
          {micNote && (
            <p className="text-xs text-gray-500 font-mono mt-0.5 pr-1">{micNote}</p>
          )}
        </div>

        <div className="h-8 w-[1px] bg-gradient-to-b from-transparent via-blue-500/30 to-transparent" />

        {/* Actions */}
        <button
          className="w-10 h-10 rounded-lg bg-blue-500/5 border border-blue-500/20 hover:border-blue-400/50 hover:bg-blue-500/10 flex items-center justify-center transition-all group relative"
          onClick={() => postToHost('api.getState')}
        >
          <Bell className="w-4 h-4 text-gray-400 group-hover:text-blue-400 transition-colors" />
          <div className="absolute top-2 right-2 w-1.5 h-1.5 bg-violet-400 rounded-full animate-pulse" />
        </button>

        <button
          className="w-10 h-10 rounded-lg bg-blue-500/5 border border-blue-500/20 hover:border-blue-400/50 hover:bg-blue-500/10 flex items-center justify-center transition-all group"
          onClick={onOpenIntegrations}
        >
          <Settings className="w-4 h-4 text-gray-400 group-hover:text-blue-400 transition-colors" />
        </button>

        <button
          className="w-10 h-10 rounded-lg bg-gradient-to-br from-blue-500/20 to-violet-500/20 border border-blue-400/30 hover:border-blue-400/50 flex items-center justify-center transition-all group shadow-lg shadow-blue-500/20"
          onClick={onOpenLogs}
        >
          <User className="w-4 h-4 text-blue-300 group-hover:text-blue-200 transition-colors" />
        </button>
      </div>
    </header>
  );
}
