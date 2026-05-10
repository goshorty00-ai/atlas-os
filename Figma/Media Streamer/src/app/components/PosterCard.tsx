import { useState } from 'react';

interface PosterCardProps {
  title: string;
  posterUrl: string;
  rating?: number;
  progress?: number;
  onClick?: () => void;
}

export function PosterCard({ title, posterUrl, rating, progress, onClick }: PosterCardProps) {
  const [isHovered, setIsHovered] = useState(false);

  return (
    <div
      className="relative cursor-pointer transition-all duration-300 group"
      style={{ width: '150px' }}
      onMouseEnter={() => setIsHovered(true)}
      onMouseLeave={() => setIsHovered(false)}
      onClick={onClick}
    >
      <div
        className={`relative rounded-lg overflow-hidden transition-all duration-300 ${
          isHovered ? 'shadow-[0_0_20px_rgba(139,92,246,0.6)] scale-105' : 'shadow-lg'
        }`}
        style={{ height: '225px' }}
      >
        <img
          src={posterUrl}
          alt={title}
          className="w-full h-full object-cover"
        />

        {rating && (
          <div className="absolute top-2 right-2 bg-black/80 backdrop-blur-sm px-2 py-1 rounded-md flex items-center gap-1">
            <span className="text-yellow-400 text-xs">★</span>
            <span className="text-white text-xs font-medium">{rating.toFixed(1)}</span>
          </div>
        )}

        {progress !== undefined && progress > 0 && (
          <div className="absolute bottom-0 left-0 right-0 h-1 bg-gray-700/50">
            <div
              className="h-full bg-gradient-to-r from-purple-500 to-blue-500"
              style={{ width: `${progress}%` }}
            />
          </div>
        )}

        <div
          className={`absolute inset-0 bg-gradient-to-t from-black/80 via-black/20 to-transparent transition-opacity duration-300 ${
            isHovered ? 'opacity-100' : 'opacity-0'
          }`}
        />
      </div>

      <div className="mt-2">
        <h3 className="text-white text-sm font-medium line-clamp-2">{title}</h3>
      </div>
    </div>
  );
}
