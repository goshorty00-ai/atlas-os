import { useEffect, useRef, useState } from 'react';

interface StreamSourceCardProps {
  sourceName: string;
  providerName?: string;
  copyableLink?: string;
  quality: string;
  fileSize: string;
  audioLanguage: string;
  subtitles: string[];
  seederCount?: number;
  onCopyLink?: () => void;
  onPlay: () => void;
}

export function StreamSourceCard({
  sourceName,
  providerName,
  copyableLink,
  quality,
  fileSize,
  audioLanguage,
  subtitles,
  seederCount,
  onCopyLink,
  onPlay,
}: StreamSourceCardProps) {
  const [copied, setCopied] = useState(false);
  const [isLinkVisible, setIsLinkVisible] = useState(false);
  const revealedInputRef = useRef<HTMLInputElement | null>(null);

  useEffect(() => {
    if (!isLinkVisible || !copyableLink) {
      return;
    }

    const frame = window.requestAnimationFrame(() => {
      try {
        revealedInputRef.current?.focus();
        revealedInputRef.current?.select();
      } catch {
      }
    });

    return () => window.cancelAnimationFrame(frame);
  }, [copyableLink, isLinkVisible]);

  const getQualityColor = (quality: string) => {
    if (quality.includes('4K') || quality.includes('HDR') || quality.includes('Dolby')) {
      return 'from-purple-500 to-pink-500';
    }
    if (quality.includes('1080p')) {
      return 'from-blue-500 to-cyan-500';
    }
    return 'from-gray-500 to-gray-600';
  };

  const handleCopyClick = async (event: React.MouseEvent<HTMLButtonElement>) => {
    event.preventDefault();
    event.stopPropagation();

    if (!copyableLink) {
      setIsLinkVisible(false);
      setCopied(false);
      return;
    }

    setIsLinkVisible(true);

    let copiedSuccessfully = false;

    try {
      if (typeof navigator !== 'undefined' && navigator.clipboard?.writeText) {
        await navigator.clipboard.writeText(copyableLink);
        copiedSuccessfully = true;
      }
    } catch {
    }

    if (!copiedSuccessfully) {
      try {
        const ta = document.createElement('textarea');
        ta.value = copyableLink;
        ta.setAttribute('readonly', 'true');
        ta.style.position = 'fixed';
        ta.style.opacity = '0';
        document.body.appendChild(ta);
        ta.select();
        copiedSuccessfully = document.execCommand('copy');
        document.body.removeChild(ta);
      } catch {
      }
    }

    onCopyLink?.();

    if (copiedSuccessfully || onCopyLink) {
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1500);
    }
  };

  return (
    <div className="bg-white/5 backdrop-blur-md rounded-lg p-4 border border-white/10 hover:border-purple-500/50 transition-all duration-300 group">
      <div className="flex items-start justify-between mb-3">
        <div className="flex-1">
          <h3 className="text-white font-medium mb-1">{sourceName}</h3>
          {providerName ? <div className="mb-1 text-xs uppercase tracking-[0.2em] text-cyan-200/80">{providerName}</div> : null}
          <div className="flex items-center gap-2 flex-wrap">
            <span
              className={`inline-block px-2 py-1 rounded text-xs font-semibold text-white bg-gradient-to-r ${getQualityColor(
                quality
              )}`}
            >
              {quality}
            </span>
            <span className="text-gray-400 text-xs">{fileSize}</span>
          </div>
        </div>
        <div className="ml-3 flex flex-col items-end gap-2">
          <button
            type="button"
            onClick={handleCopyClick}
            onMouseDown={(event) => {
              event.preventDefault();
              event.stopPropagation();
            }}
            onContextMenu={(event) => {
              event.preventDefault();
              event.stopPropagation();
            }}
            className="rounded-full border border-cyan-300/25 bg-cyan-300/12 px-3 py-1.5 text-[10px] font-semibold uppercase tracking-[0.16em] text-cyan-100 transition hover:bg-cyan-300/20 disabled:cursor-not-allowed disabled:opacity-60"
            disabled={!copyableLink}
          >
            {!copyableLink ? 'Link Unavailable' : copied ? 'Copied' : 'Copy Link'}
          </button>
          <button
            type="button"
            onClick={onPlay}
            className="w-10 h-10 rounded-full bg-gradient-to-r from-purple-500 to-blue-500 flex items-center justify-center hover:scale-110 transition-transform duration-200 group-hover:shadow-[0_0_20px_rgba(139,92,246,0.6)]"
          >
            <svg
              width="16"
              height="16"
              viewBox="0 0 16 16"
              fill="white"
              xmlns="http://www.w3.org/2000/svg"
            >
              <path d="M3 2L13 8L3 14V2Z" />
            </svg>
          </button>
        </div>
      </div>

      {copyableLink && isLinkVisible ? (
        <div className="mb-3 rounded-xl border border-cyan-300/15 bg-slate-900/70 p-3">
          <div className="mb-2 text-[10px] font-semibold uppercase tracking-[0.18em] text-cyan-200/80">
            Link selected below. Press Ctrl+C if copy did not happen automatically.
          </div>
          <input
            ref={revealedInputRef}
            type="text"
            readOnly
            value={copyableLink}
            onFocus={(event) => event.currentTarget.select()}
            onClick={(event) => event.currentTarget.select()}
            className="w-full rounded-lg border border-white/10 bg-black/30 px-3 py-2 text-xs text-slate-100 outline-none"
          />
        </div>
      ) : null}

      <div className="space-y-2">
        <div className="flex items-center gap-2">
          <svg
            width="14"
            height="14"
            viewBox="0 0 14 14"
            fill="none"
            xmlns="http://www.w3.org/2000/svg"
            className="text-gray-400"
          >
            <path
              d="M7 1L8.5 5.5H13L9.5 8.5L11 13L7 10L3 13L4.5 8.5L1 5.5H5.5L7 1Z"
              stroke="currentColor"
              strokeWidth="1.5"
              strokeLinecap="round"
              strokeLinejoin="round"
            />
          </svg>
          <span className="text-gray-300 text-sm">{audioLanguage}</span>
        </div>

        {subtitles.length > 0 && (
          <div className="flex items-start gap-2">
            <svg
              width="14"
              height="14"
              viewBox="0 0 14 14"
              fill="none"
              xmlns="http://www.w3.org/2000/svg"
              className="text-gray-400 mt-0.5"
            >
              <rect
                x="1"
                y="3"
                width="12"
                height="8"
                rx="1"
                stroke="currentColor"
                strokeWidth="1.5"
              />
              <path
                d="M3 8H5M7 8H11"
                stroke="currentColor"
                strokeWidth="1.5"
                strokeLinecap="round"
              />
            </svg>
            <span className="text-gray-300 text-sm">{subtitles.join(', ')}</span>
          </div>
        )}

        {seederCount !== undefined && (
          <div className="flex items-center gap-2">
            <div
              className={`w-2 h-2 rounded-full ${
                seederCount > 50 ? 'bg-green-500' : seederCount > 10 ? 'bg-yellow-500' : 'bg-red-500'
              }`}
            />
            <span className="text-gray-400 text-xs">{seederCount} seeders</span>
          </div>
        )}
      </div>
    </div>
  );
}
