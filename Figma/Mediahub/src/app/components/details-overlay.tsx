import { useEffect, useState } from 'react';
import {
  X, Play, Film, Plus, Eye, Share2, Star, ChevronLeft, Sparkles, Send,
  Check, Copy, Info, ChevronDown, AlertCircle, Crown, Zap,
} from 'lucide-react';
import { ImageWithFallback } from './figma/ImageWithFallback';

export interface DetailsItem {
  id: string;
  title: string;
  type: 'Movie' | 'TV';
  year: string;
  runtime?: string;
  episodeLength?: string;
  posterUrl?: string;
  backdropUrl?: string;
  tmdb?: number;
  imdb?: number;
  rt?: number;
  user?: number;
  cert?: string;
  quality?: string;
  genres: string[];
  description: string;
  director?: string;
  writers?: string[];
  releaseDate?: string;
  country?: string;
  language?: string;
  studio?: string;
  budget?: string;
  revenue?: string;
}

const QUICK_PROMPTS = [
  'Spoiler-free summary', 'Is it worth watching?', 'Find best source',
  'Explain ending', 'Parental guide', 'Similar titles', 'Cast highlights', 'Trailer breakdown',
];

const SOURCES = [
  { name: 'Plex',          stream: 'Local Library · Original',     quality: '4K HDR',  size: '38.4 GB', audio: 'Atmos 7.1', lang: 'EN',     subs: 'EN, ES, FR', health: 100, status: 'Available',       reliability: 99, recommended: true,  premium: false },
  { name: 'Jellyfin',      stream: 'Home Server',                  quality: '1080p',   size: '12.1 GB', audio: '5.1',       lang: 'EN',     subs: 'EN, ES',     health: 98,  status: 'Available',       reliability: 96, recommended: false, premium: false },
  { name: 'Netflix',       stream: 'Netflix Original',             quality: '4K Dolby Vision', size: 'Stream', audio: 'Atmos', lang: 'Multi', subs: 'Multi', health: 100, status: 'Premium',         reliability: 99, recommended: false, premium: true  },
  { name: 'Prime Video',   stream: 'Included with Prime',          quality: '4K',      size: 'Stream',  audio: '5.1',       lang: 'Multi',  subs: 'Multi',      health: 99,  status: 'Available',       reliability: 95, recommended: false, premium: true  },
  { name: 'Disney+',       stream: 'Disney+ Stream',               quality: '4K HDR',  size: 'Stream',  audio: 'Atmos',     lang: 'Multi',  subs: 'Multi',      health: 99,  status: 'Available',       reliability: 97, recommended: false, premium: true  },
  { name: 'Torrentio RD',  stream: 'YIFY · 4K HDR · Real-Debrid',  quality: '4K HDR',  size: '14.8 GB', audio: '5.1',       lang: 'EN',     subs: 'EN',         health: 92,  status: 'Buffer risk',     reliability: 84, recommended: false, premium: false },
  { name: 'Stremio Add-on',stream: 'Cinemeta · 1080p',             quality: '1080p',   size: '3.6 GB',  audio: 'Stereo',    lang: 'EN',     subs: 'EN',         health: 70,  status: 'Available',       reliability: 78, recommended: false, premium: false },
  { name: 'Local Library', stream: 'Downloads · Backup',           quality: '720p',    size: '1.9 GB',  audio: 'Stereo',    lang: 'EN',     subs: '—',          health: 100, status: 'Available',       reliability: 90, recommended: false, premium: false },
  { name: 'YouTube',       stream: 'Official Trailer',             quality: '1080p',   size: 'Stream',  audio: 'Stereo',    lang: 'EN',     subs: 'Auto',       health: 100, status: 'Available',       reliability: 99, recommended: false, premium: false },
];

