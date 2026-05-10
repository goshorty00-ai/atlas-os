// Model Catalog View - Display available AI models with tier classification

import React, { useState } from 'react';
import { 
  MODEL_CATALOG, 
  getModelsByProvider, 
  getSelectableModels,
  type ModelInfo,
  type ModelTier 
} from '../ai/models/ModelCatalog';

interface ModelCatalogViewProps {
  onModelSelect?: (model: ModelInfo) => void;
  selectedModelId?: string;
  showOnlySelectable?: boolean;
}

export function ModelCatalogView({ 
  onModelSelect, 
  selectedModelId,
  showOnlySelectable = false 
}: ModelCatalogViewProps) {
  const [selectedProvider, setSelectedProvider] = useState<'all' | 'anthropic' | 'google' | 'openai'>('all');
  
  const models = showOnlySelectable 
    ? getSelectableModels() 
    : MODEL_CATALOG.models;
  
  const filteredModels = selectedProvider === 'all' 
    ? models 
    : models.filter(m => m.provider === selectedProvider);
  
  const claudeModels = filteredModels.filter(m => m.provider === 'anthropic');
  const geminiModels = filteredModels.filter(m => m.provider === 'google');
  const openaiModels = filteredModels.filter(m => m.provider === 'openai');
  
  return (
    <div style={{ 
      padding: '20px', 
      background: 'rgba(0, 0, 0, 0.3)', 
      borderRadius: '12px',
      maxHeight: '600px',
      overflowY: 'auto'
    }}>
      <div style={{ marginBottom: '20px' }}>
        <h3 style={{ 
          color: '#00f0ff', 
          marginBottom: '10px',
          fontSize: '18px',
          fontWeight: 600
        }}>
          AI Model Catalog
        </h3>
        <p style={{ 
          color: 'rgba(255, 255, 255, 0.6)', 
          fontSize: '12px',
          marginBottom: '15px'
        }}>
          Last updated: {MODEL_CATALOG.lastUpdated}
        </p>
        
        {/* Provider filter */}
        <div style={{ display: 'flex', gap: '10px', flexWrap: 'wrap' }}>
          {(['all', 'anthropic', 'google', 'openai'] as const).map(provider => (
            <button
              key={provider}
              onClick={() => setSelectedProvider(provider)}
              style={{
                padding: '6px 12px',
                background: selectedProvider === provider 
                  ? 'rgba(0, 240, 255, 0.2)' 
                  : 'rgba(255, 255, 255, 0.05)',
                border: selectedProvider === provider 
                  ? '1px solid #00f0ff' 
                  : '1px solid rgba(255, 255, 255, 0.1)',
                borderRadius: '6px',
                color: selectedProvider === provider ? '#00f0ff' : 'rgba(255, 255, 255, 0.7)',
                fontSize: '12px',
                cursor: 'pointer',
                textTransform: 'capitalize'
              }}
            >
              {provider === 'all' ? 'All Providers' : provider}
            </button>
          ))}
        </div>
      </div>
      
      {/* Claude Models */}
      {claudeModels.length > 0 && (
        <ModelProviderSection 
          title="Anthropic Claude" 
          models={claudeModels}
          onModelSelect={onModelSelect}
          selectedModelId={selectedModelId}
        />
      )}
      
      {/* Gemini Models */}
      {geminiModels.length > 0 && (
        <ModelProviderSection 
          title="Google Gemini" 
          models={geminiModels}
          onModelSelect={onModelSelect}
          selectedModelId={selectedModelId}
        />
      )}
      
      {/* OpenAI Models */}
      {openaiModels.length > 0 && (
        <ModelProviderSection 
          title="OpenAI GPT" 
          models={openaiModels}
          onModelSelect={onModelSelect}
          selectedModelId={selectedModelId}
        />
      )}
    </div>
  );
}

function ModelProviderSection({ 
  title, 
  models, 
  onModelSelect,
  selectedModelId 
}: { 
  title: string; 
  models: ModelInfo[];
  onModelSelect?: (model: ModelInfo) => void;
  selectedModelId?: string;
}) {
  return (
    <div style={{ marginBottom: '25px' }}>
      <h4 style={{ 
        color: 'rgba(255, 255, 255, 0.9)', 
        fontSize: '14px',
        fontWeight: 600,
        marginBottom: '12px'
      }}>
        {title}
      </h4>
      
      <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
        {models.map(model => (
          <ModelCard 
            key={model.modelId} 
            model={model}
            isSelected={model.modelId === selectedModelId}
            onSelect={onModelSelect}
          />
        ))}
      </div>
    </div>
  );
}

