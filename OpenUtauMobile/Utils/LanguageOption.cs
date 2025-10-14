using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenUtauMobile.Utils
{
    public class LanguageOption(string displayName, string cultureName)
    {
        public string DisplayName { get; set; } = displayName;  // 显示在UI，比如 "简体中文"
        public string CultureName { get; set; } = cultureName; // 实际文化名，比如 "zh-CN"
    }
}
