using System;

namespace AtlasAI.DJ
{
    internal static class DjTimingMath
    {
        public static double NormalizePhaseOffsetSeconds(double difference, double beatInterval)
        {
            if (beatInterval <= 0)
                return 0;

            difference %= beatInterval;
            if (difference > beatInterval / 2.0)
                difference -= beatInterval;
            else if (difference < -beatInterval / 2.0)
                difference += beatInterval;

            return difference;
        }

        public static double NormalizePhaseBeatOffset(double difference)
        {
            difference %= 1.0;
            if (difference > 0.5)
                difference -= 1.0;
            else if (difference < -0.5)
                difference += 1.0;

            return difference;
        }

        public static double ResolveSyncBaseBpm(double sourceEffectiveBpm, double targetBaseBpm, double pitchRange)
        {
            if (sourceEffectiveBpm <= 0 || targetBaseBpm <= 0)
                return targetBaseBpm;

            var candidates = new[]
            {
                targetBaseBpm / 2.0,
                targetBaseBpm,
                targetBaseBpm * 2.0,
            };

            var bestCandidate = targetBaseBpm;
            var bestScore = double.MaxValue;

            foreach (var candidate in candidates)
            {
                if (candidate < 45 || candidate > 400)
                    continue;

                var requiredTempo = Math.Abs(((sourceEffectiveBpm / candidate) - 1) * 100.0);
                var overRangePenalty = requiredTempo > pitchRange ? (requiredTempo - pitchRange) * 3.0 : 0.0;
                var proximityPenalty = Math.Abs(sourceEffectiveBpm - candidate) / Math.Max(sourceEffectiveBpm, 1.0);
                var octavePenalty = Math.Abs(Math.Log(candidate / targetBaseBpm, 2.0)) * 6.0;
                var score = requiredTempo + overRangePenalty + proximityPenalty + octavePenalty;

                if (score < bestScore)
                {
                    bestScore = score;
                    bestCandidate = candidate;
                }
            }

            return bestCandidate;
        }

        public static int ParseLoopSizeBeats(string loopSize)
        {
            return loopSize switch
            {
                "1/4" => 1,
                "1/2" => 2,
                "2" => 8,
                _ => 4,
            };
        }
    }
}