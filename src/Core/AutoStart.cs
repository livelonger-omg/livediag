using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LiveDiag
{
    /// <summary>开机自启：写当前用户 Run 键，启动参数 --autostart 让程序只驻留托盘。
    /// 不依赖管理员权限，也不会弹 UAC。</summary>
    public static class AutoStart
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "LiveDiag";

        /// <summary>写入注册表的启动命令行（带引号，路径含空格也不会出错）。</summary>
        public static string Command
        {
            get { return "\"" + Application.ExecutablePath + "\" --autostart"; }
        }

        public static string CurrentValue()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (k == null) return "";
                    object v = k.GetValue(ValueName);
                    return v as string ?? "";
                }
            }
            catch { return ""; }
        }

        public static bool IsEnabled()
        {
            return CurrentValue().Length > 0;
        }

        public static bool Apply(bool enable, out string error)
        {
            error = "";
            try
            {
                if (enable)
                {
                    using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                    {
                        if (k == null) { error = "无法写入当前用户的启动项"; return false; }
                        k.SetValue(ValueName, Command, RegistryValueKind.String);
                    }
                }
                else
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                    {
                        if (k != null) k.DeleteValue(ValueName, false);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>启动时按配置自愈：配置要求自启但注册表项缺失，或程序被移动过路径，就重写一次。</summary>
        public static void EnsureFromConfig(Config cfg)
        {
            try
            {
                if (cfg == null) return;
                string err;
                if (cfg.AutoStart)
                {
                    if (CurrentValue() != Command) Apply(true, out err);
                }
                else if (IsEnabled())
                {
                    Apply(false, out err);
                }
            }
            catch { }
        }
    }
}
