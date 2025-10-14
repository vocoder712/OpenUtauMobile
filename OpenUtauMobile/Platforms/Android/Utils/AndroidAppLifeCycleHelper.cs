using Android.App;
using Android.OS;
using Android.Content;
using Android.Widget;
using OpenUtauMobile.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenUtauMobile.Platforms.Android.Utils
{
    public class AndroidAppLifeCycleHelper : IAppLifeCycleHelper
    {
        public void Restart()
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.M)
            {
                // 版本过低，无法使用 AlarmManager 重启应用，直接退出
                Toast.MakeText(Platform.AppContext, "Android版本过低，无法自动重启应用，请手动开启。", ToastLength.Long)?.Show();
                Java.Lang.JavaSystem.Exit(0);
                return;
            }
            Context context = Platform.CurrentActivity ?? Platform.AppContext;

            Intent intent = new(context, typeof(MainActivity));
            intent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.NewTask);

            // 用 PendingIntent 让系统重新启动应用
            PendingIntent? pendingIntent = PendingIntent.GetActivity(
                context, 0, intent, PendingIntentFlags.Immutable);
            if (pendingIntent == null)
            {
                // 无法获取 PendingIntent，直接退出
                Toast.MakeText(context, "无法自动重启应用，请手动开启。", ToastLength.Long)?.Show();
                Java.Lang.JavaSystem.Exit(0);
                return;
            }

            AlarmManager? alarmManager = (AlarmManager?)context.GetSystemService(Context.AlarmService);
            alarmManager?.Set(
                AlarmType.Rtc,
                Java.Lang.JavaSystem.CurrentTimeMillis() + 100,
                pendingIntent);
            Toast.MakeText(context, "应用将在几秒后重启", ToastLength.Long)?.Show();
            // 结束当前进程
            Java.Lang.JavaSystem.Exit(0);
        }
    }
}
