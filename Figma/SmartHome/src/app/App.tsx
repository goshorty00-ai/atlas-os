import { RouterProvider } from "react-router";
import { router } from "./routes";
import { SmartHomeProvider } from "./SmartHomeContext";

export default function App() {
  return (
    <SmartHomeProvider>
      <RouterProvider router={router} />
    </SmartHomeProvider>
  );
}