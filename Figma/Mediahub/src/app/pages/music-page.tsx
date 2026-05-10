import { useEffect, useRef, useState } from 'react';
import {
  Sparkles, Search, Upload, Wand2, Image as ImageIcon, ListPlus, ChevronRight, Maximize2,
  Headphones, Mic2, Activity, Wrench, FileMusic, Copy, Volume2, X, Save, RotateCcw,
} from 'lucide-react';
import { MusicPlayer } from '../components/music-player';
import { MusicGalaxyExplorer } from '../components/music-galaxy-explorer';
import { AlbumCard, AlbumItem } from '../components/album-card';
import { CarouselOverlay } from '../components/carousel-overlay';
import { MediaItem } from '../components/server-card';

const COVERS = [
  'https://images.unsplash.com/photo-1510759704643-849552bf3b66?w=500&q=80',
  'https://images.unsplash.com/photo-1771301455501-694654813e1a?w=500&q=80',
  'https://images.unsplash.com/photo-1744390166624-3c06c599cc34?w=500&q=80',
  'https://images.unsplash.com/photo-1751042276781-1e3412ff4418?w=500&q=80',
  'https://images.unsplash.com/photo-1744390166596-d1d0c0a76aa9?w=500&q=80',
  'https://images.unsplash.com/photo-1744390166695-d797a8f913f1?w=500&q=80',
  'https://images.unsplash.com/photo-1744390166798-bb7d46da976b?w=500&q=80',
  'https://images.unsplash.com/photo-1744390166688-2218e1ca2524?w=500&q=80',
  'https://images.unsplash.com/photo-1744390166675-966abed5fcc8?w=500&q=80',
  'https://images.unsplash.com/photo-1744390166870-ba125465856b?w=500&q=80',
  'https://images.unsplash.com/photo-1744390166851-82056c6552d0?w=500&q=80',
  'https://images.unsplash.com/photo-1609667083964-f3dbecb7e7a5?w=500&q=80',
  'https://images.unsplash.com/photo-1744390166672-712d7efd46f7?w=500&q=80',
  'https://images.unsplash.com/photo-1762503634762-c62f68160aed?w=500&q=80',
  'https://images.unsplash.com/photo-1650902565793-c2eb262aefbc?w=500&q=80',
  'https://images.unsplash.com/photo-1768935434906-e4b1890feb70?w=500&q=80',
  'https://images.unsplash.com/photo-1767477665600-98f3a7ee6c00?w=500&q=80',
  'https://images.unsplash.com/photo-1766036387562-476134a99d28?w=500&q=80',
  'https://images.unsplash.com/photo-1765445665997-6553bd993ff1?w=500&q=80',
  'https://images.unsplash.com/photo-1767403154501-a677bb969ba6?w=500&q=80',
];

const ARTISTS = ['The Weeknd', 'Daft Punk', 'Pink Floyd', 'Tame Impala', 'Kendrick Lamar', 'Billie Eilish', 'Arctic Monkeys', 'Frank Ocean', 'Radiohead', 'Beyoncé'];
const TITLES  = ['Neon Dreams', 'After Hours', 'Discovery', 'Currents', 'Midnights', 'Awaken My Love', 'Random Access', 'Lost in Sound', 'Solar Flare', 'Moonlight'];
const GENRES  = ['Synthwave', 'Electronic', 'Rock', 'Pop', 'Hip-Hop', 'Jazz', 'Lo-Fi', 'Indie'];
const MOODS   = ['Chill', 'Focus', 'Energetic', 'Late Night', 'Workout', 'Romantic', 'Cinematic'];
const SOURCES = ['Plex', 'Jellyfin', 'Local', 'Spotify Add-on'];

const FILTERS = ['All', 'Albums', 'Artists', 'Songs', 'Playlists', 'Visualizer', 'Karaoke Ready', 'Soundtracks', 'High Energy', 'Chill', 'AI Picks'];

const SHELVES = [
  'Recently Played', 'Albums', 'Artists', 'AI Mood Mixes',
  'Soundtracks', 'Karaoke Ready', 'High Energy', 'Chill', 'Old Favourites', 'Hidden Gems',
];

