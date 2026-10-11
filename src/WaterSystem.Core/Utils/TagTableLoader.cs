using System.Globalization;
using System.Text.RegularExpressions;
using Intelligent_water_storage_system.Model;

namespace Intelligent_water_storage_system.Utils
{
    /// <summary>
    /// 标签表加载器（数据契约的唯一入口）。
    ///
    /// 【核心职责】
    /// 从 configFiles/TagTable.csv 加载全部数据点定义，并做 fail-fast 校验：
    /// 收集全部错误后一次性报告，存在任何 Error 级问题即拒绝程序启动。
    /// 工业软件的正确姿态是"配置错了宁可不开机"——带病启动的后果由现场承担。
    ///
    /// 【CSV 格式】
    ///   - UTF-8（带 BOM，便于 Excel 直接打开编辑）
    ///   - 逗号分隔；# 开头行为注释；空行忽略
    ///   - 第一条有效行为表头，列名固定、顺序不敏感（按列名解析）
    ///   - 必需列：TagName / Address / Type / Access / Scale
    ///   - 可选列：Unit / UnitId / Group / Description
    ///
    /// 【校验错误码】（与 docs/设计文档.md 4.4 节对应）
    ///   E01 表头缺失/文件问题   E02 TagName 重复      E03 TagName 命名非法
    ///   E04 地址/UnitId 非法    E05 Type/Access 非法  E06 float/uint 地址未偶对齐
    ///   E07 Scale 非法          E08 RW 标签无控制控件  E09 UI 绑定的标签不存在
    ///   W01 标签无 UI 绑定（警告）  W02 地址范围重叠（警告）
    /// </summary>
    public static class TagTableLoader
    {
        // ─── 错误码常量 ───
        public const string CodeFile = "E01";
        public const string CodeDuplicate = "E02";
        public const string CodeName = "E03";
        public const string CodeAddress = "E04";
        public const string CodeType = "E05";
        public const string CodeAlign = "E06";
        public const string CodeScale = "E07";
        public const string CodeControlUnbound = "E08";
        public const string CodeBindingMissing = "E09";
        public const string WarnTagUnused = "W01";
        public const string WarnOverlap = "W02";
        public const string WarnRange = "W03";

        /// <summary>必需列。缺少任何一列即 E01 拒绝加载。</summary>
        private static readonly string[] RequiredColumns = { "TagName", "Address", "Type", "Access", "Scale" };

        /// <summary>
        /// TagName 命名规则：字母开头，仅含字母/数字/下划线/连字符，长度 ≤ 32。
        /// 允许连字符是为了兼容既有界面绑定（Flow-1 / Pressure-1）。
        /// </summary>
        private static readonly Regex TagNamePattern = new(@"^[A-Za-z][A-Za-z0-9_-]{0,31}$", RegexOptions.Compiled);

        /// <summary>
        /// 加载并校验标签表。
        /// 注意：本方法只做文件级校验（E01~E07/W02）；UI 绑定交叉核对（E08/E09/W01）
        /// 由 CheckUiBindings 在拿到界面控件绑定后单独调用。
        /// </summary>
        /// <param name="filePath">TagTable.csv 的完整路径</param>
        /// <param name="errors">Error 级问题列表（非空则必须拒绝启动）</param>
        /// <param name="warnings">Warn 级问题列表（仅记录，不阻止启动）</param>
        /// <returns>解析出的标签列表；存在错误时可能不完整，调用方必须先检查 errors</returns>
        public static List<TagAddress> Load(string filePath, out List<string> errors, out List<string> warnings)
        {
            errors = new List<string>();
            warnings = new List<string>();
            var tags = new List<TagAddress>();

            if (!File.Exists(filePath))
            {
                errors.Add($"[{CodeFile}] 标签表文件不存在: {filePath}");
                return tags;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(filePath);
            }
            catch (Exception ex)
            {
                errors.Add($"[{CodeFile}] 标签表读取失败: {ex.Message}");
                return tags;
            }

            // ── 定位表头：第一个非空且非注释的行 ──
            int headerIndex = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#")) continue;
                headerIndex = i;
                break;
            }
            if (headerIndex < 0)
            {
                errors.Add($"[{CodeFile}] 标签表为空（未找到表头行）");
                return tags;
            }

            // ── 解析表头，建立 列名→列号 映射（顺序不敏感） ──
            var header = SplitCsv(lines[headerIndex]).Select(c => c.Trim()).ToList();
            var colIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < header.Count; i++)
                colIndex[header[i]] = i;

