import { useState } from "react";
import { Bot, Send, Lightbulb, Wrench, Sparkles, HelpCircle } from "lucide-react";
import { Button } from "../components/ui/button";
import { Textarea } from "../components/ui/textarea";
import { Badge } from "../components/ui/badge";

const suggestions = [
  {
    icon: Lightbulb,
    title: "Create a Scene",
    prompt: "Create a relaxing evening scene with dim lights and soft music",
    category: "scenes",
  },
  {
    icon: Wrench,
    title: "Fix Device Issue",
    prompt: "My bedroom light is not responding, help me troubleshoot",
    category: "troubleshooting",
  },
  {
    icon: Sparkles,
    title: "Optimize Energy",
    prompt: "Suggest ways to reduce my energy consumption",
    category: "optimization",
  },
  {
    icon: HelpCircle,
    title: "Setup Help",
    prompt: "How do I add a new Philips Hue bulb to my system?",
    category: "setup",
  },
];

const conversationHistory = [
  {
    role: "assistant",
    message:
      "Hello! I'm your Smart Home AI Assistant. I can help you with device setup, troubleshooting, creating scenes, automations, and optimizing your smart home. What would you like to do today?",
    time: "Just now",
  },
];

export function AIAssistant() {
  const [input, setInput] = useState("");
  const [messages, setMessages] = useState(conversationHistory);

  const handleSendMessage = () => {
    if (!input.trim()) return;

    // Add user message
    const newMessages = [
      ...messages,
      {
        role: "user",
        message: input,
        time: "Just now",
      },
      {
        role: "assistant",
        message:
          "I understand you'd like help with that. Based on your request, I've analyzed your current setup and have some recommendations...",
        time: "Just now",
      },
    ];

    setMessages(newMessages);
    setInput("");
  };

  const handleSuggestionClick = (prompt: string) => {
    setInput(prompt);
  };

  return (
    <div className="min-h-screen bg-gradient-to-br from-gray-950 via-blue-950/20 to-black p-8">
      <div className="max-w-[1400px] mx-auto">
        <div className="mb-8">
          <h1 className="text-4xl font-bold text-transparent bg-gradient-to-r from-cyan-400 to-blue-500 bg-clip-text mb-2">
            AI Assistant
          </h1>
          <p className="text-gray-400">Your intelligent smart home companion</p>
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
          {/* Chat Interface */}
          <div className="lg:col-span-2">
            <div className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl overflow-hidden flex flex-col h-[calc(100vh-250px)]">
              {/* Chat Header */}
              <div className="p-4 border-b border-gray-800 flex items-center gap-3">
                <div className="p-2 bg-purple-500/20 rounded-lg">
                  <Bot className="w-6 h-6 text-purple-400" />
                </div>
                <div>
                  <div className="font-semibold text-gray-200">Smart Home AI</div>
                  <div className="flex items-center gap-2">
                    <div className="w-2 h-2 bg-green-500 rounded-full animate-pulse" />
                    <span className="text-xs text-gray-500">Online</span>
                  </div>
                </div>
              </div>

              {/* Messages */}
              <div className="flex-1 overflow-y-auto p-6 space-y-4">
                {messages.map((msg, index) => (
                  <div
                    key={index}
                    className={`flex ${msg.role === "user" ? "justify-end" : "justify-start"}`}
                  >
                    <div
                      className={`max-w-[80%] ${
                        msg.role === "user"
                          ? "bg-cyan-600 text-white"
                          : "bg-gray-800 text-gray-200"
                      } rounded-2xl px-4 py-3`}
                    >
                      <div className="text-sm">{msg.message}</div>
                      <div
                        className={`text-xs mt-1 ${
                          msg.role === "user" ? "text-cyan-200" : "text-gray-500"
                        }`}
                      >
                        {msg.time}
                      </div>
                    </div>
                  </div>
                ))}
              </div>

              {/* Input Area */}
              <div className="p-4 border-t border-gray-800">
                <div className="flex gap-2">
                  <Textarea
                    placeholder="Ask me anything about your smart home..."
                    value={input}
                    onChange={(e) => setInput(e.target.value)}
                    onKeyPress={(e) => {
                      if (e.key === "Enter" && !e.shiftKey) {
                        e.preventDefault();
                        handleSendMessage();
                      }
                    }}
                    className="bg-gray-900/50 border-gray-800 text-gray-100 resize-none min-h-[60px]"
                  />
                  <Button
                    onClick={handleSendMessage}
                    className="bg-cyan-600 hover:bg-cyan-700 h-[60px] px-6"
                  >
                    <Send className="w-5 h-5" />
                  </Button>
                </div>
                <div className="text-xs text-gray-500 mt-2">
                  Press Enter to send, Shift+Enter for new line
                </div>
              </div>
            </div>
          </div>

          {/* Suggestions & Capabilities */}
          <div className="space-y-6">
            {/* Quick Suggestions */}
            <div className="bg-gradient-to-br from-purple-900/20 to-gray-950/80 border border-purple-500/30 rounded-xl p-6">
              <h2 className="text-xl font-semibold text-purple-400 mb-4">Try Asking</h2>
              <div className="space-y-3">
                {suggestions.map((suggestion) => {
                  const Icon = suggestion.icon;
                  return (
                    <div
                      key={suggestion.title}
                      onClick={() => handleSuggestionClick(suggestion.prompt)}
                      className="p-3 bg-gray-900/50 rounded-lg border border-gray-800 hover:border-purple-500/30 transition-all cursor-pointer group"
                    >
                      <div className="flex items-start gap-3">
                        <Icon className="w-5 h-5 text-purple-400 mt-0.5 group-hover:text-purple-300" />
                        <div>
                          <div className="font-medium text-gray-200 mb-1 group-hover:text-purple-300">
                            {suggestion.title}
                          </div>
                          <div className="text-xs text-gray-500">{suggestion.prompt}</div>
                        </div>
                      </div>
                    </div>
                  );
                })}
              </div>
            </div>

            {/* Capabilities */}
            <div className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6">
              <h2 className="text-xl font-semibold text-cyan-400 mb-4">I Can Help With</h2>
              <div className="space-y-2">
                <Badge
                  variant="outline"
                  className="w-full justify-start border-gray-700 text-gray-300 py-2"
                >
                  🔧 Device troubleshooting
                </Badge>
                <Badge
                  variant="outline"
                  className="w-full justify-start border-gray-700 text-gray-300 py-2"
                >
                  ⚙️ Device setup & pairing
                </Badge>
                <Badge
                  variant="outline"
                  className="w-full justify-start border-gray-700 text-gray-300 py-2"
                >
                  ✨ Scene creation
                </Badge>
                <Badge
                  variant="outline"
                  className="w-full justify-start border-gray-700 text-gray-300 py-2"
                >
                  🤖 Automation suggestions
                </Badge>
                <Badge
                  variant="outline"
                  className="w-full justify-start border-gray-700 text-gray-300 py-2"
                >
                  💡 Energy optimization
                </Badge>
                <Badge
                  variant="outline"
                  className="w-full justify-start border-gray-700 text-gray-300 py-2"
                >
                  🔐 Security recommendations
                </Badge>
                <Badge
                  variant="outline"
                  className="w-full justify-start border-gray-700 text-gray-300 py-2"
                >
                  📊 Usage analytics
                </Badge>
                <Badge
                  variant="outline"
                  className="w-full justify-start border-gray-700 text-gray-300 py-2"
                >
                  🎯 Smart home tips
                </Badge>
              </div>
            </div>

            {/* Recent Topics */}
            <div className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6">
              <h2 className="text-xl font-semibold text-cyan-400 mb-4">Recent Topics</h2>
              <div className="space-y-2 text-sm">
                <div className="p-2 bg-gray-900/50 rounded text-gray-400 hover:text-gray-200 cursor-pointer">
                  Setting up bedroom automation
                </div>
                <div className="p-2 bg-gray-900/50 rounded text-gray-400 hover:text-gray-200 cursor-pointer">
                  Energy saving tips
                </div>
                <div className="p-2 bg-gray-900/50 rounded text-gray-400 hover:text-gray-200 cursor-pointer">
                  Camera placement advice
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
