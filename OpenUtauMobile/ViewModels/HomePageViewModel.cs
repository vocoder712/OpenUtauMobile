using DynamicData.Binding;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;

namespace OpenUtauMobile.ViewModels
{
    public class HomePageViewModel : ReactiveObject
    {
        //public ReactiveCommand<Unit, Unit> NewProjectCommand { get; }
        //public ReactiveCommand<Unit, Unit> OpenProjectCommand { get; }
        //public ReactiveCommand<Unit, Unit> OpenSingerManagePageCommand { get; }
        //public ReactiveCommand<Unit, Unit> OpenOptionsPageCommand { get; }
        [Reactive] public ObservableCollectionExtended<string> RecentProjectsPaths { get; set; } = [];

        public HomePageViewModel()
        {
            //NewProjectCommand = ReactiveCommand.Create(NewProject);
            //OpenProjectCommand = ReactiveCommand.Create(OpenProject);
            //OpenSingerManagePageCommand = ReactiveCommand.Create(OpenSingerManagePage);
            //OpenOptionsPageCommand = ReactiveCommand.Create(OpenOptionsPage);
        }

        //private void NewProject()
        //{

        //}

        //private void OpenProject()
        //{
        //}

        //private void OpenSingerManagePage()
        //{
            
        //}

        //private void OpenOptionsPage()
        //{
        //}

    }
}
