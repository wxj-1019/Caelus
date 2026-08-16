using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Microsoft.Build.Tasks.Windows
{
public class MarkupCompilePass1 : Task
{
    // 与真实 MarkupCompilePass1 完全一致的属性签名
    public string Language { get; set; }
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

    // 深度遍历字段，hook MarkupCompiler/CompilerWrapper 的 Error 事件，捕获异常对象
    static void HookErrors(object root, string log, int depth)
    {
        if (root == null || depth > 4) return;
        Type t = root.GetType();
        try
        {
            foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                object v;
                try { v = f.GetValue(root); } catch { continue; }
                if (v == null) continue;
                Type vt = v.GetType();
                if (vt.FullName.IndexOf("MarkupCompiler", StringComparison.Ordinal) >= 0
                    || vt.FullName.IndexOf("CompilerWrapper", StringComparison.Ordinal) >= 0)
                {
                    EventInfo ev = vt.GetEvent("Error");
                    if (ev != null)
                    {
                        try
                        {
                            string logRef = log;
                            Delegate d = Delegate.CreateDelegate(ev.EventHandlerType, new Hook(logRef), "OnError");
                            ev.AddEventHandler(v, d);
                            File.AppendAllText(log, "HOOKED Error on " + vt.FullName + Environment.NewLine);
                        }
                        catch (Exception ex) { File.AppendAllText(log, "HOOK FAIL " + vt.FullName + ": " + ex.Message + Environment.NewLine); }
                    }
                    HookErrors(v, log, depth + 1);
                }
                else if (v is System.Collections.IEnumerable && !(v is string))
                {
                    foreach (object item in (System.Collections.IEnumerable)v)
                    {
                        if (item != null && item.GetType().FullName.IndexOf("MarkupCompiler", StringComparison.Ordinal) >= 0)
                            HookErrors(item, log, depth + 1);
                    }
                }
            }
        }
        catch { }
    }

    sealed class Hook
    {
        private readonly string log;
        public Hook(string log) { this.log = log; }
        public void OnError(object sender, object e)
        {
            try
            {
                string msg = e == null ? "null" : e.ToString();
                // MarkupErrorEventArgs 有 Exception 属性
                Exception inner = null;
                try
                {
                    PropertyInfo ep = e.GetType().GetProperty("Exception");
                    if (ep != null) inner = ep.GetValue(e, null) as Exception;
                }
                catch { }
                File.AppendAllText(log, "==== MARKUP ERROR EVENT ====" + Environment.NewLine
                    + "MSG: " + msg + Environment.NewLine
                    + (inner != null ? "EXCEPTION: " + inner + Environment.NewLine + inner.StackTrace + Environment.NewLine : "")
                    + Environment.NewLine);
            }
            catch { }
        }
    }

    public override bool Execute()
    {
        string log = Path.Combine(Path.GetTempPath(), "CaelusWpf.mc1.log");
        // FirstChanceException：捕获所有即将抛出的异常（含堆栈），定位错误源头
        EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> fce = null;
        fce = delegate(object s, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
        {
            try
            {
                Exception ex = e.Exception;
                if (ex != null && (ex.Message.IndexOf("implementation", StringComparison.OrdinalIgnoreCase) >= 0
                    || ex.Message.IndexOf("Command", StringComparison.OrdinalIgnoreCase) >= 0
                    || ex.Message.IndexOf("RepeatButton", StringComparison.OrdinalIgnoreCase) >= 0
                    || ex.Message.IndexOf("没有实现", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("==== FIRSTCHANCE " + DateTime.Now.ToString("HH:mm:ss.fff") + " DOMAIN=" + AppDomain.CurrentDomain.FriendlyName + " ====");
                    sb.AppendLine(ex.GetType().FullName + ": " + ex.Message);
                    sb.AppendLine(ex.StackTrace);
                    if (ex.InnerException != null) sb.AppendLine("INNER: " + ex.InnerException + Environment.NewLine + ex.InnerException.StackTrace);
                    // 立即重试加载关键程序集，确认加载环境
                    try
                    {
                        Assembly pc = Assembly.Load("PresentationCore, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
                        sb.AppendLine("PC LOAD NOW: " + pc.Location);
                        Type ui = pc.GetType("System.Windows.UIElement", true);
                        sb.AppendLine("UIElement OK: " + ui.FullName);
                    }
                    catch (Exception pex) { sb.AppendLine("PC LOAD FAILED: " + pex); }
                    try
                    {
                        Assembly pf2 = Assembly.Load("PresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
                        sb.AppendLine("PF LOAD NOW: " + pf2.Location);
                        Type rb = pf2.GetType("System.Windows.Controls.Primitives.RepeatButton", true);
                        sb.AppendLine("RepeatButton reload OK: " + rb.FullName);
                    }
                    catch (Exception pex2) { sb.AppendLine("PF/RB RELOAD FAILED: " + pex2); }
                    // dump 已加载的 WPF 相关程序集
                    foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        try
                        {
                            if (a.FullName.IndexOf("Presentation", StringComparison.OrdinalIgnoreCase) >= 0
                                || a.FullName.IndexOf("System.Xaml", StringComparison.OrdinalIgnoreCase) >= 0
                                || a.FullName.IndexOf("WindowsBase", StringComparison.OrdinalIgnoreCase) >= 0)
                                sb.AppendLine("  LOADED: " + a.FullName + "  [" + (a.IsDynamic ? "dynamic" : a.Location) + "]");
                        }
                        catch { }
                    }
                    // dump 反射域程序集
                    try
                    {
                        foreach (Assembly a in AppDomain.CurrentDomain.ReflectionOnlyGetAssemblies())
                        {
                            try
                            {
                                if (a.FullName.IndexOf("Presentation", StringComparison.OrdinalIgnoreCase) >= 0
                                    || a.FullName.IndexOf("System.Xaml", StringComparison.OrdinalIgnoreCase) >= 0
                                    || a.FullName.IndexOf("WindowsBase", StringComparison.OrdinalIgnoreCase) >= 0)
                                    sb.AppendLine("  REFLONLY: " + a.FullName + "  [" + (a.IsDynamic ? "dynamic" : a.Location) + "]");
                            }
                            catch { }
                        }
                    }
                    catch (Exception rxe) { sb.AppendLine("  REFLONLY dump fail: " + rxe.Message); }
                    File.AppendAllText(log, sb.ToString() + Environment.NewLine);
                }
            }
            catch { }
        };
        AppDomain.CurrentDomain.FirstChanceException += fce;
        try
        {
            string pbt = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "PresentationBuildTasks.dll");
            // 必须用字节加载：LoadFrom/LoadFile 对强名称程序集都会重定向到 GAC（25H2 版）
            Assembly asm = Assembly.Load(File.ReadAllBytes(pbt));
            File.AppendAllText(log, "PARAMS: SeparateDomain=" + AlwaysCompileMarkupFilesInSeparateDomain
                + " HostInBrowser=" + HostInBrowser + " XamlDbg=" + XamlDebuggingInformation
                + " RefCount=" + (References == null ? 0 : References.Length)
                + " Lang=" + Language + Environment.NewLine);
            if (References != null)
                foreach (ITaskItem ri in References)
                    File.AppendAllText(log, "  REF: " + ri.ItemSpec + Environment.NewLine);
            if (KnownReferencePaths != null)
                foreach (string k in KnownReferencePaths)
                    File.AppendAllText(log, "  KNOWN: " + k + Environment.NewLine);
            // 模拟 TypeIndexer 的加载：LoadFrom 引用文件后 GetType
            try
            {
                // 先试 v4.0 参考程序集目录（KnownReferencePaths 指向它）
                string refPf = @"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.0\PresentationFramework.dll";
                Assembly ra2 = Assembly.LoadFrom(refPf);
                File.AppendAllText(log, "REF40 LOAD: " + refPf + " -> " + ra2.Location + Environment.NewLine);
                try
                {
                    Type rb2 = ra2.GetType("System.Windows.Controls.Primitives.RepeatButton", true);
                    File.AppendAllText(log, "  RB from ref40 OK: " + rb2.AssemblyQualifiedName + Environment.NewLine);
                }
                catch (Exception ex4) { File.AppendAllText(log, "  RB from ref40 FAIL: " + ex4.GetType().Name + ": " + ex4.Message + Environment.NewLine); }
            }
            catch (Exception ex5) { File.AppendAllText(log, "REF40 LOAD FAIL: " + ex5.Message + Environment.NewLine); }
            if (References != null)
            {
                foreach (ITaskItem ri in References)
                {
                    string spec = ri.ItemSpec;
                    if (spec.IndexOf("PresentationFramework", StringComparison.OrdinalIgnoreCase) >= 0
                        || spec.IndexOf("PresentationCore", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        try
                        {
                            Assembly ra = Assembly.LoadFrom(spec);
                            File.AppendAllText(log, "REF LOAD: " + spec + " -> " + ra.Location + Environment.NewLine);
                            if (spec.IndexOf("PresentationFramework", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                try
                                {
                                    Type rb = ra.GetType("System.Windows.Controls.Primitives.RepeatButton", true);
                                    File.AppendAllText(log, "  RB from ref OK: " + rb.AssemblyQualifiedName + Environment.NewLine);
                                }
                                catch (Exception ex2)
                                {
                                    File.AppendAllText(log, "  RB from ref FAIL: " + ex2.GetType().Name + ": " + ex2.Message + Environment.NewLine);
                                }
                            }
                        }
                        catch (Exception ex3) { File.AppendAllText(log, "REF LOAD FAIL: " + spec + " " + ex3.Message + Environment.NewLine); }
                    }
                }
            }
            Type realType = asm.GetType("Microsoft.Build.Tasks.Windows.MarkupCompilePass1", true);
            object real = Activator.CreateInstance(realType);
            HookErrors(real, log, 0);

            // References 中"参考程序集目录"的框架文件：ReflectionHelper 对其
            // ReflectionOnlyLoadFrom 会与已加载实例冲突（API restriction）。
            // 直接把 ItemSpec 改为 GAC 路径（保留元数据，临时项目仍可解析）。
            // 同理覆盖 v4.0.30319 安装目录：本机无 Targeting Pack，csproj 预设
            // _TargetFrameworkDirectories 指向框架安装目录以消除 MSB3644。
            if (References != null)
            {
                foreach (ITaskItem ri in References)
                {
                    string spec = ri.ItemSpec;
                    if (spec.IndexOf("Reference Assemblies", StringComparison.OrdinalIgnoreCase) >= 0
                        || spec.IndexOf(@"Microsoft.NET\Framework\v4.0.30319", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        string name = System.IO.Path.GetFileNameWithoutExtension(spec);
                        try
                        {
                            Assembly resolved = Assembly.Load(name);
                            if (resolved != null && !string.IsNullOrEmpty(resolved.Location) && File.Exists(resolved.Location))
                            {
                                ri.ItemSpec = resolved.Location;
                                File.AppendAllText(log, "  REFMAP: " + name + " -> " + resolved.Location + Environment.NewLine);
                            }
                        }
                        catch (Exception exm) { File.AppendAllText(log, "  REFMAP FAIL " + spec + ": " + exm.Message + Environment.NewLine); }
                    }
                }
            }
            // KnownReferencePaths 中的框架安装目录同样会被 ReflectionHelper
            // ReflectionOnlyLoadFrom 逐个探测，与反射域预加载的 GAC 副本冲突
            // （MC1000 API restriction）。替换为 GAC_MSIL 根目录：无扁平 DLL，
            // 探测自然落空，改走 ReflectionOnlyAssemblyResolve → GAC 按名解析。
            if (KnownReferencePaths != null)
            {
                for (int i = 0; i < KnownReferencePaths.Length; i++)
                {
                    string kp = KnownReferencePaths[i];
                    if (kp != null
                        && (kp.IndexOf(@"Microsoft.NET\Framework\v4.0.30319", StringComparison.OrdinalIgnoreCase) >= 0
                            || kp.IndexOf("Reference Assemblies", StringComparison.OrdinalIgnoreCase) >= 0
                            || kp.IndexOf("RefPack", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        KnownReferencePaths[i] = @"C:\Windows\Microsoft.NET\assembly\GAC_MSIL\";
                    }
                }
            }
            // 复制输入属性
            string[] inputProps = {
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
                if (src == null || dst == null || !dst.CanWrite) continue;
                object v = src.GetValue(this, null);
                if (v == null) continue;
                // 类型自适应：ITaskItem[] -> string[] / bool <-> string 等
                if (!dst.PropertyType.IsInstanceOfType(v))
                {
                    if (v is ITaskItem[] && dst.PropertyType == typeof(string[]))
                    {
                        ITaskItem[] items = (ITaskItem[])v;
                        string[] strs = new string[items.Length];
                        for (int i = 0; i < items.Length; i++) strs[i] = items[i].ItemSpec;
                        v = strs;
                    }
                    else if (dst.PropertyType.IsArray && v is Array)
                    {
                        Array sa = (Array)v;
                        Array da = Array.CreateInstance(dst.PropertyType.GetElementType(), sa.Length);
                        for (int i = 0; i < sa.Length; i++)
                            da.SetValue(Convert.ChangeType(sa.GetValue(i), dst.PropertyType.GetElementType()), i);
                        v = da;
                    }
                    else
                    {
                        try { v = Convert.ChangeType(v, dst.PropertyType); }
                        catch { continue; }
                    }
                }
                dst.SetValue(real, v, null);
            }
            // Task 基类环境注入（反射创建的任务没有 MSBuild 设置的 BuildEngine）
            PropertyInfo be = realType.GetProperty("BuildEngine", BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy);
            if (be != null && be.CanWrite) be.SetValue(real, BuildEngine, null);
            PropertyInfo ho = realType.GetProperty("HostObject", BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy);
            if (ho != null && ho.CanWrite) ho.SetValue(real, HostObject, null);

            // 25H2 CLR 对混合模式 WPF 程序集有"首次加载接口验证"问题：
            // 类型（如 RepeatButton）在 PresentationCore 加载过程中被验证时会误报
            // "does not have an implementation"。预加载全部 WPF 程序集让其先完整就绪。
            string[] preload = {
                "PresentationCore, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35",
                "WindowsBase, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35",
                "System.Xaml, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089",
                "PresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35"
            };
            foreach (string name in preload)
            {
                try
                {
                    Assembly pa = Assembly.Load(name);
                    // 触发类型加载（让 CLR 完成全部验证）
                    foreach (Type pt in pa.GetTypes()) { }
                }
                catch (Exception ex) { File.AppendAllText(log, "PRELOAD FAIL " + name + ": " + ex.Message + Environment.NewLine); }
            }
            // XamlTypeMapper 用 ReflectionOnlyLoadFrom(文件路径) 加载混合模式程序集，
            // 25H2 CLR 在 ReflectionOnly 下对文件加载的类型验证误报 TypeLoadException。
            // ReflectionHelper 会复用反射域中已加载的同名程序集——预加载 GAC 版本到反射域，
            // 让编译器走 Fusion 路径而不是文件路径。
            try
            {
                AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += delegate(object s, ResolveEventArgs re)
                {
                    try { return Assembly.ReflectionOnlyLoad(re.Name); }
                    catch { return null; }
                };
                string[] reflFiles = {
                    @"C:\Windows\Microsoft.NET\assembly\GAC_32\PresentationCore\v4.0_4.0.0.0__31bf3856ad364e35\PresentationCore.dll",
                    @"C:\Windows\Microsoft.NET\assembly\GAC_MSIL\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll",
                    @"C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.Xaml\v4.0_4.0.0.0__b77a5c561934e089\System.Xaml.dll",
                    @"C:\Windows\Microsoft.NET\assembly\GAC_MSIL\PresentationFramework\v4.0_4.0.0.0__31bf3856ad364e35\PresentationFramework.dll"
                };
                foreach (string rf in reflFiles)
                {
                    try
                    {
                        Assembly.ReflectionOnlyLoadFrom(rf);
                        File.AppendAllText(log, "REFLFILE OK: " + rf + Environment.NewLine);
                    }
                    catch (Exception ex) { File.AppendAllText(log, "REFLFILE FAIL " + rf + ": " + ex.Message + Environment.NewLine); }
                }
                // 框架文件（System 等）也预加载到反射域，避免 ReflectionHelper 从
                // 参考程序集目录 LoadFrom 时与已加载实例冲突（API restriction）
                string[] reflNames = {
                    "mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089",
                    "System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089",
                    "System.Core, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089",
                    "System.Xml, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089",
                    "System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a",
                    "System.Management, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a",
                    "System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"
                };
                foreach (string rn in reflNames)
                {
                    try { Assembly.ReflectionOnlyLoad(rn); }
                    catch (Exception ex) { File.AppendAllText(log, "REFLNAME FAIL " + rn + ": " + ex.Message + Environment.NewLine); }
                }
                try
                {
                    int n = AppDomain.CurrentDomain.ReflectionOnlyGetAssemblies().Length;
                    File.AppendAllText(log, "REFLONLY COUNT: " + n + Environment.NewLine);
                }
                catch (Exception ex) { File.AppendAllText(log, "REFLONLY COUNT FAIL: " + ex.Message + Environment.NewLine); }
            }
            catch (Exception ex) { File.AppendAllText(log, "REFLONLY SETUP FAIL: " + ex.Message + Environment.NewLine); }
            File.AppendAllText(log, "PRELOAD DONE" + Environment.NewLine);
            // dump Execute 前的程序集实例（与 Execute 后对比）
            StringBuilder pre = new StringBuilder();
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                try
                {
                    if (a.FullName.IndexOf("Presentation", StringComparison.OrdinalIgnoreCase) >= 0
                        || a.FullName.IndexOf("System.Xaml", StringComparison.OrdinalIgnoreCase) >= 0
                        || a.FullName.IndexOf("WindowsBase", StringComparison.OrdinalIgnoreCase) >= 0)
                        pre.AppendLine("  PRE: " + a.FullName + " [" + (a.IsDynamic ? "dynamic" : a.Location) + "]");
                }
                catch { }
            File.AppendAllText(log, "== BEFORE EXECUTE ==\n" + pre + Environment.NewLine);
            // 复现编译器的反射加载（ReflectionHelper.ReflectionOnlyLoadFrom）——完整顺序
            try
            {
                AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += delegate(object s, ResolveEventArgs re)
                {
                    try { return Assembly.ReflectionOnlyLoad(re.Name); }
                    catch { return null; }
                };
                string pcPath = @"C:\Windows\Microsoft.NET\assembly\GAC_32\PresentationCore\v4.0_4.0.0.0__31bf3856ad364e35\PresentationCore.dll";
                string pfPath = @"C:\Windows\Microsoft.NET\assembly\GAC_MSIL\PresentationFramework\v4.0_4.0.0.0__31bf3856ad364e35\PresentationFramework.dll";
                Assembly rpc = Assembly.ReflectionOnlyLoadFrom(pcPath);
                File.AppendAllText(log, "SEQ: PC from file OK " + rpc.Location + Environment.NewLine);
                Assembly rpf = Assembly.ReflectionOnlyLoadFrom(pfPath);
                File.AppendAllText(log, "SEQ: PF from file OK " + rpf.Location + Environment.NewLine);
                try
                {
                    Type rrb = rpf.GetType("System.Windows.Controls.Primitives.RepeatButton", true);
                    File.AppendAllText(log, "SEQ: RB OK " + rrb.FullName + Environment.NewLine);
                }
                catch (Exception ex8) { File.AppendAllText(log, "SEQ: RB FAIL " + ex8.GetType().Name + ": " + ex8.Message + Environment.NewLine); }
            }
            catch (Exception ex9) { File.AppendAllText(log, "SEQ LOAD FAIL: " + ex9.GetType().Name + ": " + ex9.Message + Environment.NewLine); }

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

            // 输出属性回拷
            string[] outProps = {
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
}
}