const CAST = [
  { name: 'Timothée Chalamet', char: 'Paul Atreides', img: 'https://images.unsplash.com/photo-1531427186611-ecfd6d936c79?w=200&q=80' },
  { name: 'Zendaya',           char: 'Chani',          img: 'https://images.unsplash.com/photo-1494790108377-be9c29b29330?w=200&q=80' },
  { name: 'Rebecca Ferguson',  char: 'Lady Jessica',   img: 'https://images.unsplash.com/photo-1438761681033-6461ffad8d80?w=200&q=80' },
  { name: 'Javier Bardem',     char: 'Stilgar',        img: 'https://images.unsplash.com/photo-1500648767791-00dcc994a43e?w=200&q=80' },
  { name: 'Josh Brolin',       char: 'Gurney Halleck', img: 'https://images.unsplash.com/photo-1463453091185-61582044d556?w=200&q=80' },
  { name: 'Austin Butler',     char: 'Feyd-Rautha',    img: 'https://images.unsplash.com/photo-1492562080023-ab3db95bfbce?w=200&q=80' },
  { name: 'Florence Pugh',     char: 'Princess Irulan',img: 'https://images.unsplash.com/photo-1485893015719-2eecabaaa9d6?w=200&q=80' },
];

const SIMILAR_POSTERS = [
  'https://images.unsplash.com/photo-1518709268805-4e9042af9f23?w=400&q=80',
  'https://images.unsplash.com/photo-1446776811953-b23d57bd21aa?w=400&q=80',
  'https://images.unsplash.com/photo-1478720568477-152d9b164e26?w=400&q=80',
  'https://images.unsplash.com/photo-1534447677768-be436bb09401?w=400&q=80',
  'https://images.unsplash.com/photo-1542204165-65bf26472b9b?w=400&q=80',
  'https://images.unsplash.com/photo-1518930259200-3e5b1f3b1e5e?w=400&q=80',
  'https://images.unsplash.com/photo-1489599849927-2ee91cede3ba?w=400&q=80',
  'https://images.unsplash.com/photo-1440404653325-ab127d49abc1?w=400&q=80',
];

const SHELVES: Array<{ title: string; count: number }> = [
  { title: 'Similar Titles',   count: 8 },
  { title: 'More Like This',   count: 8 },
  { title: 'Same Director',    count: 6 },
  { title: 'Recommended by AI',count: 8 },
];

const SEASONS = [1, 2, 3];
const EPISODES = Array.from({ length: 8 }).map((_, i) => ({
  num: i + 1,
  title: ['The Gantry', 'A Nest of Vipers', 'Defiant Jazz', 'Hide and Seek', 'The Reformed', 'Trojan Horse', 'Cold Harbor', 'Macrodata Refinement'][i],
  runtime: `${44 + (i % 4) * 3}m`,
  airDate: `Feb ${(i + 1) * 3}, 2026`,
  progress: i === 2 ? 45 : i < 2 ? 100 : 0,
}));

interface AIPrefs {
  auto: boolean;
  prefer4K: boolean;
  preferSmall: boolean;
  preferSubs: boolean;
  avoidLowHealth: boolean;
}

