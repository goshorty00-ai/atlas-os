import React, { useState } from 'react'

const ERROR_IMG_SRC =
  'data:image/svg+xml;base64,PHN2ZyB3aWR0aD0iODgiIGhlaWdodD0iODgiIHhtbG5zPSJodHRwOi8vd3d3LnczLm9yZy8yMDAwL3N2ZyIgc3Ryb2tlPSIjMDAwIiBzdHJva2UtbGluZWpvaW49InJvdW5kIiBvcGFjaXR5PSIuMyIgZmlsbD0ibm9uZSIgc3Ryb2tlLXdpZHRoPSIzLjciPjxyZWN0IHg9IjE2IiB5PSIxNiIgd2lkdGg9IjU2IiBoZWlnaHQ9IjU2IiByeD0iNiIvPjxwYXRoIGQ9Im0xNiA1OCAxNi0xOCAzMiAzMiIvPjxjaXJjbGUgY3g9IjUzIiBjeT0iMzUiIHI9IjciLz48L3N2Zz4='

/** Extract imdb ID from an RPDB poster URL to build a metahub fallback */
function rpdbToMetahub(url: string): string | null {
  const m = url.match(/\/imdb\/poster[^\/]*\/(tt\d+)/i)
  if (m) return `https://images.metahub.space/poster/medium/${m[1]}/img`
  return null
}

interface ImageWithFallbackProps extends React.ImgHTMLAttributes<HTMLImageElement> {
  fallbackSrc?: string
}

export function ImageWithFallback({ src, alt, style, className, fallbackSrc, ...rest }: ImageWithFallbackProps) {
  const [activeSrc, setActiveSrc] = useState(src)
  const [didError, setDidError] = useState(false)

  React.useEffect(() => {
    setActiveSrc(src)
    setDidError(false)
  }, [src])

  const handleError = () => {
    const current = activeSrc ?? ''
    if (current.includes('ratingposterdb.com')) {
      const mh = rpdbToMetahub(current)
      if (mh && mh !== current) { setActiveSrc(mh); return }
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
