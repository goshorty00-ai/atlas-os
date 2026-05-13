import { useState, useEffect, useRef, useCallback } from 'react';
import { Wand2, Film, Tv, Music, Gamepad2, Mic2, Server, Sparkles, Save, GripVertical, Eye, EyeOff, Pencil, Check, X, ChevronUp, ChevronDown, List, Trash2 } from 'lucide-react';

const mediaTypes = [
  { icon: Film, label: 'Movies' },
  { icon: Tv, label: 'TV Shows' },
  { icon: Music, label: 'Music' },
  { icon: Gamepad2, label: 'Games' },
  { icon: Mic2, label: 'Karaoke' },
  { icon: Server, label: 'Servers' },
];

const moods = ['Dark', 'Action', 'Comedy', 'Thriller', 'Sci-Fi', 'Romance', 'Drama', 'Horror', 'Chill', 'Energetic'];
const genres = ['Action', 'Adventure', 'Comedy', 'Crime', 'Documentary', 'Drama', 'Fantasy', 'Horror', 'Mystery', 'Romance', 'Sci-Fi', 'Thriller'];

const examplePrompts = [
  'Make me a dark sci-fi movie shelf under 2 hours',
  'Create a family game night shelf',
  'Build a karaoke queue for a party',
  'Find TV shows with new episodes this week',
  'Create a music shelf for coding',
  'Show server movies missing artwork',
];

// ── Shelf Manager types & persistence ─────────────────────────────────────────

interface BridgeShelfRaw { title: string; type?: string; items?: unknown[]; }
interface BridgeStateRaw { shelves?: BridgeShelfRaw[]; }

interface ManagedShelf {
  key: string;
  displayName: string;
  type: string;
  count: number;
  hidden: boolean;
}

const SHELF_STORE = 'atlas-shelf-manager-v2';
const CUSTOM_SHELVES_KEY = 'atlas-custom-shelves-v1';

interface CustomShelf { name: string; count: number; }

function loadCustomShelves(): CustomShelf[] {
  try {
    const raw = JSON.parse(localStorage.getItem(CUSTOM_SHELVES_KEY) ?? '{}') as Record<string, unknown[]>;
    return Object.entries(raw).map(([name, items]) => ({ name, count: Array.isArray(items) ? items.length : 0 }));
  } catch { return []; }
}

function loadShelfConfig(): Record<string, { displayName?: string; hidden?: boolean }> {
  try { return JSON.parse(localStorage.getItem(SHELF_STORE) ?? '{}'); } catch { return {}; }
}

function persistShelfConfig(shelves: ManagedShelf[]) {
  const cfg: Record<string, { displayName?: string; hidden?: boolean }> = {};
  shelves.forEach((s) => { cfg[s.key] = { displayName: s.displayName, hidden: s.hidden }; });
  localStorage.setItem(SHELF_STORE, JSON.stringify(cfg));
  // Notify other components (Streams page) that config changed
  window.dispatchEvent(new StorageEvent('storage', { key: SHELF_STORE }));
}

const ORDER_STORE = 'atlas-shelf-order-v2';

function loadShelfOrder(): string[] {
  try { return JSON.parse(localStorage.getItem(ORDER_STORE) ?? '[]'); } catch { return []; }
}

function persistShelfOrder(shelves: ManagedShelf[]) {
  localStorage.setItem(ORDER_STORE, JSON.stringify(shelves.map((s) => s.key)));
  window.dispatchEvent(new StorageEvent('storage', { key: ORDER_STORE }));
}

function mergeBridgeShelves(raw: BridgeShelfRaw[]): ManagedShelf[] {
  const cfg = loadShelfConfig();
  const savedOrder = loadShelfOrder();
  const items = raw.map((s): ManagedShelf => {
    const c = cfg[s.title] ?? {};
    return {
      key: s.title,
      displayName: c.displayName ?? s.title,
      type: s.type ?? 'movie',
      count: s.items?.length ?? 0,
      hidden: c.hidden ?? false,
    };
  });
  if (savedOrder.length > 0) {
    const orderMap = new Map(savedOrder.map((k, i) => [k, i]));
    items.sort((a, b) => (orderMap.get(a.key) ?? 9999) - (orderMap.get(b.key) ?? 9999));
  }
  return items;
}

// ── Component ─────────────────────────────────────────────────────────────────

