using System;
using System.Windows.Markup;

namespace WindowsGSM.Functions.Localization
{
    /// <summary>
    /// XAML markup extension for localization: <c>Content="{loc:Tr MainWindow.Home}"</c>.
    /// Resolves the key against <see cref="Loc"/> at load time (fallback: lang -> en -> key).
    /// It only swaps the TEXT — it never changes layout — so translating cannot move/overlap controls.
    /// Language is chosen at startup; a restart applies a language change everywhere.
    /// </summary>
    [MarkupExtensionReturnType(typeof(string))]
    public class TrExtension : MarkupExtension
    {
        public string Key { get; set; }

        public TrExtension() { }
        public TrExtension(string key) { Key = key; }

        public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key ?? string.Empty);
    }
}
