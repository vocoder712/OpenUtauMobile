using DynamicData.Binding;
using Microsoft.Maui.Handlers;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenUtauMobile.ViewModels
{
    //public class OptionItem
    //{
    //    public string Name { get; set; }
    //    public EventHandler Clicked { get; set; }
    //    public OptionItem(string name, EventHandler handler)
    //    {
    //        Name = name;
    //        Clicked = handler;
    //    }
    //}
    public partial class OptionsViewModel : ReactiveObject
    {
        //[Reactive] public ObservableCollectionExtended<OptionItem> Options { get; set; } = new();
        //public OptionsViewModel()
        //{
        //    Options.Add(new OptionItem("Settings", (s, e) => 
        //    {
        //        MessagingCenter.Send(this, "NavigateToSettings");
        //    }));
        //    Options.Add(new OptionItem("Manage Plugins", (s, e) =>
        //    {
        //        MessagingCenter.Send(this, "NavigateToManagePlugins");
        //    }));
        //    Options.Add(new OptionItem("About", (s, e) =>
        //    {
        //        MessagingCenter.Send(this, "NavigateToAbout");
        //    }));
        //}
    }
}
