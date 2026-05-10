import { ArrowLeft, Search, SlidersHorizontal, ArrowUpDown, Grid3x3, List, Maximize2, Sparkles, CheckSquare, Square } from 'lucide-react';
import { ServerCard, MediaItem } from './server-card';
import { useState } from 'react';
import { useNavigate } from 'react-router';

interface GridViewProps {
  shelfName: string;
  items: MediaItem[];
  onBack: () => void;
  onOpenCarousel: () => void;
}

export function GridView({ shelfName, items, onBack, onOpenCarousel }: GridViewProps) {
  const navigate = useNavigate();
  const [searchQuery, setSearchQuery] = useState('');
  const [viewMode, setViewMode] = useState<'grid' | 'list'>('grid');
  const [bulkSelectMode, setBulkSelectMode] = useState(false);
  const [selectedItems, setSelectedItems] = useState<Set<string>>(new Set());

  const filteredItems = items.filter(item =>
    item.title.toLowerCase().includes(searchQuery.toLowerCase())
  );

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
    if (selectedItems.size === filteredItems.length) {
      setSelectedItems(new Set());
    } else {
      setSelectedItems(new Set(filteredItems.map(item => item.id)));
    }
  };

  return (
    <div className="space-y-6">
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
            <p className="text-sm text-slate-400">{filteredItems.length} items</p>
          </div>
        </div>
        <div className="flex items-center gap-2">
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
        <div className="flex-1 min-w-[300px]">
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

        {/* Filters */}
        <button className="flex items-center gap-2 px-4 py-2 rounded-lg bg-slate-800/50 text-slate-300 hover:bg-slate-700/50 transition-colors border border-slate-700/30">
          <SlidersHorizontal size={16} />
          Filters
        </button>

        {/* Sort */}
        <button className="flex items-center gap-2 px-4 py-2 rounded-lg bg-slate-800/50 text-slate-300 hover:bg-slate-700/50 transition-colors border border-slate-700/30">
          <ArrowUpDown size={16} />
          Sort
        </button>

        {/* View Mode */}
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

        {/* Bulk Select */}
        <button
          onClick={() => {
            setBulkSelectMode(!bulkSelectMode);
            setSelectedItems(new Set());
          }}
          className={`flex items-center gap-2 px-4 py-2 rounded-lg transition-colors border ${
            bulkSelectMode
              ? 'bg-purple-500/20 text-purple-300 border-purple-500/30'
              : 'bg-slate-800/50 text-slate-300 border-slate-700/30 hover:bg-slate-700/50'
          }`}
        >
          <CheckSquare size={16} />
          Bulk Select
        </button>
      </div>

      {/* Bulk Actions */}
      {bulkSelectMode && (
        <div className="flex items-center justify-between p-4 rounded-lg bg-purple-500/10 border border-purple-500/30">
          <div className="flex items-center gap-3">
            <button
              onClick={selectAll}
              className="flex items-center gap-2 px-3 py-1.5 rounded-lg bg-slate-800/50 text-slate-300 text-sm hover:bg-slate-700/50 transition-colors"
            >
              {selectedItems.size === filteredItems.length ? <CheckSquare size={14} /> : <Square size={14} />}
              {selectedItems.size === filteredItems.length ? 'Deselect All' : 'Select All'}
            </button>
            <span className="text-slate-300 text-sm">{selectedItems.size} selected</span>
          </div>
          {selectedItems.size > 0 && (
            <button className="flex items-center gap-2 px-4 py-2 rounded-lg bg-gradient-to-r from-purple-500 to-pink-500 text-white hover:shadow-lg transition-all">
              <Sparkles size={16} />
              AI Fix Selected
            </button>
          )}
        </div>
      )}

      {/* Grid/List */}
      {viewMode === 'grid' ? (
        <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5 2xl:grid-cols-6 gap-6">
          {filteredItems.map((item) => (
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
              <ServerCard item={item} />
            </div>
          ))}
        </div>
      ) : (
        <div className="space-y-2">
          {filteredItems.map((item) => (
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
    </div>
  );
}
