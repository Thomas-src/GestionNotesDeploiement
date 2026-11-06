using GestionNotes.Models;
using GestionNotes.Services;
using GestionNotes.ViewModels;
using GestionNotes.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Windows;

namespace GestionNotes
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private readonly IHost _host;

        public App()
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration((_, config) =>
                {
                    config.SetBasePath(AppContext.BaseDirectory);
                    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                })
                .ConfigureServices((context, services) =>
                {
                    services.Configure<AppSettings>(
                        context.Configuration.GetSection("AppSettings"));

                    services.AddSingleton<ServiceUpdate>();
                    services.AddSingleton<MainWindow>();
                })
                .Build();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            await _host.StartAsync();
            bool exitProgram = false;
            try
            {
                exitProgram = HandleUpdate();
            }
            catch (Exception ex)
            {
                DisplayMessageBox("Une erreur inattendue est survenue lors de la vérification des mises à jour.", "Erreur lors de la récupération des mises à jour", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            if (exitProgram)
            {
                Shutdown();
                return;
            }

            _host.Services.GetRequiredService<MainWindow>().Show();



        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { _host.StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult(); }
            catch { }
            _host.Dispose();
            base.OnExit(e);
        }
        /// <summary>
        /// S'occupe de la gestion de mise à jour
        /// </summary>
        /// <returns>true si un redémarrage de l'application est nécessaire, false si ce n'est pas le cas</returns>

        private bool HandleUpdate()
        {
            var settings = _host.Services.GetRequiredService<IOptions<AppSettings>>().Value;
            var serviceUpdate = _host.Services.GetRequiredService<ServiceUpdate>();
            var serverFilePath = settings.UpdateFile;

            var versionStatus = serviceUpdate.CheckVersion();

            switch (versionStatus.Status)
            {
                case UpdateStatus.ERROR:
                    DisplayMessageBox(versionStatus.Message, "Erreur lors de la récupération des mises à jour", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                    break;

                case UpdateStatus.UPDATE_AVAILABLE:
                    var response = DisplayMessageBox(versionStatus.Message, "Mise à jour disponible", MessageBoxButton.YesNo, MessageBoxImage.Question);


                    if (response != MessageBoxResult.Yes)
                    {
                        return false;
                    }

                    var folderStatus = serviceUpdate.CheckUpdateFolder();

                    if (folderStatus.Status != UpdateStatus.OK)
                    {
                        DisplayMessageBox(folderStatus.Message, "Erreur de téléchargement", MessageBoxButton.OK, MessageBoxImage.Error);
                        return false;
                    }

                    var launchStatus = serviceUpdate.LaunchUpdater();

                    if (launchStatus.Status != UpdateStatus.OK)
                    {
                        DisplayMessageBox(launchStatus.Message, "Erreur de mise à jour",  MessageBoxButton.OK, MessageBoxImage.Error);
                        return false;
                    }

                    DisplayMessageBox("La mise à jour va être appliquée. L'application va se fermer puis redémarrer.", "Mise à jour", MessageBoxButton.OK, MessageBoxImage.Information);
                    return true;
                default:
                    // pas de maj
                    return false;
                    break;
            }
            return false;
        }

        private static MessageBoxResult DisplayMessageBox(string message, string title, MessageBoxButton button, MessageBoxImage image) => MessageBox.Show(message, title, button, image);
    }
}
