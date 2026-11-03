using System.Text.Json;

namespace LazyGaming;

// Paths stay on the PC. Clients select only a registered, opaque choice key.
public sealed record LaunchChoice(string Id, string Name, string ContentPath);

public static class LaunchChoices
{
    public static Game Select(Game game, string? choice)
    {
        if (game.Choices is not { Length: > 0 }) {
            if (choice != null) throw new InvalidOperationException("Esta versão do jogo não está cadastrada.");
            return game;
        }
        var selected = game.Choices.SingleOrDefault(v => v.Id == choice)
            ?? throw new InvalidOperationException("Escolha uma versão disponível do jogo.");
        if (!LocalGameStore.ValidRuntimeContent(selected.ContentPath))
            throw new InvalidOperationException("Os arquivos desta versão estão indisponíveis. Confira o cadastro no PC.");
        return game with { ProcessArgument = selected.ContentPath, LaunchArguments = ["-f", "-g", selected.ContentPath] };
    }

    public static IEnumerable<string?> Contents(Game game) => game.Choices is { Length: > 0 }
        ? game.Choices.Select(v => (string?)v.ContentPath).Append(game.ProcessArgument).Distinct(StringComparer.OrdinalIgnoreCase)
        : [game.ProcessArgument];

    public static bool Matches(Game game, Running process) => string.Equals(game.ProcessPath, process.Path, StringComparison.OrdinalIgnoreCase)
        && Contents(game).Any(content => content == null || string.Equals(content, process.Argument, StringComparison.OrdinalIgnoreCase));

    public static Game ForProcess(Game game, Running process) => game with {
        ProcessArgument = process.Argument, LaunchArguments = process.Argument == null ? game.LaunchArguments : ["-f", "-g", process.Argument]
    };
}

public sealed class LaunchPreferenceStore(string directory)
{
    string FilePath => Path.Combine(directory, "library", "launch-preferences.json");
    public string? Get(Game game)
    {
        if (game.Choices is not { Length: > 0 }) return null;
        Dictionary<string, string> values;
        try { values = LibraryJson.Read<Dictionary<string, string>>(FilePath, () => []); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { values = []; }
        return values.TryGetValue(game.Id, out var id) && game.Choices?.Any(v => v.Id == id) == true ? id : game.Choices?.FirstOrDefault()?.Id;
    }
    public void Save(Game game, string? choice)
    {
        if (choice == null || game.Choices?.Any(v => v.Id == choice) != true) return;
        using var held = new LibraryFileLock(FilePath);
        var values = LibraryJson.Read<Dictionary<string, string>>(FilePath, () => []);
        values[game.Id] = choice;
        LibraryJson.Write(FilePath, values);
    }
}
