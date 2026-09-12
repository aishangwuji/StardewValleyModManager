using SVL.Core.Platform.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SVL.Migration.Tests;

[TestClass]
public class GameInstallPathLocatorTests
{
    [TestMethod]
    public void SteamPathLookup_ShouldNotThrow()
    {
        var locator = new GameInstallPathLocator();
        _ = locator.TryLocateSteamStardewPath();
    }

    [TestMethod]
    public void GogPathLookup_ShouldNotThrow()
    {
        var locator = new GameInstallPathLocator();
        _ = locator.TryLocateGogStardewPath();
    }

    [TestMethod]
    public void XboxPathLookup_ShouldNotThrow()
    {
        var locator = new GameInstallPathLocator();
        _ = locator.TryLocateXboxStardewPath();
    }

    [TestMethod]
    public void XboxCandidates_ShouldPreferContentDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-xbox-candidate-test-" + Guid.NewGuid().ToString("N"));
        var content = Path.Combine(root, "Content");

        try
        {
            Directory.CreateDirectory(content);
            File.WriteAllText(Path.Combine(content, "Stardew Valley.exe"), string.Empty);

            var method = typeof(GameInstallPathLocator).GetMethod(
                "EnumerateXboxGameCandidates",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(method);

            var candidates = ((IEnumerable<string>)method!.Invoke(null, [new[] { root }])!).ToList();
            CollectionAssert.Contains(candidates, content);

            var validator = typeof(GameInstallPathLocator).GetMethod(
                "IsValidGamePath",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(validator);
            Assert.IsTrue((bool)validator!.Invoke(null, [content])!);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public void SteamLibraryFolders_ShouldResolveCustomLibraryPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-steam-vdf-test-" + Guid.NewGuid().ToString("N"));
        var steamApps = Path.Combine(root, "steamapps");
        var customLibrary = Path.Combine(root, "custom-library");
        var customApps = Path.Combine(customLibrary, "steamapps");

        try
        {
            Directory.CreateDirectory(steamApps);
            Directory.CreateDirectory(customApps);
            var escapedLibrary = customLibrary.Replace("\\", "\\\\", StringComparison.Ordinal);
            File.WriteAllText(
                Path.Combine(steamApps, "libraryfolders.vdf"),
                $"\"libraryfolders\"\n{{\n  \"0\" {{ \"path\" \"{escapedLibrary}\" }}\n}}\n");

            var method = typeof(GameInstallPathLocator).GetMethod(
                "GetSteamAppsDirectories",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(method);

            var directories = ((IEnumerable<string>)method!.Invoke(null, [root])!).ToList();
            CollectionAssert.Contains(directories, customApps);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
