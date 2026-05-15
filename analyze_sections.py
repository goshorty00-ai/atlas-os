filepath = 'd:/My Apps/AOS/New build/Atlas.OS/Figma/Mediahub/src/app/pages/details-page.tsx'

with open(filepath, 'r', encoding='utf-8') as f:
    content = f.read()

# Find exact boundaries of each moveable section
# We'll use known comment markers and section starts/ends

# ---- Section markers ----
# Action Bar: from '{/* Action Bar - above sources */}' to '</div>\n\n        <div id="sources-section"'
# Sources: from '<div id="sources-section"' to next section
# Cast: from '{/* Cast */}' to '{/* Similar titles */}'
# Similar: from '{/* Similar titles */}' (actually the JSX conditional) to '{/* Details metadata */}'
# Details: from '{/* Details metadata */}' to end of body

def find_section(content, start_marker, end_marker):
    start = content.find(start_marker)
    end = content.find(end_marker, start)
    if start == -1:
        return -1, -1, ''
    return start, end, content[start:end]

# Find the action bar section
ab_start = content.find('        {/* Action Bar - above sources */}\n')
ab_end = content.find('\n        <div id="sources-section"', ab_start)
# Include the trailing newline
ab_end_full = ab_end + 1  # include the \n before <div id=
action_bar_text = content[ab_start:ab_end_full]
print(f'Action Bar: L start={content[:ab_start].count(chr(10))+1}, end={content[:ab_end_full].count(chr(10))+1}')
print(f'  First: {repr(action_bar_text[:60])}')
print(f'  Last:  {repr(action_bar_text[-60:])}')
print()

# Find sources section (from anchor div to Cast comment)
src_start = content.find('\n        <div id="sources-section"') + 1  # start at the <div id...
src_end = content.find('\n        {/* Cast */}')
src_end_full = src_end + 1
sources_text = content[src_start:src_end_full]
print(f'Sources: chars {src_start}-{src_end_full}')
print(f'  First: {repr(sources_text[:80])}')
print(f'  Last:  {repr(sources_text[-80:])}')
print()

# Find Cast section
cast_start = content.find('\n        {/* Cast */}') + 1
cast_end = content.find('\n\n        {similarItems')
cast_end_full = cast_end + 1
cast_text = content[cast_start:cast_end_full]
print(f'Cast: chars {cast_start}-{cast_end_full}')
print(f'  First: {repr(cast_text[:80])}')
print(f'  Last:  {repr(cast_text[-80:])}')
print()

# Find Similar section
sim_start = content.find('\n\n        {similarItems') + 1
sim_end = content.find('\n\n        {/* Details metadata')
sim_end_full = sim_end + 1
similar_text = content[sim_start:sim_end_full]
print(f'Similar: chars {sim_start}-{sim_end_full}')
print(f'  First: {repr(similar_text[:80])}')
print(f'  Last:  {repr(similar_text[-80:])}')
print()

# Find Details section
det_start = content.find('\n\n        {/* Details metadata') + 2  # skip initial \n\n
det_end = content.find('\n      </div>\n    </div>\n  );\n}')
det_end_full = det_end + 1
details_text = content[det_start:det_end_full]
print(f'Details: chars {det_start}-{det_end_full}')
print(f'  First: {repr(details_text[:80])}')
print(f'  Last:  {repr(details_text[-80:])}')
