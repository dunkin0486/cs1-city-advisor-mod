using CityAdvisor;
using Xunit;

namespace CityAdvisor.Tests
{
    public class LocationDescriptionTests
    {
        [Fact]
        public void Origin_ReturnsCityCenter()
        {
            Assert.Equal("the city center", LocationDescription.CompassDirectionFromCenter(0f, 0f));
        }

        [Theory]
        [InlineData(0f, 1f, "north")]
        [InlineData(1f, 1f, "northeast")]
        [InlineData(1f, 0f, "east")]
        [InlineData(1f, -1f, "southeast")]
        [InlineData(0f, -1f, "south")]
        [InlineData(-1f, -1f, "southwest")]
        [InlineData(-1f, 0f, "west")]
        [InlineData(-1f, 1f, "northwest")]
        public void EightCardinalDirections_ResolveCorrectly(float x, float z, string expected)
        {
            Assert.Equal(expected, LocationDescription.CompassDirectionFromCenter(x, z));
        }

        [Fact]
        public void DescribeLocation_WithDistrictName_UsesIt()
        {
            string result = LocationDescription.DescribeLocation("Riverside", x: 5000f, z: 5000f);
            Assert.Equal("near Riverside", result);
        }

        [Fact]
        public void DescribeLocation_WithNullDistrictName_FallsBackToCompass()
        {
            // DistrictManager.GetDistrictName returns null for district 0
            // (nothing painted there) -- confirmed via ILSpy, not a case
            // that throws or returns empty string. This must not crash and
            // must still produce a usable hint.
            string result = LocationDescription.DescribeLocation(null, x: 0f, z: 1f);
            Assert.Equal("to the north of the city center", result);
        }

        [Fact]
        public void DescribeLocation_WithEmptyDistrictName_FallsBackToCompass()
        {
            string result = LocationDescription.DescribeLocation(string.Empty, x: 1f, z: 0f);
            Assert.Equal("to the east of the city center", result);
        }
    }
}
