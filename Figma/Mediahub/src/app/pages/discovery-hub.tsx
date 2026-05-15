import { useEffect, useState } from 'react';
import { createPortal } from 'react-dom';
import {
  Search, Sparkles, TrendingUp, TrendingDown, Minus, Film, Tv, Gamepad2,
  Music, User, Play, Plus, Bell, RefreshCw, Clock, Target, AlertTriangle, Info, ChevronLeft, ChevronRight, X
} from 'lucide-react';

const YT_API_KEY = (window as any).__ATLAS_YT_KEY ?? import.meta.env.VITE_YT_API_KEY ?? '';

function extractYouTubeVideoId(url: string): string | null {
  const m = url.match(/(?:youtube\.com\/watch\?v=|youtu\.be\/)([^&?/]{11})/);
  return m ? m[1] : null;
}

async function fetchYouTubeVideoId(title: string, type: string): Promise<string | null> {
  const query = encodeURIComponent(`${title} ${type === 'TV' ? 'series' : type === 'Game' ? 'gameplay' : 'movie'} official trailer`);
  // Try YouTube Data API if key is set
  if (YT_API_KEY) {
    try {
      const res = await fetch(
        `https://www.googleapis.com/youtube/v3/search?part=snippet&q=${query}&type=video&key=${YT_API_KEY}&maxResults=1`
      );
      const data = await res.json();
      const id = data?.items?.[0]?.id?.videoId;
      if (id) return id;
    } catch { /* fall through to Invidious */ }
  }
  // Fallback: Invidious API (no key required, CORS-enabled)
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

function TrailerModal({ videoId, title, onClose }: { videoId: string; title: string; onClose: () => void }) {
  const embedUrl = `https://www.youtube.com/embed/${videoId}?autoplay=1&rel=0&modestbranding=1`;
  return createPortal(
    <div className="fixed inset-0 z-[9999] flex items-center justify-center bg-black/85 backdrop-blur-sm" onClick={onClose}>
      <div
        className="relative w-[860px] max-w-[92vw] rounded-xl overflow-hidden shadow-2xl border border-slate-700/60"
        style={{ aspectRatio: '16/9' }}
        onClick={(e) => e.stopPropagation()}
      >
        <iframe src={embedUrl} title={`${title} Trailer`} className="w-full h-full"
          allow="autoplay; encrypted-media; fullscreen" allowFullScreen />
        <button onClick={onClose}
          className="absolute top-2 right-2 p-1.5 rounded-full bg-black/70 hover:bg-black text-white border border-white/20">
          <X size={16} />
        </button>
      </div>
    </div>,
    document.body
  );
}

type HeroCategory = 'Movie' | 'TV' | 'Music' | 'Game' | 'News';

type HeroItem = {
  id: string;
  title: string;
  backdropUrl: string;
  rating: number;
  runtime: string;
  release: string;
  genres: string[];
  source: string;
  description: string;
  category: HeroCategory;
  canPlay: boolean;
  hasTrailer: boolean;
  hasDetails: boolean;
  trailerUrl?: string;
  isNewSeason?: boolean;
};

type DiscoveryMediaPayload = {
  id?: string;
  title?: string;
  image?: string;
  backdropUrl?: string;
  posterUrl?: string;
  rating?: number;
  type?: string;
  releaseDate?: string;
  overview?: string;
  genres?: string[];
  runtime?: string;
  trailerUrl?: string;
};

type DiscoveryDataPayload = {
  trending?: DiscoveryMediaPayload[];
  trailers?: DiscoveryMediaPayload[];
  upcoming?: DiscoveryMediaPayload[];
  news?: DiscoveryNewsPayload[];
  celebrities?: DiscoveryCelebrityPayload[];
  featured?: DiscoveryMediaPayload | null;
  heroMovieTv?: DiscoveryMediaPayload[];
  error?: string;
};

type DiscoveryNewsPayload = {
  id?: string;
  headline?: string;
  image?: string;
  preview?: string;
  timeAgo?: string;
  trending?: boolean;
  url?: string;
  source?: string;
};

type DiscoveryCelebrityPayload = {
  id?: string;
  name?: string;
  image?: string;
  role?: string;
  trending?: boolean;
  category?: string;
};

const fallbackHeroItems: HeroItem[] = [
  {
    id: 'movie-dune-3',
    title: 'Dune: Part Three',
    backdropUrl: 'https://images.unsplash.com/photo-1518709268805-4e9042af9f23?w=1600&q=80',
    rating: 9.1,
    runtime: '2h 58m',
    release: 'Dec 2026',
    genres: ['Sci-Fi', 'Epic', 'Drama'],
    source: 'Movie · Cinema · IMAX',
    description: 'Paul Atreides leads a desert revolt across Arrakis as the galaxy ignites. The trailer is breaking the internet.',
    category: 'Movie',
    canPlay: true,
    hasTrailer: true,
    hasDetails: true,
    trailerUrl: 'https://www.youtube.com/watch?v=Way9Dexny3w',
  },
  {
    id: 'movie-fantastic-four',
    title: 'Fantastic Four: First Flight',
    backdropUrl: 'https://images.unsplash.com/photo-1489599849927-2ee91cede3ba?w=1600&q=80',
    rating: 8.4,
    runtime: '2h 12m',
    release: 'Jul 2026',
    genres: ['Movie', 'Sci-Fi', 'Action'],
    source: 'Movie · Theatrical',
    description: 'Marvel\'s first family launches into a high-stakes cosmic conflict with a new universe-level threat.',
    category: 'Movie',
    canPlay: true,
    hasTrailer: true,
    hasDetails: true,
    trailerUrl: 'https://www.youtube.com/watch?v=7SlILk2WMTI',
  },
  {
    id: 'movie-avatar-fire-ash',
    title: 'Avatar: Fire and Ash',
    backdropUrl: 'https://images.unsplash.com/photo-1502134249126-9f3755a50d78?w=1600&q=80',
    rating: 8.7,
    runtime: '3h 05m',
    release: 'Dec 2026',
    genres: ['Movie', 'Adventure', 'Epic'],
    source: 'Movie · IMAX',
    description: 'Pandora factions collide in the most ambitious chapter yet as new clans enter the war.',
    category: 'Movie',
    canPlay: true,
    hasTrailer: true,
    hasDetails: true,
    trailerUrl: 'https://www.youtube.com/watch?v=1jZ6g4qW9JY',
  },
  {
    id: 'movie-superman-legacy',
    title: 'Superman: Legacy',
    backdropUrl: 'https://images.unsplash.com/photo-1440404653325-ab127d49abc1?w=1600&q=80',
    rating: 8.2,
    runtime: '2h 20m',
    release: 'Jul 2026',
    genres: ['Movie', 'Superhero', 'Drama'],
    source: 'Movie · DC Studios',
    description: 'Clark Kent balances hope and identity while facing a politically fractured world.',
    category: 'Movie',
    canPlay: true,
    hasTrailer: true,
    hasDetails: true,
    trailerUrl: 'https://www.youtube.com/watch?v=9Qxgwf6x4fY',
  },
  {
    id: 'movie-spiderman-4',
    title: 'Spider-Man 4',
    backdropUrl: 'https://images.unsplash.com/photo-1534447677768-be436bb09401?w=1600&q=80',
    rating: 8.6,
    runtime: '2h 14m',
    release: 'Nov 2026',
    genres: ['Movie', 'Action', 'Adventure'],
    source: 'Movie · Sony/Marvel',
    description: 'Peter Parker returns with a street-level mystery that quickly escalates into multiverse danger.',
    category: 'Movie',
    canPlay: true,
    hasTrailer: true,
    hasDetails: true,
    trailerUrl: 'https://www.youtube.com/watch?v=JfVOs4VSpmA',
  },
  {
    id: 'tv-severance-s3',
    title: 'Severance Season 3',
    backdropUrl: 'https://images.unsplash.com/photo-1489599849927-2ee91cede3ba?w=1600&q=80',
    rating: 8.9,
    runtime: 'Series',
    release: 'Q1 2027',
    genres: ['TV', 'Thriller', 'Drama'],
    source: 'TV · Apple TV+ · Teaser',
    description: 'A new Lumon teaser hints at a major reveal. Early reactions are pushing this to must-watch status.',
    category: 'TV',
    canPlay: true,
    hasTrailer: true,
    hasDetails: true,
    trailerUrl: 'https://www.youtube.com/watch?v=uLtkt8BonwM',
  },
  {
    id: 'tv-last-of-us-s2',
    title: 'The Last of Us Season 2',
    backdropUrl: 'https://images.unsplash.com/photo-1478720568477-152d9b164e26?w=1600&q=80',
    rating: 9.0,
    runtime: 'Series',
    release: 'Apr 2026',
    genres: ['TV', 'Drama', 'Survival'],
    source: 'TV · HBO Max',
    description: 'The next chapter deepens the emotional fallout with darker turns and larger stakes.',
    category: 'TV',
    canPlay: true,
    hasTrailer: true,
    hasDetails: true,
    trailerUrl: 'https://www.youtube.com/watch?v=aOC8E8z_ifw',
  },
  {
    id: 'tv-mandalorian-s4',
    title: 'The Mandalorian Season 4',
    backdropUrl: 'https://images.unsplash.com/photo-1460881680858-30d872d5b530?w=1600&q=80',
    rating: 8.5,
    runtime: 'Series',
    release: 'May 2026',
    genres: ['TV', 'Sci-Fi', 'Adventure'],
    source: 'TV · Disney+',
    description: 'Din and Grogu return with a high-risk mission that could reshape outer-rim alliances.',
    category: 'TV',
    canPlay: true,
    hasTrailer: true,
    hasDetails: true,
    trailerUrl: 'https://www.youtube.com/watch?v=Y3N6M8P6hT0',
  },
  {
    id: 'tv-blade-runner-2099',
    title: 'Blade Runner 2099',
    backdropUrl: 'https://images.unsplash.com/photo-1460881680858-30d872d5b530?w=1600&q=80',
    rating: 8.7,
    runtime: 'Series',
    release: '2027',
    genres: ['TV', 'Neo-Noir', 'Sci-Fi'],
    source: 'TV · Prime Video',
    description: 'A new detective story in the Blade Runner universe explores memory, power, and identity.',
    category: 'TV',
    canPlay: true,
    hasTrailer: true,
    hasDetails: true,
    trailerUrl: 'https://www.youtube.com/watch?v=b9EkMc79ZSU',
  },
  {
    id: 'tv-stranger-things-s5',
    title: 'Stranger Things Season 5',
    backdropUrl: 'https://images.unsplash.com/photo-1485846234645-a62644f84728?w=1600&q=80',
    rating: 8.8,
    runtime: 'Series',
    release: 'TBA 2026',
    genres: ['TV', 'Horror', 'Fantasy'],
    source: 'TV · Netflix',
    description: 'Hawkins prepares for a final showdown as the Upside Down threatens to spill over permanently.',
    category: 'TV',
    canPlay: true,
    hasTrailer: true,
    hasDetails: true,
    trailerUrl: 'https://www.youtube.com/watch?v=otutSrxYpa4',
  },
  {
    id: 'music-midnight-broadcast',
    title: 'Global Album Radar: Midnight Broadcast',
    backdropUrl: 'https://images.unsplash.com/photo-1493225457124-a3eb161ffa5f?w=1600&q=80',
    rating: 8.4,
    runtime: 'Music',
    release: 'Tonight',
    genres: ['Music', 'Pop', 'Electronic'],
    source: 'Music · Streaming Charts · Spotify/Last.fm',
    description: 'A midnight release is surging on pre-save charts with high social buzz across multiple regions.',
    category: 'Music',
    canPlay: false,
    hasTrailer: false,
    hasDetails: true,
  },
  {
    id: 'music-billie-single',
    title: 'Billie Eilish: New Single Wave',
    backdropUrl: 'https://images.unsplash.com/photo-1470225620780-dba8ba36b745?w=1600&q=80',
    rating: 8.3,
    runtime: 'Music',
    release: 'This week',
    genres: ['Music', 'Alt-Pop', 'Viral'],
    source: 'Music · Billboard Pulse',
    description: 'A new single is climbing fast with heavy replay rates and strong fan sentiment.',
    category: 'Music',
    canPlay: false,
    hasTrailer: false,
    hasDetails: true,
  },
  {
    id: 'music-taylor-tour-cut',
    title: 'Taylor Swift: Tour Cut Release',
    backdropUrl: 'https://images.unsplash.com/photo-1511379938547-c1f69419868d?w=1600&q=80',
    rating: 8.6,
    runtime: 'Music',
    release: 'Fri 00:00',
    genres: ['Music', 'Pop', 'Live'],
    source: 'Music · Global Charts',
    description: 'The extended concert cut is projected to dominate weekend streams worldwide.',
    category: 'Music',
    canPlay: false,
    hasTrailer: false,
    hasDetails: true,
  },
  {
    id: 'music-charli-remix',
    title: 'Charli XCX: BRAT Remix Drop',
    backdropUrl: 'https://images.unsplash.com/photo-1501386761578-eac5c94b800a?w=1600&q=80',
    rating: 8.1,
    runtime: 'Music',
    release: 'Tomorrow',
    genres: ['Music', 'Electronic', 'Club'],
    source: 'Music · Editorial Picks',
    description: 'A remix package with major features is fuelling dance playlists and social clips.',
    category: 'Music',
    canPlay: false,
    hasTrailer: false,
    hasDetails: true,
  },
  {
    id: 'music-weeknd-collab',
    title: 'The Weeknd x Metro Collab',
    backdropUrl: 'https://images.unsplash.com/photo-1516280440614-37939bbacd81?w=1600&q=80',
    rating: 8.5,
    runtime: 'Music',
    release: 'Next week',
    genres: ['Music', 'R&B', 'Hip-Hop'],
    source: 'Music · Industry Radar',
    description: 'Insiders report a high-profile collaboration expected to trend across all major platforms.',
    category: 'Music',
    canPlay: false,
    hasTrailer: false,
    hasDetails: true,
  },
  {
    id: 'game-gta-vi',
    title: 'GTA VI Gameplay Showcase',
    backdropUrl: 'https://images.unsplash.com/photo-1542751371-adc38448a05e?w=1600&q=80',
    rating: 9.3,
    runtime: '6m trailer',
    release: '2027 Window',
    genres: ['Game', 'Open World', 'Action'],
    source: 'Games · Official Channel',
    description: 'Fresh gameplay footage and release-window clues are driving one of the hottest discussion spikes this week.',
    category: 'Game',
    canPlay: false,
    hasTrailer: true,
    hasDetails: true,
  },
  {
    id: 'game-silksong',
    title: 'Hollow Knight: Silksong Deep Dive',
    backdropUrl: 'https://images.unsplash.com/photo-1511512578047-dfb367046420?w=1600&q=80',
    rating: 8.9,
    runtime: 'Game trailer',
    release: '2026',
    genres: ['Game', 'Indie', 'Action'],
    source: 'Games · Indie World',
    description: 'New movement systems and boss reveals are igniting fan theory threads across communities.',
    category: 'Game',
    canPlay: false,
    hasTrailer: true,
    hasDetails: true,
  },
  {
    id: 'game-elden-ring-expansion',
    title: 'Elden Ring Expansion Reveal',
    backdropUrl: 'https://images.unsplash.com/photo-1550745165-9bc0b252726f?w=1600&q=80',
    rating: 9.0,
    runtime: 'Game teaser',
    release: 'Q4 2026',
    genres: ['Game', 'RPG', 'Fantasy'],
    source: 'Games · FromSoftware',
    description: 'A dark new realm and endgame systems are expected to redefine top-tier challenge runs.',
    category: 'Game',
    canPlay: false,
    hasTrailer: true,
    hasDetails: true,
  },
  {
    id: 'game-hades-2',
    title: 'Hades II Launch Window Update',
    backdropUrl: 'https://images.unsplash.com/photo-1511882150382-421056c89033?w=1600&q=80',
    rating: 8.7,
    runtime: 'Game update',
    release: 'Early 2027',
    genres: ['Game', 'Roguelike', 'Action'],
    source: 'Games · Studio Update',
    description: 'Combat refinements and new gods are pushing anticipation to a new peak.',
    category: 'Game',
    canPlay: false,
    hasTrailer: false,
    hasDetails: true,
  },
  {
    id: 'game-cod-next',
    title: 'Call of Duty NEXT Briefing',
    backdropUrl: 'https://images.unsplash.com/photo-1593305841991-05c297ba4575?w=1600&q=80',
    rating: 8.0,
    runtime: 'Event stream',
    release: 'This month',
    genres: ['Game', 'Shooter', 'Live Event'],
    source: 'Games · Publisher Event',
    description: 'Multiplayer changes and map reveals are driving active debate in the competitive scene.',
    category: 'Game',
    canPlay: false,
    hasTrailer: true,
    hasDetails: true,
  },
  {
    id: 'news-franchise-deal',
    title: 'Entertainment Flash: Major Franchise Deal',
    backdropUrl: 'https://images.unsplash.com/photo-1495020689067-958852a7765e?w=1600&q=80',
    rating: 8.0,
    runtime: 'News',
    release: 'Just now',
    genres: ['News', 'Entertainment', 'Industry'],
    source: 'News · Variety/THR',
    description: 'Studios are reacting to a blockbuster rights deal that could reshape release calendars for the next two years.',
    category: 'News',
    canPlay: false,
    hasTrailer: false,
    hasDetails: true,
  },
  {
    id: 'news-mcu-casting',
    title: 'Pedro Pascal Joins MCU Project',
    backdropUrl: 'https://images.unsplash.com/photo-1478720568477-152d9b164e26?w=1600&q=80',
    rating: 8.2,
    runtime: 'Breaking',
    release: '4h ago',
    genres: ['News', 'Casting', 'Movies'],
    source: 'News · THR',
    description: 'Major casting confirmation lands as production timelines tighten across the superhero slate.',
    category: 'News',
    canPlay: false,
    hasTrailer: false,
    hasDetails: true,
  },
  {
    id: 'news-hbo-renewal',
    title: 'HBO Renews Fantasy Hit for Season 3',
    backdropUrl: 'https://images.unsplash.com/photo-1485846234645-a62644f84728?w=1600&q=80',
    rating: 7.9,
    runtime: 'Update',
    release: '1d ago',
    genres: ['News', 'TV', 'Renewal'],
    source: 'News · Variety',
    description: 'Renewal news triggers a wave of prediction threads about cast arcs and release pacing.',
    category: 'News',
    canPlay: false,
    hasTrailer: false,
    hasDetails: true,
  },
  {
    id: 'news-gaming-expansion',
    title: 'FromSoftware Teases New Expansion',
    backdropUrl: 'https://images.unsplash.com/photo-1550745165-9bc0b252726f?w=1600&q=80',
    rating: 8.3,
    runtime: 'Update',
    release: '7h ago',
    genres: ['News', 'Gaming', 'Teaser'],
    source: 'News · IGN',
    description: 'A cryptic teaser image has sparked extensive lore analysis and release-window speculation.',
    category: 'News',
    canPlay: false,
    hasTrailer: false,
    hasDetails: true,
  },
  {
    id: 'news-concert-film',
    title: 'Beyonce Concert Film Announced',
    backdropUrl: 'https://images.unsplash.com/photo-1501386761578-eac5c94b800a?w=1600&q=80',
    rating: 8.1,
    runtime: 'Announcement',
    release: '12h ago',
    genres: ['News', 'Music', 'Live'],
    source: 'News · Billboard',
    description: 'The surprise announcement is trending with strong early demand for premium format screenings.',
    category: 'News',
    canPlay: false,
    hasTrailer: false,
    hasDetails: true,
  },
];

const filterChips = [
  'All', 'Movies', 'TV', 'Music', 'Games', 'Trailers',
  'Coming Soon', 'Hot Right Now', 'Celebrity News', 'Worth Watching', 'Skip It'
];

const radarUpdates = [
  { type: 'Movie', title: 'Dune: Part Three', summary: 'Official trailer just dropped', time: '2h ago', heat: 98, verdict: 'Must Watch', verdictColor: 'bg-green-500/30 text-green-200' },
  { type: 'TV', title: 'Severance S3', summary: 'Teaser reveals major twist', time: '5h ago', heat: 95, verdict: 'Trending Fast', verdictColor: 'bg-cyan-500/30 text-cyan-200' },
  { type: 'Game', title: 'GTA VI', summary: 'New gameplay footage released', time: '1d ago', heat: 99, verdict: 'Must Watch', verdictColor: 'bg-green-500/30 text-green-200' },
  { type: 'Music', title: 'Billie Eilish - New Single', summary: 'Album announcement coming', time: '3h ago', heat: 88, verdict: 'Trending Fast', verdictColor: 'bg-cyan-500/30 text-cyan-200' },
  { type: 'Celebrity', title: 'Denis Villeneuve confirms Dune 3', summary: 'Production starts 2027', time: '6h ago', heat: 92, verdict: 'Worth Tracking', verdictColor: 'bg-purple-500/30 text-purple-200' },
  { type: 'Movie', title: 'Superman: Legacy', summary: 'First look at costume revealed', time: '8h ago', heat: 85, verdict: 'Looks Hot', verdictColor: 'bg-amber-500/30 text-amber-200' },
];

const latestTrailers = [
  { id: '1', title: 'Avatar: Fire and Ash', type: 'Movie', releaseDate: 'Dec 2026', platform: 'Cinema', runtime: '3:12', heat: 92, verdict: 'Must Watch', verdictColor: 'bg-green-500/30 text-green-200' },
  { id: '2', title: 'The Last of Us S2', type: 'TV', releaseDate: 'Apr 2026', platform: 'HBO Max', runtime: '2:45', heat: 96, verdict: 'Must Watch', verdictColor: 'bg-green-500/30 text-green-200' },
  { id: '3', title: 'Hollow Knight: Silksong', type: 'Game', releaseDate: '2026', platform: 'Multi', runtime: '4:20', heat: 90, verdict: 'Looks Hot', verdictColor: 'bg-cyan-500/30 text-cyan-200' },
  { id: '4', title: 'The Mandalorian S4', type: 'TV', releaseDate: 'May 2026', platform: 'Disney+', runtime: '2:18', heat: 87, verdict: 'Worth Watching', verdictColor: 'bg-cyan-500/30 text-cyan-200' },
  { id: '5', title: 'Blade Runner 2099', type: 'TV', releaseDate: '2027', platform: 'Prime', runtime: '3:05', heat: 89, verdict: 'Looks Hot', verdictColor: 'bg-cyan-500/30 text-cyan-200' },
  { id: '6', title: 'Spider-Man 4', type: 'Movie', releaseDate: 'Jul 2026', platform: 'Cinema', runtime: '2:30', heat: 94, verdict: 'Must Watch', verdictColor: 'bg-green-500/30 text-green-200' },
];

const comingSoon = [
  { title: 'Superman: Legacy', date: 'Jul 11, 2026', platform: 'Cinema', type: 'Movie', countdown: '419 days', interest: 85 },
  { title: 'Andor S2', date: 'Apr 22, 2026', platform: 'Disney+', type: 'TV', countdown: '339 days', interest: 91 },
  { title: 'GTA VI', date: 'Q1 2027', platform: 'PS5/Xbox', type: 'Game', countdown: '~600 days', interest: 99 },
  { title: 'Stranger Things S5', date: 'TBA 2026', platform: 'Netflix', type: 'TV', countdown: 'TBA', interest: 88 },
];

const hotRightNow = [
  { rank: 1, title: 'Severance', category: 'TV', buzz: 98, trend: 'up', reason: 'Season 2 finale broke streaming records' },
  { rank: 2, title: 'Dune: Part Two', category: 'Movie', buzz: 95, trend: 'up', reason: 'Extended IMAX re-release announced' },
  { rank: 3, title: 'Baldur\'s Gate 3', category: 'Game', buzz: 92, trend: 'stable', reason: 'Major DLC announcement' },
  { rank: 4, title: 'Taylor Swift', category: 'Music', buzz: 94, trend: 'up', reason: 'New album surprise drop' },
  { rank: 5, title: 'Fallout', category: 'TV', buzz: 89, trend: 'down', reason: 'Season 2 production confirmed' },
];

const skipRadar = [
  { title: 'Madame Web 2', category: 'Movie', reason: 'Poor test screenings', risk: 85, verdict: 'Skip', verdictColor: 'bg-red-500/30 text-red-200' },
  { title: 'Rebel Moon Part 3', category: 'Movie', reason: 'Weak franchise performance', risk: 78, verdict: 'Wait', verdictColor: 'bg-amber-500/30 text-amber-200' },
  { title: 'The Acolyte S2', category: 'TV', reason: 'Low viewership, high costs', risk: 72, verdict: 'Maybe Later', verdictColor: 'bg-slate-500/30 text-slate-300' },
];

const news = [
  { headline: 'Pedro Pascal joins MCU in Fantastic Four', source: 'THR', time: '4h ago', category: 'Casting' },
  { headline: 'FromSoftware teases new Elden Ring expansion', source: 'IGN', time: '7h ago', category: 'Gaming' },
  { headline: 'HBO renews House of the Dragon for Season 3', source: 'Variety', time: '1d ago', category: 'TV' },
  { headline: 'Beyoncé surprise concert film announcement', source: 'Billboard', time: '12h ago', category: 'Music' },
];

const aiPicks = [
  { title: 'The Three-Body Problem', reason: 'Matches your sci-fi taste', confidence: 92, type: 'TV' },
  { title: 'Hades II', reason: 'Similar to games you play', confidence: 88, type: 'Game' },
  { title: 'Oppenheimer Re-release', reason: 'Trending with drama fans', confidence: 85, type: 'Movie' },
  { title: 'Charli XCX - BRAT', reason: 'New music trending in your genres', confidence: 79, type: 'Music' },
];

export function DiscoveryHub() {
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedFilter, setSelectedFilter] = useState('All');
  const [heroItems, setHeroItems] = useState<HeroItem[]>([]);
  const [liveTrendingItems, setLiveTrendingItems] = useState<HeroItem[]>([]);
  const [liveTrailerItems, setLiveTrailerItems] = useState<HeroItem[]>([]);
  const [liveUpcomingItems, setLiveUpcomingItems] = useState<HeroItem[]>([]);
  const [liveNewsItems, setLiveNewsItems] = useState<DiscoveryNewsPayload[]>([]);
  const [heroIndex, setHeroIndex] = useState(0);
  const [lastUpdated, setLastUpdated] = useState<Date>(new Date());
  const [infoItem, setInfoItem] = useState<HeroItem | null>(null);
  const [trailerVideoId, setTrailerVideoId] = useState<string | null>(null);

  const postToHost = (message: unknown) => {
    try {
      (window as any).chrome?.webview?.postMessage(message);
    } catch {
    }
  };

  const logDiscovery = (message: string) => {
    postToHost({
      type: 'servers.clientError',
      payload: {
        message,
        source: 'discovery-hub.tsx',
      },
    });
  };

  const buildInfoUrl = (title: string, category: HeroCategory, release: string): string => {
    const query = encodeURIComponent(`${title} ${category} ${release} info`);
    return `https://www.google.com/search?q=${query}`;
  };

  const buildTrailerSearchUrl = (title: string, category: HeroCategory): string | undefined => {
    const cleanTitle = (title ?? '').trim();
    if (!cleanTitle)
      return undefined;

    const suffix = category === 'Music'
      ? 'official video'
      : category === 'Game'
        ? 'official gameplay trailer'
        : 'official trailer';
    const query = encodeURIComponent(`${cleanTitle} ${suffix}`);
    return `https://www.youtube.com/results?search_query=${query}`;
  };

  const resolveTrailerUrl = (item: { title: string; category: HeroCategory; trailerUrl?: string }): string | undefined => {
    const direct = (item.trailerUrl ?? '').trim();
    if (direct)
      return direct;

    if (item.category === 'Movie' || item.category === 'TV' || item.category === 'Game' || item.category === 'Music')
      return buildTrailerSearchUrl(item.title, item.category);

    return undefined;
  };

  const parseReleaseDate = (value: string): Date | null => {
    const raw = (value ?? '').trim();
    if (!raw)
      return null;

    const direct = new Date(raw);
    if (!Number.isNaN(direct.getTime()))
      return direct;

    const isoPrefix = raw.match(/\d{4}-\d{2}-\d{2}/)?.[0] ?? '';
    if (isoPrefix) {
      const parsed = new Date(isoPrefix);
      if (!Number.isNaN(parsed.getTime()))
        return parsed;
    }

    return null;
  };

  const isWithinPastDays = (release: string, days: number): boolean => {
    const releaseDate = parseReleaseDate(release);
    if (!releaseDate)
      return false;

    const now = new Date();
    const start = new Date(now);
    start.setHours(0, 0, 0, 0);
    start.setDate(start.getDate() - days);

    const releaseOnly = new Date(releaseDate);
    releaseOnly.setHours(0, 0, 0, 0);

    return releaseOnly >= start && releaseOnly <= now;
  };

  const filterRecentReleases = (items: HeroItem[]): HeroItem[] =>
    items.filter((item) => isWithinPastDays(item.release, 10));

  const fallbackBackdropForCategory = (category: HeroCategory): string => {
    switch (category) {
      case 'Movie':
        return 'https://images.unsplash.com/photo-1489599849927-2ee91cede3ba?auto=format&fit=crop&w=1280&q=80';
      case 'TV':
        return 'https://images.unsplash.com/photo-1586899028174-e7098604235b?auto=format&fit=crop&w=1280&q=80';
      case 'Music':
        return 'https://images.unsplash.com/photo-1493225457124-a3eb161ffa5f?auto=format&fit=crop&w=1280&q=80';
      case 'Game':
        return 'https://images.unsplash.com/photo-1511512578047-dfb367046420?auto=format&fit=crop&w=1280&q=80';
      default:
        return 'https://images.unsplash.com/photo-1504711434969-e33886168f5c?auto=format&fit=crop&w=1280&q=80';
    }
  };

  const mapToHeroItem = (item: DiscoveryMediaPayload): HeroItem | null => {
    const type = (item.type ?? '').toLowerCase();

    const title = (item.title ?? '').trim();
    if (!title)
      return null;

    const category: HeroCategory =
      type === 'movie' ? 'Movie' :
      type === 'tv' ? 'TV' :
      type === 'music' ? 'Music' :
      type === 'game' ? 'Game' :
      'News';

    const providedBackdropUrl = ((item.backdropUrl ?? item.image) ?? '').trim();
    const backdropUrl = providedBackdropUrl || fallbackBackdropForCategory(category);

    const release = (item.releaseDate ?? '').trim() || (type === 'movie' ? 'Release date unknown' : type === 'tv' ? 'Air date unknown' : 'Upcoming');
    const trailerUrl = (item.trailerUrl ?? '').trim();
    const infoUrl = buildInfoUrl(title, category, release);

    const source =
      category === 'Movie' ? 'Movie · TMDB' :
      category === 'TV' ? 'TV · TMDB' :
      category === 'Music' ? 'Music · Latest/Upcoming' :
      category === 'Game' ? 'Game · Latest/Upcoming' :
      'News · Latest/Upcoming';

    const defaultRuntime =
      category === 'Movie' ? 'Movie' :
      category === 'TV' ? 'Series' :
      category;

    return {
      id: (item.id ?? `${type}-${title}`).toString(),
      title,
      backdropUrl,
      rating: typeof item.rating === 'number' && Number.isFinite(item.rating) ? item.rating : 0,
      runtime: (item.runtime ?? '').trim() || defaultRuntime,
      release,
      genres: Array.isArray(item.genres) ? item.genres.filter(Boolean) : [category],
      source,
      description: (item.overview ?? '').trim() || 'No overview available.',
      category,
      canPlay: category === 'Movie' || category === 'TV',
      hasTrailer: Boolean(trailerUrl) || category === 'Movie' || category === 'TV' || category === 'Game' || category === 'Music',
      hasDetails: true,
      trailerUrl: trailerUrl || undefined,
      isNewSeason: category === 'TV' && (() => {
        const rd = (item.releaseDate ?? '').trim();
        if (!rd) return false;
        const aired = new Date(rd);
        const twelveMonthsAgo = new Date();
        twelveMonthsAgo.setFullYear(twelveMonthsAgo.getFullYear() - 1);
        return aired < twelveMonthsAgo;
      })(),
    };
  };

  const requestDiscoveryData = () => {
    logDiscovery('[DiscoveryHeroData] frontend.request discovery.getData');
    logDiscovery('[DiscoveryMovieTv] frontend.request discovery.getData');
    postToHost({ type: 'discovery.getData' });
  };

  const openHeroTrailer = async (item: HeroItem) => {
    // 1. Try direct trailerUrl (TMDB provides youtube.com/watch?v= URLs)
    if (item.trailerUrl) {
      const videoId = extractYouTubeVideoId(item.trailerUrl);
      if (videoId) { setTrailerVideoId(videoId); return; }
    }
    // 2. Search YouTube by title
    const videoId = await fetchYouTubeVideoId(item.title, item.category);
    if (videoId) setTrailerVideoId(videoId);
  };

  const openHeroInfo = (item: HeroItem) => {
    logDiscovery(`[DiscoveryMovieTv] info.open id=${item.id} title=${item.title}`);
    setInfoItem(item);
  };

  const closeHeroInfo = () => {
    logDiscovery('[DiscoveryMovieTv] info.close');
    setInfoItem(null);
  };

  const shuffleArray = <T,>(items: T[]): T[] => {
    const clone = [...items];
    for (let i = clone.length - 1; i > 0; i -= 1) {
      const j = Math.floor(Math.random() * (i + 1));
      [clone[i], clone[j]] = [clone[j], clone[i]];
    }
    return clone;
  };

  const buildMixedHeroFeed = (featured: HeroItem | null, trailers: HeroItem[], trending: HeroItem[], upcoming: HeroItem[]): HeroItem[] => {
    const trailerPool = shuffleArray(trailers);
    const trendingPool = shuffleArray(trending.filter((item) => item.category !== 'News'));
    const upcomingPool = shuffleArray(upcoming.filter((item) => item.category !== 'News'));

    const byCategory: Record<HeroCategory, HeroItem[]> = {
      Movie: shuffleArray([...trailerPool, ...trendingPool, ...upcomingPool].filter((item) => item.category === 'Movie')),
      TV: shuffleArray([...trailerPool, ...trendingPool, ...upcomingPool].filter((item) => item.category === 'TV')),
      Music: shuffleArray([...trendingPool, ...upcomingPool].filter((item) => item.category === 'Music')),
      Game: shuffleArray([...trendingPool, ...upcomingPool].filter((item) => item.category === 'Game')),
      News: shuffleArray([...trendingPool, ...upcomingPool].filter((item) => item.category === 'News')),
    };

    const order: HeroCategory[] = shuffleArray(['Movie', 'TV', 'Music', 'Game', 'Movie', 'TV', 'Game', 'Music']);
    const merged: HeroItem[] = [];

    if (featured)
      merged.push(featured);

    for (const category of order) {
      const next = byCategory[category].shift();
      if (!next)
        continue;
      if (merged.some((existing) => existing.id === next.id))
        continue;
      merged.push(next);
    }

    for (const extra of shuffleArray([...trailerPool, ...trendingPool, ...upcomingPool])) {
      if (merged.some((existing) => existing.id === extra.id))
        continue;
      merged.push(extra);
      if (merged.length >= 14)
        break;
    }

    return merged;
  };

  useEffect(() => {
    const webview = (window as any).chrome?.webview;
    if (!webview?.addEventListener)
      return;

    const onMessage = (event: any) => {
      const message = event?.data;
      const messageType = (message?.type ?? '').toString();
      if (messageType)
        logDiscovery(`[DiscoveryHeroData] frontend.message type=${messageType}`);

      if (!message || message.type !== 'discovery.data')
        return;

      logDiscovery('[DiscoveryHeroData] frontend.discovery.received=true');
      logDiscovery('[DiscoveryMovieTv] frontend.received=true');
      const payload = (message.payload ?? {}) as DiscoveryDataPayload;
      const payloadKeys = Object.keys(payload ?? {}).join(',');
      logDiscovery(`[DiscoveryHeroData] frontend.payload.keys=${payloadKeys}`);
      const featured = payload.featured ? mapToHeroItem(payload.featured) : null;
      const trending = Array.isArray(payload.trending)
        ? payload.trending.map(mapToHeroItem).filter((hero): hero is HeroItem => hero !== null)
        : [];
      const trailers = Array.isArray(payload.trailers)
        ? payload.trailers.map(mapToHeroItem).filter((hero): hero is HeroItem => hero !== null)
        : [];
      const upcoming = Array.isArray(payload.upcoming)
        ? payload.upcoming.map(mapToHeroItem).filter((hero): hero is HeroItem => hero !== null)
        : [];
      const news = Array.isArray(payload.news) ? payload.news : [];

      // Dedicated recent Movie/TV items from backend (date-filtered by backend, no client filter needed)
      const heroMovieTvRaw = Array.isArray(payload.heroMovieTv)
        ? payload.heroMovieTv.map(mapToHeroItem).filter((hero): hero is HeroItem => hero !== null)
        : [];
      logDiscovery(`[DiscoveryMovieTv] frontend.heroItems count=${heroMovieTvRaw.length}`);

      const recentTrending = filterRecentReleases(trending);
      const recentTrailers = filterRecentReleases(trailers);
      const recentUpcoming = filterRecentReleases(upcoming);
      const recentFeatured = featured && isWithinPastDays(featured.release, 10) ? featured : null;

      setLiveTrendingItems(recentTrending);
      setLiveTrailerItems(recentTrailers);
      setLiveUpcomingItems(recentUpcoming);
      setLiveNewsItems(news);

      if (heroMovieTvRaw.length > 0) {
        // Use dedicated recent movie/TV feed — shuffle to mix movies and TV
        const shuffled = shuffleArray(heroMovieTvRaw);
        setHeroItems(shuffled);
        setHeroIndex(0);
        setLastUpdated(new Date());
        logDiscovery(`[DiscoveryHeroData] frontend.mapped.count=${shuffled.length}`);
        logDiscovery('[DiscoveryHeroData] frontend.usingFallback=false');
        logDiscovery('[DiscoveryMovieTv] frontend.usingFallback=false');
      } else {
        // Fall back to mixed trending/trailers/upcoming if heroMovieTv is empty
        const merged = buildMixedHeroFeed(recentFeatured, recentTrailers, recentTrending, recentUpcoming);
        logDiscovery(`[DiscoveryHeroData] frontend.mapped.count=${merged.length}`);
        logDiscovery('[DiscoveryMovieTv] frontend.usingFallback=true');

        if (merged.length > 0) {
          setHeroItems(merged);
          setHeroIndex(0);
          setLastUpdated(new Date());
          logDiscovery('[DiscoveryHeroData] frontend.usingFallback=false');
        } else {
          setHeroItems([]);
          setLastUpdated(new Date());
          logDiscovery('[DiscoveryHeroData] frontend.usingFallback=none');
        }
      }
    };

    webview.addEventListener('message', onMessage);
    requestDiscoveryData();

    return () => {
      webview.removeEventListener?.('message', onMessage);
    };
  }, []);

  useEffect(() => {
    const refreshTimer = window.setInterval(() => {
      requestDiscoveryData();
    }, 45000);

    return () => window.clearInterval(refreshTimer);
  }, []);

  useEffect(() => {
    if (heroItems.length <= 1)
      return;

    const rotation = window.setInterval(() => {
      setHeroIndex((current) => (current + 1) % heroItems.length);
    }, 10000);

    return () => window.clearInterval(rotation);
  }, [heroItems.length]);

  useEffect(() => {
    if (heroItems.length === 0) return;
    const current = heroItems[heroIndex % heroItems.length];
    if (current) {
      logDiscovery(`[DiscoveryMovieTv] current type=${current.category} title=${current.title}`);
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [heroIndex, heroItems]);

  const featuredDiscovery = heroItems.length > 0 ? heroItems[heroIndex % heroItems.length] : null;
  const previousHero = () => {
    if (heroItems.length <= 1)
      return;
    setHeroIndex((current) => (current - 1 + heroItems.length) % heroItems.length);
  };
  const nextHero = () => {
    if (heroItems.length <= 1)
      return;
    setHeroIndex((current) => (current + 1) % heroItems.length);
  };

  const liveRadar = liveTrendingItems.slice(0, 6).map((item, index) => ({
    type: item.category,
    title: item.title,
    summary: item.description,
    time: item.release,
    heat: Math.min(99, Math.max(1, Math.round(item.rating * 10) || 70)),
    verdict: item.rating >= 8 ? 'Must Watch' : item.rating >= 7 ? 'Trending Fast' : 'Worth Tracking',
    verdictColor: item.rating >= 8 ? 'bg-green-500/30 text-green-200' : item.rating >= 7 ? 'bg-cyan-500/30 text-cyan-200' : 'bg-amber-500/30 text-amber-200',
    key: `${item.id}-${index}`,
    trailerUrl: resolveTrailerUrl(item),
    infoUrl: buildInfoUrl(item.title, item.category, item.release),
  }));

  const liveTrailers = liveTrailerItems
    .filter((item) => isWithinPastDays(item.release, 10))
    .slice(0, 6)
    .map((item, index) => ({
    id: `${item.id}-${index}`,
    title: item.title.replace(/\s+-\s+Official Trailer$/i, ''),
    type: item.category,
    releaseDate: item.release,
    runtime: item.runtime,
    verdict: item.rating >= 8 ? 'Must Watch' : 'Worth Watching',
    verdictColor: item.rating >= 8 ? 'bg-green-500/30 text-green-200' : 'bg-cyan-500/30 text-cyan-200',
    trailerUrl: item.trailerUrl,
  }));

  const liveComingSoon = liveUpcomingItems
    .filter((item) => isWithinPastDays(item.release, 10))
    .slice(0, 6)
    .map((item, index) => ({
    key: `${item.id}-${index}`,
    title: item.title,
    date: item.release,
    platform: item.source,
    type: item.category,
    interest: Math.min(99, Math.max(1, Math.round(item.rating * 10) || 70)),
    summary: item.description,
    trailerUrl: resolveTrailerUrl(item),
    infoUrl: buildInfoUrl(item.title, item.category, item.release),
  }));

  const topComingSoonCards = liveComingSoon.length > 0
    ? liveComingSoon
    : liveRadar
      .filter((item) => isWithinPastDays(item.time, 10))
      .map((item) => ({
      key: item.key,
      title: item.title,
      date: item.time,
      platform: item.type,
      type: item.type,
      interest: item.heat,
      summary: item.summary,
      trailerUrl: item.trailerUrl,
      infoUrl: item.infoUrl,
    }));

  const liveHotRightNow = liveTrendingItems.slice(0, 6).map((item, index) => ({
    rank: index + 1,
    title: item.title,
    category: item.category,
    buzz: Math.min(99, Math.max(1, Math.round(item.rating * 10) || 70)),
    trend: item.rating >= 8 ? 'up' : item.rating >= 6.5 ? 'stable' : 'down',
    reason: item.description,
  }));

  return (
    <div className="space-y-6">
      <div className="relative rounded-2xl overflow-hidden border border-cyan-400/15 ring-1 ring-cyan-500/10" style={{ height: 280 }}>
        {featuredDiscovery ? (
          <img src={featuredDiscovery.backdropUrl} alt={featuredDiscovery.title} className="absolute inset-0 w-full h-full object-cover" />
        ) : (
          <div className="absolute inset-0 bg-gradient-to-br from-slate-900 to-slate-950" />
        )}
        <div className="absolute inset-0 bg-gradient-to-r from-slate-950 via-slate-950/80 to-slate-950/10" />
        <div className="absolute inset-0 bg-gradient-to-t from-slate-950/95 via-transparent to-slate-950/40" />
        <div className="absolute inset-0" style={{ background: 'radial-gradient(ellipse at 75% 50%, rgba(34,211,238,0.12) 0%, transparent 60%)' }} />

        <div className="absolute right-4 bottom-4 z-50 flex items-center gap-2">
          <button
            type="button"
            onClick={previousHero}
            className="h-9 w-9 rounded-full bg-slate-950/85 border border-cyan-400/55 text-cyan-100 hover:text-white hover:bg-slate-900 transition shadow-lg shadow-cyan-500/20"
            aria-label="Previous hero"
          >
            <ChevronLeft size={18} className="mx-auto" />
          </button>
          <button
            type="button"
            onClick={nextHero}
            className="h-9 w-9 rounded-full bg-slate-950/85 border border-cyan-400/55 text-cyan-100 hover:text-white hover:bg-slate-900 transition shadow-lg shadow-cyan-500/20"
            aria-label="Next hero"
          >
            <ChevronRight size={18} className="mx-auto" />
          </button>
        </div>

        <div className="relative h-full flex flex-col justify-end p-5 max-w-[58%]">
          {featuredDiscovery ? (
            <>
          <div className="flex items-center gap-2 mb-2">
            <span className="px-2 py-0.5 rounded-md bg-cyan-500/20 border border-cyan-400/40 text-cyan-200 text-[10px]">
              Featured
            </span>
            <span className="px-2 py-0.5 rounded-md bg-slate-900/80 border border-slate-700/60 text-slate-200 text-[10px]">
              {featuredDiscovery.source}
            </span>
            <span className="px-2 py-0.5 rounded-md bg-violet-500/15 border border-violet-400/30 text-violet-200 text-[10px]">
              {featuredDiscovery.category}
            </span>
            {featuredDiscovery.isNewSeason && (
              <span className="px-2 py-0.5 rounded-md bg-emerald-500/20 border border-emerald-400/40 text-emerald-200 text-[10px]">
                New Season
              </span>
            )}
          </div>
          <div className="text-slate-50" style={{ fontSize: 30, letterSpacing: '-0.01em', textShadow: '0 2px 24px rgba(0,0,0,0.7)' }}>
            {featuredDiscovery.title}
          </div>
          <div className="flex items-center gap-3 mt-1.5 text-slate-300 text-xs">
            <span>{featuredDiscovery.rating.toFixed(1)}</span>
            <span>{featuredDiscovery.runtime}</span>
            <span>{featuredDiscovery.release}</span>
          </div>
          <div className="flex flex-wrap gap-1.5 mt-2">
            {featuredDiscovery.genres.map((genre) => (
              <span key={genre} className="px-2 py-0.5 rounded-full bg-slate-900/70 border border-slate-700/60 text-slate-300 text-[10px]">{genre}</span>
            ))}
          </div>
          <p className="text-slate-300/90 text-xs mt-2.5 line-clamp-2 max-w-[520px]">{featuredDiscovery.description}</p>
          <div className="flex items-center gap-2 mt-3">
            {featuredDiscovery.canPlay ? (
              <button
                className="px-3 py-1.5 rounded-full bg-cyan-500/20 hover:bg-cyan-500/30 border border-cyan-400/40 text-cyan-200 text-xs flex items-center gap-1.5"
                onClick={() => openHeroInfo(featuredDiscovery)}
              >
                <Play size={11} /> Play
              </button>
            ) : null}
            {featuredDiscovery.hasTrailer ? (
              <button
                className="px-3 py-1.5 rounded-full bg-slate-900/80 hover:bg-slate-800 border border-slate-700/60 text-slate-200 text-xs flex items-center gap-1.5"
                onClick={() => openHeroTrailer(featuredDiscovery)}
              >
                <Film size={11} /> Trailer
              </button>
            ) : null}
            <button
              className="px-3 py-1.5 rounded-full bg-slate-900/80 hover:bg-slate-800 border border-slate-700/60 text-slate-200 text-xs flex items-center gap-1.5"
              onClick={() => openHeroInfo(featuredDiscovery)}
            >
              <Info size={11} /> Info
            </button>
          </div>
            </>
          ) : (
            <div className="text-slate-300 text-sm">Live discovery feed is unavailable. Check provider keys in Settings and tap Refresh Intel.</div>
          )}
        </div>
      </div>
      {/* Discovery Search Bar */}
      <div className="space-y-3">
        <div className="flex items-center gap-3">
          <div className="flex-1 relative">
            <div className="flex items-center gap-3 px-5 py-3 bg-gradient-to-r from-slate-900/80 to-slate-800/80 backdrop-blur-xl rounded-xl border border-slate-700/50 hover:border-cyan-500/30 transition-all">
              <Sparkles size={20} className="text-cyan-400" />
              <input
                type="text"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                placeholder="Ask what's hot, what's new, what's coming, or what's worth skipping…"
                className="flex-1 bg-transparent text-slate-200 placeholder:text-slate-500 outline-none"
              />
              <button className="p-2 rounded-lg bg-gradient-to-r from-cyan-500 to-purple-500 text-white hover:shadow-lg transition-all">
                <Search size={16} />
              </button>
            </div>
          </div>
          <button
            className="flex items-center gap-2 px-4 py-3 rounded-xl bg-slate-800/50 border border-slate-700 text-slate-300 hover:text-white hover:border-cyan-500/50 transition-all"
            onClick={requestDiscoveryData}
          >
            <RefreshCw size={16} />
            <span className="text-sm">Refresh Intel</span>
          </button>
        </div>

        {/* Filter Chips & Status */}
        <div className="flex items-center justify-between">
          <div className="flex flex-wrap gap-2">
            {filterChips.map((chip) => (
              <button
                key={chip}
                onClick={() => setSelectedFilter(chip)}
                className={`px-3 py-1.5 rounded-lg text-xs transition-all ${
                  selectedFilter === chip
                    ? 'bg-gradient-to-r from-cyan-500 to-purple-500 text-white'
                    : 'bg-slate-800/50 text-slate-400 hover:text-slate-200 hover:bg-slate-700/50'
                }`}
              >
                {chip}
              </button>
            ))}
          </div>
          <div className="flex items-center gap-2 text-xs text-slate-500">
            <Clock size={12} />
            <span>Updated {lastUpdated.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</span>
          </div>
        </div>
      </div>

      {/* New Releases (Past 10 Days) */}
      <div>
        <h3 className="text-slate-200 mb-4 flex items-center gap-2">
          <Target size={18} className="text-cyan-400" />
          New Releases (Past 10 Days)
        </h3>
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
          {topComingSoonCards.map((item) => (
            <div
              key={item.key}
              className="relative bg-gradient-to-br from-slate-900/80 to-slate-800/80 backdrop-blur-xl rounded-xl border border-slate-700/50 p-4 hover:border-cyan-500/30 transition-all group"
            >
              <div className="flex items-start justify-between mb-2">
                <div className={`px-2 py-1 rounded-lg text-xs ${
                  item.type === 'Movie' ? 'bg-cyan-500/20 text-cyan-300' :
                  item.type === 'TV' ? 'bg-purple-500/20 text-purple-300' :
                  item.type === 'Game' ? 'bg-green-500/20 text-green-300' :
                  item.type === 'Music' ? 'bg-pink-500/20 text-pink-300' :
                  'bg-amber-500/20 text-amber-300'
                }`}>
                  {item.type}
                </div>
                <div className="flex items-center gap-2">
                  <span className="text-xs text-slate-500">{item.date}</span>
                  <div className="flex items-center gap-1 px-2 py-0.5 rounded bg-amber-500/20 text-amber-300 text-xs">
                    🔥 {item.interest}
                  </div>
                </div>
              </div>
              <h4 className="text-slate-100 mb-1">{item.title}</h4>
              <p className="text-sm text-slate-400 mb-3 line-clamp-4">{item.summary}</p>
              <div className="text-xs text-slate-500 mb-3">{item.platform}</div>
              <div className="flex items-center gap-2">
                <button
                  className="px-3 py-1.5 rounded-full bg-slate-900/80 hover:bg-slate-800 border border-slate-700/60 text-slate-200 text-xs flex items-center gap-1.5"
                  onClick={() => item.trailerUrl ? postToHost({ type: 'mediahub.openExternalUrl', payload: { url: item.trailerUrl } }) : undefined}
                >
                  <Film size={11} /> Trailer
                </button>
                <button
                  className="px-3 py-1.5 rounded-full bg-slate-900/80 hover:bg-slate-800 border border-slate-700/60 text-slate-200 text-xs flex items-center gap-1.5"
                  onClick={() => postToHost({ type: 'mediahub.openExternalUrl', payload: { url: item.infoUrl } })}
                >
                  <Info size={11} /> Info
                </button>
              </div>
            </div>
          ))}
        </div>
        {topComingSoonCards.length === 0 ? (
          <p className="text-slate-500 text-sm mt-3">No verified releases found in the last 10 days.</p>
        ) : null}
      </div>

      {/* Latest Trailers */}
      <div>
        <h3 className="text-slate-200 mb-4">Latest Trailers</h3>
        <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 xl:grid-cols-6 gap-4">
          {liveTrailers.map((trailer) => (
            <div
              key={trailer.id}
              className="relative group"
            >
              <div className="relative rounded-xl overflow-hidden bg-slate-800 aspect-[16/9]">
                <div className="absolute inset-0 bg-gradient-to-br from-slate-700 to-slate-900 flex items-center justify-center">
                  <Play size={32} className="text-white/60 group-hover:text-white/90 group-hover:scale-110 transition-all" />
                </div>
                <div className="absolute top-2 left-2 px-2 py-0.5 rounded text-xs backdrop-blur-sm"
                  style={{
                    background: trailer.type === 'Movie' ? 'rgba(6, 182, 212, 0.3)' :
                               trailer.type === 'TV' ? 'rgba(168, 85, 247, 0.3)' :
                               'rgba(34, 197, 94, 0.3)',
                    color: trailer.type === 'Movie' ? '#67e8f9' :
                           trailer.type === 'TV' ? '#c4b5fd' :
                           '#86efac'
                  }}>
                  {trailer.type}
                </div>
                <div className="absolute top-2 right-2 px-2 py-0.5 rounded text-xs backdrop-blur-sm bg-slate-900/60 text-slate-300">
                  {trailer.runtime}
                </div>
              </div>
              <div className="mt-2">
                <h4 className="text-slate-200 text-sm truncate">{trailer.title}</h4>
                <div className="flex items-center justify-between text-xs mt-1">
                  <span className="text-slate-400">{trailer.releaseDate}</span>
                  <div className={`px-2 py-0.5 rounded ${trailer.verdictColor}`}>
                    {trailer.verdict}
                  </div>
                </div>
                <div className="flex gap-1 mt-2">
                  <button
                    className="flex-1 p-1.5 rounded-lg bg-slate-800/50 text-slate-300 hover:text-white hover:bg-slate-700 transition-all"
                    onClick={() => trailer.trailerUrl ? postToHost({ type: 'mediahub.openExternalUrl', payload: { url: trailer.trailerUrl } }) : undefined}
                  >
                    <Play size={12} className="mx-auto" />
                  </button>
                  <button
                    className="flex-1 p-1.5 rounded-lg bg-slate-800/50 text-slate-300 hover:text-white hover:bg-slate-700 transition-all"
                    onClick={() => postToHost({ type: 'mediahub.openExternalUrl', payload: { url: buildInfoUrl(trailer.title, trailer.type, trailer.releaseDate) } })}
                  >
                    <Info size={12} className="mx-auto" />
                  </button>
                </div>
              </div>
            </div>
          ))}
        </div>
        {liveTrailers.length === 0 ? (
          <p className="text-slate-500 text-sm mt-3">No verified live trailers available from current providers.</p>
        ) : null}
      </div>

      {/* Two-column layout for Coming Soon and Hot Right Now */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        {/* Coming Soon */}
        <div className="bg-gradient-to-br from-slate-900/80 to-slate-800/80 backdrop-blur-xl rounded-xl border border-slate-700/50 p-6">
          <h3 className="text-slate-100 mb-4">Coming Soon</h3>
          <div className="space-y-3">
            {liveComingSoon.map((item) => (
              <div
                key={item.key}
                className="flex items-center justify-between p-3 rounded-lg bg-slate-950/50 border border-slate-700/30 hover:border-purple-500/30 transition-all"
              >
                <div className="flex-1">
                  <h4 className="text-slate-200 text-sm">{item.title}</h4>
                  <div className="flex items-center gap-2 text-xs text-slate-400 mt-1">
                    <span>{item.date}</span>
                    <span>•</span>
                    <span>{item.platform}</span>
                  </div>
                </div>
                <div className="flex items-center gap-3">
                  <div className="text-right">
                    <div className="text-xs text-slate-500">Interest: {item.interest}</div>
                  </div>
                  <button className="p-1.5 rounded-lg bg-purple-500/20 text-purple-300 hover:bg-purple-500/30 transition-all">
                    <Bell size={14} />
                  </button>
                </div>
              </div>
            ))}
            {liveComingSoon.length === 0 ? (
              <p className="text-slate-500 text-sm">No live upcoming releases available right now.</p>
            ) : null}
          </div>
        </div>

        {/* Hot Right Now */}
        <div className="bg-gradient-to-br from-slate-900/80 to-slate-800/80 backdrop-blur-xl rounded-xl border border-slate-700/50 p-6">
          <h3 className="text-slate-100 mb-4">Hot Right Now</h3>
          <div className="space-y-3">
            {liveHotRightNow.map((item) => (
              <div
                key={item.rank}
                className="flex items-center gap-3 p-3 rounded-lg bg-slate-950/50 border border-slate-700/30 hover:border-amber-500/30 transition-all"
              >
                <div className="w-8 h-8 rounded-lg bg-gradient-to-br from-amber-500 to-orange-500 flex items-center justify-center text-white shrink-0">
                  {item.rank}
                </div>
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-2">
                    <h4 className="text-slate-200 text-sm truncate">{item.title}</h4>
                    <span className="px-2 py-0.5 rounded text-xs bg-slate-800 text-slate-400">{item.category}</span>
                  </div>
                  <p className="text-xs text-slate-400 mt-1 truncate">{item.reason}</p>
                </div>
                <div className="flex items-center gap-2 shrink-0">
                  <span className="text-amber-300 text-sm">{item.buzz}</span>
                  {item.trend === 'up' && <TrendingUp size={16} className="text-green-400" />}
                  {item.trend === 'down' && <TrendingDown size={16} className="text-red-400" />}
                  {item.trend === 'stable' && <Minus size={16} className="text-slate-400" />}
                </div>
              </div>
            ))}
            {liveHotRightNow.length === 0 ? (
              <p className="text-slate-500 text-sm">No live trending feed available right now.</p>
            ) : null}
          </div>
        </div>
      </div>

      {/* Skip Radar */}
      <div className="bg-gradient-to-br from-slate-900/80 to-slate-800/80 backdrop-blur-xl rounded-xl border border-red-500/20 p-6">
        <h3 className="text-slate-100 mb-4 flex items-center gap-2">
          <AlertTriangle size={18} className="text-red-400" />
          Skip Radar
        </h3>
        <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
          {skipRadar.map((item, index) => (
            <div
              key={index}
              className="p-4 rounded-lg bg-slate-950/50 border border-slate-700/30"
            >
              <div className="flex items-start justify-between mb-2">
                <span className="px-2 py-1 rounded bg-slate-800 text-slate-400 text-xs">{item.category}</span>
                <span className="text-xs text-red-400">Risk: {item.risk}</span>
              </div>
              <h4 className="text-slate-200 text-sm mb-2">{item.title}</h4>
              <p className="text-xs text-slate-400 mb-3">{item.reason}</p>
              <div className={`px-2 py-1 rounded text-xs inline-block ${item.verdictColor}`}>
                {item.verdict}
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* Celebrity & Entertainment News */}
      <div>
        <h3 className="text-slate-200 mb-4">Celebrity & Entertainment News</h3>
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          {liveNewsItems.map((item, index) => (
            <div
              key={index}
              className="flex gap-4 p-4 bg-gradient-to-br from-slate-900/80 to-slate-800/80 backdrop-blur-xl rounded-xl border border-slate-700/50 hover:border-purple-500/30 transition-all group cursor-pointer"
              onClick={() => item.url ? postToHost({ type: 'mediahub.openExternalUrl', payload: { url: item.url } }) : undefined}
            >
              <div className="w-20 h-20 rounded-lg bg-gradient-to-br from-purple-600 to-pink-600 shrink-0 flex items-center justify-center">
                <User size={32} className="text-white/30" />
              </div>
              <div className="flex-1 min-w-0">
                <h4 className="text-slate-100 text-sm mb-1 group-hover:text-purple-300 transition-colors">{item.headline ?? 'Untitled news'}</h4>
                <div className="flex items-center gap-2 text-xs text-slate-400">
                  <span className="px-2 py-0.5 rounded bg-purple-500/20 text-purple-300">News</span>
                  <span>{item.source ?? 'Unknown source'}</span>
                  <span>•</span>
                  <span>{item.timeAgo ?? 'recently'}</span>
                </div>
                <button className="mt-2 text-xs text-cyan-400 hover:text-cyan-300 transition-colors">
                  AI Summary →
                </button>
              </div>
            </div>
          ))}
        </div>
        {liveNewsItems.length === 0 ? (
          <p className="text-slate-500 text-sm mt-3">No live entertainment news available right now.</p>
        ) : null}
      </div>

      {/* AI Picks For You */}
      <div className="bg-gradient-to-br from-slate-900/80 to-slate-800/80 backdrop-blur-xl rounded-xl border border-purple-500/20 p-6">
        <h3 className="text-slate-100 mb-4 flex items-center gap-2">
          <Sparkles size={18} className="text-purple-400" />
          AI Picks For You
        </h3>
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
          {aiPicks.map((pick, index) => (
            <div
              key={index}
              className="p-4 rounded-lg bg-slate-950/50 border border-slate-700/30 hover:border-purple-500/30 transition-all"
            >
              <div className="flex items-center justify-between mb-3">
                <span className="px-2 py-1 rounded bg-purple-500/20 text-purple-300 text-xs">{pick.type}</span>
                <span className="text-xs text-cyan-400">{pick.confidence}% match</span>
              </div>
              <h4 className="text-slate-100 text-sm mb-2">{pick.title}</h4>
              <p className="text-xs text-slate-400 mb-3">{pick.reason}</p>
              <button className="w-full py-2 rounded-lg bg-gradient-to-r from-purple-500 to-pink-500 text-white text-xs hover:shadow-lg hover:shadow-purple-500/30 transition-all">
                Explore
              </button>
            </div>
          ))}
        </div>
      </div>

      <div className="h-8" />

      {/* Info Overlay */}
      {infoItem ? (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center"
          style={{ background: 'rgba(0,0,0,0.75)', backdropFilter: 'blur(8px)' }}
          onClick={closeHeroInfo}
        >
          <div
            className="relative w-full max-w-2xl mx-4 rounded-2xl overflow-hidden shadow-2xl"
            style={{ maxHeight: '90vh', overflowY: 'auto' }}
            onClick={(e) => e.stopPropagation()}
          >
            {/* Backdrop */}
            <div className="relative" style={{ aspectRatio: '16/9', maxHeight: 280 }}>
              <img
                src={infoItem.backdropUrl}
                alt={infoItem.title}
                className="w-full h-full object-cover"
                onError={(e) => { (e.currentTarget as HTMLImageElement).style.display = 'none'; }}
              />
              <div className="absolute inset-0 bg-gradient-to-t from-slate-900 via-slate-900/40 to-transparent" />
              <button
                className="absolute top-3 right-3 p-2 rounded-full bg-slate-900/70 hover:bg-slate-800 text-slate-200 transition-all"
                onClick={closeHeroInfo}
              >
                ✕
              </button>
              <div className="absolute bottom-3 left-4">
                <span className={`px-2 py-0.5 rounded text-xs ${infoItem.category === 'Movie' ? 'bg-cyan-500/30 text-cyan-200' : 'bg-purple-500/30 text-purple-200'}`}>
                  {infoItem.category}
                </span>
              </div>
            </div>
            {/* Content */}
            <div className="bg-slate-900 p-6 space-y-3">
              <h2 className="text-slate-50 text-2xl" style={{ letterSpacing: '-0.01em' }}>{infoItem.title}</h2>
              <div className="flex items-center gap-3 text-slate-400 text-sm">
                {infoItem.rating > 0 ? <span>⭐ {infoItem.rating.toFixed(1)}</span> : null}
                {infoItem.runtime ? <span>{infoItem.runtime}</span> : null}
                {infoItem.release && infoItem.release !== 'Release date unknown' && infoItem.release !== 'Air date unknown' ? (
                  <span>{infoItem.release}</span>
                ) : null}
              </div>
              {infoItem.genres.length > 0 ? (
                <div className="flex flex-wrap gap-1.5">
                  {infoItem.genres.map((g) => (
                    <span key={g} className="px-2 py-0.5 rounded-full bg-slate-800 border border-slate-700 text-slate-300 text-xs">{g}</span>
                  ))}
                </div>
              ) : null}
              {infoItem.description && infoItem.description !== 'No overview available.' ? (
                <p className="text-slate-300 text-sm leading-relaxed">{infoItem.description}</p>
              ) : null}
              <div className="flex items-center gap-3 pt-2">
                {infoItem.hasTrailer && infoItem.trailerUrl ? (
                  <button
                    className="px-4 py-2 rounded-full bg-slate-800 hover:bg-slate-700 border border-slate-700 text-slate-200 text-sm flex items-center gap-2"
                    onClick={() => { openHeroTrailer(infoItem); closeHeroInfo(); }}
                  >
                    <Film size={13} /> Watch Trailer
                  </button>
                ) : null}
                <button
                  className="px-4 py-2 rounded-full bg-slate-800 hover:bg-slate-700 border border-slate-700 text-slate-400 text-sm"
                  onClick={closeHeroInfo}
                >
                  Close
                </button>
              </div>
            </div>
          </div>
        </div>
      ) : null}
    {trailerVideoId && (
      <TrailerModal videoId={trailerVideoId} title={heroItems[heroIndex]?.title ?? ''} onClose={() => setTrailerVideoId(null)} />
    )}
    </div>
  );
}
