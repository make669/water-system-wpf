using System.IO;
using Intelligent_water_storage_system.Model;
using WaterSystem.Wpf.Binding;
using Xunit;

namespace WaterSystem.Wpf.Tests
{
    /// <summary>T2.5 验收：注册表分类/去重 + E08/E09/W01 逐码断言（防"漏注册→校验假绿"，风险 R3）。
    /// 与 ControlViewModelTests 共用静态注册表（同 Collection 串行，防并行竞态）。</summary>
    [Collection("UiDispatcher")]
    public class TagBindingRegistryTests
    {
        static List<TagAddress> LoadRealTags()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && d != null; i++, d = d.Parent)
            {
                var p = Path.Combine(d.FullName, "configFiles", "TagTable.csv");
                if (File.Exists(p))
                {
                    var tags = TagTableLoaderShim(p, out var errors);
                    Assert.Empty(errors);
                    return tags;
                }
            }
            throw new InvalidOperationException("未找到仓库内 configFiles/TagTable.csv");
        }

        static List<TagAddress> TagTableLoaderShim(string path, out List<string> errors)
            => Intelligent_water_storage_system.Utils.TagTableLoader.Load(path, out errors, out _);

        [Fact]
        public void 注册_分类且大小写不敏感去重()
        {
            TagBindingRegistry.Reset();
            TagBindingRegistry.Register("Pump1_F", TagBindingKind.Display);
            TagBindingRegistry.Register("pump1_f", TagBindingKind.Display);   // 大小写去重
            TagBindingRegistry.Register("Pump1_F", TagBindingKind.Display);   // 重复去重
            TagBindingRegistry.Register("Pump1_Run", TagBindingKind.Control);
            Assert.Equal(new[] { "Pump1_F" }, TagBindingRegistry.DisplayNames);
            Assert.Equal(new[] { "Pump1_Run" }, TagBindingRegistry.ControlNames);
            TagBindingRegistry.Reset();
            Assert.Empty(TagBindingRegistry.DisplayNames);
        }

        [Fact]
        public void Check_全绑定覆盖_RW有控制入口_零错误()
        {
            TagBindingRegistry.Reset();
            var tags = LoadRealTags();
            var rw = tags.Where(t => t.Access == TagAccess.RW).Select(t => t.TagName);
            foreach (var n in rw) TagBindingRegistry.Register(n, TagBindingKind.Control);
            foreach (var n in tags.Select(t => t.TagName)) TagBindingRegistry.Register(n, TagBindingKind.Display);
            var (errors, warnings) = TagBindingRegistry.Check(tags);
            Assert.Empty(errors);
            Assert.Empty(warnings);
        }

        [Fact]
        public void Check_控制绑定缺失_E08_逐点报出()
        {
            TagBindingRegistry.Reset();
            var tags = LoadRealTags();
            // 只登记一个控制绑定 → 其余 RW 标签全部 E08
            TagBindingRegistry.Register("Pump1_Run", TagBindingKind.Control);
            foreach (var n in tags.Select(t => t.TagName)) TagBindingRegistry.Register(n, TagBindingKind.Display);
            var (errors, _) = TagBindingRegistry.Check(tags);
            var e08 = errors.Where(e => e.Contains("[E08]")).ToList();
            Assert.Equal(tags.Count(t => t.Access == TagAccess.RW) - 1, e08.Count);
            Assert.Contains(e08, e => e.Contains("MainValve"));
        }

        [Fact]
        public void Check_绑定不存在标签_E09指名()
        {
            TagBindingRegistry.Reset();
            var tags = LoadRealTags();
            var rw = tags.Where(t => t.Access == TagAccess.RW).Select(t => t.TagName);
            foreach (var n in rw) TagBindingRegistry.Register(n, TagBindingKind.Control);
            foreach (var n in tags.Select(t => t.TagName)) TagBindingRegistry.Register(n, TagBindingKind.Display);
            TagBindingRegistry.Register("NotExistTag", TagBindingKind.Display);
            var (errors, _) = TagBindingRegistry.Check(tags);
            Assert.Contains(errors, e => e.Contains("[E09]") && e.Contains("NotExistTag"));
        }

        [Fact]
        public void Check_未绑定标签_W01仅警告()
        {
            TagBindingRegistry.Reset();
            var tags = LoadRealTags();
            // 只登记控制类，不登记任何显示 → 未绑定的 R 类标签记 W01
            foreach (var n in tags.Where(t => t.Access == TagAccess.RW).Select(t => t.TagName))
                TagBindingRegistry.Register(n, TagBindingKind.Control);
            var (errors, warnings) = TagBindingRegistry.Check(tags);
            Assert.Empty(errors);
            Assert.Contains(warnings, w => w.Contains("[W01]") && w.Contains("Pump1_Fault"));
        }
    }
}
