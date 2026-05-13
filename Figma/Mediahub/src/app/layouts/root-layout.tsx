import { Outlet } from 'react-router';
import { Sidebar } from '../components/sidebar';

export function RootLayout() {
  return (
    <div className="size-full flex bg-gradient-to-br from-slate-950 via-slate-900 to-slate-950">
      <Sidebar />
      <div id="atlas-main-scroll" className="flex-1 overflow-y-auto">
        <div className="max-w-[1800px] mx-auto p-8">
          <Outlet />
        </div>
      </div>
    </div>
  );
}
