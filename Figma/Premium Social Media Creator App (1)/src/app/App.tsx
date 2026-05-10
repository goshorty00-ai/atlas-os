import { RouterProvider } from "react-router";
import { router } from "./routes";
import { StudioProvider } from "./state/studioStore";

export default function App() {
  return (
    <StudioProvider>
      <RouterProvider router={router} />
    </StudioProvider>
  );
}