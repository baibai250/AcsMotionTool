namespace AcsMotionTool.Core;

/// <summary>
/// ACS 连接方式
/// </summary>
public enum AcsConnectionMode
{
    /// <summary>以太网连接真实控制器</summary>
    Ethernet = 0,

    /// <summary>连接 SPiiPlus 模拟器(无需硬件)</summary>
    Simulator = 1,
}

/// <summary>
/// 以太网通讯协议(对应 ACS.SPiiPlusNET.EthernetCommOption)
/// </summary>
public enum AcsEthernetProtocol
{
    /// <summary>点对点 UDP(DGRAM)，端口 700，最常用</summary>
    UdpPointToPoint = 700,

    /// <summary>TCP 流(STREAM)，端口 701</summary>
    TcpStream = 701,
}

/// <summary>
/// 连接参数
/// </summary>
public sealed class AcsConnectionOptions
{
    /// <summary>连接方式</summary>
    public AcsConnectionMode Mode { get; set; } = AcsConnectionMode.Ethernet;

    /// <summary>控制器 IP(以太网方式有效)</summary>
    public string IpAddress { get; set; } = "192.168.0.10";

    /// <summary>以太网协议</summary>
    public AcsEthernetProtocol Protocol { get; set; } = AcsEthernetProtocol.UdpPointToPoint;

    /// <summary>通讯超时(ms)</summary>
    public int TimeoutMs { get; set; } = 5000;

    /// <summary>连接成功后是否重启控制器(修改了控制器配置后才需要)</summary>
    public bool RebootAfterConnect { get; set; }

    /// <summary>重启超时(ms)</summary>
    public int RebootTimeoutMs { get; set; } = 120000;

    public AcsConnectionOptions Clone() => new()
    {
        Mode = Mode,
        IpAddress = IpAddress,
        Protocol = Protocol,
        TimeoutMs = TimeoutMs,
        RebootAfterConnect = RebootAfterConnect,
        RebootTimeoutMs = RebootTimeoutMs,
    };

    public string Describe() => Mode == AcsConnectionMode.Simulator
        ? "SPiiPlus 模拟器"
        : $"{IpAddress} ({(int)Protocol}/{(Protocol == AcsEthernetProtocol.UdpPointToPoint ? "UDP" : "TCP")})";
}
