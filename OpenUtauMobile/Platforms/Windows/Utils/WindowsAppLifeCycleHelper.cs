using OpenUtauMobile.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenUtauMobile.Platforms.Windows.Utils
{
    public class WindowsAppLifeCycleHelper : IAppLifeCycleHelper
    {
        public void Restart()
        {
            try
            {
                string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                System.Diagnostics.Process.Start(exePath);
                Application.Current?.Quit();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error($"Windows应用重启失败: {ex}");
            }
        }
    }
}