const TOOLS = [
  { name: 'AI Playlist Builder', icon: ListPlus,    color: 'from-cyan-500 to-blue-500' },
  { name: 'Mood Match',          icon: Sparkles,    color: 'from-purple-500 to-fuchsia-500' },
  { name: 'Similar Vibe',        icon: Activity,    color: 'from-pink-500 to-rose-500' },
  { name: 'Metadata Fixer',      icon: Wrench,      color: 'from-amber-500 to-orange-500' },
  { name: 'Cover Generator',     icon: ImageIcon,   color: 'from-violet-500 to-purple-500' },
  { name: 'Lyrics Finder',       icon: FileMusic,   color: 'from-emerald-500 to-teal-500' },
  { name: 'Karaoke Converter',   icon: Mic2,        color: 'from-rose-500 to-red-500' },
  { name: 'BPM / Key Analyzer',  icon: Activity,    color: 'from-indigo-500 to-blue-500' },
  { name: 'Duplicate Finder',    icon: Copy,        color: 'from-slate-500 to-slate-600' },
  { name: 'Audio Optimizer',     icon: Volume2,     color: 'from-cyan-500 to-emerald-500' },
  { name: 'Soundtrack Finder',   icon: Headphones,  color: 'from-fuchsia-500 to-pink-500' },
];

const CONTEXT_MENU = [
  { label: 'Play Now', section: 'play' },
  { label: 'Add to Queue', section: 'play' },
  { label: 'Add to Playlist', section: 'play' },
  { label: 'Open Album', section: 'open' },
  { label: 'Open Artist', section: 'open' },
  { label: 'Show Similar Vibe', section: 'open' },
  { label: 'AI Optimize Audio', section: 'ai' },
  { label: 'Get Album Metadata', section: 'ai' },
  { label: 'Fix Track Metadata', section: 'ai' },
  { label: 'AI Generate Cover', section: 'cover' },
  { label: 'Edit Custom Cover', section: 'cover' },
  { label: 'Replace Cover Image', section: 'cover' },
  { label: 'Restore Original Cover', section: 'cover' },
  { label: 'Save Cover to Album', section: 'cover' },
  { label: 'Search Lyrics', section: 'lyrics' },
  { label: 'Sync Lyrics', section: 'lyrics' },
  { label: 'Convert to Karaoke', section: 'lyrics' },
  { label: 'Remove Vocals', section: 'lyrics' },
  { label: 'Analyse BPM / Key', section: 'analysis' },
  { label: 'Show File Location', section: 'analysis' },
  { label: 'Hide From Library', section: 'analysis' },
];

const pick = <T,>(arr: T[], i: number) => arr[i % arr.length];

const generateAlbums = (count: number, seed: number): AlbumItem[] =>
  Array.from({ length: count }, (_, i) => {
    const idx = (seed * 11 + i * 5) % COVERS.length;
    return {
      id: `s${seed}-${i}`,
      title: pick(TITLES, idx + i),
      artist: pick(ARTISTS, idx),
      year: `${2015 + ((idx * 3 + i) % 10)}`,
      tracks: 8 + (idx % 12),
      genre: pick(GENRES, idx),
      mood: pick(MOODS, idx),
      rating: 6 + ((idx * 13) % 40) / 10,
      source: pick(SOURCES, idx),
      coverUrl: COVERS[idx],
      bpm: 80 + (idx * 7) % 80,
    };
  });

const SHELF_DATA: Record<string, AlbumItem[]> = SHELVES.reduce((acc, name, i) => {
  acc[name] = generateAlbums(12, i + 1);
  return acc;
}, {} as Record<string, AlbumItem[]>);

function albumToMedia(a: AlbumItem): MediaItem {
  return {
    id: a.id,
    title: a.title,
    year: a.year,
    type: 'Music Video',
    server: a.source ?? 'Local',
    rating: a.rating,
    posterUrl: a.coverUrl,
    hasMetadata: true,
    hasArtwork: true,
    runtime: `${a.tracks} tracks`,
    genre: a.genre,
    description: `${a.artist} · ${a.mood ?? 'Music'}`,
  };
}

interface ContextState {
  x: number;
  y: number;
  album: AlbumItem;
}

