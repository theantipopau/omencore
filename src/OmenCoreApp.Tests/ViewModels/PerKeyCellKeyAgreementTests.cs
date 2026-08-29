using FluentAssertions;
using OmenCore.Models;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    /// <summary>
    /// Forces the editor's cell identity and the stored cell's identity to be the same string.
    ///
    /// These are written in two files by two different pieces of code — <c>KeyboardMapViewModel</c>
    /// composes a key for a live editor cell, <c>SavedPerKeyCell</c> composes one for a row of
    /// <c>config.json</c> — and the save/restore cycle works only while they agree exactly. Nothing
    /// in the type system connects them.
    ///
    /// The failure they guard against is silent in the worst way. A drifted format throws nothing
    /// and logs nothing: the restore looks up every cell, matches none, and leaves the editor at its
    /// defaults. To a user that is indistinguishable from the save never having happened, and to
    /// whoever investigates it looks like a bug in saving rather than in reading back.
    ///
    /// So these tests deliberately do NOT hard-code the strings. Asserting both sides produce
    /// "led:42" would pass just as happily if someone changed one side and updated the test to
    /// match. Asserting they equal EACH OTHER is the property that actually matters.
    /// </summary>
    public class PerKeyCellKeyAgreementTests
    {
        [Fact]
        public void LedCell_AgreesBetweenEditorAndStorage()
        {
            var editorCell = new KeyLampViewModel { LedPositions = new[] { 42 } };
            var storedCell = new SavedPerKeyCell { Led = 42 };

            KeyboardMapViewModel.CellKey(editorCell).Should().Be(storedCell.Key);
        }

        [Fact]
        public void LampCell_AgreesBetweenEditorAndStorage()
        {
            var editorCell = new KeyLampViewModel { LampIds = new ushort[] { 7 } };
            var storedCell = new SavedPerKeyCell { Lamp = 7 };

            KeyboardMapViewModel.CellKey(editorCell).Should().Be(storedCell.Key);
        }

        [Fact]
        public void LightBarCell_AgreesBetweenEditorAndStorage()
        {
            var editorCell = new KeyLampViewModel { IsLightBar = true, ZoneIndex = 3 };
            var storedCell = new SavedPerKeyCell { Zone = 3 };

            KeyboardMapViewModel.CellKey(editorCell).Should().Be(storedCell.Key);
        }

        [Fact]
        public void MultiLedKey_IsKeyedByItsFirstPosition()
        {
            // In layout mode a cell IS one LED, so this is normally a single-element list. The
            // first entry is pinned anyway because the saver and the restorer must pick the SAME
            // element of it, and "first" is only obvious until someone sorts the list.
            var editorCell = new KeyLampViewModel { LedPositions = new[] { 88, 89 } };

            KeyboardMapViewModel.CellKey(editorCell).Should().Be(new SavedPerKeyCell { Led = 88 }.Key);
        }

        [Fact]
        public void LightBarWins_OverAnyLampIdsTheCellAlsoCarries()
        {
            // The editor keeps bar zones in the same collection as the keys, so a bar cell can be
            // constructed carrying leftover addressing. Both sides must break the tie the same way.
            var editorCell = new KeyLampViewModel
            {
                IsLightBar = true,
                ZoneIndex = 1,
                LampIds = new ushort[] { 5 }
            };

            KeyboardMapViewModel.CellKey(editorCell)
                .Should().Be(new SavedPerKeyCell { Zone = 1, Lamp = 5 }.Key);
        }

        [Fact]
        public void UnaddressedCell_YieldsNoKeyOnEitherSide()
        {
            // Both sides must produce the empty string, because the restore path uses it to mean
            // "skip". One side inventing a placeholder would put junk in the lookup.
            KeyboardMapViewModel.CellKey(new KeyLampViewModel()).Should().BeEmpty();
            new SavedPerKeyCell().Key.Should().BeEmpty();
        }
    }
}
