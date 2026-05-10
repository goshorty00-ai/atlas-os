import { useState, useEffect, useRef } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import {
  Play,
  Pause,
  SkipForward,
  SkipBack,
  Volume2,
  VolumeX,
  Maximize2,
  Settings,
  Subtitles,
  X,
  Sun,
  Sparkles,
  ChevronRight,
} from 'lucide-react';

interface FullscreenPlayerProps {
  onClose: () => void;
}

export function FullscreenPlayer({ onClose }: FullscreenPlayerProps) {
  const [isPlaying, setIsPlaying] = useState(true);
  const [showControls, setShowControls] = useState(true);
  const [currentTime, setCurrentTime] = useState(0);
  const [duration] = useState(7245); // 2:00:45 in seconds
  const [volume, setVolume] = useState(80);
  const [isMuted, setIsMuted] = useState(false);
  const [showSkipIntro, setShowSkipIntro] = useState(true);
  const [showChapterMarkers, setShowChapterMarkers] = useState(true);
  const hideControlsTimeout = useRef<NodeJS.Timeout>();

  const chapters = [
    { time: 0, label: 'Opening Scene' },
    { time: 420, label: 'Act 1' },
    { time: 1800, label: 'Plot Twist' },
    { time: 3600, label: 'Act 2' },
    { time: 5400, label: 'Climax' },
    { time: 6600, label: 'Resolution' },
  ];

  const formatTime = (seconds: number) => {
    const h = Math.floor(seconds / 3600);
    const m = Math.floor((seconds % 3600) / 60);
    const s = Math.floor(seconds % 60);
    return h > 0
      ? `${h}:${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`
      : `${m}:${s.toString().padStart(2, '0')}`;
  };

  useEffect(() => {
    const interval = setInterval(() => {
      if (isPlaying) {
        setCurrentTime((prev) => Math.min(prev + 1, duration));
      }
    }, 1000);

    return () => clearInterval(interval);
  }, [isPlaying, duration]);

  useEffect(() => {
    if (currentTime > 60) {
      setShowSkipIntro(false);
    }
  }, [currentTime]);

  const handleMouseMove = () => {
    setShowControls(true);
    if (hideControlsTimeout.current) {
      clearTimeout(hideControlsTimeout.current);
    }
    hideControlsTimeout.current = setTimeout(() => {
      if (isPlaying) {
        setShowControls(false);
      }
    }, 3000);
  };

  return (
    <div
      className="fixed inset-0 bg-black z-50 flex items-center justify-center"
      onMouseMove={handleMouseMove}
    >
      {/* Video Content - Mock with Image */}
      <div className="absolute inset-0">
        <img
          src="https://images.unsplash.com/photo-1770150511119-ec6b93d26de9?w=1920"
          alt="Video Content"
          className="w-full h-full object-cover"
        />
        {/* Adaptive Lighting Effect */}
        <div className="absolute inset-0 bg-gradient-to-b from-purple-900/20 via-transparent to-blue-900/20" />
      </div>

      {/* Close Button */}
      <AnimatePresence>
        {showControls && (
          <motion.button
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            onClick={onClose}
            className="absolute top-6 right-6 w-12 h-12 rounded-full backdrop-blur-md bg-black/40 border border-white/20 flex items-center justify-center text-white hover:bg-black/60 transition-all z-50"
          >
            <X className="w-6 h-6" />
          </motion.button>
        )}
      </AnimatePresence>

      {/* AI Skip Intro */}
      <AnimatePresence>
        {showSkipIntro && (
          <motion.button
            initial={{ opacity: 0, x: 20 }}
            animate={{ opacity: 1, x: 0 }}
            exit={{ opacity: 0, x: 20 }}
            onClick={() => setShowSkipIntro(false)}
            className="absolute top-6 right-24 px-6 py-3 rounded-xl backdrop-blur-md bg-cyan-500/20 border border-cyan-500/50 text-cyan-400 font-medium hover:bg-cyan-500/30 transition-all flex items-center gap-2"
          >
            <Sparkles className="w-4 h-4" />
            Skip Intro
            <ChevronRight className="w-4 h-4" />
          </motion.button>
        )}
      </AnimatePresence>

      {/* Controls Overlay */}
      <AnimatePresence>
        {showControls && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="absolute inset-0 bg-gradient-to-t from-black/80 via-transparent to-black/40"
          >
            <div className="absolute bottom-0 left-0 right-0 p-8">
              {/* Progress Bar with Chapter Markers */}
              <div className="mb-6 relative">
                <div className="relative h-2 bg-white/20 rounded-full overflow-hidden">
                  {/* Chapter Markers */}
                  {showChapterMarkers &&
                    chapters.map((chapter) => (
                      <div
                        key={chapter.time}
                        className="absolute top-0 bottom-0 w-0.5 bg-white/40"
                        style={{ left: `${(chapter.time / duration) * 100}%` }}
                      />
                    ))}

                  {/* Progress */}
                  <motion.div
                    className="absolute top-0 left-0 bottom-0 bg-gradient-to-r from-cyan-500 to-purple-500"
                    style={{ width: `${(currentTime / duration) * 100}%` }}
                  />

                  {/* Scrubber */}
                  <input
                    type="range"
                    min="0"
                    max={duration}
                    value={currentTime}
                    onChange={(e) => setCurrentTime(Number(e.target.value))}
                    className="absolute inset-0 w-full opacity-0 cursor-pointer"
                  />
                </div>

                {/* Time Display */}
                <div className="flex justify-between text-xs text-gray-300 mt-2">
                  <span>{formatTime(currentTime)}</span>
                  <span>{formatTime(duration)}</span>
                </div>
              </div>

              {/* Control Buttons */}
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-4">
                  {/* Play/Pause */}
                  <button
                    onClick={() => setIsPlaying(!isPlaying)}
                    className="w-14 h-14 rounded-full backdrop-blur-md bg-white/20 border border-white/30 flex items-center justify-center text-white hover:bg-white/30 transition-all"
                  >
                    {isPlaying ? <Pause className="w-6 h-6" /> : <Play className="w-6 h-6" />}
                  </button>

                  {/* Skip Backward */}
                  <button
                    onClick={() => setCurrentTime(Math.max(0, currentTime - 10))}
                    className="w-10 h-10 rounded-full backdrop-blur-md bg-white/10 border border-white/20 flex items-center justify-center text-white hover:bg-white/20 transition-all"
                  >
                    <SkipBack className="w-5 h-5" />
                  </button>

                  {/* Skip Forward */}
                  <button
                    onClick={() => setCurrentTime(Math.min(duration, currentTime + 10))}
                    className="w-10 h-10 rounded-full backdrop-blur-md bg-white/10 border border-white/20 flex items-center justify-center text-white hover:bg-white/20 transition-all"
                  >
                    <SkipForward className="w-5 h-5" />
                  </button>

                  {/* Volume */}
                  <div className="flex items-center gap-2">
                    <button
                      onClick={() => setIsMuted(!isMuted)}
                      className="w-10 h-10 rounded-full backdrop-blur-md bg-white/10 border border-white/20 flex items-center justify-center text-white hover:bg-white/20 transition-all"
                    >
                      {isMuted || volume === 0 ? (
                        <VolumeX className="w-5 h-5" />
                      ) : (
                        <Volume2 className="w-5 h-5" />
                      )}
                    </button>
                    <input
                      type="range"
                      min="0"
                      max="100"
                      value={isMuted ? 0 : volume}
                      onChange={(e) => {
                        setVolume(Number(e.target.value));
                        setIsMuted(false);
                      }}
                      className="w-24"
                    />
                    <span className="text-sm text-white w-8">{isMuted ? 0 : volume}%</span>
                  </div>
                </div>

                <div className="flex items-center gap-3">
                  {/* AI Subtitle Translation */}
                  <button className="px-4 py-2 rounded-lg backdrop-blur-md bg-white/10 border border-white/20 text-white hover:bg-white/20 transition-all flex items-center gap-2">
                    <Sparkles className="w-4 h-4 text-purple-400" />
                    <Subtitles className="w-5 h-5" />
                  </button>

                  {/* Night Mode */}
                  <button className="px-4 py-2 rounded-lg backdrop-blur-md bg-white/10 border border-white/20 text-white hover:bg-white/20 transition-all flex items-center gap-2">
                    <Sun className="w-5 h-5" />
                  </button>

                  {/* Settings */}
                  <button className="w-10 h-10 rounded-full backdrop-blur-md bg-white/10 border border-white/20 flex items-center justify-center text-white hover:bg-white/20 transition-all">
                    <Settings className="w-5 h-5" />
                  </button>

                  {/* Fullscreen */}
                  <button className="w-10 h-10 rounded-full backdrop-blur-md bg-white/10 border border-white/20 flex items-center justify-center text-white hover:bg-white/20 transition-all">
                    <Maximize2 className="w-5 h-5" />
                  </button>
                </div>
              </div>

              {/* AI Chapter Info */}
              {showChapterMarkers && (
                <div className="mt-4 backdrop-blur-md bg-white/5 border border-white/10 rounded-lg px-4 py-2">
                  <div className="text-sm text-cyan-400 flex items-center gap-2">
                    <Sparkles className="w-4 h-4" />
                    Current Chapter:{' '}
                    {chapters.reduce((acc, chapter) => {
                      if (chapter.time <= currentTime) return chapter;
                      return acc;
                    }).label}
                  </div>
                </div>
              )}
            </div>
          </motion.div>
        )}
      </AnimatePresence>

      {/* Center Play/Pause Toggle */}
      {!showControls && (
        <button
          onClick={() => setIsPlaying(!isPlaying)}
          className="absolute inset-0 w-full h-full cursor-pointer"
        />
      )}
    </div>
  );
}
