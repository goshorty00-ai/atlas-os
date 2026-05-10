Fix the current AI Media Centre design. Do **not** redesign randomly. Correct the existing UI structure and missing pages.

The current version has these problems:

* AI DJ is still in the sidebar — remove it completely.
* Downloads is still in the sidebar — remove it completely.
* Servers page does not work or show useful server shelves.
* TV page does not work or show useful TV content.
* Music page does not contain proper music visualizers.
* Home is still just a Continue Watching page — it must become a Discovery page.
* The 3D carousel needs a working open/close control.
* The carousel system must work across Movies, TV, and Music where relevant.
* Do not create big headers, big banners, big logos, or huge hero sections.

## Sidebar correction

Replace the sidebar with only these items:

1. Home / Discovery
2. Servers
3. Movies
4. TV
5. Music
6. Games
7. AI Karaoke
8. Shelf Creator
9. AI Chat
10. Settings

Remove completely:

* AI DJ
* Downloads

The sidebar must be compact, collapsible, and functional-looking. It should have a clear collapse button and active state. No wasted space.

## Home page must become Discovery

Change the Home page into **Discovery Hub**, not Continue Watching.

It must show:

* Latest movie trailers
* Latest TV trailers
* Latest game trailers
* Upcoming movies
* Upcoming TV shows
* Upcoming games
* Streaming this week
* Cinema releases
* Celebrity news
* Entertainment news
* Trending actors/directors
* Hot or Not AI verdicts

At the top, use a compact AI discovery/search bar with chips:

* New Trailers
* Coming Soon
* Hot Right Now
* Not Worth It
* Movies
* TV
* Games
* Celebrity News
* Streaming
* Cinema
* AI Picks

Add a **Latest Trailers 3D Carousel** directly under the chips.

Each trailer card must have:

* Thumbnail/poster
* Play Trailer button
* Type badge: Movie / TV / Game
* Release date
* Platform badge
* Heat score
* AI verdict: Hot / Wait / Skip / Must Watch
* Add Reminder button
* Add to Shelf button

Below that, add compact shelves:

* Coming Soon Movies
* Coming Soon TV
* Coming Soon Games
* Streaming This Week
* Hot or Not
* Celebrity News

Do not show Continue Watching as the main Home content. If included, it should be small and lower down.

## Carousel must have open/close button

Create a reusable **3D Carousel component** with a visible open/close toggle.

The carousel button should appear on shelves in:

* Movies
* TV
* Music
* Servers
* Discovery/Home

Button states:

* “Open Carousel”
* “Close Carousel”

When closed:

* Show normal compact shelf cards.

When open:

* Expand into a 3D coverflow carousel overlay or panel.
* Centre item is large and sharp.
* Side items are angled, smaller, and slightly blurred.
* Include left/right arrows.
* Include mouse-wheel hint.
* Include selected item info rail.
* Include Play / Trailer / Add / Details buttons.
* Include Escape-to-close hint.

The open/close button must look functional, not decorative.

## Servers page must be rebuilt

Create a working **Servers** page.

Top of Servers page:

* Latest From Servers 3D carousel
* Open Carousel / Close Carousel button
* Shows latest movies and TV episodes found on connected servers

Server carousel cards must show:

* Poster
* Title
* Type: Movie / TV
* Server source
* File status
* Metadata status
* Play button
* Trailer button
* Fix Metadata button

Below it create server shelves:

* Recently Added Movies
* Recently Added TV
* Continue From Server
* Missing Artwork
* Broken Links
* Duplicates Found
* 4K / HDR Library
* Unwatched Gems
* AI Cleanup Suggestions

Add server health cards:

* Plex
* Jellyfin
* Local Folders
* NAS
* External Drives

Each health card must show:

* Online/offline
* Storage used
* Library count
* Last scan
* Broken files
* Missing artwork
* Duplicate count
* Rescan button
* AI Fix button

## TV page must be rebuilt

Create a working **TV Command Centre** page.

It must include:

### TV carousel

At the top, add **Latest TV Trailers** with:

* Open Carousel / Close Carousel button
* 3D carousel state
* Normal shelf state

