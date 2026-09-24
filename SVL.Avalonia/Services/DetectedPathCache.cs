using System;

namespace SVL.Avalonia.Services;

/// <summary>
/// 游戏安装路径探测的短 TTL 缓存。
///
/// 探测涉及注册表查询、Steam <c>libraryfolders.vdf</c> 解析与文件系统遍历，开销较大。
/// 缓存后重复导航可直接命中，避免每次切到“Mod管理/版本设置”都在 UI 线程重复探测。
/// 负结果（未探测到）同样缓存，避免每次都重新全盘扫描。
/// </summary>
public sealed class DetectedPathCache
{
    private readonly Func<string?> _probe;
    private readonly TimeSpan _ttl;
    private readonly object _gate = new();

    private string? _cached;
    private bool _hasValue;
    private DateTime _cachedAtUtc = DateTime.MinValue;

    public DetectedPathCache(Func<string?> probe, TimeSpan? ttl = null)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _ttl = ttl ?? TimeSpan.FromMinutes(5);
    }

    /// <summary>返回缓存的探测结果；缓存缺失/过期或强制刷新时执行一次探测。</summary>
    public string? Get(bool forceRefresh = false)
    {
        lock (_gate)
        {
            if (!forceRefresh && _hasValue && DateTime.UtcNow - _cachedAtUtc < _ttl)
            {
                return _cached;
            }
        }

        var probed = _probe();

        lock (_gate)
        {
            _cached = probed;
            _hasValue = true;
            _cachedAtUtc = DateTime.UtcNow;
        }

        return probed;
    }
}