export function ShelfCreatorPage() {
  const [prompt, setPrompt] = useState('');
  const [selectedTypes, setSelectedTypes] = useState<string[]>(['Movies']);
  const [selectedMoods, setSelectedMoods] = useState<string[]>([]);
  const [selectedGenres, setSelectedGenres] = useState<string[]>([]);
  const [familySafe, setFamilySafe] = useState(false);
  const [excludeWatched, setExcludeWatched] = useState(false);

  // Shelf manager state
  const [shelves, setShelves] = useState<ManagedShelf[]>([]);
  const [customShelves, setCustomShelves] = useState<CustomShelf[]>(loadCustomShelves);
  const [editingKey, setEditingKey] = useState<string | null>(null);
  const [editingName, setEditingName] = useState('');
  const dragIndex = useRef<number | null>(null);
  const [dropTarget, setDropTarget] = useState<number | null>(null);

  // Bridge: receive servers.state to populate real shelves
  useEffect(() => {
    const applyRaw = (raw: BridgeShelfRaw[]) => {
      if (!raw.length) return;
      setShelves((prev) => {
        if (prev.length === 0) return mergeBridgeShelves(raw);
        const prevMap = new Map(prev.map((s) => [s.key, s]));
        const rawKeys = new Set(raw.map((r) => r.title));
        const updated = prev
          .filter((s) => rawKeys.has(s.key))
          .map((s) => {
            const r = raw.find((r) => r.title === s.key);
            return r ? { ...s, count: r.items?.length ?? s.count } : s;
          });
        raw.forEach((r) => {
          if (!prevMap.has(r.title)) {
            updated.push({ key: r.title, displayName: r.title, type: r.type ?? 'movie', count: r.items?.length ?? 0, hidden: false });
          }
        });
        return updated;
      });
    };

    // Seed immediately from cached global state (Streams page already received it)
    const cached = (window as any).__atlasBridgeState as { shelves?: BridgeShelfRaw[] } | undefined;
    if (cached?.shelves?.length) applyRaw(cached.shelves);

    const handler = (event: MessageEvent) => {
      try {
        const msg = typeof event.data === 'string' ? JSON.parse(event.data) : event.data;
        if (msg?.type === 'servers.state') {
          const raw = (msg.payload as BridgeStateRaw)?.shelves ?? [];
          applyRaw(raw);
        }
      } catch { /* ignore */ }
    };
    (window as any).chrome?.webview?.addEventListener('message', handler);
    // Request fresh state (C# clears dedup and re-sends)
    (window as any).chrome?.webview?.postMessage({ type: 'servers.getState' });
    return () => (window as any).chrome?.webview?.removeEventListener('message', handler);
  }, []);

  // Sync custom shelves from localStorage (updated when user creates via grid-view)
  useEffect(() => {
    const onStorage = (e: StorageEvent) => {
      if (e.key === CUSTOM_SHELVES_KEY) setCustomShelves(loadCustomShelves());
    };
    window.addEventListener('storage', onStorage);
    return () => window.removeEventListener('storage', onStorage);
  }, []);

  const deleteCustomShelf = useCallback((name: string) => {
    try {
      const raw = JSON.parse(localStorage.getItem(CUSTOM_SHELVES_KEY) ?? '{}');
      delete raw[name];
      localStorage.setItem(CUSTOM_SHELVES_KEY, JSON.stringify(raw));
      window.dispatchEvent(new StorageEvent('storage', { key: CUSTOM_SHELVES_KEY }));
    } catch { /* ignore */ }
    setCustomShelves((prev) => prev.filter((s) => s.name !== name));
  }, []);

  // Persist whenever shelves change
  useEffect(() => {
    if (shelves.length > 0) {
      persistShelfConfig(shelves);
      persistShelfOrder(shelves);
    }
  }, [shelves]);

  const toggleType = (type: string) => {
    setSelectedTypes((prev) =>
      prev.includes(type) ? prev.filter((t) => t !== type) : [...prev, type]
    );
  };

  const toggleMood = (mood: string) => {
    setSelectedMoods((prev) =>
      prev.includes(mood) ? prev.filter((m) => m !== mood) : [...prev, mood]
    );
  };

  const toggleGenre = (genre: string) => {
    setSelectedGenres((prev) =>
      prev.includes(genre) ? prev.filter((g) => g !== genre) : [...prev, genre]
    );
  };

  // Shelf manager handlers
  const startRename = useCallback((shelf: ManagedShelf) => {
    setEditingKey(shelf.key);
    setEditingName(shelf.displayName);
  }, []);

  const confirmRename = useCallback((key: string) => {
    setShelves((prev) => prev.map((s) => s.key === key ? { ...s, displayName: editingName.trim() || s.key } : s));
    setEditingKey(null);
  }, [editingName]);

  const cancelRename = useCallback(() => { setEditingKey(null); }, []);

  const toggleHidden = useCallback((idx: number) => {
    setShelves((prev) => prev.map((s, i) => i === idx ? { ...s, hidden: !s.hidden } : s));
  }, []);

  const moveUp = useCallback((idx: number) => {
    if (idx === 0) return;
    setShelves((prev) => { const a = [...prev]; [a[idx - 1], a[idx]] = [a[idx], a[idx - 1]]; return a; });
  }, []);

  const moveDown = useCallback((idx: number) => {
    setShelves((prev) => {
      if (idx >= prev.length - 1) return prev;
      const a = [...prev]; [a[idx], a[idx + 1]] = [a[idx + 1], a[idx]]; return a;
    });
  }, []);

  const handleDragStart = useCallback((idx: number) => { dragIndex.current = idx; }, []);

  const handleDragOver = useCallback((e: React.DragEvent, idx: number) => {
    e.preventDefault();
    setDropTarget(idx);
  }, []);

  const handleDrop = useCallback((toIdx: number) => {
    const fromIdx = dragIndex.current;
    if (fromIdx === null || fromIdx === toIdx) { setDropTarget(null); return; }
    setShelves((prev) => {
      const a = [...prev];
      const [item] = a.splice(fromIdx, 1);
      a.splice(toIdx, 0, item);
      return a;
    });
    dragIndex.current = null;
    setDropTarget(null);
  }, []);

  const handleDragEnd = useCallback(() => { dragIndex.current = null; setDropTarget(null); }, []);

  return (
    <div className="space-y-8">
      {/* Header */}
      <div className="flex items-center gap-4">
        <div className="p-3 rounded-xl bg-gradient-to-r from-cyan-500 to-purple-500">
          <Wand2 size={28} className="text-white" />
        </div>
        <div>
          <h1 className="text-slate-100 text-3xl">AI Shelf Creator</h1>
          <p className="text-slate-400">Create custom shelves with natural language</p>
        </div>
      </div>

      <div className="space-y-6">
        {/* Creator Panel */}
        <div className="space-y-6">
          {/* Prompt Input */}
          <div className="bg-gradient-to-br from-slate-900/80 to-slate-800/80 backdrop-blur-xl rounded-2xl border border-purple-500/30 p-6 space-y-5">
            <div className="flex items-center gap-3">
              <div className="p-2 rounded-xl bg-gradient-to-r from-cyan-500 to-purple-500">
                <Sparkles size={20} className="text-white" />
              </div>
              <h3 className="text-slate-100">Describe Your Shelf</h3>
            </div>

            <div>
              <textarea
                value={prompt}
                onChange={(e) => setPrompt(e.target.value)}
                placeholder="e.g., 'Create a shelf for dark sci-fi movies with good ratings'"
                className="w-full bg-slate-950/50 text-slate-200 placeholder:text-slate-500 rounded-xl border border-slate-700 focus:border-cyan-500/50 focus:ring-2 focus:ring-cyan-500/20 p-4 outline-none resize-none transition-all"
                rows={4}
              />
            </div>

            {/* Example Prompts */}
            <div>
              <p className="text-xs text-slate-400 mb-2">Try these examples:</p>
              <div className="flex flex-wrap gap-2">
                {examplePrompts.slice(0, 3).map((example) => (
                  <button
                    key={example}
                    onClick={() => setPrompt(example)}
                    className="px-3 py-1.5 rounded-lg bg-slate-800/50 text-slate-300 hover:text-white hover:bg-slate-700 transition-all text-xs"
                  >
                    {example}
                  </button>
                ))}
              </div>
            </div>
          </div>

          {/* Media Type Selector */}
          <div className="bg-gradient-to-br from-slate-900/80 to-slate-800/80 backdrop-blur-xl rounded-xl border border-slate-700/50 p-6">
            <h3 className="text-slate-100 mb-4">Media Types</h3>
            <div className="flex flex-wrap gap-2">
              {mediaTypes.map((type) => {
                const Icon = type.icon;
                const isSelected = selectedTypes.includes(type.label);
                return (
                  <button
                    key={type.label}
                    onClick={() => toggleType(type.label)}
                    className={`flex items-center gap-2 px-4 py-2.5 rounded-xl transition-all ${
                      isSelected
                        ? 'bg-gradient-to-r from-cyan-500 to-purple-500 text-white shadow-lg shadow-cyan-500/30'
                        : 'bg-slate-800/50 text-slate-400 hover:text-slate-200 hover:bg-slate-700/50'
                    }`}
                  >
                    <Icon size={18} />
                    <span>{type.label}</span>
                  </button>
                );
              })}
            </div>
          </div>

          {/* Moods & Genres */}
          <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
            <div className="bg-gradient-to-br from-slate-900/80 to-slate-800/80 backdrop-blur-xl rounded-xl border border-slate-700/50 p-6">
              <h3 className="text-slate-100 mb-4">Moods</h3>
              <div className="flex flex-wrap gap-2">
                {moods.map((mood) => {
                  const isSelected = selectedMoods.includes(mood);
                  return (
                    <button
                      key={mood}
                      onClick={() => toggleMood(mood)}
                      className={`px-3 py-1.5 rounded-lg text-sm transition-all ${
                        isSelected
                          ? 'bg-purple-500/30 text-purple-200 border border-purple-500/50'
                          : 'bg-slate-800/50 text-slate-400 hover:text-slate-200 hover:bg-slate-700/50'
                      }`}
                    >
                      {mood}
                    </button>
                  );
                })}
              </div>
            </div>

            <div className="bg-gradient-to-br from-slate-900/80 to-slate-800/80 backdrop-blur-xl rounded-xl border border-slate-700/50 p-6">
              <h3 className="text-slate-100 mb-4">Genres</h3>
              <div className="flex flex-wrap gap-2">
                {genres.map((genre) => {
                  const isSelected = selectedGenres.includes(genre);
                  return (
                    <button
                      key={genre}
                      onClick={() => toggleGenre(genre)}
                      className={`px-3 py-1.5 rounded-lg text-sm transition-all ${
                        isSelected
                          ? 'bg-cyan-500/30 text-cyan-200 border border-cyan-500/50'
                          : 'bg-slate-800/50 text-slate-400 hover:text-slate-200 hover:bg-slate-700/50'
                      }`}
                    >
                      {genre}
                    </button>
                  );
                })}
              </div>
            </div>
          </div>

          {/* Filters */}
          <div className="bg-gradient-to-br from-slate-900/80 to-slate-800/80 backdrop-blur-xl rounded-xl border border-slate-700/50 p-6">
            <h3 className="text-slate-100 mb-4">Filters</h3>
            <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
              <div>
                <label className="text-sm text-slate-300 mb-2 block">Rating</label>
                <select className="w-full bg-slate-950/50 text-slate-200 rounded-lg border border-slate-700 focus:border-cyan-500/50 p-2.5 outline-none">
                  <option>Any</option>
                  <option>7.0+</option>
                  <option>8.0+</option>
                  <option>9.0+</option>
                </select>
              </div>
              <div>
                <label className="text-sm text-slate-300 mb-2 block">Runtime</label>
                <select className="w-full bg-slate-950/50 text-slate-200 rounded-lg border border-slate-700 focus:border-cyan-500/50 p-2.5 outline-none">
                  <option>Any</option>
                  <option>&lt; 90 min</option>
                  <option>90-120 min</option>
                  <option>&gt; 120 min</option>
                </select>
              </div>
              <div>
                <label className="text-sm text-slate-300 mb-2 block">Server</label>
                <select className="w-full bg-slate-950/50 text-slate-200 rounded-lg border border-slate-700 focus:border-cyan-500/50 p-2.5 outline-none">
                  <option>All Servers</option>
                  <option>Main Library</option>
                  <option>4K Collection</option>
                  <option>Local Files</option>
                </select>
              </div>
              <div>
                <label className="text-sm text-slate-300 mb-2 block">Year</label>
                <select className="w-full bg-slate-950/50 text-slate-200 rounded-lg border border-slate-700 focus:border-cyan-500/50 p-2.5 outline-none">
                  <option>Any</option>
                  <option>2024+</option>
                  <option>2020s</option>
                  <option>2010s</option>
                  <option>2000s</option>
                </select>
              </div>
            </div>

            <div className="flex gap-4 mt-4">
              <label className="flex items-center gap-2 cursor-pointer">
                <input
                  type="checkbox"
                  checked={familySafe}
                  onChange={(e) => setFamilySafe(e.target.checked)}
                  className="w-4 h-4 rounded border-slate-700 bg-slate-950/50 text-cyan-500 focus:ring-cyan-500/20"
                />
                <span className="text-sm text-slate-300">Family Safe</span>
              </label>
              <label className="flex items-center gap-2 cursor-pointer">
                <input
                  type="checkbox"
                  checked={excludeWatched}
                  onChange={(e) => setExcludeWatched(e.target.checked)}
                  className="w-4 h-4 rounded border-slate-700 bg-slate-950/50 text-cyan-500 focus:ring-cyan-500/20"
                />
                <span className="text-sm text-slate-300">Exclude Watched</span>
              </label>
            </div>
          </div>

          {/* Actions */}
          <div className="flex gap-3">
            <button className="flex-1 flex items-center justify-center gap-2 px-6 py-4 rounded-xl bg-gradient-to-r from-cyan-500 to-purple-500 text-white hover:shadow-xl hover:shadow-cyan-500/30 transition-all">
              <Sparkles size={20} />
              <span>Generate Shelf</span>
            </button>
            <button className="flex items-center gap-2 px-6 py-4 rounded-xl bg-slate-800/50 text-slate-300 hover:text-white hover:bg-slate-700 transition-all">
              <Save size={20} />
              <span>Save</span>
            </button>
          </div>
        </div>
      </div>

      {/* ── Live Shelf Manager ────────────────────────────────────────────── */}
      <div className="bg-gradient-to-br from-slate-900/80 to-slate-800/80 backdrop-blur-xl rounded-2xl border border-cyan-500/20 p-6 space-y-4">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-3">
            <List size={20} className="text-cyan-400" />
            <h3 className="text-slate-100">Your Shelves</h3>
            <span className="px-2 py-0.5 rounded-full bg-slate-700/50 text-slate-400 text-xs">{shelves.length + customShelves.length} shelves</span>
            {shelves.filter((s) => s.hidden).length > 0 && (
              <span className="px-2 py-0.5 rounded-full bg-amber-500/20 text-amber-300 text-xs">
                {shelves.filter((s) => s.hidden).length} hidden
              </span>
            )}
          </div>
          <div className="flex items-center gap-2">
            <button
              onClick={() => {
                const reset = shelves.map((s) => ({ ...s, hidden: false }));
                setShelves(reset);
                persistShelfConfig(reset);
              }}
              className="px-3 py-1 rounded-lg bg-amber-500/20 text-amber-300 text-xs hover:bg-amber-500/30 transition-colors border border-amber-500/30"
            >
              Show All
            </button>
            <button
              onClick={() => {
                localStorage.removeItem(SHELF_STORE);
                localStorage.removeItem(ORDER_STORE);
                window.dispatchEvent(new StorageEvent('storage', { key: SHELF_STORE }));
                window.dispatchEvent(new StorageEvent('storage', { key: ORDER_STORE }));
                const raw = (window as any).__atlasBridgeState?.shelves ?? [];
                setShelves(mergeBridgeShelves(raw));
              }}
              className="px-3 py-1 rounded-lg bg-red-500/20 text-red-300 text-xs hover:bg-red-500/30 transition-colors border border-red-500/30"
            >
              Reset All
            </button>
            <p className="text-xs text-slate-500">Drag to reorder • saves automatically</p>
          </div>
        </div>

        {shelves.length === 0 && customShelves.length === 0 && (
          <div className="text-center py-10 text-slate-500 text-sm">
            Loading shelves from Streams…
          </div>
        )}

        <div className="space-y-1.5">
          {shelves.map((shelf, i) => (
            <div
              key={shelf.key}
              draggable
              onDragStart={() => handleDragStart(i)}
              onDragOver={(e) => handleDragOver(e, i)}
              onDrop={() => handleDrop(i)}
              onDragEnd={handleDragEnd}
              className={[
                'flex items-center gap-3 px-3 py-2.5 rounded-xl border transition-all select-none',
                dragIndex.current === i ? 'opacity-30 cursor-grabbing' : 'cursor-grab',
                dropTarget === i && dragIndex.current !== i
                  ? 'border-cyan-400/70 bg-cyan-500/10 shadow-[0_0_12px_rgba(34,211,238,0.2)]'
                  : 'border-slate-700/40 bg-slate-800/30 hover:border-slate-600/60',
                shelf.hidden ? 'opacity-50' : '',
              ].join(' ')}
            >
              {/* Grip */}
              <GripVertical size={15} className="text-slate-600 flex-shrink-0" />

              {/* Visibility toggle */}
              <button
                onClick={() => toggleHidden(i)}
                className={`p-1 rounded flex-shrink-0 transition-colors ${shelf.hidden ? 'text-slate-600 hover:text-slate-400' : 'text-cyan-400 hover:text-cyan-300'}`}
                title={shelf.hidden ? 'Show shelf' : 'Hide shelf'}
              >
                {shelf.hidden ? <EyeOff size={14} /> : <Eye size={14} />}
              </button>

              {/* Name (inline editable) */}
              <div className="flex-1 min-w-0">
                {editingKey === shelf.key ? (
                  <div className="flex items-center gap-2">
                    <input
                      autoFocus
                      value={editingName}
                      onChange={(e) => setEditingName(e.target.value)}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter') confirmRename(shelf.key);
                        if (e.key === 'Escape') cancelRename();
                      }}
                      className="flex-1 bg-slate-950 text-slate-100 text-sm rounded-lg px-2 py-1 border border-cyan-500/60 outline-none min-w-0"
                    />
                    <button onClick={() => confirmRename(shelf.key)} className="p-1 text-cyan-400 hover:text-cyan-300 flex-shrink-0"><Check size={13} /></button>
                    <button onClick={cancelRename} className="p-1 text-slate-500 hover:text-slate-300 flex-shrink-0"><X size={13} /></button>
                  </div>
                ) : (
                  <div className="flex items-center gap-2 min-w-0">
                    <span className={`text-sm truncate ${shelf.hidden ? 'text-slate-500 line-through' : 'text-slate-200'}`}>
                      {shelf.displayName}
                    </span>
                    {shelf.displayName !== shelf.key && (
                      <span className="text-xs text-slate-600 truncate hidden sm:block">({shelf.key})</span>
                    )}
                  </div>
                )}
              </div>

              {/* Type badge */}
              <span className="flex-shrink-0 px-2 py-0.5 text-[11px] rounded-full bg-slate-700/60 text-slate-400 capitalize">
                {shelf.type}
              </span>

              {/* Count */}
              <span className="flex-shrink-0 text-xs text-slate-500 w-16 text-right tabular-nums">
                {shelf.count} items
              </span>

              {/* Move & rename actions */}
              <div className="flex items-center gap-0.5 flex-shrink-0">
                <button
                  onClick={() => moveUp(i)}
                  disabled={i === 0}
                  title="Move up"
                  className="p-1 rounded text-slate-500 hover:text-slate-300 disabled:opacity-20 disabled:cursor-default transition-colors"
                >
                  <ChevronUp size={14} />
                </button>
                <button
                  onClick={() => moveDown(i)}
                  disabled={i === shelves.length - 1}
                  title="Move down"
                  className="p-1 rounded text-slate-500 hover:text-slate-300 disabled:opacity-20 disabled:cursor-default transition-colors"
                >
                  <ChevronDown size={14} />
                </button>
                <button
                  onClick={() => startRename(shelf)}
                  title="Rename shelf"
                  className="p-1 rounded text-slate-500 hover:text-cyan-400 transition-colors"
                >
                  <Pencil size={13} />
                </button>
              </div>
            </div>
          ))}
        </div>

        {/* Custom shelves (created via bulk-select in grid view) */}
        {customShelves.length > 0 && (
          <>
            <div className="flex items-center gap-2 pt-2">
              <div className="h-px flex-1 bg-slate-700/50" />
              <span className="text-xs text-slate-500 px-2">Custom Shelves</span>
              <div className="h-px flex-1 bg-slate-700/50" />
            </div>
            <div className="space-y-1.5">
              {customShelves.map((shelf) => (
                <div
                  key={shelf.name}
                  className="flex items-center gap-3 px-3 py-2.5 rounded-xl border border-cyan-500/20 bg-cyan-500/5 hover:border-cyan-500/40 transition-all"
                >
                  <span className="flex-1 min-w-0 text-sm text-slate-200 truncate">{shelf.name}</span>
                  <span className="flex-shrink-0 px-2 py-0.5 text-[11px] rounded-full bg-cyan-500/20 text-cyan-300">custom</span>
                  <span className="flex-shrink-0 text-xs text-slate-500 w-16 text-right tabular-nums">{shelf.count} items</span>
                  <button
                    onClick={() => deleteCustomShelf(shelf.name)}
                    title="Delete shelf"
                    className="p-1 rounded text-slate-600 hover:text-red-400 transition-colors flex-shrink-0"
                  >
                    <Trash2 size={14} />
                  </button>
                </div>
              ))}
            </div>
          </>
        )}
      </div>

      <div className="h-8" />
    </div>
  );
}
