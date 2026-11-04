using Microsoft.Win32;

namespace LazyGaming;

public static class DesktopReopen
{
    public const string Scheme="lazygaming-pc";
    public static string Command(string executable) {
        if(!Path.IsPathFullyQualified(executable) || executable.Contains('"') || !Path.GetFileName(executable).Equals("LazyGaming.Host.exe",StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Executável inválido.");
        return "\""+executable+"\" --open-hub"; // URI text is never passed to the executable.
    }
    public static bool Register() {
        var executable=Environment.ProcessPath;
        if(executable==null || !Path.GetFileName(executable).Equals("LazyGaming.Host.exe",StringComparison.OrdinalIgnoreCase))return false;
        try {
            var command=Command(executable);
            using var existing=Registry.CurrentUser.OpenSubKey(@"Software\Classes\"+Scheme);
            if(existing!=null && existing.GetValue("LazyGamingOwner") as string != "LazyGaming")return false;
            using var root=Registry.CurrentUser.CreateSubKey(@"Software\Classes\"+Scheme);
            root.SetValue("","URL:LazyGaming");root.SetValue("URL Protocol","");root.SetValue("LazyGamingOwner","LazyGaming");
            using var open=root.CreateSubKey(@"shell\open\command");open.SetValue("",command);return true;
        }catch(Exception e)when(e is UnauthorizedAccessException or System.Security.SecurityException or IOException){return false;}
    }
}
