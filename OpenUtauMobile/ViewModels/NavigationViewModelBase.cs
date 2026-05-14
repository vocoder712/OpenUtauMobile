namespace OpenUtauMobile.ViewModels;

/// <summary>
/// 所有导航视图模型的基类，提供导航相关的功能和接口。
/// </summary>
public abstract class NavigateViewModelBase : ViewModelBase
{
    /// <summary>
    /// 导航器实例，子类可以通过该属性进行导航操作。
    /// </summary>
    protected MainViewModel Navigator { get; }

    protected NavigateViewModelBase(MainViewModel navigator)
    {
        Navigator = navigator;
    }

    /// <summary>
    /// 当导航到该视图模型时触发该方法，子类可以重写该方法。
    /// </summary>
    public virtual void OnNavigatedTo()
    {
    }

    /// <summary>
    /// 当请求返回上一页时触发该方法，子类可以重写该方法以实现自定义的返回逻辑。
    /// </summary>
    public virtual void OnBackRequested()
    {
        Navigator.NavigateBack(this);
    }
}