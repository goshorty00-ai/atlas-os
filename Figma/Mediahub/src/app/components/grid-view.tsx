import { ArrowLeft, Search, SlidersHorizontal, ArrowUpDown, Grid3x3, List, Maximize2, Sparkles, CheckSquare, Square, Star, X, ChevronDown, BookmarkPlus } from 'lucide-react';
import { ServerCard, MediaItem } from './server-card';
import { useState, useEffect, useRef, useCallback, useMemo } from 'react';
import { useNavigate } from 'react-router';

const PAGE_SIZE = 60;
const CUSTOM_SHELVES_KEY = 'atlas-custom-shelves-v1';

function postBridge(msg: object) {
  try { (window as any).chrome?.webview?.postMessage(msg); } catch { }
}

type SortOption = 'default' | 'title-asc' | 'title-desc' | 'year-new' | 'year-old' | 'rating-high' | 'rating-low';

const SORT_LABELS: Record<SortOption, string> = {
  default:       'Default',
  'title-asc':   'Title A → Z',
  'title-desc':  'Title Z → A',
  'year-new':    'Newest First',
  'year-old':    'Oldest First',
  'rating-high': 'Rating ↓',
  'rating-low':  'Rating ↑',
};

interface GridViewProps {
  shelfName: string;
  items: MediaItem[];
  contentType?: string;
  onBack: () => void;
  onOpenCarousel: () => void;
}

