import { motion } from "motion/react";
import { useEffect, useRef, useState } from "react";

interface VisualizerProps {
  type: "waveform" | "circular" | "particles" | "bars" | "blob" | "spectrum" | "neonGrid";
  dominantColor: string;
  isPlaying: boolean;
  intensity?: number;
}

export function Visualizer({
  type,
  dominantColor,
  isPlaying,
  intensity = 0.2,
}: VisualizerProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const animationRef = useRef<number>();
  const timeRef = useRef(0);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const ctx = canvas.getContext("2d");
    if (!ctx) return;

    // Set canvas size
    const updateSize = () => {
      canvas.width =
        canvas.offsetWidth * window.devicePixelRatio;
      canvas.height =
        canvas.offsetHeight * window.devicePixelRatio;
      ctx.scale(
        window.devicePixelRatio,
        window.devicePixelRatio,
      );
    };
    updateSize();
    window.addEventListener("resize", updateSize);

    // Generate mock audio data
    const generateAudioData = (numBars: number) => {
      return Array.from({ length: numBars }, (_, i) => {
        const base =
          Math.sin(timeRef.current * 0.001 + i * 0.1) * 0.5 +
          0.5;
        const variation =
          Math.sin(timeRef.current * 0.003 + i * 0.2) * 0.3 +
          0.7;
        const pulse =
          Math.sin(timeRef.current * 0.005) * 0.2 + 0.8;
        return base * variation * pulse * (isPlaying ? 1 : 0.3);
      });
    };

    const draw = () => {
      const width = canvas.offsetWidth;
      const height = canvas.offsetHeight;

      // Clear with slight trail effect
      ctx.globalCompositeOperation = "source-over";
      ctx.fillStyle = "rgba(11, 15, 20, 0.06)";
      ctx.fillRect(0, 0, width, height);

      // Parse dominant color
      const r = parseInt(dominantColor.slice(1, 3), 16);
      const g = parseInt(dominantColor.slice(3, 5), 16);
      const b = parseInt(dominantColor.slice(5, 7), 16);

      ctx.globalCompositeOperation = "lighter";
      if (type === "waveform") {
        drawWaveform(
          ctx,
          width,
          height,
          generateAudioData(100),
          r,
          g,
          b,
        );
      } else if (type === "circular") {
        drawCircular(
          ctx,
          width,
          height,
          generateAudioData(64),
          r,
          g,
          b,
        );
      } else if (type === "particles") {
        drawParticles(
          ctx,
          width,
          height,
          generateAudioData(50),
          r,
          g,
          b,
        );
      } else if (type === "bars") {
        drawBars(
          ctx,
          width,
          height,
          generateAudioData(32),
          r,
          g,
          b,
        );
      } else if (type === "blob") {
        drawBlob(
          ctx,
          width,
          height,
          generateAudioData(8),
          r,
          g,
          b,
        );
      } else if (type === "spectrum") {
        drawSpectrum(
          ctx,
          width,
          height,
          generateAudioData(90),
          r,
          g,
          b,
        );
      } else if (type === "neonGrid") {
        drawNeonGrid(
          ctx,
          width,
          height,
          generateAudioData(24),
          r,
          g,
          b,
          timeRef.current,
        );
      }
      ctx.globalCompositeOperation = "source-over";

      if (isPlaying) {
        timeRef.current += 16;
      }
      animationRef.current = requestAnimationFrame(draw);
    };

    draw();

    return () => {
      window.removeEventListener("resize", updateSize);
      if (animationRef.current) {
        cancelAnimationFrame(animationRef.current);
      }
    };
  }, [type, dominantColor, isPlaying]);

  return (
    <canvas
      ref={canvasRef}
      className="absolute inset-0 w-full h-full"
      style={{ opacity: intensity }}
    />
  );
}

