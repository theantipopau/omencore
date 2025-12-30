using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using OmenCore.Corsair;
using OmenCore.Services;
using OmenCore.Services.Corsair;
using OmenCore.Services.Logitech;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    public class LightingViewModelDpiProfileTests : IDisposable
    {
        private readonly string _tempDir;

        public LightingViewModelDpiProfileTests()
        {
            _tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "omen_test_config_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(_tempDir);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", _tempDir);
        }

        public void Dispose()
        {
            try
            {
                Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", null);
                if (System.IO.Directory.Exists(_tempDir)) System.IO.Directory.Delete(_tempDir, true);
            }
            catch { }
        }

        [Fact]
        public async Task SaveCorsairDpiProfile_SavesToConfigAndCollection()
        {
            var log = new LoggingService();
            var cfg = new ConfigurationService();
            var corsairService = new CorsairDeviceService(new CorsairSdkStub(log), log);
            var logi = new LogitechDeviceService(new LogitechSdkStub(log), log);
            var vm = new LightingViewModel(corsairService, logi, log, null, cfg, null, null);

            vm.CorsairDpiStages.Clear();
            vm.CorsairDpiStages.Add(new CorsairDpiStage { Name = "A", Dpi = 400, Index = 0 });
            vm.CorsairDpiStages.Add(new CorsairDpiStage { Name = "B", Dpi = 1600, Index = 1 });

            await vm.SaveCorsairDpiProfileAsync("TestProfile");

            vm.CorsairDpiProfiles.Should().ContainSingle(p => p.Name == "TestProfile");
            cfg.Config.CorsairDpiProfiles.Should().ContainSingle(p => p.Name == "TestProfile");
        }

        [Fact]
        public async Task ApplyCorsairDpiProfile_UpdatesStages()
        {
            var log = new LoggingService();
            var cfg = new ConfigurationService();
            var corsairService = new CorsairDeviceService(new CorsairSdkStub(log), log);
            var logi = new LogitechDeviceService(new LogitechSdkStub(log), log);
            var vm = new LightingViewModel(corsairService, logi, log, null, cfg, null, null);

            var profile = new CorsairDpiProfile { Name = "P1" };
            profile.Stages.Add(new CorsairDpiStage { Name = "S1", Dpi = 800, Index = 0 });
            profile.Stages.Add(new CorsairDpiStage { Name = "S2", Dpi = 2400, Index = 1 });

            vm.CorsairDpiProfiles.Add(profile);
            vm.SelectedCorsairDpiProfile = profile;

            await vm.ApplyCorsairDpiProfileAsync();

            vm.CorsairDpiStages.Count.Should().Be(2);
            vm.CorsairDpiStages[0].Dpi.Should().Be(800);
            vm.CorsairDpiStages[1].Dpi.Should().Be(2400);
        }

        [Fact]
        public async Task DeleteCorsairDpiProfile_RemovesProfileAndUpdatesConfig()
        {
            var log = new LoggingService();
            var cfg = new ConfigurationService();
            var corsairService = new CorsairDeviceService(new CorsairSdkStub(log), log);
            var logi = new LogitechDeviceService(new LogitechSdkStub(log), log);
            var vm = new LightingViewModel(corsairService, logi, log, null, cfg, null, null);

            await vm.SaveCorsairDpiProfileAsync("ToDelete");
            vm.CorsairDpiProfiles.Should().ContainSingle(p => p.Name == "ToDelete");

            await vm.DeleteCorsairDpiProfileByNameAsync("ToDelete");

            vm.CorsairDpiProfiles.Should().NotContain(p => p.Name == "ToDelete");
            cfg.Config.CorsairDpiProfiles.Should().NotContain(p => p.Name == "ToDelete");
        }

        [Fact]
        public async Task SaveCorsairDpiProfile_OverwriteReplacesExisting()
        {
            var log = new LoggingService();
            var cfg = new ConfigurationService();
            var corsairService = new CorsairDeviceService(new CorsairSdkStub(log), log);
            var logi = new LogitechDeviceService(new LogitechSdkStub(log), log);
            var vm = new LightingViewModel(corsairService, logi, log, null, cfg, null, null);

            // First save
            vm.CorsairDpiStages.Clear();
            vm.CorsairDpiStages.Add(new CorsairDpiStage { Name = "A", Dpi = 400, Index = 0 });
            await vm.SaveCorsairDpiProfileAsync("OverwriteTest");

            // Change stages and save again with same name (should overwrite)
            vm.CorsairDpiStages.Clear();
            vm.CorsairDpiStages.Add(new CorsairDpiStage { Name = "B", Dpi = 2400, Index = 0 });
            await vm.SaveCorsairDpiProfileAsync("OverwriteTest");

            vm.CorsairDpiProfiles.Should().ContainSingle(p => p.Name == "OverwriteTest");
            var p = vm.CorsairDpiProfiles.Single(p => p.Name == "OverwriteTest");
            p.Stages.Should().ContainSingle().Which.Dpi.Should().Be(2400);
            cfg.Config.CorsairDpiProfiles.Single(p => p.Name == "OverwriteTest").Stages.Should().ContainSingle().Which.Dpi.Should().Be(2400);
        }
    }
}

        [Fact]
        public async Task ApplyCorsairDpiProfile_UpdatesStages()
        {
            var log = new LoggingService();
            var cfg = new ConfigurationService();
            var corsairService = new CorsairDeviceService(new CorsairSdkStub(log), log);
            var logi = new LogitechDeviceService(new LogitechSdkStub(log), log);
            var vm = new LightingViewModel(corsairService, logi, log, null, cfg, null, null);

            var profile = new CorsairDpiProfile { Name = "P1" };
            profile.Stages.Add(new CorsairDpiStage { Name = "S1", Dpi = 800, Index = 0 });
            profile.Stages.Add(new CorsairDpiStage { Name = "S2", Dpi = 2400, Index = 1 });

            vm.CorsairDpiProfiles.Add(profile);
            vm.SelectedCorsairDpiProfile = profile;

            await vm.ApplyCorsairDpiProfileAsync();

            vm.CorsairDpiStages.Count.Should().Be(2);
            vm.CorsairDpiStages[0].Dpi.Should().Be(800);
            vm.CorsairDpiStages[1].Dpi.Should().Be(2400);
        }

        [Fact]
        public async Task DeleteCorsairDpiProfile_RemovesProfileAndUpdatesConfig()
        {
            var log = new LoggingService();
            var cfg = new ConfigurationService();
            var corsairService = new CorsairDeviceService(new CorsairSdkStub(log), log);
            var logi = new LogitechDeviceService(new LogitechSdkStub(log), log);
            var vm = new LightingViewModel(corsairService, logi, log, null, cfg, null, null);

            await vm.SaveCorsairDpiProfileAsync("ToDelete");
            vm.CorsairDpiProfiles.Should().ContainSingle(p => p.Name == "ToDelete");

            await vm.DeleteCorsairDpiProfileByNameAsync("ToDelete");

            vm.CorsairDpiProfiles.Should().NotContain(p => p.Name == "ToDelete");
            cfg.Config.CorsairDpiProfiles.Should().NotContain(p => p.Name == "ToDelete");
        }
    }
}

        {
            _tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "omen_test_config_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(_tempDir);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", _tempDir);
        }

        public void Dispose()
        {
            try
            {
                Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", null);
                if (System.IO.Directory.Exists(_tempDir)) System.IO.Directory.Delete(_tempDir, true);
            }
            catch { }
        }

        [Fact]
        public async Task SaveCorsairDpiProfile_SavesToConfigAndCollection()
        {
            var log = new LoggingService();
            var cfg = new ConfigurationService();
            var corsairService = new CorsairDeviceService(new CorsairSdkStub(log), log);
            var logi = new LogitechDeviceService(new LogitechSdkStub(log), log);
            var vm = new LightingViewModel(corsairService, logi, log, null, cfg, null, null);

            vm.CorsairDpiStages.Clear();
            vm.CorsairDpiStages.Add(new CorsairDpiStage { Name = "A", Dpi = 400, Index = 0 });
            vm.CorsairDpiStages.Add(new CorsairDpiStage { Name = "B", Dpi = 1600, Index = 1 });

            await vm.SaveCorsairDpiProfileAsync("TestProfile");

            vm.CorsairDpiProfiles.Should().ContainSingle(p => p.Name == "TestProfile");
            cfg.Config.CorsairDpiProfiles.Should().ContainSingle(p => p.Name == "TestProfile");
        }

        [Fact]
        public async Task ApplyCorsairDpiProfile_UpdatesStages()
        {
            var log = new LoggingService();
            var cfg = new ConfigurationService();
            var corsairService = new CorsairDeviceService(new CorsairSdkStub(log), log);
            var logi = new LogitechDeviceService(new LogitechSdkStub(log), log);
            var vm = new LightingViewModel(corsairService, logi, log, null, cfg, null, null);

            var profile = new CorsairDpiProfile { Name = "P1" };
            profile.Stages.Add(new CorsairDpiStage { Name = "S1", Dpi = 800, Index = 0 });
            profile.Stages.Add(new CorsairDpiStage { Name = "S2", Dpi = 2400, Index = 1 });

            vm.CorsairDpiProfiles.Add(profile);
            vm.SelectedCorsairDpiProfile = profile;

            await vm.ApplyCorsairDpiProfileAsync();

            vm.CorsairDpiStages.Count.Should().Be(2);
            vm.CorsairDpiStages[0].Dpi.Should().Be(800);
            vm.CorsairDpiStages[1].Dpi.Should().Be(2400);
        }

        [Fact]
        public async Task DeleteCorsairDpiProfile_RemovesProfileAndUpdatesConfig()
        {
            var log = new LoggingService();
            var cfg = new ConfigurationService();
            var corsairService = new CorsairDeviceService(new CorsairSdkStub(log), log);
            var logi = new LogitechDeviceService(new LogitechSdkStub(log), log);
            var vm = new LightingViewModel(corsairService, logi, log, null, cfg, null, null);

            await vm.SaveCorsairDpiProfileAsync("ToDelete");
            vm.CorsairDpiProfiles.Should().ContainSingle(p => p.Name == "ToDelete");

            await vm.DeleteCorsairDpiProfileByNameAsync("ToDelete");

            vm.CorsairDpiProfiles.Should().NotContain(p => p.Name == "ToDelete");
            cfg.Config.CorsairDpiProfiles.Should().NotContain(p => p.Name == "ToDelete");
        }
    }
}
        {
            _tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "omen_test_config_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(_tempDir);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", _tempDir);
        }

