import { useState } from "react";
import { Mic, Send } from "lucide-react";
import { motion } from "motion/react";

interface Message {
  id: string;
  sender: "user" | "atlas";
  content: string;
  timestamp: Date;
}

export function ChatArea() {
  const [messages, setMessages] = useState<Message[]>([
    {
      id: "1",
      sender: "atlas",
      content: "SYSTEM INITIALIZED",
      timestamp: new Date(Date.now() - 300000),
    },
    {
      id: "2",
      sender: "user",
      content: "run diagnostics",
      timestamp: new Date(Date.now() - 240000),
    },
    {
      id: "3",
      sender: "atlas",
      content: "DIAGNOSTICS COMPLETE · ALL SYSTEMS NOMINAL",
      timestamp: new Date(Date.now() - 180000),
    },
    {
      id: "4",
      sender: "user",
      content: "status report",
      timestamp: new Date(Date.now() - 120000),
    },
    {
      id: "5",
      sender: "atlas",
      content: "CPU 23% · MEMORY NOMINAL · NO ANOMALIES",
      timestamp: new Date(Date.now() - 60000),
    },
  ]);
  const [input, setInput] = useState("");
  const [isListening, setIsListening] = useState(true);

  const handleSend = () => {
    if (!input.trim()) return;

    const newMessage: Message = {
      id: Date.now().toString(),
      sender: "user",
      content: input,
      timestamp: new Date(),
    };

    setMessages([...messages, newMessage]);
    setInput("");

    // Simulate Atlas response
    setTimeout(() => {
      const atlasResponse: Message = {
        id: (Date.now() + 1).toString(),
        sender: "atlas",
        content: "ACKNOWLEDGED · PROCESSING",
        timestamp: new Date(),
      };
      setMessages((prev) => [...prev, atlasResponse]);
    }, 800);
  };

  return (
    <div className="flex-1 flex flex-col bg-[#0b0f14]/20 relative min-w-0">
      {/* Messages area */}
      <div className="flex-1 overflow-y-auto p-[3vh] space-y-[1.5vh] scrollbar-hide">
        {messages.map((msg) => (
          <motion.div
            key={msg.id}
            initial={{ opacity: 0, y: 20 }}
            animate={{ opacity: 1, y: 0 }}
            className={`flex ${
              msg.sender === "user" ? "justify-end" : "justify-start"
            }`}
          >
            <div
              className={`max-w-[65%] rounded-lg px-[2vw] py-[1.5vh] ${
                msg.sender === "atlas"
                  ? "bg-[#0f1419] border border-cyan-500/20 shadow-[0_0_20px_rgba(34,211,238,0.1)]"
                  : "bg-slate-800/50 border border-slate-700/50"
              }`}
            >
              <div className="flex items-center gap-[1vw] mb-[0.7vh]">
                <span
                  className={`text-[0.6vw] min-text-[10px] font-mono uppercase tracking-widest ${
                    msg.sender === "atlas" ? "text-cyan-400" : "text-orange-400"
                  }`}
                >
                  {msg.sender === "atlas" ? "Atlas" : "User"}
                </span>
                <span className="text-[0.6vw] min-text-[10px] text-slate-600 font-mono">
                  {msg.timestamp.toLocaleTimeString("en-US", {
                    hour: "2-digit",
                    minute: "2-digit",
                  })}
                </span>
              </div>
              <p
                className={`text-[0.8vw] min-text-[14px] leading-relaxed ${
                  msg.sender === "atlas"
                    ? "text-slate-200 font-mono tracking-wide"
                    : "text-slate-300"
                }`}
              >
                {msg.content}
              </p>
            </div>
          </motion.div>
        ))}
      </div>

      {/* Input area */}
      <div className="p-[2vh] border-t border-cyan-500/10 shrink-0">
        <div className="flex items-center gap-[1.5vw] bg-[#0f1419] border border-cyan-500/20 rounded-lg p-[1.5vh]">
          <button
            onClick={() => setIsListening(!isListening)}
            className={`p-[1vh] rounded-lg transition-all ${
              isListening
                ? "bg-cyan-500/20 text-cyan-400"
                : "text-slate-500 hover:text-cyan-400"
            }`}
          >
            <Mic className="w-[1.5vw] h-[1.5vw] min-w-[20px] min-h-[20px]" />
          </button>

          <div className="flex-1 flex flex-col min-w-0">
            {isListening && (
              <motion.span
                initial={{ opacity: 0 }}
                animate={{ opacity: 1 }}
                className="text-[0.6vw] min-text-[10px] text-cyan-400/60 font-mono mb-[0.5vh] uppercase tracking-wider"
              >
                Listening for "Atlas"
              </motion.span>
            )}
            <input
              type="text"
              value={input}
              onChange={(e) => setInput(e.target.value)}
              onKeyPress={(e) => e.key === "Enter" && handleSend()}
              placeholder="Enter command..."
              className="bg-transparent text-slate-200 text-[0.8vw] min-text-[14px] outline-none placeholder:text-slate-600"
            />
          </div>

          <motion.button
            onClick={handleSend}
            whileHover={{ scale: 1.05 }}
            whileTap={{ scale: 0.95 }}
            className="p-[1vh] rounded-lg bg-cyan-500/10 text-cyan-400 hover:bg-cyan-500/20 transition-all hover:shadow-[0_0_12px_rgba(34,211,238,0.3)]"
          >
            <Send className="w-[1.5vw] h-[1.5vw] min-w-[20px] min-h-[20px]" />
          </motion.button>
        </div>
      </div>
    </div>
  );
}