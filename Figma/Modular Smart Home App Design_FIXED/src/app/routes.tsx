import { createBrowserRouter } from "react-router-dom";
import { Layout } from "./components/Layout";
import { Overview } from "./pages/Overview";
import { Devices } from "./pages/Devices";
import { DeviceSetup } from "./pages/DeviceSetup";
import { Rooms } from "./pages/Rooms";
import { Cameras } from "./pages/Cameras";
import { Security } from "./pages/Security";
import { AIScenes } from "./pages/AIScenes";
import { Automations } from "./pages/Automations";
import { CustomCommands } from "./pages/CustomCommands";
import { Alerts } from "./pages/Alerts";
import { ClimateEnergy } from "./pages/ClimateEnergy";
import { Access } from "./pages/Access";
import { AIAssistant } from "./pages/AIAssistant";

export const router = createBrowserRouter([
  {
    path: "/",
    Component: Layout,
    children: [
      { index: true, Component: Overview },
      { path: "devices", Component: Devices },
      { path: "device-setup", Component: DeviceSetup },
      { path: "rooms", Component: Rooms },
      { path: "cameras", Component: Cameras },
      { path: "security", Component: Security },
      { path: "ai-scenes", Component: AIScenes },
      { path: "automations", Component: Automations },
      { path: "custom-commands", Component: CustomCommands },
      { path: "alerts", Component: Alerts },
      { path: "climate-energy", Component: ClimateEnergy },
      { path: "access", Component: Access },
      { path: "ai-assistant", Component: AIAssistant },
    ],
  },
]);
