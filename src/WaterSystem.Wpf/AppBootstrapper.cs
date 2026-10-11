using System.IO;
using Intelligent_water_storage_system.Model;
using Intelligent_water_storage_system.Utils;

namespace WaterSystem.Wpf
{
    public sealed record AppPrecheckResult(
        bool Ok,
        List<TagAddress> Tags,
        AlarmConfig? Config,
        List<string> Errors,
        List<string> Warnings);

    /// <summary>
    /// 启动前契约校验（可测纯逻辑，UI 无关）。
    /// 覆盖 E01~E07（标签表文件级）+ A01~A09（报警配置）；E08/E09 由 TagBindingRegistry
    /// 在主窗 InitializeComponent 之后接入（T5.5）——顺序对应源 FrmMain：先建窗扫描绑定，再核对。
    /// fail-fast 语义：任何 Error 拒绝启动（源 FrmMain.cs:309-318/345-353）。
    /// </summary>
    public static class AppBootstrapper
    {
        public static AppPrecheckResult PrecheckConfig(string baseDir)
        {
            var errors = new List<string>();
            var warnings = new List<string>();

            var tags = TagTableLoader.Load(
                Path.Combine(baseDir, "configFiles", "TagTable.csv"),
                out var tagErrors, out var tagWarnings);
            errors.AddRange(tagErrors);
            warnings.AddRange(tagWarnings);

            var config = AlarmConfig.Load(
                Path.Combine(baseDir, "configFiles", "AlarmConfig.json"),
                out var cfgErrors, out var cfgWarnings);
            errors.AddRange(cfgErrors);
            warnings.AddRange(cfgWarnings);

            return new AppPrecheckResult(errors.Count == 0, tags, config, errors, warnings);
        }
    }
}
