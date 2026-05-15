filepath = 'd:/My Apps/AOS/New build/Atlas.OS/Figma/Mediahub/src/app/pages/details-page.tsx'

with open(filepath, 'r', encoding='utf-8') as f:
    content = f.read()

original_len = len(content)
changes = 0

# ---- Change 1: Remove the body action bar entirely ----
# Block starts at '{/* Action Bar - above sources */}' and ends before '<div id="sources-section"'
AB_BLOCK_START = '\n\n        {/* Action Bar - above sources */}\n'
SRC_ANCHOR = '        <div id="sources-section" />\n'

ab_start = content.find(AB_BLOCK_START)
src_anchor = content.find(SRC_ANCHOR)

if ab_start >= 0 and src_anchor >= 0:
    # Remove everything from AB_BLOCK_START up to (but not including) SRC_ANCHOR
    content = content[:ab_start] + '\n\n' + content[src_anchor:]
    changes += 1
    print('Change 1 OK: body action bar removed')
else:
    print(f'Change 1 FAILED: ab_start={ab_start}, src_anchor={src_anchor}')

# ---- Change 2: Fix hero Play button - scrollIntoView -> handlePlay() ----
# Current: onClick={() => document.getElementById('sources-section')?.scrollIntoView({ behavior: 'smooth' })}
old_scroll = "onClick={() => document.getElementById('sources-section')?.scrollIntoView({ behavior: 'smooth' })}"
new_play = "onClick={handlePlay}"

if old_scroll in content:
    content = content.replace(old_scroll, new_play, 1)
    changes += 1
    print('Change 2 OK: hero Play button uses handlePlay()')
else:
    print('Change 2 FAILED: scroll handler not found')

print(f'\nTotal changes: {changes}/2')
print(f'Length: {original_len} -> {len(content)} (+{len(content)-original_len})')

with open(filepath, 'w', encoding='utf-8') as f:
    f.write(content)
print('File saved.')
