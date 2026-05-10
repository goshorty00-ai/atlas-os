import { createHashRouter } from "react-router";
import { Layout } from "./components/Layout";
import { Dashboard } from "./pages/Dashboard";
import { CreateStudio } from "./pages/CreateStudio";
import { VideoStudio } from "./pages/VideoStudio";
import { Templates } from "./pages/Templates";
import { MediaLibrary } from "./pages/MediaLibrary";
import { AICreator } from "./pages/AICreator";
import { VoiceStudio } from "./pages/VoiceStudio";
import { BrandKit } from "./pages/BrandKit";
import { ContentPlanner } from "./pages/ContentPlanner";
import { Accounts } from "./pages/Accounts";
import { Analytics } from "./pages/Analytics";
import { ExportPublish } from "./pages/ExportPublish";
import { Settings } from "./pages/Settings";

export const router = createHashRouter([
  {
    path: "/",
    Component: Layout,
    children: [
      { index: true, Component: Dashboard },
      { path: "create", Component: CreateStudio },
      { path: "video-studio", Component: VideoStudio },
      { path: "templates", Component: Templates },
      { path: "media", Component: MediaLibrary },
      { path: "ai-creator", Component: AICreator },
      { path: "voice-studio", Component: VoiceStudio },
      { path: "brand-kit", Component: BrandKit },
      { path: "planner", Component: ContentPlanner },
      { path: "accounts", Component: Accounts },
      { path: "analytics", Component: Analytics },
      { path: "export", Component: ExportPublish },
      { path: "settings", Component: Settings },
    ],
  },
]);
