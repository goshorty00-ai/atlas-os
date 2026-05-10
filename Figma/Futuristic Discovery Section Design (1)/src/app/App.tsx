import { useState } from "react";
import { LivePulseTicker } from "./components/LivePulseTicker";
import { FilterBar } from "./components/FilterBar";
import { FeaturedSpotlight } from "./components/FeaturedSpotlight";
import { DiscoveryRow } from "./components/DiscoveryRow";
import { MediaCard } from "./components/MediaCard";
import { NewsCard } from "./components/NewsCard";
import { CelebrityCard } from "./components/CelebrityCard";
import { InfoPanel } from "./components/InfoPanel";
import { useDiscovery } from "./useDiscovery";

export default function App() {
  const { data, loading } = useDiscovery();
  const [isPanelOpen, setIsPanelOpen] = useState(false);
  const [panelMedia, setPanelMedia] = useState<any>(null);

  const handleMediaClick = (media: any) => {
    setPanelMedia({
      ...media,
      description: media.overview || "An epic journey through space and time, pushing the boundaries of what's possible.",
      genres: media.genres || ["Sci-Fi", "Action", "Adventure"],
      runtime: media.runtime || "2h 35m",
    });
    setIsPanelOpen(true);
  };

  if (loading || !data) {
    return (
      <div className="min-h-screen bg-slate-950 text-white flex items-center justify-center">
        <div className="text-cyan-400 text-xl">Loading Discovery...</div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-slate-950 text-white">
      {/* Sci-fi grid background */}
      <div
        className="fixed inset-0 opacity-5 pointer-events-none"
        style={{
          backgroundImage: `linear-gradient(rgba(0, 217, 255, 0.2) 1px, transparent 1px),
                            linear-gradient(90deg, rgba(0, 217, 255, 0.2) 1px, transparent 1px)`,
          backgroundSize: "40px 40px",
        }}
      />

      {/* Gradient overlays */}
      <div className="fixed inset-0 bg-gradient-to-b from-cyan-950/10 via-transparent to-slate-950/50 pointer-events-none" />

      {/* Content */}
      <div className="relative">
        {/* Live Pulse Ticker */}
        <LivePulseTicker />

        {/* Main content */}
        <div className="py-6 space-y-6">
          {/* Filter Bar */}
          <FilterBar />

          {/* Featured Spotlight */}
          {data.featured && (
            <FeaturedSpotlight
              onOpenInfo={() => handleMediaClick(data.featured)}
            />
          )}

          {/* Discovery Rows */}
          {data.trending.length > 0 && (
            <DiscoveryRow title="Trending Now">
              {data.trending.map((media, index) => (
                <div key={index} className="w-[200px] shrink-0">
                  <MediaCard
                    {...media}
                    onClick={() => handleMediaClick(media)}
                  />
                </div>
              ))}
            </DiscoveryRow>
          )}

          {data.trailers.length > 0 && (
            <DiscoveryRow title="Latest Trailers">
              {data.trailers.map((media, index) => (
                <div key={index} className="w-[280px] shrink-0">
                  <MediaCard
                    {...media}
                    onClick={() => handleMediaClick(media)}
                  />
                </div>
              ))}
            </DiscoveryRow>
          )}

          {data.news.length > 0 && (
            <DiscoveryRow title="Entertainment News">
              {data.news.map((news, index) => (
                <NewsCard
                  key={index}
                  {...news}
                  onClick={() => setIsPanelOpen(true)}
                />
              ))}
            </DiscoveryRow>
          )}

          {data.upcoming.length > 0 && (
            <DiscoveryRow title="Upcoming Releases">
              {data.upcoming.map((media, index) => (
                <div key={index} className="w-[200px] shrink-0">
                  <MediaCard
                    {...media}
                    onClick={() => handleMediaClick(media)}
                  />
                </div>
              ))}
            </DiscoveryRow>
          )}

          {data.celebrities.length > 0 && (
            <DiscoveryRow title="Celebrity Spotlight">
              {data.celebrities.map((celebrity, index) => (
                <CelebrityCard
                  key={index}
                  {...celebrity}
                  onClick={() => setIsPanelOpen(true)}
                />
              ))}
            </DiscoveryRow>
          )}

          {/* Media Grid */}
          {data.trending.length > 0 && (
            <div className="px-6 pt-10">
              <div className="flex items-center gap-3 mb-6">
                <div className="w-1 h-6 bg-gradient-to-b from-cyan-400 to-blue-500 rounded-full shadow-[0_0_10px_rgba(0,217,255,0.5)]" />
                <h2 className="text-xl font-semibold text-white">
                  Discover More
                </h2>
              </div>

              <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5 2xl:grid-cols-6 gap-5">
                {data.trending.slice(0, 12).map((media, index) => (
                  <MediaCard
                    key={index}
                    {...media}
                    onClick={() => handleMediaClick(media)}
                  />
                ))}
              </div>
            </div>
          )}
        </div>
      </div>

      {/* Info Panel */}
      <InfoPanel
        isOpen={isPanelOpen}
        onClose={() => setIsPanelOpen(false)}
        media={panelMedia}
      />
    </div>
  );
}
