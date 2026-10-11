#!/usr/bin/env bash
# 生成《合并阅读版》：源 = 五份分册（唯一事实源）；输出 = 合并阅读版.md（生成物，勿手改）。
# 用法：Git Bash 中执行  docs/build-merged.sh
set -euo pipefail
cd "$(dirname "$0")"

out="合并阅读版.md"
docs=(
  "系统总体设计.md"
  "WinForms转WPF迁移实施方案.md"
  "WPF实施细则.md"
  "WPF-UI详细设计书.md"
  "UI人性化设计概要.md"
)
declare -A scope=(
  ["系统总体设计.md"]="纲：系统使命/六层架构/模块职责边界/数据流/状态体系/质量体系/文档管辖权"
  ["WinForms转WPF迁移实施方案.md"]="怎么迁：阶段 M0~M9/决策 D1~D11/风险登记/可行性验证/git 纪律"
  ["WPF实施细则.md"]="什么顺序做：T 编号任务/三类验收/坐标总表/VM 规格/测试用例/git 节拍"
  ["WPF-UI详细设计书.md"]="怎么画：自解释原则/状态矩阵/逐卡详设/管廊拓扑/copydeck/组件策略"
  ["UI人性化设计概要.md"]="为什么：现状评审 P1~P18/设计原则/深色风格定稿/色板令牌"
)

ver() { grep -m1 '^| 文档版本' "$1" | cut -d'|' -f3 | tr -d ' '; }

{
  echo "# 智能水箱上位机 · WPF 迁移文档合订本（自动生成，勿手改）"
  echo
  echo "> 本文件由 \`docs/build-merged.sh\` 拼装生成，**修订请改对应分册后重新生成**。"
  echo "> 生成时间：$(date '+%Y-%m-%d %H:%M')　|　拼装顺序 = 阅读顺序（总体设计 → 方案 → 细则 → 设计书 → 概要）"
  echo
  echo "| # | 分册 | 版本 | 管辖权（唯一出处） |"
  echo "|---|---|---|---|"
  i=1
  for d in "${docs[@]}"; do
    echo "| $i | $d | $(ver "$d") | ${scope[$d]} |"
    i=$((i+1))
  done
  n=1
  for d in "${docs[@]}"; do
    echo
    echo "---"
    echo
    echo "# 第 ${n} 部分 · ${d%.md}（$(ver "$d")）"
    echo
    cat "$d"
    n=$((n+1))
  done
} > "$out"
echo "generated $out"
