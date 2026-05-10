import { useEffect, useRef, useState } from "react";
import { motion } from "motion/react";

interface Album {
  id: string;
  artwork: string;
  title: string;
  artist: string;
  genre: string;
}

interface GalaxyViewProps {
  albums: Album[];
}

interface Star {
  x: number;
  y: number;
  z: number;
  size: number;
  color: string;
  album: Album;
  vx: number;
  vy: number;
  vz: number;
}

const genreColors: Record<string, string> = {
  Electronic: "#06b6d4",
  Synthwave: "#a855f7",
  Jazz: "#f59e0b",
  Rock: "#ef4444",
  "Hip Hop": "#10b981",
  Classical: "#8b5cf6",
  Indie: "#ec4899",
  Ambient: "#14b8a6",
};

export function MusicGalaxyView({ albums }: GalaxyViewProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const [stars, setStars] = useState<Star[]>([]);
  const rotationRef = useRef({ x: 0, y: 0 });
  const [zoom, setZoom] = useState(1);
  const [hoveredAlbum, setHoveredAlbum] = useState<Album | null>(null);
  const mouseRef = useRef({ x: 0, y: 0 });
  
  // Initialize galaxy
  useEffect(() => {
    const newStars: Star[] = albums.map((album, index) => {
      const angle = (index / albums.length) * Math.PI * 2;
      const radius = 200 + Math.random() * 300;
      const height = (Math.random() - 0.5) * 200;
      
      return {
        x: Math.cos(angle) * radius,
        y: height,
        z: Math.sin(angle) * radius,
        size: 3 + Math.random() * 2,
        color: genreColors[album.genre] || "#ffffff",
        album,
        vx: (Math.random() - 0.5) * 0.1,
        vy: (Math.random() - 0.5) * 0.1,
        vz: (Math.random() - 0.5) * 0.1,
      };
    });
    
    setStars(newStars);
  }, [albums]);
  
  // Animation loop
  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    
    const ctx = canvas.getContext("2d");
    if (!ctx) return;
    
    let animationId: number;
    
    const animate = () => {
      const width = canvas.width;
      const height = canvas.height;
      
      // Clear with fade effect
      ctx.fillStyle = "rgba(0, 0, 0, 0.1)";
      ctx.fillRect(0, 0, width, height);
      
      // Update rotation based on mouse using ref
      rotationRef.current = {
        x: rotationRef.current.x + (mouseRef.current.y - height / 2) * 0.00005,
        y: rotationRef.current.y + (mouseRef.current.x - width / 2) * 0.00005,
      };
      
      // Sort stars by distance for proper depth
      const sortedStars = [...stars].sort((a, b) => {
        const distA = Math.sqrt(a.x * a.x + a.y * a.y + a.z * a.z);
        const distB = Math.sqrt(b.x * b.x + b.y * b.y + b.z * b.z);
        return distB - distA;
      });
      
      // Draw stars
      sortedStars.forEach((star) => {
        // Apply rotation
        const cosX = Math.cos(rotationRef.current.x);
        const sinX = Math.sin(rotationRef.current.x);
        const cosY = Math.cos(rotationRef.current.y);
        const sinY = Math.sin(rotationRef.current.y);
        
        let x = star.x;
        let y = star.y * cosX - star.z * sinX;
        let z = star.y * sinX + star.z * cosX;
        
        const tempX = x;
        x = x * cosY + z * sinY;
        z = -tempX * sinY + z * cosY;
        
        // Project to 2D
        const scale = 500 / (500 + z * zoom);
        const x2d = x * scale + width / 2;
        const y2d = y * scale + height / 2;
        
        // Draw star
        const size = Math.max(0.1, star.size * scale);
        const alpha = Math.max(0, Math.min(1, 1 - z / 1000));
        
        // Only draw if visible and size is positive
        if (size > 0 && alpha > 0) {
          // Glow effect
          const glowRadius = Math.max(0.1, size * 3);
          const gradient = ctx.createRadialGradient(x2d, y2d, 0, x2d, y2d, glowRadius);
          gradient.addColorStop(0, `${star.color}${Math.floor(alpha * 255).toString(16).padStart(2, '0')}`);
          gradient.addColorStop(1, "transparent");
          
          ctx.fillStyle = gradient;
          ctx.beginPath();
          ctx.arc(x2d, y2d, glowRadius, 0, Math.PI * 2);
          ctx.fill();
          
          // Core
          ctx.fillStyle = star.color;
          ctx.globalAlpha = alpha;
          ctx.beginPath();
          ctx.arc(x2d, y2d, size, 0, Math.PI * 2);
          ctx.fill();
          ctx.globalAlpha = 1;
        }
        
        // Update position for drift
        star.x += star.vx;
        star.y += star.vy;
        star.z += star.vz;
      });
      
      // Draw connections between nearby stars
      ctx.strokeStyle = "rgba(6, 182, 212, 0.1)";
      ctx.lineWidth = 1;
      
      for (let i = 0; i < sortedStars.length; i++) {
        for (let j = i + 1; j < sortedStars.length; j++) {
          const star1 = sortedStars[i];
          const star2 = sortedStars[j];
          
          const dx = star1.x - star2.x;
          const dy = star1.y - star2.y;
          const dz = star1.z - star2.z;
          const dist = Math.sqrt(dx * dx + dy * dy + dz * dz);
          
          if (dist < 100) {
            const cosX = Math.cos(rotationRef.current.x);
            const sinX = Math.sin(rotationRef.current.x);
            const cosY = Math.cos(rotationRef.current.y);
            const sinY = Math.sin(rotationRef.current.y);
            
            // Project both points
            const project = (star: Star) => {
              let x = star.x;
              let y = star.y * cosX - star.z * sinX;
              let z = star.y * sinX + star.z * cosX;
              
              const tempX = x;
              x = x * cosY + z * sinY;
              z = -tempX * sinY + z * cosY;
              
              const scale = 500 / (500 + z * zoom);
              return {
                x: x * scale + width / 2,
                y: y * scale + height / 2,
              };
            };
            
            const p1 = project(star1);
            const p2 = project(star2);
            
            ctx.beginPath();
            ctx.moveTo(p1.x, p1.y);
            ctx.lineTo(p2.x, p2.y);
            ctx.stroke();
          }
        }
      }
      
      animationId = requestAnimationFrame(animate);
    };
    
    animate();
    
    return () => cancelAnimationFrame(animationId);
  }, [stars, zoom]);
  
  const handleMouseMove = (e: React.MouseEvent<HTMLCanvasElement>) => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    
    const rect = canvas.getBoundingClientRect();
    mouseRef.current = {
      x: e.clientX - rect.left,
      y: e.clientY - rect.top,
    };
  };
  
  const handleWheel = (e: React.WheelEvent<HTMLCanvasElement>) => {
    e.preventDefault();
    setZoom((prev) => Math.max(0.5, Math.min(3, prev + e.deltaY * -0.001)));
  };
  
  return (
    <div className="relative h-[600px] rounded-2xl overflow-hidden">
      {/* Instructions */}
      <div className="absolute top-4 left-4 z-10 bg-black/60 backdrop-blur-sm rounded-xl px-4 py-2 border border-white/10">
        <p className="text-sm text-gray-300">
          <span className="text-cyan-400 font-semibold">Move mouse</span> to rotate • <span className="text-cyan-400 font-semibold">Scroll</span> to zoom
        </p>
      </div>
      
      {/* Genre legend */}
      <div className="absolute top-4 right-4 z-10 bg-black/60 backdrop-blur-sm rounded-xl p-4 border border-white/10 space-y-2">
        <h3 className="text-sm font-semibold text-white mb-2">Genre Clusters</h3>
        {Object.entries(genreColors).map(([genre, color]) => (
          <div key={genre} className="flex items-center gap-2">
            <div className="w-3 h-3 rounded-full" style={{ backgroundColor: color }} />
            <span className="text-xs text-gray-300">{genre}</span>
          </div>
        ))}
      </div>
      
      {/* Galaxy canvas */}
      <canvas
        ref={canvasRef}
        width={1600}
        height={600}
        className="w-full h-full cursor-move"
        onMouseMove={handleMouseMove}
        onWheel={handleWheel}
      />
      
      {/* Album info on hover */}
      {hoveredAlbum && (
        <motion.div
          className="absolute bottom-4 left-1/2 -translate-x-1/2 bg-black/80 backdrop-blur-xl rounded-xl p-4 border border-cyan-500/30 min-w-64"
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          exit={{ opacity: 0, y: 20 }}
        >
          <h4 className="text-white font-semibold">{hoveredAlbum.title}</h4>
          <p className="text-gray-300 text-sm">{hoveredAlbum.artist}</p>
        </motion.div>
      )}
    </div>
  );
}