import { useEffect, useRef, useState } from 'react';
import { motion } from 'motion/react';

interface RadarPoint {
  id: string;
  x: number;
  y: number;
  type: 'safe' | 'warning' | 'threat';
  label: string;
}

export function SecurityRadar() {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const [angle, setAngle] = useState(0);
  const [radarPoints, setRadarPoints] = useState<RadarPoint[]>([]);

  useEffect(() => {
    // Generate random radar points
    const generatePoints = () => {
      const points: RadarPoint[] = [];
      const types: Array<'safe' | 'warning' | 'threat'> = ['safe', 'safe', 'safe', 'safe', 'warning', 'safe'];
      
      for (let i = 0; i < 12; i++) {
        const radius = 50 + Math.random() * 150;
        const theta = Math.random() * Math.PI * 2;
        points.push({
          id: `point-${i}`,
          x: 200 + radius * Math.cos(theta),
          y: 200 + radius * Math.sin(theta),
          type: types[Math.floor(Math.random() * types.length)],
          label: `P${i + 1}`
        });
      }
      setRadarPoints(points);
    };

    generatePoints();
    const interval = setInterval(generatePoints, 8000);
    return () => clearInterval(interval);
  }, []);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const drawRadar = () => {
      ctx.clearRect(0, 0, 400, 400);

      // Draw concentric circles
      ctx.strokeStyle = 'rgba(14, 165, 233, 0.2)';
      ctx.lineWidth = 1;
      for (let i = 1; i <= 3; i++) {
        ctx.beginPath();
        ctx.arc(200, 200, i * 60, 0, Math.PI * 2);
        ctx.stroke();
      }

      // Draw crosshair
      ctx.strokeStyle = 'rgba(14, 165, 233, 0.3)';
      ctx.beginPath();
      ctx.moveTo(200, 20);
      ctx.lineTo(200, 380);
      ctx.moveTo(20, 200);
      ctx.lineTo(380, 200);
      ctx.stroke();

      // Draw scanning line
      ctx.save();
      ctx.translate(200, 200);
      ctx.rotate(angle);
      
      const gradient = ctx.createLinearGradient(0, 0, 180, 0);
      gradient.addColorStop(0, 'rgba(14, 165, 233, 0)');
      gradient.addColorStop(0.5, 'rgba(14, 165, 233, 0.4)');
      gradient.addColorStop(1, 'rgba(14, 165, 233, 0.1)');
      
      ctx.fillStyle = gradient;
      ctx.beginPath();
      ctx.moveTo(0, 0);
      ctx.arc(0, 0, 180, 0, Math.PI / 3);
      ctx.closePath();
      ctx.fill();
      
      ctx.restore();
    };

    const animate = () => {
      drawRadar();
      setAngle((prev) => (prev + 0.02) % (Math.PI * 2));
    };

    const animationId = setInterval(animate, 30);
    return () => clearInterval(animationId);
  }, [angle]);

  return (
    <div className="relative w-full h-full flex items-center justify-center">
      <canvas
        ref={canvasRef}
        width={400}
        height={400}
        className="absolute"
      />
      
      {radarPoints.map((point) => (
        <motion.div
          key={point.id}
          initial={{ scale: 0, opacity: 0 }}
          animate={{ scale: 1, opacity: 1 }}
          exit={{ scale: 0, opacity: 0 }}
          className="absolute"
          style={{
            left: `${point.x}px`,
            top: `${point.y}px`,
            transform: 'translate(-50%, -50%)'
          }}
        >
          <div className={`
            w-3 h-3 rounded-full
            ${point.type === 'safe' ? 'bg-emerald-400 shadow-[0_0_10px_rgba(52,211,153,0.6)]' : ''}
            ${point.type === 'warning' ? 'bg-amber-400 shadow-[0_0_10px_rgba(251,191,36,0.6)] animate-pulse' : ''}
            ${point.type === 'threat' ? 'bg-red-500 shadow-[0_0_15px_rgba(239,68,68,0.8)] animate-pulse' : ''}
          `} />
        </motion.div>
      ))}

      <div className="absolute inset-0 flex items-center justify-center pointer-events-none">
        <div className="text-center">
          <div className="text-sky-400 text-xs font-mono mb-1">SCANNING</div>
          <div className="text-sky-300 text-2xl font-bold">{radarPoints.filter(p => p.type === 'safe').length}</div>
          <div className="text-sky-500 text-xs">ACTIVE PROCESSES</div>
        </div>
      </div>
    </div>
  );
}
