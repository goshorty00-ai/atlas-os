
import { createRoot } from "react-dom/client";
import CodeIDE from "./app/components/CodeIDE.tsx";
import { AIDownloadManager } from "./app/components/AIDownloadManager.tsx";
import { DigitalBrain } from "./app/components/DigitalBrain.tsx";
import { MusicHostView } from "./app/components/MusicHostView.tsx";
import "./styles/index.css";

// Read ?mode= from URL so WPF can control starting mode
const params = new URLSearchParams(window.location.search);
const mode = (params.get("mode") || "").toLowerCase();

const root = createRoot(document.getElementById("root")!);

// Downloader-only mode: render ONLY the downloader section (no shell)
if (mode === "downloads" || mode === "downloader") {
  root.render(
    <div className="fixed inset-0 w-full h-full overflow-hidden">
      <AIDownloadManager />
    </div>
  );
} else if (mode === "music") {
  root.render(
    <div className="fixed inset-0 w-full h-full overflow-hidden">
      <MusicHostView />
    </div>
  );
} else if (mode === "orbs" || mode === "orb") {
  // Orbs overlay mode is intended to sit on top of WPF UI; keep the web surface fully transparent.
  try {
    const html = document.documentElement;
    const body = document.body;
    const rootEl = document.getElementById("root");
    html.style.background = "transparent";
    body.style.background = "transparent";
    if (rootEl) rootEl.style.background = "transparent";
  } catch {
  }

  root.render(
    <div className="fixed inset-0 w-full h-full overflow-hidden">
      <DigitalBrain />
    </div>
  );
} else {
  const initialMode = mode === "ide" ? "ide" : "autonomous";
  root.render(<CodeIDE initialMode={initialMode as "autonomous" | "ide"} />);
}
  