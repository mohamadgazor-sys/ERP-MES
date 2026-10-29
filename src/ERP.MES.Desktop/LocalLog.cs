using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace ERP.MES.Desktop;

/// <summary>
/// A small local diagnostic log (offline presentation package, docs/OFFLINE_DEMO_GUIDE.md): one text file per day in the folder named by the environment variable <c>ERPMES_LOG_DIR</c>
/// (default: ERP-MES\logs under the user's local application data). It records the start-up facts and the TYPE, message and stack of unexpected failures. It never records passwords,
/// user input or request data, and it never fails the application: any problem writing the log is ignored.
/// </summary>
internal static class LocalLog
{
    public const string FolderVariable = "ERPMES_LOG_DIR";

    private static readonly object Gate = new();

    public static string Folder =>
        Environment.GetEnvironmentVariable(FolderVariable) is { Length: > 0 } configured
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ERP-MES", "logs");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string context, Exception exception) =>
        Write("ERROR", context + ": " + exception.GetType().FullName + ": " + exception.Message + Environment.NewLine + exception);

    public static void StartupFacts(string databasePath)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";
        Info($"start; app {version}; {RuntimeInformation.FrameworkDescription}; {RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}; database {databasePath}");
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                var file = Path.Combine(Folder, "erpmes-" + DateTime.Now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) + ".log");
                File.AppendAllText(file, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + " " + level + " " + message + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // Logging must never break the application.
        }
    }
}
