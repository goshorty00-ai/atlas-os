import { motion } from "motion/react";
import { ChevronLeft, ChevronRight } from "lucide-react";
import { useEffect, useRef, useState } from "react";

interface HorizontalShelfProps {
  title: string;
  children: React.ReactNode;
}

export function HorizontalShelf({ title, children }: HorizontalShelfProps) {
  const scrollRef = useRef<HTMLDivElement>(null);
  const [canScrollLeft, setCanScrollLeft] = useState(false);
  const [canScrollRight, setCanScrollRight] = useState(true);
  
  const scroll = (direction: "left" | "right") => {
    if (!scrollRef.current) return;
    
    const scrollAmount = 600;
    const newScrollLeft = scrollRef.current.scrollLeft + (direction === "right" ? scrollAmount : -scrollAmount);
    
    scrollRef.current.scrollTo({
      left: newScrollLeft,
      behavior: "smooth"
    });
  };
  
  const checkScroll = () => {
    if (!scrollRef.current) return;
    
    const { scrollLeft, scrollWidth, clientWidth } = scrollRef.current;
    setCanScrollLeft(scrollLeft > 0);
    setCanScrollRight(scrollLeft < scrollWidth - clientWidth - 10);
  };

  const handleWheel = (event: React.WheelEvent<HTMLDivElement>) => {
    if (!scrollRef.current) return;
    if (Math.abs(event.deltaY) <= Math.abs(event.deltaX)) return;
    event.preventDefault();
    scrollRef.current.scrollBy({ left: event.deltaY, behavior: "auto" });
  };

  useEffect(() => {
    checkScroll();
  });
  
  return (
    <div className="space-y-4">
      {/* Header */}
      <div className="flex items-center justify-between">
        <h2 className="text-xl font-semibold text-white">
          {title}
        </h2>
        
        <div className="flex gap-2">
          <motion.button
            className={`w-8 h-8 rounded-full bg-white/5 backdrop-blur-sm flex items-center justify-center border border-white/10 transition-all ${
              canScrollLeft ? "text-white hover:bg-white/10" : "text-gray-600 cursor-not-allowed"
            }`}
            whileTap={canScrollLeft ? { scale: 0.9 } : {}}
            onClick={() => scroll("left")}
            disabled={!canScrollLeft}
          >
            <ChevronLeft className="w-5 h-5" />
          </motion.button>
          
          <motion.button
            className={`w-8 h-8 rounded-full bg-white/5 backdrop-blur-sm flex items-center justify-center border border-white/10 transition-all ${
              canScrollRight ? "text-white hover:bg-white/10" : "text-gray-600 cursor-not-allowed"
            }`}
            whileTap={canScrollRight ? { scale: 0.9 } : {}}
            onClick={() => scroll("right")}
            disabled={!canScrollRight}
          >
            <ChevronRight className="w-5 h-5" />
          </motion.button>
        </div>
      </div>
      
      {/* Scrollable content */}
      <div className="relative group">
        <div
          ref={scrollRef}
          className="flex gap-4 overflow-x-auto scrollbar-hide pb-4"
          onScroll={checkScroll}
          onWheel={handleWheel}
          style={{
            scrollbarWidth: "none",
            msOverflowStyle: "none"
          }}
        >
          {children}
        </div>
        
        {/* Gradient overlays */}
        {canScrollLeft && (
          <div className="absolute left-0 top-0 bottom-0 w-20 bg-gradient-to-r from-black to-transparent pointer-events-none" />
        )}
        {canScrollRight && (
          <div className="absolute right-0 top-0 bottom-0 w-20 bg-gradient-to-l from-black to-transparent pointer-events-none" />
        )}
      </div>
    </div>
  );
}
