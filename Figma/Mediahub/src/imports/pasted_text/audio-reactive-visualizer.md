Fix only the **Music Visualizer / Music Galaxy Explorer**.

Do not redesign the whole Music page.

The current visualizer is wrong because:

* The mode buttons do not visibly change anything.
* The visualizer does not look audio-reactive.
* The galaxy map looks like random blobs.
* Too many buttons are visible all the time.
* The controls waste space.
* The UI feels like decoration instead of a working music visualizer.

Rebuild this component as a proper **audio-reactive futuristic visualizer system**.

---

## Main rule

The visualizer must react to music.

Every visual mode must clearly look different and must show sound-reactive movement.

Use visual elements like:

* Bass pulses
* Waveform movement
* Spectrum bars
* Beat rings
* Particle bursts
* Frequency bands
* Reactive glow
* Lyrics sync highlights
* Album-art colour extraction
* Energy trails
* BPM pulse markers

No static blobs. No useless decorative dots.

---

## Replace visible button clutter with dropdown pills

Do not show loads of big buttons everywhere.

Use compact dropdown pills instead.

Top visualizer control bar should include only:

1. **Visualizer Mode** dropdown
2. **AI View** dropdown
3. **Focus** dropdown
4. **Options** dropdown
5. **Full Screen** button
6. **Close Explorer** button

Everything else goes inside these dropdowns.

---

## Dropdown details

### Visualizer Mode dropdown

Contains:

* Spectrum Tunnel
* Waveform City
* Circular Pulse
* Lyrics Nebula
* Mood Galaxy
* Album Orbit
* Particle Storm
* Bass Reactor

Selecting each option must visibly change the main visualizer.

### AI View dropdown

Contains:

* Similar Vibe
* Mood Path
* Artist Map
* Genre Cluster
* Discovery Route
* Listening History
* Recommended Next

Selecting each option overlays a different AI layer.

### Focus dropdown

Contains:

* Current Track
* Current Artist
* Current Album
* Genre
* Mood
* BPM
* Decade
* Playlist

Selecting a focus filters/highlights the visualizer.

### Options dropdown

Contains:

* Rotate On/Off
* Zoom In
* Zoom Out
* Reset View
* Show Lyrics
* Show Queue
* Show Album Art
* Show Frequency Labels
* Right Click Menu Help

---

## Visualizer modes must look different

### Spectrum Tunnel

A 3D tunnel of frequency bars moving toward the viewer.

Must include:

* Bass bars
* Mid bars
* Treble bars
* Beat flashes
* Depth motion
* Audio-reactive glow

### Waveform City

A futuristic neon city made from waveform skyscrapers.

Must include:

* Tall bass towers
* Mid frequency buildings
* Treble antenna spikes
* Moving waveform road
* Beat-reactive skyline glow

### Circular Pulse

A circular audio reactor around the album art.

Must include:

* Album art in centre
* Bass ring
* Mid ring
* Treble ring
* Beat shockwaves
* Rotating frequency ticks

### Lyrics Nebula

A lyric-driven particle field.

Must include:

* Current lyric line glowing
* Next lyric line faded
* Words drifting as particles
* Beat pulses behind lyrics
* Vocal frequency glow

### Mood Galaxy

A proper music galaxy, not random dots.

Must include:

* Songs as small stars
* Artists as larger planets
* Albums as orbit rings
* Genres as coloured clusters
* Mood routes as glowing paths
* Current track highlighted
* Beat-reactive star pulses

### Album Orbit

Album covers orbit around the current track.

Must include:

* Current album large in centre
* Related albums orbiting
* Cover art visible
* Orbit speed reacts to BPM
* Bass pulses shake orbit rings

### Particle Storm

A high-energy particle visualizer.

Must include:

* Particles bursting on beats
* Frequency-coloured trails
* Bass shockwaves
* Treble sparks
* Motion blur

### Bass Reactor

A heavy bass-focused visualizer.

Must include:

* Large bass core
* Subwoofer pulse rings
* Low-frequency bars
* Screen glow on kick drum
* Energy meter

---

## Working state feedback

When the user changes Visualizer Mode:

* The active mode label changes.
* Main visualizer changes shape/layout.
* Small description changes.
* Mini legend changes.
* Animation style changes.

When the user uses AI View:

* Overlay changes.
* Show a compact active chip, e.g. “AI View: Similar Vibe”.
* Display a small floating info card with the result.

When the user uses Focus:

* Highlight selected focus.
* Dim unrelated elements.
* Show compact active filter chip.

When Options are used:

* Rotate toggles visibly.
* Zoom percentage updates.
* Lyrics panel appears/disappears.
* Queue panel appears/disappears.
* Frequency labels appear/disappear.

---

## Full-screen mode

Full-screen should be for the visualizer only.

In full-screen:

* Hide most page clutter.
* Keep only compact dropdown pills.
* Keep tiny now-playing strip.
* Fill the screen with the active audio-reactive visualizer.
* ESC exits full screen.
* Close Explorer collapses the visualizer.

Do not show a big left column of buttons.

---

## Now playing player

Keep the player compact at the bottom.

It should show:

* Small album art
* Track title
* Artist
* Tiny waveform timeline
* Play/pause
* Previous/next
* Volume
* Queue
* Lyrics
* Visualizer toggle

Do not make it a huge box.

---

## Right-click menu

Right-click menu should be available from:

* Album art
* Track title
* Visualizer node
* Album cover
* Playlist card

Menu options:

* Play Now
* Add to Queue
* Add to Playlist
* Show Similar Vibe
* AI Optimize Audio
* Get Album Metadata
* Fix Track Metadata
* AI Generate Cover
* Edit Custom Cover
* Replace Cover Image
* Restore Original Cover
* Search Lyrics
* Sync Lyrics
* Convert to Karaoke
* Analyse BPM / Key
* Show File Location

Keep the menu compact.

---

## Do not include

Do not include:

* Big vertical button stack
* Lots of visible buttons
* Static blob map
* Random dots pretending to be a visualizer
* Decorative controls that do nothing
* Huge player box
* Big empty panel
* Buttons that do not visibly change the screen
* Normal Spotify layout

Final target: a compact, premium, futuristic **audio-reactive music visualizer system** with dropdown controls, real-looking visualizer modes, full-screen mode, and visible working state changes.
