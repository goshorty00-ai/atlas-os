import React, { useState, useRef } from 'react'

const ERROR_IMG_SRC =
  'data:image/svg+xml;base64,PHN2ZyB3aWR0aD0iODgiIGhlaWdodD0iODgiIHhtbG5zPSJodHRwOi8vd3d3LnczLm9yZy8yMDAwL3N2ZyIgc3Ryb2tlPSIjMDAwIiBzdHJva2UtbGluZWpvaW49InJvdW5kIiBvcGFjaXR5PSIuMyIgZmlsbD0ibm9uZSIgc3Ryb2tlLXdpZHRoPSIzLjciPjxyZWN0IHg9IjE2IiB5PSIxNiIgd2lkdGg9IjU2IiBoZWlnaHQ9IjU2IiByeD0iNiIvPjxwYXRoIGQ9Im0xNiA1OCAxNi0xOCAzMiAzMiIvPjxjaXJjbGUgY3g9IjUzIiBjeT0iMzUiIHI9IjciLz48L3N2Zz4='

// _posterCache: completed results (imdbId -> URL | null)
// _inflightCache: in-progress promises — shared across all components for the same ID
const _posterCache = new Map<string, string | null>()
const _inflightCache = new Map<string, Promise<string | null>>()

function extractImdbId(url: string): string | null {
  const m = url.match(/(tt\d{7,})/i)
  return m ? m[1] : null
}

function toMedium(url?: string): string | undefined {
  return url?.replace('/poster/small/', '/poster/medium/')
}

async function fetchCinemetaType(imdbId: string, type: string): Promise<string | null> {
  const ctrl = new AbortController()
  const t = setTimeout(() => ctrl.abort(), 4000)
  try {
    const res = await fetch(
      `https://v3-cinemeta.strem.io/meta/${type}/${imdbId}.json`,
      { signal: ctrl.signal }
    )
    if (!res.ok) return null
    const data = await res.json()
    return (data?.meta?.poster as string) ?? null
  } catch { return null }
  finally { clearTimeout(t) }
}

// Queries the correct Cinemeta endpoint(s) for a poster.
// preferType='movie'|'series' queries only that endpoint.
// preferType='both' queries in parallel and prefers series (avoids movie posters on TV shows).
async function cinemetaPoster(imdbId: string, preferType: 'movie' | 'series' | 'both' = 'both'): Promise<string | null> {
  const cacheKey = `${imdbId}:${preferType}`;
  if (_posterCache.has(cacheKey)) return _posterCache.get(cacheKey) ?? null;
  if (_inflightCache.has(cacheKey)) return _inflightCache.get(cacheKey)!;

  let promise: Promise<string | null>;
  if (preferType === 'movie') {
    promise = fetchCinemetaType(imdbId, 'movie').then(url => {
      _posterCache.set(cacheKey, url);
      _inflightCache.delete(cacheKey);
      return url;
    });
  } else if (preferType === 'series') {
    promise = fetchCinemetaType(imdbId, 'series').then(url => {
      _posterCache.set(cacheKey, url);
      _inflightCache.delete(cacheKey);
      return url;
    });
  } else {
    // 'both': parallel query, prefer series to avoid movie posters on TV shows
    promise = Promise.all([
      fetchCinemetaType(imdbId, 'series'),
      fetchCinemetaType(imdbId, 'movie'),
    ]).then(([series, movie]) => {
      const url = series ?? movie ?? null;
      _posterCache.set(cacheKey, url);
      _inflightCache.delete(cacheKey);
      return url;
    });
  }

  _inflightCache.set(cacheKey, promise);
  return promise;
}

interface ImageWithFallbackProps extends React.ImgHTMLAttributes<HTMLImageElement> {
  fallbackSrc?: string
  mediaType?: 'movie' | 'series' | 'both'
}

export function ImageWithFallback({ src, alt, style, className, fallbackSrc, mediaType = 'both', ...rest }: ImageWithFallbackProps) {
  // Init from cache so cached items render instantly
  const [activeSrc, setActiveSrc] = useState(() => {
    const medium = toMedium(src) ?? src
    const id = extractImdbId(medium ?? '')
    const cacheKey = id ? `${id}:${mediaType}` : null
    if (cacheKey && _posterCache.has(cacheKey) && _posterCache.get(cacheKey)) return _posterCache.get(cacheKey)!
    return medium
  })
  const [didError, setDidError] = useState(false)
  const genRef = useRef(0)
  // Once Cinemeta gives us a confirmed poster URL, lock it — never revert
  // regardless of how many times the bridge updates the src prop.
  const lockedRef = useRef(false)

  React.useEffect(() => {
    // If we already have a confirmed Cinemeta poster, ignore all src changes.
    if (lockedRef.current) return

    genRef.current++
    const gen = genRef.current
    const medium = toMedium(src) ?? src
    const newId = extractImdbId(medium ?? '')

    setDidError(false)

    // Cache hit — use immediately and lock
    const idCacheKey = newId ? `${newId}:${mediaType}` : null
    if (idCacheKey && _posterCache.has(idCacheKey)) {
      const cached = _posterCache.get(idCacheKey)
      if (cached) { setActiveSrc(cached); lockedRef.current = true; return }
    }

    // Show metahub URL as placeholder while Cinemeta fetches in parallel
    setActiveSrc(medium)

    if (newId) {
      cinemetaPoster(newId, mediaType).then(url => {
        if (genRef.current !== gen) return  // stale — item changed before Cinemeta came back
        if (url) { setActiveSrc(url); lockedRef.current = true }
        // If no Cinemeta URL, stay on metahub — handleError will catch the 404
      })
    }
  }, [src])

  const handleError = () => {
    const current = activeSrc ?? ''
    const gen = genRef.current
    const imdbId = extractImdbId(current)

    if (imdbId) {
      const errCacheKey = `${imdbId}:${mediaType}`
      if (_posterCache.has(errCacheKey)) {
        const cached = _posterCache.get(errCacheKey)
        if (cached && cached !== current) { setActiveSrc(cached); lockedRef.current = true; return }
        if (fallbackSrc) { setActiveSrc(fallbackSrc); return }
        setDidError(true); return
      }
      // Cinemeta fetch was kicked off in useEffect — attach to the shared promise
      cinemetaPoster(imdbId, mediaType).then(url => {
        if (genRef.current !== gen) return
        if (url) { setActiveSrc(url); lockedRef.current = true; return }
        if (fallbackSrc) { setActiveSrc(fallbackSrc); return }
        setDidError(true)
      })
      return
    }

    if (fallbackSrc && fallbackSrc !== current) { setActiveSrc(fallbackSrc); return }
    setDidError(true)
  }

  if (didError) {
    return (
      <div
        className={`inline-block bg-slate-800 text-center align-middle ${className ?? ''}`}
        style={style}
      >
        <div className="flex items-center justify-center w-full h-full opacity-30">
          <img src={ERROR_IMG_SRC} alt="Error loading image" data-original-url={src} />
        </div>
      </div>
    )
  }

  return (
    <img src={activeSrc} alt={alt} className={className} style={style} {...rest} onError={handleError} />
  )
}
