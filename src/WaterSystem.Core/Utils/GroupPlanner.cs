using Intelligent_water_storage_system.Model;

namespace Intelligent_water_storage_system.Utils
{
    /// <summary>
    /// 地址连续分组纯函数（模块 F 自 DataPollingService 提取，算法与行为逐字符保留）。
    ///
    /// 【目的】将相邻或接近的 Modbus 地址合并为一组，用一次批量读取请求代替多次单个读取。
    ///
    /// 【分组规则】
    ///   1. 先按 UnitId 排序（不同从站不能在同一请求中）
    ///   2. 再按 Address 排序
    ///   3. 从第一个标签开始，逐个检查下一个标签：
    ///      - 如果下一个标签的 Address ≤ 组内末端地址 + MaxRegisterGap
    ///        且 UnitId 相同 且 合并后不超出协议单次读取上限 → 加入当前组
    ///      - 否则 → 开始新的组
    ///   注意：组内末端地址按"上一个标签的地址 + 其 RegisterCount"计算
    ///   （float/uint 占 2 个寄存器），而不是固定 +1，否则 32 位点会与
    ///   后继地址错位合并、读出移位的值（模块 A 修复的回归锚点，见组 7 用例 4）。
    ///
    /// 【调用方前置条件】同一调用传入的标签必须同质（全是线圈或全是寄存器）——
    /// 单次读取上限 maxUnits 由首元素的 IsCoil 判定（与原实现一致）。
    ///
    /// 【行为不变量（提取验收依据，docs/模块F-设计.md 5.2）】
    ///   1. 输出组内按 UnitId 升序、组内地址升序（稳定排序）
    ///   2. 合并条件三合一：UnitId 相同 且 下一标签地址 ≤ 组末端(RegisterCount 感知) + MaxRegisterGap
    ///      且 合并后跨度 ≤ maxUnits（寄存器 125 / 线圈 2000）
    ///   3. 组末端按 RegisterCount 计算
    ///   4. 空输入 → 空列表
    /// </summary>
    public static class GroupPlanner
    {
        /// <summary>地址间隔不超过此值时合并（宁可多读空寄存器，减少请求次数）</summary>
        public const int MaxRegisterGap = 8;

        /// <summary>
        /// 将标签按 UnitId/地址分组（算法自 DataPollingService.GroupAddresses 原样迁入）。
        /// </summary>
        /// <param name="tags">要分组的标签列表（同质：全寄存器或全线圈）</param>
        /// <returns>分组结果，每组 Key = 起始地址、元素按地址升序</returns>
        public static List<AddressGroup> Group(List<TagAddress> tags)
        {
            if (tags.Count == 0)
                return new List<AddressGroup>();

            // 单次读取上限：寄存器 125 个 / 线圈 2000 个（Modbus 协议规定）
            int maxUnits = tags[0].IsCoil ? AppConstants.MaxCoilsPerRead : AppConstants.MaxRegistersPerRead;

            // 先按 UnitId 分组，再按地址排序
            tags = tags.OrderBy(t => t.UnitId).ThenBy(t => t.Address).ToList();
            var groups = new List<List<TagAddress>>();
            var current = new List<TagAddress> { tags[0] };

            for (int i = 1; i < tags.Count; i++)
            {
                // 组内末端地址（排他）：最后一个标签的地址 + 其占用的寄存器数
                var last = current.Last();
                int lastEnd = last.Address + last.RegisterCount;

                // UnitId 相同、地址差距不超允许范围、且合并后的读取跨度不超上限 → 合并
                int spanIfMerged = tags[i].Address + tags[i].RegisterCount - current[0].Address;
                if (tags[i].UnitId == current[0].UnitId
                    && tags[i].Address <= lastEnd + MaxRegisterGap
                    && spanIfMerged <= maxUnits)
                {
                    current.Add(tags[i]);
                }
                else
                {
                    // 地址跳跃太大 / UnitId 不同 / 超出单次读取上限，结束当前组，开始新组
                    groups.Add(current);
                    current = new List<TagAddress> { tags[i] };
                }
            }
            groups.Add(current); // 不要忘记最后一组

            return groups.Select(g => new AddressGroup(g.First().Address, g)).ToList();
        }

        /// <summary>
        /// 分组结果：Key = 该组起始地址（构建 Modbus 请求的 startAddress），
        /// 元素 = 组内全部标签（按地址排序）。实现 IGrouping 以保持调用方使用习惯。
        /// </summary>
        public sealed class AddressGroup : IGrouping<int, TagAddress>
        {
            public int Key { get; }
            private readonly List<TagAddress> _items;
            public AddressGroup(int key, List<TagAddress> items) { Key = key; _items = items; }

            public IEnumerator<TagAddress> GetEnumerator() => _items.GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