function ModelCard({ 
  model, 
  isSelected,
  onSelect 
}: { 
  model: ModelInfo;
  isSelected: boolean;
  onSelect?: (model: ModelInfo) => void;
}) {
  const [expanded, setExpanded] = useState(false);
  
  return (
    <div 
      style={{
        background: isSelected 
          ? 'rgba(0, 240, 255, 0.1)' 
          : 'rgba(255, 255, 255, 0.03)',
        border: isSelected 
          ? '1px solid #00f0ff' 
          : '1px solid rgba(255, 255, 255, 0.1)',
        borderRadius: '8px',
        padding: '12px',
        cursor: model.selectable && onSelect ? 'pointer' : 'default',
        opacity: model.selectable ? 1 : 0.5,
        transition: 'all 0.2s'
      }}
      onClick={() => {
        if (model.selectable && onSelect) {
          onSelect(model);
        }
      }}
    >
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
        <div style={{ flex: 1 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '6px' }}>
            <span style={{ 
              color: '#fff', 
              fontSize: '13px',
              fontWeight: 500
            }}>
              {model.displayName}
            </span>
            <TierBadge tier={model.tier} />
            {!model.selectable && (
              <span style={{
                padding: '2px 6px',
                background: 'rgba(255, 0, 0, 0.2)',
                border: '1px solid rgba(255, 0, 0, 0.4)',
                borderRadius: '4px',
                fontSize: '10px',
                color: '#ff6b6b'
              }}>
                DISABLED
              </span>
            )}
          </div>
          
          <div style={{ 
            color: 'rgba(255, 255, 255, 0.5)', 
            fontSize: '11px',
            fontFamily: 'monospace',
            marginBottom: '6px'
          }}>
            {model.modelId}
          </div>
          
          <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap', marginBottom: '6px' }}>
            {model.capabilities.slice(0, 4).map(cap => (
              <span key={cap} style={{
                padding: '2px 6px',
                background: 'rgba(0, 240, 255, 0.1)',
                border: '1px solid rgba(0, 240, 255, 0.3)',
                borderRadius: '4px',
                fontSize: '10px',
                color: '#00f0ff'
              }}>
                {cap}
              </span>
            ))}
            {model.capabilities.length > 4 && (
              <span style={{ fontSize: '10px', color: 'rgba(255, 255, 255, 0.4)' }}>
                +{model.capabilities.length - 4} more
              </span>
            )}
          </div>
          
          {model.costPer1MInputTokens !== undefined && (
            <div style={{ 
              color: 'rgba(255, 255, 255, 0.6)', 
              fontSize: '11px'
            }}>
              ${model.costPer1MInputTokens.toFixed(2)} / ${model.costPer1MOutputTokens?.toFixed(2)} per 1M tokens
            </div>
          )}
          
          {model.notes && (
            <div style={{ 
              color: 'rgba(255, 255, 255, 0.5)', 
              fontSize: '11px',
              marginTop: '6px',
              fontStyle: 'italic'
            }}>
              {model.notes}
            </div>
          )}
        </div>
        
        <button
          onClick={(e) => {
            e.stopPropagation();
            setExpanded(!expanded);
          }}
          style={{
            background: 'none',
            border: 'none',
            color: 'rgba(255, 255, 255, 0.5)',
            cursor: 'pointer',
            fontSize: '12px',
            padding: '4px'
          }}
        >
          {expanded ? '▼' : '▶'}
        </button>
      </div>
      
      {expanded && (
        <div style={{ 
          marginTop: '12px', 
          paddingTop: '12px', 
          borderTop: '1px solid rgba(255, 255, 255, 0.1)',
          fontSize: '11px',
          color: 'rgba(255, 255, 255, 0.6)'
        }}>
          <div style={{ marginBottom: '6px' }}>
            <strong>Context:</strong> {model.contextWindow.toLocaleString()} tokens
          </div>
          <div style={{ marginBottom: '6px' }}>
            <strong>Max Output:</strong> {model.maxOutputTokens.toLocaleString()} tokens
          </div>
          <div style={{ marginBottom: '6px' }}>
            <strong>Usage Tags:</strong> {model.usageTags.join(', ')}
          </div>
          <div style={{ marginBottom: '6px' }}>
            <strong>Status:</strong> {model.supportedStatus}
          </div>
          <div>
            <a 
              href={model.sourceUrl} 
              target="_blank" 
              rel="noopener noreferrer"
              style={{ color: '#00f0ff', textDecoration: 'none' }}
            >
              View Documentation →
            </a>
          </div>
        </div>
      )}
    </div>
  );
}

function TierBadge({ tier }: { tier: ModelTier }) {
  const tierColors: Record<ModelTier, { bg: string; border: string; text: string }> = {
    flagship: { bg: 'rgba(255, 215, 0, 0.2)', border: 'rgba(255, 215, 0, 0.5)', text: '#ffd700' },
    fast: { bg: 'rgba(0, 255, 127, 0.2)', border: 'rgba(0, 255, 127, 0.5)', text: '#00ff7f' },
    cheap: { bg: 'rgba(135, 206, 250, 0.2)', border: 'rgba(135, 206, 250, 0.5)', text: '#87ceeb' },
    legacy: { bg: 'rgba(255, 165, 0, 0.2)', border: 'rgba(255, 165, 0, 0.5)', text: '#ffa500' },
    preview: { bg: 'rgba(138, 43, 226, 0.2)', border: 'rgba(138, 43, 226, 0.5)', text: '#8a2be2' },
    deprecated: { bg: 'rgba(255, 69, 0, 0.2)', border: 'rgba(255, 69, 0, 0.5)', text: '#ff4500' },
    retired: { bg: 'rgba(128, 128, 128, 0.2)', border: 'rgba(128, 128, 128, 0.5)', text: '#808080' }
  };
  
  const colors = tierColors[tier];
  
  return (
    <span style={{
      padding: '2px 6px',
      background: colors.bg,
      border: `1px solid ${colors.border}`,
      borderRadius: '4px',
      fontSize: '10px',
      color: colors.text,
      textTransform: 'uppercase',
      fontWeight: 600
    }}>
      {tier}
    </span>
  );
}