export function DetailsOverlay({ item, onClose }: { item: DetailsItem; onClose: () => void }) {
  const [askInput, setAskInput] = useState('');
  const [askResponse, setAskResponse] = useState<string | null>(null);
  const [trailerOpen, setTrailerOpen] = useState(false);
  const [watchlisted, setWatchlisted] = useState(false);
  const [watched, setWatched] = useState(false);
  const [season, setSeason] = useState(2);
  const [selectedSource, setSelectedSource] = useState<string | null>('Plex');
  const [prefs, setPrefs] = useState<AIPrefs>({ auto: true, prefer4K: true, preferSmall: false, preferSubs: true, avoidLowHealth: true });

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== 'Escape') return;
      if (trailerOpen) return setTrailerOpen(false);
      onClose();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [trailerOpen, onClose]);

  const handleAsk = (q: string) => {
    setAskInput('');
    setAskResponse(`AI: ${q.startsWith('Spoiler') ? 'A son of nobility navigates a desert world ruled by spice, prophecy, and politics. No spoilers — go in fresh.' : `Here is your answer about "${q}" for ${item.title}.`}`);
  };

  return (
    <div className="fixed inset-0 z-50 bg-[#04060d] overflow-y-auto">
      {/* Top hero */}
      <div className="relative" style={{ height: 480 }}>
        <ImageWithFallback src={item.backdropUrl ?? SIMILAR_POSTERS[0]} alt={item.title} className="absolute inset-0 w-full h-full object-cover" />
        <div className="absolute inset-0 bg-gradient-to-r from-slate-950 via-slate-950/85 to-slate-950/30" />
        <div className="absolute inset-0 bg-gradient-to-t from-slate-950 via-transparent to-slate-950/60" />
        <div className="absolute inset-0" style={{ background: 'radial-gradient(ellipse at 80% 30%, rgba(34,211,238,0.10) 0%, transparent 60%)' }} />

        {/* Top bar */}
        <div className="absolute top-0 left-0 right-0 flex items-center justify-between p-4 z-10">
          <button onClick={onClose} className="flex items-center gap-1.5 px-2.5 py-1 rounded-full bg-slate-950/70 hover:bg-slate-900 border border-slate-700/60 text-slate-200 text-xs backdrop-blur">
            <ChevronLeft size={13} /> Back
          </button>
          <button onClick={onClose} className="p-1.5 rounded-full bg-slate-950/70 hover:bg-rose-500/20 border border-slate-700/60 hover:border-rose-400/40 text-slate-200 hover:text-rose-200 backdrop-blur">
            <X size={14} />
          </button>
        </div>

        {/* Hero content */}
        <div className="relative h-full flex items-end p-6 gap-5 z-[5]">
          {/* Poster */}
          <div className="hidden md:block flex-shrink-0 w-[200px] aspect-[2/3] rounded-xl overflow-hidden border border-cyan-400/30 ring-1 ring-cyan-500/20 shadow-2xl shadow-cyan-500/10 -mb-12">
            <ImageWithFallback src={item.posterUrl ?? SIMILAR_POSTERS[1]} alt={item.title} className="w-full h-full object-cover" />
          </div>
          {/* Info */}
          <div className="flex-1 min-w-0 max-w-3xl">
            <div className="flex items-center gap-2 mb-2">
              <span className="px-2 py-0.5 rounded-md bg-violet-500/15 border border-violet-400/30 text-violet-200 text-[10px]">{item.type === 'TV' ? 'TV Show' : 'Movie'}</span>
              {item.cert && <span className="px-2 py-0.5 rounded-md bg-slate-900/70 border border-slate-700/60 text-slate-300 text-[10px]">{item.cert}</span>}
              {item.quality && <span className="px-2 py-0.5 rounded-md bg-cyan-500/15 border border-cyan-400/40 text-cyan-200 text-[10px]">{item.quality}</span>}
            </div>
            <div className="text-slate-50" style={{ fontSize: 36, letterSpacing: '-0.01em', textShadow: '0 2px 24px rgba(0,0,0,0.7)' }}>{item.title}</div>
            <div className="flex items-center gap-3 mt-1.5 text-slate-300 text-xs">
              <span>{item.year}</span>
              <span>·</span>
              <span>{item.type === 'TV' ? (item.episodeLength ?? '55m / ep') : (item.runtime ?? '2h 38m')}</span>
              <span>·</span>
              <span>{item.genres.join(' · ')}</span>
            </div>

            {/* Ratings row */}
            <div className="flex flex-wrap items-center gap-2 mt-3">
              {item.tmdb !== undefined && <RatingChip label="TMDb" value={`${item.tmdb.toFixed(1)}`} color="#01b4e4" />}
              {item.imdb !== undefined && <RatingChip label="IMDb" value={`${item.imdb.toFixed(1)}`} color="#f5c518" />}
              {item.rt   !== undefined && <RatingChip label="RT"   value={`${item.rt}%`}            color="#fa320a" />}
              {item.user !== undefined && <RatingChip label="You"  value={`${item.user.toFixed(1)}`} color="#22d3ee" icon />}
            </div>

            {/* Genres */}
            <div className="flex flex-wrap gap-1.5 mt-2.5">
              {item.genres.map((g) => (
                <span key={g} className="px-2 py-0.5 rounded-full bg-slate-900/70 border border-slate-700/60 text-slate-300 text-[10px]">{g}</span>
              ))}
            </div>

            <p className="text-slate-300/90 text-sm mt-3 max-w-[640px] leading-relaxed line-clamp-3">{item.description}</p>

            {/* Buttons */}
            <div className="flex items-center gap-2 mt-4">
              <button className="px-3 py-1.5 rounded-full bg-cyan-500 hover:bg-cyan-400 text-slate-950 text-xs flex items-center gap-1.5 shadow-lg shadow-cyan-500/30">
                <Play size={12} fill="currentColor" /> Play
              </button>
              <button onClick={() => setTrailerOpen(true)} className="px-3 py-1.5 rounded-full bg-slate-900/80 hover:bg-slate-800 border border-slate-700/60 text-slate-200 text-xs flex items-center gap-1.5">
                <Film size={11} /> Trailer
              </button>
              <button onClick={() => setWatchlisted(!watchlisted)}
                className={`px-3 py-1.5 rounded-full text-xs flex items-center gap-1.5 border ${watchlisted ? 'bg-cyan-500/15 border-cyan-400/40 text-cyan-200' : 'bg-slate-900/80 hover:bg-slate-800 border-slate-700/60 text-slate-200'}`}>
                {watchlisted ? <Check size={11} /> : <Plus size={11} />} {watchlisted ? 'Watchlisted' : 'Watchlist'}
              </button>
              <button onClick={() => setWatched(!watched)}
                className={`px-3 py-1.5 rounded-full text-xs flex items-center gap-1.5 border ${watched ? 'bg-emerald-500/15 border-emerald-400/40 text-emerald-200' : 'bg-slate-900/80 hover:bg-slate-800 border-slate-700/60 text-slate-200'}`}>
                <Eye size={11} /> {watched ? 'Watched' : 'Mark Seen'}
              </button>
              <button className="p-1.5 rounded-full bg-slate-900/80 hover:bg-slate-800 border border-slate-700/60 text-slate-200">
                <Share2 size={11} />
              </button>
            </div>
          </div>
        </div>
      </div>

      {/* Body */}
      <div className="px-6 pt-16 pb-12 max-w-[1200px] mx-auto space-y-8">
        {/* AI Assistant */}
        <Section title="Ask AI" icon={<Sparkles size={13} className="text-violet-300" />}>
          <div className="rounded-xl border border-violet-400/20 bg-gradient-to-br from-slate-900/80 via-violet-950/30 to-slate-900/80 backdrop-blur-md p-4 ring-1 ring-violet-500/10">
            <p className="text-slate-300 text-xs">Ask for a spoiler-free summary, best stream, cast info, parental guide, similar titles, or whether it’s worth watching.</p>
            <div className="flex flex-wrap gap-1.5 mt-3">
              {QUICK_PROMPTS.map((q) => (
                <button key={q} onClick={() => handleAsk(q)} className="px-2.5 py-1 rounded-full bg-violet-500/10 hover:bg-violet-500/20 border border-violet-400/30 text-violet-200 text-[11px]">
                  {q}
                </button>
              ))}
            </div>
            <div className="flex items-center gap-2 mt-3">
              <div className="flex-1 flex items-center gap-2 px-3 py-1.5 rounded-lg bg-slate-950/70 border border-slate-700/60">
                <Sparkles size={11} className="text-violet-300" />
                <input value={askInput} onChange={(e) => setAskInput(e.target.value)}
                  onKeyDown={(e) => { if (e.key === 'Enter' && askInput.trim()) handleAsk(askInput); }}
                  placeholder="Ask anything about this title…"
                  className="flex-1 bg-transparent text-xs text-slate-200 placeholder:text-slate-500 outline-none" />
              </div>
              <button onClick={() => askInput.trim() && handleAsk(askInput)}
                className="px-3 py-1.5 rounded-lg bg-violet-500 hover:bg-violet-400 text-slate-950 text-xs flex items-center gap-1.5">
                <Send size={11} /> Send
              </button>
            </div>
            {askResponse && (
              <div className="mt-3 p-3 rounded-lg bg-slate-950/70 border border-violet-400/30 text-slate-200 text-xs leading-relaxed">{askResponse}</div>
            )}
          </div>
        </Section>

        {/* TV Episodes */}
        {item.type === 'TV' && (
          <Section title="Episodes" icon={<Play size={13} className="text-cyan-300" />}>
            <div className="rounded-xl border border-cyan-400/15 bg-slate-900/60 backdrop-blur-md p-4">
              <div className="flex items-center justify-between flex-wrap gap-2 mb-3">
                <div className="flex items-center gap-1.5">
                  {SEASONS.map((s) => (
                    <button key={s} onClick={() => setSeason(s)}
                      className={`px-2.5 py-1 rounded-full text-[11px] border ${season === s ? 'bg-cyan-500/15 text-cyan-200 border-cyan-400/40' : 'bg-slate-900/70 text-slate-300 border-slate-700/60 hover:bg-slate-800'}`}>
                      Season {s}
                    </button>
                  ))}
                </div>
                <div className="flex items-center gap-1.5">
                  <SmallBtn>Episode Guide</SmallBtn>
                  <SmallBtn>AI Recap</SmallBtn>
                  <SmallBtn>Spoiler-free Recap</SmallBtn>
                </div>
              </div>
              <div className="space-y-1.5">
                {EPISODES.map((ep) => (
                  <div key={ep.num} className="flex items-center gap-3 px-3 py-2 rounded-lg bg-slate-950/60 hover:bg-slate-800/60 border border-slate-700/40 group">
                    <div className="w-10 text-slate-500 text-[10px] flex-shrink-0">S{season}·E{ep.num}</div>
                    <div className="flex-1 min-w-0">
                      <div className="text-slate-100 text-xs truncate">{ep.title}</div>
                      <div className="text-slate-500 text-[10px]">{ep.runtime} · {ep.airDate}</div>
                    </div>
                    {ep.progress > 0 && ep.progress < 100 && (
                      <div className="hidden md:block w-24 h-1 rounded-full bg-slate-800 overflow-hidden flex-shrink-0">
                        <div className="h-full bg-cyan-400" style={{ width: `${ep.progress}%` }} />
                      </div>
                    )}
                    {ep.progress === 100 && <Check size={12} className="text-emerald-400 flex-shrink-0" />}
                    <button className="px-2 py-1 rounded-md bg-cyan-500/90 hover:bg-cyan-400 text-slate-950 text-[10px] flex items-center gap-1">
                      <Play size={10} fill="currentColor" /> Play
                    </button>
                    <button className="px-2 py-1 rounded-md bg-slate-800/80 hover:bg-slate-700 border border-slate-600/60 text-slate-200 text-[10px]">Recap</button>
                  </div>
                ))}
              </div>
            </div>
          </Section>
        )}

        {/* Sources */}
        <Section title="Sources" icon={<Zap size={13} className="text-cyan-300" />}>
          {/* AI Stream Selection */}
          <div className="rounded-xl border border-cyan-400/15 bg-slate-900/60 backdrop-blur-md p-3 mb-3">
            <div className="flex items-center gap-2 text-cyan-200 text-[11px] mb-2">
              <Sparkles size={11} /> AI Stream Selection
            </div>
            <div className="flex flex-wrap gap-1.5">
              <Toggle on={prefs.auto} onClick={() => setPrefs({ ...prefs, auto: !prefs.auto })}>Auto Pick Best Source</Toggle>
              <Toggle on={prefs.prefer4K} onClick={() => setPrefs({ ...prefs, prefer4K: !prefs.prefer4K })}>Prefer 4K</Toggle>
              <Toggle on={prefs.preferSmall} onClick={() => setPrefs({ ...prefs, preferSmall: !prefs.preferSmall })}>Prefer smallest file</Toggle>
              <Toggle on={prefs.preferSubs} onClick={() => setPrefs({ ...prefs, preferSubs: !prefs.preferSubs })}>Prefer subtitles</Toggle>
              <Toggle on={prefs.avoidLowHealth} onClick={() => setPrefs({ ...prefs, avoidLowHealth: !prefs.avoidLowHealth })}>Avoid low health</Toggle>
            </div>
          </div>

          <div className="space-y-1.5">
            {SOURCES.map((s) => {
              const selected = selectedSource === s.name;
              const statusColor =
                s.status === 'Available'      ? 'bg-emerald-500/15 text-emerald-300 border-emerald-400/30'
                : s.status === 'Premium'      ? 'bg-amber-500/15 text-amber-200 border-amber-400/30'
                : s.status === 'Buffer risk'  ? 'bg-rose-500/15 text-rose-200 border-rose-400/30'
                : 'bg-slate-700/40 text-slate-300 border-slate-600/50';
              return (
                <div key={s.name}
                  onClick={() => setSelectedSource(s.name)}
                  className={`flex items-center gap-3 px-3 py-2 rounded-lg border cursor-pointer transition-colors ${
                    selected ? 'bg-cyan-500/10 border-cyan-400/40 ring-1 ring-cyan-500/20' : 'bg-slate-950/60 border-slate-700/40 hover:bg-slate-800/60'
                  }`}>
                  <div className="flex items-center gap-2 min-w-[140px]">
                    <span className="w-6 h-6 rounded-md bg-slate-800 border border-slate-600/60 flex items-center justify-center text-slate-200 text-[11px]">{s.name[0]}</span>
                    <div className="min-w-0">
                      <div className="text-slate-100 text-xs truncate">{s.name}</div>
                      {s.recommended && (
                        <div className="flex items-center gap-1 text-[9px] text-violet-300">
                          <Sparkles size={8} /> Recommended
                        </div>
                      )}
                    </div>
                  </div>
                  <div className="flex-1 min-w-0">
                    <div className="text-slate-300 text-[11px] truncate">{s.stream}</div>
                    <div className="text-slate-500 text-[10px] truncate">
                      {s.quality} · {s.size} · {s.audio} · {s.lang} · Subs {s.subs}
                    </div>
                  </div>
                  <div className="hidden lg:flex items-center gap-2 flex-shrink-0">
                    <div className="text-[10px] text-slate-400">Health</div>
                    <div className="w-16 h-1.5 rounded-full bg-slate-800 overflow-hidden">
                      <div className={`h-full ${s.health > 90 ? 'bg-emerald-400' : s.health > 75 ? 'bg-cyan-400' : 'bg-amber-400'}`} style={{ width: `${s.health}%` }} />
                    </div>
                    <div className="text-[10px] text-slate-400">{s.reliability}%</div>
                  </div>
                  <span className={`px-2 py-0.5 rounded-full text-[9px] border flex items-center gap-1 ${statusColor}`}>
                    {s.status === 'Premium' && <Crown size={8} />}
                    {s.status === 'Buffer risk' && <AlertCircle size={8} />}
                    {s.status}
                  </span>
                  <button onClick={(e) => e.stopPropagation()} className="px-2 py-1 rounded-md bg-cyan-500/90 hover:bg-cyan-400 text-slate-950 text-[10px] flex items-center gap-1">
                    <Play size={9} fill="currentColor" /> Play
                  </button>
                  <button onClick={(e) => e.stopPropagation()} className="p-1 rounded-md bg-slate-800/80 hover:bg-slate-700 border border-slate-600/60 text-slate-300" title="Copy link">
                    <Copy size={10} />
                  </button>
                  <button onClick={(e) => e.stopPropagation()} className="p-1 rounded-md bg-slate-800/80 hover:bg-slate-700 border border-slate-600/60 text-slate-300" title="Details">
                    <Info size={10} />
                  </button>
                </div>
              );
            })}
          </div>
        </Section>

        {/* Cast */}
        <Section title="Cast" icon={null} action={<button className="text-cyan-300 text-[11px] hover:text-cyan-200">View Full Cast</button>}>
          <div className="flex gap-2.5 overflow-x-auto pb-2 scrollbar-hide">
            {CAST.map((c) => (
              <div key={c.name} className="flex-shrink-0 w-24 text-center">
                <div className="w-24 h-24 rounded-xl overflow-hidden ring-1 ring-slate-700/50">
                  <ImageWithFallback src={c.img} alt={c.name} className="w-full h-full object-cover" />
                </div>
                <div className="mt-1.5 text-slate-100 text-[11px] truncate">{c.name}</div>
                <div className="text-slate-500 text-[10px] truncate">{c.char}</div>
              </div>
            ))}
          </div>
        </Section>

        {/* Similar shelves */}
        {SHELVES.map((shelf) => (
          <Section key={shelf.title} title={shelf.title} icon={null} action={<button className="text-cyan-300 text-[11px] hover:text-cyan-200">View All</button>}>
            <div className="flex gap-2.5 overflow-x-auto pb-2 scrollbar-hide">
              {Array.from({ length: shelf.count }).map((_, i) => (
                <div key={i} className="flex-shrink-0 w-[130px]">
                  <div className="aspect-[2/3] rounded-xl overflow-hidden ring-1 ring-slate-700/40 hover:ring-cyan-400/60 cursor-pointer transition-all hover:-translate-y-1">
                    <ImageWithFallback src={SIMILAR_POSTERS[(i + shelf.title.length) % SIMILAR_POSTERS.length]} alt="" className="w-full h-full object-cover" />
                  </div>
                  <div className="mt-1.5 text-slate-200 text-[11px] truncate">Title {i + 1}</div>
                  <div className="text-slate-500 text-[10px]">2024 · Sci-Fi</div>
                </div>
              ))}
            </div>
          </Section>
        ))}

        {/* Metadata */}
        <Section title="Details" icon={null}>
          <div className="rounded-xl border border-slate-700/40 bg-slate-900/50 backdrop-blur-md p-4 grid grid-cols-2 md:grid-cols-3 gap-x-6 gap-y-2 text-xs">
            <Meta label="Director"  value={item.director  ?? 'Denis Villeneuve'} />
            <Meta label="Writers"   value={(item.writers  ?? ['Jon Spaihts', 'Denis Villeneuve']).join(', ')} />
            <Meta label="Release"   value={item.releaseDate ?? 'Mar 1, 2024'} />
            <Meta label="Country"   value={item.country   ?? 'United States'} />
            <Meta label="Language"  value={item.language  ?? 'English'} />
            <Meta label="Studio"    value={item.studio    ?? 'Legendary · Warner Bros.'} />
            <Meta label="Runtime"   value={item.runtime   ?? '2h 46m'} />
            <Meta label="Budget"    value={item.budget    ?? '$190M'} />
            <Meta label="Revenue"   value={item.revenue   ?? '$711M worldwide'} />
            <Meta label="Provider"  value="TMDb · IMDb · Rotten Tomatoes" />
          </div>
        </Section>
      </div>

      {/* Trailer modal */}
      {trailerOpen && (
        <div className="fixed inset-0 z-[60] bg-black/85 backdrop-blur-sm flex items-center justify-center p-6" onClick={() => setTrailerOpen(false)}>
          <div className="relative w-full max-w-4xl rounded-2xl border border-cyan-400/30 bg-slate-950 overflow-hidden ring-1 ring-cyan-500/20" onClick={(e) => e.stopPropagation()}>
            <div className="flex items-center justify-between px-4 py-2.5 border-b border-slate-800">
              <div>
                <div className="text-slate-100 text-sm">{item.title} · Official Trailer</div>
                <div className="text-slate-500 text-[10px]">Source · YouTube · TMDb</div>
              </div>
              <div className="flex items-center gap-1.5">
                <SmallBtn><Plus size={10} /> Watchlist</SmallBtn>
                <button onClick={() => setTrailerOpen(false)} className="p-1.5 rounded-md hover:bg-rose-500/20 text-slate-300 hover:text-rose-200">
                  <X size={14} />
                </button>
              </div>
            </div>
            <div className="aspect-video bg-black flex items-center justify-center relative">
              <ImageWithFallback src={item.backdropUrl ?? SIMILAR_POSTERS[0]} alt="" className="w-full h-full object-cover opacity-50" />
              <div className="absolute inset-0 flex items-center justify-center">
                <button className="w-16 h-16 rounded-full bg-cyan-500/90 hover:bg-cyan-400 text-slate-950 flex items-center justify-center shadow-2xl shadow-cyan-500/40">
                  <Play size={28} fill="currentColor" />
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

// ============================================================
// helpers
// ============================================================
function Section({ title, icon, action, children }: { title: string; icon: React.ReactNode; action?: React.ReactNode; children: React.ReactNode }) {
  return (
    <div>
      <div className="flex items-center justify-between mb-2.5">
        <div className="flex items-center gap-1.5 text-slate-200 text-sm">
          {icon} <span>{title}</span>
        </div>
        {action}
      </div>
      {children}
    </div>
  );
}

function RatingChip({ label, value, color, icon }: { label: string; value: string; color: string; icon?: boolean }) {
  return (
    <div className="px-2 py-1 rounded-md bg-slate-950/70 border border-slate-700/60 backdrop-blur flex items-center gap-1.5">
      <span className="text-[9px] tracking-wider" style={{ color }}>{label}</span>
      <span className="text-slate-100 text-xs flex items-center gap-1">
        {icon && <Star size={9} fill="currentColor" className="text-amber-300" />}
        {value}
      </span>
    </div>
  );
}

function Toggle({ on, onClick, children }: { on: boolean; onClick: () => void; children: React.ReactNode }) {
  return (
    <button onClick={onClick}
      className={`px-2.5 py-1 rounded-full text-[11px] flex items-center gap-1.5 border transition-colors ${
        on ? 'bg-cyan-500/15 text-cyan-200 border-cyan-400/40' : 'bg-slate-900/70 text-slate-300 border-slate-700/60 hover:bg-slate-800'
      }`}>
      <span className={`w-2 h-2 rounded-full ${on ? 'bg-cyan-400' : 'bg-slate-600'}`} />
      {children}
    </button>
  );
}

function SmallBtn({ children }: { children: React.ReactNode }) {
  return (
    <button className="px-2.5 py-1 rounded-md bg-slate-900/70 hover:bg-slate-800 border border-slate-700/60 text-slate-200 text-[11px] flex items-center gap-1">
      {children}
    </button>
  );
}

function Meta({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col">
      <span className="text-slate-500 text-[10px] tracking-wider uppercase">{label}</span>
      <span className="text-slate-200">{value}</span>
    </div>
  );
}

export default DetailsOverlay;
