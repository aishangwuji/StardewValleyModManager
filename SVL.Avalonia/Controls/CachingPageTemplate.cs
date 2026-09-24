using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;
using System.Linq;
using System.Runtime.CompilerServices;

namespace SVL.Avalonia.Controls;

/// <summary>
/// 带视图缓存的页面模板。
///
/// 默认的 <c>ContentControl</c> + <c>DataTemplate</c> 会在每次切换页面时重新实例化视图，
/// 重建整棵可视化树并重新求值全部绑定，重页面（如 VersionSettings，89KB XAML）切换时
/// 明显卡顿。本模板按数据实例缓存已构建的 <see cref="Control"/>：首次显示时构建，
/// 之后复用同一实例（仅挂载/卸载，不重建），从而消除切换卡顿并保留滚动位置等视图状态。
///
/// 缓存使用 <see cref="ConditionalWeakTable{TKey,TValue}"/>（弱键），ViewModel 被回收时
/// 对应视图会自动释放，不会造成泄漏。
/// </summary>
public sealed class CachingPageTemplate : IDataTemplate
{
    private readonly DataTemplates _source;
    private readonly ConditionalWeakTable<object, Control> _cache = new();

    public CachingPageTemplate(DataTemplates source)
    {
        _source = source;
    }

    /// <summary>仅当内置 DataTemplates 中存在可匹配的模板时才认领该数据。</summary>
    public bool Match(object? data) =>
        data is not null && _source.Any(template => template.Match(data));

    /// <summary>返回缓存视图；未缓存时按内置模板构建并写入缓存。</summary>
    public Control? Build(object? param)
    {
        if (param is null)
        {
            return null;
        }

        if (_cache.TryGetValue(param, out var cached))
        {
            // 正常路径下旧视图已被 ContentPresenter 卸载（无父级），可直接复用。
            if (cached.GetVisualParent() is null)
            {
                return cached;
            }

            // 极端情况（上一次切换尚未完成卸载）退回新建，避免“控件已有父级”异常；
            // 不覆盖缓存，待旧视图卸载后仍可复用。
            var fallback = BuildFromSource(param);
            return fallback;
        }

        var control = BuildFromSource(param);
        if (control is not null)
        {
            _cache.Add(param, control);
        }

        return control;
    }

    private Control? BuildFromSource(object param)
    {
        var template = _source.FirstOrDefault(t => t.Match(param));
        var control = template?.Build(param);
        if (control is null)
        {
            return null;
        }

        // DataTemplate.Build 只负责实例化，不设置 DataContext；此处补上以模拟隐式模板行为。
        control.DataContext = param;
        return control;
    }
}
