Fix the current AI Media Centre interaction flow.

Do not redesign the whole app. Do not add more random panels. Fix the page behaviour.

## Current problem

The design added a big “Streaming sources & add-ons” info box at the top of the Servers page. I do **not** want that there.

Also, clicking a movie or TV card still does not open a proper details page.

## Remove this

Remove the large top info box that says:

“Streaming sources & add-ons”
“Browse and play across every connected source…”
“Manage Sources”

Do not replace it with another big explanation box.

The Servers / Sources page should start with:

1. compact source pills row
2. compact filters
3. featured media panel if needed
4. shelves of media cards

No big instructional banner.

---

# Main fix: clicking cards must open Details page

Every movie and TV card must be wired to open a dedicated **Movie / TV Details page**.

This must work from:

* Discovery page cards
* Servers / Sources page cards
* Movies page cards
* TV page cards
* Search results
* View All grid cards
* Carousel selected item
* Featured panel buttons
* Similar title cards

## Click behaviour

When user clicks the poster/card/title:

Open:

**Title Details Page**

Do not just highlight the card.
Do not open a tiny modal.
Do not stay on the same shelf.
Do not scroll down.
Do not show only source list.
Do not open the carousel.

It must navigate to a full details page inside the module.

There should be a clear back button to return to the previous page.

---

# Details page requirements

Create a proper details page for movies and TV shows.

## Top cinematic title section

Show:

* large backdrop image
* dark gradient overlay
* poster on the left
* title
* year
* runtime / episode length
* type: Movie or TV Show
* rating chips:

  * TMDb
  * IMDb
  * Rotten Tomatoes if available
  * user rating if available
* genre chips
* age rating
* source/platform badge
* quality badge if available
* short description

Buttons must be compact:

* Play
* Trailer
* Watchlist
* Details / More Info
* Mark Watched

Do not use huge full-width buttons.

## Trailer action

Clicking **Trailer** opens a trailer overlay/player.

Trailer overlay must show:

* video area
* title
* close button
* source badge: YouTube / TMDb / provider
* small buttons: Watchlist, Add Reminder

## Sources section

Below the cinematic section, show **Sources**.

This is essential.

Sources should list available places/streams for this title.

Examples:

* Plex
* Jellyfin
* Netflix
* Prime Video
* Disney+
* Stremio Add-on
* Torrentio RD
* Local Library
* YouTube Trailer

Each source card/list row should show:

* source name
* stream title
* quality: 720p / 1080p / 4K / HDR
* audio: stereo / 5.1 / Atmos
* language
* subtitles
* file size if relevant
* reliability / health
* status: Available / Link unavailable / Buffer risk / Premium
* small Play button
* small Copy Link button
* small Source Details button

Add a compact recommended source badge:

* Recommended
* Best Quality
* Fastest
* Subtitles Available

Do not create huge source cards.

## Ask AI panel

Add compact panel called:

**Ask AI**

Not “Atlas AI”.

Text:

“Ask for a spoiler-free summary, best source, cast info, parental guide, similar titles, or whether it is worth watching.”

Quick chips:

* Spoiler-free summary
* Is it worth watching?
* Find best source
* Parental guide
* Similar titles
* Cast highlights
* Trailer breakdown

Small input:

“Ask anything about this title…”

Small send button.

## TV-specific section

If the item is a TV show, show:

* Season selector
* Episode list
* Continue watching
* Latest episode
* Next episode
* Episode guide
* AI recap
* Spoiler-free recap

Each episode row/card:

* season/episode number
* episode title
* runtime
* air date
* progress
* small Play button
* small Recap button

If the item is a movie, hide TV episode sections.

## Cast and related

Add lower compact sections:

* Cast row
* Similar Titles
* More Like This
* Same Director
* Same Cast
* From This Source

---

# Featured panel behaviour

The big cinematic featured card, like the Avatar example, is fine visually.

But it must behave properly:

* Clicking the title/poster/backdrop opens the Details page.
* Clicking Details opens the Details page.
* Clicking Trailer opens trailer overlay.
* Clicking Play opens source selection or starts recommended source.
* Clicking Watchlist toggles watchlist state.

Do not let buttons do nothing.

---

# Card interaction states

Show clear design states:

* card hover
* card selected
* navigating to details
* details page loaded
* back button return
* trailer overlay open
* source selected
* source unavailable
* added to watchlist
* Ask AI response loading

---

# Routing / prototype requirement

In Figma prototype interactions, wire:

Movie card click → Movie Details Page
TV card click → TV Details Page
Featured card click → Details Page
Details button → Details Page
Trailer button → Trailer Overlay
Back button → Previous page
Source Play button → selected/playing state
Watchlist button → active watchlist state
Ask AI send button → AI response state

Do not leave controls decorative.

---

# Do not add

Do not add:

* the large “Streaming sources & add-ons” info box
* huge admin panels
* AI Cleanup
* AI Fix
* server storage dashboard
* big instructional banners
* massive buttons
* hardcoded Atlas AI name
* random unrelated pages

Final target: clicking any movie or TV item opens a proper cinematic Movie/TV Details page with backdrop, ratings, trailer, sources, Ask AI, cast, related titles, and TV episode handling.
