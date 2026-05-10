import { useState, useMemo } from 'react';
import { Server, Film, Tv, Music, Gamepad2, Globe, HardDrive, Sparkles, Check } from 'lucide-react';
import { ServerShelf } from '../components/server-shelf';
import { CarouselOverlay } from '../components/carousel-overlay';
import { GridView } from '../components/grid-view';
import { MediaItem } from '../components/server-card';
import { FeaturedPanel } from '../components/featured-panel';

const POSTERS = [
  'https://images.unsplash.com/photo-1773592612185-bd985ac2bfe2?w=600&q=80',
  'https://images.unsplash.com/photo-1773592606902-6e0a0cc9fc47?w=600&q=80',
  'https://images.unsplash.com/photo-1700922180758-b335f78a3d59?w=600&q=80',
  'https://images.unsplash.com/photo-1698533188601-2432adf826f4?w=600&q=80',
  'https://images.unsplash.com/photo-1643377667360-c2be0413ebcc?w=600&q=80',
  'https://images.unsplash.com/photo-1764237769175-47c3e556daa9?w=600&q=80',
  'https://images.unsplash.com/photo-1633423553994-3a142131e191?w=600&q=80',
  'https://images.unsplash.com/photo-1668054562943-6ba120914cc3?w=600&q=80',
  'https://images.unsplash.com/photo-1651627567991-e4ec7b8fc72c?w=600&q=80',
  'https://images.unsplash.com/photo-1645914987454-7bd1464f570f?w=600&q=80',
  'https://images.unsplash.com/photo-1641900278396-a9cf14207f27?w=600&q=80',
  'https://images.unsplash.com/photo-1773257607064-7c6a4cbb71f1?w=600&q=80',
  'https://images.unsplash.com/photo-1777499251009-a3b4c7b62eba?w=600&q=80',
  'https://images.unsplash.com/photo-1766094933384-6a206cfa00cd?w=600&q=80',
  'https://images.unsplash.com/photo-1738980420952-56cc02acd17f?w=600&q=80',
  'https://images.unsplash.com/photo-1598062547942-17806c4df368?w=600&q=80',
  'https://images.unsplash.com/photo-1655928461456-b5c6db979360?w=600&q=80',
  'https://images.unsplash.com/photo-1735713212083-82eafc42bf64?w=600&q=80',
  'https://images.unsplash.com/photo-1759926953901-468ce451805a?w=600&q=80',
  'https://images.unsplash.com/photo-1535391879778-3bae11d29a24?w=600&q=80',
  'https://images.unsplash.com/photo-1628763228722-b11a9c545ed7?w=600&q=80',
  'https://images.unsplash.com/photo-1633766306939-d0169a014fe2?w=600&q=80',
  'https://images.unsplash.com/photo-1653628989927-957b335955be?w=600&q=80',
  'https://images.unsplash.com/photo-1631044176346-804c33ade61c?w=600&q=80',
  'https://images.unsplash.com/photo-1773982417771-d41776e48cf5?w=600&q=80',
  'https://images.unsplash.com/photo-1668720854839-a9a746084fe5?w=600&q=80',
  'https://images.unsplash.com/photo-1549394325-200e58997f69?w=600&q=80',
  'https://images.unsplash.com/photo-1762279388956-1c098163a2a8?w=600&q=80',
  'https://images.unsplash.com/photo-1531113165519-5eb0816d7e02?w=600&q=80',
  'https://images.unsplash.com/photo-1727812518464-dae2481c65c3?w=600&q=80',
];

const TITLES = [
  'Dune: Part Two', 'The Last of Us', 'Oppenheimer', 'Severance', 'Foundation',
  'Blade Runner 2049', 'The Creator', 'Poor Things', 'The Batman', 'Across the Spider-Verse',
  'Succession', 'Breaking Bad', 'The Wire', 'The Sopranos', 'Mad Men',
  'Interstellar', 'Arrival', 'Ex Machina', 'Her', 'Matrix Resurrections',
  'Ted Lasso', 'Abbott Elementary', 'The Bear', 'Barry', 'What We Do in the Shadows',
  'The Shining', 'Hereditary', 'Midsommar', 'Get Out', 'Nope',
  'Inside Out 2', 'WALL-E', 'Up', 'Coco', 'Toy Story',
  'Planet Earth II', 'Our Planet', 'Blue Planet II', 'Cosmos', 'The Last Dance',
];

const YEARS = ['2024', '2023', '2022', '2021', '2025'];
const QUALITIES = ['4K', '1080p', 'HDR', '4K HDR'];
const SERVERS = ['Plex', 'Jellyfin', 'Local NAS', 'Stremio', 'TMDb', 'Trakt'];
const DESCRIPTIONS = [
  'A gripping cinematic journey through unfamiliar worlds.',
  'An intense character study set against an unforgettable backdrop.',
  'A thrilling tale of resilience, mystery, and discovery.',
  'A bold new entry that redefines the genre.',
];

