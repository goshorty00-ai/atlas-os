import { createHashRouter } from 'react-router';
import { Layout } from './components/Layout';
import { Home } from './pages/Home';
import { SmartDevices } from './pages/SmartDevices';
import { Integrations } from './pages/Integrations';
import { Media } from './pages/Media';
import { AI } from './pages/AI';
import { Security } from './pages/Security';
import { Settings } from './pages/Settings';

export const router = createHashRouter([
  {
    path: '/',
    Component: Layout,
    children: [
      { index: true, Component: Home },
      { path: 'smart-devices', Component: SmartDevices },
      { path: 'integrations', Component: Integrations },
      { path: 'media', Component: Media },
      { path: 'ai', Component: AI },
      { path: 'security', Component: Security },
      { path: 'settings', Component: Settings },
      { path: '*', Component: Home },
    ],
  },
]);