import { motion, AnimatePresence } from 'motion/react';
import { Send, Mic, X, Plus, Settings, Trash2 } from 'lucide-react';
import { useState, useEffect } from 'react';
import { useSmartHomeContext } from '../SmartHomeContext';
import { onHostMessage } from '../bridge';

export function VoiceAssistant() {
  const { state, runCommand, saveCustomCommand, deleteCustomCommand } = useSmartHomeContext();
  const [input, setInput] = useState('');
  const [isListening, setIsListening] = useState(false);
  const [isExpanded, setIsExpanded] = useState(false);
  const [showCommands, setShowCommands] = useState(false);
  const [transcript, setTranscript] = useState('');
  const [feedback, setFeedback] = useState('');
  const [newPhrase, setNewPhrase] = useState('');

  const customCommands = state?.customCommands ?? [];

  useEffect(() => {
    const unsub = onHostMessage((type, payload) => {
      const p = payload as any;
      if (type === 'smart-home.actionResult') {
        setFeedback(p?.message ?? 'Done');
        setTimeout(() => setFeedback(''), 3000);
      } else if (type === 'smart-home.error') {
        setFeedback(`Error: ${p?.message ?? 'Unknown'}`);
        setTimeout(() => setFeedback(''), 4000);
      }
    });
    return unsub;
  }, []);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (input.trim()) {
      runCommand(input.trim());
      setInput('');
    }
  };

  const startListening = () => {
    setIsListening(true);
    const SR = (window as any).SpeechRecognition || (window as any).webkitSpeechRecognition;
    if (SR) {
      const recognition = new SR();
      recognition.continuous = false;
      recognition.interimResults = true;
      recognition.lang = 'en-US';
      recognition.onresult = (event: any) => {
        const t = event.results[event.resultIndex][0].transcript;
        setTranscript(t);
        if (event.results[event.resultIndex].isFinal) {
          setInput(t);
          setIsListening(false);
        }
      };
      recognition.onerror = () => setIsListening(false);
      recognition.onend = () => setIsListening(false);
      recognition.start();
    } else {
      setTranscript('Voice recognition not supported');
      setIsListening(false);
    }
  };

  const addCommand = () => {
    if (!newPhrase.trim()) return;
    saveCustomCommand({ phrase: newPhrase.trim(), enabled: true, responseText: '' });
    setNewPhrase('');
  };

  const quickCommands = [
    'Turn on all lights',
    'Turn off all lights',
    'Set TV volume to 50',
    'Show front door camera',
  ];

  return (
    <>
      <motion.div
        className="mx-4 my-2 relative rounded-xl backdrop-blur-xl overflow-hidden"
        style={{
          background: 'rgba(5, 10, 18, 0.85)',
          border: '1px solid rgba(0, 212, 255, 0.3)',
          boxShadow: '0 0 30px rgba(0, 212, 255, 0.15)',
        }}
        animate={{ height: isExpanded ? 'auto' : '60px' }}
      >
        <div className="flex items-center gap-4 px-4 h-[60px]">
          <div className="relative w-10 h-10 flex-shrink-0">
            {[...Array(2)].map((_, i) => (
              <motion.div key={i} className="absolute inset-0 rounded-full border-2 border-cyan-400"
                style={{ boxShadow: '0 0 15px rgba(0, 212, 255, 0.5)' }}
                animate={{ scale: [1, 1.4, 1], opacity: [0.6, 0, 0.6] }}
                transition={{ duration: 2, repeat: Infinity, delay: i * 1 }} />
            ))}
            <motion.div className="absolute inset-0 rounded-full bg-gradient-radial from-cyan-400 via-cyan-500 to-blue-600"
              style={{ boxShadow: '0 0 30px rgba(0, 212, 255, 0.8)' }}
              animate={{ boxShadow: ['0 0 30px rgba(0, 212, 255, 0.8)', '0 0 50px rgba(0, 212, 255, 1)', '0 0 30px rgba(0, 212, 255, 0.8)'] }}
              transition={{ duration: 2, repeat: Infinity }} />
          </div>
          <div className="flex-1">
            <h2 className="text-lg font-bold text-cyan-400">ATLAS AI Assistant</h2>
            <p className="text-xs text-cyan-400/60">
              {feedback || (isListening ? 'Listening...' : 'Ready for commands')}
            </p>
          </div>
          <div className="flex items-center gap-2">
            <motion.button onClick={() => setShowCommands(true)}
              className="w-9 h-9 rounded-lg flex items-center justify-center"
              style={{ background: 'rgba(0, 212, 255, 0.1)', border: '1px solid rgba(0, 212, 255, 0.3)' }}
              whileHover={{ scale: 1.05 }} whileTap={{ scale: 0.95 }}>
              <Settings className="w-4 h-4 text-cyan-400" />
            </motion.button>
            <motion.button onClick={() => setIsExpanded(!isExpanded)}
              className="px-4 py-2 rounded-lg text-sm font-medium"
              style={{ background: 'rgba(0, 212, 255, 0.2)', border: '1px solid rgba(0, 212, 255, 0.5)', color: '#00d4ff' }}
              whileHover={{ scale: 1.05 }} whileTap={{ scale: 0.95 }}>
              {isExpanded ? 'Collapse' : 'Expand'}
            </motion.button>
          </div>
        </div>

        <AnimatePresence>
          {isExpanded && (
            <motion.div className="px-4 pb-4" initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}>
              <form onSubmit={handleSubmit} className="relative mb-4">
                <input type="text" value={input} onChange={e => setInput(e.target.value)}
                  placeholder="Type or speak a command..."
                  className="w-full px-4 py-3 pr-24 rounded-lg text-sm bg-transparent outline-none placeholder:text-cyan-400/40"
                  style={{ border: '1px solid rgba(0, 212, 255, 0.3)', color: '#00d4ff' }} />
                <div className="absolute right-2 top-1/2 -translate-y-1/2 flex gap-2">
                  <motion.button type="button" onClick={startListening}
                    className="w-8 h-8 rounded-lg flex items-center justify-center"
                    style={{ background: isListening ? 'rgba(255,140,0,0.2)' : 'rgba(0,212,255,0.1)', border: `1px solid ${isListening ? '#ff8c00' : 'rgba(0,212,255,0.3)'}` }}
                    animate={isListening ? { boxShadow: ['0 0 10px #ff8c00', '0 0 20px #ff8c00', '0 0 10px #ff8c00'] } : {}}
                    transition={{ duration: 1, repeat: Infinity }}>
                    <Mic className="w-4 h-4" style={{ color: isListening ? '#ff8c00' : '#00d4ff' }} />
                  </motion.button>
                  <button type="submit" className="w-8 h-8 rounded-lg flex items-center justify-center"
                    style={{ background: 'rgba(0,212,255,0.2)', border: '1px solid rgba(0,212,255,0.5)' }}>
                    <Send className="w-4 h-4 text-cyan-400" />
                  </button>
                </div>
              </form>
              {transcript && (
                <div className="mb-4 px-3 py-2 rounded-lg text-xs"
                  style={{ background: 'rgba(0,212,255,0.1)', border: '1px solid rgba(0,212,255,0.2)', color: '#00d4ff' }}>
                  {transcript}
                </div>
              )}
              <div className="space-y-2">
                <p className="text-xs text-cyan-400/60">Quick Commands:</p>
                <div className="flex flex-wrap gap-2">
                  {quickCommands.map((cmd, i) => (
                    <motion.button key={i} onClick={() => setInput(cmd)}
                      className="px-3 py-1.5 rounded-full text-xs"
                      style={{ background: 'rgba(0,212,255,0.05)', border: '1px solid rgba(0,212,255,0.2)', color: '#00d4ff' }}
                      whileHover={{ background: 'rgba(0,212,255,0.15)', boxShadow: '0 0 15px rgba(0,212,255,0.3)' }}>
                      {cmd}
                    </motion.button>
                  ))}
                </div>
              </div>
            </motion.div>
          )}
        </AnimatePresence>
      </motion.div>

      {/* Custom Commands Panel */}
      <AnimatePresence>
        {showCommands && (
          <>
            <motion.div className="fixed inset-0 bg-black/60 backdrop-blur-sm z-50"
              initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
              onClick={() => setShowCommands(false)} />
            <motion.div className="fixed top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-full max-w-2xl z-50"
              initial={{ opacity: 0, scale: 0.9 }} animate={{ opacity: 1, scale: 1 }} exit={{ opacity: 0, scale: 0.9 }}>
              <div className="rounded-xl p-6 backdrop-blur-xl"
                style={{ background: 'rgba(5,10,18,0.95)', border: '1px solid rgba(0,212,255,0.3)', boxShadow: '0 0 50px rgba(0,212,255,0.3)' }}>
                <div className="flex items-center justify-between mb-6">
                  <h3 className="text-xl font-bold text-cyan-400">Custom Voice Commands</h3>
                  <button onClick={() => setShowCommands(false)}
                    className="w-8 h-8 rounded-lg flex items-center justify-center"
                    style={{ background: 'rgba(0,212,255,0.1)', border: '1px solid rgba(0,212,255,0.3)' }}>
                    <X className="w-4 h-4 text-cyan-400" />
                  </button>
                </div>
                <div className="space-y-3 mb-4 max-h-64 overflow-y-auto">
                  {customCommands.length === 0 && (
                    <p className="text-cyan-400/40 text-sm text-center py-4">No custom commands yet.</p>
                  )}
                  {customCommands.map(cmd => (
                    <div key={cmd.id} className="rounded-lg p-4 flex items-center gap-3"
                      style={{ background: 'rgba(0,212,255,0.05)', border: '1px solid rgba(0,212,255,0.2)' }}>
                      <div className="flex-1">
                        <p className="text-sm font-medium text-cyan-400">"{cmd.phrase}"</p>
                        {cmd.responseText && <p className="text-xs text-cyan-400/60 mt-1">{cmd.responseText}</p>}
                      </div>
                      <button onClick={() => deleteCustomCommand(cmd.id)}
                        className="w-7 h-7 rounded-lg flex items-center justify-center"
                        style={{ background: 'rgba(255,70,70,0.1)', border: '1px solid rgba(255,70,70,0.3)' }}>
                        <Trash2 className="w-3 h-3 text-red-400" />
                      </button>
                    </div>
                  ))}
                </div>
                <div className="flex gap-2">
                  <input value={newPhrase} onChange={e => setNewPhrase(e.target.value)}
                    placeholder="New command phrase..."
                    className="flex-1 px-4 py-3 rounded-lg text-sm bg-transparent outline-none placeholder:text-cyan-400/40"
                    style={{ border: '1px solid rgba(0,212,255,0.3)', color: '#00d4ff' }}
                    onKeyDown={e => e.key === 'Enter' && addCommand()} />
                  <motion.button onClick={addCommand}
                    className="px-4 py-3 rounded-lg flex items-center gap-2"
                    style={{ background: 'rgba(0,212,255,0.2)', border: '1px solid rgba(0,212,255,0.5)', color: '#00d4ff' }}
                    whileHover={{ scale: 1.02 }} whileTap={{ scale: 0.98 }}>
                    <Plus className="w-4 h-4" />
                    Add
                  </motion.button>
                </div>
              </div>
            </motion.div>
          </>
        )}
      </AnimatePresence>
    </>
  );
}
