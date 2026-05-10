using System;

namespace AtlasAI.Voice
{
    public interface IVoiceDictationRecognizer : IDisposable
    {
        event EventHandler<string>? SpeechRecognized;
        event EventHandler<string>? SpeechHypothesized;
        event EventHandler<int>? AudioLevelUpdated;
        event EventHandler<string>? RecognitionError;
        event EventHandler? RecognitionComplete;

        bool IsListening { get; }
        string InputDeviceName { get; }

        bool Start();
        void Stop();
    }
}
