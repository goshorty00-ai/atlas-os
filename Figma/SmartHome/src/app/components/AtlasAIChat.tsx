// ATLAS AI Chat Component - Premium AI assistant interface

import { motion, AnimatePresence } from "motion/react";
import { Send, Bot, User, Zap, Clock, Brain, X, Minimize2, Maximize2, Settings } from "lucide-react";
import { useState, useRef, useEffect } from "react";
import { useAtlasAI, type AIMessage } from "../ai/useAtlasAI";
import { AIProviderSettings } from "./AIProviderSettings";

interface AtlasAIChatProps {
  isOpen: boolean;
  onClose: () => void;
  className?: string;
}

export function AtlasAIChat({ isOpen, onClose, className = "" }: AtlasAIChatProps) {
  const [input, setInput] = useState("");
  const [isMinimized, setIsMinimized] = useState(false);
  const [showSettings, setShowSettings] = useState(false);
  const messagesEndRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  
  const {
    messages,
    isProcessing,
    error,
    context,
    sendQuery,
    executeAIAction,
    clearConversation,
    quickActions,
    isReady,
    hasMessages
  } = useAtlasAI();

  // Get provider status (if available)
  const [providerStatus, setProviderStatus] = useState<string>('');

  useEffect(() => {
    if (context?.providerConfig) {
      const summary = context.providerConfig;
      const enabled = Object.entries(summary)
        .filter(([_, config]: any) => config.enabled)
        .map(([provider]) => provider);
      setProviderStatus(`Active: ${enabled.join(', ')}`);
    }
  }, [context]);

  // Auto-scroll to bottom
  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages]);

  // Focus input when opened
  useEffect(() => {
    if (isOpen && !isMinimized) {
      setTimeout(() => inputRef.current?.focus(), 100);
    }
  }, [isOpen, isMinimized]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!input.trim() || isProcessing) return;
    
    const query = input.trim();
    setInput("");
    await sendQuery(query);
  };

  const handleQuickAction = async (action: () => Promise<void>) => {
    await action();
  };

  const handleActionClick = async (actionId: string, payload: any) => {
    await executeAIAction(actionId, payload);
  };

  if (!isOpen) return null;

  return (
    <motion.div
      className={`fixed bottom-4 right-4 z-50 ${className}`}
      initial={{ opacity: 0, scale: 0.9, y: 20 }}
      animate={{ opacity: 1, scale: 1, y: 0 }}
      exit={{ opacity: 0, scale: 0.9, y: 20 }}
    >
      <div
        className={`rounded-2xl overflow-hidden transition-all duration-300 ${
          isMinimized ? 'w-80 h-16' : 'w-96 h-[600px]'
        }`}
        style={{
          background: "rgba(5,10,18,0.95)",
          border: "1px solid rgba(0,212,255,0.3)",
          backdropFilter: "blur(20px)",
          boxShadow: "0 20px 40px rgba(0,212,255,0.1)"
        }}
      >
        {/* Header */}
        <div className="flex items-center justify-between p-4 border-b border-cyan-400/20">
          <div className="flex items-center gap-3">
            <div className="w-8 h-8 rounded-full flex items-center justify-center"
              style={{ background: "linear-gradient(135deg, #00d4ff, #0066ff)" }}>
              <Brain className="w-4 h-4 text-white" />
            </div>
            <div>
              <h3 className="text-sm font-semibold text-white">ATLAS AI</h3>
              <p className="text-xs text-cyan-400/60">
                {isReady ? (providerStatus || 'Ready') : 'Initializing...'}
              </p>
            </div>
          </div>
          <div className="flex items-center gap-2">
            <button
              onClick={() => setShowSettings(true)}
              className="w-8 h-8 rounded-lg flex items-center justify-center hover:bg-white/10 transition-colors"
              title="Provider Settings"
            >
              <Settings className="w-4 h-4 text-white/60" />
            </button>
            <button
              onClick={() => setIsMinimized(!isMinimized)}
              className="w-8 h-8 rounded-lg flex items-center justify-center hover:bg-white/10 transition-colors"
            >
              {isMinimized ? (
                <Maximize2 className="w-4 h-4 text-white/60" />
              ) : (
                <Minimize2 className="w-4 h-4 text-white/60" />
              )}
            </button>
            <button
              onClick={onClose}
              className="w-8 h-8 rounded-lg flex items-center justify-center hover:bg-white/10 transition-colors"
            >
              <X className="w-4 h-4 text-white/60" />
            </button>
          </div>
        </div>

        {!isMinimized && (
          <>
            {/* Messages */}
            <div className="flex-1 overflow-y-auto p-4 space-y-4" style={{ height: "calc(100% - 140px)" }}>
              {!hasMessages && (
                <div className="text-center py-8">
                  <div className="w-16 h-16 rounded-full mx-auto mb-4 flex items-center justify-center"
                    style={{ background: "rgba(0,212,255,0.1)" }}>
                    <Bot className="w-8 h-8 text-cyan-400" />
                  </div>
                  <h4 className="text-white font-medium mb-2">Welcome to ATLAS AI</h4>
                  <p className="text-sm text-white/60 mb-4">
                    I'm your smart home assistant. I can help you control devices, check status, and manage your home.
                  </p>
                  
                  {/* Quick Actions */}
                  <div className="space-y-2">
                    <p className="text-xs text-cyan-400/60 uppercase tracking-wider">Quick Actions</p>
                    <div className="grid grid-cols-1 gap-2">
                      {[
                        { label: "Device Status", action: quickActions.getAllDeviceStatus },
                        { label: "Offline Devices", action: quickActions.getOfflineDevices },
                        { label: "Turn Off Lights", action: quickActions.turnOffAllLights }
                      ].map((item, idx) => (
                        <button
                          key={idx}
                          onClick={() => handleQuickAction(item.action)}
                          className="px-3 py-2 rounded-lg text-xs font-medium transition-all hover:scale-105"
                          style={{
                            background: "rgba(0,212,255,0.1)",
                            border: "1px solid rgba(0,212,255,0.2)",
                            color: "#00d4ff"
                          }}
                        >
                          {item.label}
                        </button>
                      ))}
                    </div>
                  </div>
                </div>
              )}

              <AnimatePresence>
                {messages.map((message) => (
                  <MessageBubble
                    key={message.id}
                    message={message}
                    onActionClick={handleActionClick}
                  />
                ))}
              </AnimatePresence>

              {isProcessing && (
                <motion.div
                  initial={{ opacity: 0, y: 10 }}
                  animate={{ opacity: 1, y: 0 }}
                  className="flex items-center gap-3 p-3"
                >
                  <div className="w-8 h-8 rounded-full flex items-center justify-center"
                    style={{ background: "rgba(0,212,255,0.2)" }}>
                    <Bot className="w-4 h-4 text-cyan-400" />
                  </div>
                  <div className="flex items-center gap-2">
                    <div className="flex gap-1">
                      {[0, 1, 2].map(i => (
                        <motion.div
                          key={i}
                          className="w-2 h-2 rounded-full bg-cyan-400"
                          animate={{ opacity: [0.3, 1, 0.3] }}
                          transition={{ duration: 1.5, repeat: Infinity, delay: i * 0.2 }}
                        />
                      ))}
                    </div>
                    <span className="text-sm text-cyan-400/80">Thinking...</span>
                  </div>
                </motion.div>
              )}

              <div ref={messagesEndRef} />
            </div>

            {/* Input */}
            <form onSubmit={handleSubmit} className="p-4 border-t border-cyan-400/20">
              <div className="flex gap-3">
                <input
                  ref={inputRef}
                  type="text"
                  value={input}
                  onChange={(e) => setInput(e.target.value)}
                  placeholder="Ask ATLAS about your smart home..."
                  disabled={!isReady || isProcessing}
                  className="flex-1 px-4 py-3 rounded-xl text-sm text-white placeholder-white/40 border-0 outline-none"
                  style={{
                    background: "rgba(255,255,255,0.05)",
                    border: "1px solid rgba(255,255,255,0.1)"
                  }}
                />
                <button
                  type="submit"
                  disabled={!input.trim() || !isReady || isProcessing}
                  className="w-12 h-12 rounded-xl flex items-center justify-center transition-all disabled:opacity-50 disabled:cursor-not-allowed hover:scale-105"
                  style={{
                    background: input.trim() && isReady ? "linear-gradient(135deg, #00d4ff, #0066ff)" : "rgba(255,255,255,0.1)"
                  }}
                >
                  <Send className="w-4 h-4 text-white" />
                </button>
              </div>
            </form>
          </>
        )}
      </div>
      
      {/* Provider Settings Modal */}
      <AnimatePresence>
        {showSettings && <AIProviderSettings onClose={() => setShowSettings(false)} />}
      </AnimatePresence>
    </motion.div>
  );
}

