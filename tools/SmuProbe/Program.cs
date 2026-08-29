// SmuProbe - hardware verification harness for OmenCore's AMD SMU path.
//
// Drives the shipping OmenCore.Hardware types directly, so what is verified is what ships.
// Requires administrator rights and the PawnIO driver.
//
//   SmuProbe.exe                    transport only: driver, module, mailbox liveness  [read-only]
//   SmuProbe.exe --co <offset>      apply an All-Core Curve Optimizer offset           [WRITES]
//   SmuProbe.exe --counterfactual   compare SMU status across candidate message ids    [WRITES]
//   SmuProbe.exe --outcome [opts]   measure whether an offset actually changes clock   [WRITES]
//   SmuProbe.exe --limits [opts]    measure whether the power limits reach the silicon [WRITES]
//   SmuProbe.exe --igpu [opts]      measure whether an iGPU CO offset reaches the GFX   [WRITES]
//   SmuProbe.exe --pmtable [opts]   identify which PM table index holds which limit    [read-only,
//                                                                            or WRITES with --ab]
//
// Options for --outcome:
//   --offset <n>   offset to test (default -25)
//   --psmu5d       use the pre-fix PSMU 0x5D path instead of MP1 0x4C
//
// Options for --igpu (requires a sustained iGPU load to already be running):
//   --offset <n>       offset in CO counts, clamped +-30 (default -20)
//   --mp1              send 0xB7 on MP1 instead of PSMU
//   --sham             write 0 in both phases, to establish the noise floor
//   --readback <path>  external ryzenadj.exe, used READ-ONLY to read GFX clock and voltage
//
// Options for --limits:
//   --watts <n>        the low limit to clamp down to (default 20)
//   --readback <path>  external ryzenadj.exe, used READ-ONLY as an independent oracle
//
// Options for --pmtable:
//   --readback <path>  external ryzenadj.exe, READ-ONLY; without it nothing can be anchored
//   --size <bytes>     how much table to read (default 4096)
//   --dump <file>      write the whole phase-A table to CSV
//   --ab <watts>       WRITES this limit as a second phase, so the indices that follow it can be
//                      told apart from the ones that merely held the same number. Restored after.
//
// Why a measured mode exists at all: the SMU returns Ok for message ids that change nothing, so
// a status code is not evidence of an effect. See docs/8D87-OMEN-MAX-16-SUPPORT-PLAN.md 5.2.1.

using OmenCore.Tools.SmuProbe;

if (args.Contains("--counterfactual"))
    return Counterfactual.Run();

if (args.Contains("--outcome"))
    return Outcome.Run(args);

if (args.Contains("--igpu"))
    return Igpu.Run(args);

if (args.Contains("--limits"))
    return Limits.Run(args);

if (args.Contains("--pmtable"))
    return PmTable.Run(args);

return Transport.Run(args);