            var missingColumns = RequiredColumns.Where(c => !colIndex.ContainsKey(c)).ToList();
            if (missingColumns.Count > 0)
            {
                errors.Add($"[{CodeFile}] 表头缺少必需列: {string.Join(", ", missingColumns)}");
                return tags;
            }

            // ── 逐行解析与校验（收集全部错误，不遇错即停） ──
            var seenNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            // 寄存器区与线圈区是两个独立的 Modbus 地址空间，占用检查必须分开跟踪
            var occupiedRegisters = new HashSet<int>();
            var occupiedCoils = new HashSet<int>();

            for (int i = headerIndex + 1; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#")) continue;

                int lineNo = i + 1;
                var fields = SplitCsv(line);
                string Field(string col) =>
                    colIndex.TryGetValue(col, out int ci) && ci < fields.Length ? fields[ci].Trim() : string.Empty;

                // TagName：非空 + 命名规则 + 全局唯一
                string tagName = Field("TagName");
                if (tagName.Length == 0)
                {
                    errors.Add($"[{CodeName}] 第 {lineNo} 行：TagName 为空");
                    continue;
                }
                if (!TagNamePattern.IsMatch(tagName))
                {
                    errors.Add($"[{CodeName}] 第 {lineNo} 行：TagName \"{tagName}\" 不符合命名规则（字母开头，仅含字母/数字/下划线/连字符，≤32 字符）");
                    continue;
                }
                if (seenNames.TryGetValue(tagName, out int firstLine))
                {
                    errors.Add($"[{CodeDuplicate}] 第 {lineNo} 行：TagName \"{tagName}\" 与第 {firstLine} 行重复");
                    continue;
                }
                seenNames[tagName] = lineNo;

                // Address：0~65535 的整数
                string addrField = Field("Address");
                if (!int.TryParse(addrField, NumberStyles.Integer, CultureInfo.InvariantCulture, out int address)
                    || address < 0 || address > 65535)
                {
                    errors.Add($"[{CodeAddress}] 第 {lineNo} 行：Address \"{addrField}\" 不是 0~65535 的整数");
                    continue;
                }

                // Type / Access：枚举合法
                if (!TryParseType(Field("Type"), out TagType type))
                {
                    errors.Add($"[{CodeType}] 第 {lineNo} 行：Type \"{Field("Type")}\" 不在 coil/ushort/float/uint 之内");
                    continue;
                }
                if (!TryParseAccess(Field("Access"), out TagAccess access))
                {
                    errors.Add($"[{CodeType}] 第 {lineNo} 行：Access \"{Field("Access")}\" 不在 R/RW 之内");
                    continue;
                }

                // float/uint 占 2 个寄存器，地址必须偶对齐
                if ((type == TagType.Float || type == TagType.UInt) && address % 2 != 0)
                {
                    errors.Add($"[{CodeAlign}] 第 {lineNo} 行：{type} 类型占用 2 个寄存器，Address {address} 必须为偶数");
                    continue;
                }

                // Scale：大于 0 的数字
                string scaleField = Field("Scale");
                if (!double.TryParse(scaleField, NumberStyles.Float, CultureInfo.InvariantCulture, out double scale)
                    || scale <= 0)
                {
                    errors.Add($"[{CodeScale}] 第 {lineNo} 行：Scale \"{scaleField}\" 必须是大于 0 的数字");
                    continue;
                }

                // UnitId：可选，1~255
                byte unitId = 1;
                string uidField = Field("UnitId");
                if (uidField.Length > 0 && (!byte.TryParse(uidField, out unitId) || unitId == 0))
                {
                    errors.Add($"[{CodeAddress}] 第 {lineNo} 行：UnitId \"{uidField}\" 必须是 1~255 的整数");
                    continue;
                }

                // 地址占用检查：float/uint 跨 2 个寄存器，与同地址空间内其他点重叠只警告（W02）
                var occupied = type == TagType.Coil ? occupiedCoils : occupiedRegisters;
                int span = type == TagType.Float || type == TagType.UInt ? 2 : 1;
                for (int a = address; a < address + span; a++)
                {
                    if (!occupied.Add(a))
                        warnings.Add($"[{WarnOverlap}] 第 {lineNo} 行：{tagName} 的地址 {a} 与其他标签的地址范围重叠");
                }

                // Min/Max 可选列（显示量程）：只填其一 / Min≥Max / 非数字 → W03 警告不阻塞
                double? minV = null, maxV = null;
                string minField = Field("Min"), maxField = Field("Max");
                if (minField.Length > 0 || maxField.Length > 0)
                {
                    bool minOk = double.TryParse(minField, NumberStyles.Float, CultureInfo.InvariantCulture, out double mn);
                    bool maxOk = double.TryParse(maxField, NumberStyles.Float, CultureInfo.InvariantCulture, out double mx);
                    if (minField.Length > 0 && maxField.Length > 0 && minOk && maxOk && mn < mx)
                    {
                        minV = mn; maxV = mx;
                    }
                    else
                    {
                        warnings.Add($"[{WarnRange}] 第 {lineNo} 行：{tagName} 的量程 Min/Max 无效（只填其一、非数字或 Min≥Max），已忽略");
                    }
                }

                tags.Add(new TagAddress
                {
                    TagName = tagName,
                    Address = address,
                    Type = type,
                    Access = access,
                    Scale = scale,
                    Unit = Field("Unit"),
                    UnitId = unitId,
                    Group = Field("Group"),
                    Description = Field("Description"),
                    MinValue = minV,
                    MaxValue = maxV,
                });
            }

