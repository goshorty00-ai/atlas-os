import { useState } from 'react';
import { Search, Sparkles } from 'lucide-react';

interface AISearchBarProps {
  onSearch: (query: string) => void;
}

export function AISearchBar({ onSearch }: AISearchBarProps) {
  const [query, setQuery] = useState('');
  const [isFocused, setIsFocused] = useState(false);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    onSearch(query);
  };

  return (
    <div className="relative w-full max-w-3xl mx-auto">
      <form onSubmit={handleSubmit} className="relative">
        <div
          className={`relative backdrop-blur-md rounded-2xl border transition-all duration-300 ${
            isFocused
              ? 'bg-white/10 border-cyan-500/50 shadow-[0_0_30px_rgba(6,182,212,0.3)]'
              : 'bg-white/5 border-white/10'
          }`}
        >
          <div className="flex items-center gap-3 px-5 py-4">
            <Search className="w-5 h-5 text-cyan-400" />
            <input
              type="text"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              onFocus={() => setIsFocused(true)}
              onBlur={() => setIsFocused(false)}
              placeholder="Search across all streaming platforms..."
              className="flex-1 bg-transparent text-white placeholder:text-gray-400 outline-none"
            />
            <Sparkles className="w-5 h-5 text-purple-400" />
          </div>
        </div>

        {/* AI Suggestions */}
        {isFocused && (
          <div className="absolute top-full mt-3 w-full backdrop-blur-md bg-white/5 border border-white/10 rounded-xl p-4 shadow-[0_10px_40px_rgba(0,0,0,0.3)]">
            <div className="text-xs text-gray-400 mb-3 flex items-center gap-2">
              <Sparkles className="w-3 h-3 text-purple-400" />
              AI Suggestions
            </div>
            <div className="space-y-2">
              {['Trending sci-fi movies', 'Continue watching', 'New comedy series'].map((suggestion) => (
                <button
                  key={suggestion}
                  type="button"
                  onClick={() => setQuery(suggestion)}
                  className="w-full text-left px-3 py-2 rounded-lg text-sm text-gray-300 hover:bg-white/10 transition-colors"
                >
                  {suggestion}
                </button>
              ))}
            </div>
          </div>
        )}
      </form>
    </div>
  );
}
