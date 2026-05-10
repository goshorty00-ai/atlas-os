import { useEffect, useMemo, useState } from 'react';
import { ExternalLink, Settings, Save } from 'lucide-react';
import { postToHost } from '../atlasBridge';

type ApiIntegration = {
  id: string;
  name: string;
  status: 'online' | 'warning' | 'offline' | 'unknown';
  configured?: boolean;
};

type CatalogItem = {
  id: string;
  name: string;
  siteUrl: string;
  setupId?: string; // sent to api.openSettings
  fields?: Array<{ key: string; label: string; placeholder: string }>; // sent to api.setIntegrationKeys
};

interface IntegrationsCatalogProps {
  integrations: ApiIntegration[];
  focusId?: string | null;
  onFocusConsumed?: () => void;
}

export function IntegrationsCatalog({ integrations, focusId, onFocusConsumed }: IntegrationsCatalogProps) {
  const configured = useMemo(() => {
    const set = new Set<string>();
    (integrations ?? []).forEach((x) => {
      if (!x?.id) return;
      if (x.configured) set.add(x.id);
    });
    return set;
  }, [integrations]);

  const items: CatalogItem[] = useMemo(
    () => [
      {
        id: 'tmdb',
        name: 'TMDB',
        siteUrl: 'https://www.themoviedb.org/',
        setupId: 'tmdb',
        fields: [{ key: 'tmdb', label: 'TMDB API Key', placeholder: 'Paste your TMDB API key…' }],
      },
      {
        id: 'trakt',
        name: 'Trakt',
        siteUrl: 'https://trakt.tv/',
        setupId: 'trakt',
        fields: [
          { key: 'trakt_client_id', label: 'Client ID', placeholder: 'Trakt client id…' },
          { key: 'trakt_client_secret', label: 'Client Secret', placeholder: 'Trakt client secret…' },
          { key: 'trakt_token', label: 'Access Token', placeholder: 'Bearer token…' },
        ],
      },
      {
        id: 'spotify',
        name: 'Spotify',
        siteUrl: 'https://developer.spotify.com/',
        setupId: 'spotify',
        fields: [
          { key: 'spotify_client_id', label: 'Client ID', placeholder: 'Spotify client id…' },
          { key: 'spotify_client_secret', label: 'Client Secret', placeholder: 'Spotify client secret…' },
        ],
      },
      {
        id: 'musicbrainz',
        name: 'MusicBrainz',
        siteUrl: 'https://musicbrainz.org/',
        setupId: 'musicbrainz',
        fields: [{ key: 'musicbrainz_contact', label: 'Contact / User-Agent Suffix', placeholder: 'email@example.com or project contact…' }],
      },
      {
        id: 'lastfm',
        name: 'Last.fm',
        siteUrl: 'https://www.last.fm/api',
        setupId: 'lastfm',
        fields: [{ key: 'lastfm_key', label: 'API Key', placeholder: 'Paste your Last.fm API key…' }],
      },
      {
        id: 'discogs',
        name: 'Discogs',
        siteUrl: 'https://www.discogs.com/settings/developers',
        setupId: 'discogs',
        fields: [{ key: 'discogs_token', label: 'Personal Access Token', placeholder: 'Paste your Discogs token…' }],
      },
      {
        id: 'soundcloud',
        name: 'SoundCloud',
        siteUrl: 'https://developers.soundcloud.com/',
        setupId: 'soundcloud',
        fields: [{ key: 'soundcloud_client_id', label: 'Client ID', placeholder: 'Paste your SoundCloud client id…' }],
      },
      {
        id: 'fanarttv',
        name: 'fanart.tv',
        siteUrl: 'https://fanart.tv/get-an-api-key/',
        setupId: 'fanarttv',
        fields: [{ key: 'fanarttv_key', label: 'API Key', placeholder: 'Paste your fanart.tv key…' }],
      },
      {
        id: 'igdb',
        name: 'IGDB',
        siteUrl: 'https://api-docs.igdb.com/',
        setupId: 'igdb',
        fields: [
          { key: 'igdb_client_id', label: 'Client ID', placeholder: 'IGDB client id…' },
          { key: 'igdb_client_secret', label: 'Client Secret', placeholder: 'IGDB client secret…' },
        ],
      },
      {
        id: 'rpdb',
        name: 'RPDB',
        siteUrl: 'https://rpdb.me/',
        setupId: 'rpdb',
        fields: [{ key: 'rpdb', label: 'RPDB API Key', placeholder: 'Paste your RPDB key…' }],
      },
      {
        id: 'realdebrid',
        name: 'Real-Debrid',
        siteUrl: 'https://real-debrid.com/',
        setupId: 'cloud_provider',
        fields: [{ key: 'realdebrid', label: 'API Token', placeholder: 'Paste your Real-Debrid token…' }],
      },
      {
        id: 'addon_servers',
        name: 'Addon Servers',
        siteUrl: 'https://github.com/Stremio/stremio-addon-sdk/tree/master/docs/api',
        setupId: 'addon_servers',
      },
      {
        id: 'elevenlabs',
        name: 'ElevenLabs',
        siteUrl: 'https://elevenlabs.io/',
        setupId: 'elevenlabs',
      },
      // Popular ecosystem links (no built-in key flow yet)
      { id: 'plex', name: 'Plex', siteUrl: 'https://www.plex.tv/' },
      { id: 'jellyfin', name: 'Jellyfin', siteUrl: 'https://jellyfin.org/' },
      { id: 'emby', name: 'Emby', siteUrl: 'https://emby.media/' },
      { id: 'sonarr', name: 'Sonarr', siteUrl: 'https://sonarr.tv/' },
      { id: 'radarr', name: 'Radarr', siteUrl: 'https://radarr.video/' },
      { id: 'prowlarr', name: 'Prowlarr', siteUrl: 'https://prowlarr.com/' },
      { id: 'overseerr', name: 'Overseerr', siteUrl: 'https://overseerr.dev/' },
    ],
    []
  );

  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [drafts, setDrafts] = useState<Record<string, Record<string, string>>>({});

  // When the user clicks "Configure" on a card, jump to the matching setup here.
  // Only applies to items that actually have fields.
  useEffect(() => {
    if (!focusId) return;
    setExpandedId(focusId);
    queueMicrotask(() => {
      try {
        const el = document.getElementById(`catalog-${focusId}`);
        if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' });
      } catch {
        // ignore
      }
      try {
        onFocusConsumed?.();
      } catch {
        // ignore
      }
    });
  }, [focusId, onFocusConsumed]);

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h2 className="text-xl font-light text-gray-300">Popular Integrations</h2>
        <div className="text-sm text-gray-500">Quick setup + official links</div>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        {items.map((item) => {
          const isExpanded = expandedId === item.id;
          const isConfigured = configured.has(item.id);
          const hasFields = (item.fields?.length ?? 0) > 0;
          const itemDrafts = drafts[item.id] ?? {};

          // Once configured, keep it out of the "Popular Integrations" list.
          // Active cards already show configured integrations.
          if (hasFields && isConfigured) return null;

          return (
            <div
              key={item.id}
              id={`catalog-${item.id}`}
              className="relative rounded-xl border border-blue-500/20 bg-gradient-to-br from-blue-500/5 to-violet-500/5 backdrop-blur-xl p-5"
              style={{ boxShadow: '0 8px 32px 0 rgba(31, 38, 135, 0.15)' }}
            >
              <div className="absolute inset-0 rounded-xl bg-gradient-to-br from-white/5 to-white/0 pointer-events-none" />

              <div className="relative space-y-3">
                <div className="flex items-start justify-between">
                  <div className="space-y-1">
                    <div className="text-lg font-medium text-gray-200">{item.name}</div>
                    <div className="text-xs text-gray-500">
                      {isConfigured ? 'Configured' : 'Not configured'}
                    </div>
                  </div>

                  <div className="flex items-center gap-3">
                    {item.setupId && (
                      <button
                        className="text-gray-400 hover:text-gray-200 transition-colors"
                        title="Open setup"
                        onClick={() => {
                          if ((item.fields?.length ?? 0) > 0) {
                            setExpandedId((cur) => (cur === item.id ? null : item.id));
                            return;
                          }
                          postToHost('api.openSettings', { id: item.setupId });
                        }}
                      >
                        <Settings className="w-4 h-4" />
                      </button>
                    )}

                    <button
                      className="text-blue-400 hover:text-blue-300 transition-colors"
                      title="Open official site"
                      onClick={() => postToHost('api.openUrl', { url: item.siteUrl })}
                    >
                      <ExternalLink className="w-4 h-4" />
                    </button>
                  </div>
                </div>

                {hasFields && (
                  <div className="flex items-center justify-between text-xs">
                    <button
                      className="text-blue-400 hover:text-blue-300 transition-colors"
                      onClick={() => setExpandedId((cur) => (cur === item.id ? null : item.id))}
                    >
                      {isExpanded ? 'Hide setup' : 'Setup →'}
                    </button>
                    <div className="text-gray-600">Saved to Atlas secure store</div>
                  </div>
                )}

                {isExpanded && hasFields && (
                  <div className="pt-3 space-y-3">
                    <div className="h-[1px] bg-gradient-to-r from-transparent via-blue-500/30 to-transparent" />

                    {item.fields!.map((f) => (
                      <div key={f.key} className="space-y-2">
                        <label className="text-xs text-gray-400">{f.label}</label>
                        <input
                          type="password"
                          placeholder={f.placeholder}
                          value={itemDrafts[f.key] ?? ''}
                          onChange={(e) =>
                            setDrafts((cur) => ({
                              ...cur,
                              [item.id]: { ...cur[item.id], [f.key]: e.target.value },
                            }))
                          }
                          className="w-full h-10 px-4 bg-gray-900/50 border border-blue-500/20 rounded-lg text-sm text-gray-300 placeholder-gray-600 focus:outline-none focus:border-blue-400/50 focus:bg-gray-900/70 transition-all font-mono"
                        />
                      </div>
                    ))}

                    <div className="flex items-center gap-3">
                      <button
                        className="h-10 px-4 rounded-lg bg-gradient-to-r from-blue-500 to-violet-500 hover:from-blue-400 hover:to-violet-400 text-white font-medium text-sm transition-all shadow-lg shadow-blue-500/30 hover:shadow-blue-500/50 inline-flex items-center gap-2"
                        onClick={() => {
                          postToHost('api.setIntegrationKeys', {
                            id: item.id,
                            values: itemDrafts,
                          });
                          postToHost('api.getState');
                          setExpandedId(null);
                          setDrafts((cur) => ({ ...cur, [item.id]: {} }));
                        }}
                      >
                        <Save className="w-4 h-4" />
                        Save
                      </button>
                      <button
                        className="h-10 px-4 rounded-lg border border-blue-500/20 bg-gray-900/30 hover:border-blue-400/30 hover:bg-gray-900/50 text-gray-400 hover:text-gray-300 text-sm transition-all"
                        onClick={() => {
                          setExpandedId(null);
                          setDrafts((cur) => ({ ...cur, [item.id]: {} }));
                        }}
                      >
                        Cancel
                      </button>
                    </div>
                  </div>
                )}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}