export function MusicPage() {
  const [activeFilter, setActiveFilter] = useState<string>('All');
  const [context, setContext] = useState<ContextState | null>(null);
  const [carouselShelf, setCarouselShelf] = useState<string | null>(null);
  const [editAlbum, setEditAlbum] = useState<AlbumItem | null>(null);
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const close = () => setContext(null);
    window.addEventListener('click', close);
    window.addEventListener('scroll', close, true);
    return () => {
      window.removeEventListener('click', close);
      window.removeEventListener('scroll', close, true);
    };
  }, []);

  const openContext = (e: React.MouseEvent, album: AlbumItem) => {
    setContext({ x: e.clientX, y: e.clientY, album });
  };

  const handleContextAction = (label: string) => {
    if (!context) return;
    if (label.startsWith('Edit Custom Cover') || label === 'AI Generate Cover' || label === 'Replace Cover Image') {
      setEditAlbum(context.album);
    }
    setContext(null);
  };

  const carouselItems = carouselShelf ? SHELF_DATA[carouselShelf].map(albumToMedia) : [];

  return (
    <div ref={containerRef} className="relative space-y-5 pb-4">
      {/* 1. Top AI command row */}
      <div className="flex flex-col gap-2">
        <div className="flex items-center gap-2 rounded-xl border border-purple-500/20 bg-slate-950/60 backdrop-blur-xl px-3 py-2">
          <Sparkles size={14} className="text-purple-300 flex-shrink-0" />
          <Search size={13} className="text-slate-500" />
          <input
            placeholder="Ask AI for a vibe, playlist, artist, remix, cover, or metadata fix…"
            className="flex-1 bg-transparent outline-none text-slate-100 placeholder:text-slate-500 text-sm"
          />
          <div className="hidden md:flex items-center gap-1">
            <ActionPill icon={Upload} label="Import Music" />
            <ActionPill icon={Wand2} label="Generate Playlist" />
            <ActionPill icon={Wrench} label="Fix Metadata" />
            <ActionPill icon={ImageIcon} label="Create Covers" />
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-1.5">
          {FILTERS.map((f) => {
            const active = activeFilter === f;
            return (
              <button
                key={f}
                onClick={() => setActiveFilter(f)}
                className={`px-2.5 py-1 rounded-full text-[11px] border transition-colors ${
                  active
                    ? 'bg-cyan-500/15 border-cyan-400/50 text-cyan-200'
                    : 'bg-slate-900/40 border-slate-700/30 text-slate-400 hover:text-slate-200 hover:border-slate-600'
                }`}
              >
                {f}
              </button>
            );
          })}
        </div>
      </div>

      {/* 2. Music Galaxy Explorer */}
      <MusicGalaxyExplorer />

      {/* 7. AI music tools as compact chips */}
      <div className="space-y-2">
        <div className="flex items-baseline justify-between">
          <h3 className="text-slate-200 text-sm">AI Music Tools</h3>
          <span className="text-[10px] text-slate-500">{TOOLS.length} tools</span>
        </div>
        <div className="grid grid-cols-3 md:grid-cols-4 lg:grid-cols-6 gap-2">
          {TOOLS.map((t) => {
            const Icon = t.icon;
            return (
              <button
                key={t.name}
                className="flex items-center gap-2 px-2.5 py-2 rounded-lg bg-slate-900/60 border border-slate-700/40 hover:border-cyan-400/40 transition-colors group"
              >
                <span className={`p-1.5 rounded-md bg-gradient-to-br ${t.color}`}>
                  <Icon size={12} className="text-white" />
                </span>
                <span className="text-slate-200 text-[11px] truncate">{t.name}</span>
              </button>
            );
          })}
        </div>
      </div>

      {/* 3. Album / Artist shelves */}
      {SHELVES.map((title) => (
        <ShelfRow
          key={title}
          title={title}
          albums={SHELF_DATA[title]}
          onOpenContext={openContext}
          onOpenCarousel={() => setCarouselShelf(title)}
        />
      ))}

      {/* 4. Compact bottom player */}
      <MusicPlayer coverUrl={COVERS[0]} title="Neon Dreams" artist="Synthwave Collective" album="Cyberpunk 2077 OST" />

      {/* Carousel overlay */}
      {carouselShelf && (
        <CarouselOverlay
          isOpen
          items={carouselItems}
          shelfName={carouselShelf}
          onClose={() => setCarouselShelf(null)}
        />
      )}

      {/* Right-click context menu */}
      {context && <ContextMenu state={context} onAction={handleContextAction} />}

      {/* Edit drawer */}
      {editAlbum && (
        <EditAlbumDrawer album={editAlbum} onClose={() => setEditAlbum(null)} />
      )}
    </div>
  );
}

