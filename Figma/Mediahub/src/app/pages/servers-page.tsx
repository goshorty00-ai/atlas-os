import { useState, useMemo, useEffect, useRef, useCallback } from 'react';
import { Sparkles, ChevronDown, RefreshCw, ChevronLeft, ChevronRight } from 'lucide-react';
import { ServerShelf } from '../components/server-shelf';
import { CarouselOverlay } from '../components/carousel-overlay';
import { GridView } from '../components/grid-view';
import { MediaItem } from '../components/server-card';
import { FeaturedPanel, FeaturedItem } from '../components/featured-panel';

// ── Bridge types ─────────────────────────────────────────────────────────────

interface BridgeShelfItem {
  id: string;
  metaId?: string;
  imdbId?: string;
  title: string;
  poster?: string;
  coverUrl?: string;
  type?: string;
  year?: number | string;
  rating?: number;
  overview?: string;
  backdrop?: string;
  backdropUrl?: string;
  genres?: string[];
  runtimeMinutes?: number;
  trailerUrl?: string;
  releaseDate?: string;
  originalLanguage?: string;
}

interface BridgeShelf {
  key: string;
  title: string;
  type?: string;
  catalogId?: string;
  items: BridgeShelfItem[];
}

interface BridgeState {
  shelves: BridgeShelf[];
  serverOptions?: string[];
  selectedServer?: string;
  mode?: string;
}

// ── Helpers ──────────────────────────────────────────────────────────────────

function postBridge(msg: object) {
  try {
    (window as any).chrome?.webview?.postMessage(msg);
  } catch {
    // not in WebView2 context
  }
}

function mapBridgeType(type?: string): MediaItem['type'] {
  if (!type) return 'Movie';
  const t = type.toLowerCase();
  if (t === 'series' || t === 'channel' || t === 'tv') return 'TV';
  if (t === 'music video' || t === 'music') return 'Music Video';
  if (t === 'game') return 'Game';
  return 'Movie';
}

function mapBridgeItem(item: BridgeShelfItem, serverLabel: string, shelfType?: string): MediaItem {
  return {
    id: item.id,
    title: item.title,
    year: String(item.year ?? ''),
    type: mapBridgeType(item.type || shelfType),
    server: serverLabel,
    rating: item.rating,
    posterUrl: item.poster ?? item.coverUrl,
    hasMetadata: true,
    hasArtwork: !!(item.poster ?? item.coverUrl),
    runtime: item.runtimeMinutes ? `${item.runtimeMinutes}min` : undefined,
    genre: item.genres?.[0],
    description: item.overview,
  };
}

// Keywords that identify shelves likely to contain fresh/recent content
const FRESH_SHELF_KEYWORDS = [
  'now_playing', 'now playing', 'new release', 'new arrival', 'latest',
  'recent', 'calendar', 'upcoming', 'coming soon', 'trending', 'fresh',
  'in cinema', 'in theater', 'newly', 'just added', 'this week', 'this month',
];

function isFreshShelf(shelf: BridgeShelf): boolean {
  const id = (shelf.catalogId ?? shelf.key ?? '').toLowerCase();
  const title = (shelf.title ?? '').toLowerCase();
  return FRESH_SHELF_KEYWORDS.some(kw => id.includes(kw) || title.includes(kw));
}

