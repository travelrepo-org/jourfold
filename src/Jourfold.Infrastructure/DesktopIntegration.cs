using System.Text;
namespace Jourfold.Infrastructure;

/// <summary>User-requested registration of the portable Linux application, icon and URI handler.</summary>
public static class DesktopIntegration
{
    public static string InstallLinuxLauncher(string applicationDirectory, string? dataDirectory = null)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        var executable = Path.Combine(Path.GetFullPath(applicationDirectory), "Jourfold.Desktop");
        var iconSource = Path.Combine(applicationDirectory, "branding", "icon-256.png");
        if (!File.Exists(executable) || !File.Exists(iconSource)) throw new FileNotFoundException("Extract the complete Jourfold package before registering it.");
        var data = dataDirectory ?? Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(data)) data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        var icons = Path.Combine(data, "icons", "hicolor", "256x256", "apps"); Directory.CreateDirectory(icons);
        File.Copy(iconSource, Path.Combine(icons, "jourfold.png"), true);
        var applications = Path.Combine(data, "applications"); Directory.CreateDirectory(applications);
        var path = Path.Combine(applications, "jourfold.desktop");
        File.WriteAllText(path, "[Desktop Entry]\nType=Application\nName=Jourfold\nExec=" + QuoteExec(executable) + " %u\nIcon=jourfold\nCategories=Office;\nTerminal=false\nStartupWMClass=Jourfold\nMimeType=x-scheme-handler/jourfold;\n", new UTF8Encoding(false));
        return path;
    }
    private static string QuoteExec(string value)
    {
        if (value.Contains('\n') || value.Contains('\r')) throw new ArgumentException("Application paths cannot contain line breaks.");
        // Desktop-entry string escaping is applied after Exec argument escaping.
        var escaped = value.Replace("%", "%%").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$");
        return "\"" + escaped.Replace("\\", "\\\\") + "\"";
    }
}