function ActionPill({ icon: Icon, label }: { icon: typeof Upload; label: string }) {
  return (
    <button className="flex items-center gap-1 px-2 py-1 rounded-md bg-slate-800/60 border border-slate-700/40 text-slate-200 text-[11px] hover:border-cyan-400/50 hover:text-cyan-200 transition-colors">
      <Icon size={11} />
      {label}
    </button>
  );
}

function ShelfRow({
  title,
  albums,
  onOpenContext,
  onOpenCarousel,
}: {
  title: string;
  albums: AlbumItem[];
  onOpenContext: (e: React.MouseEvent, a: AlbumItem) => void;
  onOpenCarousel: () => void;
}) {
  return (
    <div className="space-y-2">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <h3 className="text-slate-200 text-sm">{title}</h3>
          <span className="px-1.5 py-0.5 rounded-full bg-slate-800/60 text-slate-400 text-[10px]">
            {albums.length} items
          </span>
        </div>
        <div className="flex items-center gap-1">
          <button className="px-2 py-1 rounded-md bg-slate-800/60 border border-slate-700/40 text-slate-300 text-[11px] hover:border-slate-500 transition-colors">
            Sort
          </button>
          <button className="flex items-center gap-1 px-2 py-1 rounded-md bg-slate-800/60 border border-slate-700/40 text-slate-300 text-[11px] hover:border-slate-500 transition-colors">
            View All <ChevronRight size={11} />
          </button>
          <button
            onClick={onOpenCarousel}
            className="flex items-center gap-1 px-2 py-1 rounded-md bg-gradient-to-r from-cyan-500 to-purple-500 text-white text-[11px] hover:shadow-lg hover:shadow-cyan-500/30 transition-all"
          >
            <Maximize2 size={11} /> Open Carousel
          </button>
        </div>
      </div>
      <div className="flex gap-3 overflow-x-auto scrollbar-hide pb-2">
        {albums.map((a) => (
          <AlbumCard key={a.id} album={a} onContextMenu={onOpenContext} />
        ))}
      </div>
    </div>
  );
}

function ContextMenu({ state, onAction }: { state: ContextState; onAction: (label: string) => void }) {
  const sections = ['play', 'open', 'ai', 'cover', 'lyrics', 'analysis'];
  const maxX = typeof window !== 'undefined' ? window.innerWidth - 240 : state.x;
  const maxY = typeof window !== 'undefined' ? window.innerHeight - 460 : state.y;
  return (
    <div
      onClick={(e) => e.stopPropagation()}
      onContextMenu={(e) => e.preventDefault()}
      className="fixed z-50 w-56 rounded-xl bg-slate-950/95 backdrop-blur-2xl border border-cyan-400/20 shadow-[0_20px_60px_-10px_rgba(34,211,238,0.3)] py-1.5 text-xs"
      style={{ left: Math.min(state.x, maxX), top: Math.min(state.y, maxY) }}
    >
      <div className="px-3 py-1.5 border-b border-slate-800 mb-1">
        <div className="text-slate-100 truncate">{state.album.title}</div>
        <div className="text-[10px] text-slate-400 truncate">{state.album.artist}</div>
      </div>
      {sections.map((sec, i) => (
        <div key={sec}>
          {i > 0 && <div className="my-1 border-t border-slate-800/70" />}
          {CONTEXT_MENU.filter((m) => m.section === sec).map((m) => (
            <button
              key={m.label}
              onClick={() => onAction(m.label)}
              className="w-full text-left px-3 py-1.5 text-slate-300 hover:bg-cyan-500/10 hover:text-cyan-200 transition-colors"
            >
              {m.label}
            </button>
          ))}
        </div>
      ))}
    </div>
  );
}

