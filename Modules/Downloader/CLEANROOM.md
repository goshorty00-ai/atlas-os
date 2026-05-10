This downloader module is a clean-room rewrite.

- No code was copied from JDownloader or other third-party downloader projects.
- The WebView2 UI is custom HTML/CSS/JS located in `Modules/DownloaderWeb/`.
- The native download engine is implemented in `AtlasAI.Modules.Downloader` with:
  - async-only download pipeline
  - resolver pipeline (direct HTTP + provider resolvers)
  - DPAPI-protected token storage
  - JSON persistence

