import { useState, useEffect } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import { ChevronLeft, ChevronRight, Flame, Play, Plus } from 'lucide-react';
import type { StreamingApp } from './streaming-data';

interface CarouselViewProps {
  apps: StreamingApp[];
  onAppSelect: (app: StreamingApp) => void;
}

export function CarouselView({ apps, onAppSelect }: CarouselViewProps) {
  const [currentIndex, setCurrentIndex] = useState(0);
  const itemsPerView = 5;
  const centerIndex = Math.floor(itemsPerView / 2);

  const visibleApps = apps.slice(currentIndex, currentIndex + itemsPerView);
  const currentApp = visibleApps[centerIndex] || visibleApps[0];

  const handlePrevious = () => {
    setCurrentIndex((prev) => Math.max(0, prev - 1));
  };

  const handleNext = () => {
    setCurrentIndex((prev) => Math.min(apps.length - itemsPerView, prev + 1));
  };

  return (
    <div className="relative py-12">
      {/* Background Artwork */}
      <AnimatePresence mode="wait">
        {currentApp && currentApp.trendingContent[0] && (
          <motion.div
            key={currentApp.id}
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.5 }}
            className="absolute inset-0 -z-10"
          >
            <div
              className="absolute inset-0 bg-cover bg-center"
              style={{
                backgroundImage: `url(${currentApp.trendingContent[0].imageUrl})`,
              }}
            />
            <div className="absolute inset-0 bg-gradient-to-t from-[#0a0a0f] via-[#0a0a0f]/80 to-[#0a0a0f]/40" />
            <div className="absolute inset-0 backdrop-blur-3xl" />
          </motion.div>
        )}
      </AnimatePresence>

      {/* Carousel */}
      <div className="relative flex items-center justify-center gap-6 h-80">
        {visibleApps.map((app, index) => {
          const isCentered = index === centerIndex;
          const distance = Math.abs(index - centerIndex);
          const scale = isCentered ? 1.2 : 1 - distance * 0.15;
          const opacity = isCentered ? 1 : 0.4;
          const zIndex = itemsPerView - distance;

          return (
            <motion.div
              key={app.id}
              animate={{
                scale,
                opacity,
                zIndex,
                x: (index - centerIndex) * 20,
              }}
              transition={{ type: 'spring', stiffness: 300, damping: 30 }}
              onClick={() => onAppSelect(app)}
              className="relative cursor-pointer"
              style={{ width: '200px' }}
            >
              <div
                className={`relative aspect-square rounded-2xl overflow-hidden transition-all duration-300 ${
                  isCentered
                    ? 'shadow-[0_0_60px_rgba(6,182,212,0.5)]'
                    : 'shadow-lg'
                }`}
                style={{
                  background: `linear-gradient(135deg, ${app.color}30 0%, ${app.color}15 100%)`,
                }}
              >
                {/* App Logo */}
                <div className="absolute inset-0 flex items-center justify-center p-8 backdrop-blur-sm bg-black/50">
                  <img
                    src={app.logoUrl}
                    alt={app.name}
                    className="w-full h-full object-contain"
                  />
                </div>

                {/* Glow Border */}
                {isCentered && (
                  <div
                    className="absolute inset-0 rounded-2xl"
                    style={{
                      boxShadow: `inset 0 0 60px ${app.color}60`,
                      border: `2px solid ${app.color}80`,
                    }}
                  />
                )}

                {/* AI Indicators */}
                <div className="absolute top-2 right-2 flex flex-col gap-1">
                  {app.trending && (
                    <div className="w-6 h-6 rounded-full bg-red-500/80 backdrop-blur-sm flex items-center justify-center">
                      <Flame className="w-3 h-3 text-white" />
                    </div>
                  )}
                  {app.continueWatching && (
                    <div className="w-6 h-6 rounded-full bg-cyan-500/80 backdrop-blur-sm flex items-center justify-center">
                      <Play className="w-3 h-3 text-white" />
                    </div>
                  )}
                  {app.newReleases && (
                    <div className="w-6 h-6 rounded-full bg-purple-500/80 backdrop-blur-sm flex items-center justify-center">
                      <Plus className="w-3 h-3 text-white" />
                    </div>
                  )}
                </div>
              </div>

              {/* App Name */}
              {isCentered && (
                <motion.div
                  initial={{ opacity: 0, y: 10 }}
                  animate={{ opacity: 1, y: 0 }}
                  className="mt-4 text-center"
                >
                  <div className="text-xl text-white font-medium">{app.name}</div>
                  <div className="text-sm text-gray-400 mt-1">{app.description}</div>
                </motion.div>
              )}
            </motion.div>
          );
        })}
      </div>

      {/* Navigation Buttons */}
      <button
        onClick={handlePrevious}
        disabled={currentIndex === 0}
        className="absolute left-0 top-1/2 -translate-y-1/2 w-12 h-12 rounded-full backdrop-blur-md bg-white/10 border border-white/20 flex items-center justify-center text-white hover:bg-white/20 disabled:opacity-30 disabled:cursor-not-allowed transition-all"
      >
        <ChevronLeft className="w-6 h-6" />
      </button>
      <button
        onClick={handleNext}
        disabled={currentIndex >= apps.length - itemsPerView}
        className="absolute right-0 top-1/2 -translate-y-1/2 w-12 h-12 rounded-full backdrop-blur-md bg-white/10 border border-white/20 flex items-center justify-center text-white hover:bg-white/20 disabled:opacity-30 disabled:cursor-not-allowed transition-all"
      >
        <ChevronRight className="w-6 h-6" />
      </button>

      {/* Dots Indicator */}
      <div className="flex justify-center gap-2 mt-8">
        {Array.from({ length: Math.ceil(apps.length / itemsPerView) }).map((_, i) => (
          <button
            key={i}
            onClick={() => setCurrentIndex(i * itemsPerView)}
            className={`h-1 rounded-full transition-all ${
              Math.floor(currentIndex / itemsPerView) === i
                ? 'w-8 bg-cyan-400'
                : 'w-2 bg-white/30'
            }`}
          />
        ))}
      </div>
    </div>
  );
}