function EditAlbumDrawer({ album, onClose }: { album: AlbumItem; onClose: () => void }) {
  const [form, setForm] = useState({
    title: album.title,
    artist: album.artist,
    album: album.title,
    year: album.year,
    genre: album.genre,
    track: '1',
    disc: '1',
    bpm: String(album.bpm ?? ''),
    key: 'C minor',
    lyrics: '',
  });
  const update = (k: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) =>
    setForm((f) => ({ ...f, [k]: e.target.value }));

  return (
    <div className="fixed inset-0 z-40 flex justify-end">
      <div className="absolute inset-0 bg-slate-950/70 backdrop-blur-sm" onClick={onClose} />
      <div className="relative w-[420px] h-full bg-slate-950/95 border-l border-purple-500/20 backdrop-blur-2xl overflow-y-auto">
        <div className="flex items-center justify-between px-4 py-3 border-b border-slate-800">
          <div>
            <div className="text-slate-100 text-sm">Edit Album / Track</div>
            <div className="text-[10px] text-slate-500">{album.title}</div>
          </div>
          <button onClick={onClose} className="p-1.5 rounded-md bg-slate-800 text-slate-300 hover:bg-slate-700">
            <X size={14} />
          </button>
        </div>

        <div className="p-4 space-y-3">
          <div className="aspect-square rounded-xl overflow-hidden ring-1 ring-purple-400/30 relative">
            {album.coverUrl ? (
              <img src={album.coverUrl} alt={album.title} className="w-full h-full object-cover" />
            ) : (
              <div className="w-full h-full bg-gradient-to-br from-purple-700 to-pink-600" />
            )}
            <div className="absolute bottom-2 right-2 flex gap-1">
              <button className="px-2 py-1 rounded-md bg-slate-950/80 text-slate-200 text-[10px] border border-slate-700 hover:border-cyan-400/50">Generate</button>
              <button className="px-2 py-1 rounded-md bg-slate-950/80 text-slate-200 text-[10px] border border-slate-700 hover:border-cyan-400/50">Replace</button>
            </div>
          </div>

          <div className="grid grid-cols-2 gap-2">
            <Field label="Title" value={form.title} onChange={update('title')} />
            <Field label="Artist" value={form.artist} onChange={update('artist')} />
            <Field label="Album" value={form.album} onChange={update('album')} />
            <Field label="Year" value={form.year} onChange={update('year')} />
            <Field label="Genre" value={form.genre} onChange={update('genre')} />
            <Field label="Track #" value={form.track} onChange={update('track')} />
            <Field label="Disc #" value={form.disc} onChange={update('disc')} />
            <Field label="BPM" value={form.bpm} onChange={update('bpm')} />
            <Field label="Key" value={form.key} onChange={update('key')} />
          </div>

          <div>
            <label className="text-[10px] text-slate-400 uppercase tracking-wider">Lyrics</label>
            <textarea
              value={form.lyrics}
              onChange={update('lyrics')}
              rows={4}
              className="mt-1 w-full bg-slate-900/60 border border-slate-700/40 rounded-md px-2 py-1.5 text-xs text-slate-200 outline-none focus:border-cyan-400/50"
              placeholder="Paste or fetch lyrics…"
            />
          </div>

          <div className="grid grid-cols-2 gap-2 pt-2">
            <button className="flex items-center justify-center gap-1.5 px-3 py-2 rounded-md bg-gradient-to-r from-cyan-500 to-purple-500 text-white text-xs hover:shadow-lg hover:shadow-cyan-500/30 transition-all">
              <Save size={12} /> Save
            </button>
            <button onClick={onClose} className="flex items-center justify-center gap-1.5 px-3 py-2 rounded-md bg-slate-800 text-slate-300 text-xs border border-slate-700 hover:bg-slate-700">
              Cancel
            </button>
            <button className="flex items-center justify-center gap-1.5 px-3 py-2 rounded-md bg-slate-800 text-slate-300 text-xs border border-slate-700 hover:border-cyan-400/40">
              <Wrench size={12} /> Get Metadata
            </button>
            <button className="flex items-center justify-center gap-1.5 px-3 py-2 rounded-md bg-slate-800 text-slate-300 text-xs border border-slate-700 hover:border-cyan-400/40">
              <ImageIcon size={12} /> Generate Cover
            </button>
            <button className="flex items-center justify-center gap-1.5 px-3 py-2 rounded-md bg-slate-800 text-slate-300 text-xs border border-slate-700 hover:border-cyan-400/40">
              <Upload size={12} /> Replace Cover
            </button>
            <button className="flex items-center justify-center gap-1.5 px-3 py-2 rounded-md bg-slate-800 text-slate-300 text-xs border border-slate-700 hover:border-cyan-400/40">
              <RotateCcw size={12} /> Restore Original
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

function Field({
  label, value, onChange,
}: { label: string; value: string; onChange: (e: React.ChangeEvent<HTMLInputElement>) => void }) {
  return (
    <div>
      <label className="text-[10px] text-slate-400 uppercase tracking-wider">{label}</label>
      <input
        value={value}
        onChange={onChange}
        className="mt-1 w-full bg-slate-900/60 border border-slate-700/40 rounded-md px-2 py-1.5 text-xs text-slate-200 outline-none focus:border-cyan-400/50"
      />
    </div>
  );
}
