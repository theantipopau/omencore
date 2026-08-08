using System.Collections.Generic;
using System.Linq;
using OmenCore.Hardware;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    /// <summary>
    /// Collecting reported LAMPS into physical KEYS.
    ///
    /// The device reports lamps and a wide key carries several - Space has five on board 8D87,
    /// both Shifts have three. Their centres sit closer together than one key is wide, so treating
    /// each lamp as its own cap draws overlapping squares and the modifier columns look crushed.
    ///
    /// Every case here is taken from the real 8D87 lamp map, because the interesting failures are
    /// all about spacing that a made-up fixture would not reproduce.
    /// </summary>
    public class KeyboardMapGroupingTests
    {
        private static HidLampArray.LampInfo Lamp(ushort id, uint xMm, uint yMm, byte usage) =>
            new(id, xMm * 1000, yMm * 1000, 0, usage);

        [Fact]
        public void Adjacent_lamps_sharing_a_usage_become_one_key()
        {
            // Left Shift on 8D87: three lamps at x = 12, 23 and 30 mm, all usage 0xE1.
            var map = new List<HidLampArray.LampInfo>
            {
                Lamp(80, 12, 81, 0xE1),
                Lamp(81, 23, 81, 0xE1),
                Lamp(82, 30, 81, 0xE1),
            };

            var keys = KeyboardMapViewModel.GroupLampsIntoKeys(map);

            Assert.Single(keys);
            Assert.Equal(3, keys[0].Count);
        }

        [Fact]
        public void Neighbouring_letters_stay_separate_keys()
        {
            var map = new List<HidLampArray.LampInfo>
            {
                Lamp(83, 52, 81, 0x1D), // Z
                Lamp(84, 71, 81, 0x1B), // X
                Lamp(85, 89, 81, 0x06), // C
            };

            var keys = KeyboardMapViewModel.GroupLampsIntoKeys(map);

            Assert.Equal(3, keys.Count);
            Assert.All(keys, k => Assert.Single(k));
        }

        [Fact]
        public void The_same_usage_in_different_rows_is_two_keys()
        {
            // Keypad Enter appears twice on this keyboard, one row apart. Grouping on usage alone
            // would merge them into a single key spanning the gap.
            var map = new List<HidLampArray.LampInfo>
            {
                Lamp(99, 328, 81, 0x58),
                Lamp(119, 328, 99, 0x58),
            };

            var keys = KeyboardMapViewModel.GroupLampsIntoKeys(map);

            Assert.Equal(2, keys.Count);
        }

        [Fact]
        public void The_same_usage_far_apart_in_one_row_is_two_keys()
        {
            // Both Shifts share usage 0xE1 in some reports. They are at opposite ends of the row,
            // so adjacency is what keeps them apart.
            var map = new List<HidLampArray.LampInfo>
            {
                Lamp(80, 12, 81, 0xE1),
                Lamp(93, 239, 81, 0xE1),
            };

            var keys = KeyboardMapViewModel.GroupLampsIntoKeys(map);

            Assert.Equal(2, keys.Count);
        }

        [Fact]
        public void Unbound_lamps_are_never_merged_with_each_other()
        {
            // Usage 0 means "no key reported". That is not evidence two lamps belong together, and
            // on this board each is separately addressable under decorative trim.
            var map = new List<HidLampArray.LampInfo>
            {
                Lamp(10, 100, 20, 0x00),
                Lamp(11, 108, 20, 0x00),
            };

            var keys = KeyboardMapViewModel.GroupLampsIntoKeys(map);

            Assert.Equal(2, keys.Count);
        }

        [Fact]
        public void Every_lamp_survives_grouping_exactly_once()
        {
            // The property that matters for correctness: grouping must not drop or duplicate a
            // lamp, or the apply path silently misses keys.
            var map = new List<HidLampArray.LampInfo>
            {
                Lamp(0, 12, 81, 0xE1), Lamp(1, 23, 81, 0xE1), Lamp(2, 30, 81, 0xE1),
                Lamp(3, 52, 81, 0x1D), Lamp(4, 71, 81, 0x1B),
                Lamp(5, 91, 99, 0x2C), Lamp(6, 106, 99, 0x2C), Lamp(7, 126, 99, 0x2C),
                Lamp(8, 200, 99, 0x00),
            };

            var keys = KeyboardMapViewModel.GroupLampsIntoKeys(map);

            var flattened = keys.SelectMany(k => k).Select(l => l.LampId).OrderBy(id => id).ToList();
            Assert.Equal(map.Select(l => l.LampId).OrderBy(id => id), flattened);
        }

        [Fact]
        public void Space_becomes_one_wide_key_rather_than_five()
        {
            // Five lamps at 91, 106, 126, 146 and 161 mm - gaps of 15 to 20 mm, right at the limit
            // that separates "one wide cap" from "two keys".
            var map = new List<HidLampArray.LampInfo>
            {
                Lamp(105, 91, 99, 0x2C),
                Lamp(106, 106, 99, 0x2C),
                Lamp(107, 126, 99, 0x2C),
                Lamp(108, 146, 99, 0x2C),
                Lamp(109, 161, 99, 0x2C),
            };

            var keys = KeyboardMapViewModel.GroupLampsIntoKeys(map);

            Assert.Single(keys);
            Assert.Equal(5, keys[0].Count);
        }
    }
}
