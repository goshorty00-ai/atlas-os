Fix only the **Music Galaxy Explorer functionality and interaction states**.

Do not redesign the whole Music page. Keep the current visual direction, but make the Music Galaxy Explorer actually behave like a working component.

Right now the buttons look decorative. Every button must have a clear working state and visible result.

## Main requirement

The **Music Galaxy Explorer** must have three states:

1. **Closed / collapsed**
2. **Open inside Music page**
3. **Exclusive full-screen explorer mode**

Add clear buttons for:

* Open Explorer
* Close Explorer
* Full Screen
* Exit Full Screen

These buttons must be visible, compact, and functional-looking.

---

## State 1: Closed / collapsed

When closed, the Music Galaxy Explorer should not take over the page.

Show a compact collapsed card:

* Title: Music Galaxy Explorer
* Small preview thumbnail/mini visualizer
* Short description: “Explore songs, artists, albums, moods and genres as a live galaxy map.”
* Button: Open Explorer
* Button: Full Screen

The rest of the Music page should still be visible:

* Music shelves
* Albums
* Playlists
* Compact bottom player

---

## State 2: Open inside Music page

When the user clicks **Open Explorer**, expand the galaxy panel inside the Music page.

Show:

* Large contained galaxy visualizer
* Compact controls
* Bottom mini player
* Close Explorer button
* Full Screen button

The **Close Explorer** button collapses the panel back to the compact preview card.

The open state must not hide the whole Music page unless Full Screen is clicked.

---

## State 3: Exclusive full-screen mode

When the user clicks **Full Screen**, open an exclusive Music Galaxy Explorer mode.

This mode should fill the available module viewport.

Hide or reduce:

* Sidebar
* Shelves
* Other music panels
* Normal page content

Full-screen explorer should show:

* Huge galaxy visualizer
* Floating compact control dock
* Floating now-playing mini player
* Floating mode selector
* Exit Full Screen button
* Close Explorer button
* Keyboard hints

Add visible hints:

* ESC exits full screen
* Mouse drag rotates
* Scroll zooms
* Click node to play
* Right-click opens AI options

---

## Wire every button to a visible action

Every control must have an obvious result.

### Rotate

Button label: Rotate

Click result:

* Toggles auto-rotation on/off
* Button state changes to “Rotating” when active
* Galaxy shows orbit animation lines

### Zoom In

Button label: Zoom In

Click result:

* Galaxy nodes become larger/closer
* Zoom percentage changes, e.g. 120%

### Zoom Out

Button label: Zoom Out

Click result:

* Galaxy nodes become smaller/further away
* Zoom percentage changes, e.g. 80%

### Focus Artist

Button label: Focus Artist

Click result:

* Highlights one artist cluster
* Dims unrelated nodes
* Shows artist info mini-card
* Button state becomes selected

### Mood Path

Button label: Mood Path

Click result:

* Draws a glowing route between mood nodes
* Shows path label, e.g. “Chill → Synthwave → Cinematic”
* Button state becomes selected

### Similar Vibe

Button label: Similar Vibe

Click result:

* Highlights similar tracks around the current song
* Shows “14 similar tracks found”
* Adds temporary similar-vibe shelf below or side panel

### Isolate Genre

Button label: Isolate Genre

Click result:

* Opens a small genre selector
* Choosing a genre filters the galaxy to that genre
* Shows active filter chip, e.g. “Genre: Synthwave”
* Add Clear Filter button

### Visualizer

Button label: Visualizer

Click result:

* Opens visualizer mode selector
* User can switch between:

  * Galaxy Map
  * Spectrum Tunnel
  * Waveform City
  * Circular Pulse
  * Lyrics Nebula
  * Mood Constellation

Selected mode must visibly change the visualizer.

### AI Map

Button label: AI Map

Click result:

* Shows AI-generated listening paths
* Adds glowing AI route lines
* Shows small panel:

  * “AI found 3 discovery routes”
  * Deep Focus Route
  * Cyberpunk Route
  * Late Night Route

---

## Mode selector must work

The top mode chips must visually change the main panel.

### Galaxy Map

Shows node galaxy.

### Spectrum Tunnel

Shows a tunnel of animated equalizer bars receding into depth.

### Waveform City

Shows waveform skyscrapers/3D bars like a neon city.

### Circular Pulse

Shows circular beat-reactive rings around album art.

### Lyrics Nebula

Shows floating synced lyrics particles around the current song.

### Mood Constellation

Shows mood groups connected like star constellations.

Each selected mode must have:

* Active selected chip
* Different visual layout
* Short mode label
* Relevant controls

---

## Full-screen control dock

In full-screen mode, create a floating control dock with:

* Rotate
* Zoom In
* Zoom Out
* Focus Artist
* Mood Path
* Similar Vibe
* Isolate Genre
* Visualizer Mode
* AI Map
* Reset View
* Exit Full Screen

Keep the dock compact and futuristic.

---

## Right-click menu inside explorer

Right-clicking any node should open a custom dark glass context menu.

Options:

* Play Track
* Add to Queue
* Open Artist
* Open Album
* Show Similar Vibe
* Create Playlist From This
* AI Optimize Audio
* Get Album Metadata
* AI Generate Cover
* Edit Custom Cover
* Replace Cover Image
* Search Lyrics
* Convert to Karaoke
* Analyse BPM / Key

This menu must look custom, not a default browser menu.

---

## Bottom mini player

The bottom player must stay compact.

It should include:

* Small album art
* Track title
* Artist
* Mini waveform
* Play/pause
* Previous/next
* Shuffle
* Repeat
* Lyrics
* Queue
* Volume
* Full-screen visualizer toggle

Buttons must show active/hover states.

---

## Interaction states to show

Add visual states for:

* Explorer closed
* Explorer open
* Explorer full-screen
* Auto-rotate active
* Zoom level changed
* Artist focused
* Genre isolated
* Mood path active
* Similar vibe results active
* AI map active
* Visualizer mode changed
* Right-click menu open

---

## Do not do these

Do not leave buttons decorative.
Do not make buttons that do nothing.
Do not hide all music shelves unless full-screen is active.
Do not make the explorer permanently open.
Do not create giant buttons.
Do not create a normal music player layout.
Do not redesign unrelated pages.

Final result: the Music Galaxy Explorer must clearly open, close, enter exclusive full-screen mode, exit full-screen mode, and every button must visibly do what it says.
