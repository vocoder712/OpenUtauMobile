using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenUtauMobile.Utils
{
    public static class FormatTools
    {
        public static string FormatSize(long bytes)
        {
            string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
            double size = bytes;
            int unit = 0;

            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }

            return $"{size:0.##}{units[unit]}";
        }
    }
}
