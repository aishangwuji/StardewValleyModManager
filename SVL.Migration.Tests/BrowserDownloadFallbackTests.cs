#nullable enable

using Microsoft.VisualStudio.TestTools.UnitTesting;
using SVL.Avalonia.Services;
using SVL.Core.Platform.Abstractions;

namespace SVL.Migration.Tests;

[TestClass]
public class BrowserDownloadFallbackTests
{
    [TestMethod]
    public async Task ModOnlyWaiter_ShouldAcceptAnyFileForTheSameMod()
    {
        var service = new BrowserDownloadFallbackService(
            new NxmLinkParser(),
            new RecordingExternalProcessService());

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var waitTask = service.WaitForNxmModCallbackAsync(
            2400,
            "https://www.nexusmods.com/stardewvalley/mods/2400?tab=files&nmm=1",
            cancellationToken: cancellation.Token);

        var callback = "nxm://stardewvalley/mods/2400/files/12000?key=abc&expires=1910000000&user_id=123";
        Assert.IsTrue(service.HandleNxmCallback(callback));
        Assert.AreEqual(callback, await waitTask);
    }

    [TestMethod]
    public async Task ModOnlyWaiter_ShouldRejectDifferentMod()
    {
        var service = new BrowserDownloadFallbackService(
            new NxmLinkParser(),
            new RecordingExternalProcessService());

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var waitTask = service.WaitForNxmModCallbackAsync(
            2400,
            "https://www.nexusmods.com/stardewvalley/mods/2400?tab=files&nmm=1",
            cancellationToken: cancellation.Token);

        Assert.IsFalse(service.HandleNxmCallback(
            "nxm://stardewvalley/mods/5098/files/12000?key=abc&expires=1910000000&user_id=123"));

        cancellation.Cancel();
        Assert.IsNull(await waitTask);
    }

    [TestMethod]
    public async Task ReplacedModFileWaiter_ShouldNotRemoveTheNewWaiter()
    {
        var service = new BrowserDownloadFallbackService(
            new NxmLinkParser(),
            new RecordingExternalProcessService());

        using var firstCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var secondCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var browserUrl = "https://www.nexusmods.com/stardewvalley/mods/2400?tab=files&nmm=1";
        var firstWait = service.WaitForNxmCallbackAsync(2400, 12000, browserUrl, cancellationToken: firstCancellation.Token);
        await Task.Delay(50);
        var secondWait = service.WaitForNxmCallbackAsync(2400, 12000, browserUrl, cancellationToken: secondCancellation.Token);

        Assert.IsNull(await firstWait);

        var callback = "nxm://stardewvalley/mods/2400/files/12000?key=abc&expires=1910000000&user_id=123";
        Assert.IsTrue(service.HandleNxmCallback(callback));
        Assert.AreEqual(callback, await secondWait);
    }

    [TestMethod]
    public async Task ReplacedCollectionWaiter_ShouldNotRemoveTheNewWaiter()
    {
        var service = new BrowserDownloadFallbackService(
            new NxmLinkParser(),
            new RecordingExternalProcessService());

        using var firstCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var secondCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var browserUrl = "https://www.nexusmods.com/stardewvalley/collections/test/mods";
        var firstWait = service.WaitForCollectionNxmCallbackAsync("test", 3, browserUrl, cancellationToken: firstCancellation.Token);
        await Task.Delay(50);
        var secondWait = service.WaitForCollectionNxmCallbackAsync("test", 3, browserUrl, cancellationToken: secondCancellation.Token);

        Assert.IsNull(await firstWait);

        var callback = "nxm://stardewvalley/collections/test/revisions/3?key=abc&expires=1910000000&user_id=123";
        Assert.IsTrue(service.HandleNxmCallback(callback));
        Assert.AreEqual(callback, await secondWait);
    }

    private sealed class RecordingExternalProcessService : IExternalProcessService
    {
        public bool TryOpenUrl(string url) => true;
        public bool TryOpenPath(string path) => true;
        public bool TryLaunchProcess(string fileName, string arguments, string? workingDirectory = null) => true;
        public int RunCommand(string fileName, string arguments, string? workingDirectory = null) => 0;
    }
}
