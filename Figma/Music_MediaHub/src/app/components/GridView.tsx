import { AlbumCard } from "./AlbumCard";

interface Album {
  id: string;
  artwork: string;
  title: string;
  artist: string;
  year: number;
  genre: string;
  progress?: number;
  aiScore?: number;
  isPlaying?: boolean;
}

interface GridViewProps {
  albums: Album[];
  onAlbumClick?: (album: Album) => void;
  onAlbumAction?: (album: Album, action: "play" | "playlist" | "lyrics" | "edit" | "cover" | "aiCover" | "optimize" | "refresh" | "openFolder" | "remove") => void;
}

export function GridView({ albums, onAlbumClick, onAlbumAction }: GridViewProps) {
  return (
    <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6 gap-6 pb-8">
      {albums.map((album) => (
        <AlbumCard
          key={album.id}
          {...album}
          onClick={() => onAlbumClick?.(album)}
          onAction={(action) => onAlbumAction?.(album, action)}
        />
      ))}
    </div>
  );
}
