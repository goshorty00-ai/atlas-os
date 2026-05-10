import { useState } from 'react';
import {
  Search, Pin, User, Plus, ExternalLink, Star, CheckCircle2, Globe,
  Download, Clock, AlertCircle, Grid3x3,
} from 'lucide-react';

type AppCategory = 'Movies & TV' | 'Music' | 'Games' | 'Live' | 'Sports' | 'Anime' | 'Aggregator';
type AppState = 'connected' | 'not-signed-in' | 'installed' | 'web-only' | 'update-available';

interface AppItem {
  id: string;
  name: string;
  category: AppCategory;
  color: string;
  initial: string;
  state: AppState;
  favourite?: boolean;
  recent?: boolean;
  featured?: boolean;
  tagline: string;
}

const APPS: AppItem[] = [
  { id: 'netflix',  name: 'Netflix',         category: 'Movies & TV', color: '#e50914', initial: 'N', state: 'connected',     favourite: true,  recent: true, featured: true,  tagline: 'Originals · Films · Series' },
  { id: 'prime',    name: 'Prime Video',     category: 'Movies & TV', color: '#00a8e1', initial: 'P', state: 'connected',     recent: true, featured: true, tagline: 'Movies · Series · Sports' },
  { id: 'disney',   name: 'Disney+',         category: 'Movies & TV', color: '#0a2240', initial: 'D', state: 'connected',     favourite: true, featured: true, tagline: 'Disney · Marvel · Star Wars' },
  { id: 'apple-tv', name: 'Apple TV+',       category: 'Movies & TV', color: '#1d1d1f', initial: 'A', state: 'not-signed-in', tagline: 'Premium originals' },
  { id: 'paramount',name: 'Paramount+',      category: 'Movies & TV', color: '#0064ff', initial: 'P', state: 'web-only',      tagline: 'Films · Trek · Yellowstone' },
  { id: 'now',      name: 'NOW',             category: 'Movies & TV', color: '#00d4aa', initial: 'N', state: 'web-only',      tagline: 'Sky cinema & sports' },
  { id: 'youtube',  name: 'YouTube',         category: 'Live',         color: '#ff0033', initial: 'Y', state: 'connected',     favourite: true, recent: true, featured: true, tagline: 'Creators · Live · VOD' },
  { id: 'twitch',   name: 'Twitch',          category: 'Live',         color: '#9146ff', initial: 'T', state: 'connected',     recent: true, tagline: 'Live streaming' },
  { id: 'spotify',  name: 'Spotify',         category: 'Music',        color: '#1db954', initial: 'S', state: 'connected',     favourite: true, recent: true, featured: true, tagline: 'Music · Podcasts' },
  { id: 'apple-music', name: 'Apple Music',  category: 'Music',        color: '#fa233b', initial: 'A', state: 'not-signed-in', tagline: 'Lossless · Spatial' },
  { id: 'yt-music', name: 'YouTube Music',   category: 'Music',        color: '#ff0033', initial: 'Y', state: 'connected',     tagline: 'Albums · Mixes' },
  { id: 'soundcloud', name: 'SoundCloud',    category: 'Music',        color: '#ff5500', initial: 'S', state: 'web-only',      tagline: 'Underground · DJ sets' },
  { id: 'steam',    name: 'Steam',           category: 'Games',        color: '#1b2838', initial: 'S', state: 'installed',     favourite: true, recent: true, tagline: 'PC library' },
  { id: 'xbox',     name: 'Xbox Cloud',      category: 'Games',        color: '#107c10', initial: 'X', state: 'connected',     tagline: 'Cloud gaming' },
  { id: 'gfn',      name: 'GeForce Now',     category: 'Games',        color: '#76b900', initial: 'G', state: 'update-available', tagline: 'RTX cloud streaming' },
  { id: 'crunchy',  name: 'Crunchyroll',     category: 'Anime',        color: '#f47521', initial: 'C', state: 'connected',     favourite: true, tagline: 'Anime · Manga · Simulcast' },
  { id: 'plex',     name: 'Plex',            category: 'Aggregator',   color: '#e5a00d', initial: 'P', state: 'installed',     featured: true, tagline: 'Personal media server' },
  { id: 'jellyfin', name: 'Jellyfin',        category: 'Aggregator',   color: '#aa5cc3', initial: 'J', state: 'installed',     tagline: 'Open-source media' },
];

const FILTERS = ['All', 'Movies & TV', 'Music', 'Games', 'Live', 'Sports', 'Anime', 'Installed', 'Favourites'] as const;

const STATE_META: Record<AppState, { label: string; cls: string }> = {
  'connected':         { label: 'Connected',        cls: 'bg-green-500/15 text-green-300 border-green-400/30' },
  'not-signed-in':     { label: 'Not signed in',    cls: 'bg-amber-500/15 text-amber-200 border-amber-400/30' },
  'installed':         { label: 'Installed',        cls: 'bg-cyan-500/15 text-cyan-200 border-cyan-400/30' },
  'web-only':          { label: 'Web only',         cls: 'bg-slate-700/40 text-slate-300 border-slate-600/50' },
  'update-available':  { label: 'Update available', cls: 'bg-violet-500/15 text-violet-200 border-violet-400/30' },
};

