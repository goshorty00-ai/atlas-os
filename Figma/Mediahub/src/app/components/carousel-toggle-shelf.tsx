import { useState } from 'react';
import { Maximize2, Minimize2, ChevronLeft, ChevronRight } from 'lucide-react';
import { Carousel3D, CarouselItem } from './carousel-3d';

interface CarouselToggleShelfProps {
  title: string;
  items: CarouselItem[];
  itemCount?: string;
  showAIBadge?: boolean;
  renderCard?: (item: CarouselItem) => React.ReactNode;
}

export function CarouselToggleShelf({
  title,
  items,
  itemCount,
  showAIBadge,
  renderCard
}: CarouselToggleShelfProps) {
  const [isCarouselOpen, setIsCarouselOpen] = useState(false);

  if (isCarouselOpen) {
    return (
      <div className="relative">
        {/* Header with Close Button */}
        <div className="flex items-center justify-between mb-4">
          <div className="flex items-center gap-3">
            <h3 className="text-slate-200">{title}</h3>
            {itemCount && (
              <span className="px-2 py-1 rounded-lg bg-slate-800 text-slate-400 text-xs">
                {itemCount}
              </span>
            )}
          </div>
          <button
            onClick={() => setIsCarouselOpen(false)}
            className="flex items-center gap-2 px-4 py-2 rounded-lg bg-slate-800/50 text-slate-300 hover:text-white hover:bg-slate-700 border border-slate-700 hover:border-cyan-500/50 transition-all"
          >
            <Minimize2 size={16} />
            <span className="text-sm">Close Carousel</span>
          </button>
        </div>

        {/* 3D Carousel */}
        <Carousel3D items={items} />
      </div>
    );
  }

  // Normal shelf view
  return (
    <div className="space-y-3">
      {/* Header with Open Button */}
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <h3 className="text-slate-200">{title}</h3>
          {itemCount && (
            <span className="px-2 py-1 rounded-lg bg-slate-800 text-slate-400 text-xs">
              {itemCount}
            </span>
          )}
          {showAIBadge && (
            <div className="flex items-center gap-1 px-2 py-1 rounded-lg bg-gradient-to-r from-cyan-500/20 to-purple-500/20 border border-cyan-500/30 text-xs text-cyan-300">
              AI
            </div>
          )}
        </div>
        <div className="flex items-center gap-2">
          <button
            onClick={() => setIsCarouselOpen(true)}
            className="flex items-center gap-2 px-3 py-1.5 rounded-lg bg-gradient-to-r from-cyan-500/20 to-purple-500/20 border border-cyan-500/30 text-cyan-300 hover:from-cyan-500/30 hover:to-purple-500/30 transition-all"
          >
            <Maximize2 size={14} />
            <span className="text-xs">Open Carousel</span>
          </button>
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

      {/* Compact Shelf Cards */}
      <div className="flex gap-4 overflow-x-auto pb-2 scrollbar-hide">
        {items.slice(0, 6).map((item) => (
          <div key={item.id} className="shrink-0" style={{ width: '160px' }}>
            {renderCard ? renderCard(item) : (
              <div className="relative rounded-xl overflow-hidden bg-slate-800 aspect-[2/3] group cursor-pointer">
                <div className="absolute inset-0 bg-gradient-to-br from-slate-700 to-slate-900 flex items-center justify-center">
                  <span className="text-slate-600 text-xs text-center px-2">{item.title}</span>
                </div>

                {/* Type badge */}
                <div className="absolute top-2 left-2 px-2 py-0.5 rounded text-xs backdrop-blur-sm"
                  style={{
                    background: item.type === 'Movie' ? 'rgba(6, 182, 212, 0.3)' :
                               item.type === 'TV' ? 'rgba(168, 85, 247, 0.3)' :
                               'rgba(34, 197, 94, 0.3)',
                    color: item.type === 'Movie' ? '#67e8f9' :
                           item.type === 'TV' ? '#c4b5fd' :
                           '#86efac'
                  }}>
                  {item.type}
                </div>

                {/* Hover overlay */}
                <div className="absolute inset-0 bg-gradient-to-t from-slate-950 via-slate-950/80 to-transparent opacity-0 group-hover:opacity-100 transition-all">
                  <div className="absolute bottom-0 left-0 right-0 p-2">
                    <button className="w-full py-1.5 rounded-lg bg-gradient-to-r from-cyan-500 to-purple-500 text-white text-xs hover:shadow-lg transition-all">
                      View
                    </button>
                  </div>
                </div>
              </div>
            )}
            <div className="mt-2">
              <h4 className="text-slate-200 text-sm truncate">{item.title}</h4>
              <p className="text-xs text-slate-400">{item.releaseDate}</p>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
