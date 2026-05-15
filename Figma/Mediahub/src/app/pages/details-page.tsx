import { useEffect, useState, useCallback, useRef } from 'react';
import { useNavigate, useParams, useLocation } from 'react-router';
import {
  X, Play, Film, Plus, Eye, Share2, Star, ChevronLeft, Sparkles, Send,
  Check, Copy, Info, Crown, Zap, Loader2,
} from 'lucide-react';
import { ImageWithFallback } from '../components/figma/ImageWithFallback';
import { createPortal } from 'react-dom';

// â”€â”€ Types â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

interface CinemetaMeta {
  id: string;
  name: string;
  type: string;
  poster?: string;
  background?: string;
  description?: string;
  releaseInfo?: string;
  runtime?: string;
  imdbRating?: string;
  genres?: string[];
  cast?: string[];
  director?: string | string[];
  writer?: string | string[];
  country?: string;
  language?: string;
  trailers?: { source: string; type: string }[];
  videos?: CinemetaVideo[];
}

interface CinemetaVideo {
  id: string;
  title?: string;
  season?: number;
  episode?: number;
  overview?: string;
  thumbnail?: string;
  released?: string;
  runtime?: string;
}

interface AtlasStream {
  sourceId: string;
  name: string;
  providerId: string;
  providerName: string;
  urlOrPath: string;
  quality: string;
  requiresDebrid: boolean;
  isInfoOnly: boolean;
  isPlayable: boolean;
  rank: number;
  metadata: Record<string, string>;
  sizeText: string;
  seedersText: string;
}

interface SimilarItem {
  id: string;
  imdbId?: string;
  title: string;
  poster?: string;
  year?: number | string;
  type?: string;
}

interface AIPrefs {
  auto: boolean;
  prefer4K: boolean;
  preferSmall: boolean;
  preferSubs: boolean;
  avoidLowHealth: boolean;
}

const QUICK_PROMPTS = [
  'Spoiler-free summary', 'Is it worth watching?', 'Find best source',
  'Parental guide', 'Similar titles', 'Cast highlights', 'Trailer breakdown',
];

const YT_API_KEY = (window as any).__ATLAS_YT_KEY ?? import.meta.env.VITE_YT_API_KEY ?? '';

// â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

function postBridge(msg: object) {
  try { (window as any).chrome?.webview?.postMessage(msg); } catch { /* no bridge */ }
}

function toArray(val: string | string[] | undefined): string[] {
  if (!val) return [];
  return Array.isArray(val) ? val : [val];
}

function getInitials(name: string): string {
  return name.split(' ').filter(Boolean).slice(0, 2).map((p) => p[0].toUpperCase()).join('');
}

function extractYouTubeVideoId(url: string): string | null {
  const m = url.match(/(?:youtube\.com\/watch\?v=|youtu\.be\/)([^&?/]{11})/);
  return m ? m[1] : null;
}

async function searchYouTubeTrailer(title: string, year: string, mediaType: string): Promise<string | null> {
  const query = encodeURIComponent(`${title} ${year} ${mediaType === 'series' ? 'tv series' : 'movie'} official trailer`);
  if (YT_API_KEY) {
    try {
      const res = await fetch(
        `https://www.googleapis.com/youtube/v3/search?part=snippet&q=${query}&type=video&videoEmbeddable=true&key=${YT_API_KEY}&maxResults=1`,
      );
      const data = await res.json();
      const vid = data?.items?.[0]?.id?.videoId;
      if (vid) return vid as string;
    } catch { /* fall through */ }
  }
  for (const base of ['https://inv.riverside.rocks', 'https://yewtu.be']) {
    try {
      const res = await fetch(`${base}/api/v1/search?q=${query}&type=video&fields=videoId`, { signal: AbortSignal.timeout(6000) });
      if (!res.ok) continue;
      const data = await res.json();
      if (Array.isArray(data) && data[0]?.videoId) return data[0].videoId as string;
    } catch { continue; }
  }
  return null;
}

function mapBridgeTypeLocal(type?: string): 'Movie' | 'TV' {
  if (!type) return 'Movie';
  const t = type.toLowerCase();
  if (t === 'series' || t === 'channel' || t === 'tv') return 'TV';
  return 'Movie';
}

// â”€â”€ Trailer Modal â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

