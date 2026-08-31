namespace OmenCore.Models
{
    public class SystemRestoreResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public uint SequenceNumber { get; set; }
    }
}
