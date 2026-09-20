namespace CityAdvisor
{
    /// <summary>
    /// Pure scoring math extracted from MissingInterchangeDiagnostic so it
    /// can be unit tested without the CS1 game assemblies. Deliberately has
    /// no UnityEngine/Assembly-CSharp/ICities dependency (not even
    /// UnityEngine.Mathf) — keep it that way so CI can test this without a
    /// licensed game install. See tests/MissingInterchangeScoringTests.cs.
    /// </summary>
    public static class MissingInterchangeScoring
    {
        /// <summary>
        /// Severity of a missing-interchange finding: 0 right at the search
        /// radius, saturating toward (never quite reaching) 1 as distance
        /// grows, scaled by traffic density. A plain distance/threshold
        /// ratio instead saturates to 1.0 almost immediately past the
        /// threshold on any real save — confirmed against a 179k-population
        /// test city where every real finding was 4-6x past the threshold
        /// and all scored 1.00 under the old formula.
        /// </summary>
        public static float CalculateSeverity(float density, float nearestRampDistance, float searchRadius)
        {
            if (searchRadius <= 0f || nearestRampDistance <= searchRadius)
            {
                return 0f;
            }

            float distanceFactor = 1f - (searchRadius / nearestRampDistance);
            return Clamp01(density * distanceFactor);
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
