using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using AcsMotionTool.App.Infrastructure;
using AcsMotionTool.App.Views;
using AcsMotionTool.Core;

namespace AcsMotionTool.App;

/// <summary>
/// 应用入口与组合根:
///   1) 先显示“初始化界面”，依次完成 加载配置 → 初始化数据库 → 连接 ACS → 加载软限位，成功时显示“ACS 初始化成功”；
///   2) 点“进入主界面”后创建主窗口(与初始化界面共享同一套服务/ViewModel，已建立的连接直接可用)；
///   3) `--selftest` 参数进入无界面自检模式。
/// 视图代码后置不含业务逻辑。
/// </summary>
public partial class App : Application
{
    private AppHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            WriteCrashLog("AppDomain.UnhandledException", args.ExceptionObject as Exception);

        DispatcherUnhandledException += (_, args) =>
        {
            WriteCrashLog("DispatcherUnhandledException", args.Exception);
            MessageBox.Show($"发生未处理的异常:\n\n{args.Exception.Message}\n\n详细信息已写入:\n{CrashLogPath}",
                "ACS 调试工具", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // 命令行自检模式: AcsMotionTool.exe --selftest [--ip=192.168.0.10] [--axis=0] [--quiet]
        if (e.Args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            RunSelfTest(e.Args);
            Shutdown();
            return;
        }

        _host = AppHost.Create(new WpfUiDispatcher(Dispatcher), new WpfDialogService());
        Exit += (_, _) => _host?.Dispose();

        ShowInitializationWindow(_host);
    }

    /// <summary>显示初始化界面，并在收到“进入主界面”请求时切换到主窗口</summary>
    private void ShowInitializationWindow(AppHost host)
    {
        var initializationWindow = new InitializationWindow { DataContext = host.Initialization };
        MainWindow = initializationWindow;

        host.Initialization.EnterMainRequested += (_, _) =>
        {
            var mainWindow = new MainWindow { DataContext = host.Main };
            MainWindow = mainWindow;

            // 先显示主窗口再关闭初始化窗口，避免出现“零窗口”导致程序退出
            mainWindow.Show();
            initializationWindow.Close();
        };

        initializationWindow.Show();

        // 窗口显示后再跑初始化流程，用户能立刻看到进度
        _ = host.Initialization.InitializeAsync();
    }

    /// <summary>崩溃日志路径(exe 同目录)</summary>
    public static string CrashLogPath => Path.Combine(AppContext.BaseDirectory, "acs-crash.log");

    /// <summary>自检报告路径(exe 同目录)</summary>
    public static string SelfTestLogPath => Path.Combine(AppContext.BaseDirectory, "acs-selftest.log");

    private static void RunSelfTest(string[] args)
    {
        var ip = ParseArg(args, "--ip=");
        var axis = int.TryParse(ParseArg(args, "--axis="), out var a) ? a : 0;
        var quiet = args.Any(x => x.Equals("--quiet", StringComparison.OrdinalIgnoreCase));

        var options = string.IsNullOrWhiteSpace(ip)
            ? new AcsConnectionOptions { Mode = AcsConnectionMode.Simulator, TimeoutMs = 5000 }
            : new AcsConnectionOptions { Mode = AcsConnectionMode.Ethernet, IpAddress = ip!, TimeoutMs = 5000 };

        var report = AcsSelfTest.Run(options, axis);
        AcsSelfTest.WriteReport(report, SelfTestLogPath);

        if (quiet) return;

        MessageBox.Show(report.Text + $"\n\n报告已保存到:\n{SelfTestLogPath}",
            report.Passed ? "ACS 自检: 通过" : "ACS 自检: 存在失败项",
            MessageBoxButton.OK,
            report.Passed ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private static string? ParseArg(string[] args, string prefix)
        => args.FirstOrDefault(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

    private static void WriteCrashLog(string source, Exception? ex)
    {
        try
        {
            var text = new StringBuilder()
                .AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {source}")
                .AppendLine(ex?.ToString() ?? "(无异常信息)")
                .AppendLine(new string('-', 80))
                .ToString();
            File.AppendAllText(CrashLogPath, text, Encoding.UTF8);
        }
        catch
        {
            // 记录失败时忽略，避免二次异常
        }
    }
}
