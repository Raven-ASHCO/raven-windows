using Microsoft.UI.Xaml.Markup;
using Raven.Windows.Core.Text;

namespace Raven.Windows.Helpers;

/// <summary>
/// XAML access to strings.en.json: <c>Text="{h:Str Key=welcome.title}"</c>.
/// Every key used here must exist in the string table (StringKeysTests scans the XAML files).
/// </summary>
[MarkupExtensionReturnType(ReturnType = typeof(string))]
public sealed class Str : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    protected override object ProvideValue() => Loc.Get(Key);
}
