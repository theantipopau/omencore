using System;

namespace OmenCore.Hardware
{
    public interface IEcAccess : IDisposable
    {
        bool Initialize(string devicePath);
        bool IsAvailable { get; }
        byte ReadByte(ushort address);
        void WriteByte(ushort address, byte value);
    }
}
