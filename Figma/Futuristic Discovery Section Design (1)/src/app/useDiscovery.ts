import { useState, useEffect } from 'react';
import { onHostMessage, requestDiscoveryData, searchMedia as bridgeSearchMedia, getMediaDetails as bridgeGetMediaDetails } from './bridge';

export interface DiscoveryMedia {
  id: string;
  title: string;
  image: string;
  rating?: number;
  type: 'movie' | 'tv' | 'music' | 'game' | 'trailer';
  isNew?: boolean;
  releaseDate?: string;
  overview?: string;
  genres?: string[];
  runtime?: string;
}

export interface DiscoveryNews {
  id: string;
  headline: string;
  image: string;
  preview: string;
  timeAgo: string;
  trending?: boolean;
  url?: string;
}

export interface DiscoveryCelebrity {
  id: string;
  name: string;
  image: string;
  role: string;
  trending?: boolean;
}

export interface DiscoveryData {
  trending: DiscoveryMedia[];
  trailers: DiscoveryMedia[];
  news: DiscoveryNews[];
  upcoming: DiscoveryMedia[];
  celebrities: DiscoveryCelebrity[];
  featured?: DiscoveryMedia;
}

export function useDiscovery() {
  const [data, setData] = useState<DiscoveryData | null>(null);
  const [loading, setLoading] = useState(true);
  const [selectedMedia, setSelectedMedia] = useState<DiscoveryMedia | null>(null);

  useEffect(() => {
    const unsub = onHostMessage((type, payload) => {
      switch (type) {
        case 'discovery.data':
          setData(payload as DiscoveryData);
          setLoading(false);
          break;
        case 'discovery.searchResults':
          // Handle search results
          break;
        case 'discovery.mediaDetails':
          setSelectedMedia(payload as DiscoveryMedia);
          break;
      }
    });

    // Request initial data
    requestDiscoveryData();

    return unsub;
  }, []);

  const searchMedia = (query: string, filters?: { type?: string; genre?: string }) => {
    bridgeSearchMedia(query, filters);
  };

  const getMediaDetails = (id: string, type: 'movie' | 'tv' | 'music' | 'game') => {
    bridgeGetMediaDetails(id, type);
  };

  return {
    data,
    loading,
    selectedMedia,
    searchMedia,
    getMediaDetails,
  };
}