interface SourceMeta {
  id: string;
  name: string;
  icon: typeof Server;
  color: string;
  online: boolean;
  count: number;
}

const SOURCES: SourceMeta[] = [
  { id: 'plex', name: 'Plex', icon: Server, color: 'from-amber-500 to-orange-500', online: true, count: 1589 },
  { id: 'jellyfin', name: 'Jellyfin', icon: HardDrive, color: 'from-violet-500 to-fuchsia-500', online: true, count: 695 },
  { id: 'movies', name: 'Movies Add-on', icon: Film, color: 'from-cyan-500 to-blue-500', online: true, count: 4210 },
  { id: 'tv', name: 'TV Add-on', icon: Tv, color: 'from-pink-500 to-rose-500', online: true, count: 2380 },
  { id: 'trakt', name: 'Trakt', icon: Sparkles, color: 'from-red-500 to-pink-500', online: true, count: 312 },
  { id: 'tmdb', name: 'TMDb', icon: Globe, color: 'from-emerald-500 to-teal-500', online: true, count: 9999 },
  { id: 'local', name: 'Local', icon: HardDrive, color: 'from-slate-500 to-slate-600', online: true, count: 301 },
  { id: 'music', name: 'Music', icon: Music, color: 'from-purple-500 to-indigo-500', online: true, count: 8453 },
  { id: 'games', name: 'Games', icon: Gamepad2, color: 'from-lime-500 to-green-500', online: false, count: 145 },
];

const FILTERS = ['All', 'Movies', 'TV', 'Music', 'Games', 'New', 'Popular', 'Trending'];

const featuredItem = {
  id: 'dune-part-two',
  title: 'Dune: Part Two',
  backdropUrl: 'https://images.unsplash.com/photo-1446776811953-b23d57bd21aa?w=1600&q=80',
  rating: 8.8,
  runtime: '2h 46m',
  release: 'Dec 2024',
  genres: ['Sci-Fi', 'Adventure', 'Action'],
  source: 'Available on 6 sources',
  description: 'Paul Atreides unites with Chani and the Fremen while seeking revenge against the conspirators who destroyed his family.',
  type: 'Movie' as const,
  primaryAction: 'Play' as const,
};

const pick = <T,>(arr: T[], i: number) => arr[i % arr.length];

const generateItems = (
  count: number,
  seed: number,
  overrides: Partial<MediaItem> = {}
): MediaItem[] =>
  Array.from({ length: count }, (_, i) => {
    const idx = (seed * 7 + i * 3) % POSTERS.length;
    const types: MediaItem['type'][] = ['Movie', 'TV', 'Music Video', 'Game'];
    return {
      id: `s${seed}-i${i}`,
      title: pick(TITLES, idx),
      year: pick(YEARS, idx),
      type: pick(types, i),
      server: pick(SERVERS, idx),
      rating: 6 + ((idx * 17) % 40) / 10,
      quality: pick(QUALITIES, i),
      posterUrl: POSTERS[idx],
      hasMetadata: true,
      hasArtwork: true,
      runtime: `${90 + (idx % 60)}min`,
      genre: overrides.genre ?? pick(['Action', 'Sci-Fi', 'Comedy', 'Horror', 'Family', 'Documentary'], idx),
      description: pick(DESCRIPTIONS, idx),
      ...overrides,
    };
  });

const SHELVES: { title: string; items: MediaItem[] }[] = [
  { title: 'Latest From Sources', items: generateItems(20, 1) },
  { title: 'Trending Across Add-ons', items: generateItems(20, 2) },
  { title: 'Popular Movies', items: generateItems(18, 3, { type: 'Movie' }) },
  { title: 'Popular TV', items: generateItems(18, 4, { type: 'TV' }) },
  { title: 'New Episodes', items: generateItems(16, 5, { type: 'TV' }) },
  { title: '4K / HDR', items: generateItems(15, 6, { quality: '4K HDR' }) },
  { title: 'Action', items: generateItems(18, 7, { genre: 'Action' }) },
  { title: 'Sci-Fi', items: generateItems(18, 8, { genre: 'Sci-Fi' }) },
  { title: 'Comedy', items: generateItems(16, 9, { genre: 'Comedy' }) },
  { title: 'Horror', items: generateItems(14, 10, { genre: 'Horror' }) },
  { title: 'Family', items: generateItems(12, 11, { genre: 'Family' }) },
  { title: 'Documentaries', items: generateItems(12, 12, { genre: 'Documentary' }) },
  { title: 'Music Videos', items: generateItems(15, 13, { type: 'Music Video' }) },
  { title: 'Game Trailers', items: generateItems(12, 14, { type: 'Game' }) },
  { title: 'Continue Watching', items: generateItems(10, 15) },
];

