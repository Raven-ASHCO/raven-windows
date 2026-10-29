using System;
using System.Diagnostics;
using System.IO;
using Raven.Windows.Core.Presentation;

namespace Raven.Windows.Services;

/// <summary>
/// Keeps this app's own preferences (the theme and "Show technical details": they name no one) in
/// %LOCALAPPDATA%\Raven\desktop-ui.json, beside the app and never in Raven's profile; Raven's engine
/// keeps every other setting. A missing or broken file gives the defaults; a file that can't be
/// written leaves the choice for this run only.
/// </summary>
internal static class AppPrefsStore
{
    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Raven", "desktop-ui.json");

    public static AppPrefs Load()
    {
        try
        {
            string path = FilePath;
            return File.Exists(path) ? AppPrefs.Parse(File.ReadAllText(path)) : new AppPrefs();
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Raven: desktop-ui.json: " + ex.Message);
            return new AppPrefs();
        }
    }

    public static void Save(AppPrefs prefs)
    {
        try
        {
            string path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllText(temp, prefs.ToJson());
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Raven: desktop-ui.json not saved: " + ex.Message);
        }
    }
}
