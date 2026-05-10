import { useEffect, useState } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import { Brain, TrendingUp, Shield, AlertCircle } from 'lucide-react';

interface AnalysisMessage {
  id: string;
  message: string;
  type: 'info' | 'analysis' | 'alert';
  timestamp: Date;
}

const analysisTemplates = [
  { message: 'System baseline behavior established. Monitoring for anomalies.', type: 'info' as const },
  { message: 'New executable detected downloading from external server. Behavior analysis in progress.', type: 'analysis' as const },
  { message: 'Installer attempting elevated permissions. Risk score: Medium. Analyzing digital signature.', type: 'alert' as const },
  { message: 'Network traffic patterns nominal. No suspicious connections detected.', type: 'info' as const },
  { message: 'File integrity check complete. All system files verified - no tampering detected.', type: 'info' as const },
  { message: 'AI model detected unusual registry access pattern. Correlation analysis initiated.', type: 'analysis' as const },
  { message: 'Background service attempting network communication. Behavior matches known safe pattern.', type: 'info' as const },
  { message: 'Deep learning analysis complete: 0 threats, 2 suspicious behaviors flagged for review.', type: 'analysis' as const },
];

export function AIAnalysisPanel() {
  const [currentMessage, setCurrentMessage] = useState<AnalysisMessage | null>(null);
  const [typedText, setTypedText] = useState('');

  useEffect(() => {
    const showNewMessage = () => {
      const template = analysisTemplates[Math.floor(Math.random() * analysisTemplates.length)];
      const newMessage: AnalysisMessage = {
        id: `msg-${Date.now()}`,
        message: template.message,
        type: template.type,
        timestamp: new Date()
      };
      setCurrentMessage(newMessage);
      setTypedText('');

      // Typing effect
      let index = 0;
      const typingInterval = setInterval(() => {
        if (index < template.message.length) {
          setTypedText(template.message.slice(0, index + 1));
          index++;
        } else {
          clearInterval(typingInterval);
        }
      }, 30);

      return () => clearInterval(typingInterval);
    };

    showNewMessage();
    const interval = setInterval(showNewMessage, 8000);
    return () => clearInterval(interval);
  }, []);

  const getIcon = () => {
    if (!currentMessage) return Brain;
    switch (currentMessage.type) {
      case 'info': return Shield;
      case 'analysis': return TrendingUp;
      case 'alert': return AlertCircle;
    }
  };

  const getColor = () => {
    if (!currentMessage) return 'text-sky-400';
    switch (currentMessage.type) {
      case 'info': return 'text-emerald-400';
      case 'analysis': return 'text-sky-400';
      case 'alert': return 'text-amber-400';
    }
  };

  const Icon = getIcon();
  const iconColor = getColor();

  return (
    <div className="
      h-full
      relative p-4 rounded-xl
      bg-gradient-to-br from-purple-900/20 to-blue-900/20
      border border-purple-400/30
      backdrop-blur-sm
      overflow-hidden
    ">
      {/* Animated background */}
      <motion.div
        animate={{
          background: [
            'radial-gradient(circle at 0% 0%, rgba(147, 51, 234, 0.1) 0%, transparent 50%)',
            'radial-gradient(circle at 100% 100%, rgba(59, 130, 246, 0.1) 0%, transparent 50%)',
            'radial-gradient(circle at 0% 0%, rgba(147, 51, 234, 0.1) 0%, transparent 50%)',
          ]
        }}
        transition={{ duration: 10, repeat: Infinity }}
        className="absolute inset-0"
      />

      <div className="relative">
        <div className="flex items-center gap-3 mb-4">
          <motion.div
            animate={{ rotate: 360 }}
            transition={{ duration: 8, repeat: Infinity, ease: "linear" }}
            className="
              p-2 rounded-lg
              bg-gradient-to-br from-purple-500/20 to-blue-500/20
              border border-purple-400/40
            "
          >
            <Brain className="w-5 h-5 text-purple-300" />
          </motion.div>
          
          <div className="flex-1">
            <h3 className="text-purple-100 text-sm font-semibold">AI Analysis Engine</h3>
            <div className="flex items-center gap-2 mt-0.5">
              <motion.div
                animate={{ opacity: [0.3, 1, 0.3] }}
                transition={{ duration: 2, repeat: Infinity }}
                className="w-1.5 h-1.5 rounded-full bg-purple-400"
              />
              <span className="text-purple-300/60 text-xs">Neural network active</span>
            </div>
          </div>
        </div>

        <AnimatePresence mode="wait">
          {currentMessage && (
            <motion.div
              key={currentMessage.id}
              initial={{ opacity: 0, y: 10 }}
              animate={{ opacity: 1, y: 0 }}
              exit={{ opacity: 0, y: -10 }}
              className="
                p-4 rounded-lg
                bg-gradient-to-br from-slate-800/40 to-slate-900/40
                border border-sky-400/20
              "
            >
              <div className="flex items-start gap-3">
                <motion.div
                  animate={currentMessage.type === 'alert' ? { 
                    scale: [1, 1.1, 1],
                    rotate: [0, 5, -5, 0]
                  } : {}}
                  transition={{ duration: 1, repeat: Infinity }}
                >
                  <Icon className={`w-5 h-5 ${iconColor} mt-0.5`} />
                </motion.div>
                
                <div className="flex-1">
                  <p className="text-sky-100 text-sm leading-relaxed font-mono">
                    {typedText}
                    {typedText.length < currentMessage.message.length && (
                      <motion.span
                        animate={{ opacity: [0, 1, 0] }}
                        transition={{ duration: 0.8, repeat: Infinity }}
                        className="inline-block w-1.5 h-4 bg-sky-400 ml-0.5"
                      />
                    )}
                  </p>
                  
                  {typedText.length === currentMessage.message.length && (
                    <motion.div
                      initial={{ opacity: 0 }}
                      animate={{ opacity: 1 }}
                      className="flex items-center gap-2 mt-2"
                    >
                      <div className="h-px flex-1 bg-gradient-to-r from-sky-400/40 to-transparent" />
                      <span className="text-sky-500 text-xs font-mono">
                        {currentMessage.timestamp.toLocaleTimeString('en-US', { 
                          hour: '2-digit', 
                          minute: '2-digit',
                          second: '2-digit'
                        })}
                      </span>
                    </motion.div>
                  )}
                </div>
              </div>
            </motion.div>
          )}
        </AnimatePresence>

        {/* Neural network visualization */}
        <div className="mt-4 flex items-center justify-center gap-1 opacity-30">
          {[...Array(8)].map((_, i) => (
            <motion.div
              key={i}
              animate={{ 
                height: ['4px', '16px', '4px'],
                opacity: [0.3, 1, 0.3]
              }}
              transition={{ 
                duration: 1.5,
                repeat: Infinity,
                delay: i * 0.1
              }}
              className="w-0.5 bg-purple-400 rounded-full"
            />
          ))}
        </div>
      </div>
    </div>
  );
}