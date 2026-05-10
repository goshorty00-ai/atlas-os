import { useState, useEffect } from 'react';
import {
  Play, Pause, SkipBack, SkipForward, Volume2, Heart,
  Shuffle, Repeat, ListMusic, Mic2, Sparkles
} from 'lucide-react';
import { ImageWithFallback } from './figma/ImageWithFallback';

interface MusicPlayerProps {
  coverUrl?: string;
  title?: string;
  artist?: string;
  album?: string;
}

export function MusicPlayer({
  coverUrl,
  title = 'Neon Dreams',
  artist = 'Synthwave Collective',
  album = 'Cyberpunk 2077 OST',
}: MusicPlayerProps) {
  const [isPlaying, setIsPlaying] = useState(true);
  const [volume, setVolume] = useState(75);
  const [progress, setProgress] = useState(45);
  const [favorite, setFavorite] = useState(false);
  const [bars, setBars] = useState<number[]>(Array.from({ length: 28 }, () => Math.random()));

  useEffect(() => {
    if (!isPlaying) return;
    const id = setInterval(() => {
      setBars(Array.from({ length: 28 }, () => Math.random() * 0.85 + 0.15));
      setProgress((p) => (p >= 100 ? 0 : p + 0.3));
    }, 120);
    return () => clearInterval(id);
  }, [isPlaying]);

  const iconBtn = "p-1.5 rounded-md text-slate-400 hover:text-cyan-300 hover:bg-slate-800/60 transition-colors";

  return (
    <div className="sticky bottom-0 z-30 mt-6">
      <div
        className="rounded-xl border border-purple-500/20 bg-slate-950/85 backdrop-blur-2xl px-3 py-2 flex items-center gap-3 shadow-[0_-10px_40px_-10px_rgba(168,85,247,0.25)]"
        style={{ minHeight: 80 }}
      >
        {/* Cover + info */}
        <div className="flex items-center gap-3 min-w-0 w-[220px]">
          <div className="relative w-12 h-12 rounded-lg overflow-hidden bg-gradient-to-br from-purple-600 to-pink-600 flex-shrink-0 ring-1 ring-purple-400/30">
            {coverUrl ? (
              <ImageWithFallback src={coverUrl} alt={title} className="w-full h-full object-cover" />
            ) : (
              <div className="w-full h-full flex items-center justify-center text-white/30 text-xl">♪</div>
            )}
            {isPlaying && (
              <div className="absolute inset-0 ring-2 ring-cyan-400/40 animate-pulse rounded-lg" />
            )}
          </div>
          <div className="min-w-0">
            <div className="text-slate-100 text-xs truncate">{title}</div>
            <div className="text-slate-400 text-[10px] truncate">{artist}</div>
            <div className="text-slate-500 text-[10px] truncate">{album}</div>
          </div>
          <button
            onClick={() => setFavorite(!favorite)}
            className={`p-1 transition-colors ${favorite ? 'text-pink-400' : 'text-slate-500 hover:text-pink-300'}`}
          >
            <Heart size={14} fill={favorite ? 'currentColor' : 'none'} />
          </button>
        </div>

        {/* Centre — controls + waveform */}
        <div className="flex-1 flex flex-col gap-1 min-w-0">
          <div className="flex items-center justify-center gap-2">
            <button className={iconBtn}><Shuffle size={14} /></button>
            <button className={iconBtn}><SkipBack size={16} /></button>
            <button
              onClick={() => setIsPlaying(!isPlaying)}
              className="p-2 rounded-full bg-gradient-to-r from-cyan-500 to-purple-500 text-white hover:shadow-[0_0_20px_rgba(168,85,247,0.6)] transition-all"
            >
              {isPlaying ? <Pause size={14} fill="currentColor" /> : <Play size={14} fill="currentColor" />}
            </button>
            <button className={iconBtn}><SkipForward size={16} /></button>
            <button className={iconBtn}><Repeat size={14} /></button>
          </div>

          {/* Mini waveform timeline */}
          <div className="flex items-center gap-2">
            <span className="text-[10px] text-slate-500 w-9 text-right">2:15</span>
            <div className="relative flex-1 h-5 flex items-center">
              <div className="absolute inset-y-0 inset-x-0 flex items-end justify-between gap-px">
                {bars.map((h, i) => {
                  const played = (i / bars.length) * 100 < progress;
                  return (
                    <div
                      key={i}
                      className="flex-1 rounded-sm transition-all"
                      style={{
                        height: `${(isPlaying ? h : 0.2) * 100}%`,
                        background: played
                          ? 'linear-gradient(to top, rgb(34,211,238), rgb(168,85,247))'
                          : 'rgba(100,116,139,0.4)',
                      }}
                    />
                  );
                })}
              </div>
              <input
                type="range"
                value={progress}
                onChange={(e) => setProgress(Number(e.target.value))}
                className="absolute inset-0 w-full opacity-0 cursor-pointer"
              />
            </div>
            <span className="text-[10px] text-slate-500 w-9">5:00</span>
          </div>
        </div>

        {/* Right — extras */}
        <div className="flex items-center gap-1 w-[220px] justify-end">
          <button className={iconBtn} title="Lyrics"><Mic2 size={14} /></button>
          <button className={iconBtn} title="Queue"><ListMusic size={14} /></button>
          <button className={iconBtn} title="Visualizer"><Sparkles size={14} /></button>
          <div className="flex items-center gap-1.5 ml-2">
            <Volume2 size={13} className="text-slate-400" />
            <input
              type="range"
              value={volume}
              onChange={(e) => setVolume(Number(e.target.value))}
              className="w-16 h-1 rounded-full appearance-none cursor-pointer"
              style={{
                background: `linear-gradient(to right, rgb(34,211,238) ${volume}%, rgb(51,65,85) ${volume}%)`,
              }}
            />
          </div>
        </div>
      </div>
    </div>
  );
}
