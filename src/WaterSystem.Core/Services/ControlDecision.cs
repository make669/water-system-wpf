using Intelligent_water_storage_system.Model;

namespace Intelligent_water_storage_system.Services
{
    /// <summary>操控控件类型。决定目标值计算与复原语义：Switch=USwitch（不自翻转），Jog/Rotary=自翻转控件。</summary>
    public enum ToggleControlKind
    {
        /// <summary>USwitch：点击仅发事件不自翻转，HandleToggle 负责翻转</summary>
        Switch,

        /// <summary>UJogSwitch：点击自翻转 IsOn 后才触发事件</summary>
        Jog,

        /// <summary>URotarySwitch：点击/拖拽自翻转 SwitchState 后才触发事件</summary>
        Rotary,
    }

    /// <summary>控制决策动作（HandleToggle 只执行决策，不内嵌判定逻辑）。</summary>
    public enum PlanAction
    {
        /// <summary>预检通过，向用户请求确认</summary>
        AskConfirm,

        /// <summary>用户已确认，执行写线圈</summary>
        ExecuteWrite,

        /// <summary>用户取消：不写；自翻转控件需回翻</summary>
        CancelNoop,

        /// <summary>PLC 未连接：拒绝（不弹确认框）</summary>
        RejectNotConnected,

        /// <summary>Access=R 只读点位：拒绝（uJogSwitch6 类误配的第二道防线）</summary>
        RejectReadOnly,

        /// <summary>标签不在表中（E09 启动校验之后的运行期兜底）</summary>
        TagMissing,

        /// <summary>写入成功：保持新状态</summary>
        KeepNew,

        /// <summary>写入失败：控件视觉回滚到写前值</summary>
        Rollback,
    }

    /// <summary>决策结果：动作 + 目标线圈值 + 是否需要复原控件视觉（自翻转控件的取消/拒绝路径）。</summary>
    public readonly record struct TogglePlan(PlanAction Action, bool TargetValue, bool RevertNeeded);

    /// <summary>
    /// 控制决策纯函数（模块 F 自 FrmMain.HandleToggle 提取，E1 事件链路可测化的关键）。
    ///
    /// 【三阶段模型（docs/模块F-设计.md 第 6 章）】
    ///   阶段一 Precheck：事件进入后、任何 UI 动作前——连接检查 / Access 拒写 / 标签存在性，
    ///           通过则给出目标值并要求确认；
    ///   阶段二 ResolveConfirmation：用户对确认框的应答 → 执行 or 取消复原；
    ///   阶段三 PostWrite：写线圈结果 → 保持 or 回滚。
    ///
    /// 【自翻转语义（缺陷 D3 的核心，原 9.2 未定义）】
    ///   控件事件到达时：
    ///     - USwitch 未翻转（当前值 = 旧状态）→ 目标值 = 当前值取反；取消/拒绝时不翻转（没动过）；
    ///     - UJogSwitch / URotarySwitch 已自翻转（当前值 = 目标状态）→ 目标值 = 当前值；
    ///       取消/拒绝时需回翻（RevertNeeded = true）。
    ///   编程式赋值不触发事件（E1a 预检结论），PLC 同步不会进入本决策。
    /// </summary>
    public static class ControlDecision
    {
        /// <summary>
        /// 阶段一 预检。
        /// </summary>
        /// <param name="isConnected">PLC 连接状态</param>
        /// <param name="tag">按 TagName 解析出的标签（null = 表中不存在）</param>
        /// <param name="kind">控件类型（决定目标值与复原语义）</param>
        /// <param name="currentVisualState">控件当前视觉状态（自翻转控件=新状态，USwitch=旧状态）</param>
        public static TogglePlan Precheck(bool isConnected, TagAddress? tag, ToggleControlKind kind, bool currentVisualState)
        {
            if (tag == null)
                return new TogglePlan(PlanAction.TagMissing, false, false);
            if (!isConnected)
                return new TogglePlan(PlanAction.RejectNotConnected, false, RevertNeeded(kind));
            if (tag.Access != TagAccess.RW)
                return new TogglePlan(PlanAction.RejectReadOnly, false, RevertNeeded(kind));

            return new TogglePlan(PlanAction.AskConfirm, TargetValue(kind, currentVisualState), false);
        }

        /// <summary>
        /// 阶段二 确认：把用户应答转换为执行/取消决策。
        /// </summary>
        /// <param name="confirmed">确认框应答（true=确认）</param>
        /// <param name="kind">控件类型</param>
        /// <param name="currentVisualState">控件当前视觉状态</param>
        public static TogglePlan ResolveConfirmation(bool confirmed, ToggleControlKind kind, bool currentVisualState)
        {
            if (!confirmed)
                return new TogglePlan(PlanAction.CancelNoop, false, RevertNeeded(kind));

            return new TogglePlan(PlanAction.ExecuteWrite, TargetValue(kind, currentVisualState), false);
        }

        /// <summary>
        /// 阶段三 写后：写入成功保持新状态，失败回滚（UI 层负责恢复旧视觉 + 错误提示 + 日志）。
        /// </summary>
        public static PlanAction PostWrite(bool writeSucceeded)
            => writeSucceeded ? PlanAction.KeepNew : PlanAction.Rollback;

        /// <summary>确认文案：泵启停用"启动/停止水泵"，阀门用"打开/关闭阀门"（随目标值）。</summary>
        public static string DescribeAction(string tagName, bool targetValue)
        {
            bool isPump = tagName.Contains("Pump", StringComparison.OrdinalIgnoreCase);
            return (isPump, targetValue) switch
            {
                (true, true) => "启动水泵",
                (true, false) => "停止水泵",
                (false, true) => "打开阀门",
                (false, false) => "关闭阀门",
            };
        }

        /// <summary>自翻转控件（Jog/Rotary）取消/拒绝时需要回翻；USwitch 未翻转无需复原。</summary>
        private static bool RevertNeeded(ToggleControlKind kind) => kind != ToggleControlKind.Switch;

        /// <summary>目标值：USwitch 取反（未自翻转）；Jog/Rotary 取当前值（已自翻转，当前即目标）。</summary>
        private static bool TargetValue(ToggleControlKind kind, bool currentVisualState)
            => kind == ToggleControlKind.Switch ? !currentVisualState : currentVisualState;
    }
}
