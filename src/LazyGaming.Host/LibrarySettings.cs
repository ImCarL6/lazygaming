using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LazyGaming;

// Serialize the whole read/modify/write across CLI imports and the running helper.
public sealed class LibraryFileLock : IDisposable
{
    readonly Mutex mutex;
    public LibraryFileLock(string path) {
        mutex = new(false, "Local\\LazyGaming-library-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToLowerInvariant()))));
        try { if (!mutex.WaitOne(TimeSpan.FromSeconds(15))) throw new IOException("A biblioteca está ocupada. Tente novamente."); }
        catch (AbandonedMutexException) { }
    }
    public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
}

public static class LibraryJson
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    public static T Read<T>(string path, Func<T> empty) {
        if (!File.Exists(path) && !File.Exists(path + ".bak")) return empty();
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? throw new JsonException(); }
        catch (Exception e) when (e is IOException or JsonException) {
            if (!File.Exists(path + ".bak")) throw;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path + ".bak"), Options) ?? throw new JsonException();
        }
    }
    public static void Write<T>(string path, T value) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) {
                JsonSerializer.Serialize(file, value, Options); file.Flush(true);
            }
            // The backup is the last committed value, including the first save.
            File.Copy(temp, path + ".bak.tmp", true); File.Move(path + ".bak.tmp", path + ".bak", true);
            File.Move(temp, path, true);
        } finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public record LibraryFolder(string Path, bool Enabled = true);
public record LibraryPreferences(LibraryFolder[] Folders, bool Steam = true, bool Automatic = true, int IntervalMinutes = 5, string[]? Excluded = null);
public sealed class LibrarySettings(string directory)
{
    string FilePath => Path.Combine(directory, "library", "settings.json");
    public LibraryPreferences Read() => LibraryJson.Read(FilePath, () => new LibraryPreferences([new(@"C:\Joguetes")]));
    public void Save(LibraryPreferences preferences) {
        if (preferences.Folders == null || preferences.Folders.Length > 32 || preferences.IntervalMinutes is not (1 or 5 or 15 or 30)
            || (preferences.Excluded?.Length ?? 0) > 1000) throw new InvalidOperationException("Configuração de busca inválida.");
        var folders = preferences.Folders.Select(f => {
            if (string.IsNullOrWhiteSpace(f.Path) || !Path.IsPathFullyQualified(f.Path) || f.Path.Any(char.IsControl) || f.Path.StartsWith("\\\\"))
                throw new InvalidOperationException("Escolha uma pasta local com caminho completo.");
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(f.Path));
            if (full == Path.GetPathRoot(full)) throw new InvalidOperationException("Escolha a pasta de jogos, em vez do disco inteiro.");
            return f with { Path = full };
        }).DistinctBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        using var held = new LibraryFileLock(FilePath);
        LibraryJson.Write(FilePath, preferences with { Folders = folders, Excluded = preferences.Excluded?.Distinct().ToArray() ?? [] });
    }
}
