import { useState } from 'react';
import { Wand2, Film, Tv, Music, Gamepad2, Mic2, Server, Sparkles, Save, Pin, List } from 'lucide-react';

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

const savedShelves = [
  { id: '1', name: 'Friday Night Action', type: 'Movies', items: 24, pinned: true },
  { id: '2', name: 'Rainy Day Thrillers', type: 'Movies', items: 18, pinned: false },
  { id: '3', name: 'Coding Flow Music', type: 'Music', items: 42, pinned: true },
  { id: '4', name: 'Co-op Games', type: 'Games', items: 12, pinned: false },
];

export function ShelfCreatorPage() {
  const [prompt, setPrompt] = useState('');
  const [selectedTypes, setSelectedTypes] = useState<string[]>(['Movies']);
  const [selectedMoods, setSelectedMoods] = useState<string[]>([]);
  const [selectedGenres, setSelectedGenres] = useState<string[]>([]);
  const [familySafe, setFamilySafe] = useState(false);
  const [excludeWatched, setExcludeWatched] = useState(false);

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

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        {/* Main Creator Panel */}
        <div className="lg:col-span-2 space-y-6">
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

        {/* Saved Shelves Sidebar */}
        <div className="space-y-6">
          <div className="bg-gradient-to-br from-slate-900/80 to-slate-800/80 backdrop-blur-xl rounded-xl border border-slate-700/50 p-6">
            <div className="flex items-center gap-2 mb-4">
              <List size={20} className="text-cyan-400" />
              <h3 className="text-slate-100">Saved Shelves</h3>
            </div>
            <div className="space-y-2">
              {savedShelves.map((shelf) => (
                <div
                  key={shelf.id}
                  className="flex items-center justify-between p-3 rounded-lg bg-slate-950/50 border border-slate-700/30 hover:border-cyan-500/30 transition-all group"
                >
                  <div className="flex-1">
                    <h4 className="text-slate-200 text-sm">{shelf.name}</h4>
                    <p className="text-xs text-slate-400">{shelf.type} • {shelf.items} items</p>
                  </div>
                  <button className={`p-1.5 rounded-lg transition-all ${
                    shelf.pinned
                      ? 'text-cyan-400 bg-cyan-500/20'
                      : 'text-slate-500 hover:text-cyan-400 hover:bg-cyan-500/10'
                  }`}>
                    <Pin size={14} />
                  </button>
                </div>
              ))}
            </div>
          </div>

          <div className="bg-gradient-to-br from-slate-900/80 to-slate-800/80 backdrop-blur-xl rounded-xl border border-purple-500/20 p-6">
            <div className="flex items-center gap-2 mb-3">
              <Sparkles size={18} className="text-purple-400" />
              <h4 className="text-slate-100 text-sm">AI Tips</h4>
            </div>
            <ul className="space-y-2 text-xs text-slate-400">
              <li>• Be specific with genres and moods</li>
              <li>• Combine multiple media types</li>
              <li>• Use rating filters for quality</li>
              <li>• Save shelves to reuse later</li>
            </ul>
          </div>
        </div>
      </div>

      <div className="h-8" />
    </div>
  );
}
