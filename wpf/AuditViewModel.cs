// @author zenjiro 18967498922@163.com
// 文件用途 WPF 系统体检页 ViewModel：三态（空闲/扫描中/结果）状态机，封装 SystemAudit.Collect 后台调用

using System;
using System.Collections.ObjectModel;
using System.Threading;

namespace CaelusApp
{
    internal enum AuditState
    {
        Idle = 0,
        Scanning = 1,
        Result = 2
    }

    internal sealed class AuditViewModel : ViewModelBase
    {
        public const int QuickWindowMs = 3000;
        public const int PreciseWindowMs = 30000;

        private AuditState state = AuditState.Idle;
        private double progress;
        private string phaseText = "";
        private string statusText = "";
        private bool buttonsEnabled = true;
        private string nvProbeText;
        private string amdProbeText;
        private int busy; // 0=空闲，1=占用（Interlocked）
        private bool hasResultData;
        private System.DateTime lastCheck;

        public AuditViewModel()
        {
            CapabilityRows = new ObservableCollection<AuditRowView>();
            MachineRows = new ObservableCollection<AuditRowView>();
            PersistentRows = new ObservableCollection<AuditRowView>();
            VerdictRows = new ObservableCollection<AuditRowView>();
            UpdatePhaseText();
        }

        // —— 标题区 ——
        public string PageTitle { get { return Lang.T("nav.audit"); } }
        public string PageSub { get { return Lang.T("audit.sub"); } }

        // —— 空闲态文案 ——
        public string StartButtonText { get { return Lang.T("audit.start"); } }
        public string IdleTitle { get { return Lang.T("audit.idle.title"); } }
        public string IdleHint { get { return Lang.T("audit.idle.hint"); } }

        // —— 触发按钮文案 ——
        public string QuickText { get { return Lang.T("audit.rerun"); } }
        public string PreciseText { get { return Lang.T("audit.precise"); } }
        public string NvProbeText { get { return Lang.T("audit.nvprobe"); } }
        public string AmdProbeText { get { return Lang.T("audit.amdprobe"); } }
        public string NvUnavailableReason { get { return NvAvailable ? "" : Lang.T("audit.nv.unavailable"); } }
        public string AmdUnavailableReason { get { return AmdAvailable ? "" : Lang.T("audit.amd.unavailable"); } }
        public string ScoreMethodText { get { return "评分从 100 分起算，每项需关注内容扣 6 分，最低为 0 分。"; } }

        // —— 结果分组标题 ——
        public string CapabilityTitle { get { return Lang.T("audit.sec.capability"); } }
        public string MachineTitle { get { return Lang.T("audit.sec.machine"); } }
        public string PersistentTitle { get { return Lang.T("audit.sec.persistent"); } }
        public string VerdictTitle { get { return Lang.T("audit.sec.verdicts"); } }
        public string FooterText { get { return Lang.T("audit.footer"); } }

        // —— 探测按钮可用性 ——
        public bool NvAvailable { get { try { return NvApi.Available; } catch { return false; } } }
        public bool AmdAvailable { get { try { return AdlxTweaks.Available; } catch { return false; } } }
        public bool NvProbeEnabled { get { return buttonsEnabled && NvAvailable; } }
        public bool AmdProbeEnabled { get { return buttonsEnabled && AmdAvailable; } }

        // —— 状态绑定 ——
        public AuditState State
        {
            get { return state; }
            internal set { SetProperty(ref state, value, "State"); RaiseStateBools(); }
        }

        public bool IsIdle { get { return state == AuditState.Idle; } }
        public bool IsScanning { get { return state == AuditState.Scanning; } }
        public bool HasResult { get { return state == AuditState.Result; } }
        public bool HasResultData { get { return hasResultData; } }
        public bool ShowResultTools { get { return state == AuditState.Result; } }

        private void RaiseStateBools()
        {
            Raise("IsIdle");
            Raise("IsScanning");
            Raise("HasResult");
            Raise("ShowResultTools");
        }

        private void SetHasResultData(bool value)
        {
            if (hasResultData == value) return;
            hasResultData = value;
            Raise("HasResultData");
        }

        public double Progress
        {
            get { return progress; }
            set { SetProperty(ref progress, value, "Progress"); }
        }

        public string PhaseText
        {
            get { return phaseText; }
            set { SetProperty(ref phaseText, value, "PhaseText"); }
        }

