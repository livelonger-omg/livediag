using System;
using System.Runtime.InteropServices;
using System.Text;
using LiveDiag.Interop;

namespace LiveDiag
{
    public class WlanInfo
    {
        public bool Valid;
        public string Ssid = "";
        public int Quality = -1;
        public int Rssi = -1000;
        public int TxKbps = -1;
        public int RxKbps = -1;
        public string Phy = "";
        public string Error = "";
    }

    /// <summary>通过 WLAN API 读取信号强度与协商速率，不启动 netsh 子进程。</summary>
    public class WlanCollector
    {
        private IntPtr _handle = IntPtr.Zero;
        private bool _opened;

        public WlanInfo Read()
        {
            WlanInfo info = new WlanInfo();
            if (!_opened)
            {
                _opened = true;
                uint ver;
                uint r = Native.WlanOpenHandle(2, IntPtr.Zero, out ver, out _handle);
                if (r != 0) { info.Error = "WlanOpenHandle=" + r; return info; }
            }
            if (_handle == IntPtr.Zero) { info.Error = "WLAN 服务不可用"; return info; }

            IntPtr list = IntPtr.Zero;
            try
            {
                if (Native.WlanEnumInterfaces(_handle, IntPtr.Zero, out list) != 0) { info.Error = "WlanEnumInterfaces 失败"; return info; }
                int count = Marshal.ReadInt32(list);
                int stride = Marshal.SizeOf(typeof(Native.WLAN_INTERFACE_INFO));
                StringBuilder diag = new StringBuilder();
                for (int i = 0; i < count; i++)
                {
                    // WLAN_INTERFACE_INFO_LIST: DWORD dwNumberOfItems; DWORD dwIndex; WLAN_INTERFACE_INFO[]
                    IntPtr p = new IntPtr(list.ToInt64() + 8 + (long)i * stride);
                    Native.WLAN_INTERFACE_INFO iface =
                        (Native.WLAN_INTERFACE_INFO)Marshal.PtrToStructure(p, typeof(Native.WLAN_INTERFACE_INFO));
                    diag.Append("[if" + i + " state=" + iface.isState + "]");

                    Guid g = iface.InterfaceGuid;
                    uint size;
                    IntPtr data;
                    int vt;
                    uint qr = Native.WlanQueryInterface(_handle, ref g, Native.WLAN_INTF_OPCODE_CURRENT_CONNECTION,
                        IntPtr.Zero, out size, out data, out vt);
                    if (qr != 0) { diag.Append("[query=" + qr + "]"); continue; }
                    try
                    {
                        Native.WLAN_CONNECTION_ATTRIBUTES ca =
                            (Native.WLAN_CONNECTION_ATTRIBUTES)Marshal.PtrToStructure(data, typeof(Native.WLAN_CONNECTION_ATTRIBUTES));
                        Native.WLAN_ASSOCIATION_ATTRIBUTES aa = ca.wlanAssociationAttributes;
                        info.Valid = true;
                        info.Quality = (int)aa.wlanSignalQuality;
                        info.Rssi = info.Quality / 2 - 100;

                        // 优先取真实 RSSI（dBm），比信号百分比换算更准
                        try
                        {
                            uint rsize;
                            IntPtr rdata;
                            int rvt;
                            if (Native.WlanQueryInterface(_handle, ref g, Native.WLAN_INTF_OPCODE_RSSI,
                                IntPtr.Zero, out rsize, out rdata, out rvt) == 0 && rdata != IntPtr.Zero)
                            {
                                int rssi = Marshal.ReadInt32(rdata);
                                Native.WlanFreeMemory(rdata);
                                if (rssi < 0 && rssi > -120) info.Rssi = rssi;
                            }
                        }
                        catch { }
                        info.TxKbps = (int)(aa.ulTxRate > int.MaxValue ? 0 : aa.ulTxRate);
                        info.RxKbps = (int)(aa.ulRxRate > int.MaxValue ? 0 : aa.ulRxRate);
                        info.Phy = PhyName(aa.dot11PhyType);
                        info.Ssid = Ssid(aa.dot11Ssid);
                        return info;
                    }
                    finally
                    {
                        Native.WlanFreeMemory(data);
                    }
                }
                info.Error = "没有已连接的无线网络 " + diag.ToString();
            }
            catch (Exception ex)
            {
                info.Error = ex.Message;
            }
            finally
            {
                if (list != IntPtr.Zero) Native.WlanFreeMemory(list);
            }
            return info;
        }

        private static string Ssid(Native.DOT11_SSID ssid)
        {
            try
            {
                int len = (int)ssid.uSSIDLength;
                if (len <= 0 || len > 32 || ssid.ucSSID == null) return "";
                return Encoding.UTF8.GetString(ssid.ucSSID, 0, len);
            }
            catch { return ""; }
        }

        private static string PhyName(uint phy)
        {
            switch (phy)
            {
                case 1: return "FHSS";
                case 2: return "DSSS";
                case 3: return "IR";
                case 4: return "802.11a";
                case 5: return "802.11b";
                case 6: return "802.11g";
                case 7: return "802.11n";
                case 8: return "802.11ac";
                case 9: return "802.11ad";
                case 10: return "802.11ax";
                default: return "PHY" + phy;
            }
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                Native.WlanCloseHandle(_handle, IntPtr.Zero);
                _handle = IntPtr.Zero;
            }
        }
    }
}
