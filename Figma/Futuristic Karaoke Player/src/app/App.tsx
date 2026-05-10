import { useState, useEffect, useRef, useCallback } from 'react';
import { Play, Pause, SkipBack, SkipForward, Volume2, Repeat, Repeat1, Shuffle, List, Gauge, Zap, X } from 'lucide-react';
import { motion, AnimatePresence } from 'motion/react';
import { Button } from './components/ui/button';
import { Slider } from './components/ui/slider';
import Equalizer from './components/Equalizer';
import LyricsDisplay from './components/LyricsDisplay';

interface LyricLine {
  time: number;
  text: string;
}

interface Song {
  id: string;
  title: string;
  artist: string;
  duration: number;
  lyrics: LyricLine[];
  filePath?: string;
}

const fallbackSongs: Song[] = [
  {
    id: '1',
    title: 'Neon Dreams',
    artist: 'Cyber Pulse',
    duration: 180,
    lyrics: [
      { time: 0, text: '♪ ♪ ♪' },
      { time: 5, text: 'Walking through the city lights' },
      { time: 9, text: 'Neon colors burning bright' },
      { time: 13, text: 'Electric feelings in the air' },
      { time: 17, text: 'Take me to a place out there' },
      { time: 21, text: '' },
      { time: 23, text: 'Digital hearts beat as one' },
      { time: 27, text: 'Racing till the night is done' },
      { time: 31, text: 'In this world of chrome and steel' },
      { time: 35, text: 'Tell me that this love is real' },
      { time: 39, text: '' },
      { time: 41, text: 'Neon dreams, neon skies' },
      { time: 45, text: 'See the future in your eyes' },
      { time: 49, text: 'Dancing through the laser beams' },
      { time: 53, text: 'Living in our neon dreams' },
      { time: 57, text: '' },
      { time: 59, text: 'Synthesizers fill the night' },
      { time: 63, text: 'Holographic pure delight' },
      { time: 67, text: 'Gravity has lost its hold' },
      { time: 71, text: 'In this realm of blue and gold' },
      { time: 75, text: '' },
      { time: 77, text: 'Neon dreams, neon skies' },
      { time: 81, text: 'See the future in your eyes' },
      { time: 85, text: 'Dancing through the laser beams' },
      { time: 89, text: 'Living in our neon dreams' },
      { time: 93, text: '' },
      { time: 95, text: '♪ ♪ ♪' },
    ],
  },
  {
    id: '2',
    title: 'Cosmic Voyage',
    artist: 'Star Runners',
    duration: 165,
    lyrics: [
      { time: 0, text: '♪ ♪ ♪' },
      { time: 4, text: 'Floating through the endless space' },
      { time: 8, text: 'Stars illuminate your face' },
      { time: 12, text: 'Galaxies within our reach' },
      { time: 16, text: 'Every moment we can teach' },
      { time: 20, text: '' },
      { time: 22, text: 'Breaking through the atmosphere' },
      { time: 26, text: 'Leave behind our doubts and fear' },
      { time: 30, text: 'Cosmic rays will guide our way' },
      { time: 34, text: 'To the dawn of a new day' },
      { time: 38, text: '' },
      { time: 40, text: 'On a cosmic voyage we fly' },
      { time: 44, text: 'Through the universe so high' },
      { time: 48, text: 'Nothing here to hold us down' },
      { time: 52, text: 'In the stars we will be found' },
      { time: 56, text: '' },
      { time: 58, text: '♪ ♪ ♪' },
    ],
  },
];

