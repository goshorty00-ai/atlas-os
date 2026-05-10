import { createHashRouter } from 'react-router';
import { RootLayout } from './layouts/root-layout';
import { DiscoveryHub } from './pages/discovery-hub';
import { ServersPage } from './pages/servers-page';
import { MoviesPage } from './pages/movies-page';
import { TVPage } from './pages/tv-page';
import { GamesPage } from './pages/games-page';
import { MusicPage } from './pages/music-page';
import { KaraokePage } from './pages/karaoke-page';
import { ShelfCreatorPage } from './pages/shelf-creator-page';
import { AIChatPage } from './pages/ai-chat-page';
import { SettingsPage } from './pages/settings-page';
import { AppsPage } from './pages/apps-page';
import { DetailsPage } from './pages/details-page';

export const router = createHashRouter([
  {
    path: '/',
    Component: RootLayout,
    children: [
      { index: true, Component: DiscoveryHub },
      { path: 'servers', Component: ServersPage },
      { path: 'movies', Component: MoviesPage },
      { path: 'tv', Component: TVPage },
      { path: 'music', Component: MusicPage },
      { path: 'games', Component: GamesPage },
      { path: 'apps', Component: AppsPage },
      { path: 'karaoke', Component: KaraokePage },
      { path: 'shelf-creator', Component: ShelfCreatorPage },
      { path: 'chat', Component: AIChatPage },
      { path: 'settings', Component: SettingsPage },
      { path: 'details/:id', Component: DetailsPage },
    ],
  },
]);
