import { AnimatePresence, motion } from "motion/react";
import { ExternalLink, Users, X } from "lucide-react";

export function KaraokePartyModal({
  isOpen,
  onClose,
  url = "https://www.mykaraoke.party",
}: {
  isOpen: boolean;
  onClose: () => void;
  url?: string;
}) {
  return (
    <AnimatePresence>
      {isOpen && (
        <motion.div
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          className="fixed inset-0 z-[70] flex items-center justify-center bg-black/70 backdrop-blur-xl"
        >
          <button
            onClick={onClose}
            className="absolute top-8 right-8 w-12 h-12 rounded-full bg-white/5 backdrop-blur-md border border-white/10 flex items-center justify-center text-white/70 hover:text-white hover:bg-white/10 transition-all duration-300 z-10"
            aria-label="Close"
          >
            <X className="w-6 h-6" />
          </button>

          <motion.div
            initial={{ opacity: 0, scale: 0.98, y: 8 }}
            animate={{ opacity: 1, scale: 1, y: 0 }}
            exit={{ opacity: 0, scale: 0.98, y: 8 }}
            className="relative w-[min(1200px,94vw)] h-[min(760px,86vh)] rounded-2xl bg-black/40 border border-white/10 overflow-hidden shadow-2xl"
          >
            <div className="h-14 px-5 flex items-center gap-3 border-b border-white/10 bg-black/40">
              <div className="w-9 h-9 rounded-full bg-white/5 border border-white/10 flex items-center justify-center text-white/80">
                <Users className="w-5 h-5" />
              </div>
              <div className="min-w-0">
                <div className="text-sm text-white font-medium">Karaoke Party</div>
                <div className="text-xs text-white/60 truncate">{url}</div>
              </div>
              <div className="ml-auto flex items-center gap-2">
                <button
                  onClick={() => {
                    try {
                      window.open(url, "_blank", "noopener,noreferrer");
                    } catch {
                    }
                  }}
                  className="px-3 py-2 rounded-xl bg-white/5 border border-white/10 text-white/80 hover:text-white hover:bg-white/10 transition-colors flex items-center gap-2 text-xs"
                >
                  <ExternalLink className="w-4 h-4" />
                  Open in browser
                </button>
              </div>
            </div>

            <div className="h-[calc(100%-56px)] bg-black">
              <iframe
                title="Karaoke Party"
                src={url}
                className="w-full h-full"
                allow="autoplay; fullscreen; clipboard-read; clipboard-write"
              />
            </div>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}

