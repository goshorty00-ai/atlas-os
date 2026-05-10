import { useEffect, useState } from "react";
import { TopNav } from "@/app/components/TopNav";
import { LeftSidebar } from "@/app/components/LeftSidebar";
import { ChatArea } from "@/app/components/ChatArea";
import { DigitalBrain } from "@/app/components/DigitalBrain";
import { AIScanner } from "@/app/components/AIScanner";
import { SocialMediaCreator } from "@/app/components/SocialMediaCreator";
import { CodeIDE } from "@/app/components/CodeIDE";
import { DJBooth } from "@/app/components/DJBooth";
import { MediaCentre } from "@/app/components/MediaCentre";
import { AIDownloadManager } from "@/app/components/AIDownloadManager";
import { SettingsPanel } from "@/app/components/SettingsPanel";
import { FloatingHUD } from "@/app/components/FloatingHUD";
import { DesignAnnotations } from "@/app/components/DesignAnnotations";
import { AnimatePresence } from "motion/react";
import { Info } from "lucide-react";

function useOnlineStatus() {
  const [online, setOnline] = useState(() => {
    try {
      return navigator.onLine;
    } catch {
      return true;
    }
  });

  useEffect(() => {
    const handleOnline = () => setOnline(true);
    const handleOffline = () => setOnline(false);
    window.addEventListener("online", handleOnline);
    window.addEventListener("offline", handleOffline);
    return () => {
      window.removeEventListener("online", handleOnline);
      window.removeEventListener("offline", handleOffline);
    };
  }, []);

  return online;
}

function OfflineFallback({ title, note }: { title: string; note: string }) {
  return (
    <div className="flex-1 flex items-center justify-center p-12">
      <div className="w-full max-w-4xl bg-slate-900/30 border border-cyan-500/20 rounded-2xl p-10 shadow-[0_0_60px_rgba(34,211,238,0.12)]">
        <div className="font-mono text-3xl text-cyan-400 tracking-wider font-bold">
          {title}
        </div>
        <div className="mt-4 font-mono text-slate-300 text-lg leading-relaxed">
          Offline mode detected. {note}
        </div>
        <div className="mt-6 font-mono text-slate-500 text-sm">
          Tip: If you’re on Wi‑Fi, check the router or Windows network status. Some features require an active internet connection.
        </div>
      </div>
    </div>
  );
}

function App() {
  const isOnline = useOnlineStatus();
  const mode = (() => {
    try {
      return new URLSearchParams(window.location.search).get("mode") || "";
    } catch {
      return "";
    }
  })();

  // Downloader-only mode: render ONLY the download section (no command center chrome).
  if (mode === "downloads" || mode === "downloader") {
    return (
      <div className="fixed inset-0 w-full h-full bg-[#0b0f14] text-slate-200 overflow-hidden">
        <AIDownloadManager />
      </div>
    );
  }

  const [activeTab, setActiveTab] = useState("AI Chat");
  const [showFloatingHUD, setShowFloatingHUD] = useState(false);
  const [showAnnotations, setShowAnnotations] = useState(false);
  const [showSidebar, setShowSidebar] = useState(true);

  return (
    <div className="fixed inset-0 w-full h-full bg-[#0b0f14] text-slate-200 overflow-hidden flex flex-col">
      {/* Top Navigation */}
      <TopNav />

      {/* Main Content Area */}
      <div className="flex-1 flex overflow-hidden min-h-0 relative isolate">
        {/* Left Sidebar */}
        <AnimatePresence>
          {showSidebar && (
            <LeftSidebar
              activeTab={activeTab}
              onTabChange={setActiveTab}
              onClose={() => setShowSidebar(false)}
            />
          )}
        </AnimatePresence>
        {/* Sidebar reopen strip */}
        {!showSidebar && (
          <button
            onClick={() => setShowSidebar(true)}
            className="w-8 shrink-0 border-r border-cyan-500/10 bg-[#0f1419] flex items-center justify-center hover:bg-cyan-500/10 transition-colors group cursor-pointer relative z-[5000] pointer-events-auto"
            title="Open sidebar"
          >
            <svg width="8" height="40" viewBox="0 0 8 40" className="text-cyan-500/40 group-hover:text-cyan-400 transition-colors">
              <path d="M4 4 L4 36" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
              <path d="M2 16 L4 12 L6 16" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
            </svg>
          </button>
        )}

        {/* Conditional Content Based on Active Tab */}
        {activeTab === "AI Security" ? (
          <>
            {/* AI Scanner - Full Width */}
            {isOnline ? (
              <AIScanner />
            ) : (
              <OfflineFallback
                title="AI SECURITY"
                note="Security scanning is unavailable without internet."
              />
            )}
          </>
        ) : activeTab === "AI Create" ? (
          <>
            {/* Social Media Creator - Full Width */}
            {isOnline ? (
              <SocialMediaCreator />
            ) : (
              <OfflineFallback
                title="AI CREATE"
                note="Social content generation is unavailable without internet."
              />
            )}
          </>
        ) : activeTab === "AI Code" ? (
          <>
            {/* Code IDE - Full Width */}
            <CodeIDE showSidebar={showSidebar} onReopenSidebar={() => setShowSidebar(true)} />
          </>
        ) : activeTab === "AI DJ Booth" ? (
          <>
            {/* DJ Booth - Full Width */}
            <DJBooth />
          </>
        ) : activeTab === "AI Media Centre" ? (
          <>
            {/* Media Centre - Full Width */}
            <MediaCentre />
          </>
        ) : activeTab === "AI Downloads" ? (
          <>
            {/* Download Manager - Full Width */}
            <AIDownloadManager />
          </>
        ) : activeTab === "AI Settings" ? (
          <>
            {/* Settings Panel - Full Width */}
            <SettingsPanel />
          </>
        ) : (
          <>
            {isOnline ? (
              <>
                {/* Chat Area */}
                <ChatArea />

                {/* Digital Brain Visualization */}
                <DigitalBrain />
              </>
            ) : (
              <OfflineFallback
                title="AI CHAT"
                note="Chat is unavailable without internet."
              />
            )}
          </>
        )}
      </div>

      {/* Floating HUD */}
      {showFloatingHUD && <FloatingHUD />}

      {/* Design Annotations Modal (Dev Only - Hidden in Production) */}
      <AnimatePresence>
        {showAnnotations && (
          <DesignAnnotations
            onClose={() => setShowAnnotations(false)}
          />
        )}
      </AnimatePresence>

      {/* Control Buttons (Dev Only - Hidden in Production) */}
      <div className="fixed bottom-4 right-4 flex flex-col gap-2 opacity-30 hover:opacity-100 transition-opacity">
        <button
          onClick={() => setShowFloatingHUD(!showFloatingHUD)}
          className="p-2 bg-slate-800/80 border border-cyan-500/30 rounded-lg hover:bg-slate-700/80 transition-all"
          title="Toggle Floating HUD"
        >
          <Info className="w-4 h-4 text-cyan-400" />
        </button>
      </div>
    </div>
  );
}

export default App;