function TrailerModal({ videoId, title, onClose }: { videoId: string; title: string; onClose: () => void }) {
  const embedUrl = `https://www.youtube.com/embed/${videoId}?autoplay=1&rel=0&modestbranding=1`;
  return createPortal(
    <div className="fixed inset-0 z-[9999] flex items-center justify-center bg-black/85 backdrop-blur-sm" onClick={onClose}>
      <div
        className="relative w-[860px] max-w-[92vw] rounded-xl overflow-hidden shadow-2xl border border-slate-700/60"
        style={{ aspectRatio: '16/9' }}
        onClick={(e) => e.stopPropagation()}
      >
        <iframe
          src={embedUrl}
          title={`${title} Trailer`}
          className="w-full h-full"
          allow="autoplay; encrypted-media; fullscreen"
          allowFullScreen
        />
        <button
          onClick={onClose}
          className="absolute top-2 right-2 p-1.5 rounded-full bg-black/70 hover:bg-black text-white border border-white/20"
        >
          <X size={16} />
        </button>
      </div>
    </div>,
    document.body,
  );
}

// â”€â”€ Main Component â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

export function DetailsPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const location = useLocation();

  // Item data from navigation state (passed by ServerCard, CarouselOverlay, FeaturedPanel, etc.)
  const stateItem = (location.state as any)?.item as Record<string, any> | undefined;

  // â”€â”€ UI state â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
  const [askInput, setAskInput] = useState('');
  const [askResponse, setAskResponse] = useState<string | null>(null);
  const [trailerOpen, setTrailerOpen] = useState(false);
  const [trailerVideoId, setTrailerVideoId] = useState<string | null>(null);
  const [trailerLoading, setTrailerLoading] = useState(false);
  const [watchlisted, setWatchlisted] = useState(false);
  const [watched, setWatched] = useState(false);
  const [season, setSeason] = useState(1);
  const [selectedSource, setSelectedSource] = useState<string | null>(null);
  const [prefs, setPrefs] = useState<AIPrefs>({ auto: true, prefer4K: true, preferSmall: false, preferSubs: true, avoidLowHealth: true });

  // â”€â”€ Data state â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
  const [meta, setMeta] = useState<CinemetaMeta | null>(null);
  const [metaLoading, setMetaLoading] = useState(false);
  const [streams, setStreams] = useState<AtlasStream[]>([]);
  const [streamsLoading, setStreamsLoading] = useState(false);
  const [similarItems, setSimilarItems] = useState<SimilarItem[]>([]);
  const [castImages, setCastImages] = useState<Record<string, string>>({});
  const requestedDetailKeyRef = useRef<string>('');

  // â”€â”€ Derived values â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
  const imdbId: string | undefined = stateItem?.imdbId ?? (id?.startsWith('tt') ? id : undefined);
  const rawType: string = stateItem?.type ?? '';
  const isTV = rawType.toLowerCase() === 'tv' || rawType === 'series' || meta?.type === 'series';
  const cinemetaType: 'movie' | 'series' = isTV ? 'series' : 'movie';
  const bridgeType: 'movie' | 'series' = (rawType.toLowerCase() === 'tv' || rawType.toLowerCase() === 'series') ? 'series' : 'movie';

  const title: string = meta?.name ?? stateItem?.title ?? id ?? '';
  const year: string = stateItem?.year ?? meta?.releaseInfo ?? '';
  const runtime: string = meta?.runtime ?? stateItem?.runtime ?? '';
  const genres: string[] = meta?.genres ?? (stateItem?.genres as string[] | undefined) ?? (stateItem?.genre ? [stateItem.genre as string] : []);
  const description: string = meta?.description ?? (stateItem?.description as string | undefined) ?? (stateItem?.overview as string | undefined) ?? '';
  const posterUrl: string = meta?.poster ?? stateItem?.posterUrl ?? '';
  const backdropUrl: string = meta?.background ?? stateItem?.backdropUrl ?? '';
  const imdbRating: number | undefined = meta?.imdbRating ? parseFloat(meta.imdbRating) : (typeof stateItem?.rating === 'number' ? stateItem.rating : undefined);
  const cast: string[] = meta?.cast ?? [];
  const directors: string[] = toArray(meta?.director);
  const writers: string[] = toArray(meta?.writer);
  const country: string = meta?.country ?? '';
  const language: string = meta?.language ?? '';

  const allEpisodes = meta?.videos ?? [];
  const seasons: number[] = [...new Set(allEpisodes.map((v) => v.season).filter((s): s is number => typeof s === 'number'))].sort((a, b) => a - b);
  const visibleEpisodes: CinemetaVideo[] = allEpisodes.filter((v) => v.season === season);

  // â”€â”€ Fetch Cinemeta metadata â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
  useEffect(() => {
    if (!imdbId) return;
    setMetaLoading(true);
    fetch(`https://v3-cinemeta.strem.io/meta/${cinemetaType}/${encodeURIComponent(imdbId)}.json`,
      { signal: AbortSignal.timeout(8000) })
      .then((r) => r.json())
      .then((data) => {
        if (data?.meta) {
          setMeta(data.meta);
          const trailer = (data.meta.trailers ?? []).find((t: any) => (t.type ?? '').toLowerCase() === 'trailer') ?? data.meta.trailers?.[0];
          if (trailer?.source) setTrailerVideoId(trailer.source as string);
          const vids: CinemetaVideo[] = data.meta.videos ?? [];
          const firstSeason = vids.map((v) => v.season).filter((s): s is number => typeof s === 'number').sort((a, b) => a - b)[0];
          if (firstSeason) setSeason(firstSeason);
        }
      })
      .catch(() => { /* silently ignore */ })
      .finally(() => setMetaLoading(false));
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [imdbId]);

  // â”€â”€ Fetch similar items from Cinemeta genre catalog â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
  useEffect(() => {
    const genre = genres[0];
    if (!genre) return;
    const enc = encodeURIComponent(genre);
    fetch(`https://v3-cinemeta.strem.io/catalog/${cinemetaType}/top/genre=${enc}.json`,
      { signal: AbortSignal.timeout(6000) })
      .then((r) => r.json())
      .then((data) => {
        if (Array.isArray(data?.metas)) {
          const items: SimilarItem[] = (data.metas as any[])
            .filter((m: any) => m.id !== imdbId)
            .slice(0, 10)
            .map((m: any) => ({
              id: m.id as string,
              imdbId: m.id as string,
              title: (m.name ?? m.title ?? '') as string,
              poster: m.poster as string | undefined,
              year: m.releaseInfo as string | undefined,
              type: m.type as string | undefined,
            }));
          setSimilarItems(items);
        }
      })
      .catch(() => { /* silently ignore */ });
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [genres[0], cinemetaType, imdbId]);

  // ── Fetch Wikipedia actor photos for cast ─────────────────────────────────
  useEffect(() => {
    if (cast.length === 0) return;
    setCastImages({});
    cast.slice(0, 12).forEach((name) => {
      fetch(
        `https://en.wikipedia.org/w/api.php?action=query&titles=${encodeURIComponent(name)}&prop=pageimages&format=json&pithumbsize=185&origin=*`,
        { signal: AbortSignal.timeout(5000) }
      )
        .then((r) => r.json())
        .then((data) => {
          const pages = data?.query?.pages;
          if (!pages) return;
          const page = Object.values(pages)[0] as any;
          const thumb = page?.thumbnail?.source as string | undefined;
          if (thumb) setCastImages((prev) => ({ ...prev, [name]: thumb }));
        })
        .catch(() => { /* ignore */ });
    });
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [cast.join(',')]);

  // ── Listen for streams from the C# bridge ────────────────────────────────────────────────────────────────────────────────────────
  useEffect(() => {
    const handler = (event: MessageEvent) => {
      try {
        if (!event.data) return;
        const msg = typeof event.data === 'string' ? JSON.parse(event.data) : event.data;
        if (msg?.type === 'atlas:streams:state') {
          const expectedMediaId = (imdbId ?? id ?? '').trim();
          const incomingMediaId = String(msg.mediaId ?? '').trim();
          if (expectedMediaId && incomingMediaId && !incomingMediaId.includes(expectedMediaId) && !expectedMediaId.includes(incomingMediaId)) {
            return;
          }

          setStreamsLoading(!!msg.isBusy);
          const srcs: AtlasStream[] = msg.sources ?? [];
          if (srcs.length > 0) {
            setStreams(srcs);
            setSelectedSource((prev) => prev ?? srcs[0]?.sourceId ?? null);
          } else if (!msg.isBusy) {
            setStreams([]);
          }
        }
      } catch { /* ignore parse errors */ }
    };
    (window as any).chrome?.webview?.addEventListener('message', handler);
    return () => {
      (window as any).chrome?.webview?.removeEventListener('message', handler);
    };
  }, [imdbId, id]);

  // Request streams for this item when we have an imdb id
  useEffect(() => {
    const mediaId = (imdbId ?? id ?? '').trim();
    if (!mediaId) return;

    const requestKey = `${mediaId}::${bridgeType}`;
    if (requestedDetailKeyRef.current === requestKey) return;
    requestedDetailKeyRef.current = requestKey;

    setStreamsLoading(true);
    setStreams([]);
    postBridge({
      type: 'servers.openDetail',
      payload: {
        metaId: mediaId,
        imdbId: imdbId ?? '',
        id: mediaId,
        title: title || mediaId,
        type: bridgeType,
      },
    });
  }, [imdbId, id, bridgeType, title]);

  // â”€â”€ Keyboard shortcuts â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== 'Escape') return;
      if (trailerOpen) { setTrailerOpen(false); return; }
      navigate(-1);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [trailerOpen, navigate]);

  // â”€â”€ Trailer handler â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
  const handleTrailerClick = useCallback(async () => {
    if (trailerVideoId) { setTrailerOpen(true); return; }
    const rawUrl = (stateItem?.trailerUrl as string | undefined) ?? '';
    if (rawUrl) {
      const vid = extractYouTubeVideoId(rawUrl);
      if (vid) { setTrailerVideoId(vid); setTrailerOpen(true); return; }
    }
    setTrailerLoading(true);
    const found = await searchYouTubeTrailer(title, year, cinemetaType);
    setTrailerLoading(false);
    if (found) { setTrailerVideoId(found); setTrailerOpen(true); }
  }, [trailerVideoId, stateItem, title, year, cinemetaType]);

  // -- Play handler: scroll to Sources section so user can pick a stream --
  const handlePlay = useCallback(() => {
    document.getElementById('sources-section')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }, []);

  // â”€â”€ AI ask â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
  const handleAsk = (q: string) => {
    setAskInput('');
    setAskResponse(
      q.toLowerCase().includes('spoiler')
        ? `AI: A spoiler-free overview of "${title}" - a must-watch for fans of the genre.`
        : `AI: Here's information about "${q}" for ${title}.`,
    );
  };

  // â”€â”€ Stream display helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
  function streamSubtitle(s: AtlasStream): string {
    const parts: string[] = [];
    if (s.providerName && s.providerName !== s.name) parts.push(s.providerName);
    if (s.sizeText) parts.push(s.sizeText);
    if (s.seedersText) parts.push(s.seedersText);
    return parts.join(' · ') || '-';
  }

  // â”€â”€ Render â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
  return (
    <div className="fixed inset-0 z-50 bg-[#04060d] overflow-y-auto">

      {/* Trailer modal */}
      {trailerOpen && trailerVideoId && (
        <TrailerModal videoId={trailerVideoId} title={title} onClose={() => setTrailerOpen(false)} />
      )}

      {/* â”€â”€ Hero â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ */}
      <div className="relative" style={{ height: 480 }}>
        {(backdropUrl || imdbId) ? (
          <ImageWithFallback
            src={backdropUrl || `https://images.metahub.space/background/medium/${imdbId}/img`}
            fallbackSrc={posterUrl || undefined}
            alt={title}
            className="absolute inset-0 w-full h-full object-cover"
          />
        ) : (
          <div className="absolute inset-0 bg-gradient-to-br from-slate-800 to-slate-900" />
        )}
        <div className="absolute inset-0 bg-gradient-to-r from-slate-950 via-slate-950/85 to-slate-950/30" />
        <div className="absolute inset-0 bg-gradient-to-t from-slate-950 via-transparent to-slate-950/60" />
        <div className="absolute inset-0" style={{ background: 'radial-gradient(ellipse at 80% 30%, rgba(34,211,238,0.10) 0%, transparent 60%)' }} />

        {/* Top bar */}
        <div className="absolute top-0 left-0 right-0 flex items-center justify-between p-4 z-10">
          <button onClick={() => navigate(-1)} className="flex items-center gap-1.5 px-2.5 py-1 rounded-full bg-slate-950/70 hover:bg-slate-900 border border-slate-700/60 text-slate-200 text-xs backdrop-blur">
            <ChevronLeft size={13} /> Back
          </button>
          <button onClick={() => navigate(-1)} className="p-1.5 rounded-full bg-slate-950/70 hover:bg-rose-500/20 border border-slate-700/60 hover:border-rose-400/40 text-slate-200 hover:text-rose-200 backdrop-blur">
            <X size={14} />
          </button>
        </div>

        {/* Hero content */}
        <div className="relative h-full flex items-end p-6 gap-5 z-[5]">
          {/* Poster */}
          {posterUrl && (
            <div className="hidden md:block flex-shrink-0 w-[200px] aspect-[2/3] rounded-xl overflow-hidden border border-cyan-400/30 ring-1 ring-cyan-500/20 shadow-2xl shadow-cyan-500/10 -mb-12">
              <ImageWithFallback src={posterUrl} alt={title} className="w-full h-full object-cover" />
            </div>
          )}

          {/* Info */}
          <div className="flex-1 min-w-0 max-w-3xl">
            <div className="flex items-center gap-2 mb-2">
              <span className="px-2 py-0.5 rounded-md bg-violet-500/15 border border-violet-400/30 text-violet-200 text-[10px]">
                {isTV ? 'TV Show' : 'Movie'}
              </span>
              {metaLoading && !meta && <Loader2 size={12} className="animate-spin text-slate-400" />}
            </div>

            <div className="text-slate-50" style={{ fontSize: 36, letterSpacing: '-0.01em', textShadow: '0 2px 24px rgba(0,0,0,0.7)' }}>{title || id}</div>

            <div className="flex items-center gap-3 mt-1.5 text-slate-300 text-xs flex-wrap">
              {year && <span>{year}</span>}
              {year && runtime && <span>·</span>}
              {runtime && <span>{runtime}</span>}
              {(year || runtime) && genres.length > 0 && <span>·</span>}
              {genres.length > 0 && <span>{genres.slice(0, 3).join(' · ')}</span>}
            </div>

            {imdbRating !== undefined && (
              <div className="flex flex-wrap items-center gap-2 mt-3">
                <RatingChip label="IMDb" value={`${imdbRating.toFixed(1)}`} color="#f5c518" />
              </div>
            )}

            {genres.length > 0 && (
              <div className="flex flex-wrap gap-1.5 mt-2.5">
                {genres.map((g) => (
                  <span key={g} className="px-2 py-0.5 rounded-full bg-slate-900/70 border border-slate-700/60 text-slate-300 text-[10px]">{g}</span>
                ))}
              </div>
            )}

            {description && (
              <p className="text-slate-300/90 text-sm mt-3 max-w-[640px] leading-relaxed line-clamp-3">{description}</p>
            )}

            {/* Action buttons */}
            <div className="flex items-center gap-2 mt-4 flex-wrap">
              <button
                onClick={handlePlay}
                className="px-3 py-1.5 rounded-full bg-cyan-500 hover:bg-cyan-400 text-slate-950 text-xs flex items-center gap-1.5 shadow-lg shadow-cyan-500/30"
              >
                <Play size={12} fill="currentColor" /> Play
              </button>
              <button
                onClick={handleTrailerClick}
                disabled={trailerLoading}
                className="px-3 py-1.5 rounded-full bg-slate-900/80 hover:bg-slate-800 border border-slate-700/60 text-slate-200 text-xs flex items-center gap-1.5 disabled:opacity-60"
              >
                {trailerLoading ? <Loader2 size={11} className="animate-spin" /> : <Film size={11} />}
                Trailer
              </button>
              <button
                onClick={() => setWatchlisted(!watchlisted)}
                className={`px-3 py-1.5 rounded-full text-xs flex items-center gap-1.5 border ${watchlisted ? 'bg-cyan-500/15 border-cyan-400/40 text-cyan-200' : 'bg-slate-900/80 hover:bg-slate-800 border-slate-700/60 text-slate-200'}`}
              >
                {watchlisted ? <Check size={11} /> : <Plus size={11} />} {watchlisted ? 'Watchlisted' : 'Watchlist'}
              </button>
              <button
                onClick={() => setWatched(!watched)}
                className={`px-3 py-1.5 rounded-full text-xs flex items-center gap-1.5 border ${watched ? 'bg-emerald-500/15 border-emerald-400/40 text-emerald-200' : 'bg-slate-900/80 hover:bg-slate-800 border-slate-700/60 text-slate-200'}`}
              >
                <Eye size={11} /> {watched ? 'Watched' : 'Mark Seen'}
              </button>
              <button className="p-1.5 rounded-full bg-slate-900/80 hover:bg-slate-800 border border-slate-700/60 text-slate-200">
                <Share2 size={11} />
              </button>
            </div>
          </div>
        </div>
      </div>

      {/* â”€â”€ Body â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ */}
      <div className="px-6 pt-16 pb-12 max-w-[1200px] mx-auto space-y-8">

        {/* AI Assistant */}
        <Section title="Ask AI" icon={<Sparkles size={13} className="text-violet-300" />}>
          <div className="rounded-xl border border-violet-400/20 bg-gradient-to-br from-slate-900/80 via-violet-950/30 to-slate-900/80 backdrop-blur-md p-4 ring-1 ring-violet-500/10">
            <p className="text-slate-300 text-xs">Ask for a spoiler-free summary, best source, cast info, parental guide, similar titles, or whether it's worth watching.</p>
            <div className="flex flex-wrap gap-1.5 mt-3">
              {QUICK_PROMPTS.map((q) => (
                <button key={q} onClick={() => handleAsk(q)} className="px-2.5 py-1 rounded-full bg-violet-500/10 hover:bg-violet-500/20 border border-violet-400/30 text-violet-200 text-[11px]">{q}</button>
              ))}
            </div>
            <div className="flex items-center gap-2 mt-3">
              <div className="flex-1 flex items-center gap-2 px-3 py-1.5 rounded-lg bg-slate-950/70 border border-slate-700/60">
                <Sparkles size={11} className="text-violet-300" />
                <input
                  value={askInput}
                  onChange={(e) => setAskInput(e.target.value)}
                  onKeyDown={(e) => { if (e.key === 'Enter' && askInput.trim()) handleAsk(askInput); }}
                  placeholder="Ask anything about this title..."
                  className="flex-1 bg-transparent text-xs text-slate-200 placeholder:text-slate-500 outline-none"
                />
              </div>
              <button
                onClick={() => askInput.trim() && handleAsk(askInput)}
                className="px-3 py-1.5 rounded-lg bg-violet-500 hover:bg-violet-400 text-slate-950 text-xs flex items-center gap-1.5"
              >
                <Send size={11} /> Send
              </button>
            </div>
            {askResponse && (
              <div className="mt-3 p-3 rounded-lg bg-slate-950/70 border border-violet-400/30 text-slate-200 text-xs leading-relaxed">{askResponse}</div>
            )}
          </div>
        </Section>

        {/* TV Episodes */}
        {isTV && (
          <Section title="Episodes" icon={<Play size={13} className="text-cyan-300" />}>
            <div className="rounded-xl border border-cyan-400/15 bg-slate-900/60 backdrop-blur-md p-4">
              {seasons.length > 0 ? (
                <>
                  <div className="flex items-center gap-1.5 flex-wrap mb-3">
                    {seasons.map((s) => (
                      <button
                        key={s}
                        onClick={() => setSeason(s)}
                        className={`px-2.5 py-1 rounded-full text-[11px] border ${season === s ? 'bg-cyan-500/15 text-cyan-200 border-cyan-400/40' : 'bg-slate-900/70 text-slate-300 border-slate-700/60 hover:bg-slate-800'}`}
                      >
                        Season {s}
                      </button>
                    ))}
                  </div>
                  <div className="space-y-1.5">
                    {visibleEpisodes.map((ep) => (
                      <div key={ep.id} className="flex items-center gap-3 px-3 py-2 rounded-lg bg-slate-950/60 hover:bg-slate-800/60 border border-slate-700/40">
                        <div className="w-10 text-slate-500 text-[10px] flex-shrink-0">S{ep.season}·E{ep.episode}</div>
                        <div className="flex-1 min-w-0">
                          <div className="text-slate-100 text-xs truncate">{ep.title ?? `Episode ${ep.episode}`}</div>
                          <div className="text-slate-500 text-[10px]">{[ep.runtime, ep.released].filter(Boolean).join(' · ')}</div>
                        </div>
                        <button
                          onClick={() => postBridge({ type: 'servers.playItem', payload: { id: ep.id, title: ep.title, mediaType: 'series' } })}
                          className="px-2 py-1 rounded-md bg-cyan-500/90 hover:bg-cyan-400 text-slate-950 text-[10px] flex items-center gap-1"
                        >
                          <Play size={10} fill="currentColor" /> Play
                        </button>
                      </div>
                    ))}
                  </div>
                </>
              ) : (
                <div className="text-slate-500 text-xs text-center py-4">
                  {metaLoading ? 'Loading episode guide...' : 'No episodes found for this series.'}
                </div>
              )}
            </div>
          </Section>
        )}


        {/* Cast */}
        {cast.length > 0 && (
          <Section title="Cast" icon={null}>
            <div className="flex gap-2.5 overflow-x-auto pb-2 scrollbar-hide">
              {cast.slice(0, 12).map((name) => (
                <div key={name} className="flex-shrink-0 w-24 text-center">
                  <div className="w-24 h-24 rounded-xl overflow-hidden ring-1 ring-slate-700/50 bg-gradient-to-br from-slate-800 to-slate-900 flex items-center justify-center">
                    {castImages[name] ? (
                      <img src={castImages[name]} alt={name} className="w-full h-full object-cover object-top" />
                    ) : (
                      <span className="text-slate-300 text-lg font-semibold">{getInitials(name)}</span>
                    )}
                  </div>
                  <div className="mt-1.5 text-slate-100 text-[11px] truncate">{name}</div>
                </div>
              ))}
            </div>
          </Section>
        )}

        {/* Similar titles */}
        {similarItems.length > 0 && (
          <Section
            title={`More ${genres[0] ?? ''} ${isTV ? 'Shows' : 'Movies'}`}
            icon={null}
            action={<button className="text-cyan-300 text-[11px] hover:text-cyan-200">View All</button>}
          >
            <div className="flex gap-2.5 overflow-x-auto pb-2 scrollbar-hide">
              {similarItems.map((sim) => (
                <div
                  key={sim.id}
                  className="flex-shrink-0 w-[130px] cursor-pointer"
                  onClick={() =>
                    navigate(`/details/${sim.id}`, {
                      state: {
                        item: {
                          id: sim.id,
                          imdbId: sim.imdbId,
                          title: sim.title,
                          year: String(sim.year ?? ''),
                          type: mapBridgeTypeLocal(sim.type),
                          posterUrl: sim.poster,
                          server: '',
                          hasMetadata: true,
                          hasArtwork: !!sim.poster,
                        },
                      },
                    })
                  }
                >
                  <div className="aspect-[2/3] rounded-xl overflow-hidden ring-1 ring-slate-700/40 hover:ring-cyan-400/60 transition-all hover:-translate-y-1">
                    <ImageWithFallback
                      src={sim.poster ?? `https://images.metahub.space/poster/medium/${sim.imdbId ?? sim.id}/img`}
                      alt={sim.title}
                      className="w-full h-full object-cover"
                    />
                  </div>
                  <div className="mt-1.5 text-slate-200 text-[11px] truncate">{sim.title}</div>
                  <div className="text-slate-500 text-[10px]">
                    {sim.year ? String(sim.year) : ''}{genres[0] ? ` · ${genres[0]}` : ''}
                  </div>
                </div>
              ))}
            </div>
          </Section>
        )}

        {/* Details metadata */}
        {(directors.length > 0 || writers.length > 0 || country || language || runtime) && (
          <Section title="Details" icon={null}>
            <div className="rounded-xl border border-slate-700/40 bg-slate-900/50 backdrop-blur-md p-4 grid grid-cols-2 md:grid-cols-3 gap-x-6 gap-y-2 text-xs">
              {directors.length > 0 && <Meta label="Director" value={directors.join(', ')} />}
              {writers.length > 0 && <Meta label="Writers" value={writers.join(', ')} />}
              {runtime && <Meta label="Runtime" value={runtime} />}
              {country && <Meta label="Country" value={country} />}
              {language && <Meta label="Language" value={language} />}
              {genres.length > 0 && <Meta label="Genres" value={genres.join(', ')} />}
            </div>
          </Section>
        )}


        <div id="sources-section" />
        {/* Sources */}
        <Section title="Sources" icon={<Zap size={13} className="text-cyan-300" />}>
          <div className="rounded-xl border border-cyan-400/15 bg-slate-900/60 backdrop-blur-md p-3 mb-3">
            <div className="flex items-center gap-2 text-cyan-200 text-[11px] mb-2">
              <Sparkles size={11} /> AI Stream Selection
            </div>
            <div className="flex flex-wrap gap-1.5">
              <Toggle on={prefs.auto} onClick={() => setPrefs({ ...prefs, auto: !prefs.auto })}>Auto Pick Best</Toggle>
              <Toggle on={prefs.prefer4K} onClick={() => setPrefs({ ...prefs, prefer4K: !prefs.prefer4K })}>Prefer 4K</Toggle>
              <Toggle on={prefs.preferSmall} onClick={() => setPrefs({ ...prefs, preferSmall: !prefs.preferSmall })}>Smallest File</Toggle>
              <Toggle on={prefs.preferSubs} onClick={() => setPrefs({ ...prefs, preferSubs: !prefs.preferSubs })}>Prefer Subtitles</Toggle>
              <Toggle on={prefs.avoidLowHealth} onClick={() => setPrefs({ ...prefs, avoidLowHealth: !prefs.avoidLowHealth })}>Avoid Low Health</Toggle>
            </div>
          </div>

          {streamsLoading && streams.length === 0 && (
            <div className="flex items-center gap-2 text-slate-400 text-xs py-4 px-3">
              <Loader2 size={13} className="animate-spin" /> Fetching streams...
            </div>
          )}

          {!streamsLoading && streams.length === 0 && (
            <div className="text-slate-500 text-xs px-3 py-4 rounded-xl border border-slate-700/40 bg-slate-900/40">
              No sources found. Open this title from the main library shelf to load streams.
            </div>
          )}

          {streams.length > 0 && (
            <div className="space-y-1.5">
              {streams.map((s) => {
                const selected = selectedSource === s.sourceId;
                return (
                  <div
                    key={s.sourceId}
                    onClick={() => setSelectedSource(s.sourceId)}
                    className={`flex items-center gap-3 px-3 py-2 rounded-lg border cursor-pointer transition-colors ${selected ? 'bg-cyan-500/10 border-cyan-400/40 ring-1 ring-cyan-500/20' : 'bg-slate-950/60 border-slate-700/40 hover:bg-slate-800/60'}`}
                  >
                    <div className="flex items-center gap-2 min-w-[130px]">
                      <span className="w-6 h-6 rounded-md bg-slate-800 border border-slate-600/60 flex items-center justify-center text-slate-200 text-[11px]">
                        {(s.providerName || s.name || '?')[0].toUpperCase()}
                      </span>
                      <div className="min-w-0">
                        <div className="text-slate-100 text-xs truncate">{s.name || s.providerName || 'Source'}</div>
                        {s.requiresDebrid && (
                          <div className="flex items-center gap-1 text-[9px] text-amber-300">
                            <Crown size={8} /> Debrid
                          </div>
                        )}
                      </div>
                    </div>
                    <div className="flex-1 min-w-0">
                      <div className="text-slate-300 text-[11px] truncate">{s.providerName || s.name}</div>
                      <div className="text-slate-500 text-[10px] truncate">{streamSubtitle(s)}</div>
                    </div>
                    {s.quality && (
                      <span className="px-2 py-0.5 rounded-full text-[9px] border bg-slate-700/40 text-slate-300 border-slate-600/50 flex-shrink-0">
                        {s.quality}
                      </span>
                    )}
                    {s.isPlayable && (
                      <button
                        onClick={(e) => {
                          e.stopPropagation();
                          postBridge({
                            type: 'servers.playSource',
                            payload: {
                              sourceId: s.sourceId,
                              urlOrPath: s.urlOrPath,
                              name: s.name || s.providerName || 'Source',
                            },
                          });
                        }}
                        className="px-2 py-1 rounded-md bg-cyan-500/90 hover:bg-cyan-400 text-slate-950 text-[10px] flex items-center gap-1"
                      >
                        <Play size={9} fill="currentColor" /> Play
                      </button>
                    )}
                    {s.isInfoOnly && (
                      <span className="px-2 py-0.5 rounded-full text-[9px] border bg-slate-800/60 text-slate-400 border-slate-600/40 flex items-center gap-1">
                        <Info size={8} /> Info
                      </span>
                    )}
                    <button
                      onClick={(e) => {
                        e.stopPropagation();
                        postBridge({
                          type: 'servers.copyLink',
                          payload: {
                            text: s.urlOrPath,
                            sourceId: s.sourceId,
                          },
                        });
                      }}
                      className="p-1 rounded-md bg-slate-800/80 hover:bg-slate-700 border border-slate-600/60 text-slate-300"
                      title="Copy link"
                    >
                      <Copy size={10} />
                    </button>
                  </div>
                );
              })}
            </div>
          )}
        </Section>

      </div>
    </div>
  );
}

// â”€â”€ Sub-components â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

function Section({
  title, icon, action, children,
}: {
  title: string;
  icon: React.ReactNode;
  action?: React.ReactNode;
  children: React.ReactNode;
}) {
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
    <button
      onClick={onClick}
      className={`px-2.5 py-1 rounded-full text-[11px] flex items-center gap-1.5 border transition-colors ${on ? 'bg-cyan-500/15 text-cyan-200 border-cyan-400/40' : 'bg-slate-900/70 text-slate-300 border-slate-700/60 hover:bg-slate-800'}`}
    >
      <span className={`w-2 h-2 rounded-full ${on ? 'bg-cyan-400' : 'bg-slate-600'}`} />
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

export default DetailsPage;