### Continue Watching Episodes

Episode cards must show:

* Show title
* Season and episode
* Episode name
* Progress bar
* Resume button
* Next Episode button
* AI Recap button

### Episode Tracker

Create a panel showing:

* New episodes this week
* Upcoming episodes
* Missed episodes
* Season progress
* Finale alerts
* Renewed / Cancelled status

### TV shelves

Add shelves:

* Trending Shows
* New TV Trailers
* Upcoming Series
* Binge Worthy
* One Season Wonders
* Cancelled Too Soon
* Shows Like You Watch
* AI Recommended Next Series

### Smart TV tools

Add compact buttons/cards:

* AI Episode Recap
* Spoiler-Free Summary
* Skip Filler Episodes
* Find Best Episode Order
* Where to Watch
* Build Binge Shelf
* Notify New Episode

TV cannot be blank or just copied from movies.

## Music page must have visualizers

Create a proper **Music** page with visualizers.

At the top, add **Music Galaxy Explorer**.

This should be a contained futuristic visualizer panel with:

* Glowing music nodes
* Lines connecting artists, genres, moods, albums, BPM, and decades
* Genre cluster legend
* Mouse rotate hint
* Scroll zoom hint
* Click node to play
* AI “Find Similar Vibe” button

Add a **Now Playing visualizer bar**:

* Album art
* Track title
* Artist
* Spectrum bars
* Waveform timeline
* Play / pause
* Skip
* Shuffle
* Repeat
* Volume
* Lyrics
* Queue

Add visualizer modes:

* Galaxy
* Spectrum
* Waveform
* Circular Pulse
* Lyrics Sync
* Mood Map

Add music shelves:

* Recently Played
* AI Mood Mixes
* Albums
* Artists
* Soundtracks
* Karaoke Ready Songs
* High Energy
* Chill
* Old Favourites

Add AI music tools:

* AI Playlist Generator
* Mood Mix
* Coding Flow
* Gym Mix
* Late Night Mix
* Find Similar Songs
* Fix Metadata
* Find Missing Album Art

## Movies page carousel

Movies page needs:

* Normal shelves by default
* Open Carousel / Close Carousel button on each major shelf
* 3D carousel for All Movies, Latest Movies, Hidden Gems, 4K/HDR, AI Picks
* Trailer button on cards
* AI verdict labels
* No giant hero banner

## Games page

Keep Games as its own launcher/discovery page.

Add:

* Latest Game Trailers with carousel open/close
* Recently Played
* Installed Games
* Coming Soon Games
* Steam / Game Pass / PS Plus Highlights
* Co-op Games
* Controller Ready
* Short Session Games
* Long Campaigns
* AI Recommended Games

Game cards must show:

* Platform
* Install/play status
* Last played
* Play button
* Trailer button
* AI reason
* Performance suggestion

## AI Karaoke must look working

Create a proper working-looking karaoke page.

Include:

* Current song
* Synced lyrics
* Highlighted active lyric line
* Next lyric preview
* Mic input meter
* Vocal level meter
* Pitch guide
* Score meter
* Play/pause
* Restart verse
* Key change
* Tempo change
* Vocal removal toggle
* Duet mode
* Score mode
* Echo/reverb
* Mic input selection
* Lyrics sync correction
* Party queue
* Singer names
* Estimated wait time

Add AI buttons:

* Generate Karaoke Night
* Pick Songs For My Voice
* Create Duet List
* Warm-Up Songs
* Find Songs Everyone Knows
* AI Vocal Coach
* Fix Lyrics Timing
* Remove Vocals

## Final design rules

Do not add back AI DJ.
Do not add back Downloads.
Do not make Home a Continue Watching page.
Do not make TV blank.
Do not make Servers blank.
Do not make Music only shelves.
Do not create fake non-working controls.
Do not use huge banners or giant page titles.
Do not use a large logo.
Do not create a footer.
Do not use cartoon mascots.
Do not waste space.

The final result should feel like a compact premium futuristic AI-powered entertainment discovery centre with working pages for Discovery, Servers, Movies, TV, Music, Games, Karaoke, Shelf Creator, AI Chat, and Settings.
