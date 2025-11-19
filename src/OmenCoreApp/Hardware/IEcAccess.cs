namespace OmenCore.Hardware
{
    public interface IEcAccess
    {
        bool Initialize(string devicePath);
        bool IsAvailable { get; }
        byte ReadByte(ushort address);
        void WriteByte(ushort address, byte value);
    }
}
