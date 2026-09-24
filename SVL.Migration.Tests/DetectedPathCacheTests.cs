using System;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SVL.Avalonia.Services;

namespace SVL.Migration.Tests;

/// <summary>
/// 游戏安装路径探测缓存测试：验证 TTL 命中、强制刷新、负结果缓存与过期重探，
/// 保证导航路径不再重复执行昂贵的注册表/VDF/文件系统探测。
/// </summary>
[TestClass]
public class DetectedPathCacheTests
{
    [TestMethod]
    public void Get_SecondCallWithinTtl_ShouldNotReprobe()
    {
        var calls = 0;
        var cache = new DetectedPathCache(() => { calls++; return "C:/Game"; }, TimeSpan.FromMinutes(5));

        Assert.AreEqual("C:/Game", cache.Get());
        Assert.AreEqual("C:/Game", cache.Get());
        Assert.AreEqual(1, calls, "TTL 内重复调用不应再次探测");
    }

    [TestMethod]
    public void Get_ForceRefresh_ShouldReprobe()
    {
        var calls = 0;
        var cache = new DetectedPathCache(() => { calls++; return "C:/Game"; }, TimeSpan.FromMinutes(5));

        cache.Get();
        cache.Get(forceRefresh: true);

        Assert.AreEqual(2, calls, "强制刷新必须重新探测");
    }

    [TestMethod]
    public void Get_NegativeResult_ShouldBeCached()
    {
        var calls = 0;
        var cache = new DetectedPathCache(() => { calls++; return null; }, TimeSpan.FromMinutes(5));

        Assert.IsNull(cache.Get());
        Assert.IsNull(cache.Get());
        Assert.AreEqual(1, calls, "未探测到的负结果也应缓存，避免每次全盘扫描");
    }

    [TestMethod]
    public void Get_AfterTtlExpiry_ShouldReprobe()
    {
        var calls = 0;
        var cache = new DetectedPathCache(() => { calls++; return "C:/Game"; }, TimeSpan.FromMilliseconds(40));

        cache.Get();
        Thread.Sleep(90);
        cache.Get();

        Assert.AreEqual(2, calls, "超过 TTL 后应重新探测");
    }
}
