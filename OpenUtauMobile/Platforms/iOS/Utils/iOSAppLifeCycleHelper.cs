using CommunityToolkit.Maui.Alerts;
using OpenUtauMobile.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenUtauMobile.Platforms.iOS.Utils
{
    public class iOSAppLifeCycleHelper : IAppLifeCycleHelper
    {
        public void Restart()
        {
            Toast.Make("iOS平台不支持应用内重启，请手动打开应用。", CommunityToolkit.Maui.Core.ToastDuration.Long).Show();
            Application.Current?.Quit();
        }
    }
}
