filepath = 'd:/My Apps/AOS/New build/Atlas.OS/Figma/Mediahub/src/app/pages/details-page.tsx'

with open(filepath, 'r', encoding='utf-8') as f:
    content = f.read()

original_len = len(content)
changes = 0

# ---- Change 1: Hero Play button -> scroll to #sources-section ----
old = 'onClick={handlePlay}\n                className="px-3 py-1.5 rounded-full bg-cyan-500 hover:bg-cyan-400 text-slate-950 text-xs flex items-center gap-1.5 shadow-lg shadow-cyan-500/30"'
new = "onClick={() => document.getElementById('sources-section')?.scrollIntoView({ behavior: 'smooth' })}\n                className=\"px-3 py-1.5 rounded-full bg-cyan-500 hover:bg-cyan-400 text-slate-950 text-xs flex items-center gap-1.5 shadow-lg shadow-cyan-500/30\""
if old in content:
    content = content.replace(old, new, 1)
    changes += 1
    print('Change 1 OK: hero Play scrolls to sources-section')
else:
    print('Change 1 FAILED: hero play button not found')
    # Try alternate search
    idx = content.find('onClick={handlePlay}')
    print(f'  handlePlay onClick occurrences search, first at idx={idx}')
    ctx = content[idx-5:idx+200] if idx >= 0 else ''
    print(f'  Context: {repr(ctx[:150])}')

# ---- Change 2: Action bar Play button -> servers.play ----
old2 = '''          <button
            onClick={handlePlay}
            className="px-5 py-2.5 rounded-full bg-cyan-500 hover:bg-cyan-400 text-slate-950 text-sm font-semibold flex items-center gap-2 shadow-lg shadow-cyan-500/30 transition-colors"
          >
            <Play size={15} fill="currentColor" /> Play
          </button>'''
new2 = """          <button
            onClick={() => postBridge({ type: 'servers.play', payload: { metaId: imdbId, id: imdbId ?? id, title, type: isTV ? 'series' : 'movie' } })}
            className="px-5 py-2.5 rounded-full bg-cyan-500 hover:bg-cyan-400 text-slate-950 text-sm font-semibold flex items-center gap-2 shadow-lg shadow-cyan-500/30 transition-colors"
          >
            <Play size={15} fill="currentColor" /> Play
          </button>"""
if old2 in content:
    content = content.replace(old2, new2, 1)
    changes += 1
    print('Change 2 OK: action bar Play uses servers.play')
else:
    print('Change 2 FAILED: action bar play button not found')

# ---- Change 3: Add id anchor before Sources section ----
old3 = '        {/* Sources */}\n        <Section title="Sources"'
new3 = '        <div id="sources-section" />\n        {/* Sources */}\n        <Section title="Sources"'
if old3 in content:
    content = content.replace(old3, new3, 1)
    changes += 1
    print('Change 3 OK: sources-section anchor added')
else:
    print('Change 3 FAILED: Sources section not found')

print(f'\nTotal changes: {changes}/3')
print(f'Length: {original_len} -> {len(content)} (+{len(content)-original_len})')

with open(filepath, 'w', encoding='utf-8') as f:
    f.write(content)
print('File saved.')
