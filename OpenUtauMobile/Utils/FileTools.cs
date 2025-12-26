using Serilog;

namespace OpenUtauMobile.Utils
{
    public static class FileTools
    {
        /// <summary>
        /// 字节转换为可读大小格式
        /// </summary>
        /// <param name="bytes">64位signed</param>
        /// <returns></returns>
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
        /// <summary>
        /// 计算目录大小
        /// </summary>
        /// <param name="directory"></param>
        /// <returns>long</returns>
        public static long GetDirectorySize(DirectoryInfo directory)
        {
            try
            {
                return directory.EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"无法计算 {directory.FullName} 的大小");
                return 0;
            }
        }
    }
}
