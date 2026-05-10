import { useEffect, useRef } from 'react';
import { motion } from 'motion/react';

interface Node {
  id: number;
  x: number;
  y: number;
  label: string;
  type: 'core' | 'edge' | 'client';
}

export function NetworkVisualization() {
  const canvasRef = useRef<HTMLCanvasElement>(null);

  const nodes: Node[] = [
    { id: 1, x: 200, y: 150, label: 'Atlas Core', type: 'core' },
    { id: 2, x: 100, y: 80, label: 'API Gateway', type: 'edge' },
    { id: 3, x: 300, y: 80, label: 'Load Balancer', type: 'edge' },
    { id: 4, x: 50, y: 200, label: 'Client 1', type: 'client' },
    { id: 5, x: 150, y: 220, label: 'Client 2', type: 'client' },
    { id: 6, x: 250, y: 220, label: 'Client 3', type: 'client' },
    { id: 7, x: 350, y: 200, label: 'Database', type: 'edge' },
  ];

  const connections = [
    { from: 1, to: 2 },
    { from: 1, to: 3 },
    { from: 1, to: 7 },
    { from: 2, to: 4 },
    { from: 2, to: 5 },
    { from: 3, to: 5 },
    { from: 3, to: 6 },
  ];

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    let animationFrame: number;
    let pulseOffset = 0;

    const animate = () => {
      ctx.clearRect(0, 0, canvas.width, canvas.height);
      pulseOffset += 0.02;

      // Draw connections with animated flow
      connections.forEach((conn, index) => {
        const fromNode = nodes.find(n => n.id === conn.from);
        const toNode = nodes.find(n => n.id === conn.to);
        
        if (!fromNode || !toNode) return;

        const gradient = ctx.createLinearGradient(fromNode.x, fromNode.y, toNode.x, toNode.y);
        const opacity = 0.3 + Math.sin(pulseOffset + index * 0.5) * 0.2;
        
        gradient.addColorStop(0, `rgba(59, 130, 246, ${opacity})`);
        gradient.addColorStop(0.5, `rgba(139, 92, 246, ${opacity})`);
        gradient.addColorStop(1, `rgba(59, 130, 246, ${opacity})`);

        ctx.strokeStyle = gradient;
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.moveTo(fromNode.x, fromNode.y);
        ctx.lineTo(toNode.x, toNode.y);
        ctx.stroke();

        // Animated data packets
        const t = (pulseOffset * 2 + index) % 1;
        const packetX = fromNode.x + (toNode.x - fromNode.x) * t;
        const packetY = fromNode.y + (toNode.y - fromNode.y) * t;
        
        ctx.fillStyle = '#60a5fa';
        ctx.shadowBlur = 10;
        ctx.shadowColor = '#60a5fa';
        ctx.beginPath();
        ctx.arc(packetX, packetY, 3, 0, Math.PI * 2);
        ctx.fill();
        ctx.shadowBlur = 0;
      });

      // Draw nodes
      nodes.forEach((node) => {
        const pulse = Math.sin(pulseOffset * 2) * 2;
        let color, size;

        switch (node.type) {
          case 'core':
            color = '#8b5cf6';
            size = 14 + pulse;
            break;
          case 'edge':
            color = '#3b82f6';
            size = 10 + pulse;
            break;
          case 'client':
            color = '#06b6d4';
            size = 8;
            break;
        }

        // Outer glow
        const gradient = ctx.createRadialGradient(node.x, node.y, 0, node.x, node.y, size * 2);
        gradient.addColorStop(0, color + '40');
        gradient.addColorStop(1, color + '00');
        ctx.fillStyle = gradient;
        ctx.beginPath();
        ctx.arc(node.x, node.y, size * 2, 0, Math.PI * 2);
        ctx.fill();

        // Node
        ctx.fillStyle = color;
        ctx.beginPath();
        ctx.arc(node.x, node.y, size, 0, Math.PI * 2);
        ctx.fill();

        // Inner highlight
        ctx.fillStyle = 'rgba(255, 255, 255, 0.3)';
        ctx.beginPath();
        ctx.arc(node.x - 2, node.y - 2, size * 0.4, 0, Math.PI * 2);
        ctx.fill();
      });

      animationFrame = requestAnimationFrame(animate);
    };

    animate();

    return () => {
      if (animationFrame) {
        cancelAnimationFrame(animationFrame);
      }
    };
  }, []);

  return (
    <div className="relative rounded-xl border border-blue-500/20 bg-gradient-to-br from-blue-500/5 to-violet-500/5 backdrop-blur-xl p-6 overflow-hidden"
      style={{
        boxShadow: '0 8px 32px 0 rgba(31, 38, 135, 0.15)'
      }}
    >
      {/* Glassmorphism overlay */}
      <div className="absolute inset-0 rounded-xl bg-gradient-to-br from-white/5 to-white/0 pointer-events-none" />

      <div className="relative space-y-4">
        <div className="flex items-center justify-between">
          <h3 className="text-lg font-medium text-gray-200">Network Topology</h3>
          <div className="flex items-center space-x-2">
            <div className="w-2 h-2 rounded-full bg-green-400 animate-pulse shadow-lg shadow-green-400/50" />
            <span className="text-xs text-gray-500">Live</span>
          </div>
        </div>

        <div className="relative h-64 bg-gray-900/30 rounded-lg border border-blue-500/20 overflow-hidden">
          <canvas
            ref={canvasRef}
            width={400}
            height={256}
            className="w-full h-full"
          />
          
          {/* Grid overlay */}
          <div 
            className="absolute inset-0 opacity-10 pointer-events-none"
            style={{
              backgroundImage: `
                linear-gradient(to right, rgba(59, 130, 246, 0.3) 1px, transparent 1px),
                linear-gradient(to bottom, rgba(59, 130, 246, 0.3) 1px, transparent 1px)
              `,
              backgroundSize: '20px 20px'
            }}
          />
        </div>

        {/* Legend */}
        <div className="flex items-center justify-center space-x-6 text-xs text-gray-500">
          <div className="flex items-center space-x-2">
            <div className="w-3 h-3 rounded-full bg-violet-500" />
            <span>Core</span>
          </div>
          <div className="flex items-center space-x-2">
            <div className="w-3 h-3 rounded-full bg-blue-500" />
            <span>Gateway</span>
          </div>
          <div className="flex items-center space-x-2">
            <div className="w-3 h-3 rounded-full bg-cyan-500" />
            <span>Client</span>
          </div>
        </div>

        {/* Stats */}
        <div className="grid grid-cols-3 gap-3 pt-2">
          {[
            { label: 'Nodes', value: '7' },
            { label: 'Latency', value: '12ms' },
            { label: 'Throughput', value: '2.4GB/s' }
          ].map((stat) => (
            <div key={stat.label} className="text-center space-y-1">
              <div className="text-xl font-medium text-gray-200">{stat.value}</div>
              <div className="text-xs text-gray-500">{stat.label}</div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
