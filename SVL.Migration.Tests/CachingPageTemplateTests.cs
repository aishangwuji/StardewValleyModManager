using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SVL.Avalonia.Controls;

namespace SVL.Migration.Tests;

/// <summary>
/// 页面视图缓存模板测试：验证同一 ViewModel 实例只构建一次视图并被复用，
/// 这是消除页面切换卡顿（避免重建可视化树）的核心保证。
/// </summary>
[TestClass]
public class CachingPageTemplateTests
{
    [TestMethod]
    public void Build_SameData_ShouldBuildOnceAndReuseInstance()
    {
        var buildCount = 0;
        var source = new DataTemplates
        {
            new FuncDataTemplate<object>((_, _) =>
            {
                buildCount++;
                return new Border();
            }, supportsRecycling: true)
        };
        var template = new CachingPageTemplate(source);
        var viewModel = new object();

        var first = template.Build(viewModel);
        var second = template.Build(viewModel);

        Assert.IsNotNull(first);
        Assert.AreSame(first, second, "同一 ViewModel 必须复用同一视图实例");
        Assert.AreEqual(1, buildCount, "视图只应构建一次");
        Assert.AreSame(viewModel, first!.DataContext);
    }

    [TestMethod]
    public void Build_DifferentData_ShouldBuildSeparateInstances()
    {
        var source = new DataTemplates
        {
            new FuncDataTemplate<object>((_, _) => new Border(), supportsRecycling: true)
        };
        var template = new CachingPageTemplate(source);

        var first = template.Build(new object());
        var second = template.Build(new object());

        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.AreNotSame(first, second);
    }

    [TestMethod]
    public void Build_NoMatchingTemplate_ShouldReturnNull()
    {
        var source = new DataTemplates
        {
            new FuncDataTemplate<string>((_, _) => new Border(), supportsRecycling: true)
        };
        var template = new CachingPageTemplate(source);

        Assert.IsFalse(template.Match(new object()));
        Assert.IsNull(template.Build(new object()));
    }
}
