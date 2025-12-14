using SqlConstraintHelper.Core.Models;
using System.Windows;

namespace SqlConstraintHelper.Desktop.Services
{
    public interface IThemeManager
    {
        Theme CurrentTheme { get; }
        void ApplyTheme(Theme theme);
        void ToggleTheme();
    }

    public class ThemeManager : IThemeManager
    {
        private Theme _currentTheme = Theme.Light;
        private const string ThemeResourceKey = "CurrentThemeResources";

        public Theme CurrentTheme => _currentTheme;

        public ThemeManager()
        {
            // Initialize with light theme
            ApplyTheme(Theme.Light);
        }

        public void ApplyTheme(Theme theme)
        {
            try
            {
                var app = Application.Current;
                if (app == null) return;

                var resolvedTheme = theme == Theme.System ? GetSystemTheme() : theme;

                // 1. Determine the key of the dictionary we need to load.
                string keyToFind = resolvedTheme == Theme.Dark ? "DarkTheme" : "LightTheme";

                // 2. Use TryFindResource to retrieve the keyed ResourceDictionary (LightTheme/DarkTheme).
                // This is the FIX for the lookup error.
                ResourceDictionary? themeDict = app.TryFindResource(keyToFind) as ResourceDictionary;

                if (themeDict != null)
                {
                    // 3. Find and remove the EXISTING theme dictionary.
                    // We use the ThemeResourceKey to store a reference to the active theme dictionary.
                    if (app.Resources[ThemeResourceKey] is ResourceDictionary existingTheme)
                    {
                        // Remove the currently active theme dictionary from the MergedDictionaries list
                        app.Resources.MergedDictionaries.Remove(existingTheme);
                    }

                    // 4. Apply new theme: Add the new theme dictionary to MergedDictionaries.
                    app.Resources.MergedDictionaries.Add(themeDict);

                    // 5. Store a reference to the newly active dictionary.
                    app.Resources[ThemeResourceKey] = themeDict;

                    _currentTheme = theme;
                    UpdateWindowBackgrounds();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error applying theme: {ex.Message}");
            }
        }

        public void ToggleTheme()
        {
            var newTheme = _currentTheme == Theme.Light ? Theme.Dark : Theme.Light;
            ApplyTheme(newTheme);
        }

        private static Theme GetSystemTheme()
        {
            try
            {
                // Check Windows registry for system theme
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

                var value = key?.GetValue("AppsUseLightTheme");
                if (value is int intValue)
                {
                    return intValue == 1 ? Theme.Light : Theme.Dark;
                }
            }
            catch
            {
                // If we can't detect, default to Light
            }

            return Theme.Light;
        }

        private static void UpdateWindowBackgrounds()
        {
            var app = Application.Current;
            if (app == null) return;

            try
            {
                var bgBrush = app.TryFindResource("PrimaryBackground") as System.Windows.Media.Brush;

                foreach (Window window in app.Windows)
                {
                    if (bgBrush != null)
                    {
                        window.Background = bgBrush;
                    }
                }
            }
            catch
            {
                // Silently fail
            }
        }
    }
}