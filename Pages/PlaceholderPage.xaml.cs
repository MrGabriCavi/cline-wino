using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace ClineWin
{
    /// <summary>Pagina segnaposto per le sezioni non ancora implementate; il titolo arriva come parametro.</summary>
    public sealed partial class PlaceholderPage : Page
    {
        public PlaceholderPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            TitleText.Text = Routes.Find(e.Parameter as string).Title;
        }
    }
}
