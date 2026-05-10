import { useEffect, useRef, useState } from "react";
import { motion } from "motion/react";

interface Album {
  id: string;
  artwork: string;
  title: string;
  artist: string;
}

interface WaveSpectrumViewProps {
  albums: Album[];
  spectrumBars?: number[];
  onAlbumClick?: (album: Album) => void;
}

export function WaveSpectrumView({ albums, spectrumBars = [], onAlbumClick }: WaveSpectrumViewProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const [hoveredAlbum, setHoveredAlbum] = useState<Album | null>(null);
  const albumPositionsRef = useRef<Array<{ x: number; y: number; album: Album; radius: number }>>([]);
  const mouseRef = useRef({ x: 0, y: 0 });
  const imageCacheRef = useRef<Map<string, HTMLImageElement>>(new Map());
  const barsRef = useRef<number[]>(Array.from({ length: 64 }, () => 0));

  useEffect(() => {
    barsRef.current = spectrumBars.length > 0 ? spectrumBars : Array.from({ length: 64 }, () => 0);
  }, [spectrumBars]);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const ctx = canvas.getContext("2d");
    if (!ctx) return;

    const width = canvas.width;
    const height = canvas.height;

    // Initialize album positions in wave formation
    albumPositionsRef.current = albums.slice(0, 30).map((album, index) => {
      const col = index % 6;
      const row = Math.floor(index / 6);
      
      return {
        x: col * (width / 6) + width / 12,
        y: row * 100 + 150,
        album,
        radius: 40,
      };
    });

    const visibleAlbums = albums.slice(0, 30);
    const keepIds = new Set(visibleAlbums.map((album) => album.id));
    for (const album of visibleAlbums) {
      if (imageCacheRef.current.has(album.id)) continue;
      const image = new Image();
      image.crossOrigin = "anonymous";
      image.decoding = "async";
      image.src = album.artwork;
      imageCacheRef.current.set(album.id, image);
    }
    for (const id of Array.from(imageCacheRef.current.keys())) {
      if (!keepIds.has(id)) {
        imageCacheRef.current.delete(id);
      }
    }

    let animationId: number;
    let time = 0;

    const animate = () => {
      time += 0.02;

      // Clear canvas with fade effect
      ctx.fillStyle = "rgba(0, 0, 0, 0.05)";
      ctx.fillRect(0, 0, width, height);

      const activeBars = barsRef.current.length > 0 ? barsRef.current : Array.from({ length: 64 }, () => 0);

      ctx.lineWidth = 2;
      for (let freq = 0; freq < 5; freq++) {
        ctx.beginPath();
        for (let x = 0; x < width; x += 5) {
          const barIndex = Math.min(activeBars.length - 1, Math.max(0, Math.floor((x / width) * activeBars.length)));
          const amplitude = activeBars[barIndex] ?? 0;
          const y = height / 2 +
            Math.sin(x * 0.01 + time + freq * 0.3) * (18 + amplitude * 95) +
            Math.cos(time * 2 + freq + barIndex * 0.12) * (8 + amplitude * 32);
          
          if (x === 0) {
            ctx.moveTo(x, y);
          } else {
            ctx.lineTo(x, y);
          }
        }
        
        const gradient = ctx.createLinearGradient(0, 0, width, 0);
        gradient.addColorStop(0, `rgba(6, 182, 212, ${0.1 + freq * 0.1})`);
        gradient.addColorStop(0.5, `rgba(168, 85, 247, ${0.2 + freq * 0.1})`);
        gradient.addColorStop(1, `rgba(236, 72, 153, ${0.1 + freq * 0.1})`);
        ctx.strokeStyle = gradient;
        
        ctx.stroke();
      }

      // Draw frequency bars
      const barCount = activeBars.length;
      const barWidth = width / barCount;
      
      for (let i = 0; i < barCount; i++) {
        const amplitude = activeBars[i] ?? 0;
        const barHeight = Math.max(10, amplitude * (height * 0.42) + Math.sin(time * 2.4 + i * 0.12) * 10);
        
        const x = i * barWidth;
        const y = height - barHeight;
        
        const gradient = ctx.createLinearGradient(x, y, x, height);
        gradient.addColorStop(0, "rgba(6, 182, 212, 0.6)");
        gradient.addColorStop(0.5, "rgba(168, 85, 247, 0.4)");
        gradient.addColorStop(1, "rgba(6, 182, 212, 0.2)");
        
        ctx.fillStyle = gradient;
        ctx.fillRect(x, y, barWidth - 2, barHeight);
      }

      // Draw albums with wave effect
      albumPositionsRef.current.forEach((pos, index) => {
        // Wave displacement
        const amplitude = activeBars[index % activeBars.length] ?? 0;
        const waveY = Math.sin(pos.x * 0.01 + time * 2) * (10 + amplitude * 50) + 
               Math.cos(time * 3 + index * 0.3) * (6 + amplitude * 18);
        const currentY = pos.y + waveY;

        // Pulsing effect
        const pulse = 1 + (activeBars[(index * 2) % activeBars.length] ?? 0) * 0.35;
        const radius = pos.radius * pulse;

        // Create circular gradient for album glow
        const glowGradient = ctx.createRadialGradient(
          pos.x, currentY, radius * 0.5,
          pos.x, currentY, radius * 2
        );
        glowGradient.addColorStop(0, "rgba(6, 182, 212, 0.4)");
        glowGradient.addColorStop(0.5, "rgba(168, 85, 247, 0.2)");
        glowGradient.addColorStop(1, "transparent");

        ctx.fillStyle = glowGradient;
        ctx.beginPath();
        ctx.arc(pos.x, currentY, radius * 2, 0, Math.PI * 2);
        ctx.fill();

        // Draw album circle
        ctx.save();
        ctx.beginPath();
        ctx.arc(pos.x, currentY, radius, 0, Math.PI * 2);
        ctx.closePath();
        ctx.clip();

        // Create image element if not exists
        const img = imageCacheRef.current.get(pos.album.id);
        if (img && img.complete && img.naturalWidth > 0) {
          ctx.drawImage(img, pos.x - radius, currentY - radius, radius * 2, radius * 2);
        } else {
          const fallbackGradient = ctx.createLinearGradient(pos.x - radius, currentY - radius, pos.x + radius, currentY + radius);
          fallbackGradient.addColorStop(0, "rgba(6, 182, 212, 0.35)");
          fallbackGradient.addColorStop(1, "rgba(168, 85, 247, 0.22)");
          ctx.fillStyle = fallbackGradient;
          ctx.fillRect(pos.x - radius, currentY - radius, radius * 2, radius * 2);
        }

        ctx.restore();

        // Draw border
        ctx.strokeStyle = "rgba(6, 182, 212, 0.6)";
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.arc(pos.x, currentY, radius, 0, Math.PI * 2);
        ctx.stroke();

        // Rotating ring effect
        ctx.strokeStyle = "rgba(168, 85, 247, 0.6)";
        ctx.lineWidth = 3;
        ctx.beginPath();
        ctx.arc(pos.x, currentY, radius + 5, time * 2 + index, time * 2 + index + Math.PI);
        ctx.stroke();
      });

      // Draw connecting lines between nearby albums
      ctx.strokeStyle = "rgba(6, 182, 212, 0.1)";
      ctx.lineWidth = 1;
      
      for (let i = 0; i < albumPositionsRef.current.length; i++) {
        for (let j = i + 1; j < albumPositionsRef.current.length; j++) {
          const pos1 = albumPositionsRef.current[i];
          const pos2 = albumPositionsRef.current[j];
          
          const dx = pos2.x - pos1.x;
          const dy = pos2.y - pos1.y;
          const dist = Math.sqrt(dx * dx + dy * dy);
          
          if (dist < 200) {
            const wave1Y = Math.sin(pos1.x * 0.01 + time * 2) * (8 + (activeBars[i % activeBars.length] ?? 0) * 30) + 
                          Math.cos(time * 3 + i * 0.3) * 16;
            const wave2Y = Math.sin(pos2.x * 0.01 + time * 2) * (8 + (activeBars[j % activeBars.length] ?? 0) * 30) + 
                          Math.cos(time * 3 + j * 0.3) * 16;
            
            ctx.globalAlpha = 1 - dist / 200;
            ctx.beginPath();
            ctx.moveTo(pos1.x, pos1.y + wave1Y);
            ctx.lineTo(pos2.x, pos2.y + wave2Y);
            ctx.stroke();
            ctx.globalAlpha = 1;
          }
        }
      }

      animationId = requestAnimationFrame(animate);
    };

    animate();

    return () => cancelAnimationFrame(animationId);
  }, [albums]);

  const handleCanvasClick = (e: React.MouseEvent<HTMLCanvasElement>) => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const rect = canvas.getBoundingClientRect();
    const x = e.clientX - rect.left;
    const y = e.clientY - rect.top;

    // Check if click is on any album
    for (const pos of albumPositionsRef.current) {
      const dx = x - pos.x;
      const dy = y - pos.y;
      const dist = Math.sqrt(dx * dx + dy * dy);

      if (dist < pos.radius) {
        onAlbumClick?.(pos.album);
        break;
      }
    }
  };

  const handleMouseMove = (e: React.MouseEvent<HTMLCanvasElement>) => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const rect = canvas.getBoundingClientRect();
    const x = e.clientX - rect.left;
    const y = e.clientY - rect.top;

    mouseRef.current = { x, y };

    // Check hover state
    let foundHover = false;
    for (const pos of albumPositionsRef.current) {
      const dx = x - pos.x;
      const dy = y - pos.y;
      const dist = Math.sqrt(dx * dx + dy * dy);

      if (dist < pos.radius) {
        setHoveredAlbum(pos.album);
        foundHover = true;
        break;
      }
    }

    if (!foundHover) {
      setHoveredAlbum(null);
    }
  };

  return (
    <div className="relative h-[700px] rounded-2xl overflow-hidden bg-black">
      {/* Instructions */}
      <div className="absolute top-4 left-4 z-10 bg-black/60 backdrop-blur-sm rounded-xl px-4 py-2 border border-white/10">
        <p className="text-sm text-gray-300">
          <span className="text-cyan-400 font-semibold">Click albums</span> to play • Watch the <span className="text-purple-400 font-semibold">wave spectrum</span>
        </p>
      </div>

      {/* Canvas */}
      <canvas
        ref={canvasRef}
        width={1600}
        height={700}
        className="w-full h-full cursor-pointer"
        onClick={handleCanvasClick}
        onMouseMove={handleMouseMove}
      />

      {/* Hovered album info */}
      {hoveredAlbum && (
        <motion.div
          className="absolute bottom-4 left-1/2 -translate-x-1/2 bg-black/80 backdrop-blur-xl rounded-xl p-4 border border-cyan-500/30 min-w-80 z-10"
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          exit={{ opacity: 0, y: 20 }}
        >
          <div className="flex items-center gap-4">
            <div className="w-16 h-16 rounded-lg overflow-hidden border border-cyan-500/50">
              <img
                src={hoveredAlbum.artwork}
                alt={hoveredAlbum.title}
                className="w-full h-full object-cover"
              />
            </div>
            <div className="flex-1 min-w-0">
              <h4 className="text-white font-semibold truncate">{hoveredAlbum.title}</h4>
              <p className="text-gray-300 text-sm truncate">{hoveredAlbum.artist}</p>
            </div>
          </div>
        </motion.div>
      )}
    </div>
  );
}
