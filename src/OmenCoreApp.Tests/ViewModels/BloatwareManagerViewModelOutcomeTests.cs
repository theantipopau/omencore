using System;
using System.Reflection;
using FluentAssertions;
using OmenCore.Services.BloatwareManager;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    public class BloatwareManagerViewModelOutcomeTests
    {
        [Fact]
        public void BuildSingleRemovalOutcomeMessage_WhenSkipped_IncludesDetail()
        {
            var app = new BloatwareApp
            {
                Name = "Victus Welcome",
                LastRemovalStatus = RemovalStatus.Skipped,
                LastRemovalDetail = "Package was already absent before removal attempt."
            };

            var message = InvokeBuildSingleRemovalOutcomeMessage(app);

            message.Should().Contain("Skipped");
            message.Should().Contain(app.Name);
            message.Should().Contain("already absent");
        }

        [Fact]
        public void BuildBulkRemovalCompletionMessage_WhenSkippedExists_ContainsSkipSummaryAndExample()
        {
            var result = new BulkRemovalResult
            {
                RequestedTotal = 3,
                Completed = true
            };
            result.Succeeded.Add(new BloatwareApp
            {
                Name = "HP Audio Switch",
                LastRemovalStatus = RemovalStatus.VerifiedSuccess
            });
            result.Skipped.Add(new BloatwareApp
            {
                Name = "Victus Launcher",
                LastRemovalStatus = RemovalStatus.Skipped,
                LastRemovalDetail = "Startup registry value was already absent before removal attempt."
            });

            var message = InvokeBuildBulkRemovalCompletionMessage(result);

            message.Should().Contain("1 removed");
            message.Should().Contain("1 skipped");
            message.Should().Contain("Example skip");
            message.Should().Contain("Victus Launcher");
        }

        private static string InvokeBuildSingleRemovalOutcomeMessage(BloatwareApp app)
        {
            var method = typeof(BloatwareManagerViewModel).GetMethod(
                "BuildSingleRemovalOutcomeMessage",
                BindingFlags.NonPublic | BindingFlags.Static);
            method.Should().NotBeNull();

            var result = method!.Invoke(null, new object[] { app });
            result.Should().BeOfType<string>();
            return (string)result!;
        }

        private static string InvokeBuildBulkRemovalCompletionMessage(BulkRemovalResult result)
        {
            var method = typeof(BloatwareManagerViewModel).GetMethod(
                "BuildBulkRemovalCompletionMessage",
                BindingFlags.NonPublic | BindingFlags.Static);
            method.Should().NotBeNull();

            var value = method!.Invoke(null, new object[] { result });
            value.Should().BeOfType<string>();
            return (string)value!;
        }
    }
}