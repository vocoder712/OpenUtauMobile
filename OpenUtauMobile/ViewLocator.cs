using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using OpenUtauMobile.ViewModels;
using Serilog;

namespace OpenUtauMobile;

public class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        if (data is null)
        {
            return null;
        }

        string name = data.GetType().FullName!.Replace("ViewModel", "View");
        Type? type = Type.GetType(name);
        if (type != null)
        {
            try
            {
                return (Control)Activator.CreateInstance(type)!;
            }
            catch (Exception exception)
            {
                Log.Error(exception, "创建视图 {ViewType} 失败", name);
                return new TextBlock
                {
                    Text = $"Failed to create view: {name}\n{exception.GetBaseException().Message}"
                };
            }
        }

        return new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
