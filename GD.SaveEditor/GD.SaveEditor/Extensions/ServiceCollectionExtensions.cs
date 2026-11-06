using GD.SaveEditor.Presentation.Services;
using GD.SaveEditor.Presentation.Services.Toasts;
using GD.SaveEditor.Presentation.ViewModels.Characters;
using GD.SaveEditor.Presentation.ViewModels.Database;
using GD.SaveEditor.Presentation.ViewModels.Notifications;
using GD.SaveEditor.Presentation.ViewModels.Settings;
using GD.SaveEditor.Presentation.ViewModels.Shell;
using GD.SaveEditor.Presentation.Views;
using GD.SaveEditor.Presentation.Views.Database;
using GD.SaveEditor.Presentation.Views.Settings;
using GD.SaveEditor.Services.GameData;
using GD.SaveEditor.Services.GameData.Database;
using GD.SaveEditor.Services.GameData.Items;
using GD.SaveEditor.Services.GameData.Textures;
using GD.SaveEditor.Services.GameData.Waypoints;
using GD.SaveEditor.Services.Notifications;
using GD.SaveEditor.Services.Saves;
using GD.SaveEditor.Services.Settings;
using GD.SaveEditor.Services.Theming;
using GD.SaveEditor.Services.Updates;

namespace GD.SaveEditor.Extensions;

static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddApplicationServices() => services
            .AddInfrastructure()
            .AddPresentation();

        private IServiceCollection AddInfrastructure()
        {
            services.AddSingleton<ISettingsService, JsonSettingsService>();
            services.AddSingleton<IThemeService, ThemeService>();
            services.AddSingleton<INotificationService, JsonNotificationService>();
            services.AddSingleton<IUpdateService, GitHubUpdateService>();

            // Save files (player.gdc)
            services.AddSingleton<ISaveFolderScanner, SaveFolderScanner>();
            services.AddSingleton<ICharacterSaveParser, CharacterSaveParser>();
            services.AddSingleton<ISaveFolderLoader, SaveFolderLoader>();
            services.AddSingleton<ICharacterSaveWriter, CharacterSaveWriter>();
            services.AddSingleton<ICharacterSaveEditor, CharacterSaveEditor>();
            // Shared stash (transfer.gst / transfer.gsh)
            services.AddSingleton<ITransferStashService, TransferStashService>();

            // Game data (%APPDATA%\GD.SaveEditor\database.db) built from the game's .arz / .arc files
            services.AddSingleton<IGameFileProvider, GameFileProvider>();
            services.AddSingleton<IGameDatabaseMigrator, GameDatabaseMigrator>();
            services.AddSingleton<IDatabaseGenerator, DatabaseGenerator>();
            services.AddSingleton<IGameTextProvider, GameTextProvider>();
            services.AddSingleton<IGameItemProvider, GameItemProvider>();
            services.AddSingleton<IGameIconProvider, GameIconProvider>();
            services.AddSingleton<IGameWaypointProvider, GameWaypointProvider>();
            services.AddSingleton<IItemCatalog, ItemCatalog>();

            return services;
        }

        private IServiceCollection AddPresentation()
        {
            // Services
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<IFolderPickerService, FolderPickerService>();
            services.AddSingleton<IToastService, ToastService>();
            services.AddSingleton<IAutoUpdater, AutoUpdater>();

            // Notification center (bell in the title bar)
            services.AddSingleton<NotificationCenterViewModel>();

            // Character details (Character / Stash / Waypoints / Factions tabs)
            services.AddSingleton<CharacterViewModelFactory>();

            // Shell
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindow>();

            // Settings (new instance every time the dialog opens)
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<SettingsWindow>();

            // Database generator (new instance every time the dialog opens)
            services.AddTransient<DatabaseGeneratorViewModel>();
            services.AddTransient<DatabaseGeneratorWindow>();

            return services;
        }
    }
}
