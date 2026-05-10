using System;
using System.Diagnostics;
using System.Threading.Tasks;
using AtlasAI.Core;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Agent Intent Phases - micro-state machine for "alive" feedback
    /// </summary>
    public enum AgentIntentPhase
    {
        Idle,           // No active intent
        Understanding,  // Analyzing command (150-300ms visual)
        Executing,      // Running macro
        Completed       // Just finished (brief visual cue)
    }

    /// <summary>
    /// Controls the Agent Intent phases and wires them to Presence/HoloCore.
    /// Creates the illusion of cognition and decision-making without slowing execution.
    /// </summary>
    public class AgentIntentController
    {
        private static AgentIntentController? _instance;
        public static AgentIntentController Instance => _instance ??= new AgentIntentController();

        private AgentIntentPhase _currentPhase = AgentIntentPhase.Idle;
        private DateTime _phaseStartTime;
        private string _currentMacroTitle = "";

        // Phase durations (visual only, don't block execution)
        private const int UnderstandingMinMs = 150;
        private const int CompletedDisplayMs = 800;

        public event EventHandler<AgentIntentPhase>? PhaseChanged;
        public event EventHandler<string>? StatusTextChanged;

        public AgentIntentPhase CurrentPhase => _currentPhase;
        public string CurrentMacroTitle => _currentMacroTitle;

        private AgentIntentController() { }

        /// <summary>
        /// Begin the Understanding phase - called when command is entered
        /// </summary>
        public async Task BeginUnderstandingAsync(string input)
        {
            SetPhase(AgentIntentPhase.Understanding);
            UpdatePresenceForPhase(AgentIntentPhase.Understanding);
            RaiseStatusText("Analyzing...");

            // Brief visual pause (non-blocking feel)
            await Task.Delay(UnderstandingMinMs);
        }

        /// <summary>
        /// Transition to Executing phase - called when macro starts
        /// </summary>
        public void BeginExecuting(string macroTitle)
        {
            _currentMacroTitle = macroTitle;
            SetPhase(AgentIntentPhase.Executing);
            UpdatePresenceForPhase(AgentIntentPhase.Executing);
            RaiseStatusText($"Running {macroTitle}...");
        }

        /// <summary>
        /// Transition to Completed phase - called when macro finishes
        /// </summary>
        public async Task CompleteAsync(bool success)
        {
            SetPhase(AgentIntentPhase.Completed);
            UpdatePresenceForPhase(AgentIntentPhase.Completed);
            RaiseStatusText(success ? "Complete" : "Failed");

            // Brief completion visual, then decay to idle
            await Task.Delay(CompletedDisplayMs);
            
            ReturnToIdle();
        }

        /// <summary>
        /// Cancel intent and return to idle (e.g., no macro matched)
        /// </summary>
        public void Cancel()
        {
            ReturnToIdle();
        }

        private void ReturnToIdle()
        {
            _currentMacroTitle = "";
            SetPhase(AgentIntentPhase.Idle);
            UpdatePresenceForPhase(AgentIntentPhase.Idle);
            RaiseStatusText("");
        }

        private void SetPhase(AgentIntentPhase phase)
        {
            if (_currentPhase != phase)
            {
                var previous = _currentPhase;
                _currentPhase = phase;
                _phaseStartTime = DateTime.Now;
                Debug.WriteLine($"[AgentIntent] Phase: {previous} → {phase}");
                PhaseChanged?.Invoke(this, phase);
            }
        }

        /// <summary>
        /// Wire intent phases to PresenceController/PresenceVisualModel
        /// Reuses existing visual states - no new animations needed
        /// </summary>
        private void UpdatePresenceForPhase(AgentIntentPhase phase)
        {
            try
            {
                var presence = PresenceController.Instance;
                var model = PresenceVisualModel.Instance;

                switch (phase)
                {
                    case AgentIntentPhase.Understanding:
                        // Slight attention increase, brief thinking pulse
                        // No orange core (read-only operation)
                        model.TargetAttentionLevel = 0.85;
                        model.TargetPulseRate = 0.6;
                        model.TargetPulseAmplitude = 0.45;
                        model.TargetCoreGlow = 0.65;
                        model.TargetCoreAccent = 0.0; // No orange for read-only
                        model.TargetRingSpeed = 0.5;
                        model.TargetNoiseJitter = 0.03; // Minimal jitter = focused
                        presence.RecordInput();
                        break;

                    case AgentIntentPhase.Executing:
                        // Working state - steady, confident motion
                        presence.IsWorkingTask = true;
                        model.TargetAttentionLevel = 0.9;
                        model.TargetPulseRate = 0.5;
                        model.TargetPulseAmplitude = 0.4;
                        model.TargetCoreGlow = 0.7;
                        model.TargetCoreAccent = 0.0; // No orange for read-only
                        model.TargetRingSpeed = 0.55;
                        model.TargetNoiseJitter = 0.02; // Very stable = confidence
                        break;

                    case AgentIntentPhase.Completed:
                        // Slight outward release, calm pulse
                        presence.IsWorkingTask = false;
                        model.TargetAttentionLevel = 0.75;
                        model.TargetPulseRate = 0.35;
                        model.TargetPulseAmplitude = 0.5; // Brief expansion
                        model.TargetCoreGlow = 0.6;
                        model.TargetCoreAccent = 0.0;
                        model.TargetRingSpeed = 0.3;
                        model.TargetNoiseJitter = 0.01;
                        break;

                    case AgentIntentPhase.Idle:
                    default:
                        // Smooth decay back to attentive/idle
                        presence.IsWorkingTask = false;
                        // Let PresenceController handle natural decay
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AgentIntent] Presence update error: {ex.Message}");
            }
        }

        private void RaiseStatusText(string text)
        {
            StatusTextChanged?.Invoke(this, text);
        }
    }
}
