using System.Collections.Generic;

namespace OmenCore.Models
{
    public class OmenCleanupResult
    {
        public bool UninstallTriggered { get; set; }
        public bool StorePackageRemoved { get; set; }
        public bool RegistryCleaned { get; set; }
        public bool FilesRemoved { get; set; }
        public bool ServicesCleaned { get; set; }
        public bool Success => Errors.Count == 0;
        public List<string> Steps { get; } = new();
        public List<string> Warnings { get; } = new();
        public List<string> Errors { get; } = new();
    }
}
