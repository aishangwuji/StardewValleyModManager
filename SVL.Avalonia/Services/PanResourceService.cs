namespace SVL.Avalonia.Services;

/// <summary>
/// 网盘资源条目（Mod 展示用 DTO）。
/// <para>Business Rule: 网盘链接（PanUrl）由外部项目接口统一提供，本服务只做数据搬运与展示，不解析、不拼装链接。</para>
/// </summary>
public sealed record PanResourceItem(
    string Name,
    string Description,
    string Source,
    string PanUrl);

/// <summary>
/// 网盘资源服务：为顶栏“网盘资源”页提供 Mod 展示数据。
/// <para>Reason: 与其他项目（网盘链接供给方）的联调尚未开始，此处先以空实现占位，
/// 后续只需在 <see cref="GetPanResourcesAsync"/> 内接入对方接口并映射为 <see cref="PanResourceItem"/>，调用方无需改动。</para>
/// </summary>
public sealed class PanResourceService
{
    /// <summary>
    /// 获取网盘资源列表。当前返回空集合；联调后改为请求外部接口。
    /// </summary>
    /// <param name="keyword">可选搜索关键词，透传给后续的外部接口。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>网盘资源条目（当前为空）。</returns>
    public Task<IReadOnlyList<PanResourceItem>> GetPanResourcesAsync(
        string? keyword = null,
        CancellationToken cancellationToken = default)
    {
        // TODO: 与其他项目联调——在此调用对方提供的网盘链接接口，
        // 将返回结果映射为 PanResourceItem（Name/Description/Source/PanUrl）。
        _ = keyword;
        _ = cancellationToken;
        return Task.FromResult<IReadOnlyList<PanResourceItem>>(Array.Empty<PanResourceItem>());
    }
}