function drawWaveform(
  ctx: CanvasRenderingContext2D,
  width: number,
  height: number,
  data: number[],
  r: number,
  g: number,
  b: number,
) {
  const centerY = height / 2;
  const amplitude = height * 0.3;

  // Create gradient
  const gradient = ctx.createLinearGradient(0, 0, width, 0);
  gradient.addColorStop(0, `rgba(${r}, ${g}, ${b}, 0.2)`);
  gradient.addColorStop(0.5, `rgba(${r}, ${g}, ${b}, 0.8)`);
  gradient.addColorStop(1, `rgba(${r}, ${g}, ${b}, 0.2)`);

  ctx.beginPath();
  ctx.strokeStyle = gradient;
  ctx.lineWidth = 3;
  ctx.lineCap = "round";

  for (let i = 0; i < data.length; i++) {
    const x = (i / data.length) * width;
    const y = centerY + Math.sin(i * 0.1) * amplitude * data[i];

    if (i === 0) {
      ctx.moveTo(x, y);
    } else {
      ctx.lineTo(x, y);
    }
  }

  ctx.stroke();

  // Add glow
  ctx.shadowBlur = 20;
  ctx.shadowColor = `rgba(${r}, ${g}, ${b}, 0.8)`;
  ctx.stroke();
  ctx.shadowBlur = 0;
}

function drawCircular(
  ctx: CanvasRenderingContext2D,
  width: number,
  height: number,
  data: number[],
  r: number,
  g: number,
  b: number,
) {
  const centerX = width / 2;
  const centerY = height / 2;
  const radius = Math.min(width, height) * 0.25;

  for (let i = 0; i < data.length; i++) {
    const angle = (i / data.length) * Math.PI * 2;
    const barHeight = data[i] * radius * 0.8;

    const x1 = centerX + Math.cos(angle) * radius;
    const y1 = centerY + Math.sin(angle) * radius;
    const x2 = centerX + Math.cos(angle) * (radius + barHeight);
    const y2 = centerY + Math.sin(angle) * (radius + barHeight);

    const gradient = ctx.createLinearGradient(x1, y1, x2, y2);
    gradient.addColorStop(0, `rgba(${r}, ${g}, ${b}, 0.3)`);
    gradient.addColorStop(1, `rgba(${r}, ${g}, ${b}, 0.9)`);

    ctx.beginPath();
    ctx.strokeStyle = gradient;
    ctx.lineWidth = 4;
    ctx.moveTo(x1, y1);
    ctx.lineTo(x2, y2);
    ctx.stroke();
  }
}

function drawParticles(
  ctx: CanvasRenderingContext2D,
  width: number,
  height: number,
  data: number[],
  r: number,
  g: number,
  b: number,
) {
  for (let i = 0; i < data.length; i++) {
    const x = Math.random() * width;
    const y = Math.random() * height;
    const size = data[i] * 8 + 1;
    const alpha = data[i] * 0.8;

    const gradient = ctx.createRadialGradient(
      x,
      y,
      0,
      x,
      y,
      size,
    );
    gradient.addColorStop(
      0,
      `rgba(${r}, ${g}, ${b}, ${alpha})`,
    );
    gradient.addColorStop(1, `rgba(${r}, ${g}, ${b}, 0)`);

    ctx.fillStyle = gradient;
    ctx.beginPath();
    ctx.arc(x, y, size, 0, Math.PI * 2);
    ctx.fill();
  }
}

function drawSpectrum(
  ctx: CanvasRenderingContext2D,
  width: number,
  height: number,
  data: number[],
  r: number,
  g: number,
  b: number,
) {
  const pad = 18;
  const usable = Math.max(10, width - pad * 2);
  const barW = usable / data.length;
  const maxH = height * 0.5;

  for (let i = 0; i < data.length; i++) {
    const v = data[i];
    const x = pad + i * barW;
    const h = Math.max(2, v * maxH);
    const y = height - pad - h;

    const grad = ctx.createLinearGradient(0, y, 0, y + h);
    grad.addColorStop(0, `rgba(${r}, ${g}, ${b}, 0.0)`);
    grad.addColorStop(0.2, `rgba(${r}, ${g}, ${b}, 0.35)`);
    grad.addColorStop(0.7, `rgba(168, 85, 247, 0.55)`);
    grad.addColorStop(1, `rgba(6, 182, 212, 0.75)`);

    ctx.fillStyle = grad;
    ctx.shadowBlur = 18;
    ctx.shadowColor = `rgba(${r}, ${g}, ${b}, 0.55)`;
    ctx.fillRect(x, y, Math.max(1, barW * 0.7), h);
  }

  ctx.shadowBlur = 0;
}