        public string StatusText
        {
            get { return statusText; }
            set { SetProperty(ref statusText, value, "StatusText"); }
        }

        public bool ButtonsEnabled
        {
            get { return buttonsEnabled; }
            set
            {
                if (SetProperty(ref buttonsEnabled, value, "ButtonsEnabled"))
                {
                    Raise("NvProbeEnabled");
                    Raise("AmdProbeEnabled");
                }
            }
        }

        // —— 结果行集合 ——
        public ObservableCollection<AuditRowView> CapabilityRows { get; private set; }
        public ObservableCollection<AuditRowView> MachineRows { get; private set; }
        public ObservableCollection<AuditRowView> PersistentRows { get; private set; }
        public ObservableCollection<AuditRowView> VerdictRows { get; private set; }

        // —— 健康摘要（结果态顶部）——
        // 关注项 = 全部警告行数；评分 = 100 - 关注项×6（下限 0）；等级按评分档
        public int ConcernCount
        {
            get
            {
                int n = 0;
                foreach (AuditRowView r in CapabilityRows) if (r.Warn) n++;
                foreach (AuditRowView r in MachineRows) if (r.Warn) n++;
                foreach (AuditRowView r in PersistentRows) if (r.Warn) n++;
                foreach (AuditRowView r in VerdictRows) if (r.Warn) n++;
                return n;
            }
        }
        public int Score
        {
            get { int s = 100 - ConcernCount * 6; return s < 0 ? 0 : s; }
        }
        public string HealthLabel
        {
            get
            {
                int s = Score;
                if (s >= 85) return "优秀";
                if (s >= 70) return "良好";
                return "需优化";
            }
        }
        public bool IsCaution { get { return ConcernCount > 0; } }
        public bool PersistentHasWarn { get { return AnyWarn(PersistentRows); } }
        public bool VerdictHasWarn { get { return AnyWarn(VerdictRows); } }
        public string LastCheckText
        {
            get
            {
                if (lastCheck == default(System.DateTime)) return "";
                return "上次体检：" + lastCheck.ToString("HH:mm");
            }
        }
        private static bool AnyWarn(System.Collections.Generic.IEnumerable<AuditRowView> rows)
        {
            foreach (AuditRowView r in rows) if (r.Warn) return true;
            return false;
        }
        // 供视图刷新健康绑定（RenderReport 与样例注入后调用）
        internal void NotifyHealth()
        {
            SetHasResultData(true);
            Raise("ConcernCount"); Raise("Score"); Raise("HealthLabel");
            Raise("IsCaution"); Raise("PersistentHasWarn"); Raise("VerdictHasWarn");
            Raise("LastCheckText");
        }

