using System;
using System.IO;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SVL.Avalonia.Services;

namespace SVL.Migration.Tests;

/// <summary>
/// Mods 目录签名测试：验证目录未变化时签名稳定、新增/修改/删除（含嵌套）时签名变化，
/// 保证 mtime 失效判断能正确跳过或触发重扫。
/// </summary>
[TestClass]
public class ModsFolderSignatureTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "svl_modsig_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
    }

    [TestMethod]
    public void Compute_Unchanged_ShouldReturnSameSignature()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "a");

        var first = ModsFolderSignature.Compute(_root);
        var second = ModsFolderSignature.Compute(_root);

        Assert.AreNotEqual(string.Empty, first);
        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void Compute_AfterFileAdded_ShouldChange()
    {
        var first = ModsFolderSignature.Compute(_root);

        Thread.Sleep(20);
        File.WriteAllText(Path.Combine(_root, "new.txt"), "x");

        Assert.AreNotEqual(first, ModsFolderSignature.Compute(_root));
    }

    [TestMethod]
    public void Compute_AfterFileModified_ShouldChange()
    {
        var file = Path.Combine(_root, "a.txt");
        File.WriteAllText(file, "a");
        var first = ModsFolderSignature.Compute(_root);

        Thread.Sleep(20);
        File.WriteAllText(file, "bb");

        Assert.AreNotEqual(first, ModsFolderSignature.Compute(_root));
    }

    [TestMethod]
    public void Compute_AfterFileDeleted_ShouldChange()
    {
        var file = Path.Combine(_root, "a.txt");
        File.WriteAllText(file, "a");
        var first = ModsFolderSignature.Compute(_root);

        Thread.Sleep(20);
        File.Delete(file);

        Assert.AreNotEqual(first, ModsFolderSignature.Compute(_root));
    }

    [TestMethod]
    public void Compute_NestedFile_ShouldBeIncluded()
    {
        var nested = Path.Combine(_root, "ModA");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "manifest.json"), "{}");
        var first = ModsFolderSignature.Compute(_root);

        Thread.Sleep(20);
        File.WriteAllText(Path.Combine(nested, "extra.txt"), "x");

        Assert.AreNotEqual(first, ModsFolderSignature.Compute(_root), "嵌套文件变化必须被签名感知");
    }

    [TestMethod]
    public void Compute_MissingDirectory_ShouldReturnEmpty()
    {
        Assert.AreEqual(string.Empty, ModsFolderSignature.Compute(Path.Combine(_root, "nope")));
    }
}
