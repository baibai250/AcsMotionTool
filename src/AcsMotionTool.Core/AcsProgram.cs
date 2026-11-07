using ACS.SPiiPlusNET;

namespace AcsMotionTool.Core;

/// <summary>
/// 控制器程序缓冲区(ACSPL+ 程序)操作:上传/下载/编译/运行/停止。
/// 典型的"回零"就是运行控制器里预先写好的回零程序缓冲区。
/// </summary>
public sealed class AcsProgram
{
    private const string LogSource = "Program";
    private readonly AcsController _owner;

    internal AcsProgram(AcsController owner) => _owner = owner;

    /// <summary>缓冲区数量</summary>
    public double GetBuffersCount()
        => _owner.Execute(api => api.GetBuffersCount(), "读取缓冲区数量", 0.0, LogSource);

    /// <summary>运行缓冲区程序</summary>
    public bool Run(int bufferIndex, string? label = null)
    {
        var ok = _owner.Execute(api => api.RunBuffer((ProgramBuffer)bufferIndex, label!), $"运行缓冲区 {bufferIndex}", LogSource);
        if (ok) _owner.LogInfo($"运行程序缓冲区 {bufferIndex}" + (string.IsNullOrEmpty(label) ? "" : $" (label={label})"), LogSource);
        return ok;
    }

    /// <summary>停止缓冲区程序</summary>
    public bool Stop(int bufferIndex)
    {
        var ok = _owner.Execute(api => api.StopBuffer((ProgramBuffer)bufferIndex), $"停止缓冲区 {bufferIndex}", LogSource);
        if (ok) _owner.LogInfo($"停止程序缓冲区 {bufferIndex}", LogSource);
        return ok;
    }

    /// <summary>挂起缓冲区程序</summary>
    public bool Suspend(int bufferIndex)
        => _owner.Execute(api => api.SuspendBuffer((ProgramBuffer)bufferIndex), $"挂起缓冲区 {bufferIndex}", LogSource);

    /// <summary>编译缓冲区程序</summary>
    public bool Compile(int bufferIndex)
        => _owner.Execute(api => api.CompileBuffer((ProgramBuffer)bufferIndex), $"编译缓冲区 {bufferIndex}", LogSource);

    /// <summary>读取缓冲区程序状态</summary>
    public ProgramStates GetState(int bufferIndex)
        => _owner.Execute(api => api.GetProgramState((ProgramBuffer)bufferIndex), $"读取缓冲区 {bufferIndex} 状态", ProgramStates.ACSC_NONE, LogSource);

    /// <summary>上传(读取)控制器缓冲区中的程序文本(空缓冲区返回空字符串)</summary>
    public string Upload(int bufferIndex)
        => _owner.Execute(api => api.UploadBuffer((ProgramBuffer)bufferIndex) ?? string.Empty,
            $"上传缓冲区 {bufferIndex}", string.Empty, LogSource);

    /// <summary>
    /// 下载(写入)程序文本到控制器缓冲区。**先清空再写入**(SDK 的 DownloadBuffer/AppendBuffer 都是“追加”语义，
    /// 直接追加会把新旧程序拼在一起，这里改用 LoadBuffer)。文本会自动清洗为 ASCII 并统一换行。
    /// </summary>
    public bool Download(int bufferIndex, string text)
    {
        var clean = AcsProgramText.Sanitize(text, out var removed);
        if (removed > 0)
        {
            _owner.LogWarn($"程序文本包含 {removed} 个非 ASCII 字符或需转换的注释(控制器只接受 ASCII、注释用 !)，已自动处理", LogSource);
        }
        if (clean.Length == 0)
        {
            _owner.LogWarn($"缓冲区 {bufferIndex} 的程序文本为空，未写入", LogSource);
            return false;
        }

        var lines = clean.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length;
        var ok = _owner.Execute(api => api.LoadBuffer((ProgramBuffer)bufferIndex, clean), $"下载缓冲区 {bufferIndex}(覆盖)", LogSource);
        if (ok) _owner.LogInfo($"已写入程序缓冲区 {bufferIndex}({lines} 行，已清空旧程序)", LogSource);
        return ok;
    }

    /// <summary>追加程序文本到缓冲区(文本会自动清洗为 ASCII)</summary>
    public bool Append(int bufferIndex, string text)
    {
        var clean = AcsProgramText.Sanitize(text, out _);
        if (clean.Length == 0) return false;
        return _owner.Execute(api => api.AppendBuffer((ProgramBuffer)bufferIndex, clean), $"追加缓冲区 {bufferIndex}", LogSource);
    }

    /// <summary>清空缓冲区指定行范围</summary>
    public bool Clear(int bufferIndex, int fromLine = 0, int toLine = 65535)
        => _owner.Execute(api => api.ClearBuffer((ProgramBuffer)bufferIndex, fromLine, toLine), $"清空缓冲区 {bufferIndex}", LogSource);

    /// <summary>
    /// 等待程序结束(轮询方式)。
    /// 判定规则:出现 RUN/DEBUG/SUSPEND 状态后再次回到 COMPILED 视为结束;
    /// 若始终未进入运行状态，则在宽限期(1s)后按"已结束/未启动"处理。
    /// </summary>
    public bool WaitProgramEnd(int bufferIndex, int timeoutMs = 120000, int pollMs = 50, int graceMs = 1000)
    {
        var buffer = (ProgramBuffer)bufferIndex;
        var start = Environment.TickCount64;
        var deadline = start + timeoutMs;
        var sawRunning = false;

        while (Environment.TickCount64 < deadline)
        {
            var state = _owner.Execute(api => api.GetProgramState(buffer), $"读取缓冲区 {bufferIndex} 状态", ProgramStates.ACSC_NONE, LogSource);

            if (state is ProgramStates.ACSC_PST_RUN or ProgramStates.ACSC_PST_DEBUG or ProgramStates.ACSC_PST_SUSPEND)
            {
                sawRunning = true;
            }
            else if (state == ProgramStates.ACSC_PST_COMPILED)
            {
                if (sawRunning) return true;
                if (Environment.TickCount64 - start > graceMs) return true;   // 程序未启动或已结束
            }
            else if (state == ProgramStates.ACSC_NONE)
            {
                // 缓冲区为空/无程序
                if (Environment.TickCount64 - start > graceMs) return true;
            }

            Thread.Sleep(pollMs);
        }

        _owner.LogWarn($"等待缓冲区 {bufferIndex} 程序结束超时({timeoutMs}ms)", LogSource);
        return false;
    }
}
