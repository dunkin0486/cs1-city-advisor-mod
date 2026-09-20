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

        [Theory]
        [InlineData(0f, 0f, true)]
        [InlineData(4800f, 4800f, true)]
        [InlineData(-4800f, -4800f, true)]
        [InlineData(4800.1f, 0f, false)]
        [InlineData(0f, -4800.1f, false)]
        public void IsWithinVanillaDistrictGrid_MatchesExpectedBounds(float x, float z, bool expected)
        {
            Assert.Equal(expected, LocationDescription.IsWithinVanillaDistrictGrid(x, z));
        }

        [Theory]
        [InlineData(-8600.4f, -893f)]   // real finding from a live save that reported a wrong district
        [InlineData(8603.6f, 3090.9f)]  // another real finding, same wrong district, opposite map edge
        public void IsWithinVanillaDistrictGrid_FalseForRealOutOfRangeFindings(float x, float z)
        {
            // DistrictManager.GetDistrict's 512x512 grid at 19.2m/cell only
            // covers vanilla's ~4915m range and silently clamps beyond it
            // (confirmed via ILSpy) -- these are real coordinates from a
            // live 81-Tiles save where GetDistrict returned the same wrong
            // district for every far-map finding regardless of actual
            // position. Must be treated as untrustworthy, not used.
            Assert.False(LocationDescription.IsWithinVanillaDistrictGrid(x, z));
        }
    }
}
