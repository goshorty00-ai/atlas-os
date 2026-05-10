using System;

namespace AtlasAI.Services;

public interface IMemoryService
{
    void Initialize();
    LocalUserProfile GetSnapshot();
    void Update(Action<LocalUserProfile> updater);
    void ObserveUserMessage(string userText);
    string BuildPromptMemoryBlock(LocalUserProfile snapshot);
    void RecordAction(string action);
}