        // 启动快速（3s）/ 精确（30s）体检
        public void StartAudit(int windowMs)
        {
            if (Interlocked.Exchange(ref busy, 1) == 1) return;
            nvProbeText = null;
            amdProbeText = null;
            BeginScan(windowMs);
            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                AuditReport report = null;
                try { report = SystemAudit.Collect(windowMs); }
                catch { }
                Interlocked.Exchange(ref busy, 0);
                DispatchFinish(report, windowMs, false);
            });
        }

        // NVIDIA 写入实测：先做探针，再 Collect
        public void StartNvProbe()
        {
            if (!NvAvailable)
            {
                StatusText = Lang.T("audit.nv.unavailable");
                return;
            }
            if (Interlocked.Exchange(ref busy, 1) == 1) return;
            BeginScan(QuickWindowMs);
            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                string summary;
                try
                {
                    var results = NvDrsTweaks.ProbeWriteback();
                    if (results.Count == 0) summary = Lang.T("audit.nv.failed");
                    else
                    {
                        int ok = 0;
                        var parts = new System.Collections.Generic.List<string>();
                        foreach (var r in results)
                        {
                            if (r.Ok) ok++;
                            parts.Add(r.Key + "=" + r.Outcome);
                        }
                        summary = Lang.F("audit.nv.summary", ok, results.Count)
                            + "（" + string.Join("，", parts.ToArray()) + "）";
                    }
                }
                catch (Exception ex) { summary = Lang.T("audit.nv.failed") + " " + ex.Message; }
                nvProbeText = summary;
                AuditReport report = null;
                try { report = SystemAudit.Collect(QuickWindowMs); }
                catch { }
                Interlocked.Exchange(ref busy, 0);
                DispatchFinish(report, QuickWindowMs, true);
            });
        }

        // AMD 写入实测：先做探针，再 Collect
        public void StartAmdProbe()
        {
            if (!AmdAvailable)
            {
                StatusText = Lang.T("audit.amd.unavailable");
                return;
            }
            if (Interlocked.Exchange(ref busy, 1) == 1) return;
            BeginScan(QuickWindowMs);
            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                string summary;
                try
                {
                    var rows = AdlxTweaks.ProbeWriteback();
                    if (rows.Count == 0) summary = Lang.T("audit.amd.failed");
                    else
                    {
                        int ok = 0;
                        var parts = new System.Collections.Generic.List<string>();
                        foreach (var r in rows)
                        {
                            if (r.Ok) ok++;
                            parts.Add(r.Name + "=" + r.Outcome);
                        }
                        summary = Lang.F("audit.nv.summary", ok, rows.Count)
                            + "（" + string.Join("，", parts.ToArray()) + "）";
                    }
                }
                catch (Exception ex) { summary = Lang.T("audit.amd.failed") + " " + ex.Message; }
                amdProbeText = summary;
                AuditReport report = null;
                try { report = SystemAudit.Collect(QuickWindowMs); }
                catch { }
                Interlocked.Exchange(ref busy, 0);
                DispatchFinish(report, QuickWindowMs, true);
            });
        }

        private void BeginScan(int windowMs)
        {
            ButtonsEnabled = false;
            StatusText = "";
            Progress = 0;
            State = AuditState.Scanning;
            UpdatePhaseText();
        }

        // ThreadPool 完成回调 → Dispatcher 切回 UI 线程
        private void DispatchFinish(AuditReport report, int windowMs, bool isProbe)
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(delegate
            {
                ButtonsEnabled = true;
                if (report == null)
                {
                    StatusText = Lang.T("audit.failed");
                    State = hasResultData ? AuditState.Result : AuditState.Idle;
                    UpdatePhaseText();
                    return;
                }
                StatusText = "";
                Progress = 1;
                PhaseText = Lang.T("audit.phase.done");
                RenderReport(report);
            }));
        }

        private void RenderReport(AuditReport report)
        {
            FillGroup(CapabilityRows, report.Capability, "NVIDIA 驱动接口", nvProbeText);
            FillGroup(MachineRows, report.Machine, null, null);
            FillGroup(PersistentRows, report.Persistent, null, null);
            FillGroup(VerdictRows, report.Verdicts, "AMD 驱动接口", amdProbeText);
            lastCheck = System.DateTime.Now;
            NotifyHealth();
            SetHasResultData(true);
            State = AuditState.Result;
        }

        private void FillGroup(ObservableCollection<AuditRowView> target,
            System.Collections.Generic.List<AuditRow> source, string probeRowName, string probeText)
        {
            target.Clear();
            if (source == null) return;
            foreach (AuditRow row in source)
            {
                string note = row.Note ?? "";
                if (probeText != null && probeRowName != null && row.Name == probeRowName)
                    note = Lang.T("audit.nv.result") + probeText;
                target.Add(new AuditRowView(row.Name, row.Value, note,
                    Lang.T("audit.evidence") + (row.Evidence ?? ""), row.Warn));
            }
        }

        // 根据进度比给出阶段文案（与 WinForms 一致的分段）
        internal void ReportProgress(double ratio)
        {
            Progress = ratio;
            phaseText = AuditPhaseText(ratio);
            Raise("PhaseText");
        }

        private void UpdatePhaseText()
        {
            phaseText = AuditPhaseText(Progress);
            Raise("PhaseText");
        }

        // 与 WinForms AuditPhaseText 相同的分段
        internal static string AuditPhaseText(double ratio)
        {
            if (ratio < 0.12) return Lang.T("audit.phase.capability");
            if (ratio < 0.72) return Lang.T("audit.phase.measure");
            if (ratio < 0.92) return Lang.T("audit.phase.persistent");
            return Lang.T("audit.phase.verdict");
        }
    }

    // 单条结果行（视图模型）
    internal sealed class AuditRowView
    {
        public string Name { get; private set; }
        public string Value { get; private set; }
        public string Note { get; private set; }
        public string Evidence { get; private set; }
        public bool Warn { get; private set; }

        public AuditRowView(string name, string value, string note, string evidence, bool warn)
        {
            Name = name;
            Value = value;
            Note = note;
            Evidence = evidence;
            Warn = warn;
        }
    }
}
