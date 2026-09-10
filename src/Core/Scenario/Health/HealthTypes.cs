// @author zenjiro 18967498922@163.com
// 文件用途 维护动作框架的类型：动作契约、扫描报告/执行结果、历史记录

using System;
using System.Collections.Generic;

namespace CaelusApp
{
    internal enum HealthTrigger { Auto, Manual }
    internal enum HealthOutcome { Success, Failed, Skipped }

    /// <summary>一条可处理的发现项（如一个新启动项）。Id 在动作内稳定唯一。</summary>
    internal sealed class HealthFinding
    {
        public string Id;
        public string Label;
        public string Detail;
        public bool Risky;      // 系统/微软项：UI 默认不勾选
    }

    internal sealed class HealthReport
    {
        public string ActionId;
        public long Bytes;
        public readonly List<HealthFinding> Findings = new List<HealthFinding>();
    }

    internal sealed class HealthResult
    {
        public string ActionId;
        public HealthOutcome Outcome;
        public long FreedBytes;
        public int ItemCount;
        public string Error;
        public string Summary;
        public string UndoPayload;   // 可逆动作：还原负载（多行，\n 分隔单行负载）
    }

    internal sealed class HealthRecord
    {
        public string Id;
        public DateTime Time;
        public string Trigger;       // Auto / Manual / Undo
        public string ActionId;
        public HealthOutcome Outcome;
        public long FreedBytes;
        public int ItemCount;
        public string Summary;
        public string UndoPayload;
    }

    internal interface IHealthAction
    {
        string Id { get; }
        string TitleKey { get; }
        string DescKey { get; }
        bool AllowAuto { get; }      // 定时调度是否允许自动 Execute
        bool CanUndo { get; }
        HealthReport Analyze();                          // 纯只读扫描，UI 可反复调用
        HealthResult Execute(string[] selectedIds);      // null=不限定（仅 AllowAuto 自动路径）
        bool Undo(string undoPayload, out string error); // 单行负载还原一条
        List<HealthFinding> ListDisabled();              // 当前禁用态可还原项；不支持则空
    }

    /// <summary>自动维护周期到点时的附带职责（如启动项新闻与基线提交），保持 Analyze 纯。</summary>
    internal interface IHealthAutoCycle
    {
        void OnAutoCycle();
    }
}
