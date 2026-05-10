import { X, ChevronLeft, ChevronRight, Play, Film, Info, Plus, Star, Pause } from 'lucide-react';
import { MediaItem } from './server-card';
import { useState, useEffect, useCallback } from 'react';
import { ImageWithFallback } from './figma/ImageWithFallback';

interface CarouselOverlayProps {
  isOpen: boolean;
  items: MediaItem[];
  initialIndex?: number;
  shelfName: string;
  onClose: () => void;
}

export function CarouselOverlay({ isOpen, items, initialIndex = 0, shelfName, onClose }: CarouselOverlayProps) {
  const [currentIndex, setCurrentIndex] = useState(initialIndex);
  const [autoRotate, setAutoRotate] = useState(false);

  const goToNext = useCallback(() => {
    setCurrentIndex((p) => (p + 1) % items.length);
  }, [items.length]);
  const goToPrevious = useCallback(() => {
    setCurrentIndex((p) => (p - 1 + items.length) % items.length);
  }, [items.length]);

  useEffect(() => {
    if (!isOpen) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
      if (e.key === 'ArrowLeft') goToPrevious();
      if (e.key === 'ArrowRight') goToNext();
    };
    let wheelLock = false;
    const onWheel = (e: WheelEvent) => {
      if (wheelLock) return;
      wheelLock = true;
      if (e.deltaY > 0) goToNext();
      else if (e.deltaY < 0) goToPrevious();
      setTimeout(() => { wheelLock = false; }, 250);
    };
    window.addEventListener('keydown', onKey);
    window.addEventListener('wheel', onWheel, { passive: true });
    return () => {
      window.removeEventListener('keydown', onKey);
      window.removeEventListener('wheel', onWheel);
    };
  }, [isOpen, goToNext, goToPrevious, onClose]);

  useEffect(() => {
    if (!autoRotate || !isOpen) return;
    const id = setInterval(goToNext, 3000);
    return () => clearInterval(id);
  }, [autoRotate, isOpen, goToNext]);

  if (!isOpen || items.length === 0) return null;
  const currentItem = items[currentIndex];

  // 7 visible: -3..+3
  const visible = [-3, -2, -1, 0, 1, 2, 3].map((offset) => {
    const index = (currentIndex + offset + items.length) % items.length;
    return { item: items[index], offset, index };
  });

  return (
    <div className="fixed inset-0 z-50 flex flex-col bg-slate-950/98 backdrop-blur-2xl overflow-hidden">
      {/* Soft blurred backdrop from selected poster */}
      {currentItem.posterUrl && (
        <div
          key={currentItem.id}
          className="absolute inset-0 bg-cover bg-center opacity-25 blur-3xl scale-110 transition-opacity duration-700"
          style={{ backgroundImage: `url(${currentItem.posterUrl})` }}
        />
      )}
      <div className="absolute inset-0 bg-gradient-to-b from-slate-950/60 via-transparent to-slate-950/90" />

      {/* Top bar */}
      <div className="relative z-10 flex items-center justify-between px-5 py-3 border-b border-white/5">
        <div className="flex items-center gap-3">
          <button
            onClick={onClose}
            className="p-1.5 rounded-md bg-slate-800/60 hover:bg-slate-700 text-slate-200 border border-slate-700/50 transition-colors"
            title="Close (Esc)"
          >
            <X size={16} />
          </button>
          <div className="flex items-baseline gap-3">
            <span className="text-slate-100 text-sm">{shelfName}</span>
            <span className="text-xs text-slate-400">{currentIndex + 1} of {items.length}</span>
          </div>
        </div>
        <div className="flex items-center gap-3">
          <button
            onClick={() => setAutoRotate((v) => !v)}
            className={`flex items-center gap-1.5 px-2.5 py-1 rounded-md text-xs border transition-colors ${
              autoRotate
                ? 'bg-cyan-500/20 border-cyan-400/50 text-cyan-200'
                : 'bg-slate-800/60 border-slate-700/50 text-slate-300 hover:border-slate-600'
            }`}
          >
            {autoRotate ? <Pause size={11} /> : <Play size={11} fill="currentColor" />}
            Auto Rotate
          </button>
          <span className="hidden md:inline text-[10px] text-slate-500">
            ESC close · ← → navigate · scroll wheel
          </span>
        </div>
      </div>

      {/* Coverflow stage */}
      <div
        className="relative z-10 flex-1 flex items-center justify-center"
        style={{ perspective: '1800px' }}
      >
        <button
          onClick={goToPrevious}
          className="absolute left-4 z-20 p-2 rounded-full bg-slate-900/70 hover:bg-slate-800 text-slate-200 border border-slate-700/50 backdrop-blur-md transition-colors"
        >
          <ChevronLeft size={22} />
        </button>
        <button
          onClick={goToNext}
          className="absolute right-4 z-20 p-2 rounded-full bg-slate-900/70 hover:bg-slate-800 text-slate-200 border border-slate-700/50 backdrop-blur-md transition-colors"
        >
          <ChevronRight size={22} />
        </button>

        <div
          className="relative w-full h-full flex items-center justify-center"
          style={{ transformStyle: 'preserve-3d' }}
        >
          {visible.map(({ item, offset, index }) => {
            const abs = Math.abs(offset);
            const isCenter = offset === 0;
            const translateX = offset * 165;
            const translateZ = isCenter ? 0 : -abs * 160;
            const rotateY = offset === 0 ? 0 : (offset > 0 ? -38 : 38);
            const scale = isCenter ? 1 : Math.max(0.65, 1 - abs * 0.12);
            const opacity = abs > 3 ? 0 : 1 - abs * 0.18;
            const blur = isCenter ? 0 : Math.min(abs * 1.2, 4);
            const brightness = isCenter ? 1 : Math.max(0.55, 1 - abs * 0.18);

            return (
              <div
                key={item.id}
                className="absolute transition-all duration-500 ease-out cursor-pointer"
                style={{
                  transform: `translateX(${translateX}px) translateZ(${translateZ}px) rotateY(${rotateY}deg) scale(${scale})`,
                  opacity,
                  zIndex: 50 - abs,
                  filter: `blur(${blur}px) brightness(${brightness})`,
                  transformStyle: 'preserve-3d',
                }}
                onClick={() => setCurrentIndex(index)}
              >
                <div
                  className={`relative w-[260px] h-[390px] rounded-2xl overflow-hidden bg-slate-900 ${
                    isCenter
                      ? 'ring-1 ring-cyan-400/60 shadow-[0_0_60px_-5px_rgba(34,211,238,0.55)]'
                      : 'ring-1 ring-slate-700/40 shadow-2xl'
                  }`}
                >
                  {item.posterUrl ? (
                    <ImageWithFallback
                      src={item.posterUrl}
                      alt={item.title}
                      className="w-full h-full object-cover"
                    />
                  ) : (
                    <div className="w-full h-full flex items-center justify-center text-slate-600 text-sm p-4 text-center">
                      {item.title}
                    </div>
                  )}

                  {/* Small badges on every poster */}
                  <div className="absolute top-2 left-2 flex flex-col gap-1">
                    <span className="px-1.5 py-0.5 rounded bg-slate-900/80 text-slate-100 text-[10px] backdrop-blur-sm">
                      {item.type}
                    </span>
                  </div>
                  <div className="absolute top-2 right-2 flex flex-col items-end gap-1">
                    {item.rating !== undefined && (
                      <span className="flex items-center gap-1 px-1.5 py-0.5 rounded bg-slate-900/80 text-amber-300 text-[10px] backdrop-blur-sm">
                        <Star size={9} fill="currentColor" /> {item.rating.toFixed(1)}
                      </span>
                    )}
                    <span className="px-1.5 py-0.5 rounded bg-violet-500/30 text-violet-100 text-[10px] backdrop-blur-sm border border-violet-400/30">
                      {item.server}
                    </span>
                  </div>

                  {/* Centre hover overlay */}
                  {isCenter && (
                    <div className="absolute inset-0 opacity-0 hover:opacity-100 transition-opacity bg-gradient-to-t from-slate-950/80 via-slate-950/20 to-transparent flex items-end justify-center gap-2 p-4">
                      <button className="p-2 rounded-full bg-cyan-500 text-white shadow-lg hover:bg-cyan-400">
                        <Play size={16} fill="currentColor" />
                      </button>
                      <button className="p-2 rounded-full bg-slate-800/90 text-slate-100 border border-slate-700 hover:bg-slate-700">
                        <Film size={16} />
                      </button>
                    </div>
                  )}
                </div>
              </div>
            );
          })}
        </div>
      </div>

      {/* Compact info rail */}
      <div className="relative z-10 border-t border-white/5 bg-slate-950/70 backdrop-blur-xl">
        <div className="max-w-5xl mx-auto px-6 py-4 flex items-center gap-5">
          <div className="flex-1 min-w-0">
            <div className="flex items-baseline gap-2 mb-1">
              <h3 className="text-slate-100 truncate">{currentItem.title}</h3>
              <span className="text-xs text-slate-400">{currentItem.year}</span>
            </div>
            <div className="flex items-center gap-2 flex-wrap text-[11px]">
              {currentItem.runtime && (
                <span className="text-slate-400">{currentItem.runtime}</span>
              )}
              {currentItem.rating !== undefined && (
                <span className="flex items-center gap-1 text-amber-300">
                  <Star size={10} fill="currentColor" /> {currentItem.rating.toFixed(1)}
                </span>
              )}
              {currentItem.genre && (
                <span className="px-1.5 py-0.5 rounded bg-slate-800/70 text-slate-300 border border-slate-700/40">
                  {currentItem.genre}
                </span>
              )}
              <span className="px-1.5 py-0.5 rounded bg-violet-500/15 text-violet-200 border border-violet-400/30">
                {currentItem.server}
              </span>
              {currentItem.quality && (
                <span className="px-1.5 py-0.5 rounded bg-cyan-500/20 text-cyan-200 border border-cyan-400/30">
                  {currentItem.quality}
                </span>
              )}
              <span className="px-1.5 py-0.5 rounded bg-slate-800/70 text-slate-300 border border-slate-700/40">
                {currentItem.type}
              </span>
            </div>
            {currentItem.description && (
              <p className="text-xs text-slate-400 mt-1.5 truncate">
                {currentItem.description}
              </p>
            )}
          </div>

          <div className="flex items-center gap-1.5 flex-shrink-0">
            <button className="flex items-center gap-1.5 px-3 py-1.5 rounded-full bg-gradient-to-r from-cyan-500 to-blue-500 text-white text-xs hover:shadow-lg hover:shadow-cyan-500/40 transition-all">
              <Play size={12} fill="currentColor" /> Play
            </button>
            <button className="flex items-center gap-1.5 px-3 py-1.5 rounded-full bg-slate-800/70 text-slate-200 text-xs border border-slate-700/50 hover:border-violet-400/50 hover:text-violet-200 transition-colors">
              <Film size={12} /> Trailer
            </button>
            <button className="flex items-center gap-1.5 px-3 py-1.5 rounded-full bg-slate-800/70 text-slate-200 text-xs border border-slate-700/50 hover:border-slate-500 transition-colors">
              <Info size={12} /> Details
            </button>
            <button className="flex items-center gap-1.5 px-3 py-1.5 rounded-full bg-slate-800/70 text-slate-200 text-xs border border-slate-700/50 hover:border-slate-500 transition-colors">
              <Plus size={12} /> Add
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
