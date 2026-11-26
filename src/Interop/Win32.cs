using System;
using System.Runtime.InteropServices;

namespace LiveDiag.Interop
{
    /// <summary>所有 Win32 / IP Helper / WLAN / PDH 的 P/Invoke 声明集中在此。</summary>
    internal static class Native
    {
        public const int AF_UNSPEC = 0;
        public const int AF_INET = 2;
        public const int AF_INET6 = 23;
        public const int ERROR_SUCCESS = 0;
        public const int ERROR_BUFFER_OVERFLOW = 111;
        public const int ERROR_INSUFFICIENT_BUFFER = 122;
        public const int ERROR_ACCESS_DENIED = 5;

        // ================= 网卡信息 =================
        public const uint GAA_FLAG_SKIP_ANYCAST = 0x2;
        public const uint GAA_FLAG_SKIP_MULTICAST = 0x4;
        public const uint GAA_FLAG_INCLUDE_GATEWAYS = 0x80;

        [DllImport("iphlpapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint GetAdaptersAddresses(int family, uint flags, IntPtr reserved, IntPtr addresses, ref uint size);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        public static extern uint GetBestInterface(uint destAddr, out uint bestIfIndex);

        [StructLayout(LayoutKind.Sequential)]
        public struct IP_ADAPTER_ADDRESSES
        {
            public long AlignLength;            // union { ULONGLONG Alignment; struct { ULONG Length; ULONG IfIndex; } }
            public IntPtr Next;
            public IntPtr AdapterName;
            public IntPtr FirstUnicastAddress;
            public IntPtr FirstAnycastAddress;
            public IntPtr FirstMulticastAddress;
            public IntPtr FirstDnsServerAddress;
            public IntPtr DnsSuffix;
            public IntPtr Description;
            public IntPtr FriendlyName;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] PhysicalAddress;
            public uint PhysicalAddressLength;
            public uint Flags;
            public uint Mtu;
            public uint IfType;
            public uint OperStatus;
            public uint Ipv6IfIndex;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public uint[] ZoneIndices;
            public IntPtr FirstPrefix;
            public ulong TransmitLinkSpeed;
            public ulong ReceiveLinkSpeed;
            public IntPtr FirstWinsServerAddress;
            public IntPtr FirstGatewayAddress;
        }

        /// <summary>单播 / 网关 / DNS 三种链表的公共头（我们只需要 Address）。</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct IP_ADAPTER_SOCKADDR_NODE
        {
            public long AlignLength;
            public IntPtr Next;
            public IntPtr lpSockaddr;
            public int iSockaddrLength;
        }

        // ================= 接口流量计数器 =================
        [DllImport("iphlpapi.dll", SetLastError = true)]
        public static extern uint GetIfTable(IntPtr pIfTable, ref uint pdwSize, bool bOrder);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MIB_IFROW
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string wszName;
            public uint dwIndex;
            public uint dwType;
            public uint dwMtu;
            public uint dwSpeed;
            public uint dwPhysAddrLen;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] bPhysAddr;
            public uint dwAdminStatus;
            public uint dwOperStatus;
            public uint dwLastChange;
            public uint dwInOctets;
            public uint dwInUcastPkts;
            public uint dwInNUcastPkts;
            public uint dwInDiscards;
            public uint dwInErrors;
            public uint dwInUnknownProtos;
            public uint dwOutOctets;
            public uint dwOutUcastPkts;
            public uint dwOutNUcastPkts;
            public uint dwOutDiscards;
            public uint dwOutErrors;
            public uint dwOutQLen;
            public uint dwDescrLen;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public byte[] bDescr;
        }

        // ================= ICMP =================
        [StructLayout(LayoutKind.Sequential)]
        public struct IP_OPTION_INFORMATION
        {
            public byte Ttl;
            public byte Tos;
            public byte Flags;
            public uint OptionsSize;
            public IntPtr OptionsData;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ICMP_ECHO_REPLY
        {
            public uint Address;
            public uint Status;
            public uint RoundTripTime;
            public ushort DataSize;
            public ushort Reserved;
            public IntPtr Data;
            public IP_OPTION_INFORMATION Options;
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        public static extern IntPtr IcmpCreateFile();

        [DllImport("iphlpapi.dll", SetLastError = true)]
        public static extern bool IcmpCloseHandle(IntPtr handle);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        public static extern uint IcmpSendEcho(IntPtr handle, uint destAddress, byte[] requestData, ushort requestSize,
            IntPtr requestOptions, byte[] replyBuffer, uint replySize, uint timeout);

        // ================= TCP / UDP 表 =================
        public const int TCP_TABLE_OWNER_PID_ALL = 5;
        public const int UDP_TABLE_OWNER_PID = 1;

        [DllImport("iphlpapi.dll", SetLastError = true)]
        public static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int pdwSize, bool bOrder,
            int ulAf, int tableClass, uint reserved);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        public static extern uint GetExtendedUdpTable(IntPtr pUdpTable, ref int pdwSize, bool bOrder,
            int ulAf, int tableClass, uint reserved);

