
  import { createRoot } from "react-dom/client";
  import App from "./app/App.tsx";
  import "./styles/index.css";

  try {
    console.log("[AddonNavTest] addon-frontend.mounted=true");
  } catch {}

  createRoot(document.getElementById("root")!).render(<App />);
  