import { useState, useEffect } from "react";
import { motion, AnimatePresence } from "motion/react";
import {
  Play,
  Pause,
  SkipForward,
  SkipBack,
  Volume2,
  Music2,
  Film,
  Radio,
  Tv,
  Heart,
  ListMusic,
  Shuffle,
  Repeat,
  Maximize2,
  TrendingUp,
  Clock,
  Activity,
  Disc3,
  Waves,
  Sparkles,
  Image,
  Gamepad2,
  Mic2,
  X,
  Folder,
} from "lucide-react";

interface MediaItem {
  id: string;
  title: string;
  artist: string;
  album: string;
  duration: string;
  cover: string;
  type: "music" | "video" | "podcast";
}

export function MediaCentre() {
  const [isPlaying, setIsPlaying] = useState(false);
  const [currentTime, setCurrentTime] = useState(0);
  const [volume, setVolume] = useState(75);
  const [activeCategory, setActiveCategory] = useState<"music" | "video" | "radio" | "tv" | "images" | "games" | "karaoke">("music");
  const [selectedMedia, setSelectedMedia] = useState<MediaItem | null>(null);
  const [shuffle, setShuffle] = useState(false);
  const [repeat, setRepeat] = useState(false);
  const [showLibrary, setShowLibrary] = useState(false);

  const [visualizerBars, setVisualizerBars] = useState<number[]>([]);

  const mediaLibrary: MediaItem[] = [
    {
      id: "1",
      title: "Neon Dreams",
      artist: "Synthwave Collective",
      album: "Cyberpunk Nights",
      duration: "3:45",
      cover: "#1e3a5f",
      type: "music",
    },
    {
      id: "2",
      title: "Digital Horizons",
      artist: "Future Pulse",
      album: "Electric Sky",
      duration: "4:12",
      cover: "#3a1e5f",
      type: "music",
    },
    {
      id: "3",
      title: "Quantum Echo",
      artist: "Neural Network",
      album: "AI Sessions",
      duration: "5:28",
      cover: "#1e5f4a",
      type: "music",
    },
    {
      id: "4",
      title: "Chrome Waves",
      artist: "Circuit Dreams",
      album: "Binary Sunset",
      duration: "3:56",
      cover: "#5f1e3a",
      type: "music",
    },
    {
      id: "5",
      title: "Tech Documentary",
      artist: "Atlas Media",
      album: "Future Series",
      duration: "45:00",
      cover: "#2d4a5f",
      type: "video",
    },
    {
      id: "6",
      title: "AI Podcast #42",
      artist: "Tech Talk",
      album: "Digital Minds",
      duration: "52:15",
      cover: "#4a2d5f",
      type: "podcast",
    },
  ];

  const categories = [
    { id: "music", icon: Music2, label: "Music" },
    { id: "video", icon: Film, label: "Videos" },
    { id: "radio", icon: Radio, label: "Radio" },
    { id: "tv", icon: Tv, label: "TV" },
    { id: "images", icon: Image, label: "Images" },
    { id: "games", icon: Gamepad2, label: "Games" },
    { id: "karaoke", icon: Mic2, label: "Karaoke" },
  ];

  // Generate visualizer bars
  useEffect(() => {
    const interval = setInterval(() => {
      if (isPlaying) {
        setVisualizerBars(Array.from({ length: 64 }, () => Math.random() * 100));
      } else {
        setVisualizerBars(Array.from({ length: 64 }, () => 10));
      }
    }, 100);
    return () => clearInterval(interval);
  }, [isPlaying]);

  // Simulate playback progress
  useEffect(() => {
    let interval: NodeJS.Timeout;
    if (isPlaying && selectedMedia) {
      interval = setInterval(() => {
        setCurrentTime((prev) => {
          const max = 300; // 5 minutes max for demo
          return prev >= max ? 0 : prev + 1;
        });
      }, 1000);
    }
    return () => clearInterval(interval);
  }, [isPlaying, selectedMedia]);

  // Set first item as selected on mount
  useEffect(() => {
    if (!selectedMedia) {
      setSelectedMedia(mediaLibrary[0]);
    }
  }, []);

  const formatTime = (seconds: number) => {
    const mins = Math.floor(seconds / 60);
    const secs = seconds % 60;
    return `${mins}:${secs.toString().padStart(2, "0")}`;
  };

  const filteredMedia = mediaLibrary.filter((item) => {
    if (activeCategory === "music") return item.type === "music";
    if (activeCategory === "video") return item.type === "video";
    return true;
  });

  return (
    <div className="flex-1 flex overflow-hidden bg-gradient-to-br from-[#0a0e12] to-[#0b0f14]">
      {/* Main Content */}
      <div className="flex-1 flex flex-col">
        {/* Header */}
        <div className="h-16 border-b border-cyan-500/10 px-6 flex items-center justify-between bg-[#0a0e12]/80 backdrop-blur-sm">
          <div className="flex items-center gap-4">
            <motion.div
              animate={{ rotate: [0, 360] }}
              transition={{ duration: 8, repeat: Infinity, ease: "linear" }}
            >
              <Sparkles className="w-6 h-6 text-cyan-400" />
            </motion.div>
            <div>
              <h2 className="text-lg font-mono tracking-wider text-cyan-400">
                AI MEDIA CENTRE
              </h2>
              <p className="text-xs text-slate-500 font-mono mt-0.5">
                AI Optimization
              </p>
            </div>
            
            {/* Library Button */}
            <motion.button
              whileHover={{ scale: 1.05 }}
              whileTap={{ scale: 0.95 }}
              onClick={() => setShowLibrary(true)}
              className="ml-4 flex items-center gap-2 px-4 py-2 rounded-lg border bg-purple-500/20 border-purple-500/50 text-purple-400 hover:bg-purple-500/30 shadow-[0_0_15px_rgba(168,85,247,0.3)] transition-all"
            >
              <Folder className="w-4 h-4" />
              <span className="text-xs font-mono uppercase">Library</span>
            </motion.button>
          </div>

          {/* Category Tabs */}
          <div className="flex items-center gap-2">
            {categories.map((cat) => (
              <motion.button
                key={cat.id}
                whileHover={{ scale: 1.05 }}
                whileTap={{ scale: 0.95 }}
                onClick={() => setActiveCategory(cat.id as any)}
                className={`flex items-center gap-2 px-4 py-2 rounded-lg border transition-all ${
                  activeCategory === cat.id
                    ? "bg-cyan-500/20 border-cyan-500/50 text-cyan-400 shadow-[0_0_15px_rgba(34,211,238,0.3)]"
                    : "bg-slate-900/50 border-slate-700/50 text-slate-400 hover:border-cyan-500/30"
                }`}
              >
                <cat.icon className="w-4 h-4" />
                <span className="text-xs font-mono uppercase">{cat.label}</span>
              </motion.button>
            ))}
          </div>
        </div>

        {/* Main Content Area */}
        <div className="flex-1 flex overflow-hidden">
          {/* Left Panel - Now Playing & Visualizer */}
          <div className="w-[500px] flex flex-col border-r border-cyan-500/10 p-6 bg-[#0a0e12]/40">
            {/* Album Art / Video Preview */}
            <div className="relative aspect-square rounded-2xl mb-6 overflow-hidden border border-cyan-500/20 shadow-[0_0_40px_rgba(34,211,238,0.1)]">
              {selectedMedia && (
                <>
                  {/* Background gradient based on cover color */}
                  <div
                    className="absolute inset-0 opacity-60"
                    style={{
                      background: `linear-gradient(135deg, ${selectedMedia.cover} 0%, #0a0e12 100%)`,
                    }}
                  />

                  {/* Animated rings */}
                  <motion.div
                    className="absolute inset-0 flex items-center justify-center"
                    animate={{ rotate: 360 }}
                    transition={{ duration: 20, repeat: Infinity, ease: "linear" }}
                  >
                    {[...Array(3)].map((_, i) => (
                      <motion.div
                        key={i}
                        className="absolute rounded-full border border-cyan-400/20"
                        style={{
                          width: `${70 + i * 30}%`,
                          height: `${70 + i * 30}%`,
                        }}
                        animate={{
                          scale: [1, 1.05, 1],
                          opacity: [0.3, 0.6, 0.3],
                        }}
                        transition={{
                          duration: 3,
                          repeat: Infinity,
                          delay: i * 0.5,
                        }}
                      />
                    ))}
                  </motion.div>

                  {/* Center icon */}
                  <div className="absolute inset-0 flex items-center justify-center">
                    <motion.div
                      animate={
                        isPlaying
                          ? {
                              rotate: 360,
                              scale: [1, 1.1, 1],
                            }
                          : {}
                      }
                      transition={{
                        rotate: { duration: 4, repeat: Infinity, ease: "linear" },
                        scale: { duration: 2, repeat: Infinity },
                      }}
                    >
                      <Disc3 className="w-32 h-32 text-cyan-400/40" />
                    </motion.div>
                  </div>

                  {/* Play/Pause overlay */}
                  <div className="absolute inset-0 flex items-center justify-center">
                    <motion.button
                      whileHover={{ scale: 1.1 }}
                      whileTap={{ scale: 0.9 }}
                      onClick={() => setIsPlaying(!isPlaying)}
                      className={`w-20 h-20 rounded-full border-2 flex items-center justify-center transition-all backdrop-blur-md ${
                        isPlaying
                          ? "bg-cyan-500/30 border-cyan-400 shadow-[0_0_30px_rgba(34,211,238,0.6)]"
                          : "bg-slate-900/50 border-cyan-500/30 hover:border-cyan-400"
                      }`}
                    >
                      {isPlaying ? (
                        <Pause className="w-10 h-10 text-cyan-400" />
                      ) : (
                        <Play className="w-10 h-10 text-cyan-400 ml-1" />
                      )}
                    </motion.button>
                  </div>

                  {/* Corner info */}
                  <div className="absolute top-4 left-4 right-4 flex items-center justify-between">
                    <div className="px-3 py-1 rounded-full bg-slate-900/80 backdrop-blur-sm border border-cyan-500/30">
                      <span className="text-xs font-mono text-cyan-400">NOW PLAYING</span>
                    </div>
                    <motion.button
                      whileHover={{ scale: 1.1 }}
                      whileTap={{ scale: 0.9 }}
                      className="p-2 rounded-full bg-slate-900/80 backdrop-blur-sm border border-orange-500/30 hover:bg-orange-500/20"
                    >
                      <Heart className="w-4 h-4 text-orange-400" />
                    </motion.button>
                  </div>
                </>
              )}
            </div>

            {/* Track Info */}
            {selectedMedia && (
              <div className="mb-6">
                <h3 className="text-xl font-mono font-bold text-slate-200 mb-1">
                  {selectedMedia.title}
                </h3>
                <p className="text-sm text-slate-400 mb-2">{selectedMedia.artist}</p>
                <p className="text-xs text-slate-600 font-mono">{selectedMedia.album}</p>
              </div>
            )}

            {/* Progress Bar */}
            <div className="mb-4">
              <div className="relative h-2 bg-slate-900 rounded-full overflow-hidden mb-2">
                <motion.div
                  className="absolute inset-y-0 left-0 bg-gradient-to-r from-cyan-500 to-cyan-400 shadow-[0_0_10px_rgba(34,211,238,0.5)]"
                  style={{ width: `${(currentTime / 300) * 100}%` }}
                  animate={{ width: `${(currentTime / 300) * 100}%` }}
                />
                <input
                  type="range"
                  min="0"
                  max="300"
                  value={currentTime}
                  onChange={(e) => setCurrentTime(parseInt(e.target.value))}
                  className="absolute inset-0 w-full opacity-0 cursor-pointer"
                />
              </div>
              <div className="flex justify-between text-xs font-mono text-slate-500">
                <span>{formatTime(currentTime)}</span>
                <span>{selectedMedia?.duration || "0:00"}</span>
              </div>
            </div>

            {/* Playback Controls */}
            <div className="flex items-center justify-between mb-6">
              <div className="flex items-center gap-2">
                <motion.button
                  whileHover={{ scale: 1.1 }}
                  whileTap={{ scale: 0.9 }}
                  onClick={() => setShuffle(!shuffle)}
                  className={`p-3 rounded-lg border transition-all ${
                    shuffle
                      ? "bg-cyan-500/20 border-cyan-500/50 text-cyan-400"
                      : "bg-slate-900/50 border-slate-700/50 text-slate-400 hover:border-cyan-500/30"
                  }`}
                >
                  <Shuffle className="w-4 h-4" />
                </motion.button>
                
                <motion.button
                  whileHover={{ scale: 1.1 }}
                  whileTap={{ scale: 0.9 }}
                  className="p-3 rounded-lg bg-slate-900/50 border border-slate-700/50 text-slate-400 hover:border-cyan-500/30"
                >
                  <SkipBack className="w-5 h-5" />
                </motion.button>
              </div>

              <motion.button
                whileHover={{ scale: 1.05 }}
                whileTap={{ scale: 0.95 }}
                onClick={() => setIsPlaying(!isPlaying)}
                className={`p-4 rounded-xl border-2 transition-all ${
                  isPlaying
                    ? "bg-cyan-500/30 border-cyan-500/60 shadow-[0_0_25px_rgba(34,211,238,0.5)]"
                    : "bg-slate-900/50 border-cyan-500/30 hover:bg-cyan-500/10"
                }`}
              >
                {isPlaying ? (
                  <Pause className="w-6 h-6 text-cyan-400" />
                ) : (
                  <Play className="w-6 h-6 text-cyan-400 ml-0.5" />
                )}
              </motion.button>

              <div className="flex items-center gap-2">
                <motion.button
                  whileHover={{ scale: 1.1 }}
                  whileTap={{ scale: 0.9 }}
                  className="p-3 rounded-lg bg-slate-900/50 border border-slate-700/50 text-slate-400 hover:border-cyan-500/30"
                >
                  <SkipForward className="w-5 h-5" />
                </motion.button>
                
                <motion.button
                  whileHover={{ scale: 1.1 }}
                  whileTap={{ scale: 0.9 }}
                  onClick={() => setRepeat(!repeat)}
                  className={`p-3 rounded-lg border transition-all ${
                    repeat
                      ? "bg-cyan-500/20 border-cyan-500/50 text-cyan-400"
                      : "bg-slate-900/50 border-slate-700/50 text-slate-400 hover:border-cyan-500/30"
                  }`}
                >
                  <Repeat className="w-4 h-4" />
                </motion.button>
              </div>
            </div>

            {/* Volume Control */}
            <div className="mb-6">
              <div className="flex items-center justify-between mb-2">
                <div className="flex items-center gap-2">
                  <Volume2 className="w-4 h-4 text-slate-400" />
                  <span className="text-xs font-mono text-slate-500 uppercase">Volume</span>
                </div>
                <span className="text-xs font-mono text-cyan-400">{volume}%</span>
              </div>
              <div className="relative h-1.5 bg-slate-900 rounded-full overflow-hidden">
                <motion.div
                  className="absolute inset-y-0 left-0 bg-gradient-to-r from-cyan-500 to-cyan-400"
                  style={{ width: `${volume}%` }}
                />
                <input
                  type="range"
                  min="0"
                  max="100"
                  value={volume}
                  onChange={(e) => setVolume(parseInt(e.target.value))}
                  className="absolute inset-0 w-full opacity-0 cursor-pointer"
                />
              </div>
            </div>

            {/* Visualizer */}
            <div className="flex-1 flex items-end gap-0.5 px-4 pb-2 rounded-lg bg-slate-900/30 border border-cyan-500/10">
              {visualizerBars.map((height, index) => (
                <motion.div
                  key={index}
                  className="flex-1 bg-gradient-to-t from-cyan-400 to-cyan-300 rounded-t"
                  animate={{ height: `${height}%` }}
                  transition={{ duration: 0.1 }}
                  style={{ minHeight: "4px" }}
                />
              ))}
            </div>
          </div>

          {/* Right Panel - Library & Queue */}
          <div className="flex-1 flex flex-col">
            {/* Stats Bar */}
            <div className="h-20 border-b border-cyan-500/10 px-6 flex items-center gap-8 bg-[#0a0e12]/60">
              <div className="flex items-center gap-3">
                <div className="p-3 rounded-lg bg-cyan-500/10 border border-cyan-500/30">
                  <ListMusic className="w-5 h-5 text-cyan-400" />
                </div>
                <div>
                  <div className="text-xs text-slate-500 font-mono uppercase">Library</div>
                  <div className="text-lg font-mono font-bold text-cyan-400">
                    {mediaLibrary.length} items
                  </div>
                </div>
              </div>

              <div className="flex items-center gap-3">
                <div className="p-3 rounded-lg bg-orange-500/10 border border-orange-500/30">
                  <Clock className="w-5 h-5 text-orange-400" />
                </div>
                <div>
                  <div className="text-xs text-slate-500 font-mono uppercase">Playtime</div>
                  <div className="text-lg font-mono font-bold text-orange-400">24h 32m</div>
                </div>
              </div>

              <div className="flex items-center gap-3">
                <div className="p-3 rounded-lg bg-purple-500/10 border border-purple-500/30">
                  <TrendingUp className="w-5 h-5 text-purple-400" />
                </div>
                <div>
                  <div className="text-xs text-slate-500 font-mono uppercase">Trending</div>
                  <div className="text-lg font-mono font-bold text-purple-400">
                    {isPlaying ? "Playing" : "Ready"}
                  </div>
                </div>
              </div>
            </div>

            {/* Media Library */}
            <div className="flex-1 overflow-y-auto p-6 scrollbar-hide">
              <div className="mb-4 flex items-center justify-between">
                <h3 className="text-sm font-mono text-slate-400 uppercase tracking-wider">
                  Media Library
                </h3>
                <div className="flex items-center gap-2">
                  <Activity className="w-4 h-4 text-cyan-400" />
                  <span className="text-xs font-mono text-cyan-400">
                    {filteredMedia.length} tracks
                  </span>
                </div>
              </div>

              <div className="space-y-2">
                {filteredMedia.map((item, index) => (
                  <motion.div
                    key={item.id}
                    initial={{ opacity: 0, x: 20 }}
                    animate={{ opacity: 1, x: 0 }}
                    transition={{ delay: index * 0.05 }}
                    onClick={() => setSelectedMedia(item)}
                    className={`p-4 rounded-lg border cursor-pointer transition-all ${
                      selectedMedia?.id === item.id
                        ? "bg-cyan-500/10 border-cyan-500/40 shadow-[0_0_15px_rgba(34,211,238,0.2)]"
                        : "bg-slate-900/30 border-slate-800 hover:border-cyan-500/30 hover:bg-slate-900/50"
                    }`}
                  >
                    <div className="flex items-center gap-4">
                      {/* Cover */}
                      <div
                        className="w-12 h-12 rounded-lg flex items-center justify-center border border-cyan-500/20"
                        style={{ backgroundColor: item.cover }}
                      >
                        <Music2 className="w-6 h-6 text-white/40" />
                      </div>

                      {/* Info */}
                      <div className="flex-1 min-w-0">
                        <div className="flex items-center gap-2 mb-1">
                          <h4
                            className={`text-sm font-mono font-bold truncate ${
                              selectedMedia?.id === item.id ? "text-cyan-400" : "text-slate-200"
                            }`}
                          >
                            {item.title}
                          </h4>
                          {selectedMedia?.id === item.id && isPlaying && (
                            <motion.div
                              animate={{ scale: [1, 1.2, 1] }}
                              transition={{ duration: 1, repeat: Infinity }}
                            >
                              <Waves className="w-3 h-3 text-cyan-400" />
                            </motion.div>
                          )}
                        </div>
                        <p className="text-xs text-slate-500 truncate">{item.artist}</p>
                        <div className="flex items-center gap-3 mt-1">
                          <span className="text-[10px] text-slate-600 font-mono">
                            {item.album}
                          </span>
                          <span className="text-[10px] text-slate-700">•</span>
                          <span className="text-[10px] text-cyan-400 font-mono">
                            {item.duration}
                          </span>
                        </div>
                      </div>

                      {/* Quick actions */}
                      <div className="flex items-center gap-2">
                        <motion.button
                          whileHover={{ scale: 1.1 }}
                          whileTap={{ scale: 0.9 }}
                          className="p-2 rounded-lg bg-slate-800/50 border border-slate-700/50 hover:border-cyan-500/50 text-slate-400 hover:text-cyan-400 transition-all"
                        >
                          <Play className="w-4 h-4" />
                        </motion.button>
                        <motion.button
                          whileHover={{ scale: 1.1 }}
                          whileTap={{ scale: 0.9 }}
                          className="p-2 rounded-lg bg-slate-800/50 border border-slate-700/50 hover:border-orange-500/50 text-slate-400 hover:text-orange-400 transition-all"
                        >
                          <Heart className="w-4 h-4" />
                        </motion.button>
                      </div>
                    </div>
                  </motion.div>
                ))}
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* Library Modal */}
      <AnimatePresence>
        {showLibrary && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="absolute inset-0 bg-black/80 backdrop-blur-md flex items-center justify-center z-50"
            onClick={() => setShowLibrary(false)}
          >
            <motion.div
              initial={{ scale: 0.9, opacity: 0 }}
              animate={{ scale: 1, opacity: 1 }}
              exit={{ scale: 0.9, opacity: 0 }}
              onClick={(e) => e.stopPropagation()}
              className="w-[90%] h-[85%] bg-gradient-to-br from-[#0a0e12] to-[#0b0f14] border border-purple-500/30 rounded-2xl shadow-[0_0_60px_rgba(168,85,247,0.3)] overflow-hidden"
            >
              {/* Modal Header */}
              <div className="h-16 border-b border-purple-500/20 px-6 flex items-center justify-between bg-[#0a0e12]/80">
                <div className="flex items-center gap-3">
                  <Folder className="w-6 h-6 text-purple-400" />
                  <div>
                    <h3 className="text-lg font-mono font-bold text-purple-400 tracking-wider">
                      MEDIA LIBRARY
                    </h3>
                    <p className="text-xs text-slate-500 font-mono">Complete Collection</p>
                  </div>
                </div>
                <motion.button
                  whileHover={{ scale: 1.1 }}
                  whileTap={{ scale: 0.9 }}
                  onClick={() => setShowLibrary(false)}
                  className="p-2 rounded-lg bg-slate-900/50 border border-slate-700/50 hover:border-purple-500/50 text-slate-400 hover:text-purple-400 transition-all"
                >
                  <X className="w-5 h-5" />
                </motion.button>
              </div>

              {/* Modal Content - Grid of Categories */}
              <div className="p-8 h-[calc(100%-4rem)] overflow-y-auto">
                <div className="grid grid-cols-4 gap-6">
                  {categories.map((cat, index) => (
                    <motion.div
                      key={cat.id}
                      initial={{ opacity: 0, y: 20 }}
                      animate={{ opacity: 1, y: 0 }}
                      transition={{ delay: index * 0.1 }}
                      whileHover={{ scale: 1.05, y: -8 }}
                      onClick={() => {
                        setActiveCategory(cat.id as any);
                        setShowLibrary(false);
                      }}
                      className="relative group cursor-pointer"
                    >
                      {/* Card */}
                      <div className="aspect-square rounded-2xl bg-gradient-to-br from-slate-900 to-slate-950 border border-cyan-500/20 hover:border-cyan-500/50 transition-all overflow-hidden shadow-lg hover:shadow-[0_0_30px_rgba(34,211,238,0.4)]">
                        {/* Animated background gradient */}
                        <div className="absolute inset-0 bg-gradient-to-br from-cyan-500/10 via-purple-500/10 to-orange-500/10 opacity-0 group-hover:opacity-100 transition-opacity" />
                        
                        {/* Icon */}
                        <div className="absolute inset-0 flex items-center justify-center">
                          <motion.div
                            animate={{ rotate: [0, 360] }}
                            transition={{ duration: 20, repeat: Infinity, ease: "linear" }}
                            className="absolute inset-8 rounded-full border border-cyan-500/10"
                          />
                          <cat.icon className="w-24 h-24 text-cyan-400/40 group-hover:text-cyan-400 transition-all" />
                        </div>

                        {/* Badge with count */}
                        <div className="absolute top-4 right-4 px-3 py-1 rounded-full bg-slate-900/80 backdrop-blur-sm border border-cyan-500/30">
                          <span className="text-xs font-mono text-cyan-400">
                            {cat.id === "music" ? "4" : cat.id === "video" ? "1" : cat.id === "radio" ? "12" : cat.id === "tv" ? "8" : cat.id === "images" ? "156" : cat.id === "games" ? "7" : "3"}
                          </span>
                        </div>
                      </div>

                      {/* Label */}
                      <div className="mt-4 text-center">
                        <h4 className="text-lg font-mono font-bold text-slate-200 group-hover:text-cyan-400 transition-colors">
                          {cat.label}
                        </h4>
                        <p className="text-xs text-slate-600 font-mono mt-1">
                          {cat.id === "music"
                            ? "Tracks & Albums"
                            : cat.id === "video"
                            ? "Movies & Shows"
                            : cat.id === "radio"
                            ? "Live Streams"
                            : cat.id === "tv"
                            ? "Channels"
                            : cat.id === "images"
                            ? "Photos & Art"
                            : cat.id === "games"
                            ? "Interactive"
                            : "Sing Along"}
                        </p>
                      </div>
                    </motion.div>
                  ))}
                </div>

                {/* Additional Stats Section */}
                <div className="mt-12 grid grid-cols-3 gap-6">
                  <motion.div
                    initial={{ opacity: 0, y: 20 }}
                    animate={{ opacity: 1, y: 0 }}
                    transition={{ delay: 0.8 }}
                    className="p-6 rounded-xl bg-gradient-to-br from-cyan-500/10 to-cyan-500/5 border border-cyan-500/20"
                  >
                    <div className="flex items-center gap-4">
                      <div className="p-4 rounded-lg bg-cyan-500/20 border border-cyan-500/30">
                        <Music2 className="w-8 h-8 text-cyan-400" />
                      </div>
                      <div>
                        <div className="text-2xl font-mono font-bold text-cyan-400">1,247</div>
                        <div className="text-xs text-slate-500 font-mono uppercase">Total Tracks</div>
                      </div>
                    </div>
                  </motion.div>

                  <motion.div
                    initial={{ opacity: 0, y: 20 }}
                    animate={{ opacity: 1, y: 0 }}
                    transition={{ delay: 0.9 }}
                    className="p-6 rounded-xl bg-gradient-to-br from-orange-500/10 to-orange-500/5 border border-orange-500/20"
                  >
                    <div className="flex items-center gap-4">
                      <div className="p-4 rounded-lg bg-orange-500/20 border border-orange-500/30">
                        <Clock className="w-8 h-8 text-orange-400" />
                      </div>
                      <div>
                        <div className="text-2xl font-mono font-bold text-orange-400">892h</div>
                        <div className="text-xs text-slate-500 font-mono uppercase">Total Duration</div>
                      </div>
                    </div>
                  </motion.div>

                  <motion.div
                    initial={{ opacity: 0, y: 20 }}
                    animate={{ opacity: 1, y: 0 }}
                    transition={{ delay: 1.0 }}
                    className="p-6 rounded-xl bg-gradient-to-br from-purple-500/10 to-purple-500/5 border border-purple-500/20"
                  >
                    <div className="flex items-center gap-4">
                      <div className="p-4 rounded-lg bg-purple-500/20 border border-purple-500/30">
                        <Heart className="w-8 h-8 text-purple-400" />
                      </div>
                      <div>
                        <div className="text-2xl font-mono font-bold text-purple-400">342</div>
                        <div className="text-xs text-slate-500 font-mono uppercase">Favorites</div>
                      </div>
                    </div>
                  </motion.div>
                </div>
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}