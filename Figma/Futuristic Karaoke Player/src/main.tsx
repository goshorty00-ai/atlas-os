
  import { createRoot } from "react-dom/client";
  import App from "./app/App.tsx";
  import "./styles/index.css";

  try { document.documentElement.classList.add("dark"); } catch {
  }
  createRoot(document.getElementById("root")!).render(<App />);
  
