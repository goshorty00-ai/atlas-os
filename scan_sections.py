filepath = 'd:/My Apps/AOS/New build/Atlas.OS/Figma/Mediahub/src/app/pages/details-page.tsx'

with open(filepath, 'r', encoding='utf-8') as f:
    lines = f.readlines()

for i, l in enumerate(lines):
    stripped = l.strip()
    if 'Section title=' in stripped or '{/* ' in stripped or 'sources-section' in stripped:
        print(f'L{i+1}: {stripped[:120]}')
