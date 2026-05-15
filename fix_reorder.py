filepath = 'd:/My Apps/AOS/New build/Atlas.OS/Figma/Mediahub/src/app/pages/details-page.tsx'

with open(filepath, 'r', encoding='utf-8') as f:
    content = f.read()

original_len = len(content)
changes = 0

# ============================================================
# CHANGE 1: Fix source row play buttons servers.playStream -> servers.playSource
# ============================================================
old_play_stream = "postBridge({ type: 'servers.playStream', payload: { sourceId: s.sourceId, urlOrPath: s.urlOrPath } })"
new_play_source  = "postBridge({ type: 'servers.playSource', payload: { sourceId: s.sourceId } })"
if old_play_stream in content:
    content = content.replace(old_play_stream, new_play_source, 1)
    changes += 1
    print('Change 1 OK: source row play fixed (playStream -> playSource)')
else:
    print('Change 1 FAILED')

# ============================================================
# CHANGE 2: Reorder body sections
# Current order:  ActionBar -> Sources -> Cast -> Similar -> Details
# Desired order:  Cast -> Similar -> Details -> ActionBar -> Sources
# ============================================================

# Define exact anchor text for each section boundary
# These are the exact text strings that start and end each block

# --- Locate each block by its start and end marker ---

# Action Bar block: from its comment to just before sources-section anchor
AB_START = '        {/* Action Bar - above sources */}\n'
# Sources block: from sources-section anchor to just before Cast comment  
SRC_START = '        <div id="sources-section" />\n'
# Cast block: from its comment to just before Similar conditional
CAST_START = '\n        {/* Cast */}\n'
# Similar block: from its conditional to just before Details comment
SIM_START = '\n        {/* Similar titles */}\n'
# Details block: from its comment to just before closing </div> of body
DET_START = '\n        {/* Details metadata */}\n'
# The end of the body
BODY_END = '\n      </div>\n    </div>\n  );\n}'

# Find positions
ab_start_idx = content.find(AB_START)
src_start_idx = content.find(SRC_START)
cast_start_idx = content.find(CAST_START)  
sim_start_idx = content.find(SIM_START)
det_start_idx = content.find(DET_START)
body_end_idx = content.find(BODY_END)

print(f'  ActionBar at char {ab_start_idx} (line ~{content[:ab_start_idx].count(chr(10))+1})')
print(f'  Sources at char {src_start_idx} (line ~{content[:src_start_idx].count(chr(10))+1})')
print(f'  Cast at char {cast_start_idx} (line ~{content[:cast_start_idx].count(chr(10))+1})')
print(f'  Similar at char {sim_start_idx} (line ~{content[:sim_start_idx].count(chr(10))+1})')
print(f'  Details at char {det_start_idx} (line ~{content[:det_start_idx].count(chr(10))+1})')
print(f'  BodyEnd at char {body_end_idx} (line ~{content[:body_end_idx].count(chr(10))+1})')

# Extract each section text
action_bar_text = content[ab_start_idx:src_start_idx]  # ActionBar block (no trailing \n needed, SRC handles)
sources_text    = content[src_start_idx:cast_start_idx]  # Sources block (includes anchor div, ends at \n before Cast)
cast_text       = content[cast_start_idx:sim_start_idx]  # Cast block
similar_text    = content[sim_start_idx:det_start_idx]   # Similar block  
details_text    = content[det_start_idx:body_end_idx]    # Details block (no trailing \n\n)

print(f'\n  ActionBar block ({len(action_bar_text)} chars): first={repr(action_bar_text[:50])}, last={repr(action_bar_text[-30:])}')
print(f'  Sources block ({len(sources_text)} chars): first={repr(sources_text[:50])}, last={repr(sources_text[-30:])}')
print(f'  Cast block ({len(cast_text)} chars): first={repr(cast_text[:50])}, last={repr(cast_text[-30:])}')
print(f'  Similar block ({len(similar_text)} chars): first={repr(similar_text[:50])}, last={repr(similar_text[-30:])}')
print(f'  Details block ({len(details_text)} chars): first={repr(details_text[:50])}, last={repr(details_text[-30:])}')

# Sanity check - all found
if any(x == -1 for x in [ab_start_idx, src_start_idx, cast_start_idx, sim_start_idx, det_start_idx, body_end_idx]):
    print('\nERROR: One or more markers not found. Aborting reorder.')
else:
    # Replace the entire reorderable block with the new order:
    # Cast -> Similar -> Details -> ActionBar -> Sources
    old_block = content[ab_start_idx:body_end_idx]  # entire section to replace
    new_block = cast_text + similar_text + details_text + '\n\n' + action_bar_text + sources_text
    content = content[:ab_start_idx] + new_block + content[body_end_idx:]
    changes += 1
    print('\nChange 2 OK: sections reordered (Cast+Similar+Details ABOVE ActionBar+Sources)')

print(f'\nTotal changes: {changes}/2')
print(f'Length: {original_len} -> {len(content)} (+{len(content)-original_len})')

with open(filepath, 'w', encoding='utf-8') as f:
    f.write(content)
print('File saved.')
