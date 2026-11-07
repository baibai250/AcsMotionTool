using System.Text;

namespace AcsMotionTool.Core;

/// <summary>自检结果</summary>
public sealed class AcsSelfTestReport
{
    public List<string> Lines { get; } = new();
    public bool Passed { get; private set; } = true;

    internal void Ok(string message) => Lines.Add($"[OK]   {message}");

    internal void Fail(string message)
    {
        Lines.Add($"[FAIL] {message}");
        Passed = false;
    }

    internal void Info(string message) => Lines.Add($"       {message}");

    internal void Header(string message)
    {
        Lines.Add(string.Empty);
        Lines.Add($"== {message} ==");
    }

    public string Text => string.Join(Environment.NewLine, Lines);

    public override string ToString() => Text;
}

/// <summary>
/// 环境自检:不依赖界面，按顺序验证连接、状态读取、使能、运动、IO、变量、终端等主要功能。
/// 可用于确认 ACS 运行环境是否正常(例如模拟器是否可用)。
/// </summary>
public static class AcsSelfTest
{
    /// <summary>执行自检</summary>
    /// <param name="options">连接参数(模拟器或以太网)</param>
    /// <param name="axisIndex">用于运动测试的轴号</param>
    public static AcsSelfTestReport Run(AcsConnectionOptions options, int axisIndex = 0)
    {
        var report = new AcsSelfTestReport();
        report.Lines.Add($"ACS 环境自检  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        report.Lines.Add($"连接方式: {options.Describe()}");

        using var controller = new AcsController();
        controller.LogRaised += (_, e) =>
        {
            if (e.Level is AcsLogLevel.Warn or AcsLogLevel.Error)
            {
                report.Info($"日志({e.Level}): {e.Message}");
            }
        };

        report.Header("1. 连接");
        if (!controller.Connect(options))
        {
            report.Fail("连接失败，后续测试跳过");
            return report;
        }
        report.Ok($"连接成功，IsConnected={controller.IsConnected}，模拟器={controller.IsSimulator}");

        var info = controller.Info;
        if (info == null)
        {
            report.Fail("读取控制器信息失败");
        }
        else
        {
            report.Ok($"控制器信息: 固件={info.FirmwareVersion}, 序列号={info.SerialNumber}, 轴数={info.AxisCount:F0}, 缓冲区={info.BufferCount:F0}");
        }

        var axis = controller.GetAxis(axisIndex, $"Axis{axisIndex}");

        report.Header("2. 轴状态与参数");
        var status = axis.ReadStatus(readParameters: true);
        if (!status.IsValid)
        {
            report.Fail("读取轴状态失败");
        }
        else
        {
            report.Ok($"轴{axisIndex}: 位置={status.EncoderPosition:F4}, 命令位置={status.CommandPosition:F4}, 使能={status.IsEnabled}, 到位={status.IsInPosition}, 报警={status.HasFault}");
            report.Info($"参数: vel={status.Velocity}, acc={status.Acceleration}, dec={status.Deceleration}, jerk={status.Jerk}, kdec={status.KillDeceleration}");
        }

        report.Header("3. 伺服使能");
        if (axis.Enable(wait: true, timeoutMs: 15000)) report.Ok("使能成功");
        else report.Fail("使能失败");

        report.Header("4. 参数写入与回读");
        var target = new AcsAxisParameters { Velocity = 20, Acceleration = 200, Deceleration = 200, Jerk = 2000, KillDeceleration = 2000 };
        if (axis.SetParameters(target)) report.Ok($"写入参数 {target}");
        else report.Fail("写入参数失败");

        var readBack = axis.ReadParameters();
        var paramsOk = Math.Abs(readBack.Velocity - target.Velocity) < 1e-6
                       && Math.Abs(readBack.Acceleration - target.Acceleration) < 1e-6
                       && Math.Abs(readBack.Deceleration - target.Deceleration) < 1e-6;
        if (paramsOk) report.Ok($"参数回读一致: {readBack}");
        else report.Fail($"参数回读不一致: {readBack}");

        report.Header("5. 运动测试");
        axis.SetEncoderPosition(0);
        var start = axis.ReadStatus().EncoderPosition;
        if (axis.MoveAbsolute(10, 20, 200, 200, wait: true, timeoutMs: 30000))
        {
            var position = axis.ReadStatus().EncoderPosition;
            report.Ok($"绝对运动到 10 完成，当前位置={position:F4}");
            if (Math.Abs(position - 10) > 0.5) report.Fail($"位置偏差过大(期望 10，实际 {position:F4})");
        }
        else
        {
            report.Fail($"绝对运动失败(起始位置 {start:F4})");
        }

        if (axis.MoveRelative(5, 20, null, null, wait: true, timeoutMs: 30000))
        {
            var position = axis.ReadStatus().EncoderPosition;
            report.Ok($"相对运动 +5 完成，当前位置={position:F4}");
        }
        else
        {
            report.Fail("相对运动失败");
        }

        if (axis.StartJog(5))
        {
            Thread.Sleep(300);
            var moving = axis.ReadStatus().IsMoving;
            axis.Stop();
            Thread.Sleep(200);
            report.Ok($"Jog 启动成功，运动中={moving}，停止后运动={axis.ReadStatus().IsMoving}");
        }
        else
        {
            report.Fail("Jog 启动失败");
        }

        report.Header("6. 数字 IO");
        var inputPort = controller.Io.ReadInputPort(0);
        report.Ok($"读输入端口 0 = 0x{inputPort:X}");
        if (controller.Io.SetOutputPort(0, 0x0001))
        {
            var outputPort = controller.Io.ReadOutputPort(0);
            report.Ok($"写输出端口 0 = 0x0001，回读 = 0x{outputPort:X}");
            controller.Io.SetOutputPort(0, 0x0000);
        }
        else
        {
            report.Fail("写输出端口失败");
        }

        report.Header("7. 变量与终端");
        var fpos = controller.Variables.ReadAsText("FPOS(0)");
        if (!string.IsNullOrEmpty(fpos)) report.Ok($"读变量 FPOS(0) = {fpos}");
        else report.Fail("读变量 FPOS(0) 失败");

        var query = controller.Terminal.Transaction("?FPOS(0)");
        if (!string.IsNullOrEmpty(query) && !query.StartsWith("错误")) report.Ok($"终端 ?FPOS(0) -> {query.Trim()}");
        else report.Fail($"终端 ?FPOS(0) 失败: {query}");

        report.Header("8. 运动终止码判定(5000~5008 是正常终止，5009 起才是故障)");
        axis.StartJog(5);
        Thread.Sleep(300);
        axis.Stop();
        Thread.Sleep(300);
        var stopped = axis.ReadStatus();
        report.Info($"HALT 之后 MERR = {AcsMotionErrors.DescribeWithCode(stopped.MotionError)}");
        if (AcsMotionErrors.IsFault(stopped.MotionError))
        {
            report.Fail($"HALT 之后出现故障码: {AcsMotionErrors.DescribeForDisplay(stopped.MotionError)}");
        }
        else if (!stopped.HasFault)
        {
            report.Ok("正常终止码未被误判为报警(HasFault=False)");
        }
        else
        {
            report.Fail("轴被误判为报警");
        }

        report.Header("9. 回零能力");
        if (axis.Home(new AcsHomingOptions { Mode = AcsHomingMode.SoftZero }, 10000))
        {
            var zeroPosition = axis.ReadStatus().EncoderPosition;
            if (Math.Abs(zeroPosition) < 1e-6) report.Ok($"软件置零可用(当前位置 {zeroPosition:F4})");
            else report.Fail($"软件置零后位置不为 0: {zeroPosition:F4}");
        }
        else
        {
            report.Fail("软件置零失败");
        }

        if (options.Mode == AcsConnectionMode.Simulator)
        {
            report.Info("模拟器没有原点开关/索引脉冲: 只能用“软件置零”模拟回零，HOME 指令会一直找原点直到超时");
        }

        report.Header("10. 程序缓冲区");
        var buffers = controller.Program.GetBuffersCount();
        if (buffers > 0) report.Ok($"控制器可用程序缓冲区数量 = {buffers:F0}");
        else report.Fail("读取缓冲区数量失败");

        var testBuffer = (int)Math.Min(63, Math.Max(0, buffers - 1));
        var programText = AcsProgramText.SoftZeroProgram(axisIndex);
        if (controller.Program.Download(testBuffer, programText))
        {
            var uploaded = controller.Program.Upload(testBuffer);
            if (uploaded.Contains("SET RPOS", StringComparison.OrdinalIgnoreCase))
                report.Ok($"程序写入(覆盖)/回读正常: 缓冲区 {testBuffer} = {uploaded.Replace("\r\n", " | ").Trim()}");
            else
                report.Fail($"程序回读内容异常: '{uploaded.Replace("\r\n", " | ").Trim()}'");
        }
        else
        {
            report.Fail($"程序写入失败(缓冲区 {testBuffer})");
        }

        report.Header("11. 收尾");
        axis.Stop();
        axis.Disable();
        report.Ok("已停止并下使能");
        controller.Disconnect();
        report.Ok("已断开连接");

        report.Lines.Add(string.Empty);
        report.Lines.Add(report.Passed ? "自检结论: 通过 ✔" : "自检结论: 存在失败项 ✘");
        return report;
    }

    /// <summary>把自检报告写入文件</summary>
    public static bool WriteReport(AcsSelfTestReport report, string path)
    {
        try
        {
            File.WriteAllText(path, report.Text, Encoding.UTF8);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
