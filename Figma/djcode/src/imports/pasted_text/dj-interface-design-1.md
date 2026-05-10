Create a **desktop DJ mixing interface from scratch** for my app with a **fixed single-screen layout** where **everything important is visible at once**.

## Absolute rule

The interface must fit inside **one full desktop screen with no vertical scrolling anywhere except the track list at the bottom**.

### Only scrollable area allowed

* the **track rows inside the bottom browser table**

### Must NOT scroll

* top toolbar
* waveform area
* left deck
* center mixer
* right deck
* source buttons
* browser sidebar
* search/filter row

Do **not** place any internal scrollbars inside deck panels, mixer panels, sidebar panels, waveform sections, or source rows.

---

## Main goal

Design a **real professional 2-deck DJ workstation** that feels like finished desktop software.

It must include:

* **2 full visible decks**
* **center mixer**
* **clearly visible horizontal crossfader**
* **small waveform section**
* **bottom browser**
* **Spotify, SoundCloud, TIDAL, Apple Music, Beatport, Local Files integration**
* **no overlaps**
* **no cropped controls**
* **no giant empty space**
* **no stacked sections covering each other**

---

## Mandatory screen composition

Fit the whole UI inside a normal desktop frame in this exact order:

### 1. Slim fixed toolbar

Very short top bar only.

Include:

* Library
* Save
* FX
* Record
* Mic
* Broadcast
* Headphones
* Audio
* Connected status
* Settings

Keep it compact and low height.

### 2. Small compact waveform area

Waveforms must be **short and shallow**, not tall.

Requirements:

* Deck A waveform
* Deck B waveform
* BPM / key / elapsed / total time
* playhead marker
* beat grid
* compact height only

This section must use **minimal vertical space**.

### 3. Main performance zone

This is the priority area and must be fully visible.

Use **3 equal columns**:

* Left Deck A
* Center Mixer
* Right Deck B

The whole performance zone must fit on screen with **no cropping and no overlap with the browser**.

#### Deck A must visibly include:

* jog wheel
* deck label
* BPM / key / time
* tempo slider
* play
* cue
* sync
* pitch bend
* loop controls
* hot cues
* level meter
* mini track info
* slip / quant / vinyl toggles

#### Mixer must visibly include:

* channel A strip
* channel B strip
* gain
* EQ high / mid / low
* filter
* cue buttons
* channel faders
* master meter
* headphone control
* FX assign
* **horizontal crossfader fully visible at the bottom**

The crossfader is mandatory and must not be hidden or cut off.

#### Deck B must visibly include:

* jog wheel
* deck label
* BPM / key / time
* tempo slider
* play
* cue
* sync
* pitch bend
* loop controls
* hot cues
* level meter
* mini track info
* slip / quant / vinyl toggles

---

## Critical spacing rule

The browser must start **below** the decks and mixer.
It must **not overlap** Deck A, Mixer, or Deck B.
No section may sit on top of another section.

The performance zone and browser must be separated by a clear divider.

---

## Bottom browser layout

The browser should be compact and fixed in height.

### Browser split

Use:

* **left fixed sidebar**
* **right browser content**

### Sidebar

Include:

* All Tracks
* Favorites
* Recent
* Playlists
* Crates
* History
* Downloads
* Local Files
* Streaming Services

Sidebar must be fully visible with **no scrollbar**.

### Browser content

At the top of browser content include:

* search bar
* filter button
* sort button
* source chips for Spotify, SoundCloud, TIDAL, Apple Music, Beatport, Local Files

These controls must be fixed and fully visible.

Below that, add the track table with columns:

* #
* Title
* Artist
* Album
* Genre
* BPM
* Key
* Time
* Source
* Energy

Only the **track rows area** may scroll vertically.

---

## Streaming integrations

Show built-in source support for:

* Spotify
* SoundCloud
* TIDAL
* Apple Music
* Beatport
* Local Files

Show them as clean source chips or tabs in the browser header, not giant panels.

The UI should look like it supports:

* switching sources
* searching across sources
* dragging tracks to Deck A or Deck B
* showing track source labels in the table

Keep these integrations compact and clean.

---

## Exact proportion guidance

Use this priority for vertical space:

* toolbar = very small
* waveforms = small
* decks + mixer = largest area
* browser = compact but usable

The deck/mixer area should get the most height.
The browser should be short enough that it does not cover the performance controls.

---

## Visual style

* dark premium desktop software
* matte black / charcoal / graphite
* subtle cyan / amber / white accents
* thin dividers
* crisp typography
* minimal glow
* realistic DJ software layout
* futuristic but disciplined

---

## Strict fail conditions to avoid

Do not do any of these:

* no overlapping browser over decks
* no clipped jog wheels
* no hidden crossfader
* no internal panel scrollbars
* no giant waveform area
* no oversized browser
* no huge headers
* no floating cards
* no abstract dashboard styling
* no mobile layout
* no partial deck controls off-screen

---

## Final requirement

At first glance, without scrolling, I must be able to see:

* both jog wheels
* full mixer
* horizontal crossfader
* both waveforms
* browser sidebar
* search/filter/source chips
* top of the track table

Only the **track rows** may scroll.
