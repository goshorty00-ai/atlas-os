import { useState, useEffect } from "react";
import { motion } from "motion/react";
import {
  Play,
  Pause,
  Volume2,
  Disc3,
  Music,
  Activity,
  Folder,
  Search,
  Zap,
  Sparkles,
} from "lucide-react";

interface Track {
  id: string;
  name: string;
  artist: string;
  album: string;
  bpm: number;
  duration: string;
  time: string;
  genre: string;
  key: string;
  energy: number;
}

interface DeckState {
  isPlaying: boolean;
  track: Track | null;
  volume: number;
  gain: number;
  tempo: number;
  currentTime: number;
  eq: { hi: number; mid: number; low: number };
}

const musicServices = [
  { id: "spotify", name: "Spotify", icon: "♫" },
  { id: "soundcloud", name: "SoundCloud", icon: "☁" },
  { id: "apple", name: "Apple", icon: "" },
  { id: "youtube", name: "YouTube", icon: "▶" },
  { id: "tidal", name: "Tidal", icon: "〰" },
];

const library: Track[] = [
  { id: "1", name: "Love on my Mind", artist: "Freemasons ft. Amanda Wilson", album: "Ibiza No1", bpm: 129.0, duration: "3:14", time: "03:14", genre: "House", key: "Am", energy: 75 },
  { id: "2", name: "Bob The Radio", artist: "Spencer & Hill Project", album: "Ibiza No1", bpm: 130.0, duration: "5:34", time: "05:34", genre: "House", key: "Gm", energy: 82 },
  { id: "3", name: "Mind Ur Step", artist: "Dennis Ferrer ft. Janelle Kroll", album: "Ibiza No1", bpm: 124.0, duration: "7:44", time: "07:44", genre: "House", key: "Dm", energy: 68 },
  { id: "4", name: "Waves", artist: "Mr Probz - Robin Schulz Remix", album: "Ibiza No1", bpm: 120.0, duration: "3:52", time: "03:52", genre: "Deep House", key: "Em", energy: 55 },
  { id: "5", name: "Galvanize", artist: "The Chemical Brothers", album: "Ibiza No1", bpm: 130.0, duration: "6:35", time: "06:35", genre: "Electronic", key: "Fm", energy: 88 },
  { id: "6", name: "Discopolis", artist: "Kris Menace & Lifelike", album: "Ibiza No1", bpm: 120.0, duration: "3:21", time: "03:21", genre: "House", key: "Cm", energy: 72 },
  { id: "7", name: "Around The World", artist: "Daft Punk", album: "Homework", bpm: 121.0, duration: "7:09", time: "07:09", genre: "House", key: "Em", energy: 78 },
  { id: "8", name: "One More Time", artist: "Daft Punk", album: "Discovery", bpm: 122.0, duration: "5:20", time: "05:20", genre: "House", key: "Bm", energy: 85 },
];

