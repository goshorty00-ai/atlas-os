using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.SmartHome
{
    internal interface ISmartHomeVerifierObserver
    {
        void OnEvent(SmartHomeVerifierEvent evt);
    }

    internal interface ISmartHomeProviderManagementVerifierHooks
    {
        Task<SmartHomeRingLoginVerificationResult> LoginRingAsync(string email, string password, string code, string pendingHardwareId, CancellationToken cancellationToken);
    }

    internal sealed record SmartHomeVerifierEvent(
        string Source,
        string Name,
        IReadOnlyDictionary<string, string> Data);

    internal sealed record SmartHomeRingLoginVerificationResult(
        bool Ok,
        string Message,
        bool RequiresTwoFactor = false,
        string RefreshToken = "",
        string PendingHardwareId = "");
}