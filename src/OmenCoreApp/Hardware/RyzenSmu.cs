using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using OmenCore.Models;

namespace OmenCore.Hardware
{
    /// <summary>
    /// Ryzen System Management Unit (SMU) communication layer, over PawnIO's signed
    /// <c>RyzenSMU</c> module.
    ///
    /// The module owns the PCI-config indirect access itself (it drives the address/data
    /// register pair internally) and exposes the SMU register window as two ioctls:
    /// <c>ioctl_read_smu_register</c> and <c>ioctl_write_smu_register</c>. This class layers
    /// the SMU mailbox handshake on top of those so that BOTH the MP1 and PSMU mailboxes stay
    /// reachable — the module's own <c>ioctl_send_smu_command</c> only ever talks to the one
    /// mailbox it picks per CPU code name, which on Strix Point is the PSMU triple, so it
    /// cannot serve the MP1 commands that Curve Optimizer and STAPM both need.
    ///
    /// Register addresses come from <see cref="RyzenControl.ConfigureSmuAddresses"/>.
    /// </summary>
    public sealed class RyzenSmu : IDisposable
    {
        public enum SmuStatus : int
        {
            Bad = 0x0,
            Ok = 0x1,
            Failed = 0xFF,
            UnknownCmd = 0xFE,
            CmdRejectedPrereq = 0xFD,
            CmdRejectedBusy = 0xFC
        }

        // MP1 (Power Management) mailbox addresses
        public uint Mp1AddrMsg { get; set; }
        public uint Mp1AddrRsp { get; set; }
        public uint Mp1AddrArg { get; set; }

        // PSMU (Platform SMU) mailbox addresses
        public uint PsmuAddrMsg { get; set; }
        public uint PsmuAddrRsp { get; set; }
        public uint PsmuAddrArg { get; set; }

        private const int SmuArgCount = 6;

        /// <summary>
        /// Wall-clock budget for each half of the mailbox handshake. The module performs its own
        /// retries in kernel mode, but this class polls from user mode where every read is a
        /// separate ioctl round trip - so this is bounded by elapsed time rather than by a raw
        /// iteration count. SMU commands complete in microseconds to low milliseconds.
        /// </summary>
        private const int SmuWaitTimeoutMs = 1000;

        /// <summary>
        /// Cross-process mutex guarding the SMU/PCI indirect register pair. This MUST be the
        /// same name LibreHardwareMonitor uses (<c>Global\Access_PCI</c>, which is what the
        /// PawnIO module's own docs ask callers to hold), because LHM drives the same mailboxes
        /// through the same module - both in-process and in the separate HardwareWorker process.
        /// A mailbox handshake is a multi-step read/modify sequence on a shared address/data
        /// register pair, so an unsynchronized second caller can interleave and corrupt it.
        /// </summary>
        private static readonly Mutex PciMutex = CreatePciMutex();
        private static bool _pciMutexIsGlobal;

        private const int PciMutexTimeoutMs = 5000;

        private bool _disposed;

        // PawnIO access
        private IntPtr _pawnIOLib = IntPtr.Zero;
        private IntPtr _handle = IntPtr.Zero;
        private bool _initialized;
        private bool _moduleLoaded;

        // PawnIO function delegates
        private delegate int PawnioOpen(out IntPtr handle);
        private delegate int PawnioLoad(IntPtr handle, byte[] blob, IntPtr size);
        private delegate int PawnioExecute(IntPtr handle, string name, ulong[] input, IntPtr inSize, ulong[] output, IntPtr outSize, out IntPtr returnSize);
        private delegate int PawnioClose(IntPtr handle);

        private PawnioOpen? _pawnioOpen;
        private PawnioLoad? _pawnioLoad;
        private PawnioExecute? _pawnioExecute;
        private PawnioClose? _pawnioClose;

        public bool IsAvailable => _initialized && _moduleLoaded && _handle != IntPtr.Zero;

        /// <summary>
        /// Why <see cref="IsAvailable"/> is false, in terms a user can act on. Empty when
        /// available. Distinguishes "PawnIO missing" from "PawnIO fine, this CPU is not a part
        /// the RyzenSMU module supports" - the module's own entry point refuses to load on any
        /// CPU outside families 17h/19h/1Ah or without a known mailbox, so a load rejection is
        /// an authoritative capability answer, not a plumbing failure.
        /// </summary>
        public string UnavailableReason { get; private set; } = "AMD SMU backend has not been initialized.";

