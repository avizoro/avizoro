using Avizoro.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Sample.Maui
{
    public partial class SecondPage : ContentPage
    {
        public SecondPage()
        {
            InitializeComponent();
        }

        private static IServiceProvider Services =>
            IPlatformApplication.Current!.Services;

        private async void OnSecondPageExceptionClicked(object? sender, EventArgs e)
        {
            var kit = Services.GetRequiredService<IAvizoro>();

            try
            {
                throw new InvalidOperationException("Excepción controlada desde la segunda pantalla");
            }
            catch (Exception ex)
            {
                await kit.TrackExceptionAsync(ex);
            }

            StatusLabel.Text = "Excepción reportada. El screenshot de esta pantalla debería adjuntarse.";
        }
    }
}
