using System;
using Microsoft.UI.Xaml.Controls;
using Raven.Windows.Helpers;

namespace Raven.Windows.Views;

/// <summary>Shown when Raven cannot start (for example a release build without raven_client).</summary>
public sealed partial class ErrorPage : UserControl
{
    public ErrorPage(Exception error)
    {
        InitializeComponent();
        Ui.ShowError(ErrorBar, error);
    }
}
