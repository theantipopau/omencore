using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace OmenCore.Hardware
{
    /// <summary>
    /// The per-key keyboard MCU's own command surface, over HID interface <c>mi_03</c>.
    ///
    /// This is the second of the two lighting paths a per-key OMEN keyboard exposes, and the two
    /// are not interchangeable:
    ///
    ///   mi_04  <see cref="HidLampArray"/>  standard HID LampArray. STATIC per-key colour. One
    ///          report lights a key and it persists after the writing process exits. No readback.
    ///   mi_03  this class                  the MCU's vendor protocol. The ANIMATION ENGINE - all
    ///          twelve effects OMEN Gaming Hub offers are one frame each, rendered device-side with
    ///          no host involvement. Has a real state readback.
    ///
    /// The MCU also owns the STATIC COLOUR MAP via commands 0x05/0x06/0x07 - the same path OGH
    /// uses. A colour set this way is held by the MCU and survives the Fn overlay, because the
    /// MCU can redraw its own picture. The LampArray (mi_04) path requires host ownership, and
    /// a picture set that way is lost after Fn because the MCU has no base layer to restore.
    ///
    /// PROTOCOL PROVENANCE. Derived from OMEN Gaming Hub 1101.2607.3.0 - McuSDK2.dll's
    /// <c>GeneralCommandHelper</c> and <c>StarmadeKbLightingEffectCommandHelper</c> - and confirmed
    /// byte-for-byte against a USBPcap capture of OGH driving board 8D87, then driven independently.
    /// It is NOT the McuSDK v1 <c>KeyboardCommandHelper</c> path, whose 23-byte record and
    /// BLength = 22 belong to a different HP keyboard and mismatch this wire. Full map:
    /// omen-max-16/reference/keyboard-mcu.md.
    ///
    /// SCOPE. Measured on ONE machine: board 8D87, BIOS F.07, EC 40.38, Darfon 0D62:54BF. The
    /// device list below is deliberately narrow for that reason - these are vendor commands, and a
    /// keyboard that does not speak them may do something other than ignore them.
    ///
    /// WHAT AN ACK MEANS. Every command is answered <c>EC AC</c> (accepted) or <c>EC FA</c>
    /// (refused). Accepted means the frame parsed. It does not mean anything lit: during a stuck
    /// state on 8D87 every frame HP's own client sent was acknowledged and the keyboard stayed
    /// dark. <see cref="TryReadEffect"/> is the stronger check, and a person looking is stronger
    /// still.
    /// </summary>
    public sealed class DojoKeyboardMcu : IDisposable
    {
        // ── Identity ───────────────────────────────────────────────────────────────

        private const ushort DarfonVid = 0x0D62;

        /// <summary>
        /// PIDs HP's own device table pairs with <c>mi_03</c> + <c>DojoPerKeyRGB</c>. 0x54BF is
        /// "HP Gaming Keyboard II", measured on 8D87; 0x30BF sits beside it in HP's list and is
        /// unmeasured here.
        /// </summary>
        private static readonly ushort[] KnownPids = { 0x54BF, 0x30BF };

        /// <summary>
        /// The PIDs this protocol has actually been driven against, as opposed to the ones HP's own
        /// table pairs with this code path.
        ///
        /// The difference matters to a user. `0x54BF` was measured on 8D87 - twelve effects, a state
        /// readback, confirmed by eye. `0x30BF` sits beside it in HP's device list and has never been
        /// seen here, so a machine carrying it gets the same code on the strength of HP's table
        /// alone. That is a reasonable bet and it is not evidence, and the UI says which one a user
        /// is on rather than presenting both as equally established.
        /// </summary>
        private static readonly ushort[] VerifiedPids = { 0x54BF };

        /// <summary>Whether this device's protocol support was confirmed on hardware, or inferred
        /// from HP's device table.</summary>
        public bool IsVerifiedDevice => VerifiedPids.Contains(ProductId);

        /// <summary>
        /// The vendor collection, not the keyboard one. A composite keyboard exposes several
        /// interfaces under one PID and only this one carries lighting - selecting on VID:PID
        /// alone takes whichever Windows enumerated first, which is usually the wrong one.
        /// </summary>
        private const string VendorInterface = "MI_03";

        // ── Frame layout ───────────────────────────────────────────────────────────

        // Reports are 65 bytes: a report id of 0 that is stripped on the wire, then the frame.
        // Wire offset N therefore lives at buffer offset N+1, in both directions.
        private const int ReportLength = 65;
        private const byte ReportId = 0x00;
        private const int PayloadAt = 5;         // buffer index of wire payload[0]
        private const int PayloadCapacity = 60;

        private const byte AckHigh = 0xEC;       // payload[0] of every reply
        private const byte AckOk = 0xAC;         // payload[1]: accepted
        private const byte AckRefused = 0xFA;    //             refused

        // ── Commands ───────────────────────────────────────────────────────────────

        private const byte CmdSetLightingOnOff = 0x09;
        private const byte CmdStoreLightingToFlash = 0x0A;
        private const byte CmdSetBrightness = 0x0C;
        private const byte CmdSetLightingEffect = 0x03;
        private const byte CmdSetKeyR = 0x05;
        private const byte CmdSetKeyG = 0x06;
        private const byte CmdSetKeyB = 0x07;
        private const byte CmdRestoreDefault = 0x10;
        private const byte CmdGetLightingEffect = 0x83;

        // OGH's key map on 8D87 is 176 entries, matching HP's own layout resource: the pre-26C1
        // DojoKBKeysGlobalData.json declares 180 and SetNullBytes zeroes 176-179 as padding. Every
        // position below that is a real LED - 8D87 is cycle 25C1, so the cycle > 260 row that would
        // blank 137, 138 and 146 does not apply, and lighting those positions confirms they are
        // KeyDot and the last cell of KeyShiftR. Three pages per channel, BLength declared as 0 on
        // all nine pages while each carries PayloadCapacity colour bytes.
        public const int ColorMapLength = 176;
        private const int ColorPages = 3;

        /// <summary>LightingEffectTarget.ALL_LED_AREA - the only target a keyboard has.</summary>
        private const byte TargetAllLedArea = 0x00;

        /// <summary>Declared length of the effect record. Six colour slots reach the device but
        /// only four are inside this length, so only four are written here.</summary>
        private const int EffectRecordLength = 36;

        private static readonly byte[] FlashMagic = { 0xAC, 0x53 };
        private static readonly byte[] RestoreMagic = { 0x94, 0x10, 0x98, 0x27 };
        private const byte RestoreIndex = 7;

        // Field offsets inside the effect record.
        private const int FxEffect = 0, FxShowMode = 1, FxColorNumber = 2, FxLedSpeed = 3;
        private const int FxBrightness = 4, FxDirection = 5, FxRippleSize = 6, FxRaindropFreq = 7;
        private const int FxInnerBrightness = 8, FxOuterBrightness = 9;
        private const int FxColor0 = 24, FxMaxColors = 4;

        private readonly IntPtr _handle;
        private bool _disposed;

        public ushort VendorId { get; private init; }
        public ushort ProductId { get; private init; }
        public string ProductName { get; private init; } = string.Empty;
        public string DevicePath { get; private init; } = string.Empty;

        private DojoKeyboardMcu(IntPtr handle) => _handle = handle;

        // ── Effect vocabulary ──────────────────────────────────────────────────────

        /// <summary>
        /// Wire values from <c>LightingEffectType</c>. NOT the dropdown order, and NOT the light
        /// bar's numbering - the BIOS four-zone path uses different values for the same names and
        /// has no Ghosting, Ripple or OMEN X at all. Four numbering schemes exist for these twelve
        /// names; always say which one a number belongs to.
        /// </summary>
        public enum Effect : byte
        {
            Off = 0,
            Breathing = 2,
            ColorCycle = 4,
            Starlight = 7,
            Ghosting = 8,
            Ripple = 9,
            Wave = 10,
            Raindrop = 12,
            OmenX = 13,
            AudioPulse = 14,
            Confetti = 15,
            Sun = 16,
            Swipe = 17
        }

        /// <summary>
        /// Merges two ideas: 0 and 1 say how many CUSTOM colours the record carries, 2..5 name a
        /// preset palette instead. The colour block is only meaningful for 0 and 1.
        /// </summary>
        public enum ShowMode : byte
        {
            SingleCustomColor = 0,
            MultipleCustomColors = 1,
            Volcano = 2,
            Jungle = 3,
            Ocean = 4,
            Rainbow = 5
        }

        public enum EffectSpeed : byte { Slow = 0, Medium = 1, Fast = 2 }

        /// <summary>Wire order. The UI enum swaps the first two, so a value copied out of OGH's
        /// view-model is not this.</summary>
        public enum EffectDirection : byte
        {
            Inward = 0, Outward = 1, RightToLeft = 2, LeftToRight = 3,
            Up = 4, Down = 5, Clockwise = 6, CounterClockwise = 7
        }

        /// <summary>Sentinel for ColorNumber meaning "a preset is selected, ignore the colours".
        /// The field is otherwise a ZERO-BASED count: four custom colours send 3.</summary>
        public const byte ColorNumberPreset = 4;

        /// <summary>One effect as the MCU holds it. Colours are RGB.</summary>
        public struct EffectRecord
        {
            public Effect Effect;
            public ShowMode ShowMode;
            public byte ColorNumber;
            public EffectSpeed Speed;
            public byte Brightness;
            public EffectDirection Direction;
            public byte RippleSize;
            public byte RaindropFrequency;

            /// <summary>Audio Pulse only: the high-band level. Host-fed, see the remarks on
            /// <see cref="SetEffect"/>.</summary>
            public byte InnerBrightness;

            /// <summary>Audio Pulse only: the low-band level.</summary>
            public byte OuterBrightness;

            /// <summary>Up to four RGB triples. Ignored unless ShowMode is a custom mode.</summary>
            public (byte R, byte G, byte B)[] Colors;
        }

        // ── Opening ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Open the MCU's vendor interface, or null if this machine has no such keyboard.
        ///
        /// Null is the ordinary answer on most hardware and is not an error - it means the caller
        /// should fall back to another lighting path, not that something failed.
        /// </summary>
        public static DojoKeyboardMcu? Open()
        {
            foreach (string path in EnumerateHidPaths())
            {
                if (!LooksLikeTheVendorInterface(path)) continue;

                var device = TryOpen(path);
                if (device != null) return device;
            }

            return null;
        }

        /// <summary>
        /// Every candidate path with the reason it was or was not taken. Exists so a field report
        /// from an unsupported model is actionable: the interesting failure is "the right VID:PID
        /// was there and mi_03 was missing", which a bare null cannot express.
        /// </summary>
        public static IReadOnlyList<string> DescribeCandidates()
        {
            var lines = new List<string>();

            foreach (string path in EnumerateHidPaths())
            {
                if (path.IndexOf($"VID_{DarfonVid:X4}", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                string pid = KnownPids.Any(p => path.IndexOf($"PID_{p:X4}", StringComparison.OrdinalIgnoreCase) >= 0)
                    ? "known PID"
                    : "UNKNOWN PID";
                string mi = path.IndexOf(VendorInterface, StringComparison.OrdinalIgnoreCase) >= 0
                    ? VendorInterface
                    : "not " + VendorInterface;

                lines.Add($"{pid}, {mi} : {path}");
            }

            return lines;
        }

        private static bool LooksLikeTheVendorInterface(string path) =>
            path.IndexOf($"VID_{DarfonVid:X4}", StringComparison.OrdinalIgnoreCase) >= 0 &&
            path.IndexOf(VendorInterface, StringComparison.OrdinalIgnoreCase) >= 0 &&
            KnownPids.Any(p => path.IndexOf($"PID_{p:X4}", StringComparison.OrdinalIgnoreCase) >= 0);

        private static DojoKeyboardMcu? TryOpen(string path)
        {
            IntPtr h = Native.CreateFileW(path, Native.GenericRead | Native.GenericWrite,
                                          Native.ShareReadWrite, IntPtr.Zero, Native.OpenExisting,
                                          Native.FileFlagOverlapped, IntPtr.Zero);
            if (h == Native.InvalidHandle) return null;

            try
            {
                var attrs = new Native.HiddAttributes { Size = Marshal.SizeOf<Native.HiddAttributes>() };
                if (!Native.HidD_GetAttributes(h, ref attrs)) { Native.CloseHandle(h); return null; }

                if (!Native.HidD_GetPreparsedData(h, out IntPtr preparsed))
                {
                    Native.CloseHandle(h);
                    return null;
                }

                ushort outLength;
                try
                {
                    var caps = new Native.HidpCaps();
                    Native.HidP_GetCaps(preparsed, ref caps);
                    outLength = caps.OutputReportByteLength;
                }
                finally { Native.HidD_FreePreparsedData(preparsed); }

                // The report size is the last structural check available before sending vendor
                // bytes. An interface that cannot carry a 65-byte report is not this protocol
                // whatever its path says.
                if (outLength != ReportLength) { Native.CloseHandle(h); return null; }

                string product = string.Empty;
                var nameBuffer = new byte[256];
                if (Native.HidD_GetProductString(h, nameBuffer, nameBuffer.Length))
                    product = Encoding.Unicode.GetString(nameBuffer).Split('\0')[0];

                return new DojoKeyboardMcu(h)
                {
                    VendorId = attrs.VendorID,
                    ProductId = attrs.ProductID,
                    ProductName = product,
                    DevicePath = path
                };
            }
            catch
            {
                Native.CloseHandle(h);
                return null;
            }
        }

        // ── Commands ───────────────────────────────────────────────────────────────

        /// <summary>Blank or unblank the backlight without disturbing the installed effect.</summary>
        public bool SetLightingEnabled(bool enabled) =>
            Send(CmdSetLightingOnOff, 0, new byte[] { (byte)(enabled ? 1 : 0) });

        /// <summary>
        /// Set the MCU's own brightness. EXPECT THIS TO FAIL ON 8D87.
        ///
        /// Command 0x0C is HP's, read out of McuSDK2, and OGH sends it - but measured on this board
        /// the frame is not acknowledged at any payload value (0, 1, 2, 3, 50, 100), while 0x03,
        /// 0x83, 0x09, 0x0A and 0x10 are all acknowledged through the same handle and the same
        /// frame builder. Five accepted and one refused across six payloads is the command being
        /// rejected, not a bad value or a broken transport. It is real somewhere; not here.
        ///
        /// It stays because it costs one frame and is the right lever on a board that implements
        /// it. Brightness for STATIC colour does not depend on it:
        /// <see cref="Services.KeyboardLighting.DojoPerKeyBackend"/> scales the picture on the
        /// host, because the 176-entry colour map is raw RGB with no intensity channel of its own.
        /// </summary>
        public bool SetBrightness(byte level) =>
            Send(CmdSetBrightness, 0, new[] { level });

        /// <summary>
        /// Install an effect. One frame, and it holds with no host process running - there is no
        /// repaint loop and no keep-alive on this path.
        ///
        /// TWO THINGS THAT SURPRISE CALLERS:
        ///
        /// 1. The MCU MERGES; it does not replace. A frame only updates the fields the selected
        ///    effect actually consumes, and everything else keeps its previous value. So you cannot
        ///    clear a field by sending zero unless the current effect consumes it - a frame that
        ///    "sets everything to defaults" does no such thing. To put the record into a known
        ///    state, write each field while an effect that consumes it is selected, then switch.
        ///
        /// 2. Two effects render BLACK when driven naively, and neither is a failure.
        ///    <see cref="Effect.Swipe"/> has no preset palette, so it needs custom colours -
        ///    sent with a preset it shows nothing. <see cref="Effect.AudioPulse"/> IS its two
        ///    level bytes: with Inner/Outer at 0 it is black by definition. Reproducing it means a
        ///    ~5 Hz loop feeding those two bytes from an audio thread, which is what OGH does.
        ///
        /// Volatile until <see cref="StoreToFlash"/>.
        /// </summary>
        public bool SetEffect(EffectRecord record)
        {
            var payload = new byte[EffectRecordLength];
            payload[FxEffect] = (byte)record.Effect;
            payload[FxShowMode] = (byte)record.ShowMode;
            payload[FxColorNumber] = record.ColorNumber;
            payload[FxLedSpeed] = (byte)record.Speed;
            payload[FxBrightness] = record.Brightness;
            payload[FxDirection] = (byte)record.Direction;
            payload[FxRippleSize] = record.RippleSize;

            // OGH assigns this the same value as LedSpeed for every effect, so the two always
            // match in a capture. Separating them is untested rather than known to work.
            payload[FxRaindropFreq] = record.RaindropFrequency;

            payload[FxInnerBrightness] = record.InnerBrightness;
            payload[FxOuterBrightness] = record.OuterBrightness;

            var colors = record.Colors ?? Array.Empty<(byte, byte, byte)>();
            for (int i = 0; i < Math.Min(colors.Length, FxMaxColors); i++)
            {
                int at = FxColor0 + (i * 3);
                payload[at] = colors[i].R;
                payload[at + 1] = colors[i].G;
                payload[at + 2] = colors[i].B;
            }

            return Send(CmdSetLightingEffect, TargetAllLedArea, payload);
        }

        /// <summary>
        /// Read the installed effect back, or null if the device refused or did not answer.
        ///
        /// This is the merged state the MCU is holding, not an echo of the last write - which is
        /// what makes it worth reading, and also means a matching readback does not prove the
        /// firmware took the field from the frame you just sent.
        /// </summary>
        public EffectRecord? TryReadEffect()
        {
            if (!Write(CmdGetLightingEffect, TargetAllLedArea, 0, Array.Empty<byte>())) return null;

            byte[]? reply = Read();
            if (reply == null) return null;

            // Reaching colour 4 needs payload[35], which is buffer[40].
            if (reply.Length < PayloadAt + FxColor0 + (FxMaxColors * 3)) return null;
            if (reply[PayloadAt] == AckHigh && reply[PayloadAt + 1] == AckRefused) return null;

            byte P(int offset) => reply[PayloadAt + offset];

            var colors = new (byte R, byte G, byte B)[FxMaxColors];
            for (int i = 0; i < FxMaxColors; i++)
            {
                int at = FxColor0 + (i * 3);
                colors[i] = (P(at), P(at + 1), P(at + 2));
            }

            return new EffectRecord
            {
                Effect = (Effect)P(FxEffect),
                ShowMode = (ShowMode)P(FxShowMode),
                ColorNumber = P(FxColorNumber),
                Speed = (EffectSpeed)P(FxLedSpeed),
                Brightness = P(FxBrightness),
                Direction = (EffectDirection)P(FxDirection),
                RippleSize = P(FxRippleSize),
                RaindropFrequency = P(FxRaindropFreq),
                InnerBrightness = P(FxInnerBrightness),
                OuterBrightness = P(FxOuterBrightness),
                Colors = colors
            };
        }

        /// <summary>
        /// Persist the current lighting across a power cycle.
        ///
        /// THIS IS A REAL FLASH WRITE to the MCU. It is why an effect survives a reboot, and why
        /// it must never go in a loop or be called once per frame of an animation. Call it when a
        /// user saves a profile, not when one is applied.
        /// </summary>
        public bool StoreToFlash() =>
            Send(CmdStoreLightingToFlash, TargetAllLedArea, FlashMagic);

        /// <summary>
        /// Reset the lighting to HP's firmware defaults.
        ///
        /// Worth knowing about before the next apparently-bricked keyboard: this is a firmware
        /// level reset over the ordinary command surface, so it does not need an EC drain or a
        /// teardown.
        /// </summary>
        public bool RestoreFirmwareDefaults() =>
            Send(CmdRestoreDefault, RestoreIndex, RestoreMagic);

        /// <summary>
        /// Set every key to one colour via the MCU's own static colour map (commands 0x05/0x06/0x07).
        ///
        /// This is the path OGH uses for per-key colour: command 0x09 with payload 0x01 switches
        /// to the static display mode, then three channels of colour pages fill the 176-entry map.
        /// The MCU holds the map and redraws it after the Fn overlay — which is the property the
        /// LampArray path does not have, and the reason this method exists.
        ///
        /// Raw, like everything on this interface: the map has no intensity channel, so these bytes
        /// are what the MCU draws. Anything that owes a user a brightness setting scales before it
        /// gets here.
        ///
        /// Volatile until <see cref="StoreToFlash"/>.
        /// </summary>
        public bool SetStaticColor(byte r, byte g, byte b)
        {
            var rc = new byte[ColorMapLength];
            var gc = new byte[ColorMapLength];
            var bc = new byte[ColorMapLength];
            Array.Fill(rc, r);
            Array.Fill(gc, g);
            Array.Fill(bc, b);

            return SetStaticColorMap(rc, gc, bc);
        }

        /// <summary>
        /// Set every key INDIVIDUALLY via the same static colour map, one entry per LED position.
        ///
        /// This is the only way a per-key picture survives the Fn overlay. The MCU redraws its base
        /// layer from its own state when Fn is released, and this map is that state; a picture
        /// painted onto the mi_04 LampArray is not, so the display stays on the overlay frame
        /// indefinitely. Anything that wants a picture to STAY belongs here rather than there.
        ///
        /// Each channel is indexed by LED position and must be <see cref="ColorMapLength"/> long.
        /// Every position is live on this board, so a colour reaches all of them.
        ///
        /// Volatile until <see cref="StoreToFlash"/>.
        /// </summary>
        public bool SetStaticColorMap(ReadOnlySpan<byte> r, ReadOnlySpan<byte> g, ReadOnlySpan<byte> b)
        {
            if (r.Length != ColorMapLength || g.Length != ColorMapLength || b.Length != ColorMapLength)
                throw new ArgumentException(
                    $"Each channel must be {ColorMapLength} entries, one per LED position; " +
                    $"got {r.Length}/{g.Length}/{b.Length}.");

            // Command 0x09 with payload 0x01 selects the static display mode. OGH sends it ahead of
            // every colour round and so does this - without it the colour pages land in a map the
            // effect engine is not displaying.
            if (!Send(CmdSetLightingOnOff, 0, new byte[] { 0x01 })) return false;

            return SendColorPages(CmdSetKeyR, r)
                && SendColorPages(CmdSetKeyG, g)
                && SendColorPages(CmdSetKeyB, b);
        }

        private bool SendColorPages(byte command, ReadOnlySpan<byte> channel)
        {
            for (byte page = 0; page < ColorPages; page++)
            {
                int from = page * PayloadCapacity;
                int fill = Math.Min(PayloadCapacity, ColorMapLength - from);

                // The payload is always the full 60 bytes even on the last page, where only 56 of
                // them are map entries - HP's page 2 carries 56 colour bytes and four zeros. BLength
                // is declared 0 on all nine pages, which is what the capture shows on the wire.
                var payload = new byte[PayloadCapacity];
                for (int i = 0; i < fill; i++)
                    payload[i] = channel[from + i];

                if (!Send(command, page, 0, payload)) return false;
            }
            return true;
        }

        // ── Wire ───────────────────────────────────────────────────────────────────

        /// <summary>Write a frame and require an <c>EC AC</c> reply. False covers refused,
        /// unanswered and failed-to-write alike; the caller cannot act differently on those.</summary>
        private bool Send(byte command, byte index, byte[] payload) =>
            Send(command, index, payload.Length, payload);

        private bool Send(byte command, byte index, int bLength, byte[] payload)
        {
            if (!Write(command, index, bLength, payload)) return false;

            byte[]? reply = Read();
            if (reply == null || reply.Length < PayloadAt + 2) return false;

            return reply[PayloadAt] == AckHigh && reply[PayloadAt + 1] == AckOk;
        }

        private bool Write(byte command, byte index, int bLength, byte[] payload)
        {
            ThrowIfDisposed();
            if (payload.Length > PayloadCapacity)
                throw new ArgumentException($"payload is {payload.Length} bytes, max {PayloadCapacity}", nameof(payload));

            var report = new byte[ReportLength];
            report[0] = ReportId;
            report[1] = command;
            report[2] = index;
            report[3] = (byte)(bLength & 0xFF);
            report[4] = (byte)(bLength >> 8);
            payload.CopyTo(report, PayloadAt);

            return Transfer(report, write: true) > 0;
        }

        private byte[]? Read()
        {
            var buffer = new byte[ReportLength];
            return Transfer(buffer, write: false) > 0 ? buffer : null;
        }

        /// <summary>
        /// Overlapped rather than synchronous so a device that never answers times out instead of
        /// hanging the caller forever inside ReadFile. A keyboard that does not reply is a result.
        /// </summary>
        private int Transfer(byte[] buffer, bool write)
        {
            using var completed = new ManualResetEvent(false);
            var overlapped = new NativeOverlapped
            {
                EventHandle = completed.SafeWaitHandle.DangerousGetHandle()
            };

            bool started = write
                ? Native.WriteFile(_handle, buffer, (uint)buffer.Length, out uint transferred, ref overlapped)
                : Native.ReadFile(_handle, buffer, (uint)buffer.Length, out transferred, ref overlapped);

            if (!started)
            {
                if (Marshal.GetLastWin32Error() != Native.ErrorIoPending) return 0;

                if (!completed.WaitOne(IoTimeout))
                {
                    Native.CancelIo(_handle);
                    return 0;
                }

                if (!Native.GetOverlappedResult(_handle, ref overlapped, out transferred, true)) return 0;
            }

            return (int)transferred;
        }

        private static readonly TimeSpan IoTimeout = TimeSpan.FromMilliseconds(1500);

        private static IEnumerable<string> EnumerateHidPaths()
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'HID%'");

            foreach (System.Management.ManagementObject o in searcher.Get())
            {
                string? id = o["PNPDeviceID"]?.ToString();
                if (string.IsNullOrEmpty(id)) continue;
                yield return $@"\\?\{id.Replace('\\', '#')}#{{4d1e55b2-f16f-11cf-88cb-001111000030}}";
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DojoKeyboardMcu));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_handle != IntPtr.Zero && _handle != Native.InvalidHandle) Native.CloseHandle(_handle);
        }

        private static class Native
        {
            internal const uint GenericRead = 0x80000000;
            internal const uint GenericWrite = 0x40000000;
            internal const uint ShareReadWrite = 0x03;
            internal const uint OpenExisting = 3;
            internal const uint FileFlagOverlapped = 0x40000000;
            internal const int ErrorIoPending = 997;
            internal static readonly IntPtr InvalidHandle = new(-1);

            [StructLayout(LayoutKind.Sequential)]
            internal struct HiddAttributes { public int Size; public ushort VendorID, ProductID, VersionNumber; }

            [StructLayout(LayoutKind.Sequential)]
            internal struct HidpCaps
            {
                public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
                [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
                public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps,
                              NumberInputDataIndices, NumberOutputButtonCaps, NumberOutputValueCaps,
                              NumberOutputDataIndices, NumberFeatureButtonCaps, NumberFeatureValueCaps,
                              NumberFeatureDataIndices;
            }

            [DllImport("hid.dll")] internal static extern bool HidD_GetAttributes(IntPtr h, ref HiddAttributes a);
            [DllImport("hid.dll")] internal static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr p);
            [DllImport("hid.dll")] internal static extern bool HidD_FreePreparsedData(IntPtr p);
            [DllImport("hid.dll")] internal static extern int HidP_GetCaps(IntPtr p, ref HidpCaps c);
            [DllImport("hid.dll")] internal static extern bool HidD_GetProductString(IntPtr h, byte[] b, int len);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern IntPtr CreateFileW(string path, uint access, uint share, IntPtr sa,
                                                      uint disposition, uint flags, IntPtr template);
            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern bool WriteFile(IntPtr h, byte[] b, uint len, out uint written, ref NativeOverlapped o);
            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern bool ReadFile(IntPtr h, byte[] b, uint len, out uint read, ref NativeOverlapped o);
            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern bool GetOverlappedResult(IntPtr h, ref NativeOverlapped o, out uint transferred, bool wait);
            [DllImport("kernel32.dll")] internal static extern bool CancelIo(IntPtr h);
            [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr h);
        }
    }
}
