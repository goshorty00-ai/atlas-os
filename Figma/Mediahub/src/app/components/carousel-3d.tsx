import { useState, useEffect } from 'react';
import { ChevronLeft, ChevronRight, Play, Plus, Bell, Sparkles } from 'lucide-react';

export interface CarouselItem {
  id: string;
  title: string;
  type: 'Movie' | 'TV' | 'Game';
  releaseDate: string;
  platform: string;
  heatScore: number;
  aiVerdict: string;
  verdictColor: string;
}

interface Carousel3DProps {
  items: CarouselItem[];
  onItemSelect?: (item: CarouselItem) => void;
}

export function Carousel3D({ items, onItemSelect }: Carousel3DProps) {
  const [currentIndex, setCurrentIndex] = useState(0);
  const [autoScroll, setAutoScroll] = useState(false);

  useEffect(() => {
    if (!autoScroll) return;

    const interval = setInterval(() => {
      setCurrentIndex((prev) => (prev + 1) % items.length);
    }, 4000);

    return () => clearInterval(interval);
  }, [autoScroll, items.length]);

  const handlePrev = () => {
    setCurrentIndex((prev) => (prev - 1 + items.length) % items.length);
  };

  const handleNext = () => {
    setCurrentIndex((prev) => (prev + 1) % items.length);
  };

  const getCardStyle = (index: number) => {
    const diff = index - currentIndex;
    const normalizedDiff = ((diff + items.length) % items.length);
    const adjustedDiff = normalizedDiff > items.length / 2 ? normalizedDiff - items.length : normalizedDiff;

    if (adjustedDiff === 0) {
      // Center card
      return {
        transform: 'translateX(0) scale(1) rotateY(0deg)',
        zIndex: 10,
        opacity: 1,
        filter: 'blur(0px)',
      };
    } else if (adjustedDiff === 1) {
      // Right card
      return {
        transform: 'translateX(60%) scale(0.85) rotateY(-25deg)',
        zIndex: 5,
        opacity: 0.6,
        filter: 'blur(1px)',
      };
    } else if (adjustedDiff === -1) {
      // Left card
      return {
        transform: 'translateX(-60%) scale(0.85) rotateY(25deg)',
        zIndex: 5,
        opacity: 0.6,
        filter: 'blur(1px)',
      };
    } else if (adjustedDiff === 2) {
      // Far right
      return {
        transform: 'translateX(120%) scale(0.7) rotateY(-35deg)',
        zIndex: 1,
        opacity: 0.3,
        filter: 'blur(2px)',
      };
    } else if (adjustedDiff === -2) {
      // Far left
      return {
        transform: 'translateX(-120%) scale(0.7) rotateY(35deg)',
        zIndex: 1,
        opacity: 0.3,
        filter: 'blur(2px)',
      };
    } else {
      // Hidden
      return {
        transform: 'translateX(0) scale(0.5)',
        zIndex: 0,
        opacity: 0,
        filter: 'blur(3px)',
      };
    }
  };

  const currentItem = items[currentIndex];

  return (
    <div className="relative">
      {/* Carousel */}
      <div className="relative h-[400px] overflow-hidden">
        <div className="absolute inset-0 flex items-center justify-center" style={{ perspective: '1000px' }}>
          {items.map((item, index) => (
            <div
              key={item.id}
              className="absolute w-64 h-80 transition-all duration-700 ease-out cursor-pointer"
              style={{
                ...getCardStyle(index),
                transformStyle: 'preserve-3d',
              }}
              onClick={() => {
                setCurrentIndex(index);
                onItemSelect?.(item);
              }}
            >
              <div className="relative w-full h-full rounded-xl overflow-hidden bg-gradient-to-br from-slate-700 to-slate-900 border border-slate-600/50 shadow-2xl">
                {/* Poster placeholder */}
                <div className="absolute inset-0 flex items-center justify-center">
                  <span className="text-slate-500 text-center px-4">{item.title}</span>
                </div>

                {/* Type badge */}
                <div className="absolute top-3 left-3 px-2 py-1 rounded-lg text-xs backdrop-blur-sm"
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

                {/* Heat score */}
                <div className="absolute top-3 right-3 px-2 py-1 rounded-lg text-xs backdrop-blur-sm bg-amber-500/30 text-amber-200 flex items-center gap-1">
                  <span>🔥</span>
                  <span>{item.heatScore}</span>
                </div>

                {/* Center - only show on active card */}
                {index === currentIndex && (
                  <div className="absolute inset-0 flex items-center justify-center bg-slate-950/40 backdrop-blur-sm">
                    <Play size={64} className="text-white/80" />
                  </div>
                )}
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* Navigation */}
      <div className="absolute top-1/2 -translate-y-1/2 left-4 right-4 flex items-center justify-between pointer-events-none">
        <button
          onClick={handlePrev}
          className="p-3 rounded-xl bg-slate-900/80 backdrop-blur-sm border border-slate-700 text-slate-300 hover:text-white hover:border-cyan-500/50 transition-all pointer-events-auto"
        >
          <ChevronLeft size={24} />
        </button>
        <button
          onClick={handleNext}
          className="p-3 rounded-xl bg-slate-900/80 backdrop-blur-sm border border-slate-700 text-slate-300 hover:text-white hover:border-cyan-500/50 transition-all pointer-events-auto"
        >
          <ChevronRight size={24} />
        </button>
      </div>

      {/* Info Rail */}
      <div className="mt-6 bg-gradient-to-br from-slate-900/80 to-slate-800/80 backdrop-blur-xl rounded-xl border border-slate-700/50 p-6">
        <div className="flex items-start justify-between">
          <div className="flex-1">
            <h3 className="text-slate-100 text-xl mb-2">{currentItem.title}</h3>
            <div className="flex items-center gap-3 text-sm mb-3">
              <span className="px-2 py-1 rounded bg-slate-800 text-slate-300">{currentItem.releaseDate}</span>
              <span className="px-2 py-1 rounded bg-slate-800 text-slate-300">{currentItem.platform}</span>
              <div className={`px-2 py-1 rounded ${currentItem.verdictColor}`}>
                <Sparkles size={14} className="inline mr-1" />
                {currentItem.aiVerdict}
              </div>
            </div>
            <div className="flex gap-2">
              <button className="flex items-center gap-2 px-4 py-2 rounded-lg bg-gradient-to-r from-cyan-500 to-purple-500 text-white hover:shadow-lg hover:shadow-cyan-500/30 transition-all">
                <Play size={16} />
                <span>Watch Trailer</span>
              </button>
              <button className="p-2 rounded-lg bg-slate-800/50 text-slate-300 hover:text-white hover:bg-slate-700 transition-all">
                <Bell size={16} />
              </button>
              <button className="p-2 rounded-lg bg-slate-800/50 text-slate-300 hover:text-white hover:bg-slate-700 transition-all">
                <Plus size={16} />
              </button>
            </div>
          </div>
          <div className="flex items-center gap-2">
            <span className="text-xs text-slate-400">Auto-scroll</span>
            <button
              onClick={() => setAutoScroll(!autoScroll)}
              className={`relative w-11 h-6 rounded-full transition-colors ${
                autoScroll ? 'bg-cyan-500' : 'bg-slate-700'
              }`}
            >
              <div
                className={`absolute top-1 w-4 h-4 rounded-full bg-white transition-transform ${
                  autoScroll ? 'translate-x-6' : 'translate-x-1'
                }`}
              />
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
