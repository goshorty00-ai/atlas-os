Create a **Movie / TV Details page** for the AI Media Centre.

This page opens when the user clicks any movie or TV show card from Discovery, Servers, Movies, TV, Search, Carousel, or View All grid.

Do not redesign the whole app. Design only this details page and its states.

## Important naming rule

Do not use “Atlas AI” anywhere.

Use generic wording for now:

* AI Assistant
* Ask AI
* AI Summary
* AI Stream Selection
* AI Recommendations

The app name may change later, so avoid hardcoding Atlas.

---

# Page goal

The details page should feel like a premium streaming title page.

It must show:

* Movie/TV backdrop
* Poster
* Title
* Year
* Runtime / episode length
* Rating scores
* Genre chips
* Description
* Trailer button
* Play button
* Watchlist button
* Source/stream list
* Cast
* Similar titles
* AI assistant panel

It should look cinematic, compact, and polished.

---

# Layout

Use a full details page inside the module.

No huge app header.
No giant logo.
No footer.
No random admin panels.
No wasted blank space.

## Top cinematic section

Create a cinematic top section with:

* Full-width backdrop image
* Dark gradient overlay
* Poster on the left
* Main info on the right

Main info includes:

* Back button
* Type label: Movie or TV Show
* Title
* Year
* Runtime or episode length
* TMDb rating
* IMDb rating
* Rotten Tomatoes score if available
* User rating
* Genre chips
* Certification/age rating
* Quality badge if available: HD / 4K / HDR
* Short description

Buttons should be compact:

* Play
* Trailer
* Watchlist
* Seen / Watched
* Share

Do not make huge full-width buttons.

---

# AI assistant panel

Add a compact AI panel below the main info.

Title:

**Ask AI**

Text:

“Ask for a spoiler-free summary, best stream, cast info, parental guide, similar titles, or whether it is worth watching.”

Quick prompt chips:

* Spoiler-free summary
* Is it worth watching?
* Find best source
* Explain ending
* Parental guide
* Similar titles
* Cast highlights
* Trailer breakdown

Add a small input:

“Ask anything about this title…”

Add a small Send button.

No hardcoded app name.

---

# Source / stream selection section

Create a section called:

**Sources**

This is where the user chooses where to play the title from.

Source cards should be compact and stacked/listed.

Each source card must show:

* Source/add-on name
* Stream title
* Quality: 720p / 1080p / 4K / HDR
* File size if relevant
* Audio: stereo / 5.1 / Atmos
* Language
* Subtitles
* Seeders/health if torrent style
* Reliability score
* Status: Available / Link unavailable / Buffer risk / Premium
* Small Play button
* Small Copy Link button if relevant
* Small Details button

Example sources:

* Plex
* Jellyfin
* Netflix
* Prime Video
* Disney+
* Stremio Add-on
* Torrentio RD
* Local Library
* YouTube Trailer

Add a compact **AI Stream Selection** row above sources:

* Auto Pick Best Source toggle
* Prefer 4K toggle
* Prefer smallest file toggle
* Prefer subtitles toggle
* Avoid low health sources toggle

AI should highlight the recommended source with a small “Recommended” badge.

Do not make source cards huge.

---

# TV show-specific state

For TV shows, include an episode/season section.

Add:

* Season selector
* Episode list
* Continue Watching episode
* Latest episode
* Next episode
* Episode guide button
* AI recap button
* Spoiler-free recap button

Episode cards should show:

* Season/episode number
* Episode title
* Runtime
* Air date
* Progress
* Play button
* Recap button

For a movie, hide the episode section.

---

# Cast section

Add a compact cast row:

* Actor photo
* Actor name
* Character name

Horizontal scroll is okay.

Add a View Full Cast button.

---

# Related sections

Add compact shelves lower down:

* Similar Titles
* More Like This
* Same Director
* Same Cast
* From This Source
* Recommended by AI

Each shelf uses compact poster cards.

---

# Metadata / details section

Add a compact details panel, not a huge admin section.

Include:

* Director / creator
* Writers
* Release date
* Country
* Language
* Studio/network
* Runtime
* Budget/revenue if available
* External IDs if useful
* Source metadata provider

Keep it neat and small.

---

# Interaction states

Show states for:

* Loading title details
* Trailer modal open
* Source selected
* Source unavailable
* AI recommended source
* Added to Watchlist
* Marked as Watched
* Ask AI response state
* TV episode selected
* Back to previous page

---

# Trailer modal

When Trailer is clicked:

Open a compact cinematic trailer overlay.

Overlay includes:

* Trailer video player
* Close button
* Title
* Source: YouTube / TMDb / provider
* Add Reminder / Watchlist button

Do not navigate away.

---

# Visual style

Use:

* Dark cinematic backdrop
* Glass panels
* Cyan/violet accents
* Compact buttons
* Poster art
* Rating chips
* Premium streaming UI
* Thin neon borders
* Smooth hover states

Avoid:

* Giant blank areas
* Huge buttons
* Admin dashboard design
* File repair tools
* AI cleanup buttons
* Hardcoded Atlas name
* Basic white modal
* Footer
* Massive app logo

Final target: when a user clicks a movie or TV show, they land on a premium cinematic details page with backdrop, poster, metadata, trailer, sources, compact AI assistant, cast, similar titles, and TV episode handling where needed.
