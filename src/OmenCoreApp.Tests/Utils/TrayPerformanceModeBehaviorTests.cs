using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Controls;
using FluentAssertions;
using OmenCore.Utils;
using Xunit;

namespace OmenCoreApp.Tests.Utils
{
    /// <summary>
    /// Behavior-level tray tests for performance mode state sync/checkmark updates.
    /// Uses uninitialized-object construction so private tray logic can be tested
    /// without creating a real TaskbarIcon/WPF tray host.
    /// </summary>
    public class TrayPerformanceModeBehaviorTests
    {
        [Fact]
        public void UpdatePerformanceModeCheckmarks_NormalizesAliasBeforeSelectingCheckmark()
        {
            RunInSta(() =>
            {
                var service = CreateTrayServiceForPerformanceModeTests();

                InvokePrivateMethod(service, "UpdatePerformanceModeCheckmarks", "turbo");

                GetMenuHeader(service, "_perfBalancedMenuItem").Should().StartWith("  ");
                GetMenuHeader(service, "_perfQuietMenuItem").Should().StartWith("  ");
                GetMenuHeader(service, "_perfPerformanceMenuItem").Should().StartWith("✓");
            });
        }

        [Fact]
        public void SetPerformanceMode_NormalizesAlias_UpdatesHeaderAndEmitsCanonicalPayload()
        {
            RunInSta(() =>
            {
                var service = CreateTrayServiceForPerformanceModeTests();
                string? observedMode = null;
                service.PerformanceModeChangeRequested += mode => observedMode = mode;

                InvokePrivateMethod(service, "SetPerformanceMode", "silent");

                GetPrivateField<string>(service, "_currentPerformanceMode").Should().Be("Quiet");
                GetMenuHeader(service, "_performanceModeMenuItem").Should().Be("Performance: Quiet");
                GetMenuHeader(service, "_perfBalancedMenuItem").Should().StartWith("  ");
                GetMenuHeader(service, "_perfPerformanceMenuItem").Should().StartWith("  ");
                GetMenuHeader(service, "_perfQuietMenuItem").Should().StartWith("✓");
                observedMode.Should().Be("Quiet");
            });
        }

        private static TrayIconService CreateTrayServiceForPerformanceModeTests()
        {
            var service = (TrayIconService)RuntimeHelpers.GetUninitializedObject(typeof(TrayIconService));

            SetPrivateField(service, "_performanceModeMenuItem", new MenuItem());
            SetPrivateField(service, "_perfBalancedMenuItem", new MenuItem());
            SetPrivateField(service, "_perfPerformanceMenuItem", new MenuItem());
            SetPrivateField(service, "_perfQuietMenuItem", new MenuItem());
            SetPrivateField(service, "_currentPerformanceMode", "Balanced");

            return service;
        }

        private static void RunInSta(Action action)
        {
            Exception? captured = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    captured = ex;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (captured != null)
            {
                ExceptionDispatchInfo.Capture(captured).Throw();
            }
        }

        private static string GetMenuHeader(object target, string fieldName)
        {
            var menuItem = GetPrivateField<MenuItem>(target, fieldName);
            menuItem.Header.Should().NotBeNull();
            return menuItem.Header!.ToString()!;
        }

        private static void InvokePrivateMethod(object target, string methodName, params object[] args)
        {
            var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            method.Should().NotBeNull($"private method {methodName} should exist");
            method!.Invoke(target, args);
        }

        private static T GetPrivateField<T>(object target, string fieldName)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            field.Should().NotBeNull($"private field {fieldName} should exist");
            return (T)field!.GetValue(target)!;
        }

        private static void SetPrivateField(object target, string fieldName, object? value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            field.Should().NotBeNull($"private field {fieldName} should exist");
            field!.SetValue(target, value);
        }
    }
}