        /// <summary>
        /// SMU firmware version reported by the module, or null if it could not be read.
        /// Informational only - a successful read is good evidence the mailbox round-trips,
        /// but this is deliberately NOT used to gate <see cref="IsAvailable"/>, because the
        /// module issues a fixed message id for this query whose meaning is not confirmed
        /// across every supported part.
        /// </summary>
        public uint? SmuVersion { get; private set; }

        private static Mutex CreatePciMutex()
        {
            try
            {
                var mutex = new Mutex(false, @"Global\Access_PCI");
                _pciMutexIsGlobal = true;
                return mutex;
            }
            catch (Exception ex)
            {
                // Creating a Global\ object can fail on a locked-down session. Fall back to a
                // process-local mutex so we degrade rather than throwing out of a static
                // initializer, but record it - cross-process coordination with LHM is lost.
                System.Diagnostics.Debug.WriteLine(
                    $"[RyzenSmu] Could not create Global\\Access_PCI mutex, falling back to process-local: {ex.Message}");
                _pciMutexIsGlobal = false;
                return new Mutex();
            }
        }

        /// <summary>
        /// True when the SMU/PCI mutex is the shared cross-process one. False means another
        /// EC/PCI consumer could interleave with our mailbox handshakes.
        /// </summary>
        public static bool HasCrossProcessPciLock => _pciMutexIsGlobal;

        /// <summary>
        /// Mirrors the RyzenSMU module's own <c>check_smu_register_range</c> guard. The module
        /// rejects anything outside these windows with STATUS_NOT_SUPPORTED, so validating here
        /// turns a silent opaque HRESULT into a diagnosable "this mailbox address is wrong".
        /// </summary>
        public static bool IsSmuRegisterAddressSupported(uint address)
        {
            // 0x3B10000-0x3B10FFF - SMU mailboxes
            if ((address & 0xFFFFF000u) == 0x3B10000u) return true;
            // 0x13000000-0x130000F0 - SMU mailboxes on pre-Ryzen
            if (address >= 0x13000000u && address <= 0x130000F0u) return true;
            // 0x56000-0x5AFFF - SMU SVI2 planes
            if (address >= 0x56000u && address <= 0x5AFFFu) return true;
            // 0x6F000-0x6FFFF - SMU extended SVI2 planes
            if ((address & 0xFFFFF000u) == 0x6F000u) return true;
            return false;
        }

        public bool Initialize()
        {
            if (_initialized) return IsAvailable;

            try
            {
                string? libPath = ResolvePawnIOLibPath();
                if (libPath == null)
                {
                    UnavailableReason =
                        "PawnIOLib.dll was not found. Install PawnIO from pawnio.eu, then restart OmenCore.";
                    return false;
                }

                _pawnIOLib = NativeMethods.LoadLibrary(libPath);
                if (_pawnIOLib == IntPtr.Zero)
                {
                    UnavailableReason =
                        $"PawnIOLib.dll could not be loaded (error {Marshal.GetLastWin32Error()}).";
                    return false;
                }

                if (!ResolveFunctions())
                {
                    UnavailableReason = "PawnIOLib.dll is present but its expected entry points could not be resolved.";
                    return false;
                }

                int hr = _pawnioOpen!(out _handle);
                if (hr < 0 || _handle == IntPtr.Zero)
                {
                    _handle = IntPtr.Zero;
                    UnavailableReason =
                        $"The PawnIO driver could not be opened (HRESULT 0x{hr:X8}). Run OmenCore as administrator, " +
                        "and reboot if PawnIO was just installed.";
                    return false;
                }

                if (!LoadSmuModule(out string moduleFailureReason))
                {
                    UnavailableReason = moduleFailureReason;
                    _pawnioClose!(_handle);
                    _handle = IntPtr.Zero;
                    return false;
                }

                _moduleLoaded = true;
                _initialized = true;
                UnavailableReason = string.Empty;

                // Informational liveness probe. Not a gate - see SmuVersion.
                if (TryGetSmuVersion(out uint version))
                {
                    SmuVersion = version;
                    System.Diagnostics.Debug.WriteLine(
                        $"[RyzenSmu] RyzenSMU module loaded; SMU version 0x{version:X8}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine(
                        "[RyzenSmu] RyzenSMU module loaded, but the SMU version query did not return a value");
                }

                if (!_pciMutexIsGlobal)
                {
                    System.Diagnostics.Debug.WriteLine(
                        "[RyzenSmu] WARNING: using a process-local PCI lock; SMU access is not coordinated with other tools");
                }

                return true;
            }
            catch (Exception ex)
            {
                UnavailableReason = $"AMD SMU initialization failed: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[RyzenSmu] Initialization error: {ex.Message}");
                return false;
            }
        }

