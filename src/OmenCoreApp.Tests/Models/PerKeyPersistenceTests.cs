using System.Collections.Generic;
using FluentAssertions;
using OmenCore.Models;
using Xunit;

namespace OmenCoreApp.Tests.Models
{
    /// <summary>
    /// Pins the identity a saved per-key cell is filed under.
    ///
    /// WHY THIS IS WORTH A TEST FILE. Saving and restoring are written in two different places and
    /// agree only by convention: the view-model composes a key for each editor cell, and this model
    /// composes one for each stored cell, and nothing but these tests forces them to be the same
    /// string. When two such formats drift, the symptom is not an exception — it is a restore that
    /// silently matches nothing and leaves the keyboard black, which reads as "it forgot my
    /// colours" and looks identical to the picture never having been saved.
    ///
    /// The other half is the kind collision. A colour-map position and a lamp id are both small
    /// integers over the same range and they address DIFFERENT lights: position 12 is one LED of
    /// one key, lamp 12 is a whole key somewhere else entirely. A board that gains a layout between
    /// two runs switches which addressing the editor uses, so an un-kinded key would restore a
    /// picture onto the wrong lights and be perfectly consistent about it.
    /// </summary>
    public class PerKeyPersistenceTests
    {
        [Fact]
        public void LedCell_IsKeyedByColourMapPosition()
        {
            new SavedPerKeyCell { Led = 42 }.Key.Should().Be("led:42");
        }

        [Fact]
        public void LampCell_IsKeyedByLampId()
        {
            new SavedPerKeyCell { Lamp = 42 }.Key.Should().Be("lamp:42");
        }

        [Fact]
        public void LightBarCell_IsKeyedByZone()
        {
            new SavedPerKeyCell { Zone = 2 }.Key.Should().Be("zone:2");
        }

        [Fact]
        public void SameIndex_DifferentKind_DoesNotCollide()
        {
            // The whole reason the kind is in the string. Were these equal, a board that gained a
            // layout would restore its lamp picture onto colour-map positions - wrong lights, and
            // no error anywhere to say so.
            var led = new SavedPerKeyCell { Led = 12 }.Key;
            var lamp = new SavedPerKeyCell { Lamp = 12 }.Key;
            var zone = new SavedPerKeyCell { Zone = 12 }.Key;

            led.Should().NotBe(lamp);
            led.Should().NotBe(zone);
            lamp.Should().NotBe(zone);
        }

        [Fact]
        public void ZoneWins_WhenMoreThanOneAddressIsSet()
        {
            // Defensive rather than expected. The writer sets exactly one, but a hand-edited
            // config.json is a supported thing to have and the precedence should be stated
            // somewhere rather than being whatever the property happened to check first.
            new SavedPerKeyCell { Zone = 1, Led = 5, Lamp = 9 }.Key.Should().Be("zone:1");
            new SavedPerKeyCell { Led = 5, Lamp = 9 }.Key.Should().Be("led:5");
        }

        [Fact]
        public void UnaddressedCell_HasNoKey()
        {
            // Must be empty rather than something like "led:-1", because the restore path uses an
            // empty key to mean "skip this". A junk key that parses would be inserted into the
            // lookup and could match another junk cell.
            new SavedPerKeyCell().Key.Should().BeEmpty();
        }

        [Fact]
        public void Defaults_AreAnUnpaintedCellAtFullLevel()
        {
            var cell = new SavedPerKeyCell();

            cell.Color.Should().Be("#000000");
            cell.Level.Should().Be(100, "an unset level must not silently dim a restored picture");
            cell.Led.Should().Be(-1);
            cell.Lamp.Should().Be(-1);
            cell.Zone.Should().Be(-1);
        }

        [Fact]
        public void StoredPicture_RoundTripsThroughItsOwnLookup()
        {
            // The restore path builds exactly this dictionary. A duplicate key would mean one cell
            // silently overwriting another, so the count is part of the claim.
            var saved = new List<SavedPerKeyCell>
            {
                new() { Led = 0, Color = "#FF0000", Level = 100 },
                new() { Led = 1, Color = "#00FF00", Level = 50 },
                new() { Zone = 0, Color = "#0000FF", Level = 25 }
            };

            var lookup = new Dictionary<string, SavedPerKeyCell>();
            foreach (var cell in saved)
                if (cell.Key.Length > 0) lookup[cell.Key] = cell;

            lookup.Should().HaveCount(3);
            lookup["led:1"].Color.Should().Be("#00FF00");
            lookup["led:1"].Level.Should().Be(50);
            lookup["zone:0"].Color.Should().Be("#0000FF");
        }

        [Fact]
        public void KeyboardLightingSettings_StartsWithNoPicture()
        {
            // Empty is what makes startup restore skip. A default that looked like a real picture
            // would blank the keyboard on first run of a new install.
            var settings = new KeyboardLightingSettings();

            settings.PerKeyPicture.Should().BeEmpty();
            settings.PerKeyBrightness.Should().Be(100);
            settings.RestorePerKeyPictureOnStartup.Should().BeTrue();
        }
    }
}
