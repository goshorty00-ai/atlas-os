import { motion } from 'motion/react';
import { Send, Mic } from 'lucide-react';
import { useState } from 'react';

export function AIAssistant() {
  const [input, setInput] = useState('');
  const [isListening, setIsListening] = useState(false);

  const exampleCommands = [
    'Turn living room lights blue',
    'Set house temperature to 21 degrees',
    'Activate night mode',
    'Show front door camera',
  ];

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (input.trim()) {
      // Handle command
      setInput('');
    }
  };

  return (
    <div className="mt-8">
      <h3 className="text-sm text-cyan-400/80 mb-4 flex items-center gap-2">
        <div className="w-1 h-4 bg-cyan-400 rounded-full" style={{ boxShadow: '0 0 10px #00d4ff' }} />
        AI Assistant
      </h3>

      <div
        className="relative rounded-xl p-6 backdrop-blur-xl"
        style={{
          background: 'rgba(5, 10, 18, 0.6)',
          border: '1px solid rgba(0, 212, 255, 0.3)',
          boxShadow: '0 0 30px rgba(0, 212, 255, 0.15), inset 0 0 30px rgba(0, 212, 255, 0.05)',
        }}
      >
        {/* Holographic Orb */}
        <div className="flex justify-center mb-6">
          <div className="relative w-24 h-24">
            {/* Outer Rings */}
            {[...Array(3)].map((_, i) => (
              <motion.div
                key={i}
                className="absolute inset-0 rounded-full border-2 border-cyan-400"
                style={{
                  boxShadow: '0 0 20px rgba(0, 212, 255, 0.5)',
                }}
                animate={{
                  scale: [1, 1.3, 1],
                  opacity: [0.6, 0, 0.6],
                }}
                transition={{
                  duration: 3,
                  repeat: Infinity,
                  delay: i * 1,
                }}
              />
            ))}
            
            {/* Core Orb */}
            <motion.div
              className="absolute inset-0 rounded-full bg-gradient-radial from-cyan-400 via-cyan-500 to-blue-600"
              style={{
                boxShadow: '0 0 40px rgba(0, 212, 255, 0.8), inset 0 0 30px rgba(255, 255, 255, 0.3)',
              }}
              animate={{
                boxShadow: [
                  '0 0 40px rgba(0, 212, 255, 0.8), inset 0 0 30px rgba(255, 255, 255, 0.3)',
                  '0 0 60px rgba(0, 212, 255, 1), inset 0 0 40px rgba(255, 255, 255, 0.5)',
                  '0 0 40px rgba(0, 212, 255, 0.8), inset 0 0 30px rgba(255, 255, 255, 0.3)',
                ],
              }}
              transition={{
                duration: 2,
                repeat: Infinity,
              }}
            />

            {/* Energy Particles */}
            {[...Array(8)].map((_, i) => (
              <motion.div
                key={i}
                className="absolute w-1 h-1 rounded-full bg-cyan-400"
                style={{
                  left: '50%',
                  top: '50%',
                  boxShadow: '0 0 5px #00d4ff',
                }}
                animate={{
                  x: Math.cos((i / 8) * Math.PI * 2) * 50,
                  y: Math.sin((i / 8) * Math.PI * 2) * 50,
                  opacity: [1, 0],
                }}
                transition={{
                  duration: 2,
                  repeat: Infinity,
                  delay: i * 0.25,
                }}
              />
            ))}
          </div>
        </div>

        {/* Input Form */}
        <form onSubmit={handleSubmit} className="relative">
          <input
            type="text"
            value={input}
            onChange={(e) => setInput(e.target.value)}
            placeholder="Ask the house AI..."
            className="w-full px-4 py-3 pr-24 rounded-lg text-sm bg-transparent outline-none placeholder:text-cyan-400/40 transition-all"
            style={{
              border: '1px solid rgba(0, 212, 255, 0.3)',
              color: '#00d4ff',
              boxShadow: '0 0 20px rgba(0, 212, 255, 0.1)',
            }}
            onFocus={(e) => {
              e.target.style.borderColor = 'rgba(0, 212, 255, 0.6)';
              e.target.style.boxShadow = '0 0 30px rgba(0, 212, 255, 0.3)';
            }}
            onBlur={(e) => {
              e.target.style.borderColor = 'rgba(0, 212, 255, 0.3)';
              e.target.style.boxShadow = '0 0 20px rgba(0, 212, 255, 0.1)';
            }}
          />
          
          <div className="absolute right-2 top-1/2 -translate-y-1/2 flex gap-2">
            <motion.button
              type="button"
              onClick={() => setIsListening(!isListening)}
              className="w-8 h-8 rounded-lg flex items-center justify-center transition-all"
              style={{
                background: isListening ? 'rgba(255, 140, 0, 0.2)' : 'rgba(0, 212, 255, 0.1)',
                border: `1px solid ${isListening ? '#ff8c00' : 'rgba(0, 212, 255, 0.3)'}`,
              }}
              animate={isListening ? {
                boxShadow: [
                  '0 0 10px #ff8c00',
                  '0 0 20px #ff8c00',
                  '0 0 10px #ff8c00',
                ],
              } : {}}
              transition={{ duration: 1, repeat: Infinity }}
            >
              <Mic className="w-4 h-4" style={{ color: isListening ? '#ff8c00' : '#00d4ff' }} />
            </motion.button>

            <button
              type="submit"
              className="w-8 h-8 rounded-lg flex items-center justify-center transition-all hover:scale-110"
              style={{
                background: 'rgba(0, 212, 255, 0.2)',
                border: '1px solid rgba(0, 212, 255, 0.5)',
                boxShadow: '0 0 15px rgba(0, 212, 255, 0.3)',
              }}
            >
              <Send className="w-4 h-4 text-cyan-400" />
            </button>
          </div>
        </form>

        {/* Example Commands */}
        <div className="mt-4 space-y-2">
          <p className="text-xs text-cyan-400/60">Quick Commands:</p>
          <div className="flex flex-wrap gap-2">
            {exampleCommands.map((cmd, index) => (
              <motion.button
                key={index}
                onClick={() => setInput(cmd)}
                className="px-3 py-1.5 rounded-full text-xs transition-all"
                style={{
                  background: 'rgba(0, 212, 255, 0.05)',
                  border: '1px solid rgba(0, 212, 255, 0.2)',
                  color: '#00d4ff',
                }}
                whileHover={{
                  background: 'rgba(0, 212, 255, 0.15)',
                  borderColor: 'rgba(0, 212, 255, 0.4)',
                  boxShadow: '0 0 15px rgba(0, 212, 255, 0.3)',
                }}
              >
                {cmd}
              </motion.button>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}
