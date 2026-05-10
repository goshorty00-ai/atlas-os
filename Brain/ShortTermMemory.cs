using System;
using System.Collections.Generic;

namespace AtlasAI.Brain
{
    public class ShortTermMemory
    {
        private readonly List<string> _recentAssistantTexts = new List<string>();
        public string LastResponseStyle { get; private set; } = "";

        public void SetLastResponseStyle(string style)
        {
            LastResponseStyle = style;
        }

        public void AddAssistantText(string text)
        {
            _recentAssistantTexts.Add(text);
            if (_recentAssistantTexts.Count > 50)
            {
                _recentAssistantTexts.RemoveAt(0);
            }
        }

        public string[] GetRecentAssistantTexts(int count)
        {
            int start = Math.Max(0, _recentAssistantTexts.Count - count);
            int length = _recentAssistantTexts.Count - start;
            if (length <= 0) return Array.Empty<string>();
            
            var result = new string[length];
            _recentAssistantTexts.CopyTo(start, result, 0, length);
            return result;
        }
    }
}