function MessageBubble({ 
  message, 
  onActionClick 
}: { 
  message: AIMessage; 
  onActionClick: (actionId: string, payload: any) => void;
}) {
  const isUser = message.role === 'user';
  
  return (
    <motion.div
      initial={{ opacity: 0, y: 10 }}
      animate={{ opacity: 1, y: 0 }}
      className={`flex gap-3 ${isUser ? 'flex-row-reverse' : ''}`}
    >
      <div className={`w-8 h-8 rounded-full flex items-center justify-center flex-shrink-0 ${
        isUser ? 'bg-cyan-400/20' : 'bg-gradient-to-br from-cyan-400/20 to-blue-400/20'
      }`}>
        {isUser ? (
          <User className="w-4 h-4 text-cyan-400" />
        ) : (
          <Bot className="w-4 h-4 text-cyan-400" />
        )}
      </div>
      
      <div className={`flex-1 ${isUser ? 'text-right' : ''}`}>
        <div className={`inline-block max-w-[85%] p-3 rounded-2xl ${
          isUser 
            ? 'bg-gradient-to-r from-cyan-400/20 to-blue-400/20 text-white' 
            : 'bg-white/5 text-white/90'
        } ${isUser ? 'rounded-br-md' : 'rounded-bl-md'}`}>
          <p className="text-sm leading-relaxed">{message.content}</p>
          
          {/* Usage Info */}
          {!isUser && message.usage && (
            <div className="mt-2 pt-2 border-t border-white/10 flex items-center gap-3 text-xs text-white/40">
              <span title="Model used">{message.usage.model}</span>
              <span>•</span>
              <span title="Tokens used">{message.usage.totalTokens} tokens</span>
              <span>•</span>
              <span title="Estimated cost">${message.usage.estimatedCost.toFixed(6)}</span>
              {message.usage.estimated && (
                <span title="Estimated values">~</span>
              )}
            </div>
          )}
          
          {/* Structured Response */}
          {message.structured && (
            <div className="mt-3 pt-3 border-t border-white/10">
              {message.structured.type === 'device_list' && (
                <div className="text-xs text-white/60">
                  Device information retrieved
                </div>
              )}
            </div>
          )}
          
          {/* Action Buttons */}
          {message.actions && message.actions.length > 0 && (
            <div className="mt-3 flex flex-wrap gap-2">
              {message.actions.map((action, idx) => (
                <button
                  key={idx}
                  onClick={() => onActionClick(action.id, action.payload)}
                  className="px-3 py-1.5 rounded-lg text-xs font-medium transition-all hover:scale-105"
                  style={{
                    background: "rgba(0,212,255,0.2)",
                    border: "1px solid rgba(0,212,255,0.3)",
                    color: "#00d4ff"
                  }}
                >
                  <Zap className="w-3 h-3 inline mr-1" />
                  {action.label}
                </button>
              ))}
            </div>
          )}
        </div>
        
        <div className={`mt-1 text-xs text-white/30 ${isUser ? 'text-right' : ''}`}>
          <Clock className="w-3 h-3 inline mr-1" />
          {new Date(message.timestamp).toLocaleTimeString([], { 
            hour: '2-digit', 
            minute: '2-digit' 
          })}
        </div>
      </div>
    </motion.div>
  );
}