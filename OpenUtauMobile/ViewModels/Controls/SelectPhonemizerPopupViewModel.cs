using DynamicData.Binding;
using OpenUtau.Api;
using OpenUtau.Core;
using OpenUtauMobile.Resources.Strings;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
namespace OpenUtauMobile.ViewModels.Controls
{
    public partial class SelectPhonemizerPopupViewModel : ReactiveObject
    {
        /// <summary>
        /// 所有分组
        /// </summary>
        [Reactive] public ObservableCollectionExtended<KeyValuePair<IGrouping<string, PhonemizerFactory>, string>> Groups { get; set; } = [];
        /// <summary>
        /// 右侧列表展示的音素器
        /// </summary>
        [Reactive] public ObservableCollectionExtended<KeyValuePair<PhonemizerFactory, string>> PhonemizerFactories { get; set; } = [];
        /// <summary>
        /// 当前选中的分组名称
        /// </summary>
        [Reactive] public KeyValuePair<IGrouping<string, PhonemizerFactory>, string> CurrentGroup { get; set; }

        public SelectPhonemizerPopupViewModel()
        {

        }

        public void Load()
        {
            Groups.Clear();
            PhonemizerFactories.Clear();
            Groups.AddRange(DocManager.Inst.PhonemizerFactories.GroupBy(f => f.language).OrderBy(g => g.Key).Select(g => new KeyValuePair<IGrouping<string, PhonemizerFactory>, string>(g, GetLocalizedGroupName(g.Key))));
            if (Groups.Count > 0)
            {
                CurrentGroup = Groups.FirstOrDefault();
                LoadGroup();
            }
        }

        /// <summary>
        /// 加载右侧列表
        /// </summary>
        public void LoadGroup()
        {
            PhonemizerFactories.Clear();
            PhonemizerFactories.AddRange(CurrentGroup.Key.Select(f => new KeyValuePair<PhonemizerFactory, string>(f, f.ToString())));
        }

        /// <summary>
        /// ex. ja -> 日语
        /// </summary>
        /// <param name="language">双字母语言代码</param>
        /// <returns>已翻译的语言名称</returns>
        private static string GetLocalizedGroupName(string? language)
        {
            if (language == null)
            {
                return "General";
            }
            string l = language.Replace("-", "_").Replace(" ", "_").Replace(".", "_").ToLower();
            string propertyName = $"Languages_{l}";
            string displayName = AppResources.ResourceManager.GetString(propertyName) ?? language;
            return displayName;
        }

    }
}