type ViewState = 'shelves' | 'grid';

export function ServersPage() {
  const [viewState, setViewState] = useState<ViewState>('shelves');
  const [carouselOpen, setCarouselOpen] = useState(false);
  const [activeSource, setActiveSource] = useState<string>('all');
  const [activeFilter, setActiveFilter] = useState<string>('All');
  const [currentShelf, setCurrentShelf] = useState<{ name: string; items: MediaItem[] } | null>(null);

  const openCarousel = (name: string, items: MediaItem[]) => {
    setCurrentShelf({ name, items });
    setCarouselOpen(true);
  };
  const openGrid = (name: string, items: MediaItem[]) => {
    setCurrentShelf({ name, items });
    setViewState('grid');
  };

  const filteredShelves = useMemo(() => {
    return SHELVES.map((s) => {
      let items = s.items;
      if (activeFilter === 'Movies') items = items.filter((i) => i.type === 'Movie');
      else if (activeFilter === 'TV') items = items.filter((i) => i.type === 'TV');
      else if (activeFilter === 'Music') items = items.filter((i) => i.type === 'Music Video');
      else if (activeFilter === 'Games') items = items.filter((i) => i.type === 'Game');
      return { ...s, items };
    }).filter((s) => s.items.length > 0);
  }, [activeFilter]);

  if (viewState === 'grid' && currentShelf) {
    return (
      <GridView
        shelfName={currentShelf.name}
        items={currentShelf.items}
        onBack={() => setViewState('shelves')}
        onOpenCarousel={() => openCarousel(currentShelf.name, currentShelf.items)}
      />
    );
  }

  return (
    <>
      <div className="space-y-5 pb-8">
        {/* Featured */}
        <FeaturedPanel item={featuredItem} />

        {/* Source pills */}
        <div className="flex items-center gap-2 overflow-x-auto scrollbar-hide pb-1">
          <button
            onClick={() => setActiveSource('all')}
            className={`flex items-center gap-2 px-3 py-1.5 rounded-lg border whitespace-nowrap transition-all ${
              activeSource === 'all'
                ? 'bg-gradient-to-r from-cyan-500/20 to-violet-500/20 border-cyan-400/50 text-cyan-200'
                : 'bg-slate-900/60 border-slate-700/40 text-slate-300 hover:border-slate-600'
            }`}
          >
            <Sparkles size={14} />
            <span className="text-xs">All Sources</span>
          </button>
          {SOURCES.map((src) => {
            const Icon = src.icon;
            const active = activeSource === src.id;
            return (
              <button
                key={src.id}
                onClick={() => setActiveSource(src.id)}
                className={`flex items-center gap-2 px-3 py-1.5 rounded-lg border whitespace-nowrap transition-all ${
                  active
                    ? 'bg-slate-800/80 border-cyan-400/50 text-slate-100'
                    : 'bg-slate-900/60 border-slate-700/40 text-slate-300 hover:border-slate-600'
                }`}
              >
                <span className={`p-1 rounded bg-gradient-to-br ${src.color}`}>
                  <Icon size={11} className="text-white" />
                </span>
                <span className="text-xs">{src.name}</span>
                <span
                  className={`w-1.5 h-1.5 rounded-full ${src.online ? 'bg-emerald-400 shadow-[0_0_6px] shadow-emerald-400' : 'bg-slate-600'}`}
                />
                <span className="text-[10px] text-slate-400">{src.count.toLocaleString()}</span>
              </button>
            );
          })}
        </div>

        {/* Filter chips */}
        <div className="flex items-center gap-2 flex-wrap">
          {FILTERS.map((f) => {
            const active = activeFilter === f;
            return (
              <button
                key={f}
                onClick={() => setActiveFilter(f)}
                className={`flex items-center gap-1 px-2.5 py-1 rounded-full text-xs border transition-all ${
                  active
                    ? 'bg-cyan-500/15 border-cyan-400/50 text-cyan-200'
                    : 'bg-slate-900/40 border-slate-700/30 text-slate-400 hover:text-slate-200 hover:border-slate-600'
                }`}
              >
                {active && <Check size={10} />}
                {f}
              </button>
            );
          })}
        </div>

        {/* Shelves */}
        {filteredShelves.map((shelf) => (
          <ServerShelf
            key={shelf.title}
            title={shelf.title}
            items={shelf.items}
            count={shelf.items.length}
            onViewAll={() => openGrid(shelf.title, shelf.items)}
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
