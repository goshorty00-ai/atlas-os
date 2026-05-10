// AI Provider Settings Component

import { useState, useEffect } from 'react';
import { motion } from 'motion/react';
import { Key, Check, X, AlertCircle, Cpu } from 'lucide-react';
import { 
  getProviderFactory, 
  saveProviderMode, 
  saveProviderCredentials,
  type ProviderMode 
} from '../ai';
import { ModelCatalogView } from './ModelCatalogView';
import { getSelectableModels, type ModelInfo } from '../ai/models/ModelCatalog';
import type { CostMode } from '../ai/AICore';

export function AIProviderSettings({ onClose }: { onClose: () => void }) {
  const factory = getProviderFactory();
  const [mode, setMode] = useState<ProviderMode>(factory.getMode());
  const [claudeKey, setClaudeKey] = useState('');
  const [geminiKey, setGeminiKey] = useState('');
  const [saved, setSaved] = useState(false);
  const [showModelCatalog, setShowModelCatalog] = useState(false);
  const [selectedClaudeModel, setSelectedClaudeModel] = useState<string>('claude-3-5-sonnet-20241022');
  const [selectedGeminiModel, setSelectedGeminiModel] = useState<string>('gemini-1.5-flash-latest');
  const [costMode, setCostMode] = useState<CostMode>('balanced');

  useEffect(() => {
    // Load existing keys (masked)
    const stored = localStorage.getItem('atlas_ai_claude_key');
    if (stored) {
      setClaudeKey('••••••••' + stored.slice(-4));
    }
    const storedGemini = localStorage.getItem('atlas_ai_gemini_key');
    if (storedGemini) {
      setGeminiKey('••••••••' + storedGemini.slice(-4));
    }
    
    // Load model preferences
    const claudeModel = localStorage.getItem('atlas_ai_claude_model');
    if (claudeModel) setSelectedClaudeModel(claudeModel);
    
    const geminiModel = localStorage.getItem('atlas_ai_gemini_model');
    if (geminiModel) setSelectedGeminiModel(geminiModel);
    
    // Load cost mode
    const storedCostMode = localStorage.getItem('atlas_ai_cost_mode') as CostMode;
    if (storedCostMode) setCostMode(storedCostMode);
  }, []);

  const handleSave = () => {
    // Save mode
    saveProviderMode(mode);
    factory.setMode(mode);

    // Save keys if they don't look masked
    const credentials: any = {};
    if (claudeKey && !claudeKey.includes('••••')) {
      credentials.claudeApiKey = claudeKey;
    }
    if (geminiKey && !geminiKey.includes('••••')) {
      credentials.geminiApiKey = geminiKey;
    }

    if (Object.keys(credentials).length > 0) {
      saveProviderCredentials(credentials);
      factory.updateCredentials(credentials);
    }
    
    // Save model preferences
    localStorage.setItem('atlas_ai_claude_model', selectedClaudeModel);
    localStorage.setItem('atlas_ai_gemini_model', selectedGeminiModel);
    localStorage.setItem('atlas_ai_cost_mode', costMode);
    factory.updateModelConfig({
      claudeModel: selectedClaudeModel,
      geminiModel: selectedGeminiModel
    });

    setSaved(true);
    setTimeout(() => {
      setSaved(false);
      onClose();
      // Reload page to reinitialize providers
      window.location.reload();
    }, 1500);
  };

  return (
    <motion.div
      className="fixed inset-0 z-50 flex items-center justify-center"
      style={{ background: 'rgba(0,0,0,0.75)', backdropFilter: 'blur(6px)' }}
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      exit={{ opacity: 0 }}
      onClick={onClose}
    >
      <motion.div
        className="relative w-full max-w-md mx-4 rounded-2xl overflow-hidden"
        style={{
          background: 'rgba(5,10,18,0.97)',
          border: '1px solid rgba(0,212,255,0.3)',
          boxShadow: '0 20px 40px rgba(0,212,255,0.1)'
        }}
        initial={{ scale: 0.9, y: 20 }}
        animate={{ scale: 1, y: 0 }}
        exit={{ scale: 0.9, y: 20 }}
        onClick={e => e.stopPropagation()}
      >
        <div className="h-1 w-full" style={{ background: 'linear-gradient(90deg, #00d4ff, #0066ff)' }} />

        <div className="p-6">
          <div className="flex items-center justify-between mb-6">
            <div className="flex items-center gap-3">
              <div
                className="w-10 h-10 rounded-full flex items-center justify-center"
                style={{ background: 'rgba(0,212,255,0.2)' }}
              >
                <Key className="w-5 h-5 text-cyan-400" />
              </div>
              <div>
                <h3 className="text-lg font-bold text-white">AI Provider Settings</h3>
                <p className="text-xs text-cyan-400/60">Configure API keys and mode</p>
              </div>
            </div>
            <button
              onClick={onClose}
              className="w-8 h-8 rounded-lg flex items-center justify-center hover:bg-white/10 transition-colors"
            >
              <X className="w-4 h-4 text-white/60" />
            </button>
          </div>

          {/* Mode Selection */}
          <div className="mb-6">
            <label className="text-xs font-semibold text-white/60 uppercase tracking-wider mb-2 block">
              Provider Mode
            </label>
            <div className="flex gap-3">
              <button
                onClick={() => setMode('mock')}
                className="flex-1 px-4 py-3 rounded-xl text-sm font-medium transition-all"
                style={{
                  background: mode === 'mock' ? 'rgba(0,212,255,0.2)' : 'rgba(255,255,255,0.05)',
                  border: `1px solid ${mode === 'mock' ? '#00d4ff' : 'rgba(255,255,255,0.1)'}`,
                  color: mode === 'mock' ? '#00d4ff' : 'rgba(255,255,255,0.6)'
                }}
              >
                Mock (Dev)
              </button>
              <button
                onClick={() => setMode('real')}
                className="flex-1 px-4 py-3 rounded-xl text-sm font-medium transition-all"
                style={{
                  background: mode === 'real' ? 'rgba(0,212,255,0.2)' : 'rgba(255,255,255,0.05)',
                  border: `1px solid ${mode === 'real' ? '#00d4ff' : 'rgba(255,255,255,0.1)'}`,
                  color: mode === 'real' ? '#00d4ff' : 'rgba(255,255,255,0.6)'
                }}
              >
                Real APIs
              </button>
            </div>
          </div>
          
          {/* Cost Mode Selection */}
          <div className="mb-6">
            <label className="text-xs font-semibold text-white/60 uppercase tracking-wider mb-2 block">
              Cost Mode
            </label>
            <div className="flex gap-2">
              <button
                onClick={() => setCostMode('cheapest')}
                className="flex-1 px-3 py-2 rounded-lg text-xs font-medium transition-all"
                style={{
                  background: costMode === 'cheapest' ? 'rgba(135,206,250,0.2)' : 'rgba(255,255,255,0.05)',
                  border: `1px solid ${costMode === 'cheapest' ? '#87ceeb' : 'rgba(255,255,255,0.1)'}`,
                  color: costMode === 'cheapest' ? '#87ceeb' : 'rgba(255,255,255,0.6)'
                }}
              >
                Cheapest
              </button>
              <button
                onClick={() => setCostMode('balanced')}
                className="flex-1 px-3 py-2 rounded-lg text-xs font-medium transition-all"
                style={{
                  background: costMode === 'balanced' ? 'rgba(0,255,127,0.2)' : 'rgba(255,255,255,0.05)',
                  border: `1px solid ${costMode === 'balanced' ? '#00ff7f' : 'rgba(255,255,255,0.1)'}`,
                  color: costMode === 'balanced' ? '#00ff7f' : 'rgba(255,255,255,0.6)'
                }}
              >
                Balanced
              </button>
              <button
                onClick={() => setCostMode('best_quality')}
                className="flex-1 px-3 py-2 rounded-lg text-xs font-medium transition-all"
                style={{
                  background: costMode === 'best_quality' ? 'rgba(255,215,0,0.2)' : 'rgba(255,255,255,0.05)',
                  border: `1px solid ${costMode === 'best_quality' ? '#ffd700' : 'rgba(255,255,255,0.1)'}`,
                  color: costMode === 'best_quality' ? '#ffd700' : 'rgba(255,255,255,0.6)'
                }}
              >
                Best Quality
              </button>
            </div>
            <p className="text-xs text-white/40 mt-2">
              {costMode === 'cheapest' && 'Always use the cheapest available model'}
              {costMode === 'balanced' && 'Balance cost and quality (recommended)'}
              {costMode === 'best_quality' && 'Use premium models for best results'}
            </p>
          </div>

          {/* API Keys */}
          {mode === 'real' && (
            <>
              <div className="mb-4">
                <label className="text-xs font-semibold text-white/60 uppercase tracking-wider mb-2 block">
                  Claude API Key
                </label>
                <input
                  type="password"
                  value={claudeKey}
                  onChange={e => setClaudeKey(e.target.value)}
                  placeholder="sk-ant-..."
                  className="w-full px-4 py-3 rounded-xl text-sm text-white placeholder-white/40 border-0 outline-none"
                  style={{
                    background: 'rgba(255,255,255,0.05)',
                    border: '1px solid rgba(255,255,255,0.1)'
                  }}
                />
              </div>

              <div className="mb-4">
                <label className="text-xs font-semibold text-white/60 uppercase tracking-wider mb-2 block">
                  Gemini API Key
                </label>
                <input
                  type="password"
                  value={geminiKey}
                  onChange={e => setGeminiKey(e.target.value)}
                  placeholder="AIza..."
                  className="w-full px-4 py-3 rounded-xl text-sm text-white placeholder-white/40 border-0 outline-none"
                  style={{
                    background: 'rgba(255,255,255,0.05)',
                    border: '1px solid rgba(255,255,255,0.1)'
                  }}
                />
              </div>
              
              {/* Model Selection */}
              <div className="mb-4">
                <label className="text-xs font-semibold text-white/60 uppercase tracking-wider mb-2 block">
                  Default Models
                </label>
                <div className="space-y-2">
                  <ModelSelector 
                    label="Claude"
                    selectedModel={selectedClaudeModel}
                    provider="anthropic"
                    onChange={setSelectedClaudeModel}
                  />
                  <ModelSelector 
                    label="Gemini"
                    selectedModel={selectedGeminiModel}
                    provider="google"
                    onChange={setSelectedGeminiModel}
                  />
                </div>
                <button
                  onClick={() => setShowModelCatalog(!showModelCatalog)}
                  className="mt-2 text-xs text-cyan-400 hover:text-cyan-300 flex items-center gap-1"
                >
                  <Cpu className="w-3 h-3" />
                  {showModelCatalog ? 'Hide' : 'View'} Model Catalog
                </button>
              </div>
              
              {showModelCatalog && (
                <div className="mb-4">
                  <ModelCatalogView 
                    showOnlySelectable={true}
                    selectedModelId={selectedClaudeModel}
                    onModelSelect={(model) => {
                      if (model.provider === 'anthropic') {
                        setSelectedClaudeModel(model.modelId);
                      } else if (model.provider === 'google') {
                        setSelectedGeminiModel(model.modelId);
                      }
                    }}
                  />
                </div>
              )}

              <div
                className="mb-6 p-3 rounded-xl flex items-start gap-3"
                style={{ background: 'rgba(255,165,0,0.1)', border: '1px solid rgba(255,165,0,0.3)' }}
              >
                <AlertCircle className="w-4 h-4 text-orange-400 flex-shrink-0 mt-0.5" />
                <p className="text-xs text-orange-400/90">
                  API keys are stored locally in your browser. Never share your keys. ATLAS will automatically select cheaper models for simple tasks.
                </p>
              </div>
            </>
          )}

          {/* Save Button */}
          <button
            onClick={handleSave}
            disabled={saved}
            className="w-full px-6 py-3 rounded-xl text-sm font-medium transition-all flex items-center justify-center gap-2"
            style={{
              background: saved
                ? 'rgba(0,255,0,0.2)'
                : 'linear-gradient(135deg, #00d4ff, #0066ff)',
              border: `1px solid ${saved ? 'rgba(0,255,0,0.4)' : 'transparent'}`,
              color: 'white'
            }}
          >
            {saved ? (
              <>
                <Check className="w-4 h-4" />
                Saved! Reloading...
              </>
            ) : (
              'Save Settings'
            )}
          </button>
        </div>
      </motion.div>
    </motion.div>
  );
}

function ModelSelector({ 
  label, 
  selectedModel, 
  provider, 
  onChange 
}: { 
  label: string;
  selectedModel: string;
  provider: 'anthropic' | 'google';
  onChange: (modelId: string) => void;
}) {
  const models = getSelectableModels().filter(m => m.provider === provider);
  const selected = models.find(m => m.modelId === selectedModel);
  
  return (
    <div>
      <label className="text-xs text-white/50 mb-1 block">{label}</label>
      <select
        value={selectedModel}
        onChange={e => onChange(e.target.value)}
        className="w-full px-3 py-2 rounded-lg text-xs text-white border-0 outline-none"
        style={{
          background: 'rgba(255,255,255,0.05)',
          border: '1px solid rgba(255,255,255,0.1)'
        }}
      >
        {models.map(model => (
          <option key={model.modelId} value={model.modelId}>
            {model.displayName} ({model.tier})
          </option>
        ))}
      </select>
      {selected && (
        <p className="text-xs text-white/40 mt-1">
          ${selected.costPer1MInputTokens?.toFixed(2)}/M tokens • {selected.contextWindow.toLocaleString()} context
        </p>
      )}
    </div>
  );
}