        private string? ResolvePawnIOLibPath()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string bundledLibPath = Path.Combine(appDir, "drivers", "PawnIOLib.dll");
            if (File.Exists(bundledLibPath)) return bundledLibPath;

            string? pawnIOPath = FindPawnIOInstallation();
            if (pawnIOPath != null)
            {
                string installedLibPath = Path.Combine(pawnIOPath, "PawnIOLib.dll");
                if (File.Exists(installedLibPath)) return installedLibPath;
            }

            return null;
        }

        private string? FindPawnIOInstallation()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
                if (key != null)
                {
                    string? installLocation = key.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrEmpty(installLocation) && Directory.Exists(installLocation))
                    {
                        return installLocation;
                    }
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException
                                       or UnauthorizedAccessException
                                       or IOException)
            {
                // The uninstall key is unreadable under a restricted token or is mid-write.
                // Not fatal: the default install path below is checked next, and a genuine
                // absence surfaces as UnavailableReason rather than as an exception here.
                System.Diagnostics.Debug.WriteLine(
                    $"[RyzenSmu] Could not read the PawnIO uninstall key: {ex.Message}");
            }

            string defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PawnIO");
            if (Directory.Exists(defaultPath)) return defaultPath;

            return null;
        }

        private bool ResolveFunctions()
        {
            IntPtr openPtr = NativeMethods.GetProcAddress(_pawnIOLib, "pawnio_open");
            IntPtr loadPtr = NativeMethods.GetProcAddress(_pawnIOLib, "pawnio_load");
            IntPtr executePtr = NativeMethods.GetProcAddress(_pawnIOLib, "pawnio_execute");
            IntPtr closePtr = NativeMethods.GetProcAddress(_pawnIOLib, "pawnio_close");

            if (openPtr == IntPtr.Zero || loadPtr == IntPtr.Zero ||
                executePtr == IntPtr.Zero || closePtr == IntPtr.Zero)
            {
                return false;
            }

            _pawnioOpen = Marshal.GetDelegateForFunctionPointer<PawnioOpen>(openPtr);
            _pawnioLoad = Marshal.GetDelegateForFunctionPointer<PawnioLoad>(loadPtr);
            _pawnioExecute = Marshal.GetDelegateForFunctionPointer<PawnioExecute>(executePtr);
            _pawnioClose = Marshal.GetDelegateForFunctionPointer<PawnioClose>(closePtr);

            return true;
        }

        /// <summary>
        /// Loads the RyzenSMU PawnIO module. Without this the ioctl names below resolve to
        /// nothing - the handle is open but no code is bound behind it.
        /// </summary>
        private bool LoadSmuModule(out string failureReason)
        {
            string[] moduleNames = { "RyzenSMU.bin", "RyzenSMU.amx" };
            byte[]? module = null;

            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            foreach (var moduleName in moduleNames)
            {
                string modulePath = Path.Combine(appDir, "drivers", moduleName);
                if (File.Exists(modulePath))
                {
                    module = File.ReadAllBytes(modulePath);
                    break;
                }
            }

            if (module == null || module.Length == 0)
            {
                string? pawnIOPath = FindPawnIOInstallation();
                if (pawnIOPath != null)
                {
                    foreach (var moduleName in moduleNames)
                    {
                        string installedModule = Path.Combine(pawnIOPath, "modules", moduleName);
                        if (File.Exists(installedModule))
                        {
                            module = File.ReadAllBytes(installedModule);
                            break;
                        }
                    }
                }
            }

            if (module == null || module.Length == 0)
            {
                failureReason =
                    "The PawnIO RyzenSMU module was not found. Place RyzenSMU.bin in OmenCore's drivers folder " +
                    "or in %ProgramFiles%\\PawnIO\\modules, then restart OmenCore.";
                return false;
            }

            int hr = _pawnioLoad!(_handle, module, (IntPtr)module.Length);
            if (hr < 0)
            {
                // The module's own entry point validates CPU vendor, family (17h/19h/1Ah) and
                // that the detected part has a known mailbox, so a rejection here is the
                // module telling us this CPU is out of scope.
                failureReason =
                    $"The PawnIO RyzenSMU module refused to load on this CPU (HRESULT 0x{hr:X8}). " +
                    "This CPU is not one the module supports for SMU access.";
                return false;
            }

            failureReason = string.Empty;
            return true;
        }

        /// <summary>
        /// Send command to MP1 (Power Management) mailbox.
        /// </summary>
        public SmuStatus SendMp1(uint message, ref uint[] arguments)
        {
            return SendMsg(Mp1AddrMsg, Mp1AddrRsp, Mp1AddrArg, message, ref arguments);
        }

        /// <summary>
        /// Send command to PSMU (Platform SMU) mailbox.
        /// </summary>
        public SmuStatus SendPsmu(uint message, ref uint[] arguments)
        {
            return SendMsg(PsmuAddrMsg, PsmuAddrRsp, PsmuAddrArg, message, ref arguments);
        }

        /// <summary>
        /// Reads the SMU firmware version through the module's own helper.
        /// </summary>
        public bool TryGetSmuVersion(out uint version)
        {
            version = 0;
            if (!IsAvailable || _pawnioExecute == null) return false;

            if (!TryAcquirePciMutex()) return false;
            try
            {
                ulong[] output = new ulong[1];
                int hr = _pawnioExecute(_handle, "ioctl_get_smu_version",
                    Array.Empty<ulong>(), IntPtr.Zero, output, (IntPtr)1, out _);
                if (hr < 0) return false;

                version = (uint)output[0];
                return version != 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                ReleasePciMutex();
            }
        }

        private SmuStatus SendMsg(uint addrMsg, uint addrRsp, uint addrArg, uint msg, ref uint[] args)
        {
            if (!IsAvailable) return SmuStatus.Failed;

            // Catch an unconfigured or out-of-window mailbox before it becomes an opaque
            // driver error. RyzenControl leaves all six addresses at 0 for unknown families.
            if (addrMsg == 0 || addrRsp == 0 || addrArg == 0)
            {
                System.Diagnostics.Debug.WriteLine(
                    "[RyzenSmu] Mailbox addresses are not configured for this CPU family");
                return SmuStatus.Failed;
            }

            if (!IsSmuRegisterAddressSupported(addrMsg) ||
                !IsSmuRegisterAddressSupported(addrRsp) ||
                !IsSmuRegisterAddressSupported(addrArg + (4 * (SmuArgCount - 1))))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[RyzenSmu] Mailbox addresses outside the module's supported SMU register window " +
                    $"(msg 0x{addrMsg:X}, rsp 0x{addrRsp:X}, arg 0x{addrArg:X})");
                return SmuStatus.Failed;
            }

            uint[] cmdArgs = new uint[SmuArgCount];
            int argsLength = Math.Min(args.Length, cmdArgs.Length);
            for (int i = 0; i < argsLength; i++)
                cmdArgs[i] = args[i];

            if (!TryAcquirePciMutex())
                return SmuStatus.CmdRejectedBusy;

            try
            {
                uint value = 0;

                // Step 1: wait for any in-flight command to finish. The response register reads
                // non-zero when the SMU is idle; issuing a new command while it is still zero
                // would stomp a transaction that is still being processed.
                if (!WaitForResponse(addrRsp, out value))
                {
                    return SmuStatus.CmdRejectedBusy;
                }

                // Step 2: clear the response register.
                if (!SmuWriteReg(addrRsp, 0))
                    return SmuStatus.Failed;

                // Step 3: write the arguments.
                for (int i = 0; i < SmuArgCount; i++)
                {
                    if (!SmuWriteReg(addrArg + (uint)(i * 4), cmdArgs[i]))
                        return SmuStatus.Failed;
                }

                // Step 4: write the message id, which starts execution.
                if (!SmuWriteReg(addrMsg, msg))
                    return SmuStatus.Failed;

                // Step 5: wait for a response.
                if (!WaitForResponse(addrRsp, out value))
                {
                    // Nothing came back at all - distinct from the SMU answering with an error.
                    return SmuStatus.Bad;
                }

                // Step 6: read the arguments back regardless of status, so callers that inspect
                // them on a partial result still see what the SMU left behind.
                for (int i = 0; i < argsLength; i++)
                {
                    uint readBack = 0;
                    if (SmuReadReg(addrArg + (uint)(i * 4), ref readBack))
                        args[i] = readBack;
                }

                return (SmuStatus)value;
            }
            finally
            {
                ReleasePciMutex();
            }
        }

        /// <summary>
        /// Polls the response register until it reads non-zero, or the timeout expires.
        /// Breaks on ANY non-zero value: the SMU signals failures (0xFF, 0xFE, 0xFC) through
        /// this same register, so waiting specifically for OK would spin out the full budget on
        /// every rejected command and then report it late.
        /// </summary>
        private bool WaitForResponse(uint addrRsp, out uint value)
        {
            value = 0;
            long start = Environment.TickCount64;

            do
            {
                uint data = 0;
                if (SmuReadReg(addrRsp, ref data) && data != 0)
                {
                    value = data;
                    return true;
                }

                Thread.SpinWait(64);
            }
            while ((Environment.TickCount64 - start) < SmuWaitTimeoutMs);

            return false;
        }

        private static bool TryAcquirePciMutex()
        {
            try
            {
                return PciMutex.WaitOne(PciMutexTimeoutMs);
            }
            catch (AbandonedMutexException)
            {
                // A previous owner died holding it. We now own it; the SMU mailbox is
                // stateless between transactions, so continuing is correct.
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RyzenSmu] PCI mutex acquisition failed: {ex.Message}");
                return false;
            }
        }

        private static void ReleasePciMutex()
        {
            try
            {
                PciMutex.ReleaseMutex();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RyzenSmu] PCI mutex release failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Write an SMU register. The module performs the PCI-config address/data handshake.
        /// Callers must hold the PCI mutex.
        /// </summary>
        private bool SmuWriteReg(uint addr, uint data)
        {
            if (!IsAvailable || _pawnioExecute == null) return false;

            try
            {
                ulong[] input = { addr, data };
                int hr = _pawnioExecute(_handle, "ioctl_write_smu_register",
                    input, (IntPtr)2, Array.Empty<ulong>(), IntPtr.Zero, out _);
                if (hr < 0)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[RyzenSmu] SMU register write to 0x{addr:X} failed: HRESULT 0x{hr:X8}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RyzenSmu] SMU register write threw: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Read an SMU register. Callers must hold the PCI mutex.
        /// </summary>
        private bool SmuReadReg(uint addr, ref uint data)
        {
            if (!IsAvailable || _pawnioExecute == null) return false;

            try
            {
                ulong[] input = { addr };
                ulong[] output = new ulong[1];
                int hr = _pawnioExecute(_handle, "ioctl_read_smu_register",
                    input, (IntPtr)1, output, (IntPtr)1, out _);
                if (hr < 0)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[RyzenSmu] SMU register read from 0x{addr:X} failed: HRESULT 0x{hr:X8}");
                    return false;
                }

                data = (uint)output[0];
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RyzenSmu] SMU register read threw: {ex.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_handle != IntPtr.Zero && _pawnioClose != null)
            {
                _pawnioClose(_handle);
                _handle = IntPtr.Zero;
            }

            if (_pawnIOLib != IntPtr.Zero)
            {
                NativeMethods.FreeLibrary(_pawnIOLib);
                _pawnIOLib = IntPtr.Zero;
            }

            _moduleLoaded = false;
            _initialized = false;
        }

        private static class NativeMethods
        {
            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern IntPtr LoadLibrary(string lpFileName);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool FreeLibrary(IntPtr hModule);

            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
            public static extern IntPtr GetProcAddress(IntPtr hModule, string procName);
        }
    }
}
