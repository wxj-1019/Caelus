using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Microsoft.Build.Tasks.Windows { public class MarkupCompilePass1 : Task : Task
{
    // 与真�?MarkupCompilePass1 完全一致的属性签�?    public string Language { get; set; }
    public string UICulture { get; set; }
    public ITaskItem[] ApplicationMarkup { get; set; }
    public ITaskItem[] SplashScreen { get; set; }
    public string LanguageSourceExtension { get; set; }
    public ITaskItem[] PageMarkup { get; set; }
    public ITaskItem[] ContentFiles { get; set; }
    public string AssemblyName { get; set; }
    public string OutputType { get; set; }
    public string AssemblyVersion { get; set; }
    public string AssemblyPublicKeyToken { get; set; }
    public ITaskItem[] References { get; set; }
    public string RootNamespace { get; set; }
    public string[] KnownReferencePaths { get; set; }
    public ITaskItem[] AssembliesGeneratedDuringBuild { get; set; }
    public bool AlwaysCompileMarkupFilesInSeparateDomain { get; set; }
    public bool HostInBrowser { get; set; }
    public string LocalizationDirectivesToLocFile { get; set; }
    public bool ContinueOnError { get; set; }
    public ITaskItem[] SourceCodeFiles { get; set; }
    public string DefineConstants { get; set; }
    public ITaskItem[] ExtraBuildControlFiles { get; set; }
    public bool XamlDebuggingInformation { get; set; }
    public bool IsRunningInVisualStudio { get; set; }
    public string OutputPath { get; set; }

    [Output] public ITaskItem[] GeneratedCodeFiles { get; set; }
    [Output] public ITaskItem[] GeneratedBamlFiles { get; set; }
    [Output] public ITaskItem[] GeneratedLocalizationFiles { get; set; }
    [Output] public bool RequirePass2ForMainAssembly { get; set; }
    [Output] public bool RequirePass2ForSatelliteAssembly { get; set; }
    [Output] public ITaskItem[] AllGeneratedFiles { get; set; }

    public override bool Execute()
    {
        string log = Path.Combine(Path.GetTempPath(), "CaelusWpf.mc1.log");
        try
        {
            string pbt = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "PresentationBuildTasks.dll");
            Assembly asm = Assembly.LoadFrom(pbt);
            Type realType = asm.GetType("Microsoft.Build.Tasks.Windows.MarkupCompilePass1", true);
            object real = Activator.CreateInstance(realType);

            // 复制输入属�?            string[] inputProps = {
                "Language", "UICulture", "ApplicationMarkup", "SplashScreen", "LanguageSourceExtension",
                "PageMarkup", "ContentFiles", "AssemblyName", "OutputType", "AssemblyVersion",
                "AssemblyPublicKeyToken", "References", "RootNamespace", "KnownReferencePaths",
                "AssembliesGeneratedDuringBuild", "AlwaysCompileMarkupFilesInSeparateDomain",
                "HostInBrowser", "LocalizationDirectivesToLocFile", "ContinueOnError",
                "SourceCodeFiles", "DefineConstants", "ExtraBuildControlFiles",
                "XamlDebuggingInformation", "IsRunningInVisualStudio", "OutputPath"
            };
            foreach (string p in inputProps)
            {
                PropertyInfo src = GetType().GetProperty(p);
                PropertyInfo dst = realType.GetProperty(p);
                if (src != null && dst != null && dst.CanWrite && src.GetValue(this, null) != null)
                    dst.SetValue(real, src.GetValue(this, null), null);
            }
            // TaskLoggingHelper 注入（真实任务需要）
            PropertyInfo tl = realType.GetProperty("TaskLogger");
            if (tl != null && tl.CanWrite) tl.SetValue(real, Log, null);

            bool ok;
            try
            {
                ok = (bool)realType.GetMethod("Execute").Invoke(real, null);
            }
            catch (TargetInvocationException tie)
            {
                File.AppendAllText(log, "==== EXCEPTION " + DateTime.Now.ToString("HH:mm:ss.fff") + " ====" + Environment.NewLine
                    + tie.InnerException + Environment.NewLine + Environment.NewLine);
                ok = false;
            }
            catch (Exception ex)
            {
                File.AppendAllText(log, "==== EXCEPTION2 " + DateTime.Now.ToString("HH:mm:ss.fff") + " ====" + Environment.NewLine
                    + ex + Environment.NewLine + Environment.NewLine);
                ok = false;
            }

            // 输出属性回�?            string[] outProps = {
                "GeneratedCodeFiles", "GeneratedBamlFiles", "GeneratedLocalizationFiles",
                "RequirePass2ForMainAssembly", "RequirePass2ForSatelliteAssembly", "AllGeneratedFiles"
            };
            foreach (string p in outProps)
            {
                PropertyInfo src = GetType().GetProperty(p);
                PropertyInfo dst = realType.GetProperty(p);
                if (src != null && dst != null && dst.CanRead)
                    src.SetValue(this, dst.GetValue(real, null), null);
            }
            return ok;
        }
        catch (Exception ex)
        {
            try { File.AppendAllText(log, "==== WRAPPER ERROR " + DateTime.Now.ToString("HH:mm:ss.fff") + " ====" + Environment.NewLine + ex + Environment.NewLine); }
            catch { }
            return false;
        }
    }
}}
}
