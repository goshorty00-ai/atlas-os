import { useState, useRef, useEffect } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import { MessageSquare, X, Send, Bot, User, Mic } from 'lucide-react';
import { bridge } from '../bridge';

const SECURITY_SPEECH_WIRED = false;
const SECURITY_MIC_WIRED = false;

interface Message {
  id: string;
  text: string;
  sender: 'user' | 'ai';
  timestamp: Date;
}

export function SecurityChat() {
  const [isOpen, setIsOpen] = useState(false);
  const [messages, setMessages] = useState<Message[]>([
    {
      id: '1',
      text: "Hello! I'm your AI security assistant. Ask me anything about your system security, threats, or recent activity.",
      sender: 'ai',
      timestamp: new Date()
    }
  ]);
  const [inputText, setInputText] = useState('');
  const [isTyping, setIsTyping] = useState(false);
  const [voiceNote, setVoiceNote] = useState('');
  const messagesEndRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  const scrollToBottom = () => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  };

  useEffect(() => {
    scrollToBottom();
  }, [messages]);

  useEffect(() => {
    if (isOpen && inputRef.current) {
      inputRef.current.focus();
    }
  }, [isOpen]);

  useEffect(() => {
    const unsubResponse = bridge.on('chat_response', (data: any) => {
      const aiMessage: Message = {
        id: `ai-${Date.now()}`,
        text: data.text,
        sender: 'ai',
        timestamp: new Date()
      };
      setMessages(prev => [...prev, aiMessage]);
    });

    const unsubTyping = bridge.on('chat_typing', (data: any) => {
      setIsTyping(data.typing);
    });

    const unsubTranscript = bridge.on('security-mic-transcript', (data: any) => {
      const transcript = typeof data?.transcript === 'string' ? data.transcript.trim() : '';
      if (!transcript) {
        return;
      }

      setInputText(transcript);
      setVoiceNote('Voice captured. Press Send.');
    });

    return () => {
      unsubResponse();
      unsubTyping();
      unsubTranscript();
    };
  }, []);

  useEffect(() => {
    if (!voiceNote) {
      return;
    }

    const timeout = window.setTimeout(() => setVoiceNote(''), 2400);
    return () => window.clearTimeout(timeout);
  }, [voiceNote]);

  const handleSend = () => {
    if (!inputText.trim()) return;

    const userMessage: Message = {
      id: `user-${Date.now()}`,
      text: inputText,
      sender: 'user',
      timestamp: new Date()
    };

    setMessages(prev => [...prev, userMessage]);
    bridge.sendChat(inputText);
    setInputText('');
  };

  const handleKeyPress = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSend();
    }
  };

  const handleMicClick = () => {
    if (!SECURITY_MIC_WIRED) {
      setVoiceNote('Mic not wired');
      return;
    }
  };

  return (
    <>
      <AnimatePresence>
        {!isOpen && (
          <motion.button
            initial={{ scale: 0, opacity: 0 }}
            animate={{ scale: 1, opacity: 1 }}
            exit={{ scale: 0, opacity: 0 }}
            whileHover={{ scale: 1.05 }}
            whileTap={{ scale: 0.95 }}
            onClick={() => setIsOpen(true)}
            className="fixed bottom-6 right-6 z-50 p-3 rounded-full bg-gradient-to-br from-sky-500 to-purple-500 border border-sky-400/50 shadow-[0_0_30px_rgba(14,165,233,0.5)] hover:shadow-[0_0_40px_rgba(14,165,233,0.7)] transition-all duration-300"
          >
            <MessageSquare className="w-5 h-5 text-white" />
            <motion.div
              animate={{ scale: [1, 1.2, 1] }}
              transition={{ duration: 2, repeat: Infinity }}
              className="absolute inset-0 rounded-full bg-sky-400/20 blur-md"
            />
          </motion.button>
        )}
      </AnimatePresence>

      <AnimatePresence>
        {isOpen && (
          <motion.div
            initial={{ opacity: 0, y: 20, scale: 0.95 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: -20, scale: 0.95 }}
            transition={{ duration: 0.2 }}
            className="fixed bottom-6 right-6 z-50 w-96 h-[600px] rounded-2xl bg-gradient-to-br from-slate-900/95 to-slate-950/95 border border-sky-500/30 backdrop-blur-xl shadow-[0_0_50px_rgba(14,165,233,0.3)] flex flex-col overflow-hidden"
          >
            <div className="p-4 border-b border-sky-500/20 bg-gradient-to-br from-slate-800/50 to-slate-900/50">
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-3">
                  <motion.div
                    animate={{ rotate: 360 }}
                    transition={{ duration: 8, repeat: Infinity, ease: "linear" }}
                    className="p-2 rounded-lg bg-gradient-to-br from-sky-500/20 to-purple-500/20 border border-sky-400/30"
                  >
                    <Bot className="w-5 h-5 text-sky-300" />
                  </motion.div>
                  <div>
                    <h3 className="text-sky-100 font-semibold">Security Assistant</h3>
                    <div className="flex items-center gap-1.5">
                      <motion.div
                        animate={{ opacity: [0.3, 1, 0.3] }}
                        transition={{ duration: 2, repeat: Infinity }}
                        className="w-1.5 h-1.5 rounded-full bg-emerald-400"
                      />
                      <span className="text-emerald-400 text-xs">Online</span>
                    </div>
                  </div>
                </div>
                <button
                  onClick={() => setIsOpen(false)}
                  className="p-2 rounded-lg hover:bg-red-500/20 border border-transparent hover:border-red-400/30 transition-all duration-200"
                >
                  <X className="w-5 h-5 text-sky-300" />
                </button>
              </div>
            </div>

            <div className="flex-1 overflow-y-auto p-4 space-y-4 scrollbar-thin scrollbar-thumb-sky-500/20 scrollbar-track-transparent">
              {messages.map((message) => (
                <motion.div
                  key={message.id}
                  initial={{ opacity: 0, y: 10 }}
                  animate={{ opacity: 1, y: 0 }}
                  className={`flex items-start gap-3 ${message.sender === 'user' ? 'flex-row-reverse' : ''}`}
                >
                  <div className={`p-2 rounded-lg border ${message.sender === 'ai' ? 'bg-purple-500/20 border-purple-400/30' : 'bg-sky-500/20 border-sky-400/30'}`}>
                    {message.sender === 'ai' ? <Bot className="w-4 h-4 text-purple-300" /> : <User className="w-4 h-4 text-sky-300" />}
                  </div>
                  <div className={`flex-1 ${message.sender === 'user' ? 'text-right' : ''}`}>
                    <div className={`inline-block max-w-[85%] p-3 rounded-lg ${message.sender === 'ai' ? 'bg-gradient-to-br from-slate-800/60 to-slate-900/60 border border-sky-400/20 text-sky-100' : 'bg-gradient-to-br from-sky-500/30 to-purple-500/30 border border-sky-400/30 text-sky-50'}`}>
                      <p className="text-sm leading-relaxed whitespace-pre-wrap">{message.text}</p>
                    </div>
                    <span className="text-sky-500/60 text-xs mt-1 inline-block">
                      {message.timestamp.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' })}
                    </span>
                  </div>
                </motion.div>
              ))}

              {isTyping && (
                <motion.div initial={{ opacity: 0 }} animate={{ opacity: 1 }} className="flex items-start gap-3">
                  <div className="p-2 rounded-lg border bg-purple-500/20 border-purple-400/30">
                    <Bot className="w-4 h-4 text-purple-300" />
                  </div>
                  <div className="p-3 rounded-lg bg-gradient-to-br from-slate-800/60 to-slate-900/60 border border-sky-400/20">
                    <div className="flex items-center gap-1">
                      {[...Array(3)].map((_, i) => (
                        <motion.div
                          key={i}
                          animate={{ opacity: [0.3, 1, 0.3] }}
                          transition={{ duration: 1, repeat: Infinity, delay: i * 0.2 }}
                          className="w-2 h-2 rounded-full bg-sky-400"
                        />
                      ))}
                    </div>
                  </div>
                </motion.div>
              )}
              <div ref={messagesEndRef} />
            </div>

            <div className="p-4 border-t border-sky-500/20 bg-gradient-to-br from-slate-800/50 to-slate-900/50">
              <div className="flex items-end gap-2">
                <div className="flex-1">
                  <input
                    ref={inputRef}
                    type="text"
                    value={inputText}
                    onChange={(e) => setInputText(e.target.value)}
                    onKeyPress={handleKeyPress}
                    placeholder="Ask about your security..."
                    className="w-full px-4 py-3 rounded-lg bg-slate-800/60 border border-sky-500/20 text-sky-100 text-sm placeholder:text-sky-400/40 focus:outline-none focus:border-sky-400/40 transition-colors duration-200"
                  />
                </div>
                {SECURITY_SPEECH_WIRED ? null : null}
                <button
                  type="button"
                  onClick={handleMicClick}
                  className="p-3 rounded-lg bg-slate-800/60 border border-sky-500/20 hover:border-sky-400/40 transition-all duration-200"
                  aria-label="Security mic"
                  title="Security mic"
                >
                  <Mic className="w-5 h-5 text-sky-300" />
                </button>
                <button
                  onClick={handleSend}
                  disabled={!inputText.trim()}
                  className="p-3 rounded-lg bg-gradient-to-br from-sky-500/30 to-purple-500/30 border border-sky-400/30 hover:from-sky-500/40 hover:to-purple-500/40 disabled:opacity-40 disabled:cursor-not-allowed transition-all duration-200"
                >
                  <Send className="w-5 h-5 text-sky-300" />
                </button>
              </div>
              {voiceNote ? <p className="text-sky-300/70 text-[10px] mt-2">{voiceNote}</p> : null}
              <p className="text-sky-500/40 text-xs mt-2">Press Enter to send • Powered by AI</p>
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </>
  );
}
