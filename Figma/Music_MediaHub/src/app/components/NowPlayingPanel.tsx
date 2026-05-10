import { motion } from "motion/react";
import { Play, Pause, SkipForward, SkipBack, Shuffle, Repeat, Volume2, Music2 } from "lucide-react";
import { useRef, useEffect } from "react";
import { ImageWithFallback } from "./figma/ImageWithFallback";
import { Slider } from "./ui/slider";

interface NowPlayingPanelProps {
  artwork: string;
  title: string;
  artist: string;
  album: string;
  isPlaying: boolean;
  progress: number;
  volume: number;
  currentTimeText: string;
  totalTimeText: string;
  shuffleEnabled: boolean;
  repeatEnabled: boolean;
  spectrumBars: number[];
  onTogglePlayPause: () => void;
  onNext: () => void;
  onPrevious: () => void;
  onToggleShuffle: () => void;
  onToggleRepeat: () => void;
  onVolumeChange: (value: number) => void;
  onSeekChange: (value: number) => void;
  onLyricsClick?: () => void;
}

export function NowPlayingPanel({
  artwork,
  title,
  artist,
  album,
  isPlaying,
  progress,
  volume,
  currentTimeText,
  totalTimeText,
  shuffleEnabled,
  repeatEnabled,
  spectrumBars,
  onTogglePlayPause,
  onNext,
  onPrevious,
  onToggleShuffle,
  onToggleRepeat,
  onVolumeChange,
  onSeekChange,
  onLyricsClick,
}: NowPlayingPanelProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  
  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    
    const ctx = canvas.getContext("2d");
    if (!ctx) return;
    
    const width = canvas.width;
    const height = canvas.height;
    const bars = spectrumBars.length > 0 ? spectrumBars : Array.from({ length: 64 }, () => 0);
    ctx.clearRect(0, 0, width, height);
    ctx.fillStyle = "rgba(0, 0, 0, 0.24)";
    ctx.fillRect(0, 0, width, height);

    const barWidth = width / bars.length;
    bars.forEach((value, index) => {
      const amplitude = Math.max(0.05, Math.min(1, value));
      const barHeight = isPlaying ? amplitude * height * 0.92 : height * 0.1;
      const x = index * barWidth;
      const y = height - barHeight;
      const gradient = ctx.createLinearGradient(x, y, x, height);
      gradient.addColorStop(0, "#06b6d4");
      gradient.addColorStop(0.5, "#a855f7");
      gradient.addColorStop(1, "#f97316");
      ctx.fillStyle = gradient;
      ctx.fillRect(x, y, Math.max(1, barWidth - 2), barHeight);
    });
  }, [isPlaying, spectrumBars]);
  
  return (
    <div className="fixed bottom-0 left-0 right-0 h-24 bg-black/80 backdrop-blur-2xl border-t border-white/10 z-50">
      <div className="h-full flex items-center gap-6 px-6">
        {/* Album art and info */}
        <div className="flex items-center gap-4 w-64">
          <div className="relative w-16 h-16 flex-shrink-0">
            <div className="absolute inset-0 bg-gradient-to-r from-cyan-500/30 to-purple-500/30 rounded-lg blur-lg" />
            <ImageWithFallback
              src={artwork}
              alt={title}
              className="relative w-full h-full object-cover rounded-lg"
            />
          </div>
          
          <div className="flex-1 min-w-0">
            <h4 className="text-white font-medium text-sm truncate">{title}</h4>
            <p className="text-gray-400 text-xs truncate">{artist}</p>
            <p className="text-gray-500 text-[11px] truncate">{album}</p>
          </div>
        </div>
        
        {/* Visualizer */}
        <div className="flex-1 flex flex-col items-center gap-2 max-w-2xl">
          <div className="w-full h-12">
            <canvas 
              ref={canvasRef} 
              width={800} 
              height={48} 
              className="w-full h-full rounded-lg"
            />
          </div>
          
          {/* Progress bar */}
          <div className="w-full flex items-center gap-3">
            <span className="text-xs text-gray-400 w-10 text-right">{currentTimeText}</span>
            <Slider
              value={[progress]}
              onValueChange={(value) => onSeekChange(value[0])}
              max={100}
              step={1}
              className="flex-1"
            />
            <span className="text-xs text-gray-400 w-10">{totalTimeText}</span>
          </div>
        </div>
        
        {/* Controls */}
        <div className="flex items-center gap-3">
          <motion.button
            className={`w-8 h-8 rounded-full flex items-center justify-center transition-colors ${
              shuffleEnabled ? "bg-cyan-500 text-black" : "text-gray-400 hover:text-white"
            }`}
            whileTap={{ scale: 0.9 }}
            onClick={onToggleShuffle}
          >
            <Shuffle className="w-4 h-4" />
          </motion.button>
          
          <motion.button
            className="text-gray-400 hover:text-white transition-colors"
            whileTap={{ scale: 0.9 }}
            onClick={onPrevious}
          >
            <SkipBack className="w-5 h-5" />
          </motion.button>
          
          <motion.button
            className="w-12 h-12 rounded-full bg-gradient-to-r from-cyan-500 to-purple-500 flex items-center justify-center shadow-lg shadow-cyan-500/30"
            whileHover={{ scale: 1.1 }}
            whileTap={{ scale: 0.95 }}
            onClick={onTogglePlayPause}
          >
            {isPlaying ? (
              <Pause className="w-5 h-5 text-black fill-black" />
            ) : (
              <Play className="w-5 h-5 text-black fill-black ml-1" />
            )}
          </motion.button>
          
          <motion.button
            className="text-gray-400 hover:text-white transition-colors"
            whileTap={{ scale: 0.9 }}
            onClick={onNext}
          >
            <SkipForward className="w-5 h-5" />
          </motion.button>
          
          <motion.button
            className={`w-8 h-8 rounded-full flex items-center justify-center transition-colors ${
              repeatEnabled ? "bg-cyan-500 text-black" : "text-gray-400 hover:text-white"
            }`}
            whileTap={{ scale: 0.9 }}
            onClick={onToggleRepeat}
          >
            <Repeat className="w-4 h-4" />
          </motion.button>
        </div>
        
        {/* Volume */}
        <div className="flex items-center gap-3 w-32">
          <Volume2 className="w-4 h-4 text-gray-400" />
          <Slider
            value={[volume]}
            onValueChange={(value) => onVolumeChange(value[0])}
            max={100}
            step={1}
          />
        </div>
        
        {/* Lyrics */}
        {onLyricsClick && (
          <motion.button
            className="text-gray-400 hover:text-white transition-colors"
            whileTap={{ scale: 0.9 }}
            onClick={onLyricsClick}
          >
            <Music2 className="w-5 h-5" />
          </motion.button>
        )}
      </div>
    </div>
  );
}