            return tags;
        }

        /// <summary>
        /// UI 绑定交叉核对（E08/E09/W01）。在 FrmMain 完成控件扫描（ControlTagMap.BuildFrom）后调用。
        ///
        /// 【双向核对】
        ///   - E09：任何 UI 绑定（控制或显示）指向的标签在表中不存在 → Error
        ///   - E08：表中 Access=RW 的标签没有任何控制控件绑定 → Error
        ///          （RW 意味着可写，界面上却没有操作入口，属于契约缺口）
        ///   - W01：表中标签无任何绑定（允许存在，如故障只读点）→ Warn
        /// </summary>
        /// <param name="tags">已加载的标签列表</param>
        /// <param name="controlBindings">控制类控件绑定的标签名集合（UJogSwitch/URotarySwitch/USwitch 的 Tag）</param>
        /// <param name="displayBindings">显示类控件绑定的标签名集合（ParaName / 仪表 Text 等）</param>
        /// <param name="errors">Error 输出列表（追加）</param>
        /// <param name="warnings">Warn 输出列表（追加）</param>
        public static void CheckUiBindings(
            List<TagAddress> tags,
            IEnumerable<string> controlBindings,
            IEnumerable<string> displayBindings,
            List<string> errors,
            List<string> warnings)
        {
            var tagSet = new HashSet<string>(tags.Select(t => t.TagName), StringComparer.OrdinalIgnoreCase);
            var controlSet = new HashSet<string>(controlBindings, StringComparer.OrdinalIgnoreCase);
            var allBoundSet = new HashSet<string>(controlSet, StringComparer.OrdinalIgnoreCase);
            foreach (var b in displayBindings) allBoundSet.Add(b);

            // E09：UI 绑定的标签必须在表中存在
            foreach (var bound in allBoundSet)
            {
                if (!tagSet.Contains(bound))
                    errors.Add($"[{CodeBindingMissing}] UI 绑定的标签 \"{bound}\" 不在标签表中，请检查控件 Tag/ParaName 或补充标签行");
            }

            // E08：RW 标签必须有控制控件；W01：无任何绑定的标签记警告
            foreach (var tag in tags)
            {
                if (tag.Access == TagAccess.RW && !controlSet.Contains(tag.TagName))
                    errors.Add($"[{CodeControlUnbound}] 标签 \"{tag.TagName}\" 为 RW（可写）但界面无对应控制控件，请检查控件 Tag 绑定");
                if (!allBoundSet.Contains(tag.TagName))
                    warnings.Add($"[{WarnTagUnused}] 标签 \"{tag.TagName}\" 在界面中无绑定控件（如为预留点可忽略）");
            }
        }

        /// <summary>按逗号拆分一行。约定文本字段（Unit/Group/Description）内不使用英文逗号。</summary>
        private static string[] SplitCsv(string line) => line.Split(',');

        private static bool TryParseType(string s, out TagType type)
        {
            switch ((s ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "coil": type = TagType.Coil; return true;
                case "ushort": type = TagType.UShort; return true;
                case "float": type = TagType.Float; return true;
                case "uint": type = TagType.UInt; return true;
                default: type = TagType.UShort; return false;
            }
        }

        private static bool TryParseAccess(string s, out TagAccess access)
        {
            switch ((s ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "R": access = TagAccess.R; return true;
                case "RW": access = TagAccess.RW; return true;
                default: access = TagAccess.R; return false;
            }
        }
    }
}
