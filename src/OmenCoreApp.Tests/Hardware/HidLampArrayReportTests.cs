using System.Collections.Generic;
using FluentAssertions;
using OmenCore.Hardware;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    /// <summary>
    /// Pins the HID LampArray report byte layouts.
    ///
    /// These are worth testing precisely because getting them wrong does not fail loudly. A
    /// misplaced offset produces a report the device accepts and acts on - it just lights the
    /// wrong lamp, or the right lamp in the wrong colour. There is no colour readback in the
    /// LampArray spec to catch it with, so the byte layout is the last place it can be checked
    /// automatically.
    ///
    /// Layouts are from the HID Lighting And Illumination usage page (0x59). The 51-byte
    /// LampMultiUpdateReport size is corroborated by hardware: both LampArrays on the board this
    /// was written against report exactly 51-byte feature reports.
    /// </summary>
    public class HidLampArrayReportTests
    {
        private const int ReportLength = 51;

        [Fact]
        public void RangeUpdate_PlacesEveryFieldWhereTheSpecSaysIt()
        {
            var report = HidLampArray.BuildRangeUpdateReport(ReportLength, 0x0102, 0x0304, 10, 20, 30, 40);

            report[0].Should().Be(5, "LampRangeUpdateReport is report id 5");
            report[1].Should().Be(0x01, "LampUpdateComplete must be set or the device waits for more");
            report[2].Should().Be(0x02, "LampIdStart is little-endian u16");
            report[3].Should().Be(0x01);
            report[4].Should().Be(0x04, "LampIdEnd is little-endian u16");
            report[5].Should().Be(0x03);
            report[6].Should().Be(10);
            report[7].Should().Be(20);
            report[8].Should().Be(30);
            report[9].Should().Be(40, "intensity is the fourth channel, after RGB");
        }

        [Fact]
        public void MultiUpdate_ChannelsStartAfterAllEightIdSlots()
        {
            // The trap this guards. The id and channel arrays are both sized for eight lamps
            // whether or not all eight are used, so a partial batch must still leave the unused id
            // slots in place. Packing channels immediately after the ids in use would shift every
            // channel forward and misalign the whole batch.
            var lamps = new List<HidLampArray.LampColor>
            {
                new(0x1111, 1, 2, 3, 4),
                new(0x2222, 5, 6, 7, 8)
            };

            var report = HidLampArray.BuildMultiUpdateReport(ReportLength, lamps, 0, 2, isLastBatch: true);

            report[0].Should().Be(4, "LampMultiUpdateReport is report id 4");
            report[1].Should().Be(2, "LampCount");
            report[2].Should().Be(0x01, "LampUpdateComplete on the final batch");

            // Ids at 3, packed u16.
            report[3].Should().Be(0x11);
            report[4].Should().Be(0x11);
            report[5].Should().Be(0x22);
            report[6].Should().Be(0x22);

            // Unused id slots stay zero rather than being overwritten by channel data.
            for (int i = 7; i < 19; i++) report[i].Should().Be(0, $"id slot byte {i} is unused");

            // Channels at 3 + 8*2 = 19, regardless of how few lamps are in this batch.
            report[19].Should().Be(1);
            report[20].Should().Be(2);
            report[21].Should().Be(3);
            report[22].Should().Be(4);
            report[23].Should().Be(5);
            report[24].Should().Be(6);
            report[25].Should().Be(7);
            report[26].Should().Be(8);
        }

        [Fact]
        public void MultiUpdate_FullBatchFillsTheReportExactly()
        {
            // Eight lamps is the most one report holds, and it should reach byte 50 of 51 - which
            // is why the 51-byte feature report length seen on real hardware is corroboration
            // rather than coincidence.
            var lamps = new List<HidLampArray.LampColor>();
            for (ushort i = 0; i < 8; i++) lamps.Add(new HidLampArray.LampColor(i, 255, 255, 255, 255));

            var report = HidLampArray.BuildMultiUpdateReport(ReportLength, lamps, 0, 8, isLastBatch: true);

            report[50].Should().Be(255, "the eighth lamp's intensity is the final byte");
            report[19 + (8 * 4) - 1].Should().Be(255);
        }

        [Fact]
        public void MultiUpdate_OnlyTheFinalBatchIsFlaggedComplete()
        {
            // A non-final batch flagged complete makes the device apply a partial frame, which
            // tears visibly across a large update.
            var lamps = new List<HidLampArray.LampColor>();
            for (ushort i = 0; i < 16; i++) lamps.Add(new HidLampArray.LampColor(i, 1, 1, 1));

            var first = HidLampArray.BuildMultiUpdateReport(ReportLength, lamps, 0, 8, isLastBatch: false);
            var last = HidLampArray.BuildMultiUpdateReport(ReportLength, lamps, 8, 8, isLastBatch: true);

            first[2].Should().Be(0x00);
            last[2].Should().Be(0x01);
        }

        [Fact]
        public void MultiUpdate_ReadsFromTheGivenOffset()
        {
            var lamps = new List<HidLampArray.LampColor>
            {
                new(0x0001, 11, 11, 11),
                new(0x0002, 22, 22, 22),
                new(0x0003, 33, 33, 33)
            };

            var report = HidLampArray.BuildMultiUpdateReport(ReportLength, lamps, offset: 2, count: 1, isLastBatch: true);

            report[3].Should().Be(0x03, "the batch starts at the requested offset, not at zero");
            report[19].Should().Be(33);
        }

        [Fact]
        public void LampIds_AreLittleEndian()
        {
            var lamps = new List<HidLampArray.LampColor> { new(0x00FF, 0, 0, 0) };
            var report = HidLampArray.BuildMultiUpdateReport(ReportLength, lamps, 0, 1, true);

            report[3].Should().Be(0xFF, "low byte first");
            report[4].Should().Be(0x00);
        }

        [Fact]
        public void Intensity_DefaultsToFull()
        {
            // A default of 0 would make every colour set through the convenience constructor
            // invisible, which looks exactly like the write not working.
            var lamp = new HidLampArray.LampColor(0, 255, 0, 0);
            lamp.Intensity.Should().Be(255);
        }
    }
}