function makeFeaturedFromBridge(
  shelves: BridgeShelf[],
  selectedServer: string
): FeaturedItem[] {
  const currentYear = new Date().getFullYear(); // 2026
  const recentYearCutoff = currentYear - 1;     // 2025 or newer counts as "recent"
  const tenDaysAgo = Date.now() - 10 * 24 * 60 * 60 * 1000;

  const seen = new Set<string>();
  // Buckets: prefer items from fresh shelves or with an exact releaseDate in last 10 days
  // Secondary: items from 2025+ by year
  const freshMovies: FeaturedItem[] = [];
  const freshTv: FeaturedItem[] = [];
  const recentYearMovies: FeaturedItem[] = [];
  const recentYearTv: FeaturedItem[] = [];

  for (const shelf of shelves) {
    const shelfFresh = isFreshShelf(shelf);
    for (const item of shelf.items) {
      const bg = item.backdropUrl ?? item.backdrop;
      if (!bg) continue;

      // Skip if the "backdrop" is actually just a poster URL (portrait, not landscape)
      const bgLower = bg.toLowerCase();
      if (bgLower.includes('/poster/') || bgLower.includes('poster-default') ||
          bgLower.includes('/thumbnail')) continue;

      // English-only: skip items with a known non-English original language
      const lang = (item.originalLanguage ?? '').toLowerCase().trim();
      if (lang && lang !== 'en') continue;

      // Deduplicate by id or title
      const key = ((item.id || item.title || '').toLowerCase()).trim();
      if (!key || seen.has(key)) continue;
      seen.add(key);

      const itemType = mapBridgeType(item.type);
      const fi: FeaturedItem = {
        id: item.id,
        imdbId: item.imdbId || (item.id?.startsWith('tt') ? item.id : undefined),
        title: item.title,
        backdropUrl: bg,
        rating: item.rating,
        runtime: item.runtimeMinutes ? `${Math.floor(item.runtimeMinutes / 60)}h ${item.runtimeMinutes % 60}m` : undefined,
        release: (() => {
          if (!item.releaseDate) return item.year ? String(item.year) : undefined;
          const d = new Date(item.releaseDate);
          const day = d.getUTCDate();
          const mon = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'][d.getUTCMonth()];
          const yr = d.getUTCFullYear();
          const label = `${day} ${mon} ${yr}`;
          return d.getTime() > Date.now() ? `Coming ${label}` : label;
        })(),
        genres: item.genres ?? [],
        source: selectedServer || 'Addon source',
        description: item.overview ?? '',
        primaryAction: 'Play',
        type: itemType === 'TV' ? 'TV' : 'Movie',
        trailerUrl: item.trailerUrl || `https://www.youtube.com/results?search_query=${encodeURIComponent((item.title || '') + ' ' + (itemType === 'TV' ? 'series trailer' : 'official trailer'))}`,
      };

      const releaseMs = item.releaseDate ? new Date(item.releaseDate).getTime() : 0;
      const hasExactRecent = releaseMs > 0 && releaseMs > tenDaysAgo;
      const itemYear = releaseMs > 0 ? new Date(item.releaseDate!).getFullYear() : (typeof item.year === 'number' ? item.year : parseInt(String(item.year || '0'), 10));
      const isYearRecent = itemYear >= recentYearCutoff;

      if (hasExactRecent || shelfFresh) {
        if (fi.type === 'TV') freshTv.push(fi); else freshMovies.push(fi);
      } else if (isYearRecent) {
        if (fi.type === 'TV') recentYearTv.push(fi); else recentYearMovies.push(fi);
      }
    }
  }

  // Pick best bucket for each type, fall through to year-recent if fresh is empty
  const pickMovies = freshMovies.length > 0 ? freshMovies : recentYearMovies;
  const pickTv = freshTv.length > 0 ? freshTv : recentYearTv;

  const MAX = 20;
  const half = Math.floor(MAX / 2);
  const movies = pickMovies.slice(0, half);
  const tv = pickTv.slice(0, half);

  // Interleave movie/TV so hero rotates through both types
  const result: FeaturedItem[] = [];
  const maxLen = Math.max(movies.length, tv.length);
  for (let i = 0; i < maxLen; i++) {
    if (i < movies.length) result.push(movies[i]);
    if (i < tv.length) result.push(tv[i]);
  }

  return result.slice(0, MAX);
}

type ViewState = 'shelves' | 'grid';

