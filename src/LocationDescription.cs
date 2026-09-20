using System;

namespace CityAdvisor
{
    /// <summary>
    /// Pure, game-independent helpers for describing a world position in
    /// player-facing text, extracted the same way as
    /// MissingInterchangeScoring so it's unit testable without the CS1
    /// game assemblies. Takes plain floats rather than Vector3.
    /// </summary>
    public static class LocationDescription
    {
        private static readonly string[] CompassPoints =
        {
            "north", "northeast", "east", "southeast",
            "south", "southwest", "west", "northwest",
        };

        /// <summary>
        /// Rough 8-point compass direction from the map center (world
        /// origin) to the given position. Convention assumed: +z is north,
        /// +x is east, matching standard Unity world axes -- this has NOT
        /// been visually cross-checked against CS1's in-game minimap/
        /// compass orientation, only assumed. Treat it as a rough "look
        /// over there" hint, not a precise bearing, until verified in-game.
        /// </summary>
        public static string CompassDirectionFromCenter(float x, float z)
        {
            if (x == 0f && z == 0f)
            {
                return "the city center";
            }

            double angle = Math.Atan2(x, z); // radians, 0 = north, clockwise
            if (angle < 0)
            {
                angle += 2 * Math.PI;
            }

            int index = (int)Math.Round(angle / (2 * Math.PI) * 8) % 8;
            return CompassPoints[index];
        }

        /// <summary>
        /// Builds a player-facing location hint given an (optionally null)
        /// district name. DistrictManager.GetDistrictName returns null for
        /// district 0 -- the reserved "no district painted here" slot, not
        /// an edge case that throws or returns empty string (confirmed via
        /// ILSpy) -- so this always has a usable fallback even in an area
        /// with no districts at all.
        /// </summary>
        public static string DescribeLocation(string districtNameOrNull, float x, float z)
        {
            if (!string.IsNullOrEmpty(districtNameOrNull))
            {
                return $"near {districtNameOrNull}";
            }

            return $"to the {CompassDirectionFromCenter(x, z)} of the city center";
        }
    }
}