export function AppsPage() {
  const [filter, setFilter] = useState<(typeof FILTERS)[number]>('All');
  const [query, setQuery] = useState('');

  const filtered = APPS.filter((a) => {
    if (query && !a.name.toLowerCase().includes(query.toLowerCase())) return false;
    if (filter === 'All') return true;
    if (filter === 'Installed')  return a.state === 'installed' || a.state === 'connected';
    if (filter === 'Favourites') return a.favourite;
    if (filter === 'Sports')     return false;
    return a.category === filter;
  });

  const featured  = APPS.filter((a) => a.featured);
  const moviesTV  = filtered.filter((a) => a.category === 'Movies & TV');
  const music     = filtered.filter((a) => a.category === 'Music');
  const games     = filtered.filter((a) => a.category === 'Games');
  const live      = filtered.filter((a) => a.category === 'Live' || a.category === 'Anime' || a.category === 'Aggregator');

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center gap-3">
        <div className="p-2 rounded-lg bg-gradient-to-br from-cyan-500 to-violet-500">
          <Grid3x3 size={18} className="text-white" />
        </div>
        <div>
          <div className="text-slate-100">Apps</div>
          <div className="text-slate-500 text-xs">Compact streaming launcher · {APPS.length} apps</div>
        </div>
      </div>

      {/* Search + filters */}
      <div className="space-y-2.5">
        <div className="flex items-center gap-2 px-3 py-2 rounded-xl bg-slate-900/70 border border-slate-700/60 backdrop-blur-md">
          <Search size={14} className="text-slate-400" />
          <input
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Search apps…"
            className="flex-1 bg-transparent text-sm text-slate-200 placeholder:text-slate-500 outline-none"
          />
        </div>
        <div className="flex flex-wrap gap-1.5">
          {FILTERS.map((f) => (
            <button key={f} onClick={() => setFilter(f)}
              className={`px-2.5 py-1 rounded-full text-[11px] border transition-colors ${
                filter === f
                  ? 'bg-cyan-500/20 text-cyan-200 border-cyan-400/40'
                  : 'bg-slate-900/60 text-slate-300 border-slate-700/60 hover:bg-slate-800'
              }`}>
              {f}
            </button>
          ))}
        </div>
      </div>

      <Section title="Featured Apps">
        {featured.map((a) => <AppCard key={a.id} app={a} />)}
      </Section>

      {moviesTV.length > 0 && <Section title="Movies & TV">{moviesTV.map((a) => <AppCard key={a.id} app={a} />)}</Section>}
      {music.length    > 0 && <Section title="Music">       {music.map((a) => <AppCard key={a.id} app={a} />)}</Section>}
      {games.length    > 0 && <Section title="Gaming">      {games.map((a) => <AppCard key={a.id} app={a} />)}</Section>}
      {live.length     > 0 && <Section title="Live & Creator">{live.map((a) => <AppCard key={a.id} app={a} />)}</Section>}
    </div>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div>
      <div className="flex items-center justify-between mb-2.5">
        <div className="text-slate-200 text-sm">{title}</div>
        <span className="text-slate-500 text-[10px]">{Array.isArray(children) ? children.length : 1} apps</span>
      </div>
      <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5 gap-2.5">
        {children}
      </div>
    </div>
  );
}

function AppCard({ app }: { app: AppItem }) {
  const meta = STATE_META[app.state];
  return (
    <div className="group relative rounded-xl border border-slate-700/50 bg-slate-900/60 backdrop-blur-md overflow-hidden hover:border-cyan-400/40 transition-colors">
      <div className="relative h-16" style={{ background: `linear-gradient(135deg, ${app.color} 0%, ${app.color}88 60%, rgba(15,23,42,0.9) 100%)` }}>
        <div className="absolute top-2 left-2.5 w-9 h-9 rounded-lg bg-slate-950/60 border border-white/15 flex items-center justify-center text-white" style={{ fontSize: 16 }}>
          {app.initial}
        </div>
        <div className="absolute top-2 right-2 flex items-center gap-1">
          {app.favourite && <Star size={10} className="text-amber-300" fill="currentColor" />}
          {app.recent && <Clock size={10} className="text-cyan-300" />}
        </div>
        <div className="absolute bottom-1.5 right-2">
          <span className={`px-1.5 py-0.5 rounded-full text-[9px] border backdrop-blur-sm ${meta.cls} flex items-center gap-1`}>
            {app.state === 'connected' && <CheckCircle2 size={8} />}
            {app.state === 'web-only' && <Globe size={8} />}
            {app.state === 'installed' && <Download size={8} />}
            {app.state === 'not-signed-in' && <AlertCircle size={8} />}
            {app.state === 'update-available' && <Download size={8} />}
            {meta.label}
          </span>
        </div>
      </div>
      <div className="p-2.5">
        <div className="text-slate-100 text-xs truncate">{app.name}</div>
        <div className="text-slate-500 text-[10px] truncate">{app.tagline}</div>
        <div className="mt-2 flex items-center gap-1">
          <button className="flex-1 px-2 py-1 rounded-md bg-cyan-500/90 hover:bg-cyan-400 text-slate-950 text-[10px] flex items-center justify-center gap-1">
            <ExternalLink size={9} /> Open
          </button>
          <IconBtn title="Pin"><Pin size={10} /></IconBtn>
          <IconBtn title="Account"><User size={10} /></IconBtn>
          <IconBtn title="Add to Sidebar"><Plus size={10} /></IconBtn>
        </div>
      </div>
    </div>
  );
}

function IconBtn({ children, title }: { children: React.ReactNode; title: string }) {
  return (
    <button title={title} className="p-1 rounded-md bg-slate-800/80 hover:bg-slate-700 border border-slate-600/60 text-slate-300 hover:text-cyan-200">
      {children}
    </button>
  );
}

export default AppsPage;
