using FluentAssertions;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    public class StandardProfileNoticeTests
    {
        [Theory]
        [InlineData(null, "8C77", false)]
        [InlineData("", "8C77", false)]
        [InlineData("8C77", "8C77", true)]
        [InlineData("8c77", "8C77", true)]
        [InlineData("8C77", "8E5F", false)]   // dismissed on one board, a different board shows it again
        public void Dismissal_IsPerBoard(string? dismissed, string productId, bool hidden)
        {
            MainViewModel.IsStandardProfileNoticeDismissed(dismissed, productId).Should().Be(hidden);
        }
    }
}
