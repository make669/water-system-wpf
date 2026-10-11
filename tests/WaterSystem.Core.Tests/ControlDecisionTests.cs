using Intelligent_water_storage_system.Model;
using Intelligent_water_storage_system.Services;
using Xunit;

namespace IntelligentWaterStorage.Tests
{
    /// <summary>
    /// ControlDecision 控制决策三阶段（模块 F 组 8，6 条；设计依据 docs/模块F-设计.md 7-组8、6.3 决策表）。
    /// 缺陷 D3 的核心语义：自翻转控件（Jog/Rotary）目标值=当前值、取消/拒绝需回翻；USwitch 目标值=取反。
    /// </summary>
    public class ControlDecisionTests
    {
        private static readonly TagAddress RwTag = new() { TagName = "Pump1_Run", Access = TagAccess.RW };
        private static readonly TagAddress RoTag = new() { TagName = "Pump1_Fault", Access = TagAccess.R };

        [Fact]
        public void Precheck_ConnectedRw_AskConfirm_WithKindDependentTarget()
        {
            // USwitch 未自翻转：目标 = 当前取反
            var sw = ControlDecision.Precheck(isConnected: true, RwTag, ToggleControlKind.Switch, currentVisualState: false);
            Assert.Equal(PlanAction.AskConfirm, sw.Action);
            Assert.True(sw.TargetValue);
            Assert.False(sw.RevertNeeded);

            // Jog 已自翻转：目标 = 当前值
            var jog = ControlDecision.Precheck(true, RwTag, ToggleControlKind.Jog, currentVisualState: true);
            Assert.Equal(PlanAction.AskConfirm, jog.Action);
            Assert.True(jog.TargetValue);

            // Rotary 同 Jog
            var rot = ControlDecision.Precheck(true, RwTag, ToggleControlKind.Rotary, currentVisualState: false);
            Assert.False(rot.TargetValue);
        }

        [Fact]
        public void Precheck_NotConnected_Rejects_RevertOnlySelfToggling()
        {
            var jog = ControlDecision.Precheck(false, RwTag, ToggleControlKind.Jog, currentVisualState: true);
            Assert.Equal(PlanAction.RejectNotConnected, jog.Action);
            Assert.True(jog.RevertNeeded);   // 已自翻转 → 需回翻

            var sw = ControlDecision.Precheck(false, RwTag, ToggleControlKind.Switch, currentVisualState: false);
            Assert.Equal(PlanAction.RejectNotConnected, sw.Action);
            Assert.False(sw.RevertNeeded);   // 未翻转 → 无需复原
        }

        [Fact]
        public void Precheck_ReadOnly_Rejects_BeforeConfirm()
        {
            var plan = ControlDecision.Precheck(true, RoTag, ToggleControlKind.Jog, currentVisualState: true);

            Assert.Equal(PlanAction.RejectReadOnly, plan.Action);
            Assert.True(plan.RevertNeeded);
        }

        [Fact]
        public void Precheck_NullTag_ReturnsTagMissing()
        {
            var plan = ControlDecision.Precheck(true, null, ToggleControlKind.Switch, currentVisualState: false);

            Assert.Equal(PlanAction.TagMissing, plan.Action);
        }

        [Fact]
        public void ResolveConfirmation_Cancel_Noop_RevertSelfToggling()
        {
            var cancel = ControlDecision.ResolveConfirmation(confirmed: false, ToggleControlKind.Jog, currentVisualState: true);
            Assert.Equal(PlanAction.CancelNoop, cancel.Action);
            Assert.True(cancel.RevertNeeded);

            var confirmed = ControlDecision.ResolveConfirmation(true, ToggleControlKind.Switch, currentVisualState: false);
            Assert.Equal(PlanAction.ExecuteWrite, confirmed.Action);
            Assert.True(confirmed.TargetValue);
        }

        [Fact]
        public void PostWrite_And_DescribeAction_FollowContract()
        {
            Assert.Equal(PlanAction.KeepNew, ControlDecision.PostWrite(writeSucceeded: true));
            Assert.Equal(PlanAction.Rollback, ControlDecision.PostWrite(writeSucceeded: false));

            Assert.Equal("启动水泵", ControlDecision.DescribeAction("Pump1_Run", targetValue: true));
            Assert.Equal("停止水泵", ControlDecision.DescribeAction("Pump1_Run", targetValue: false));
            Assert.Equal("打开阀门", ControlDecision.DescribeAction("MainValve", targetValue: true));
            Assert.Equal("关闭阀门", ControlDecision.DescribeAction("Valve2", targetValue: false));
        }
    }
}
