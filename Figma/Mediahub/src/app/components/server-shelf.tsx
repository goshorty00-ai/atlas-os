import { ServerCard, MediaItem } from './server-card';
import { ChevronLeft, ChevronRight, Grid3x3, Maximize2, ArrowUpDown } from 'lucide-react';
import { useRef, useState } from 'react';

interface ServerShelfProps {
  title: string;
  items: MediaItem[];
  count?: number;
  serverFilter?: string;
  onViewAll: () => void;
  onOpenCarousel: () => void;
  onSort?: () => void;
}

export function ServerShelf({
  title,
  items,
  count,
  serverFilter,
  onViewAll,
  onOpenCarousel,
  onSort
}: ServerShelfProps) {
  const scrollRef = useRef<HTMLDivElement>(null);
  const [showLeftArrow, setShowLeftArrow] = useState(false);
  const [showRightArrow, setShowRightArrow] = useState(true);

  const scroll = (direction: 'left' | 'right') => {
    if (scrollRef.current) {
      const scrollAmount = 600;
      const newScrollLeft = scrollRef.current.scrollLeft + (direction === 'left' ? -scrollAmount : scrollAmount);
      scrollRef.current.scrollTo({ left: newScrollLeft, behavior: 'smooth' });

      setTimeout(() => {
        checkArrows();
      }, 300);
    }
  };

  const checkArrows = () => {
    if (scrollRef.current) {
      const { scrollLeft, scrollWidth, clientWidth } = scrollRef.current;
      setShowLeftArrow(scrollLeft > 10);
      setShowRightArrow(scrollLeft < scrollWidth - clientWidth - 10);
    }
  };

  return (
    <div className="space-y-3">
      {/* Shelf Header */}
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <h3 className="text-slate-200">{title}</h3>
          {count !== undefined && (
            <span className="px-2 py-0.5 rounded-full bg-slate-700/50 text-slate-400 text-xs">{count} items</span>
          )}
          {serverFilter && (
            <span className="px-2 py-0.5 rounded-full bg-violet-500/20 text-violet-300 text-xs">{serverFilter}</span>
          )}
        </div>
        <div className="flex items-center gap-2">
          {onSort && (
            <button
              onClick={onSort}
              className="flex items-center gap-1 px-3 py-1.5 rounded-lg bg-slate-800/50 text-slate-300 text-xs hover:bg-slate-700/50 transition-colors border border-slate-700/30"
            >
              <ArrowUpDown size={14} />
              Sort
            </button>
          )}
          <button
            onClick={onViewAll}
            className="flex items-center gap-1 px-3 py-1.5 rounded-lg bg-slate-800/50 text-slate-300 text-xs hover:bg-slate-700/50 transition-colors border border-slate-700/30"
          >
            <Grid3x3 size={14} />
            View All
          </button>
          <button
            onClick={onOpenCarousel}
            className="flex items-center gap-1 px-3 py-1.5 rounded-lg bg-gradient-to-r from-cyan-500 to-purple-500 text-white text-xs hover:shadow-lg hover:shadow-cyan-500/30 transition-all"
          >
            <Maximize2 size={14} />
            Open Carousel
          </button>
        </div>
      </div>

      {/* Shelf Content */}
      <div className="relative group/shelf">
        {/* Left Arrow */}
        {showLeftArrow && (
          <button
            onClick={() => scroll('left')}
            className="absolute left-0 top-1/2 -translate-y-1/2 z-10 p-2 rounded-full bg-slate-900/90 text-white hover:bg-slate-800 transition-all shadow-xl backdrop-blur-sm opacity-0 group-hover/shelf:opacity-100"
          >
            <ChevronLeft size={24} />
          </button>
        )}

        {/* Right Arrow */}
        {showRightArrow && (
          <button
            onClick={() => scroll('right')}
            className="absolute right-0 top-1/2 -translate-y-1/2 z-10 p-2 rounded-full bg-slate-900/90 text-white hover:bg-slate-800 transition-all shadow-xl backdrop-blur-sm opacity-0 group-hover/shelf:opacity-100"
          >
            <ChevronRight size={24} />
          </button>
        )}

        {/* Scrollable Cards */}
        <div
          ref={scrollRef}
          onScroll={checkArrows}
          className="flex gap-5 overflow-x-auto scrollbar-hide pt-2 pb-4 pl-4 pr-4 scroll-smooth"
        >
          {items.map((item) => (
            <ServerCard key={item.id} item={item} />
          ))}
        </div>
      </div>
    </div>
  );
}
