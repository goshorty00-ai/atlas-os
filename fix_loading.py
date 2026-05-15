filepath = 'd:/My Apps/AOS/New build/Atlas.OS/Figma/Mediahub/src/app/pages/details-page.tsx'

with open(filepath, 'r', encoding='utf-8') as f:
    content = f.read()

original_len = len(content)
changes = 0

# ---- Fix 1: isTV check should be more robust (include lowercase 'tv' and 'series') ----
# Old: const isTV = rawType === 'TV' || meta?.type === 'series';
old_istv = "const isTV = rawType === 'TV' || meta?.type === 'series';"
new_istv = "const isTV = rawType.toLowerCase() === 'tv' || rawType === 'series' || meta?.type === 'series';"

if old_istv in content:
    content = content.replace(old_istv, new_istv, 1)
    changes += 1
    print('Fix 1 OK: isTV check now handles lowercase tv/series')
else:
    print(f'Fix 1 FAILED: not found')

# ---- Fix 2: Remove cinemetaType from servers.openDetail dependency array ----
# The type from stateItem is sufficient; we shouldn't re-fire streams when meta loads
old_open_detail_deps = "  }, [imdbId, cinemetaType]);"
new_open_detail_deps = "  // eslint-disable-next-line react-hooks/exhaustive-deps\n  }, [imdbId]);"

if old_open_detail_deps in content:
    content = content.replace(old_open_detail_deps, new_open_detail_deps, 1)
    changes += 1
    print('Fix 2 OK: servers.openDetail no longer fires on cinemetaType change')
else:
    print(f'Fix 2 FAILED: not found')

# ---- Fix 3: Add timeout to Cinemeta meta fetch ----
old_meta_fetch = (
    "    fetch(`https://v3-cinemeta.strem.io/meta/${cinemetaType}/${encodeURIComponent(imdbId)}.json`)\n"
    "      .then((r) => r.json())"
)
new_meta_fetch = (
    "    fetch(`https://v3-cinemeta.strem.io/meta/${cinemetaType}/${encodeURIComponent(imdbId)}.json`,\n"
    "      { signal: AbortSignal.timeout(8000) })\n"
    "      .then((r) => r.json())"
)

if old_meta_fetch in content:
    content = content.replace(old_meta_fetch, new_meta_fetch, 1)
    changes += 1
    print('Fix 3 OK: added 8s timeout to Cinemeta meta fetch')
else:
    print(f'Fix 3 FAILED: not found')

# ---- Fix 4: Add timeout to similar items Cinemeta catalog fetch ----
old_similar_fetch = (
    "    fetch(`https://v3-cinemeta.strem.io/catalog/${cinemetaType}/top/genre=${enc}.json`)\n"
    "      .then((r) => r.json())"
)
new_similar_fetch = (
    "    fetch(`https://v3-cinemeta.strem.io/catalog/${cinemetaType}/top/genre=${enc}.json`,\n"
    "      { signal: AbortSignal.timeout(6000) })\n"
    "      .then((r) => r.json())"
)

if old_similar_fetch in content:
    content = content.replace(old_similar_fetch, new_similar_fetch, 1)
    changes += 1
    print('Fix 4 OK: added 6s timeout to similar items fetch')
else:
    print(f'Fix 4 FAILED: not found')

print(f'\nTotal changes: {changes}/4')
print(f'Length: {original_len} -> {len(content)}')

with open(filepath, 'w', encoding='utf-8') as f:
    f.write(content)
print('File saved.')
