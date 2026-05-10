import { useState } from 'react';
import { Wand2, Film, Tv, Music, Gamepad2, Mic2, Save, Sparkles } from 'lucide-react';

const mediaTypes = [
  { icon: Film, label: 'Movies' },
  { icon: Tv, label: 'TV Shows' },
  { icon: Music, label: 'Music' },
  { icon: Gamepad2, label: 'Games' },
  { icon: Mic2, label: 'Karaoke' },
];

const moods = ['Dark', 'Action', 'Comedy', 'Thriller', 'Sci-Fi', 'Romance', 'Drama', 'Horror'];

export function AIShelfCreator() {
  const [prompt, setPrompt] = useState('');
  const [selectedTypes, setSelectedTypes] = useState<string[]>(['Movies']);
  const [selectedMoods, setSelectedMoods] = useState<string[]>([]);

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

  return (
    <div
      className="relative bg-gradient-to-br from-slate-900/80 to-slate-800/80 backdrop-blur-xl rounded-2xl border border-purple-500/30 p-6 space-y-5"
      style={{
        backdropFilter: 'blur(20px)',
        boxShadow: '0 0 40px rgba(168, 85, 247, 0.15)',
      }}
    >
      {/* Glow effect */}
      <div className="absolute -inset-0.5 bg-gradient-to-r from-cyan-500 to-purple-500 rounded-2xl -z-10 blur opacity-10" />

      {/* Header */}
      <div className="flex items-center gap-3">
        <div className="p-2 rounded-xl bg-gradient-to-r from-cyan-500 to-purple-500">
          <Wand2 size={20} className="text-white" />
        </div>
        <div>
          <h3 className="text-slate-100">AI Shelf Creator</h3>
          <p className="text-xs text-slate-400">Generate custom media shelves with AI</p>
        </div>
      </div>

      {/* Prompt Input */}
      <div>
        <label className="text-sm text-slate-300 mb-2 block">Describe your shelf</label>
        <textarea
          value={prompt}
          onChange={(e) => setPrompt(e.target.value)}
          placeholder="e.g., 'Create a shelf for dark sci-fi movies with good ratings'"
          className="w-full bg-slate-950/50 text-slate-200 placeholder:text-slate-500 rounded-xl border border-slate-700 focus:border-cyan-500/50 focus:ring-2 focus:ring-cyan-500/20 p-4 outline-none resize-none transition-all"
          rows={3}
        />
      </div>

      {/* Media Type Selector */}
      <div>
        <label className="text-sm text-slate-300 mb-2 block">Media Types</label>
        <div className="flex flex-wrap gap-2">
          {mediaTypes.map((type) => {
            const Icon = type.icon;
            const isSelected = selectedTypes.includes(type.label);
            return (
              <button
                key={type.label}
                onClick={() => toggleType(type.label)}
                className={`flex items-center gap-2 px-4 py-2 rounded-xl transition-all ${
                  isSelected
                    ? 'bg-gradient-to-r from-cyan-500 to-purple-500 text-white shadow-lg shadow-cyan-500/30'
                    : 'bg-slate-800/50 text-slate-400 hover:text-slate-200 hover:bg-slate-700/50'
                }`}
              >
                <Icon size={16} />
                <span className="text-sm">{type.label}</span>
              </button>
            );
          })}
        </div>
      </div>

      {/* Mood Selector */}
      <div>
        <label className="text-sm text-slate-300 mb-2 block">Moods & Genres</label>
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

      {/* Filters */}
      <div className="grid grid-cols-3 gap-4">
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
      </div>

      {/* Actions */}
      <div className="flex gap-3">
        <button className="flex-1 flex items-center justify-center gap-2 px-6 py-3 rounded-xl bg-gradient-to-r from-cyan-500 to-purple-500 text-white hover:shadow-xl hover:shadow-cyan-500/30 transition-all">
          <Sparkles size={18} />
          <span>Generate Shelf</span>
        </button>
        <button className="flex items-center gap-2 px-6 py-3 rounded-xl bg-slate-800/50 text-slate-300 hover:text-white hover:bg-slate-700 transition-all">
          <Save size={18} />
          <span>Save</span>
        </button>
      </div>
    </div>
  );
}