export default function App() {
  const postHost = useCallback((msg: any) => {
    try {
      const w = window as any;
      if (w?.chrome?.webview?.postMessage) w.chrome.webview.postMessage(msg);
    } catch {
    }
  }, []);

  const [songs, setSongs] = useState<Song[]>(fallbackSongs);
  const [currentSongIndex, setCurrentSongIndex] = useState(0);
  const [isPlaying, setIsPlaying] = useState(false);
  const [currentTime, setCurrentTime] = useState(0);
  const [volume, setVolume] = useState([70]);
  const [repeatMode, setRepeatMode] = useState<'off' | 'all' | 'one'>('all');
  const [shuffleMode, setShuffleMode] = useState(false);
  const [showPlaylist, setShowPlaylist] = useState(true);
  const [playbackSpeed, setPlaybackSpeed] = useState([1]);
  const [autoAdvance, setAutoAdvance] = useState(true);
  const [showSearch, setShowSearch] = useState(false);
  const [ytQuery, setYtQuery] = useState('');
  const [ytEmbedSrc, setYtEmbedSrc] = useState<string>('');
  const [controlsVisible, setControlsVisible] = useState(true);
  const hideControlsTimerRef = useRef<number | null>(null);
  const lyricsByFilePathRef = useRef<Record<string, LyricLine[]>>({});

  const currentSong = songs[currentSongIndex];

  const bumpControls = useCallback(() => {
    setControlsVisible(true);
    if (hideControlsTimerRef.current) window.clearTimeout(hideControlsTimerRef.current);
    hideControlsTimerRef.current = window.setTimeout(() => setControlsVisible(false), 2500);
  }, []);

  useEffect(() => {
    bumpControls();

    const onMove = () => bumpControls();
    const onKey = () => bumpControls();
    const onTouch = () => bumpControls();

    window.addEventListener('mousemove', onMove);
    window.addEventListener('keydown', onKey);
    window.addEventListener('touchstart', onTouch, { passive: true });
    window.addEventListener('pointermove', onMove);

    return () => {
      window.removeEventListener('mousemove', onMove);
      window.removeEventListener('keydown', onKey);
      window.removeEventListener('touchstart', onTouch);
      window.removeEventListener('pointermove', onMove);
      if (hideControlsTimerRef.current) window.clearTimeout(hideControlsTimerRef.current);
      hideControlsTimerRef.current = null;
    };
  }, [bumpControls]);

  useEffect(() => {
    try {
      const w = window as any;
      if (!w?.chrome?.webview?.addEventListener) return;

      const handler = (event: any) => {
        const msg = event?.data;
        if (!msg || typeof msg.type !== 'string') return;

        if (msg.type === 'karaoke.state') {
          const p = msg.payload || {};
          const items = Array.isArray(p.items) ? p.items : [];
          const mapped = items.map((it: any) => {
            const fp = String(it.filePath || it.id || '').trim();
            const id = fp || String(it.id || '').trim();
            const title = String(it.title || '').trim() || (fp ? fp.split(/[\\/]/).pop()?.replace(/\.[^.]+$/, '') : '') || 'Unknown';
            const artist = String(it.artist || '').trim();
            const duration = Math.max(0, Number(it.durationSeconds || 0) || 0);
            const lyrics = fp && lyricsByFilePathRef.current[fp] ? lyricsByFilePathRef.current[fp] : [];
            return { id, title, artist, duration, lyrics, filePath: fp } as Song;
          }).filter((s: Song) => String(s.id || '').length > 0);

          setSongs(mapped.length > 0 ? mapped : fallbackSongs);

          const playing = !!p.isPlaying;
          setIsPlaying(playing);

          const vol = Number(p.volume ?? NaN);
          if (Number.isFinite(vol)) setVolume([Math.max(0, Math.min(100, vol))]);

          const t = Number(p.progressSeconds ?? NaN);
          if (Number.isFinite(t) && t >= 0) setCurrentTime(t);
        }

        if (msg.type === 'lyrics.result') {
          const p = msg.payload || {};
          const fp = String(p.filePath || '').trim();
          const lines = Array.isArray(p.lines) ? p.lines : [];
          if (!fp || lines.length === 0) return;
          const mappedLines: LyricLine[] = lines.map((l: any) => ({
            time: Math.max(0, Number(l.timeSeconds || 0) || 0),
            text: String(l.text || ''),
          }));
          lyricsByFilePathRef.current[fp] = mappedLines;
          setSongs(prev => prev.map(s => (s.filePath && s.filePath === fp) ? { ...s, lyrics: mappedLines } : s));
        }
      };

      w.chrome.webview.addEventListener('message', handler);
      postHost({ type: 'karaoke.getState' });
      return () => {
        try { w.chrome.webview.removeEventListener('message', handler); } catch {
        }
      };
    } catch {
      return;
    }
  }, [postHost]);

  const handlePlayPause = () => {
    postHost({ type: 'play.toggle' });
  };

  const handleNext = () => {
    if (shuffleMode) {
      let nextIndex;
      do {
        nextIndex = Math.floor(Math.random() * songs.length);
      } while (nextIndex === currentSongIndex && songs.length > 1);
      setCurrentSongIndex(nextIndex);
    } else {
      setCurrentSongIndex((prev) => (prev + 1) % songs.length);
    }
    const next = songs.length > 0 ? songs[(shuffleMode ? currentSongIndex : (currentSongIndex + 1)) % songs.length] : null;
    const fp = (next as any)?.filePath;
    if (fp) postHost({ type: 'karaoke.play', payload: { filePath: fp } });
  };

  const handlePrevious = () => {
    if (currentTime > 3) {
      postHost({ type: 'play.seek', payload: { seconds: 0 } });
    } else {
      setCurrentSongIndex((prev) => (prev - 1 + songs.length) % songs.length);
      const next = songs.length > 0 ? songs[(currentSongIndex - 1 + songs.length) % songs.length] : null;
      const fp = (next as any)?.filePath;
      if (fp) postHost({ type: 'karaoke.play', payload: { filePath: fp } });
    }
  };

  const toggleRepeat = () => {
    setRepeatMode((prev) => {
      if (prev === 'off') return 'all';
      if (prev === 'all') return 'one';
      return 'off';
    });
  };

  const selectSong = (index: number) => {
    setCurrentSongIndex(index);
    const s = songs[index];
    const fp = (s as any)?.filePath;
    if (fp) {
      postHost({ type: 'karaoke.play', payload: { filePath: fp } });
      postHost({ type: 'lyrics.get', payload: { filePath: fp } });
    }
  };

  const handleProgressChange = (value: number[]) => {
    const sec = Number(value?.[0] ?? 0);
    postHost({ type: 'play.seek', payload: { seconds: sec } });
  };

  const runYouTubeSearch = () => {
    const q = ytQuery.trim();
    if (!q) return;
    const isUrl = /^https?:\/\//i.test(q);
    if (isUrl) {
      const m =
        q.match(/[?&]v=([^&]+)/i) ||
        q.match(/youtu\.be\/([^?&/]+)/i) ||
        q.match(/youtube\.com\/shorts\/([^?&/]+)/i) ||
        q.match(/youtube\.com\/embed\/([^?&/]+)/i);
      const id = (m?.[1] ?? '').trim();
      if (id) setYtEmbedSrc(`https://www.youtube.com/embed/${encodeURIComponent(id)}?autoplay=1&rel=0`);
      else setYtEmbedSrc(`https://www.youtube.com/embed?autoplay=1&rel=0&listType=search&list=${encodeURIComponent(q)}`);
    } else {
      setYtEmbedSrc(`https://www.youtube.com/embed?autoplay=1&rel=0&listType=search&list=${encodeURIComponent(q)}`);
    }
    setShowSearch(false);
    bumpControls();
  };

  const handleVolumeChange = (value: number[]) => {
    const v = Math.max(0, Math.min(100, Number(value?.[0] ?? 0) || 0));
    setVolume([v]);
    postHost({ type: 'play.volume', payload: { value: v } });
  };

  const formatTime = (seconds: number) => {
    const mins = Math.floor(seconds / 60);
    const secs = Math.floor(seconds % 60);
    return `${mins}:${secs.toString().padStart(2, '0')}`;
  };

  return (
    <div className="h-screen w-screen bg-black relative overflow-hidden">
      {/* Animated Grid Background */}
      <div className="absolute inset-0" style={{
        backgroundImage: `
          linear-gradient(rgba(6, 182, 212, 0.1) 1px, transparent 1px),
          linear-gradient(90deg, rgba(6, 182, 212, 0.1) 1px, transparent 1px)
        `,
        backgroundSize: '50px 50px',
        animation: 'gridMove 20s linear infinite'
      }} />
      
      {/* Scanline Effect */}
      <div className="absolute inset-0 pointer-events-none" style={{
        background: 'repeating-linear-gradient(0deg, transparent, transparent 2px, rgba(6, 182, 212, 0.03) 2px, rgba(6, 182, 212, 0.03) 4px)',
        animation: 'scanline 8s linear infinite'
      }} />

      {/* Glowing Orbs */}
      <motion.div 
        className="absolute w-96 h-96 rounded-full blur-[150px]"
        style={{ background: 'radial-gradient(circle, rgba(6, 182, 212, 0.4) 0%, transparent 70%)' }}
        animate={{
          x: ['-10%', '110%'],
          y: ['10%', '80%', '10%'],
        }}
        transition={{
          duration: 20,
          repeat: Infinity,
          ease: 'linear'
        }}
      />
      <motion.div 
        className="absolute w-96 h-96 rounded-full blur-[150px]"
        style={{ background: 'radial-gradient(circle, rgba(168, 85, 247, 0.4) 0%, transparent 70%)' }}
        animate={{
          x: ['110%', '-10%'],
          y: ['80%', '10%', '80%'],
        }}
        transition={{
          duration: 25,
          repeat: Infinity,
          ease: 'linear'
        }}
      />

      <div className="relative z-10 h-screen w-screen overflow-hidden">
        <div className="absolute inset-0">
          <Equalizer isPlaying={isPlaying} />
        </div>

        <div className="absolute inset-0 px-6 pt-20 pb-28">
          <div className="max-w-6xl mx-auto h-full flex flex-col items-center justify-center">
            <AnimatePresence mode="wait">
              <motion.div
                key={currentSong.id}
                initial={{ opacity: 0, y: -10 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: 10 }}
                className="text-center mb-6"
              >
                <div className="inline-block relative">
                  <h2 className="text-4xl md:text-5xl font-black text-white mb-2 tracking-tight"
                    style={{
                      textShadow: '0 0 18px rgba(6, 182, 212, 0.55), 0 0 42px rgba(168, 85, 247, 0.35)'
                    }}>
                    {currentSong.title}
                  </h2>
                  <div className="h-[3px] bg-gradient-to-r from-transparent via-cyan-400 to-transparent rounded-full" />
                </div>
                <p className="text-cyan-300 text-lg font-mono tracking-wider mt-2">// {currentSong.artist}</p>
              </motion.div>
            </AnimatePresence>

            {ytEmbedSrc ? (
              <div className="w-full max-w-5xl flex-1 min-h-0 rounded-2xl overflow-hidden border border-cyan-500/30 bg-black/40">
                <iframe
                  title="YouTube Karaoke"
                  className="w-full h-full"
                  src={ytEmbedSrc}
                  allow="autoplay; encrypted-media; fullscreen"
                />
              </div>
            ) : (
              <div className="w-full max-w-5xl flex-1 min-h-0">
                <LyricsDisplay lyrics={currentSong.lyrics} currentTime={currentTime} isPlaying={isPlaying} />
              </div>
            )}
          </div>
        </div>

        <AnimatePresence>
          {(controlsVisible || showSearch) && (
            <motion.div
              initial={{ opacity: 0, y: 10 }}
              animate={{ opacity: 1, y: 0 }}
              exit={{ opacity: 0, y: 10 }}
              className="absolute inset-x-0 bottom-0 z-20 px-6 pb-6 pt-3"
              onMouseMove={bumpControls}
              style={{ pointerEvents: 'auto' }}
            >
              <div className="max-w-6xl mx-auto rounded-2xl bg-black/55 backdrop-blur-xl border border-cyan-500/25 overflow-hidden">
                <div className="px-4 py-3 flex items-center justify-between">
                  <div className="flex items-center gap-2">
                    <Button
                      onClick={() => {
                        setShowPlaylist(!showPlaylist);
                        bumpControls();
                      }}
                      variant="ghost"
                      size="sm"
                      className="h-8 px-3 text-cyan-200 hover:text-cyan-100 hover:bg-cyan-500/10 border border-cyan-500/25"
                    >
                      <List className="w-4 h-4 mr-2" />
                      Queue
                    </Button>
                    <Button
                      onClick={() => {
                        setShowSearch(true);
                        bumpControls();
                      }}
                      variant="ghost"
                      size="sm"
                      className="h-8 px-3 text-cyan-200 hover:text-cyan-100 hover:bg-cyan-500/10 border border-cyan-500/25"
                    >
                      Search
                    </Button>
                    {ytEmbedSrc ? (
                      <Button
                        onClick={() => {
                          setYtEmbedSrc('');
                          bumpControls();
                        }}
                        variant="ghost"
                        size="sm"
                        className="h-8 w-8 p-0 text-cyan-200/80 hover:text-cyan-100 hover:bg-cyan-500/10 border border-cyan-500/25"
                      >
                        <X className="w-4 h-4" />
                      </Button>
                    ) : null}
                  </div>
                  <div className="px-3 py-1 bg-cyan-500/10 border border-cyan-500/25 rounded-lg">
                    <span className="text-cyan-200 font-mono text-xs tracking-wider">TRACK {String(currentSongIndex + 1).padStart(2, '0')}/{String(songs.length).padStart(2, '0')}</span>
                  </div>
                </div>

                <div className="px-4 pb-3">
                  <Slider
                    value={[currentTime]}
                    max={Math.max(1, currentSong.duration)}
                    step={0.1}
                    onValueChange={handleProgressChange}
                    className="mb-2 [&_[data-slot=slider-track]]:h-2 [&_[data-slot=slider-track]]:bg-white/10 [&_[data-slot=slider-track]]:border [&_[data-slot=slider-track]]:border-cyan-500/20 [&_[data-slot=slider-range]]:bg-gradient-to-r [&_[data-slot=slider-range]]:from-cyan-400 [&_[data-slot=slider-range]]:via-fuchsia-500 [&_[data-slot=slider-range]]:to-purple-500 [&_[data-slot=slider-thumb]]:size-3 [&_[data-slot=slider-thumb]]:border-cyan-300 [&_[data-slot=slider-thumb]]:bg-black"
                  />
                  <div className="flex justify-between text-xs font-mono">
                    <span className="text-cyan-200">{formatTime(currentTime)}</span>
                    <span className="text-cyan-200/60">{formatTime(currentSong.duration)}</span>
                  </div>
                </div>

                <div className="px-4 pb-4 flex items-center justify-between gap-4">
                  <div className="flex items-center gap-2">
                    <Button
                      onClick={handlePrevious}
                      variant="ghost"
                      size="sm"
                      className="h-10 w-10 p-0 rounded-xl bg-black/40 hover:bg-cyan-500/15 border border-cyan-500/25 text-cyan-200"
                    >
                      <SkipBack className="w-5 h-5" />
                    </Button>

                    <Button
                      onClick={handlePlayPause}
                      className="h-12 w-12 p-0 rounded-2xl bg-gradient-to-br from-cyan-500 via-cyan-400 to-purple-500 text-black"
                      style={{ boxShadow: '0 0 26px rgba(6, 182, 212, 0.45), 0 0 44px rgba(168, 85, 247, 0.22)' }}
                    >
                      {isPlaying ? <Pause className="w-6 h-6" /> : <Play className="w-6 h-6 ml-[1px]" />}
                    </Button>

                    <Button
                      onClick={handleNext}
                      variant="ghost"
                      size="sm"
                      className="h-10 w-10 p-0 rounded-xl bg-black/40 hover:bg-cyan-500/15 border border-cyan-500/25 text-cyan-200"
                    >
                      <SkipForward className="w-5 h-5" />
                    </Button>
                  </div>

                  <div className="flex items-center gap-2">
                    <Button
                      onClick={() => setShuffleMode(!shuffleMode)}
                      variant="ghost"
                      size="sm"
                      className={`h-9 w-9 p-0 rounded-xl border transition-all ${
                        shuffleMode
                          ? 'bg-cyan-500/20 border-cyan-400/40 text-cyan-200'
                          : 'bg-black/35 border-cyan-500/25 text-cyan-200/70 hover:text-cyan-200'
                      }`}
                    >
                      <Shuffle className="w-4 h-4" />
                    </Button>

                    <Button
                      onClick={toggleRepeat}
                      variant="ghost"
                      size="sm"
                      className={`h-9 w-9 p-0 rounded-xl border transition-all ${
                        repeatMode !== 'off'
                          ? 'bg-cyan-500/20 border-cyan-400/40 text-cyan-200'
                          : 'bg-black/35 border-cyan-500/25 text-cyan-200/70 hover:text-cyan-200'
                      }`}
                    >
                      {repeatMode === 'one' ? <Repeat1 className="w-4 h-4" /> : <Repeat className="w-4 h-4" />}
                    </Button>

                    <div className="hidden md:flex items-center gap-2 px-3 py-1.5 bg-black/35 border border-cyan-500/20 rounded-xl">
                      <Gauge className="w-4 h-4 text-cyan-200/80" />
                      <span className="text-cyan-200 font-mono text-xs">{playbackSpeed[0].toFixed(1)}x</span>
                      <Slider
                        value={playbackSpeed}
                        min={0.5}
                        max={2}
                        step={0.1}
                        onValueChange={setPlaybackSpeed}
                        className="w-20 [&_[data-slot=slider-track]]:h-1.5 [&_[data-slot=slider-track]]:bg-white/10 [&_[data-slot=slider-range]]:bg-cyan-400 [&_[data-slot=slider-thumb]]:size-2.5"
                      />
                    </div>

                    <Button
                      onClick={() => setAutoAdvance(!autoAdvance)}
                      variant="ghost"
                      size="sm"
                      className={`h-9 w-9 p-0 rounded-xl border transition-all ${
                        autoAdvance
                          ? 'bg-cyan-500/20 border-cyan-400/40 text-cyan-200'
                          : 'bg-black/35 border-cyan-500/25 text-cyan-200/70 hover:text-cyan-200'
                      }`}
                    >
                      <Zap className="w-4 h-4" />
                    </Button>
                  </div>

                  <div className="hidden lg:flex items-center gap-3 min-w-[240px] justify-end">
                    <Volume2 className="w-4 h-4 text-cyan-200/80" />
                    <Slider
                      value={volume}
                      max={100}
                      step={1}
                      onValueChange={handleVolumeChange}
                      className="w-36 [&_[data-slot=slider-track]]:h-1.5 [&_[data-slot=slider-track]]:bg-white/10 [&_[data-slot=slider-range]]:bg-gradient-to-r [&_[data-slot=slider-range]]:from-cyan-400 [&_[data-slot=slider-range]]:to-purple-500 [&_[data-slot=slider-thumb]]:size-2.5"
                    />
                    <span className="text-cyan-200 font-mono text-xs w-10 text-right">{volume[0]}%</span>
                  </div>
                </div>
              </div>
            </motion.div>
          )}
        </AnimatePresence>
      </div>

      <AnimatePresence>
        {showSearch && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 z-[80] bg-black/80 backdrop-blur-xl flex items-center justify-center p-4"
            onMouseMove={bumpControls}
          >
            <motion.div
              initial={{ opacity: 0, scale: 0.98, y: 10 }}
              animate={{ opacity: 1, scale: 1, y: 0 }}
              exit={{ opacity: 0, scale: 0.98, y: 10 }}
              className="w-[min(860px,94vw)] rounded-2xl bg-black/60 border border-cyan-500/30 overflow-hidden"
            >
              <div className="h-14 px-4 flex items-center gap-2 border-b border-cyan-500/20">
                <div className="text-cyan-300 font-mono text-xs tracking-wider">YOUTUBE</div>
                <div className="flex-1 flex items-center gap-2">
                  <input
                    value={ytQuery}
                    onChange={(e) => setYtQuery(e.target.value)}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter') runYouTubeSearch();
                    }}
                    placeholder="Search or paste a YouTube link…"
                    className="w-full h-9 px-3 rounded-lg bg-black/40 border border-cyan-500/20 text-white/90 text-sm outline-none focus:border-cyan-400/50"
                    autoFocus
                  />
                  <Button
                    onClick={runYouTubeSearch}
                    size="sm"
                    className="h-9 px-4 rounded-lg bg-cyan-500/20 border border-cyan-400/30 text-cyan-200 hover:bg-cyan-500/30"
                  >
                    Go
                  </Button>
                  <Button
                    onClick={() => setShowSearch(false)}
                    variant="ghost"
                    size="sm"
                    className="h-9 px-3 text-cyan-200/80 hover:text-cyan-200 hover:bg-cyan-500/10"
                  >
                    Close
                  </Button>
                </div>
              </div>
              <div className="p-4 text-white/60 text-sm">
                Enter a search like: <span className="text-cyan-200">bohemian rhapsody karaoke</span>
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>

      <style>{`
        @keyframes gridMove {
          0% { transform: translateY(0); }
          100% { transform: translateY(50px); }
        }
        @keyframes scanline {
          0% { transform: translateY(-100%); }
          100% { transform: translateY(100%); }
        }
      `}</style>
    </div>
  );
}
