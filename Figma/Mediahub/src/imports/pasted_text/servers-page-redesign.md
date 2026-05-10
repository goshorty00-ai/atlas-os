Fix and redesign only the **Servers page** and the **reusable carousel system** for the AI Media Centre.

Do not redesign Discovery. Do not redesign Music. Do not redesign TV. Do not redesign Games. Do not redesign Karaoke. Focus only on:

1. Servers page layout
2. Server shelves
3. Reusable carousel open/close behaviour
4. Grid “View All” behaviour

The current Servers page is wrong because the carousel is always sitting on the page and does not clearly open/close into a proper full carousel view. Fix that.

---

## Main requirement: reusable carousel system

Create a reusable carousel component that can be used later in:

* Servers
* Movies
* TV
* Music
* Games

Every shelf should have an **Open Carousel** button.

When carousel is closed:

* The page shows normal horizontal shelves.
* Each shelf has compact cards.
* Each shelf has a **View All** button.
* Each shelf has an **Open Carousel** button.
* No giant carousel should be visible by default.

When user clicks **Open Carousel**:

* The carousel opens into its own focused page/panel/view.
* It should feel like a dedicated cinematic carousel mode.
* It should replace or overlay the main shelf view.
* It must have a clear **Close Carousel** button.
* It must show the selected shelf name at the top.
* It must show all item info clearly.
* It must allow left/right navigation.
* It must support mouse wheel navigation.
* It must support keyboard navigation.
* It should have an Escape-to-close hint.

The carousel open view must include:

* Large selected poster/cover in the centre
* Angled side cards left and right
* Background blur based on selected item
* Play button
* Trailer button
* Details button
* Add to Shelf button
* Metadata status
* Ratings
* Genre tags
* Runtime or episode length
* Year
* Server/source badge
* Quality badge, for example 1080p / 4K / HDR
* File status
* AI verdict
* Short description/synopsis
* Bottom action rail
* Close Carousel button

The carousel should not be decorative. It must look like a real working interaction state.

---

## Servers page default view

The default Servers page should be shelf-based, not carousel-first.

At the top of the Servers page, create a compact server overview strip:

* Total servers
* Online servers
* Library size
* Storage used
* Missing artwork count
* Broken file count
* Last scan time
* AI Cleanup button
* Rescan All button

Do not use a huge title. Keep it compact.

---

## Server health cards

Below the overview strip, create compact server cards for:

* Plex Server
* Jellyfin Server
* Local Media Folders
* NAS Library
* External Drive Library

Each server card should show:

* Online/offline status
* Storage used
* Movies count
* TV count
* Music count
* Games count if relevant
* Last scan
* Scan progress
* Missing metadata
* Missing artwork
* Broken links
* Duplicates found
* Rescan button
* AI Fix button
* Open Library button

Make these cards futuristic, dark, glassy, and compact.

---

## Server shelves

The Servers page must show shelves grouped by different genres and useful server categories.

Each shelf must have:

* Shelf title
* Item count
* Server/source filter badge
* View All button
* Open Carousel button
* Sort button
* Horizontal cards

Create these shelves:

### Recently Added From Servers

Mixed movies and TV recently found.

### Latest Movies From Servers

Movie cards only.

### Latest TV From Servers

TV episode/show cards only.

### Continue From Server

Items in progress from server libraries.

### 4K / HDR Library

High quality movie/TV shelf.

### Action

Genre shelf.

### Sci-Fi

Genre shelf.

### Comedy

Genre shelf.

### Horror

Genre shelf.

### Family

Genre shelf.

### Documentaries

Genre shelf.

### Music Videos

Music/video shelf if server contains them.

### Missing Artwork

Cards needing poster or backdrop fixes.

### Missing Metadata

Cards needing title/year/genre/rating fixes.

### Broken Links

Files that cannot be found or played.

### Duplicates Found

Duplicate files grouped together.

### AI Cleanup Suggestions

AI-generated shelf with recommended fixes.

---

## Shelf card design

Each server shelf card should include:

* Poster/thumbnail
* Title
* Year
* Type badge: Movie / TV / Music Video / Game
* Server source badge
* Rating badge
* Quality badge
* Metadata status badge
* Artwork status badge
* Hover overlay with:

  * Play
  * Trailer
  * Details
  * Fix Metadata
  * Add to Shelf

Cards should look premium and loaded with real usable information.

---

## View All grid behaviour

Each shelf must have a **View All** button.

When clicked, it opens a grid view for that shelf.

Grid view must include:

* Shelf title
* Back to Servers button
* Search within shelf
* Filters
* Sort dropdown
* Grid/list toggle
* Cards in a responsive grid
* Bulk select mode
* AI Fix Selected button
* Open Carousel button for that grid’s items

Example:

Click “View All” on Sci-Fi shelf → opens Sci-Fi grid view with all Sci-Fi items from servers.

Click “View All” on Missing Artwork shelf → opens grid of items missing artwork with AI Fix Selected.

Click “View All” on Broken Links shelf → opens grid with file path/status and repair actions.

---

## Carousel from View All

The grid view should also have an **Open Carousel** button.

When clicked:

* It opens the same reusable carousel system.
* It uses the current grid’s items.
* It has Close Carousel.
* Closing carousel returns back to the grid view, not the main Servers page.

---

## Carousel open view design

Design the carousel open view as a full-page focused mode inside the module.

Layout:

Top row:

* Back / Close Carousel button
* Shelf name
* Item position, for example 7 of 118
* Server/source filter
* Auto rotate toggle
* Speed slider if needed

Centre:

* 3D coverflow cards
* Selected item large in centre
* Left/right cards angled
* Soft background blur
* Large play icon on selected card

Bottom information rail:

* Title
* Year
* Runtime
* Genres
* Ratings: TMDb / IMDb / User
* Server source
* File path short preview
* Quality
* Metadata status
* Artwork status
* AI verdict
* Short description
* Buttons:

  * Play
  * Trailer
  * Details
  * Fix Metadata
  * Add to Shelf
  * Mark Watched

This must feel like a proper cinematic media inspection mode.

---

## Visual style

Use:

* Dark navy/black background
* Glassmorphism cards
* Cyan/violet/electric blue accents
* Soft neon glow
* Thin borders
* Compact futuristic UI
* Smooth transitions
* Premium media dashboard style

Avoid:

* Giant headers
* Huge logos
* Empty blank carousel cards
* Oversized hero banners
* Basic grey placeholder cards
* Fake buttons with no purpose
* Plain admin-dashboard look
* Netflix clone layout

---

## Animation states

Show or describe:

* Shelf card hover lift
* Open Carousel transition
* Close Carousel transition
* Carousel card depth rotation
* Shelf horizontal scroll
* View All grid transition
* Server scan progress animation
* AI Fix loading shimmer
* Metadata repair progress
* Broken link warning pulse

---

Final result: the Servers page should feel like a powerful media library control room with shelves, server health, genre shelves, metadata repair tools, View All grids, and a reusable open/close carousel mode that can later be used in Movies, TV, Music, and Games.
