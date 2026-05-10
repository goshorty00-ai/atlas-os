import { useState } from "react";
import { GridView } from "./grid-view";
import { CarouselView } from "./carousel-view";
import { ListView } from "./list-view";
import { AppExpansionPanel } from "./app-expansion-panel";
import { FullscreenPlayer } from "./fullscreen-player";
import { AISearchBar } from "./ai-search-bar";
import { Grid3x3, List, Layers } from "lucide-react";
import {
  streamingApps,
  type StreamingApp,
} from "./streaming-data";

export type ViewMode = "grid" | "carousel" | "list";

export function StreamingLibrary() {
  const [viewMode, setViewMode] = useState<ViewMode>("grid");
  const [selectedApp, setSelectedApp] =
    useState<StreamingApp | null>(null);
  const [isPlayerOpen, setIsPlayerOpen] = useState(false);
  const [embeddedApp, setEmbeddedApp] =
    useState<StreamingApp | null>(null);

  const handleAppSelect = (app: StreamingApp) => {
    // Embed the website in an iframe
    setEmbeddedApp(app);
  };

  const handleCloseEmbed = () => {
    setEmbeddedApp(null);
  };

  const handleAppExpand = (app: StreamingApp) => {
    setSelectedApp(app);
  };

  const handleAppClose = () => {
    setSelectedApp(null);
  };

  const handleLaunchPlayer = () => {
    setIsPlayerOpen(true);
  };

  const handleClosePlayer = () => {
    setIsPlayerOpen(false);
  };

  return (
    <div className="relative w-full min-h-screen">
      {/* Fixed Search Bar */}
      <div className="fixed top-0 left-0 right-0 z-40 bg-[#0a0a0f]/95 backdrop-blur-md border-b border-white/10 px-6 py-4">
        <AISearchBar
          onSearch={(query) => console.log("Search:", query)}
        />

        {/* View Mode Controls */}
        <div className="flex items-center gap-2 mt-4">
          <button
            onClick={() => setViewMode("grid")}
            className={`px-4 py-2 rounded-lg backdrop-blur-md transition-all duration-300 ${
              viewMode === "grid"
                ? "bg-cyan-500/20 border border-cyan-500/50 text-cyan-400 shadow-[0_0_20px_rgba(6,182,212,0.3)]"
                : "bg-white/5 border border-white/10 text-gray-400 hover:bg-white/10"
            }`}
          >
            <Grid3x3 className="w-5 h-5" />
          </button>
          <button
            onClick={() => setViewMode("carousel")}
            className={`px-4 py-2 rounded-lg backdrop-blur-md transition-all duration-300 ${
              viewMode === "carousel"
                ? "bg-cyan-500/20 border border-cyan-500/50 text-cyan-400 shadow-[0_0_20px_rgba(6,182,212,0.3)]"
                : "bg-white/5 border border-white/10 text-gray-400 hover:bg-white/10"
            }`}
          >
            <Layers className="w-5 h-5" />
          </button>
          <button
            onClick={() => setViewMode("list")}
            className={`px-4 py-2 rounded-lg backdrop-blur-md transition-all duration-300 ${
              viewMode === "list"
                ? "bg-cyan-500/20 border border-cyan-500/50 text-cyan-400 shadow-[0_0_20px_rgba(6,182,212,0.3)]"
                : "bg-white/5 border border-white/10 text-gray-400 hover:bg-white/10"
            }`}
          >
            <List className="w-5 h-5" />
          </button>
        </div>
      </div>

      {/* Scrollable Content Area - with top padding to account for fixed header */}
      <div className="pt-40 px-6">
        {embeddedApp ? (
          // Show embedded iframe
          <div className="relative w-full h-[calc(100vh-12rem)]">
            <div className="flex items-center justify-between mb-4">
              <div className="flex items-center gap-3">
                <div
                  className="w-12 h-12 rounded-lg overflow-hidden"
                  style={{
                    background: `linear-gradient(135deg, ${embeddedApp.color}40 0%, ${embeddedApp.color}20 100%)`,
                  }}
                >
                  <div className="w-full h-full flex items-center justify-center p-2">
                    <img
                      src={embeddedApp.logoUrl}
                      alt={embeddedApp.name}
                      className="w-full h-full object-contain"
                    />
                  </div>
                </div>
                <div>
                  <div className="text-white font-medium">
                    {embeddedApp.name}
                  </div>
                  <div className="text-sm text-gray-400">
                    {embeddedApp.websiteUrl}
                  </div>
                </div>
              </div>
              <button
                onClick={handleCloseEmbed}
                className="px-4 py-2 rounded-lg backdrop-blur-md bg-white/10 border border-white/20 text-white hover:bg-white/20 transition-all"
              >
                Close
              </button>
            </div>
            <div className="w-full h-full rounded-2xl overflow-hidden border border-white/10 shadow-[0_0_60px_rgba(0,0,0,0.5)]">
              <iframe
                src={embeddedApp.websiteUrl}
                className="w-full h-full"
                title={embeddedApp.name}
                sandbox="allow-same-origin allow-scripts allow-popups allow-forms"
              />
            </div>
          </div>
        ) : (
          // Show app grid/carousel/list
          <>
            {viewMode === "grid" && (
              <GridView
                apps={streamingApps}
                onAppSelect={handleAppSelect}
              />
            )}
            {viewMode === "carousel" && (
              <CarouselView
                apps={streamingApps}
                onAppSelect={handleAppSelect}
              />
            )}
            {viewMode === "list" && (
              <ListView
                apps={streamingApps}
                onAppSelect={handleAppSelect}
              />
            )}
          </>
        )}
      </div>

      {/* App Expansion Panel */}
      {selectedApp && (
        <AppExpansionPanel
          app={selectedApp}
          onClose={handleAppClose}
          onLaunch={handleLaunchPlayer}
        />
      )}

      {/* Fullscreen Player */}
      {isPlayerOpen && (
        <FullscreenPlayer onClose={handleClosePlayer} />
      )}
    </div>
  );
}