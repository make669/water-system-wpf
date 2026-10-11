using System.Globalization;

namespace Intelligent_water_storage_system.Utils
{
    /// <summary>
    /// 数值显示格式化（纯函数，WPF 显示层专用，Core 中零 UI 依赖）。
    /// 修 P1：水位曾显示 3.59625473022461 m——小数位由 Scale 的有效位推导，禁止全精度直出。
    /// 规格与测试用例见《WPF实施细则》§3.7（TC-F1~F5）。
    /// </summary>
    public static class ValueFormatter
    {
        /// <summary>小数位 = Scale 的十进制有效位数：1→0 位、0.1→1 位、0.01→2 位（封顶 3 位）。</summary>
        public static int DecimalsOf(double scale)
        {
            if (scale <= 0) return 0;
            for (int d = 0; d <= 3; d++)
            {
                double scaled = scale * Math.Pow(10, d);
                if (Math.Abs(scaled - Math.Round(scaled)) < 1e-9) return d;
            }
            return 3;
        }

        /// <summary>定宽工程值文本（InvariantCulture；MidpointRounding.AwayFromZero，现场读数习惯）。</summary>
        public static string Format(double raw, double scale)
        {
            int decimals = DecimalsOf(scale);
            double rounded = Math.Round(raw, decimals, MidpointRounding.AwayFromZero);
            return rounded.ToString($"F{decimals}", CultureInfo.InvariantCulture);
        }
    }
}
