namespace OmenCore.Models
{
    public class OmenCleanupOptions
    {
        public bool RemoveStorePackage { get; set; } = true;
        public bool RemoveLegacyInstallers { get; set; } = true;
        public bool RemoveRegistryTraces { get; set; } = true;
        public bool RemoveResidualFiles { get; set; } = true;
        public bool RemoveServicesAndTasks { get; set; } = true;
        public bool KillRunningProcesses { get; set; } = true;
        public bool PreserveFirewallRules { get; set; } = true;
        public bool DryRun { get; set; }
    }
}
