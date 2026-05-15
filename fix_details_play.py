filepath = 'd:/My Apps/AOS/New build/Atlas.OS/Figma/Mediahub/src/app/pages/details-page.tsx'

with open(filepath, 'r', encoding='utf-8') as f:
    content = f.read()

original_len = len(content)

# ---- Change 1: Insert handlePlay after trailer callback ----
trailer_end = '  }, [trailerVideoId, stateItem, title, year, cinemetaType]);\n'
handle_play_fn = '''  }, [trailerVideoId, stateItem, title, year, cinemetaType]);

  // -- Play handler --
  const handlePlay = useCallback(() => {
    const playable = streams.filter((s) => s.isPlayable);
    const target = playable.find((s) => s.sourceId === selectedSource) ?? playable[0];
    if (target) {
      postBridge({ type: 'servers.playSource', payload: { sourceId: target.sourceId } });
    } else {
      postBridge({ type: 'servers.play', payload: { metaId: imdbId, id: imdbId, title, type: isTV ? 'series' : 'movie' } });
    }
  }, [streams, selectedSource, imdbId, title, isTV]);
'''
if trailer_end in content:
    content = content.replace(trailer_end, handle_play_fn, 1)
    print('Change 1 OK: handlePlay inserted')
else:
    print('Change 1 FAILED: trailer_end not found')

# ---- Change 2: Fix hero Play button ----
old_play = "onClick={() => postBridge({ type: 'servers.playItem', payload: { id: imdbId ?? id, title, mediaType: cinemetaType } })}"
new_play = "onClick={handlePlay}"
if old_play in content:
    content = content.replace(old_play, new_play, 1)
    print('Change 2 OK: hero Play button fixed')
else:
    print('Change 2 FAILED: playItem not found')

# ---- Change 3: Insert action bar above Sources ----
sources_marker = '        {/* Sources */}\n'
action_bar = '''        {/* Action Bar - above sources */}
        <div className="flex items-center gap-3 flex-wrap">
          <button
            onClick={handlePlay}
            className="px-5 py-2.5 rounded-full bg-cyan-500 hover:bg-cyan-400 text-slate-950 text-sm font-semibold flex items-center gap-2 shadow-lg shadow-cyan-500/30 transition-colors"
          >
            <Play size={15} fill="currentColor" /> Play
          </button>
          <button
            onClick={handleTrailerClick}
            disabled={trailerLoading}
            className="px-4 py-2.5 rounded-full bg-slate-800 hover:bg-slate-700 border border-slate-700/60 text-slate-200 text-sm flex items-center gap-2 disabled:opacity-60 transition-colors"
          >
            {trailerLoading ? <Loader2 size={13} className="animate-spin" /> : <Film size={13} />}
            Trailer
          </button>
          <button
            onClick={() => setWatchlisted(!watchlisted)}
            className={`px-4 py-2.5 rounded-full text-sm flex items-center gap-2 border transition-colors ${watchlisted ? 'bg-cyan-500/15 border-cyan-400/40 text-cyan-200' : 'bg-slate-800 hover:bg-slate-700 border-slate-700/60 text-slate-200'}`}
          >
            {watchlisted ? <Check size={13} /> : <Plus size={13} />} {watchlisted ? 'Watchlisted' : 'Watchlist'}
          </button>
          <button
            onClick={() => setWatched(!watched)}
            className={`px-4 py-2.5 rounded-full text-sm flex items-center gap-2 border transition-colors ${watched ? 'bg-emerald-500/15 border-emerald-400/40 text-emerald-200' : 'bg-slate-800 hover:bg-slate-700 border-slate-700/60 text-slate-200'}`}
          >
            <Eye size={13} /> {watched ? 'Watched' : 'Mark Seen'}
          </button>
          <button className="p-2.5 rounded-full bg-slate-800 hover:bg-slate-700 border border-slate-700/60 text-slate-200 transition-colors">
            <Share2 size={13} />
          </button>
        </div>

        {/* Sources */}
'''
if sources_marker in content:
    content = content.replace(sources_marker, action_bar, 1)
    print('Change 3 OK: action bar inserted above Sources')
else:
    print('Change 3 FAILED: sources_marker not found')

print(f'Length: {original_len} -> {len(content)} (+{len(content)-original_len})')

with open(filepath, 'w', encoding='utf-8') as f:
    f.write(content)
print('File saved.')