export function GridView({ shelfName, items, contentType, onBack, onOpenCarousel }: GridViewProps) {
  const navigate = useNavigate();
  const [searchQuery, setSearchQuery]       = useState('');
  const [viewMode, setViewMode]             = useState<'grid' | 'list'>('grid');
  const [bulkSelectMode, setBulkSelectMode] = useState(false);
  const [selectedItems, setSelectedItems]   = useState<Set<string>>(new Set());
  const [visibleCount, setVisibleCount]     = useState(PAGE_SIZE);
  const sentinelRef = useRef<HTMLDivElement>(null);

  // Filter / sort
  const [filterOpen, setFilterOpen] = useState(false);
  const [sortOpen, setSortOpen]     = useState(false);
  const [sortOption, setSortOption] = useState<SortOption>('default');
  const [minRating, setMinRating]   = useState(0);

  // Create shelf modal
  const [showCreateShelf, setShowCreateShelf] = useState(false);
  const [newShelfName, setNewShelfName]       = useState('');

  // ── Filtered + sorted items ───────────────────────────────────────────────
  const filteredItems = useMemo(() => {
    let result = items.filter((item) => {
      const matchesSearch = item.title.toLowerCase().includes(searchQuery.toLowerCase());
      const matchesRating = minRating === 0 || Number(item.rating ?? 0) >= minRating;
      return matchesSearch && matchesRating;
    });
    switch (sortOption) {
      case 'title-asc':   return [...result].sort((a, b) => a.title.localeCompare(b.title));
      case 'title-desc':  return [...result].sort((a, b) => b.title.localeCompare(a.title));
      case 'year-new':    return [...result].sort((a, b) => Number(b.year || 0) - Number(a.year || 0));
      case 'year-old':    return [...result].sort((a, b) => Number(a.year || 0) - Number(b.year || 0));
      case 'rating-high': return [...result].sort((a, b) => Number(b.rating ?? 0) - Number(a.rating ?? 0));
      case 'rating-low':  return [...result].sort((a, b) => Number(a.rating ?? 0) - Number(b.rating ?? 0));
      default: return result;
    }
  }, [items, searchQuery, minRating, sortOption]);

  // Reset pagination when filters change
  const lastRequestedCountRef = useRef(0);
  useEffect(() => {
    setVisibleCount(PAGE_SIZE);
    lastRequestedCountRef.current = 0;
  }, [searchQuery, minRating, sortOption]);

  const filteredLengthRef = useRef(filteredItems.length);
  filteredLengthRef.current = filteredItems.length;
  const visibleCountRef = useRef(visibleCount);
  visibleCountRef.current = visibleCount;

  const loadMore = useCallback(() => {
    setVisibleCount((n) => {
      if (n >= filteredLengthRef.current) return n;
      return Math.min(n + PAGE_SIZE, filteredLengthRef.current);
    });
  }, []);

  // Attach scroll listener to the known scroll container
  useEffect(() => {
    const scrollEl = document.getElementById('atlas-main-scroll');
    if (!scrollEl) return;

    const onScroll = () => {
      const total = filteredLengthRef.current;
      const visible = visibleCountRef.current;
      const { scrollTop, scrollHeight, clientHeight } = scrollEl;
      const nearBottom = scrollTop + clientHeight >= scrollHeight - 600;

      if (!nearBottom) return;

      if (visible < total) {
        loadMore();
      } else if (total > 0 && total > lastRequestedCountRef.current) {
        // All local items shown — ask backend for more
        lastRequestedCountRef.current = total;
        postBridge({ type: 'servers.loadMoreShelfItems', contentType: contentType || 'movie' });
      }
    };

    scrollEl.addEventListener('scroll', onScroll, { passive: true });
    // Trigger once immediately in case already at bottom
    onScroll();
    return () => scrollEl.removeEventListener('scroll', onScroll);
  }, [loadMore, contentType]);

  const visibleItems = filteredItems.slice(0, visibleCount);

  const toggleSelectItem = (id: string) => {
    const newSelected = new Set(selectedItems);
    if (newSelected.has(id)) {
      newSelected.delete(id);
    } else {
      newSelected.add(id);
    }
    setSelectedItems(newSelected);
  };

  const selectAll = () => {
    if (selectedItems.size === filteredItems.length)
      setSelectedItems(new Set());
    else
      setSelectedItems(new Set(filteredItems.map((i) => i.id)));
  };

  const createCustomShelf = () => {
    if (!newShelfName.trim()) return;
    const chosen = filteredItems.filter((item) => selectedItems.has(item.id));
    try {
      const existing = JSON.parse(localStorage.getItem(CUSTOM_SHELVES_KEY) ?? '{}');
      existing[newShelfName.trim()] = chosen;
      localStorage.setItem(CUSTOM_SHELVES_KEY, JSON.stringify(existing));
      window.dispatchEvent(new StorageEvent('storage', { key: CUSTOM_SHELVES_KEY }));
    } catch { /* ignore */ }
    setShowCreateShelf(false);
    setNewShelfName('');
    setBulkSelectMode(false);
    setSelectedItems(new Set());
  };

  return (
    <div className="space-y-5">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-4">
          <button
            onClick={onBack}
            className="p-2 rounded-lg bg-slate-800/50 text-slate-300 hover:bg-slate-700/50 transition-colors"
          >
            <ArrowLeft size={20} />
          </button>
          <div>
            <h2 className="text-slate-100">{shelfName}</h2>
            <p className="text-sm text-slate-400">
              {filteredItems.length} items
              {minRating > 0 && <span className="ml-2 text-amber-300 text-xs">★ {minRating}+ active</span>}
            </p>
          </div>
        </div>
        <div className="flex items-center gap-2">
          {/* Create Shelf — visible when items are selected in bulk mode */}
          {bulkSelectMode && selectedItems.size > 0 && (
            <button
              onClick={() => setShowCreateShelf(true)}
              className="flex items-center gap-2 px-4 py-2 rounded-lg bg-gradient-to-r from-cyan-500 to-blue-600 text-white text-sm hover:shadow-lg hover:shadow-cyan-500/30 transition-all"
            >
              <BookmarkPlus size={16} />
              Create Shelf ({selectedItems.size})
            </button>
          )}
          <button
            onClick={onOpenCarousel}
            className="flex items-center gap-2 px-4 py-2 rounded-lg bg-gradient-to-r from-cyan-500 to-purple-500 text-white text-sm hover:shadow-lg hover:shadow-cyan-500/30 transition-all"
          >
            <Maximize2 size={16} />
            Open Carousel
          </button>
        </div>
      </div>

      {/* Controls */}
      <div className="flex items-center gap-3 flex-wrap">
        {/* Search */}
        <div className="flex-1 min-w-[280px]">
          <div className="relative">
            <Search size={18} className="absolute left-3 top-1/2 -translate-y-1/2 text-slate-400" />
            <input
              type="text"
              placeholder="Search within shelf..."
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              className="w-full pl-10 pr-4 py-2 rounded-lg bg-slate-800/50 border border-slate-700/30 text-slate-100 placeholder:text-slate-500 focus:outline-none focus:ring-2 focus:ring-cyan-500/50"
            />
          </div>
        </div>

        {/* Filters toggle */}
        <button
          onClick={() => setFilterOpen((p) => !p)}
          className={`flex items-center gap-2 px-4 py-2 rounded-lg border transition-colors ${
            filterOpen || minRating > 0
              ? 'bg-cyan-500/20 text-cyan-300 border-cyan-500/40'
              : 'bg-slate-800/50 text-slate-300 border-slate-700/30 hover:bg-slate-700/50'
          }`}
        >
          <SlidersHorizontal size={16} />
          Filters{minRating > 0 ? ` (★${minRating}+)` : ''}
        </button>

        {/* Sort dropdown */}
        <div className="relative">
          <button
            onClick={() => setSortOpen((p) => !p)}
            className={`flex items-center gap-2 px-4 py-2 rounded-lg border transition-colors ${
              sortOption !== 'default'
                ? 'bg-cyan-500/20 text-cyan-300 border-cyan-500/40'
                : 'bg-slate-800/50 text-slate-300 border-slate-700/30 hover:bg-slate-700/50'
            }`}
          >
            <ArrowUpDown size={16} />
            {SORT_LABELS[sortOption]}
            <ChevronDown size={14} className={`transition-transform ${sortOpen ? 'rotate-180' : ''}`} />
          </button>
          {sortOpen && (
            <>
              <div className="fixed inset-0 z-40" onClick={() => setSortOpen(false)} />
              <div className="absolute top-full mt-1 right-0 z-50 bg-slate-900 border border-slate-700/60 rounded-xl overflow-hidden shadow-2xl min-w-[180px]">
                {(Object.entries(SORT_LABELS) as [SortOption, string][]).map(([key, label]) => (
                  <button
                    key={key}
                    onClick={() => { setSortOption(key); setSortOpen(false); }}
                    className={`w-full text-left px-4 py-2.5 text-sm transition-colors ${
                      sortOption === key ? 'bg-cyan-500/20 text-cyan-200' : 'text-slate-300 hover:bg-slate-800'
                    }`}
                  >
                    {label}
                  </button>
                ))}
              </div>
            </>
          )}
        </div>

        {/* View mode */}
        <div className="flex rounded-lg bg-slate-800/50 border border-slate-700/30 overflow-hidden">
          <button
            onClick={() => setViewMode('grid')}
            className={`p-2 ${viewMode === 'grid' ? 'bg-cyan-500 text-white' : 'text-slate-400 hover:text-slate-300'} transition-colors`}
          >
            <Grid3x3 size={16} />
          </button>
          <button
            onClick={() => setViewMode('list')}
            className={`p-2 ${viewMode === 'list' ? 'bg-cyan-500 text-white' : 'text-slate-400 hover:text-slate-300'} transition-colors`}
          >
            <List size={16} />
          </button>
        </div>

        {/* Bulk select */}
        <button
          onClick={() => { setBulkSelectMode(!bulkSelectMode); setSelectedItems(new Set()); }}
          className={`flex items-center gap-2 px-4 py-2 rounded-lg transition-colors border ${
            bulkSelectMode
              ? 'bg-purple-500/20 text-purple-300 border-purple-500/30'
              : 'bg-slate-800/50 text-slate-300 border-slate-700/30 hover:bg-slate-700/50'
          }`}
        >
          <CheckSquare size={16} />
          {bulkSelectMode ? `${selectedItems.size} selected` : 'Bulk Select'}
        </button>

        {/* Create Shelf — always in controls bar */}
        <button
          onClick={() => {
            if (!bulkSelectMode) {
              setBulkSelectMode(true);
            } else if (selectedItems.size === 0) {
              // prompt to select items first — do nothing, hint is shown below
            } else {
              setShowCreateShelf(true);
            }
          }}
          className={`flex items-center gap-2 px-4 py-2 rounded-lg transition-all border ${
            bulkSelectMode && selectedItems.size > 0
              ? 'bg-gradient-to-r from-cyan-500 to-blue-600 text-white border-transparent shadow-lg shadow-cyan-500/20'
              : 'bg-slate-800/50 text-slate-300 border-slate-700/30 hover:bg-slate-700/50'
          }`}
        >
          <BookmarkPlus size={16} />
          {bulkSelectMode && selectedItems.size > 0 ? `Create Shelf (${selectedItems.size})` : 'Create Shelf'}
        </button>
      </div>

      {/* ── Filter panel ─────────────────────────────────────────────────── */}
      {filterOpen && (
        <div className="p-5 rounded-xl bg-slate-800/60 border border-cyan-500/20 space-y-4">
          <div className="flex items-center justify-between">
            <span className="text-slate-200 text-sm font-semibold">Filters</span>
            <button onClick={() => setMinRating(0)} className="text-xs text-slate-500 hover:text-slate-300 transition-colors">
              Reset
            </button>
          </div>
          <div className="space-y-2">
            <div className="flex items-center justify-between">
              <label className="flex items-center gap-1.5 text-slate-300 text-sm">
                <Star size={14} className="text-amber-400" fill="currentColor" />
                Minimum Rating
              </label>
              <span className="text-amber-300 font-bold text-sm w-16 text-right">
                {minRating === 0 ? 'All' : `${minRating} / 10`}
              </span>
            </div>
            <input
              type="range"
              min="0"
              max="10"
              step="0.5"
              value={minRating}
              onChange={(e) => setMinRating(parseFloat(e.target.value))}
              className="w-full h-2 appearance-none cursor-pointer rounded-full bg-slate-700
                [&::-webkit-slider-thumb]:appearance-none [&::-webkit-slider-thumb]:w-5 [&::-webkit-slider-thumb]:h-5
                [&::-webkit-slider-thumb]:rounded-full [&::-webkit-slider-thumb]:bg-amber-400
                [&::-webkit-slider-thumb]:cursor-pointer accent-amber-400"
            />
            <div className="flex justify-between text-[10px] text-slate-600 px-0.5">
              <span>0</span><span>2.5</span><span>5</span><span>7.5</span><span>10</span>
            </div>
          </div>
        </div>
      )}

      {/* Bulk Actions bar */}
      {bulkSelectMode && (
        <div className="flex items-center gap-3 p-3 rounded-lg bg-purple-500/10 border border-purple-500/30">
          <button
            onClick={selectAll}
            className="flex items-center gap-2 px-3 py-1.5 rounded-lg bg-slate-800/50 text-slate-300 text-sm hover:bg-slate-700/50 transition-colors"
          >
            {selectedItems.size === filteredItems.length ? <CheckSquare size={14} /> : <Square size={14} />}
            {selectedItems.size === filteredItems.length ? 'Deselect All' : 'Select All'}
          </button>
          <span className="text-slate-400 text-sm">{selectedItems.size} item{selectedItems.size !== 1 ? 's' : ''} selected — select items then tap <span className="text-cyan-300">Create Shelf</span> above</span>
        </div>
      )}

      {/* ── Create Shelf modal ──────────────────────────────────────────── */}
      {showCreateShelf && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm">
          <div className="bg-slate-900 border border-cyan-500/30 rounded-2xl p-6 space-y-4 w-full max-w-sm shadow-2xl mx-4">
            <div className="flex items-center justify-between">
              <h3 className="text-slate-100 font-semibold">Create New Shelf</h3>
              <button onClick={() => setShowCreateShelf(false)} className="text-slate-500 hover:text-slate-300 transition-colors">
                <X size={18} />
              </button>
            </div>
            <p className="text-slate-400 text-sm">
              {selectedItems.size} item{selectedItems.size !== 1 ? 's' : ''} will be added to this shelf.
            </p>
            <input
              type="text"
              placeholder="Shelf name…"
              value={newShelfName}
              onChange={(e) => setNewShelfName(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && createCustomShelf()}
              autoFocus
              className="w-full px-4 py-2.5 rounded-lg bg-slate-800 border border-slate-700/50 text-slate-100 placeholder:text-slate-500 focus:outline-none focus:ring-2 focus:ring-cyan-500/50"
            />
            <div className="flex gap-2">
              <button
                onClick={() => setShowCreateShelf(false)}
                className="flex-1 py-2 rounded-lg bg-slate-800 text-slate-300 hover:bg-slate-700 transition-colors text-sm"
              >
                Cancel
              </button>
              <button
                onClick={createCustomShelf}
                disabled={!newShelfName.trim()}
                className="flex-1 py-2 rounded-lg bg-gradient-to-r from-cyan-500 to-blue-600 text-white text-sm hover:shadow-lg disabled:opacity-50 disabled:cursor-not-allowed transition-all"
              >
                Create Shelf
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Grid/List */}
      {viewMode === 'grid' ? (
        <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5 2xl:grid-cols-6 gap-5 items-start">
          {visibleItems.map((item) => (
            <div key={item.id} className="relative">
              {bulkSelectMode && (
                <button
                  onClick={() => toggleSelectItem(item.id)}
                  className="absolute top-2 left-2 z-10 p-1 rounded bg-slate-900/90 backdrop-blur-sm"
                >
                  {selectedItems.has(item.id) ? (
                    <CheckSquare size={20} className="text-purple-400" />
                  ) : (
                    <Square size={20} className="text-slate-400" />
                  )}
                </button>
              )}
              <ServerCard item={item} className="!w-full" />
            </div>
          ))}
        </div>
      ) : (
        <div className="space-y-2">
          {visibleItems.map((item) => (
            <div
              key={item.id}
              onClick={() => !bulkSelectMode && navigate(`/details/${item.id}`)}
              className="flex items-center gap-4 p-4 rounded-lg bg-gradient-to-br from-slate-900/80 to-slate-800/80 backdrop-blur-xl border border-slate-700/30 hover:border-cyan-500/30 transition-all cursor-pointer"
            >
              {bulkSelectMode && (
                <button onClick={(e) => { e.stopPropagation(); toggleSelectItem(item.id); }}>
                  {selectedItems.has(item.id) ? (
                    <CheckSquare size={20} className="text-purple-400" />
                  ) : (
                    <Square size={20} className="text-slate-400" />
                  )}
                </button>
              )}
              <div className="w-16 h-24 rounded bg-slate-700/30 flex-shrink-0 overflow-hidden">
                {item.posterUrl ? (
                  <img src={item.posterUrl} alt={item.title} className="w-full h-full object-cover" />
                ) : (
                  <div className="w-full h-full flex items-center justify-center text-xs text-slate-600">No Poster</div>
                )}
              </div>
              <div className="flex-1">
                <h4 className="text-slate-100">{item.title}</h4>
                <p className="text-sm text-slate-400">{item.year} • {item.type}</p>
                <div className="flex items-center gap-2 mt-1">
                  <span className="px-2 py-0.5 rounded bg-violet-500/20 text-violet-300 text-xs">{item.server}</span>
                  {item.quality && (
                    <span className="px-2 py-0.5 rounded bg-cyan-500/90 text-white text-xs">{item.quality}</span>
                  )}
                  {!item.hasArtwork && (
                    <span className="px-2 py-0.5 rounded bg-amber-500/90 text-white text-xs">No Artwork</span>
                  )}
                  {!item.hasMetadata && (
                    <span className="px-2 py-0.5 rounded bg-red-500/90 text-white text-xs">No Metadata</span>
                  )}
                </div>
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Infinite scroll sentinel — always mounted so observer never breaks */}
      <div ref={sentinelRef} className="h-2" />
      {visibleCount < filteredItems.length ? (
        <button
          onClick={loadMore}
          className="w-full py-4 text-slate-400 hover:text-slate-200 text-sm transition-colors"
        >
          Load more ({visibleCount} of {filteredItems.length})
        </button>
      ) : (
        <button
          onClick={() => {
            lastRequestedCountRef.current = filteredItems.length;
            postBridge({ type: 'servers.loadMoreShelfItems', contentType: contentType || 'movie' });
          }}
          className="w-full py-4 text-slate-500 hover:text-slate-300 text-sm transition-colors"
        >
          Load more from server
        </button>
      )}
    </div>
  );
}
