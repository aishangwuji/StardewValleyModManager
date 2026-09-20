using Microsoft.VisualStudio.TestTools.UnitTesting;
using SVL.Avalonia.Services;

namespace SVL.Migration.Tests;

[TestClass]
public class RecycleBinAndRegistryTests
{
    [TestMethod]
    public void RecycleBin_EmptyPath_ShouldFail()
    {
        Assert.IsFalse(SVL.Core.Platform.Services.RecycleBinService.TryMoveToRecycleBin(string.Empty, out _));
        Assert.IsFalse(RecycleBinService.TryMoveToRecycleBin("   ", out _));
    }

    [TestMethod]
    public void RecycleBin_MissingPath_ShouldSucceed()
    {
        var missing = Path.Combine(Path.GetTempPath(), "svl-missing-" + Guid.NewGuid().ToString("N"));
        Assert.IsTrue(SVL.Core.Platform.Services.RecycleBinService.TryMoveToRecycleBin(missing, out _));
    }

    [TestMethod]
    public void RecycleBin_RealFile_ShouldLeaveNoDataLoss()
    {
        var root = Path.Combine(Path.GetTempPath(), "svl-recycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "mod.txt");
            File.WriteAllText(file, "data");
            var ok = RecycleBinService.TryMoveToRecycleBin(file, out _);
            Assert.IsTrue(ok, "真实文件必须可恢复地移走");
            Assert.IsFalse(File.Exists(file));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [TestMethod]
    public void Registry_TryAddRenameRemove_ShouldBeAtomic()
    {
        var storage = Path.Combine(Path.GetTempPath(), "svl-registry-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new InstanceRegistryStore(storage);
            Assert.IsTrue(store.TryAddManualInstance("Base", @"D:\Games\SDV"));
            Assert.IsFalse(store.TryAddManualInstance("Dup", @"d:\games\sdv"));
            Assert.AreEqual(1, store.LoadManualInstances().Count);

            store.UpsertManualInstance("Renamed", @"D:\Games\SDV");
            Assert.AreEqual("Renamed", store.LoadManualInstances()[0].Name);

            Assert.IsTrue(store.RenameManualInstancesByPath(@"D:\GAMES\SDV", "Final"));
            Assert.AreEqual("Final", store.LoadManualInstances()[0].Name);

            Assert.IsTrue(store.RemoveManualInstancesByPath(@"d:\games\sdv"));
            Assert.AreEqual(0, store.LoadManualInstances().Count);
        }
        finally
        {
            if (Directory.Exists(storage))
            {
                Directory.Delete(storage, true);
            }
        }
    }
}
