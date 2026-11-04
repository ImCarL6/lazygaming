using System.Text.RegularExpressions;

namespace LazyGaming;

// Emuladores encontrados nas pastas de jogos viram uma capa por jogo, não uma capa do emulador.
// Hoje: Eglantine com conteúdos NUP válidos (mesma validação do cadastro manual). Arquivos do mesmo jogo
// (idiomas/edições) viram escolhas no painel Como jogar; nada é extraído, copiado ou executado para indexar.
public static class EmulatorLibrary
{
    public static bool IsEmulator(string executable) => Path.GetFileName(executable).Equals("eglantine.exe", StringComparison.OrdinalIgnoreCase);

    public static List<Game> Index(string emulator, string searchRoot, Func<string, bool>? valid = null) {
        valid ??= LocalGameStore.ValidRuntimeContent;
        var contents = new List<string>();
        try {
            contents = Directory.EnumerateFiles(searchRoot, "*.nup", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System })
                .Where(p => { try { return valid(p); } catch (IOException) { return false; } }).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).Take(64).ToList();
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        var groups = new List<List<string>>();
        foreach (var file in contents) {
            var key = Key(file);
            var group = groups.FirstOrDefault(g => g.Select(Key).Any(k => k.Contains(key) || key.Contains(k)));
            if (group == null) groups.Add([file]); else group.Add(file);
        }
        var folder = Path.GetDirectoryName(emulator)!;
        return groups.Select(group => {
            var title = group.Select(Title).OrderByDescending(t => t.Length).First();
            var id = "emu-" + LibraryHash(emulator + "|" + Key(group[0]));
            var ordered = group.OrderBy(f => Language(f).Order).ThenBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            LaunchChoice[]? choices = null; string? label = null;
            if (ordered.Count > 1) {
                var used = new HashSet<string>();
                choices = ordered.Select(f => {
                    var (lid, name, _) = Language(f);
                    var unique = lid; for (var n = 2; !used.Add(unique); n++) unique = lid + "-" + n;
                    return new LaunchChoice(unique, unique == lid ? name : name + " " + unique[(lid.Length + 1)..], f);
                }).Take(8).ToArray();
                label = ordered.Any(f => Language(f).Id != "original") ? "Idioma" : "Versão";
            }
            var first = ordered[0];
            // Arte opcional ao lado do jogo ("<jogo> - capa.jpg", "<jogo> - fundo.jpg"; ou capa/fundo/cover/backdrop se só houver
            // um jogo na pasta). O PC prepara as imagens e o app da TV recebe as mesmas pelo auxiliar.
            var art = SidecarArt(group, Key(group[0]), groups.Count == 1);
            var metadata = art == null ? null : new GameMetadata(null, title, null, [], [], "", default, "local", Artwork: art);
            return new Game(id, title, emulator, emulator, ArtworkId: id, Folder: folder, ProcessArgument: first, LaunchDirectory: folder,
                LocalMetadata: metadata, LaunchArguments: ["-f", "-g", first], Choices: choices, ChoiceLabel: label);
        }).ToList();
    }
    static readonly string[] CoverNames = ["capa", "cover", "poster", "box"], BackdropNames = ["fundo", "backdrop", "background", "hero"];
    public static LocalArtwork? SidecarArt(IEnumerable<string> contents, string key, bool alone) {
        string? Find(string[] kinds) {
            foreach (var dir in contents.Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase)) {
                IEnumerable<string> images;
                try { images = Directory.EnumerateFiles(dir!).Where(f => Regex.IsMatch(Path.GetExtension(f), @"^\.(jpe?g|png|webp)$", RegexOptions.IgnoreCase)).ToList(); } catch (IOException) { continue; } catch (UnauthorizedAccessException) { continue; }
                foreach (var image in images) {
                    var name = Path.GetFileNameWithoutExtension(image);
                    var m = Regex.Match(name, @"^(?<game>.*?)[\s._-]*(?<kind>" + string.Join("|", kinds) + @")$", RegexOptions.IgnoreCase);
                    if (!m.Success) continue;
                    var game = Regex.Replace(m.Groups["game"].Value.ToLowerInvariant(), "[^a-z0-9]", "");
                    if (game.Length == 0 ? alone : game.Contains(key) || key.Contains(game)) return image;
                }
            }
            return null;
        }
        var cover = Find(CoverNames); var backdrop = Find(BackdropNames);
        return cover == null && backdrop == null ? null : new LocalArtwork(cover ?? backdrop!, backdrop ?? cover!, "", "");
    }
    // Nome do jogo sem versão, atualização, idioma e marcadores técnicos.
    public static string Title(string file) {
        var t = Path.GetFileNameWithoutExtension(file);
        t = Regex.Replace(t, @"\[[^\]]*\]|\([^)]*\)", " ");
        t = Regex.Replace(t, @"\+\s*(Update|DLC|Atualiza\w*)[^+]*", " ", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\bv?\d+(\.\d+)+[a-z]?\b", " ", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\b(PT-?BR|PTBR|EN|ENG|English|Ingl[eê]s|Portugu[eê]s|DualSense|Dual Sense|NSP|NUP|XCI)\b", " ", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\s+", " ").Trim(' ', '-', '_', '+');
        return t.Length > 0 ? t : Path.GetFileNameWithoutExtension(file);
    }
    static string Key(string file) => Regex.Replace(Title(file).ToLowerInvariant(), "[^a-z0-9]", "");
    public static (string Id, string Name, int Order) Language(string file) {
        var name = Path.GetFileNameWithoutExtension(file);
        if (Regex.IsMatch(name, @"\bPT-?BR\b|PTBR|Portugu[eê]s", RegexOptions.IgnoreCase)) return ("pt-br", "Português (Brasil)", 0);
        if (Regex.IsMatch(name, @"\b(EN|ENG|English|Ingl[eê]s)\b", RegexOptions.IgnoreCase)) return ("en", "English", 1);
        return ("original", "Original", 2);
    }
    static string LibraryHash(string text) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text.ToLowerInvariant())))[..12];
}