export function DJBooth() {
  const [deckA, setDeckA] = useState<DeckState>({
    isPlaying: true,
    track: library[0],
    volume: 75,
    gain: 85,
    tempo: -0.3,
    currentTime: 45,
    eq: { hi: 100, mid: 100, low: 100 },
  });

  const [deckB, setDeckB] = useState<DeckState>({
    isPlaying: false,
    track: library[1],
    volume: 70,
    gain: 88,
    tempo: 0.0,
    currentTime: 65,
    eq: { hi: 100, mid: 100, low: 100 },
  });

  const [crossfader, setCrossfader] = useState(50);
  const [masterVolume, setMasterVolume] = useState(80);
  const [selectedService, setSelectedService] = useState("spotify");
  const [selectedTrack, setSelectedTrack] = useState<string | null>("2");
  const [searchQuery, setSearchQuery] = useState("");

  const bpmCompatibility = deckA.track && deckB.track 
    ? Math.max(0, 100 - Math.abs(deckA.track.bpm - deckB.track.bpm) * 2)
    : 0;

  const [waveformA, setWaveformA] = useState<number[]>([]);
  const [waveformB, setWaveformB] = useState<number[]>([]);

  useEffect(() => {
    const generateWaveform = (intensity: number = 1) => {
      return Array.from({ length: 20 }, (_, i) => {
        const baseHeight = 30 + Math.sin(i * 0.15) * 20;
        const variation = Math.random() * 50 * intensity;
        return baseHeight + variation;
      });
    };
    setWaveformA(generateWaveform(0.8));
    setWaveformB(generateWaveform(1.2));
  }, []);

  useEffect(() => {
    const interval = setInterval(() => {
      if (deckA.isPlaying) {
        setDeckA((prev) => ({
          ...prev,
          currentTime: prev.currentTime >= 100 ? 0 : prev.currentTime + 0.1,
        }));
      }
      if (deckB.isPlaying) {
        setDeckB((prev) => ({
          ...prev,
          currentTime: prev.currentTime >= 100 ? 0 : prev.currentTime + 0.1,
        }));
      }
    }, 100);
    return () => clearInterval(interval);
  }, [deckA.isPlaying, deckB.isPlaying]);

  const togglePlayDeck = (deck: "A" | "B") => {
    if (deck === "A") {
      setDeckA({ ...deckA, isPlaying: !deckA.isPlaying });
    } else {
      setDeckB({ ...deckB, isPlaying: !deckB.isPlaying });
    }
  };

  const loadTrackToDeck = (track: Track, deck: "A" | "B") => {
    if (deck === "A") {
      setDeckA({ ...deckA, track, currentTime: 0, isPlaying: false });
    } else {
      setDeckB({ ...deckB, track, currentTime: 0, isPlaying: false });
    }
  };

  const filteredLibrary = library.filter(
    (track) =>
      track.name.toLowerCase().includes(searchQuery.toLowerCase()) ||
      track.artist.toLowerCase().includes(searchQuery.toLowerCase())
  );

  return (
    <div className="w-full h-screen bg-gradient-to-b from-[#07121C] to-[#0C1B2A] flex flex-col overflow-hidden">
      {/* TOP BAR - 5.5% of screen height */}
      <div className="h-[5.5vh] min-h-[60px] flex items-center justify-between px-[3vw] border-b border-slate-800/20 shrink-0">
        <div className="flex items-center gap-[1.5vw] flex-1">
          <div className="text-[0.6vw] min-text-[10px] font-mono text-cyan-400/70 tracking-wider font-semibold">DECK A</div>
          <div className="flex items-end h-[2.8vh] gap-[0.1vw]">
            {waveformA.map((height, index) => {
              const isPast = (index / 20) * 100 < deckA.currentTime;
              return (
                <div
                  key={index}
                  className={`w-[0.5vw] min-w-[4px] rounded-sm transition-all ${
                    isPast ? "bg-cyan-400/80" : "bg-slate-700/30"
                  }`}
                  style={{ height: `${height * 0.7}%` }}
                />
              );
            })}
          </div>
          <div className="text-[0.65vw] min-text-[11px] font-mono text-slate-400 max-w-[12vw] truncate">
            {deckA.track?.name || "No Track"}
          </div>
        </div>

        <motion.button
          whileHover={{ scale: 1.02 }}
          whileTap={{ scale: 0.98 }}
          className={`w-[2.5vw] h-[2.5vw] min-w-[48px] min-h-[48px] rounded-full border flex items-center justify-center transition-all ${
            deckA.isPlaying && deckB.isPlaying
              ? "bg-gradient-to-br from-cyan-500/10 to-orange-500/10 border-purple-500/40 shadow-[0_0_15px_rgba(168,85,247,0.2)]"
              : "bg-slate-900/30 border-slate-700/40"
          }`}
        >
          <Sparkles className={`w-[1.2vw] h-[1.2vw] min-w-[20px] min-h-[20px] ${deckA.isPlaying && deckB.isPlaying ? "text-purple-400" : "text-slate-500"}`} />
        </motion.button>

        <div className="flex items-center gap-[1.5vw] flex-1 justify-end">
          <div className="text-[0.65vw] min-text-[11px] font-mono text-slate-400 max-w-[12vw] truncate text-right">
            {deckB.track?.name || "No Track"}
          </div>
          <div className="flex items-end h-[2.8vh] gap-[0.1vw]">
            {waveformB.map((height, index) => {
              const isPast = (index / 20) * 100 < deckB.currentTime;
              return (
                <div
                  key={index}
                  className={`w-[0.5vw] min-w-[4px] rounded-sm transition-all ${
                    isPast ? "bg-orange-400/80" : "bg-slate-700/30"
                  }`}
                  style={{ height: `${height * 0.7}%` }}
                />
              );
            })}
          </div>
          <div className="text-[0.6vw] min-text-[10px] font-mono text-orange-400/70 tracking-wider font-semibold">DECK B</div>
        </div>
      </div>

      {/* SOURCE STRIP - 5.5% of screen height */}
      <div className="h-[5.5vh] min-h-[60px] flex items-center justify-center gap-0 shrink-0 border-b border-slate-800/20">
        <Music className="w-[1vw] h-[1vw] min-w-[14px] min-h-[14px] text-slate-600 mr-[1.5vw]" />
        <div className="flex items-center bg-slate-900/20 rounded-lg border border-slate-700/20 overflow-hidden">
          {musicServices.map((service, idx) => (
            <button
              key={service.id}
              onClick={() => setSelectedService(service.id)}
              className={`h-[3.3vh] min-h-[36px] px-[2vw] min-px-[20px] font-mono text-[0.55vw] min-text-[10px] transition-all relative ${
                selectedService === service.id
                  ? "bg-cyan-500/10 text-cyan-400"
                  : "text-slate-500 hover:text-slate-400 hover:bg-slate-800/20"
              } ${idx > 0 ? "border-l border-slate-700/20" : ""}`}
            >
              <span className="mr-[0.8vw] text-[0.7vw] min-text-[12px]">{service.icon}</span>
              {service.name}
              {selectedService === service.id && (
                <div className="absolute bottom-0 left-0 right-0 h-[1px] bg-cyan-400/60" />
              )}
            </button>
          ))}
        </div>
        
        <div className="ml-[3vw] flex items-center gap-[1vw] w-[12vw] min-w-[192px]">
          <Volume2 className="w-[1vw] h-[1vw] min-w-[14px] min-h-[14px] text-slate-600" />
          <div className="flex-1 relative h-[0.7vh] min-h-[6px] bg-slate-900/50 rounded-full overflow-hidden border border-slate-800/30">
            <div
              className="h-full bg-gradient-to-r from-cyan-500/50 to-cyan-400/50 transition-all"
              style={{ width: `${masterVolume}%` }}
            />
            <input
              type="range"
              min="0"
              max="100"
              value={masterVolume}
              onChange={(e) => setMasterVolume(parseInt(e.target.value))}
              className="absolute inset-0 w-full opacity-0 cursor-pointer"
            />
          </div>
          <span className="text-[0.6vw] min-text-[10px] font-mono text-cyan-400/70 w-[2.5vw] text-right">{masterVolume}%</span>
        </div>
      </div>

      {/* PERFORMANCE ROW - 50% of screen height */}
      <div className="h-[50vh] min-h-[540px] flex items-center justify-center gap-[4vw] px-[3vw] shrink-0 border-b border-slate-800/20">
        
        {/* DECK A */}
        <div className="flex flex-col items-center gap-[1.5vh]">
          <motion.div
            animate={{ rotate: deckA.isPlaying ? 360 : 0 }}
            transition={{
              duration: deckA.isPlaying ? 3 : 0,
              repeat: deckA.isPlaying ? Infinity : 0,
              ease: "linear",
            }}
            className="relative"
          >
            <div
              className={`w-[18.75vw] h-[18.75vw] min-w-[300px] min-h-[300px] max-w-[360px] max-h-[360px] rounded-full bg-gradient-to-br from-slate-900/80 via-slate-800/80 to-black/80 relative overflow-hidden transition-all ${
                deckA.isPlaying
                  ? "shadow-[inset_0_0_40px_rgba(34,211,238,0.12),0_0_40px_rgba(34,211,238,0.15)]"
                  : "shadow-[inset_0_0_30px_rgba(0,0,0,0.5)]"
              }`}
              style={{
                border: deckA.isPlaying
                  ? "1.5px solid rgba(34,211,238,0.3)"
                  : "1px solid rgba(71,85,105,0.25)",
              }}
            >
              {[...Array(16)].map((_, i) => (
                <div
                  key={i}
                  className="absolute inset-0 rounded-full border border-slate-700/12"
                  style={{ margin: `${i * 11}px` }}
                />
              ))}

              {deckA.isPlaying && (
                <motion.div
                  animate={{ rotate: -360 }}
                  transition={{ duration: 6, repeat: Infinity, ease: "linear" }}
                  className="absolute inset-[2.3vw] rounded-full border border-cyan-400/15"
                />
              )}

              <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[5.8vw] h-[5.8vw] min-w-[90px] min-h-[90px] max-w-[112px] max-h-[112px] rounded-full bg-gradient-to-br from-slate-900 to-slate-950 flex flex-col items-center justify-center"
                style={{
                  border: deckA.isPlaying
                    ? "1.5px solid rgba(34,211,238,0.25)"
                    : "1px solid rgba(71,85,105,0.25)",
                  boxShadow: deckA.isPlaying
                    ? "0 0 20px rgba(34,211,238,0.15)"
                    : "none",
                }}
              >
                <Disc3 className="w-[1.5vw] h-[1.5vw] min-w-[24px] min-h-[24px] text-cyan-400/50 mb-[0.5vh]" />
                <div
                  className={`text-[2vw] min-text-[32px] font-mono font-bold transition-all ${
                    deckA.isPlaying ? "text-cyan-400" : "text-slate-600"
                  }`}
                  style={{
                    textShadow: deckA.isPlaying
                      ? "0 0 15px rgba(34,211,238,0.3)"
                      : "none",
                  }}
                >
                  {deckA.track ? deckA.track.bpm.toFixed(1) : "---"}
                </div>
                <div className="text-[0.5vw] min-text-[8px] text-slate-600 font-mono tracking-wider mt-[0.3vh]">BPM</div>
              </div>
            </div>
          </motion.div>

          <motion.button
            whileHover={{ scale: 1.04 }}
            whileTap={{ scale: 0.96 }}
            onClick={() => togglePlayDeck("A")}
            className={`w-[3vw] h-[3vw] min-w-[48px] min-h-[48px] max-w-[60px] max-h-[60px] rounded-full border transition-all ${
              deckA.isPlaying
                ? "bg-cyan-500/85 border-cyan-400/50 shadow-[0_0_18px_rgba(34,211,238,0.25)]"
                : "bg-slate-900/30 border-slate-700/40 hover:border-cyan-500/30"
            }`}
          >
            {deckA.isPlaying ? (
              <Pause className="w-[1.5vw] h-[1.5vw] min-w-[24px] min-h-[24px] text-slate-900 mx-auto" />
            ) : (
              <Play className="w-[1.5vw] h-[1.5vw] min-w-[24px] min-h-[24px] text-cyan-400 mx-auto ml-[0.2vw]" />
            )}
          </motion.button>

          <div className="w-full px-[2vw]">
            <div className="flex items-center justify-between mb-[0.5vh]">
              <span className="text-[0.5vw] min-text-[9px] font-mono text-slate-600">TEMPO</span>
              <span className="text-[0.6vw] min-text-[10px] font-mono text-cyan-400/70">
                {deckA.tempo >= 0 ? "+" : ""}{deckA.tempo.toFixed(1)}%
              </span>
            </div>
            <div className="relative h-[0.5vh] min-h-[4px] bg-slate-900/50 rounded-full overflow-hidden border border-slate-800/30">
              <div
                className="absolute top-0 left-1/2 -translate-x-1/2 h-full bg-cyan-500/60"
                style={{ width: `${Math.abs(deckA.tempo) * 2}%` }}
              />
            </div>
          </div>
        </div>

        {/* CENTER MIXER */}
        <div className="w-[14.6vw] min-w-[240px] max-w-[280px] h-full flex flex-col items-center justify-between py-[2vh] bg-slate-900/20 rounded-lg border border-slate-800/30">
          
          <div className="flex gap-[2vw]">
            <div className="flex flex-col items-center gap-[0.5vh]">
              <div className="text-[0.5vw] min-text-[9px] font-mono text-cyan-400/60">A</div>
              <div className="w-[0.8vw] min-w-[8px] h-[11vh] min-h-[96px] bg-slate-900/60 rounded-full overflow-hidden border border-slate-800/40">
                <div 
                  className="w-full bg-gradient-to-t from-cyan-500/60 to-cyan-400/60"
                  style={{ height: `${deckA.gain}%`, marginTop: 'auto' }}
                />
              </div>
              <div className="text-[0.5vw] min-text-[8px] font-mono text-slate-600">{deckA.gain}%</div>
            </div>
            <div className="flex flex-col items-center gap-[0.5vh]">
              <div className="text-[0.5vw] min-text-[9px] font-mono text-orange-400/60">B</div>
              <div className="w-[0.8vw] min-w-[8px] h-[11vh] min-h-[96px] bg-slate-900/60 rounded-full overflow-hidden border border-slate-800/40">
                <div 
                  className="w-full bg-gradient-to-t from-orange-500/60 to-orange-400/60"
                  style={{ height: `${deckB.gain}%`, marginTop: 'auto' }}
                />
              </div>
              <div className="text-[0.5vw] min-text-[8px] font-mono text-slate-600">{deckB.gain}%</div>
            </div>
          </div>

          <div className="flex flex-col items-center gap-[1vh] my-[2vh]">
            <div className="text-[0.5vw] min-text-[9px] font-mono text-slate-600 uppercase tracking-widest mb-[0.5vh]">EQ</div>
            <div className="w-[4.2vw] h-[4.2vw] min-w-[70px] min-h-[70px] max-w-[80px] max-h-[80px] rounded-full bg-slate-900/40 border border-cyan-500/25 flex items-center justify-center">
              <div className="text-center">
                <span className="text-[0.8vw] min-text-[14px] font-mono text-cyan-400/70 font-semibold">HI</span>
                <div className="text-[0.5vw] min-text-[9px] text-slate-600 font-mono mt-[0.3vh]">100%</div>
              </div>
            </div>
            <div className="w-[4.2vw] h-[4.2vw] min-w-[70px] min-h-[70px] max-w-[80px] max-h-[80px] rounded-full bg-slate-900/40 border border-orange-500/25 flex items-center justify-center">
              <div className="text-center">
                <span className="text-[0.8vw] min-text-[14px] font-mono text-orange-400/70 font-semibold">MID</span>
                <div className="text-[0.5vw] min-text-[9px] text-slate-600 font-mono mt-[0.3vh]">100%</div>
              </div>
            </div>
            <div className="w-[4.2vw] h-[4.2vw] min-w-[70px] min-h-[70px] max-w-[80px] max-h-[80px] rounded-full bg-slate-900/40 border border-green-500/25 flex items-center justify-center">
              <div className="text-center">
                <span className="text-[0.8vw] min-text-[14px] font-mono text-green-400/70 font-semibold">LOW</span>
                <div className="text-[0.5vw] min-text-[9px] text-slate-600 font-mono mt-[0.3vh]">100%</div>
              </div>
            </div>
          </div>

          <div className="flex gap-[3vw] my-[2vh]">
            <div className="flex flex-col items-center gap-[1vh]">
              <div className="text-[0.5vw] min-text-[9px] font-mono text-cyan-400/60">CH A</div>
              <div className="w-[0.7vw] min-w-[6px] h-[13vh] min-h-[112px] bg-slate-900/60 rounded-full overflow-hidden border border-slate-800/40 relative">
                <div 
                  className="absolute bottom-0 w-full bg-cyan-500/70"
                  style={{ height: `${deckA.volume}%` }}
                />
              </div>
              <div className="text-[0.5vw] min-text-[9px] font-mono text-slate-600">{deckA.volume}%</div>
            </div>
            <div className="flex flex-col items-center gap-[1vh]">
              <div className="text-[0.5vw] min-text-[9px] font-mono text-orange-400/60">CH B</div>
              <div className="w-[0.7vw] min-w-[6px] h-[13vh] min-h-[112px] bg-slate-900/60 rounded-full overflow-hidden border border-slate-800/40 relative">
                <div 
                  className="absolute bottom-0 w-full bg-orange-500/70"
                  style={{ height: `${deckB.volume}%` }}
                />
              </div>
              <div className="text-[0.5vw] min-text-[9px] font-mono text-slate-600">{deckB.volume}%</div>
            </div>
          </div>

          <div className="w-full px-[2vw] mt-auto">
            <div className="flex items-center justify-between mb-[1vh]">
              <span className="text-[0.5vw] min-text-[9px] text-cyan-400/50 font-mono tracking-wider">A</span>
              <Activity className="w-[1vw] h-[1vw] min-w-[14px] min-h-[14px] text-slate-700" />
              <span className="text-[0.5vw] min-text-[9px] text-orange-400/50 font-mono tracking-wider">B</span>
            </div>
            <div className="relative h-[0.9vh] min-h-[8px] bg-slate-900/60 rounded-full overflow-hidden border border-slate-800/40">
              <div
                className="absolute top-0 left-0 h-full bg-cyan-500/60 transition-all"
                style={{ width: `${100 - crossfader}%` }}
              />
              <div
                className="absolute top-0 right-0 h-full bg-orange-500/60 transition-all"
                style={{ width: `${crossfader}%` }}
              />
              <input
                type="range"
                min="0"
                max="100"
                value={crossfader}
                onChange={(e) => setCrossfader(parseInt(e.target.value))}
                className="absolute inset-0 w-full opacity-0 cursor-pointer"
              />
            </div>
            <div className="text-center mt-[0.5vh]">
              <div className="text-[0.5vw] min-text-[8px] font-mono text-slate-600 uppercase">Crossfader</div>
            </div>
          </div>

          <div className="flex items-center gap-[1vw] px-[1.5vw] py-[0.7vh] bg-slate-900/40 rounded border border-slate-700/20 mt-[1.5vh]">
            <Zap className={`w-[0.8vw] h-[0.8vw] min-w-[12px] min-h-[12px] ${bpmCompatibility > 80 ? "text-green-400" : "text-amber-400"}`} />
            <div className="text-[0.6vw] min-text-[10px] font-mono text-slate-400">{bpmCompatibility.toFixed(0)}%</div>
            <div className="text-[0.5vw] min-text-[8px] font-mono text-slate-600">SYNC</div>
          </div>
        </div>

        {/* DECK B */}
        <div className="flex flex-col items-center gap-[1.5vh]">
          <motion.div
            animate={{ rotate: deckB.isPlaying ? 360 : 0 }}
            transition={{
              duration: deckB.isPlaying ? 3 : 0,
              repeat: deckB.isPlaying ? Infinity : 0,
              ease: "linear",
            }}
            className="relative"
          >
            <div
              className={`w-[18.75vw] h-[18.75vw] min-w-[300px] min-h-[300px] max-w-[360px] max-h-[360px] rounded-full bg-gradient-to-br from-slate-900/80 via-slate-800/80 to-black/80 relative overflow-hidden transition-all ${
                deckB.isPlaying
                  ? "shadow-[inset_0_0_40px_rgba(249,115,22,0.12),0_0_40px_rgba(249,115,22,0.15)]"
                  : "shadow-[inset_0_0_30px_rgba(0,0,0,0.5)]"
              }`}
              style={{
                border: deckB.isPlaying
                  ? "1.5px solid rgba(249,115,22,0.3)"
                  : "1px solid rgba(71,85,105,0.25)",
              }}
            >
              {[...Array(16)].map((_, i) => (
                <div
                  key={i}
                  className="absolute inset-0 rounded-full border border-slate-700/12"
                  style={{ margin: `${i * 11}px` }}
                />
              ))}

              {deckB.isPlaying && (
                <motion.div
                  animate={{ rotate: -360 }}
                  transition={{ duration: 6, repeat: Infinity, ease: "linear" }}
                  className="absolute inset-[2.3vw] rounded-full border border-orange-400/15"
                />
              )}

              <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[5.8vw] h-[5.8vw] min-w-[90px] min-h-[90px] max-w-[112px] max-h-[112px] rounded-full bg-gradient-to-br from-slate-900 to-slate-950 flex flex-col items-center justify-center"
                style={{
                  border: deckB.isPlaying
                    ? "1.5px solid rgba(249,115,22,0.25)"
                    : "1px solid rgba(71,85,105,0.25)",
                  boxShadow: deckB.isPlaying
                    ? "0 0 20px rgba(249,115,22,0.15)"
                    : "none",
                }}
              >
                <Disc3 className="w-[1.5vw] h-[1.5vw] min-w-[24px] min-h-[24px] text-orange-400/50 mb-[0.5vh]" />
                <div
                  className={`text-[2vw] min-text-[32px] font-mono font-bold transition-all ${
                    deckB.isPlaying ? "text-orange-400" : "text-slate-600"
                  }`}
                  style={{
                    textShadow: deckB.isPlaying
                      ? "0 0 15px rgba(249,115,22,0.3)"
                      : "none",
                  }}
                >
                  {deckB.track ? deckB.track.bpm.toFixed(1) : "---"}
                </div>
                <div className="text-[0.5vw] min-text-[8px] text-slate-600 font-mono tracking-wider mt-[0.3vh]">BPM</div>
              </div>
            </div>
          </motion.div>

          <motion.button
            whileHover={{ scale: 1.04 }}
            whileTap={{ scale: 0.96 }}
            onClick={() => togglePlayDeck("B")}
            className={`w-[3vw] h-[3vw] min-w-[48px] min-h-[48px] max-w-[60px] max-h-[60px] rounded-full border transition-all ${
              deckB.isPlaying
                ? "bg-orange-500/85 border-orange-400/50 shadow-[0_0_18px_rgba(249,115,22,0.25)]"
                : "bg-slate-900/30 border-slate-700/40 hover:border-orange-500/30"
            }`}
          >
            {deckB.isPlaying ? (
              <Pause className="w-[1.5vw] h-[1.5vw] min-w-[24px] min-h-[24px] text-slate-900 mx-auto" />
            ) : (
              <Play className="w-[1.5vw] h-[1.5vw] min-w-[24px] min-h-[24px] text-orange-400 mx-auto ml-[0.2vw]" />
            )}
          </motion.button>

          <div className="w-full px-[2vw]">
            <div className="flex items-center justify-between mb-[0.5vh]">
              <span className="text-[0.5vw] min-text-[9px] font-mono text-slate-600">TEMPO</span>
              <span className="text-[0.6vw] min-text-[10px] font-mono text-orange-400/70">
                {deckB.tempo >= 0 ? "+" : ""}{deckB.tempo.toFixed(1)}%
              </span>
            </div>
            <div className="relative h-[0.5vh] min-h-[4px] bg-slate-900/50 rounded-full overflow-hidden border border-slate-800/30">
              <div
                className="absolute top-0 left-1/2 -translate-x-1/2 h-full bg-orange-500/60"
                style={{ width: `${Math.abs(deckB.tempo) * 2}%` }}
              />
            </div>
          </div>
        </div>
      </div>

      {/* LIBRARY - Remaining 38.9% of screen */}
      <div className="flex-1 flex flex-col bg-slate-950/15 min-h-0">
        <div className="h-[4.2vh] min-h-[45px] flex items-center justify-between px-[3vw] border-b border-slate-800/20 shrink-0">
          <div className="flex items-center gap-[1.5vw]">
            <Folder className="w-[1vw] h-[1vw] min-w-[14px] min-h-[14px] text-slate-600" />
            <span className="text-[0.6vw] min-text-[10px] font-mono text-slate-500">Ibiza No1</span>
            <span className="text-[0.6vw] min-text-[10px] text-slate-700 font-mono">· 540 Songs</span>
          </div>
          <div className="relative">
            <Search className="absolute left-[0.8vw] top-1/2 -translate-y-1/2 w-[0.8vw] h-[0.8vw] min-w-[12px] min-h-[12px] text-slate-600" />
            <input
              type="text"
              placeholder="Search tracks..."
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              className="pl-[2.5vw] pr-[1.5vw] py-[0.7vh] bg-slate-900/30 border border-slate-700/25 rounded text-[0.6vw] min-text-[10px] text-slate-300 placeholder-slate-700 focus:border-cyan-500/25 focus:outline-none w-[12.5vw] min-w-[192px]"
            />
          </div>
        </div>

        <div className="flex-1 overflow-y-auto">
          <table className="w-full text-[0.7vw] min-text-[12px]">
            <thead className="sticky top-0 bg-slate-950/85 border-b border-slate-800/20 z-10 backdrop-blur-sm">
              <tr className="text-slate-600 font-mono text-[0.6vw] min-text-[10px]">
                <th className="text-left px-[3vw] py-[0.9vh] w-[3vw]">#</th>
                <th className="text-left px-[1.5vw] py-[0.9vh]">Title</th>
                <th className="text-left px-[1.5vw] py-[0.9vh]">Artist</th>
                <th className="text-left px-[1.5vw] py-[0.9vh]">Album</th>
                <th className="text-left px-[1.5vw] py-[0.9vh]">Genre</th>
                <th className="text-left px-[1.5vw] py-[0.9vh]">Key</th>
                <th className="text-left px-[1.5vw] py-[0.9vh]">Time</th>
                <th className="text-left px-[1.5vw] py-[0.9vh]">BPM</th>
              </tr>
            </thead>
            <tbody>
              {filteredLibrary.map((track, index) => (
                <motion.tr
                  key={track.id}
                  onClick={() => setSelectedTrack(track.id)}
                  onDoubleClick={() => loadTrackToDeck(track, "A")}
                  whileHover={{ backgroundColor: "rgba(30, 41, 59, 0.25)" }}
                  className={`cursor-pointer border-b border-slate-800/10 font-mono text-[0.6vw] min-text-[10px] transition-colors ${
                    selectedTrack === track.id
                      ? "bg-cyan-500/5 text-cyan-400"
                      : "text-slate-500 hover:text-slate-300"
                  }`}
                >
                  <td className="px-[3vw] py-[1.2vh] text-slate-700">{index + 1}</td>
                  <td className="px-[1.5vw] py-[1.2vh] text-slate-300">{track.name}</td>
                  <td className="px-[1.5vw] py-[1.2vh] text-slate-500">{track.artist}</td>
                  <td className="px-[1.5vw] py-[1.2vh] text-slate-600">{track.album}</td>
                  <td className="px-[1.5vw] py-[1.2vh] text-slate-600">{track.genre}</td>
                  <td className="px-[1.5vw] py-[1.2vh] text-purple-400/60">{track.key}</td>
                  <td className="px-[1.5vw] py-[1.2vh] text-slate-600">{track.time}</td>
                  <td className="px-[1.5vw] py-[1.2vh] text-cyan-400/70">{track.bpm.toFixed(1)}</td>
                </motion.tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