n        public void Dispose()
        {
            try
            {
                Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", null);
                if (System.IO.Directory.Exists(_tempDir)) System.IO.Directory.Delete(_tempDir, true);
            }
            catch { }
        }

        [Fact]
        public async Task SaveCorsairDpiProfile_SavesToConfigAndCollection()
        {
            var log = new LoggingService();
            var cfg = new ConfigurationService();
            var corsairService = new CorsairDeviceService(new CorsairSdkStub(log), log);
            var logi = new LogitechDeviceService(new LogitechSdkStub(log), log);
            var vm = new LightingViewModel(corsairService, logi, log, null, cfg, null, null);

            vm.CorsairDpiStages.Clear();
            vm.CorsairDpiStages.Add(new CorsairDpiStage { Name = "A", Dpi = 400, Index = 0 });
            vm.CorsairDpiStages.Add(new CorsairDpiStage { Name = "B", Dpi = 1600, Index = 1 });

            await vm.SaveCorsairDpiProfileAsync("TestProfile");

            vm.CorsairDpiProfiles.Should().ContainSingle(p => p.Name == "TestProfile");
            cfg.Config.CorsairDpiProfiles.Should().ContainSingle(p => p.Name == "TestProfile");
        }

        [Fact]
        public async Task ApplyCorsairDpiProfile_UpdatesStages()
        {
            var log = new LoggingService();
            var cfg = new ConfigurationService();
            var corsairService = new CorsairDeviceService(new CorsairSdkStub(log), log);
            var logi = new LogitechDeviceService(new LogitechSdkStub(log), log);
            var vm = new LightingViewModel(corsairService, logi, log, null, cfg, null, null);

            var profile = new CorsairDpiProfile { Name = "P1" };
            profile.Stages.Add(new CorsairDpiStage { Name = "S1", Dpi = 800, Index = 0 });
            profile.Stages.Add(new CorsairDpiStage { Name = "S2", Dpi = 2400, Index = 1 });

            vm.CorsairDpiProfiles.Add(profile);
            vm.SelectedCorsairDpiProfile = profile;

            await vm.ApplyCorsairDpiProfileAsync();

            vm.CorsairDpiStages.Count.Should().Be(2);
            vm.CorsairDpiStages[0].Dpi.Should().Be(800);
            vm.CorsairDpiStages[1].Dpi.Should().Be(2400);
        }

        [Fact]
        public async Task DeleteCorsairDpiProfile_RemovesProfileAndUpdatesConfig()
        {
            var log = new LoggingService();
            var cfg = new ConfigurationService();
            var corsairService = new CorsairDeviceService(new CorsairSdkStub(log), log);
            var logi = new LogitechDeviceService(new LogitechSdkStub(log), log);
            var vm = new LightingViewModel(corsairService, logi, log, null, cfg, null, null);

            await vm.SaveCorsairDpiProfileAsync("ToDelete");
            vm.CorsairDpiProfiles.Should().ContainSingle(p => p.Name == "ToDelete");

            await vm.DeleteCorsairDpiProfileByNameAsync("ToDelete");

            vm.CorsairDpiProfiles.Should().NotContain(p => p.Name == "ToDelete");
            cfg.Config.CorsairDpiProfiles.Should().NotContain(p => p.Name == "ToDelete");
        }
    }
}