        [StructLayout(LayoutKind.Sequential)]
        public struct MIB_TCPROW_OWNER_PID
        {
            public uint state;
            public uint localAddr;
            public uint localPort;
            public uint remoteAddr;
            public uint remotePort;
            public uint owningPid;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MIB_TCPROW
        {
            public uint dwState;
            public uint dwLocalAddr;
            public uint dwLocalPort;
            public uint dwRemoteAddr;
            public uint dwRemotePort;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MIB_UDPROW_OWNER_PID
        {
            public uint localAddr;
            public uint localPort;
            public uint owningPid;
        }

        // IPv6 版本（推流走 IPv6 时只查 IPv4 表会完全看不到连接）
        [StructLayout(LayoutKind.Sequential)]
        public struct MIB_TCP6ROW_OWNER_PID
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] localAddr;
            public uint localScopeId;
            public uint localPort;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] remoteAddr;
            public uint remoteScopeId;
            public uint remotePort;
            public uint state;
            public uint owningPid;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MIB_UDP6ROW_OWNER_PID
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] localAddr;
            public uint localScopeId;
            public uint localPort;
            public uint owningPid;
        }

        // ================= 进程快照（取进程名不依赖打开目标进程） =================
        public const uint TH32CS_SNAPPROCESS = 0x00000002;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PROCESSENTRY32W
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "Process32FirstW")]
        public static extern bool Process32First(IntPtr snapshot, ref PROCESSENTRY32W entry);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "Process32NextW")]
        public static extern bool Process32Next(IntPtr snapshot, ref PROCESSENTRY32W entry);

        // TCP 状态
        public const int MIB_TCP_STATE_LISTEN = 2;
        public const int MIB_TCP_STATE_SYN_SENT = 3;
        public const int MIB_TCP_STATE_ESTAB = 5;
        public const int MIB_TCP_STATE_TIME_WAIT = 11;

        // ================= TCP ESTATS（逐连接字节数） =================
        public const int TcpConnectionEstatsSynOpts = 0;
        public const int TcpConnectionEstatsData = 1;
        public const int TcpConnectionEstatsSndCong = 2;
        public const int TcpConnectionEstatsPath = 3;

        [StructLayout(LayoutKind.Sequential)]
        public struct TCP_ESTATS_DATA_RW_v0
        {
            [MarshalAs(UnmanagedType.U1)]
            public bool EnableCollection;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TCP_ESTATS_DATA_ROD_v0
        {
            public ulong DataBytesOut;
            public ulong DataSegsOut;
            public ulong DataBytesIn;
            public ulong DataSegsIn;
            public ulong SegsOut;
            public ulong SegsIn;
            public uint SoftErrors;
            public uint SoftErrorReason;
            public uint SndUna;
            public uint SndNxt;
            public uint SndMax;
            public ulong ThruBytesAcked;
            public uint RcvNxt;
            public ulong ThruBytesReceived;
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        public static extern uint SetPerTcpConnectionEStats(ref MIB_TCPROW row, int estatsType,
            ref TCP_ESTATS_DATA_RW_v0 rw, uint rwVersion, uint rwSize, uint offset);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        public static extern uint GetPerTcpConnectionEStats(ref MIB_TCPROW row, int estatsType,
            IntPtr rw, uint rwVersion, uint rwSize, uint rwOffset,
            IntPtr ros, uint rosVersion, uint rosSize, uint rosOffset,
            ref TCP_ESTATS_DATA_ROD_v0 rod, uint rodVersion, uint rodSize, uint rodOffset);

        [DllImport("iphlpapi.dll", SetLastError = true, EntryPoint = "GetPerTcpConnectionEStats")]
        public static extern uint GetPerTcpConnectionEStatsRw(ref MIB_TCPROW row, int estatsType,
            ref TCP_ESTATS_DATA_RW_v0 rw, uint rwVersion, uint rwSize, uint rwOffset,
            IntPtr ros, uint rosVersion, uint rosSize, uint rosOffset,
            ref TCP_ESTATS_DATA_ROD_v0 rod, uint rodVersion, uint rodSize, uint rodOffset);

        [DllImport("iphlpapi.dll", SetLastError = true, EntryPoint = "GetPerTcpConnectionEStats")]
        public static extern uint GetPerTcpConnectionEStatsRaw(ref MIB_TCPROW row, int estatsType,
            IntPtr rw, uint rwVersion, uint rwSize, uint rwOffset,
            IntPtr ros, uint rosVersion, uint rosSize, uint rosOffset,
            IntPtr rod, uint rodVersion, uint rodSize, uint rodOffset);

        // ================= 系统性能 =================
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetSystemTimes(out ulong idleTime, out ulong kernelTime, out ulong userTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetProcessTimes(IntPtr process, out ulong creation, out ulong exit,
            out ulong kernel, out ulong user);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr handle);

        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);

        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetSystemPowerStatus(ref SYSTEM_POWER_STATUS status);

        [DllImport("psapi.dll", SetLastError = true)]
        public static extern bool EnumProcesses(int[] pProcessIds, int cb, out int pBytesReturned);

        [StructLayout(LayoutKind.Sequential)]
        public struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetProcessIoCounters(IntPtr process, out IO_COUNTERS counters);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool QueryFullProcessImageName(IntPtr process, uint flags, System.Text.StringBuilder name, ref uint size);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateMutex(IntPtr attr, bool initialOwner, string name);

        public const int ERROR_ALREADY_EXISTS = 183;

        // ================= WLAN =================
        [DllImport("wlanapi.dll", SetLastError = true)]
        public static extern uint WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr clientHandle);

        [DllImport("wlanapi.dll", SetLastError = true)]
        public static extern uint WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);

        [DllImport("wlanapi.dll", SetLastError = true)]
        public static extern uint WlanEnumInterfaces(IntPtr clientHandle, IntPtr reserved, out IntPtr interfaceList);

        [DllImport("wlanapi.dll", SetLastError = true)]
        public static extern uint WlanQueryInterface(IntPtr clientHandle, ref Guid interfaceGuid, int opcode,
            IntPtr reserved, out uint dataSize, out IntPtr data, out int opcodeValueType);

        [DllImport("wlanapi.dll", SetLastError = true)]
        public static extern void WlanFreeMemory(IntPtr memory);

        public const int WLAN_INTF_OPCODE_CURRENT_CONNECTION = 7;
        public const int WLAN_INTF_OPCODE_RSSI = 0x10000102;

        [StructLayout(LayoutKind.Sequential)]
        public struct DOT11_SSID
        {
            public uint uSSIDLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] ucSSID;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WLAN_ASSOCIATION_ATTRIBUTES
        {
            public DOT11_SSID dot11Ssid;
            public uint dot11BssType;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public byte[] dot11Bssid;
            public uint dot11PhyType;
            public uint uDot11PhyIndex;
            public uint wlanSignalQuality;   // 0-100
            public uint ulRxRate;            // kbps
            public uint ulTxRate;            // kbps
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WLAN_SECURITY_ATTRIBUTES
        {
            [MarshalAs(UnmanagedType.Bool)] public bool bSecurityEnabled;
            [MarshalAs(UnmanagedType.Bool)] public bool bOneXEnabled;
            public uint dot11AuthAlgorithm;
            public uint dot11CipherAlgorithm;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WLAN_CONNECTION_ATTRIBUTES
        {
            public uint isState;
            public uint wlanConnectionMode;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strProfileName;
            public WLAN_ASSOCIATION_ATTRIBUTES wlanAssociationAttributes;
            public WLAN_SECURITY_ATTRIBUTES wlanSecurityAttributes;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WLAN_INTERFACE_INFO
        {
            public Guid InterfaceGuid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strInterfaceDescription;
            public uint isState;
        }

        // ================= PDH（磁盘队列 / GPU） =================
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        public static extern uint PdhOpenQuery(string dataSource, IntPtr userData, out IntPtr query);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        public static extern uint PdhAddCounter(IntPtr query, string fullCounterPath, IntPtr userData, out IntPtr counter);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        public static extern uint PdhAddEnglishCounter(IntPtr query, string fullCounterPath, IntPtr userData, out IntPtr counter);

        [DllImport("pdh.dll")]
        public static extern uint PdhCollectQueryData(IntPtr query);

        [DllImport("pdh.dll")]
        public static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out double value);

        [DllImport("pdh.dll")]
        public static extern uint PdhCloseQuery(IntPtr query);

        [DllImport("pdh.dll")]
        public static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format,
            ref uint bufferSize, ref uint itemCount, IntPtr itemBuffer);

        public const uint PDH_FMT_DOUBLE = 0x00000200;
        public const uint PDH_FMT_NOCAP100 = 0x00008000;

        // ================= 通用 =================
        public static int GetPort(ushort netPort)
        {
            return (netPort & 0xFF) << 8 | (netPort >> 8) & 0xFF;
        }

        public static int GetPort(uint netPort)
        {
            return GetPort((ushort)(netPort & 0xFFFF));
        }

        public static uint ToNetUint(byte[] ip)
        {
            return (uint)(ip[3] << 24 | ip[2] << 16 | ip[1] << 8 | ip[0]);
        }

        public static string UintToIp(uint addr)
        {
            return ((addr) & 0xFF) + "." + ((addr >> 8) & 0xFF) + "." + ((addr >> 16) & 0xFF) + "." + ((addr >> 24) & 0xFF);
        }

        public static string SockaddrToString(IntPtr pSockaddr, int len)
        {
            if (pSockaddr == IntPtr.Zero || len < 8) return null;
            int family = Marshal.ReadInt16(pSockaddr);
            if (family == AF_INET)
            {
                byte[] b = new byte[4];
                Marshal.Copy(new IntPtr(pSockaddr.ToInt64() + 4), b, 0, 4);
                return b[0] + "." + b[1] + "." + b[2] + "." + b[3];
            }
            if (family == AF_INET6 && len >= 24)
            {
                byte[] b = new byte[16];
                Marshal.Copy(new IntPtr(pSockaddr.ToInt64() + 8), b, 0, 16);
                return new System.Net.IPAddress(b).ToString();
            }
            return null;
        }
    }
}
