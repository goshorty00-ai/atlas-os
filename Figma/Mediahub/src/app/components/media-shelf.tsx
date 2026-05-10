import { useState } from 'react';
import { Play, Info, Plus, Video, ChevronLeft, ChevronRight, Sparkles } from 'lucide-react';

interface MediaItem {
  id: string;
  title: string;
  year: string;
  type: 'movie' | 'tv' | 'music' | 'game';
  rating: string;
  server: string;
  progress?: number;
  aiReason?: string;
  image: string;
}

interface MediaShelfProps {
  title: string;
  items: MediaItem[];
  itemCount?: string;
  showAIBadge?: boolean;
}

function MediaCard({ item }: { item: MediaItem }) {
  const [hovered, setHovered] = useState(false);

  return (
    <div
      className="relative group cursor-pointer shrink-0"
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      style={{ width: '160px' }}
    >
      {/* Poster */}
      <div className="relative rounded-xl overflow-hidden bg-slate-800 aspect-[2/3]">
        <div
          className="absolute inset-0 bg-gradient-to-br from-slate-700 to-slate-900 flex items-center justify-center"
        >
          <span className="text-slate-600 text-xs text-center px-2">{item.title}</span>
        </div>

        {/* Progress bar if applicable */}
        {item.progress !== undefined && (
          <div className="absolute bottom-0 left-0 right-0 h-1 bg-slate-900/50">
            <div
              className="h-full bg-gradient-to-r from-cyan-500 to-purple-500"
              style={{ width: `${item.progress}%` }}
            />
          </div>
        )}

        {/* AI Badge */}
        {item.aiReason && (
          <div className="absolute top-2 right-2 p-1.5 rounded-lg bg-gradient-to-r from-cyan-500/90 to-purple-500/90 backdrop-blur-sm">
            <Sparkles size={12} className="text-white" />
          </div>
        )}

        {/* Hover overlay */}
        <div
          className={`absolute inset-0 bg-gradient-to-t from-slate-950 via-slate-950/80 to-transparent transition-all duration-300 ${
            hovered ? 'opacity-100' : 'opacity-0'
          }`}
        >
          <div className="absolute bottom-0 left-0 right-0 p-3 space-y-2">
            {/* Action buttons */}
            <div className="flex gap-2">
              <button className="flex-1 p-2 rounded-lg bg-gradient-to-r from-cyan-500 to-purple-500 text-white hover:shadow-lg hover:shadow-cyan-500/30 transition-all flex items-center justify-center gap-1">
                <Play size={14} />
              </button>
              <button className="p-2 rounded-lg bg-slate-800/80 text-slate-300 hover:text-white hover:bg-slate-700 transition-all">
                <Video size={14} />
              </button>
              <button className="p-2 rounded-lg bg-slate-800/80 text-slate-300 hover:text-white hover:bg-slate-700 transition-all">
                <Info size={14} />
              </button>
              <button className="p-2 rounded-lg bg-slate-800/80 text-slate-300 hover:text-white hover:bg-slate-700 transition-all">
                <Plus size={14} />
              </button>
            </div>
          </div>
        </div>

        {/* Glow on hover */}
        {hovered && (
          <div className="absolute -inset-0.5 bg-gradient-to-r from-cyan-500 to-purple-500 rounded-xl -z-10 blur opacity-30" />
        )}
      </div>

      {/* Info */}
      <div className="mt-2 space-y-1">
        <h4 className="text-slate-200 text-sm truncate">{item.title}</h4>
        <div className="flex items-center gap-2 text-xs">
          <span className="px-1.5 py-0.5 rounded bg-slate-800 text-slate-400">{item.year}</span>
          <span className="px-1.5 py-0.5 rounded bg-amber-500/20 text-amber-400">{item.rating}</span>
        </div>
        {item.aiReason && (
          <p className="text-xs text-cyan-400 truncate">{item.aiReason}</p>
        )}
      </div>
    </div>
  );
}

export function MediaShelf({ title, items, itemCount, showAIBadge }: MediaShelfProps) {
  return (
    <div className="space-y-3">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <h3 className="text-slate-200">{title}</h3>
          {itemCount && (
            <span className="px-2 py-1 rounded-lg bg-slate-800 text-slate-400 text-xs">
              {itemCount}
            </span>
          )}
          {showAIBadge && (
            <div className="flex items-center gap-1 px-2 py-1 rounded-lg bg-gradient-to-r from-cyan-500/20 to-purple-500/20 border border-cyan-500/30">
              <Sparkles size={12} className="text-cyan-400" />
              <span className="text-xs text-cyan-300">AI</span>
            </div>
          )}
        </div>
        <div className="flex items-center gap-2">
          <button className="text-xs text-cyan-400 hover:text-cyan-300 transition-colors">
            View All
          </button>
          <div className="flex gap-1">
            <button className="p-1.5 rounded-lg bg-slate-800/50 text-slate-400 hover:text-white hover:bg-slate-700 transition-all">
              <ChevronLeft size={16} />
            </button>
            <button className="p-1.5 rounded-lg bg-slate-800/50 text-slate-400 hover:text-white hover:bg-slate-700 transition-all">
              <ChevronRight size={16} />
            </button>
          </div>
        </div>
      </div>

      {/* Shelf */}
      <div className="flex gap-4 overflow-x-auto pb-2 scrollbar-hide">
        {items.map((item) => (
          <MediaCard key={item.id} item={item} />
        ))}
      </div>
    </div>
  );
}