using OmenCore.Services.Logitech;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    public class LightingViewModelDpiProfileTests : IDisposable
    {
        private readonly string _tempDir;

        public LightingViewModelDpiProfileTests()
        {
            _tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "omen_test_config_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(_tempDir);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", _tempDir);
        }

        public void Dispose()
        {
            try
            {
                Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", null);
                if (System.IO.Directory.Exists(_tempDir)) System.IO.Directory.Delete(_tempDir, true);
            }
            catch { }
        }

        [Fact]
        public async Task SaveCorsairDpiProfile_SavesToConfigAndCollection()
        {
            var log = new LoggingService();
            var cfg = new ConfigurationService();
            var corsairService = new CorsairDeviceService(new CorsairSdkStub(log), log);
            var logi = new LogitechDeviceService(new LogitechSdkStub(log), log);
            var vm = new LightingViewModel(corsairService, logi, log, null, cfg, null, null);

            vm.CorsairDpiStages.Clear();
            vm.CorsairDpiStages.Add(new CorsairDpiStage { Name = "A", Dpi = 400, Index = 0 });
            vm.CorsairDpiStages.Add(new CorsairDpiStage { Name = "B", Dpi = 1600, Index = 1 });

            await vm.SaveCorsairDpiProfileAsync("TestProfile");

            vm.CorsairDpiProfiles.Should().ContainSingle(p => p.Name == "TestProfile");
            cfg.Config.CorsairDpiProfiles.Should().ContainSingle(p => p.Name == "TestProfile");
        }

        [Fact]
        public async Task ApplyCorsairDpiProfile_UpdatesStages()
        {
            var log = new LoggingService();
            var cfg = new ConfigurationService();
            var corsairService = new CorsairDeviceService(new CorsairSdkStub(log), log);
            var logi = new LogitechDeviceService(new LogitechSdkStub(log), log);
            var vm = new LightingViewModel(corsairService, logi, log, null, cfg, null, null);

            var profile = new CorsairDpiProfile { Name = "P1" };
            profile.Stages.Add(new CorsairDpiStage { Name = "S1", Dpi = 800, Index = 0 });
            profile.Stages.Add(new CorsairDpiStage { Name = "S2", Dpi = 2400, Index = 1 });

            vm.CorsairDpiProfiles.Add(profile);
            vm.SelectedCorsairDpiProfile = profile;

            await vm.ApplyCorsairDpiProfileAsync();

            vm.CorsairDpiStages.Count.Should().Be(2);
            vm.CorsairDpiStages[0].Dpi.Should().Be(800);
            vm.CorsairDpiStages[1].Dpi.Should().Be(2400);
        }

        [Fact]
        public async Task DeleteCorsairDpiProfile_RemovesProfileAndUpdatesConfig()
        {
            var log = new LoggingService();
            var cfg = new ConfigurationService();
            var corsairService = new CorsairDeviceService(new CorsairSdkStub(log), log);
            var logi = new LogitechDeviceService(log);
            var vm = new LightingViewModel(corsairService, logi, log, null, cfg, null, null);

            await vm.SaveCorsairDpiProfileAsync("ToDelete");
            vm.CorsairDpiProfiles.Should().ContainSingle(p => p.Name == "ToDelete");

            await vm.DeleteCorsairDpiProfileByNameAsync("ToDelete");

            vm.CorsairDpiProfiles.Should().NotContain(p => p.Name == "ToDelete");
            cfg.Config.CorsairDpiProfiles.Should().NotContain(p => p.Name == "ToDelete");
        }
    }
}


            vm.CorsairDpiProfiles.Should().NotContain(p => p.Name == "ToDelete");
            cfg.Config.CorsairDpiProfiles.Should().NotContain(p => p.Name == "ToDelete");
        }
    }
}




















































            vm.CorsairDpiStages.Count.Should().Be(2);
            await vm.ApplyCorsairDpiProfileAsync();            vm.SelectedCorsairDpiProfile = profile;
            vm.CorsairDpiProfiles.Add(profile);            profile.Stages.Add(new CorsairDpiStage { Name = "S2", Dpi = 2400, Index = 1 });            profile.Stages.Add(new CorsairDpiStage { Name = "S1", Dpi = 800, Index = 0 });
            var profile = new CorsairDpiProfile { Name = "P1" };            var vm = new LightingViewModel(corsairService, logi, log, null, cfg, null, null);            var logi = new LogitechDeviceService(log);            var corsairService = new CorsairDeviceService(new CorsairSdkStub(log), log);            var cfg = new ConfigurationService();            var log = new LoggingService();        {        public async Task ApplyCorsairDpiProfile_UpdatesStages()

n            vm.CorsairDpiProfiles.Should().ContainSingle(p => p.Name == "TestProfile");
            await vm.SaveCorsairDpiProfileAsync("TestProfile");            vm.CorsairDpiStages.Add(new CorsairDpiStage { Name = "B", Dpi = 1600, Index = 1 });            vm.CorsairDpiStages.Add(new CorsairDpiStage { Name = "A", Dpi = 400, Index = 0 });
            vm.CorsairDpiStages.Clear();            var vm = new LightingViewModel(corsairService, logi, log, null, cfg, null, null);            var logi = new LogitechDeviceService(log);            var corsairService = new CorsairDeviceService(new CorsairSdkStub(log), log);            var cfg = new ConfigurationService();            var log = new LoggingService();        {        public async Task SaveCorsairDpiProfile_SavesToConfigAndCollection()

                Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", null);            try        {
n        public void Dispose()        }            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", _tempDir);            System.IO.Directory.CreateDirectory(_tempDir);            _tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "omen_test_config_" + Guid.NewGuid().ToString("N"));        {n        public LightingViewModelDpiProfileTests()