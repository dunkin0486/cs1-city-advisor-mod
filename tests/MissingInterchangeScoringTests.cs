using CityAdvisor;
using Xunit;

namespace CityAdvisor.Tests
{
    public class MissingInterchangeScoringTests
    {
        [Fact]
        public void AtOrBelowSearchRadius_ReturnsZero()
        {
            Assert.Equal(0f, MissingInterchangeScoring.CalculateSeverity(density: 1f, nearestRampDistance: 1500f, searchRadius: 1500f));
            Assert.Equal(0f, MissingInterchangeScoring.CalculateSeverity(density: 1f, nearestRampDistance: 500f, searchRadius: 1500f));
        }

        [Fact]
        public void ZeroOrNegativeSearchRadius_ReturnsZero()
        {
            Assert.Equal(0f, MissingInterchangeScoring.CalculateSeverity(density: 1f, nearestRampDistance: 5000f, searchRadius: 0f));
            Assert.Equal(0f, MissingInterchangeScoring.CalculateSeverity(density: 1f, nearestRampDistance: 5000f, searchRadius: -100f));
        }

        [Fact]
        public void ZeroDensity_ReturnsZeroRegardlessOfDistance()
        {
            Assert.Equal(0f, MissingInterchangeScoring.CalculateSeverity(density: 0f, nearestRampDistance: 100000f, searchRadius: 1500f));
        }

        [Fact]
        public void RegressionCase_RealFindingsFromAlderCitySave_DoNotAllSaturateToOne()
        {
            // Confirmed regression: on a real 179k-population save, all 5
            // real findings had density ~1.0 (capped traffic density) and
            // were 4-6x past the 1500m threshold, and the old
            // density * (distance / threshold) formula scored every one of
            // them 1.00 -- the metric couldn't distinguish "somewhat bad"
            // from "very bad". This must no longer saturate.
            float severityA = MissingInterchangeScoring.CalculateSeverity(density: 1f, nearestRampDistance: 5939f, searchRadius: 1500f);
            float severityB = MissingInterchangeScoring.CalculateSeverity(density: 1f, nearestRampDistance: 6079f, searchRadius: 1500f);

            Assert.True(severityA < 1f, $"Expected severity < 1.0, got {severityA}");
            Assert.True(severityB < 1f, $"Expected severity < 1.0, got {severityB}");
            // Distinct distances that far apart should not collapse to an
            // identical score -- that was exactly the bug.
            Assert.NotEqual(severityA, severityB);
        }

        [Fact]
        public void SeverityIncreasesMonotonicallyWithDistance_AtFixedDensity()
        {
            float nearer = MissingInterchangeScoring.CalculateSeverity(density: 0.8f, nearestRampDistance: 2000f, searchRadius: 1500f);
            float farther = MissingInterchangeScoring.CalculateSeverity(density: 0.8f, nearestRampDistance: 8000f, searchRadius: 1500f);

            Assert.True(farther > nearer, $"Expected farther ({farther}) > nearer ({nearer})");
        }

        [Fact]
        public void SeverityIncreasesMonotonicallyWithDensity_AtFixedDistance()
        {
            float lowDensity = MissingInterchangeScoring.CalculateSeverity(density: 0.3f, nearestRampDistance: 4000f, searchRadius: 1500f);
            float highDensity = MissingInterchangeScoring.CalculateSeverity(density: 0.9f, nearestRampDistance: 4000f, searchRadius: 1500f);

            Assert.True(highDensity > lowDensity, $"Expected highDensity ({highDensity}) > lowDensity ({lowDensity})");
        }

        [Fact]
        public void SeverityNeverExceedsOne_EvenAtExtremeDistance()
        {
            float severity = MissingInterchangeScoring.CalculateSeverity(density: 1f, nearestRampDistance: float.MaxValue, searchRadius: 1500f);
            Assert.InRange(severity, 0f, 1f);
        }

        [Theory]
        [InlineData(1f, 3000f, 1500f, 0.5f)]   // distanceFactor = 1 - 1500/3000 = 0.5
        [InlineData(0.5f, 3000f, 1500f, 0.25f)] // density halves it
        [InlineData(1f, 6000f, 1500f, 0.75f)]  // distanceFactor = 1 - 1500/6000 = 0.75
        public void MatchesExpectedFormula(float density, float distance, float radius, float expected)
        {
            float severity = MissingInterchangeScoring.CalculateSeverity(density, distance, radius);
            Assert.Equal(expected, severity, precision: 4);
        }
    }
}
