using LazyGaming;
using System.Text.Json;

static class LaunchChoiceTests
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static async Task Refused(Func<Task> action) { try { await action(); } catch (InvalidOperationException) { return; } throw new Exception("Unsafe launch accepted"); }
    public static async Task Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "lg-choices-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var library = new GameLibrary(root, true);
            var executable = Path.Combine(root, "eglantine.exe"); File.WriteAllText(executable, "fixture, never executed");
            string Content(string name) { var path = Path.Combine(root, name, "main"); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "fixture"); File.WriteAllText(path + ".npdm", "fixture"); return path; }
            string pt = Content("pt"), en = Content("en"), unrelated = Content("unrelated");
            var game = new Game("local-111111111111", "Example", executable, executable, Folder: root, ProcessArgument: pt,
                Choices: [new("pt-br", "Português (Brasil)", pt), new("en", "English", en)], ChoiceLabel: "Idioma");
            library.Games.Clear(); library.Games.Add(game);
            var platform = new SimulatedGames { CloseAccepted = true }; var environment = new SimulatedEnvironment();
            var service = new GameService(library, platform, environment);
            await Refused(() => service.Start(game.Id, choice: en));
            await Refused(() => service.Start(game.Id, choice: "unknown"));
            Check(platform.LaunchCount == 0 && environment.Preparations == 0, "unregistered/path choice refused before session changes");
            await service.Start(game.Id, "stream", "en");
            Check(platform.Active.Single().Argument == en && platform.LastMode == "stream", "selected content reaches platform in selected transport");
            var launch = WindowsGames.LaunchInfo(LaunchChoices.Select(game, "en"));
            Check(!launch.UseShellExecute && launch.ArgumentList.SequenceEqual(["-f", "-g", en]), "exact arguments, no shell");
            Check(new LaunchPreferenceStore(root).Get(game) == "en", "last launched choice survives new store/PC-TV sessions");
            var status = JsonSerializer.SerializeToElement(await service.StatusAsync());
            Check(status.GetProperty("games")[0].GetProperty("running").GetBoolean() && !status.ToString().Contains(en), "status recognizes English and hides paths");
            await Refused(() => service.Start(game.Id, choice: "pt-br"));
            await service.Start(game.Id); await service.FocusRunning(); await service.MoveToTv(game.Id);
            Check(platform.LaunchCount == 1 && platform.Moved.Single().Argument == en, "continue/focus/move preserve running variant, no duplicate");
            platform.Active.Add(new(55, executable, unrelated));
            await service.Stop(game.Id, false);
            Check(platform.Active.Single().Argument == unrelated, "close targets only registered runtime content");
            platform.Active.Clear(); await service.Tick();
            await service.Start(game.Id);
            Check(platform.Active.Single().Argument == en, "older clients without choice use remembered variant");
            await service.Stop(game.Id, false);
            File.Delete(en); await Refused(() => service.Start(game.Id, choice: "en"));
            Check(platform.LaunchCount == 2, "missing choice does not launch fallback silently");
            await service.Start(game.Id, choice: "pt-br"); await service.Stop(game.Id, false);
            var preferencePath = Path.Combine(root, "library", "launch-preferences.json");
            File.WriteAllText(preferencePath, "broken"); File.WriteAllText(preferencePath + ".bak", "broken");
            Check(new LaunchPreferenceStore(root).Get(game) == "pt-br", "corrupt optional preference never breaks library");
            Check(!LaunchChoices.Matches(game, new(2, executable, unrelated)), "same emulator with unrelated content is not this game");
            var plain = library.Games[0] with { Choices = null, ProcessArgument = null };
            Check(LaunchChoices.Select(plain, null) == plain, "single version launch unchanged");
            Console.WriteLine("PASS: variants, transport, persistence, exact runtime, continue, stop, missing files and invalid choices.");
        } finally { Directory.Delete(root, true); }
    }
}
