using System.Runtime.CompilerServices;

// A handful of hardware/service types kept `internal` visibility across the split from
// OmenCoreApp (e.g. Hardware/HpWmiBios.cs, Services/CurveRecoveryService.cs's neighbors).
// Both consumers need to see them exactly as they could when everything was one assembly.
// Note: the OmenCoreApp *project* builds an assembly literally named "OmenCore"
// (<AssemblyName>OmenCore</AssemblyName> in OmenCoreApp.csproj), not "OmenCoreApp" -
// InternalsVisibleTo needs the real assembly name, not the project folder name.
[assembly: InternalsVisibleTo("OmenCore")]
[assembly: InternalsVisibleTo("OmenCoreApp.Tests")]
