import { MessageSquare, Send, Sparkles } from 'lucide-react';
import { useState } from 'react';

const chatHistory = [
  { id: '1', role: 'user', message: 'What should I watch tonight?' },
  { id: '2', role: 'assistant', message: 'Based on your recent viewing history, I recommend "Blade Runner 2049". You enjoyed similar sci-fi films, and it\'s available in 4K on your main server.' },
  { id: '3', role: 'user', message: 'Any alternatives?' },
  { id: '4', role: 'assistant', message: 'Sure! Here are some alternatives:\n\n1. "Arrival" - Thought-provoking sci-fi\n2. "Ex Machina" - AI thriller\n3. "Interstellar" - Epic space drama\n\nAll are highly rated and match your preferences.' },
];

export function AIChatPage() {
  const [message, setMessage] = useState('');

  return (
    <div className="space-y-6 h-[calc(100vh-12rem)]">
      {/* Header */}
      <div className="flex items-center gap-4">
        <div className="p-3 rounded-xl bg-gradient-to-r from-purple-500 to-pink-500">
          <MessageSquare size={28} className="text-white" />
        </div>
        <div>
          <h1 className="text-slate-100 text-3xl">AI Chat</h1>
          <p className="text-slate-400">Get personalized recommendations and assistance</p>
        </div>
      </div>

      {/* Chat Container */}
      <div className="flex flex-col h-full bg-gradient-to-br from-slate-900/80 to-slate-800/80 backdrop-blur-xl rounded-2xl border border-slate-700/50">
        {/* Messages */}
        <div className="flex-1 overflow-y-auto p-6 space-y-4">
          {chatHistory.map((chat) => (
            <div
              key={chat.id}
              className={`flex ${chat.role === 'user' ? 'justify-end' : 'justify-start'}`}
            >
              <div
                className={`max-w-2xl rounded-xl p-4 ${
                  chat.role === 'user'
                    ? 'bg-gradient-to-r from-cyan-500 to-purple-500 text-white'
                    : 'bg-slate-950/50 border border-slate-700/50 text-slate-200'
                }`}
              >
                {chat.role === 'assistant' && (
                  <div className="flex items-center gap-2 mb-2">
                    <Sparkles size={14} className="text-purple-400" />
                    <span className="text-xs text-purple-400">AI Assistant</span>
                  </div>
                )}
                <p className="whitespace-pre-line">{chat.message}</p>
              </div>
            </div>
          ))}
        </div>

        {/* Input */}
        <div className="border-t border-slate-700/50 p-4">
          <div className="flex gap-3">
            <input
              type="text"
              value={message}
              onChange={(e) => setMessage(e.target.value)}
              placeholder="Ask me anything about your media library..."
              className="flex-1 bg-slate-950/50 text-slate-200 placeholder:text-slate-500 rounded-xl border border-slate-700 focus:border-cyan-500/50 focus:ring-2 focus:ring-cyan-500/20 px-4 py-3 outline-none transition-all"
            />
            <button className="p-3 rounded-xl bg-gradient-to-r from-purple-500 to-pink-500 text-white hover:shadow-lg hover:shadow-purple-500/30 transition-all">
              <Send size={20} />
            </button>
          </div>

          {/* Quick Prompts */}
          <div className="flex flex-wrap gap-2 mt-3">
            {[
              'What should I watch?',
              'Find action movies',
              'Create a playlist',
              'Show trending',
            ].map((prompt) => (
              <button
                key={prompt}
                onClick={() => setMessage(prompt)}
                className="px-3 py-1.5 rounded-lg bg-slate-800/50 text-slate-300 hover:text-white hover:bg-slate-700 transition-all text-sm"
              >
                {prompt}
              </button>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}