export function ServersPage() {
  const [viewState, setViewState] = useState<ViewState>('shelves');
  const [carouselOpen, setCarouselOpen] = useState(false);
  const [typeFilter, setTypeFilter] = useState<'All' | 'Movies' | 'TV'>('All');
  const [shelfFilter, setShelfFilter] = useState<string | null>(null);
  const [typeDropOpen, setTypeDropOpen] = useState(false);
  const [shelfDropOpen, setShelfDropOpen] = useState(false);
  const [currentShelf, setCurrentShelf] = useState<{ name: string; items: MediaItem[]; contentType?: string } | null>(null);
  const [bridgeState, setBridgeState] = useState<BridgeState | null>(null);
  const [timedOut, setTimedOut] = useState(false);
  const [featuredIndex, setFeaturedIndex] = useState(0);
  const hasShelvesRef = useRef(false);
  const autoRotateRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const [refreshing, setRefreshing] = useState(false);

  // Custom shelves created via bulk-select in grid view
  const loadCustomShelves = () => {
    try {
      const raw = JSON.parse(localStorage.getItem('atlas-custom-shelves-v1') ?? '{}') as Record<string, MediaItem[]>;
      return Object.entries(raw).map(([title, items]) => ({ title, items }));
    } catch { return []; }
  };
  const [customShelves, setCustomShelves] = useState<{ title: string; items: MediaItem[] }[]>(loadCustomShelves);

  // ── Bridge wiring ──────────────────────────────────────────────────────────
  useEffect(() => {
    const handler = (event: MessageEvent) => {
      if (!event.data) return;
      const msg = typeof event.data === 'string' ? JSON.parse(event.data) : event.data;
      if (msg?.type === 'servers.state') {
        const payload = msg.payload as BridgeState;
        if (payload) {
          // Cache globally so Shelf Creator can read even after message has already fired
          (window as any).__atlasBridgeState = payload;
          setBridgeState(payload);
          if ((payload.shelves?.length ?? 0) > 0) hasShelvesRef.current = true;
        }
      }
    };
    // Also re-apply when Shelf Creator saves new config, or when a custom shelf is created
    const onStorage = () => {
      setBridgeState((prev) => prev ? { ...prev } : prev);
      setCustomShelves(loadCustomShelves());
    };
    window.addEventListener('storage', onStorage);
    (window as any).chrome?.webview?.addEventListener('message', handler);
    // Seed from global cache immediately (no wait for bridge)
    const cached = (window as any).__atlasBridgeState as BridgeState | undefined;
    if (cached?.shelves?.length) {
      setBridgeState(cached);
      hasShelvesRef.current = true;
    }
    postBridge({ type: 'servers.ready' });
    postBridge({ type: 'servers.getState' });
    // Poll every 10s for 90s — streaming catalog addons (Netflix, Prime, Apple TV, Marvel)
    // can take 30-60s to return results after app start. 10s gaps let BuildServerShelvesAsync
    // complete without being cancelled by the next poll-triggered rebuild.
    const intervals: ReturnType<typeof setInterval>[] = [];
    const poll = setInterval(() => {
      postBridge({ type: 'servers.getState' });
    }, 10000);
    intervals.push(poll);
    const tid = setTimeout(() => {
      clearInterval(poll);
      // After 90s switch to a slow keep-alive every 30s for another 2 min
      const slow = setInterval(() => postBridge({ type: 'servers.getState' }), 30000);
      intervals.push(slow);
      setTimeout(() => {
        setTimedOut(true);
        intervals.forEach(clearInterval);
      }, 120000);
    }, 90000);
    return () => {
      window.removeEventListener('storage', onStorage);
      (window as any).chrome?.webview?.removeEventListener('message', handler);
      intervals.forEach(clearInterval);
      clearTimeout(tid);
    };
  }, []);

  const refreshShelves = useCallback(() => {
    setRefreshing(true);
    postBridge({ type: 'servers.getState' });
    setTimeout(() => setRefreshing(false), 2000);
  }, []);

  const selectedServer = bridgeState?.selectedServer ?? '';

  // ── Read shelf config from localStorage (Shelf Creator persists here) ────
  const getShelfCfg = () => {
    try { return JSON.parse(localStorage.getItem('atlas-shelf-manager-v2') ?? '{}') as Record<string, { displayName?: string; hidden?: boolean }>; } catch { return {}; }
  };
  const getShelfOrder = (): string[] => {
    try { return JSON.parse(localStorage.getItem('atlas-shelf-order-v2') ?? '[]'); } catch { return []; }
  };

  const bridgeShelves = useMemo(() => {
    if (!bridgeState?.shelves) return [];
    const cfg = getShelfCfg();
    const order = getShelfOrder();
    const withItems = bridgeState.shelves.filter((s) => s.items?.length > 0);
    const mapped = withItems.map((s) => {
      const c = cfg[s.title] ?? {};
      return {
        title: c.displayName && c.displayName !== s.title ? c.displayName : s.title,
        originalTitle: s.title,
        hidden: c.hidden === true,  // only hide if EXPLICITLY set
        items: s.items.map((item) => mapBridgeItem(item, selectedServer, s.type)),
      };
    });
    // Safety: if more than half the shelves would be hidden, ignore the hidden flags
    const hiddenCount = mapped.filter((s) => s.hidden).length;
    const useHidden = hiddenCount < mapped.length / 2;
    let shelves = useHidden ? mapped.filter((s) => !s.hidden) : mapped.map((s) => ({ ...s, hidden: false }));
    // Apply saved order if present
    if (order.length > 0) {
      const orderMap = new Map(order.map((k: string, i: number) => [k, i]));
      shelves = [...shelves].sort((a, b) => {
        const ai = orderMap.get(a.originalTitle) ?? 9999;
        const bi = orderMap.get(b.originalTitle) ?? 9999;
        return ai - bi;
      });
    }
    return shelves;
  }, [bridgeState, selectedServer]); // eslint-disable-line react-hooks/exhaustive-deps

  const featuredItems = useMemo<FeaturedItem[]>(() => {
    if (!bridgeState?.shelves) return [];
    return makeFeaturedFromBridge(bridgeState.shelves, selectedServer);
  }, [bridgeState, selectedServer]);

  const [featuredMeta, setFeaturedMeta] = useState<Partial<FeaturedItem>>({});
  const featuredItem = featuredItems[featuredIndex % Math.max(1, featuredItems.length)] ?? null;

  // Fetch description + runtime from Cinemeta when featured item has no description
  useEffect(() => {
    setFeaturedMeta({});
    if (!featuredItem) return;
    if (featuredItem.description && featuredItem.description.length > 20) return; // already has it
    const imdbId = featuredItem.imdbId;
    if (!imdbId || !imdbId.startsWith('tt')) return;
    const stremioType = featuredItem.type === 'TV' ? 'series' : 'movie';
    const url = `https://v3-cinemeta.strem.io/meta/${stremioType}/${imdbId}.json`;
    let cancelled = false;
    fetch(url)
      .then((r) => r.json())
      .then((data) => {
        if (cancelled) return;
        const meta = data?.meta;
        if (!meta) return;
        setFeaturedMeta({
          description: meta.description || meta.overview || '',
          runtime: meta.runtime ? String(meta.runtime).replace(/\s*min.*$/i, '') + ' min' : featuredItem.runtime,
          genres: (meta.genres as string[] | undefined) ?? featuredItem.genres,
          rating: meta.imdbRating ? parseFloat(meta.imdbRating) : featuredItem.rating,
        });
      })
      .catch(() => {});
    return () => { cancelled = true; };
  }, [featuredItem?.imdbId]); // eslint-disable-line react-hooks/exhaustive-deps

  const displayedFeaturedItem = featuredItem
    ? { ...featuredItem, ...featuredMeta }
    : null;

  // Auto-rotate hero every 8 seconds; reset timer on manual navigation
  const startAutoRotate = useCallback(() => {
    if (autoRotateRef.current) clearInterval(autoRotateRef.current);
    if (featuredItems.length <= 1) { autoRotateRef.current = null; return; }
    autoRotateRef.current = setInterval(
      () => setFeaturedIndex((i) => (i + 1) % featuredItems.length),
      8000
    );
  }, [featuredItems.length]);

  useEffect(() => {
    startAutoRotate();
    return () => { if (autoRotateRef.current) clearInterval(autoRotateRef.current); };
  }, [startAutoRotate]);

  const openCarousel = (name: string, items: MediaItem[]) => {
    const allItems = bridgeShelves.find((s) => s.title === name)?.items ?? items;
    setCurrentShelf({ name, items: allItems });
    setCarouselOpen(true);
  };
  const openGrid = (name: string) => {
    const shelf = bridgeShelves.find((s) => s.title === name);
    const allItems = shelf?.items ?? [];
    const origTitle = (shelf as any)?.originalTitle ?? name;
    const ct = bridgeState?.shelves?.find((s) => s.title === origTitle)?.type ?? 'movie';
    setCurrentShelf({ name, items: allItems, contentType: ct });
    setViewState('grid');
  };

  const filteredShelves = useMemo(() => {
    let shelves = bridgeShelves;
    if (shelfFilter) shelves = shelves.filter((s) => s.title === shelfFilter);
    return shelves.map((s) => {
      let items = s.items;
      if (typeFilter === 'Movies') items = items.filter((i) => i.type === 'Movie');
      else if (typeFilter === 'TV') items = items.filter((i) => i.type === 'TV');
      return { ...s, items };
    }).filter((s) => s.items.length > 0);
  }, [bridgeShelves, typeFilter, shelfFilter]);

  // When new state arrives while grid is open, update items so infinite scroll sees the new batch
  useEffect(() => {
    if (viewState === 'grid' && currentShelf) {
      const updated = bridgeShelves.find((s) => s.title === currentShelf.name || (s as any).originalTitle === currentShelf.name)?.items;
      if (updated && updated.length !== currentShelf.items.length) {
        setCurrentShelf((prev) => prev ? { ...prev, items: updated } : prev);
      }
    }
  }, [bridgeShelves]); // eslint-disable-line react-hooks/exhaustive-deps

  if (viewState === 'grid' && currentShelf) {
    return (
      <GridView
        shelfName={currentShelf.name}
        items={currentShelf.items}
        contentType={currentShelf.contentType}
        onBack={() => setViewState('shelves')}
        onOpenCarousel={() => openCarousel(currentShelf.name, currentShelf.items)}
      />
    );
  }

  const hasLoadedShelves = (bridgeState?.shelves?.length ?? 0) > 0;

  // Loading — waiting for shelves (show spinner until shelves arrive or timeout)
  if (!hasLoadedShelves && !timedOut) {
    return (
      <div className="flex items-center justify-center h-64 text-slate-400">
        <div className="text-center space-y-2">
          <Sparkles size={32} className="mx-auto opacity-40 animate-pulse" />
          <p className="text-sm">Loading addon shelves…</p>
        </div>
      </div>
    );
  }

  // Empty / timed out with no shelves
  if (!hasLoadedShelves) {
    return (
      <div className="flex items-center justify-center h-64 text-slate-400">
        <div className="text-center space-y-2">
          <Sparkles size={32} className="mx-auto opacity-40" />
          <p className="text-sm">No addon shelves loaded. Check installed addons.</p>
        </div>
      </div>
    );
  }

  return (
    <>
      <div className="space-y-5 pb-8">
        {/* Featured */}
        {displayedFeaturedItem && (
          <div className="relative">
            <FeaturedPanel item={displayedFeaturedItem} />
            {featuredItems.length > 1 && (
              <div className="absolute bottom-3 right-3 z-10 flex gap-1.5">
                <button
                  onClick={(e) => { e.stopPropagation(); setFeaturedIndex((i) => (i - 1 + featuredItems.length) % featuredItems.length); startAutoRotate(); }}
                  className="p-1.5 rounded-full bg-slate-900/70 hover:bg-slate-800 border border-slate-700/50 text-slate-200 hover:text-cyan-300 transition-all"
                >
                  <ChevronLeft size={16} />
                </button>
                <button
                  onClick={(e) => { e.stopPropagation(); setFeaturedIndex((i) => (i + 1) % featuredItems.length); startAutoRotate(); }}
                  className="p-1.5 rounded-full bg-slate-900/70 hover:bg-slate-800 border border-slate-700/50 text-slate-200 hover:text-cyan-300 transition-all"
                >
                  <ChevronRight size={16} />
                </button>
              </div>
            )}
          </div>
        )}

        {/* Filter pills */}
        {(typeDropOpen || shelfDropOpen) && (
          <div className="fixed inset-0 z-40" onClick={() => { setTypeDropOpen(false); setShelfDropOpen(false); }} />
        )}
        <div className="flex items-center gap-2">
          {/* Refresh button */}
          <button
            onClick={refreshShelves}
            title="Refresh shelves from addons"
            className="p-1.5 rounded-full bg-slate-800/60 border border-slate-700/40 text-slate-400 hover:text-cyan-300 hover:border-cyan-400/50 transition-all"
          >
            <RefreshCw size={13} className={refreshing ? 'animate-spin' : ''} />
          </button>
          {/* Content type dropdown */}
          <div className="relative z-50">
            <button
              onClick={() => { setTypeDropOpen((p) => !p); setShelfDropOpen(false); }}
              className="flex items-center gap-1.5 px-3 py-1.5 rounded-full text-xs border bg-slate-900/60 border-slate-700/40 text-slate-200 hover:border-cyan-400/50 transition-all whitespace-nowrap"
            >
              {typeFilter === 'All' ? 'Movies & TV' : typeFilter}
              <ChevronDown size={11} className={`transition-transform duration-200 ${typeDropOpen ? 'rotate-180' : ''}`} />
            </button>
            {typeDropOpen && (
              <div className="absolute top-full mt-1 left-0 bg-slate-900 border border-slate-700/60 rounded-lg overflow-hidden shadow-xl min-w-[130px]">
                {(['All', 'Movies', 'TV'] as const).map((opt) => (
                  <button
                    key={opt}
                    onClick={() => { setTypeFilter(opt); setTypeDropOpen(false); }}
                    className={`w-full text-left px-3 py-2 text-xs transition-colors ${typeFilter === opt ? 'bg-cyan-500/20 text-cyan-200' : 'text-slate-300 hover:bg-slate-800'}`}
                  >
                    {opt === 'All' ? 'All Types' : opt}
                  </button>
                ))}
              </div>
            )}
          </div>

          {/* Shelf dropdown */}
          <div className="relative z-50">
            <button
              onClick={() => { setShelfDropOpen((p) => !p); setTypeDropOpen(false); }}
              className="flex items-center gap-1.5 px-3 py-1.5 rounded-full text-xs border bg-slate-900/60 border-slate-700/40 text-slate-200 hover:border-cyan-400/50 transition-all whitespace-nowrap"
            >
              {shelfFilter ?? 'All Shelves'}
              <ChevronDown size={11} className={`transition-transform duration-200 ${shelfDropOpen ? 'rotate-180' : ''}`} />
            </button>
            {shelfDropOpen && (
              <div className="absolute top-full mt-1 left-0 bg-slate-900 border border-slate-700/60 rounded-lg overflow-hidden shadow-xl min-w-[180px] max-h-60 overflow-y-auto">
                <button
                  onClick={() => { setShelfFilter(null); setShelfDropOpen(false); }}
                  className={`w-full text-left px-3 py-2 text-xs transition-colors ${!shelfFilter ? 'bg-cyan-500/20 text-cyan-200' : 'text-slate-300 hover:bg-slate-800'}`}
                >
                  All Shelves
                </button>
                {bridgeShelves.map((s) => (
                  <button
                    key={s.title}
                    onClick={() => { setShelfFilter(s.title); setShelfDropOpen(false); }}
                    className={`w-full text-left px-3 py-2 text-xs transition-colors ${shelfFilter === s.title ? 'bg-cyan-500/20 text-cyan-200' : 'text-slate-300 hover:bg-slate-800'}`}
                  >
                    {s.title}
                  </button>
                ))}
              </div>
            )}
          </div>
        </div>

        {/* Shelves */}
        {filteredShelves.map((shelf) => (
          <ServerShelf
            key={shelf.title}
            title={shelf.title}
            items={shelf.items}
            count={shelf.items.length}
            onViewAll={() => openGrid(shelf.title)}
            onOpenCarousel={() => openCarousel(shelf.title, shelf.items)}
          />
        ))}

        {/* Custom shelves created from bulk-select */}
        {customShelves.map((shelf) => (
          <ServerShelf
            key={'custom-' + shelf.title}
            title={shelf.title}
            items={shelf.items}
            count={shelf.items.length}
            onViewAll={() => { setCurrentShelf({ name: shelf.title, items: shelf.items }); setViewState('grid'); }}
            onOpenCarousel={() => openCarousel(shelf.title, shelf.items)}
          />
        ))}
      </div>

      {carouselOpen && currentShelf && (
        <CarouselOverlay
          isOpen={carouselOpen}
          items={currentShelf.items}
          shelfName={currentShelf.name}
          onClose={() => setCarouselOpen(false)}
        />
      )}
    </>
  );
}

export default ServersPage;
