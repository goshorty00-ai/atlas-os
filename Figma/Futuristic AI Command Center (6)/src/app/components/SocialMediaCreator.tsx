import { useState } from "react";
import { motion } from "motion/react";
import {
  Image,
  Video,
  Wand2,
  Hash,
  Send,
  Calendar,
  Sparkles,
  Type,
  Palette,
  Music,
  Mic,
  Instagram,
  Twitter,
  Facebook,
  Linkedin,
  Youtube,
  CheckCircle2,
  Clock,
  TrendingUp,
} from "lucide-react";

interface Platform {
  id: string;
  name: string;
  icon: React.ReactNode;
  enabled: boolean;
  status: "ready" | "posting" | "posted";
}

export function SocialMediaCreator() {
  const [platforms, setPlatforms] = useState<Platform[]>([
    { id: "instagram", name: "Instagram", icon: <Instagram className="w-5 h-5" />, enabled: true, status: "ready" },
    { id: "twitter", name: "Twitter", icon: <Twitter className="w-5 h-5" />, enabled: true, status: "ready" },
    { id: "facebook", name: "Facebook", icon: <Facebook className="w-5 h-5" />, enabled: true, status: "ready" },
    { id: "linkedin", name: "LinkedIn", icon: <Linkedin className="w-5 h-5" />, enabled: false, status: "ready" },
    { id: "youtube", name: "YouTube", icon: <Youtube className="w-5 h-5" />, enabled: false, status: "ready" },
  ]);

  const [caption, setCaption] = useState("");
  const [generatedImage, setGeneratedImage] = useState<string | null>(null);
  const [isGenerating, setIsGenerating] = useState(false);
  const [activeTab, setActiveTab] = useState<"compose" | "preview">("compose");
  const [engagementScore, setEngagementScore] = useState(87);

  const togglePlatform = (id: string) => {
    setPlatforms(platforms.map(p => 
      p.id === id ? { ...p, enabled: !p.enabled } : p
    ));
  };

  const generateCaption = () => {
    const captions = [
      "Innovating the future with AI-powered solutions 🚀 #TechInnovation #AIRevolution",
      "Breaking barriers with cutting-edge technology ⚡ #FutureTech #Innovation",
      "Transforming ideas into reality with Atlas AI 🌟 #AI #Technology #Future",
      "Next-generation solutions for tomorrow's challenges 💡 #Innovation #Tech",
    ];
    setCaption(captions[Math.floor(Math.random() * captions.length)]);
  };

  const generateImage = () => {
    setIsGenerating(true);
    setTimeout(() => {
      setGeneratedImage("https://images.unsplash.com/photo-1677442136019-21780ecad995?w=800&q=80");
      setIsGenerating(false);
    }, 2000);
  };

  const generateHashtags = () => {
    const hashtags = " #AI #Innovation #Tech #Future #Digital #MachineLearning #Automation";
    setCaption(prev => prev + hashtags);
  };

  const postToAll = () => {
    platforms.forEach((platform, index) => {
      if (platform.enabled) {
        setTimeout(() => {
          setPlatforms(prev => prev.map(p =>
            p.id === platform.id ? { ...p, status: "posting" } : p
          ));
          
          setTimeout(() => {
            setPlatforms(prev => prev.map(p =>
              p.id === platform.id ? { ...p, status: "posted" } : p
            ));
          }, 1500);
        }, index * 500);
      }
    });
  };

  const aiTools = [
    { id: "image", name: "AI Image Gen", icon: <Image className="w-4 h-4" />, color: "cyan", action: generateImage },
    { id: "video", name: "AI Video Gen", icon: <Video className="w-4 h-4" />, color: "orange", action: () => {} },
    { id: "caption", name: "Caption AI", icon: <Type className="w-4 h-4" />, color: "cyan", action: generateCaption },
    { id: "hashtag", name: "Hashtag AI", icon: <Hash className="w-4 h-4" />, color: "orange", action: generateHashtags },
    { id: "enhance", name: "Enhance Text", icon: <Wand2 className="w-4 h-4" />, color: "cyan", action: () => {} },
    { id: "palette", name: "Color Palette", icon: <Palette className="w-4 h-4" />, color: "orange", action: () => {} },
    { id: "music", name: "Music Gen", icon: <Music className="w-4 h-4" />, color: "cyan", action: () => {} },
    { id: "voice", name: "Voice Over", icon: <Mic className="w-4 h-4" />, color: "orange", action: () => {} },
  ];

  return (
    <div className="flex-1 flex overflow-hidden">
      {/* Left Panel - Creation Tools */}
      <div className="flex-1 flex flex-col bg-[#0b0f14]/20 overflow-hidden">
        {/* Header */}
        <div className="p-6 border-b border-cyan-500/10">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-4">
              <motion.div
                className="relative"
                animate={{ rotate: 360 }}
                transition={{ duration: 8, repeat: Infinity, ease: "linear" }}
              >
                <Sparkles className="w-8 h-8 text-cyan-400" />
                <motion.div
                  className="absolute inset-0 rounded-full"
                  animate={{ scale: [1, 1.5], opacity: [0.5, 0] }}
                  transition={{ duration: 2, repeat: Infinity, ease: "easeOut" }}
                >
                  <Sparkles className="w-8 h-8 text-orange-400" />
                </motion.div>
              </motion.div>
              <div>
                <h2 className="text-xl font-mono tracking-wider text-cyan-400">
                  AI CONTENT CREATOR
                </h2>
                <p className="text-xs text-slate-500 font-mono mt-1">
                  MULTI-PLATFORM POST GENERATOR
                </p>
              </div>
            </div>
            
            {/* Engagement Predictor */}
            <div className="flex items-center gap-3 bg-[#0f1419] border border-green-500/30 rounded-lg px-4 py-2">
              <TrendingUp className="w-5 h-5 text-green-400" />
              <div>
                <div className="text-xs text-slate-500 font-mono">
                  PREDICTED ENGAGEMENT
                </div>
                <div className="text-lg font-mono text-green-400">
                  {engagementScore}%
                </div>
              </div>
            </div>
          </div>
        </div>

        {/* AI Tools Grid */}
        <div className="p-6 border-b border-cyan-500/10">
          <div className="text-xs font-mono text-slate-400 uppercase tracking-wider mb-3">
            AI Creative Tools
          </div>
          <div className="grid grid-cols-4 gap-3">
            {aiTools.map((tool, index) => (
              <motion.button
                key={tool.id}
                initial={{ opacity: 0, y: 20 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ delay: index * 0.05 }}
                onClick={tool.action}
                disabled={isGenerating}
                className={`relative p-4 rounded-lg border ${
                  tool.color === "cyan"
                    ? "bg-cyan-500/5 border-cyan-500/30 hover:bg-cyan-500/10"
                    : "bg-orange-500/5 border-orange-500/30 hover:bg-orange-500/10"
                } transition-all group ${isGenerating ? "opacity-50 cursor-not-allowed" : ""}`}
              >
                <div className={`flex items-center justify-center mb-2 ${
                  tool.color === "cyan" ? "text-cyan-400" : "text-orange-400"
                }`}>
                  {tool.icon}
                </div>
                <div className={`text-[10px] font-mono uppercase tracking-wider text-center ${
                  tool.color === "cyan" ? "text-cyan-400/80" : "text-orange-400/80"
                }`}>
                  {tool.name}
                </div>
                
                {/* Hover glow */}
                <motion.div
                  className={`absolute inset-0 rounded-lg opacity-0 group-hover:opacity-100 transition-opacity ${
                    tool.color === "cyan"
                      ? "shadow-[0_0_20px_rgba(34,211,238,0.2)]"
                      : "shadow-[0_0_20px_rgba(249,115,22,0.2)]"
                  }`}
                />
              </motion.button>
            ))}
          </div>
        </div>

        {/* Content Creation Area */}
        <div className="flex-1 overflow-y-auto p-6 scrollbar-hide">
          {/* Caption Input */}
          <div className="mb-6">
            <div className="flex items-center justify-between mb-2">
              <label className="text-xs font-mono text-slate-400 uppercase tracking-wider">
                Post Caption
              </label>
              <span className="text-xs font-mono text-slate-600">
                {caption.length} / 2200
              </span>
            </div>
            <textarea
              value={caption}
              onChange={(e) => setCaption(e.target.value)}
              placeholder="Enter your caption or use AI to generate..."
              className="w-full h-32 bg-[#0f1419] border border-cyan-500/20 rounded-lg p-4 text-slate-200 text-sm resize-none outline-none focus:border-cyan-500/40 transition-colors"
            />
          </div>

          {/* Media Preview */}
          <div className="mb-6">
            <label className="text-xs font-mono text-slate-400 uppercase tracking-wider mb-2 block">
              Generated Media
            </label>
            <div className="relative aspect-video bg-[#0f1419] border border-cyan-500/20 rounded-lg overflow-hidden">
              {isGenerating ? (
                <div className="absolute inset-0 flex items-center justify-center">
                  <div className="text-center">
                    <motion.div
                      animate={{ rotate: 360 }}
                      transition={{ duration: 2, repeat: Infinity, ease: "linear" }}
                      className="w-12 h-12 border-4 border-cyan-500/20 border-t-cyan-400 rounded-full mx-auto mb-3"
                    />
                    <div className="text-sm font-mono text-cyan-400">
                      AI GENERATING...
                    </div>
                  </div>
                </div>
              ) : generatedImage ? (
                <img
                  src={generatedImage}
                  alt="Generated"
                  className="w-full h-full object-cover"
                />
              ) : (
                <div className="absolute inset-0 flex items-center justify-center">
                  <div className="text-center">
                    <Image className="w-12 h-12 text-slate-600 mx-auto mb-2" />
                    <div className="text-sm font-mono text-slate-600">
                      NO MEDIA GENERATED
                    </div>
                  </div>
                </div>
              )}
            </div>
          </div>

          {/* Schedule Options */}
          <div className="mb-6">
            <label className="text-xs font-mono text-slate-400 uppercase tracking-wider mb-2 block">
              Schedule Post
            </label>
            <div className="flex gap-3">
              <button className="flex-1 flex items-center justify-center gap-2 bg-cyan-500/10 border border-cyan-500/30 rounded-lg px-4 py-3 hover:bg-cyan-500/20 transition-all">
                <Send className="w-4 h-4 text-cyan-400" />
                <span className="text-sm font-mono text-cyan-400">POST NOW</span>
              </button>
              <button className="flex-1 flex items-center justify-center gap-2 bg-orange-500/10 border border-orange-500/30 rounded-lg px-4 py-3 hover:bg-orange-500/20 transition-all">
                <Calendar className="w-4 h-4 text-orange-400" />
                <span className="text-sm font-mono text-orange-400">SCHEDULE</span>
              </button>
            </div>
          </div>

          {/* AI Suggestions */}
          <motion.div
            initial={{ opacity: 0, y: 20 }}
            animate={{ opacity: 1, y: 0 }}
            className="rounded-lg border border-cyan-500/20 bg-cyan-500/5 p-4"
          >
            <div className="flex items-start gap-3">
              <motion.div
                animate={{ rotate: [0, 360] }}
                transition={{ duration: 4, repeat: Infinity, ease: "linear" }}
              >
                <Sparkles className="w-5 h-5 text-cyan-400" />
              </motion.div>
              <div className="flex-1">
                <div className="text-sm font-mono text-cyan-400 uppercase tracking-wider mb-2">
                  AI Suggestions
                </div>
                <ul className="space-y-1 text-xs text-slate-400 font-mono">
                  <li>• Add 3-5 trending hashtags for better reach</li>
                  <li>• Peak engagement time: 6:00 PM - 9:00 PM</li>
                  <li>• Consider adding a call-to-action</li>
                  <li>• Video content gets 2.5x more engagement</li>
                </ul>
              </div>
            </div>
          </motion.div>
        </div>
      </div>

      {/* Right Panel - Platform Selection & Preview */}
      <div className="w-96 flex flex-col bg-[#0b0f14]/40 border-l border-cyan-500/10">
        {/* Tabs */}
        <div className="flex border-b border-cyan-500/10">
          <button
            onClick={() => setActiveTab("compose")}
            className={`flex-1 px-4 py-3 text-xs font-mono uppercase tracking-wider transition-colors ${
              activeTab === "compose"
                ? "text-cyan-400 border-b-2 border-cyan-400"
                : "text-slate-500 hover:text-slate-400"
            }`}
          >
            Platforms
          </button>
          <button
            onClick={() => setActiveTab("preview")}
            className={`flex-1 px-4 py-3 text-xs font-mono uppercase tracking-wider transition-colors ${
              activeTab === "preview"
                ? "text-cyan-400 border-b-2 border-cyan-400"
                : "text-slate-500 hover:text-slate-400"
            }`}
          >
            Preview
          </button>
        </div>

        {activeTab === "compose" ? (
          <>
            {/* Platform Selection */}
            <div className="flex-1 overflow-y-auto p-4 scrollbar-hide">
              <div className="text-xs font-mono text-slate-400 uppercase tracking-wider mb-3">
                Select Platforms
              </div>
              <div className="space-y-3">
                {platforms.map((platform, index) => (
                  <motion.div
                    key={platform.id}
                    initial={{ opacity: 0, x: 20 }}
                    animate={{ opacity: 1, x: 0 }}
                    transition={{ delay: index * 0.05 }}
                    onClick={() => togglePlatform(platform.id)}
                    className={`relative p-4 rounded-lg border cursor-pointer transition-all ${
                      platform.enabled
                        ? "bg-cyan-500/10 border-cyan-500/30"
                        : "bg-slate-900/20 border-slate-700/30"
                    }`}
                  >
                    <div className="flex items-center justify-between">
                      <div className="flex items-center gap-3">
                        <div className={`${
                          platform.enabled ? "text-cyan-400" : "text-slate-600"
                        }`}>
                          {platform.icon}
                        </div>
                        <span className={`text-sm font-mono ${
                          platform.enabled ? "text-cyan-400" : "text-slate-600"
                        }`}>
                          {platform.name}
                        </span>
                      </div>
                      
                      {/* Status Indicator */}
                      <div className="flex items-center gap-2">
                        {platform.status === "posted" && (
                          <CheckCircle2 className="w-4 h-4 text-green-400" />
                        )}
                        {platform.status === "posting" && (
                          <motion.div
                            animate={{ rotate: 360 }}
                            transition={{ duration: 1, repeat: Infinity, ease: "linear" }}
                          >
                            <Clock className="w-4 h-4 text-orange-400" />
                          </motion.div>
                        )}
                        <div
                          className={`w-3 h-3 rounded-full ${
                            platform.enabled ? "bg-cyan-400" : "bg-slate-600"
                          }`}
                        />
                      </div>
                    </div>
                  </motion.div>
                ))}
              </div>

              {/* Post to All Button */}
              <motion.button
                onClick={postToAll}
                whileHover={{ scale: 1.02 }}
                whileTap={{ scale: 0.98 }}
                className="w-full mt-6 p-4 bg-gradient-to-r from-cyan-500/20 to-orange-500/20 border border-cyan-500/30 rounded-lg hover:border-cyan-500/50 transition-all group"
              >
                <div className="flex items-center justify-center gap-3">
                  <Send className="w-5 h-5 text-cyan-400 group-hover:text-cyan-300" />
                  <span className="text-sm font-mono text-cyan-400 uppercase tracking-wider group-hover:text-cyan-300">
                    Post to All Platforms
                  </span>
                </div>
              </motion.button>
            </div>
          </>
        ) : (
          <>
            {/* Platform Preview */}
            <div className="flex-1 overflow-y-auto p-4 scrollbar-hide">
              <div className="text-xs font-mono text-slate-400 uppercase tracking-wider mb-3">
                Platform Preview
              </div>
              
              {/* Instagram Preview */}
              <div className="mb-4">
                <div className="flex items-center gap-2 mb-2">
                  <Instagram className="w-4 h-4 text-cyan-400" />
                  <span className="text-xs font-mono text-cyan-400">Instagram</span>
                </div>
                <div className="bg-[#0f1419] border border-cyan-500/20 rounded-lg p-3">
                  <div className="flex items-center gap-2 mb-3">
                    <div className="w-8 h-8 rounded-full bg-gradient-to-br from-cyan-500 to-orange-500" />
                    <div className="text-xs font-mono text-slate-300">atlas_ai</div>
                  </div>
                  {generatedImage && (
                    <img
                      src={generatedImage}
                      alt="Preview"
                      className="w-full aspect-square object-cover rounded mb-2"
                    />
                  )}
                  <div className="text-xs text-slate-400 line-clamp-3">
                    {caption || "Your caption will appear here..."}
                  </div>
                </div>
              </div>

              {/* Twitter Preview */}
              <div className="mb-4">
                <div className="flex items-center gap-2 mb-2">
                  <Twitter className="w-4 h-4 text-cyan-400" />
                  <span className="text-xs font-mono text-cyan-400">Twitter</span>
                </div>
                <div className="bg-[#0f1419] border border-cyan-500/20 rounded-lg p-3">
                  <div className="flex items-center gap-2 mb-3">
                    <div className="w-8 h-8 rounded-full bg-gradient-to-br from-cyan-500 to-orange-500" />
                    <div>
                      <div className="text-xs font-mono text-slate-300">Atlas AI</div>
                      <div className="text-[10px] text-slate-600">@atlas_ai</div>
                    </div>
                  </div>
                  <div className="text-xs text-slate-400 mb-2">
                    {caption || "Your tweet will appear here..."}
                  </div>
                  {generatedImage && (
                    <img
                      src={generatedImage}
                      alt="Preview"
                      className="w-full aspect-video object-cover rounded"
                    />
                  )}
                </div>
              </div>

              {/* Analytics Preview */}
              <motion.div
                initial={{ opacity: 0, y: 20 }}
                animate={{ opacity: 1, y: 0 }}
                className="rounded-lg border border-orange-500/20 bg-orange-500/5 p-3"
              >
                <div className="text-xs font-mono text-orange-400 uppercase tracking-wider mb-2">
                  Predicted Analytics
                </div>
                <div className="grid grid-cols-3 gap-2 text-center">
                  <div>
                    <div className="text-lg font-mono text-cyan-400">2.5K</div>
                    <div className="text-[10px] text-slate-500 font-mono">Likes</div>
                  </div>
                  <div>
                    <div className="text-lg font-mono text-orange-400">450</div>
                    <div className="text-[10px] text-slate-500 font-mono">Shares</div>
                  </div>
                  <div>
                    <div className="text-lg font-mono text-green-400">180</div>
                    <div className="text-[10px] text-slate-500 font-mono">Comments</div>
                  </div>
                </div>
              </motion.div>
            </div>
          </>
        )}
      </div>
    </div>
  );
}