function drawNeonGrid(
  ctx: CanvasRenderingContext2D,
  width: number,
  height: number,
  data: number[],
  r: number,
  g: number,
  b: number,
  t: number,
) {
  const cell = 44;
  const offX = (t * 0.02) % cell;
  const offY = (t * 0.015) % cell;

  const pulse = data.reduce((a, x) => a + x, 0) / Math.max(1, data.length);
  const alpha = Math.max(0.04, Math.min(0.16, 0.06 + pulse * 0.12));

  ctx.lineWidth = 1;
  ctx.shadowBlur = 10;
  ctx.shadowColor = `rgba(${r}, ${g}, ${b}, ${alpha * 2.2})`;
  ctx.strokeStyle = `rgba(${r}, ${g}, ${b}, ${alpha})`;

  for (let x = -cell; x <= width + cell; x += cell) {
    ctx.beginPath();
    ctx.moveTo(x - offX, 0);
    ctx.lineTo(x - offX, height);
    ctx.stroke();
  }

  ctx.strokeStyle = `rgba(168, 85, 247, ${alpha * 0.7})`;
  ctx.shadowColor = `rgba(168, 85, 247, ${alpha * 1.8})`;
  for (let y = -cell; y <= height + cell; y += cell) {
    ctx.beginPath();
    ctx.moveTo(0, y - offY);
    ctx.lineTo(width, y - offY);
    ctx.stroke();
  }

  ctx.shadowBlur = 0;
}

function drawBars(
  ctx: CanvasRenderingContext2D,
  width: number,
  height: number,
  data: number[],
  r: number,
  g: number,
  b: number,
) {
  const barWidth = width / data.length;
  const maxHeight = height * 0.8;

  for (let i = 0; i < data.length; i++) {
    const x = i * barWidth;
    const barHeight = data[i] * maxHeight;
    const y = height - barHeight;

    const gradient = ctx.createLinearGradient(x, height, x, y);
    gradient.addColorStop(0, `rgba(${r}, ${g}, ${b}, 0.3)`);
    gradient.addColorStop(1, `rgba(${r}, ${g}, ${b}, 0.9)`);

    ctx.fillStyle = gradient;
    ctx.fillRect(x, y, barWidth - 2, barHeight);
  }
}

function drawBlob(
  ctx: CanvasRenderingContext2D,
  width: number,
  height: number,
  data: number[],
  r: number,
  g: number,
  b: number,
) {
  const centerX = width / 2;
  const centerY = height / 2;
  const baseRadius = Math.min(width, height) * 0.2;

  ctx.beginPath();

  for (let i = 0; i <= data.length; i++) {
    const angle = (i / data.length) * Math.PI * 2;
    const radius =
      baseRadius + data[i % data.length] * baseRadius * 0.8;
    const x = centerX + Math.cos(angle) * radius;
    const y = centerY + Math.sin(angle) * radius;

    if (i === 0) {
      ctx.moveTo(x, y);
    } else {
      ctx.lineTo(x, y);
    }
  }

  ctx.closePath();

  const gradient = ctx.createRadialGradient(
    centerX,
    centerY,
    0,
    centerX,
    centerY,
    baseRadius * 2,
  );
  gradient.addColorStop(0, `rgba(${r}, ${g}, ${b}, 0.5)`);
  gradient.addColorStop(1, `rgba(${r}, ${g}, ${b}, 0.1)`);

  ctx.fillStyle = gradient;
  ctx.fill();

  ctx.strokeStyle = `rgba(${r}, ${g}, ${b}, 0.8)`;
  ctx.lineWidth = 2;
  ctx.stroke();
}
