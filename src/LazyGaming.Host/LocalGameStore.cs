using System.Text.Json;
using System.Text.Json.Serialization;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace LazyGaming;

// Cadastro local explícito: não depende de uma loja e nunca executa os arquivos importados.
public record LocalGameRegistration(string Executable, string WorkingDirectory, GameMetadata Metadata, string? Arguments = null, string? ContentPath = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Id = null);

public sealed class LocalGameStore(string directory)
{
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    string FilePath => Path.Combine(directory, "library", "local-games.json");
    static string Identity(LocalGameRegistration item) => item.Id ?? GameLibrary.LocalId(item.WorkingDirectory);
    public static bool ValidRuntimeContent(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.Any(char.IsControl) || !File.Exists(path)) return false;
        if (Path.GetFileName(path) == "main") return File.Exists(path + ".npdm");
        if (!Path.GetExtension(path).Equals(".nup", StringComparison.OrdinalIgnoreCase)) return false;
        // Conferir o contêiner/TOC sem extrair, copiar ou executar arquivos do jogo.
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[128];
        if (stream.ReadAtLeast(header, header.Length, false) != header.Length || !header[..4].SequenceEqual("NUP\u0001"u8)
            || BinaryPrimitives.ReadUInt16LittleEndian(header[4..]) != header.Length) return false;
        var offset = BinaryPrimitives.ReadUInt64LittleEndian(header[8..]);
        var size = BinaryPrimitives.ReadUInt64LittleEndian(header[16..]);
        if (BinaryPrimitives.ReadUInt64LittleEndian(header[64..]) != (ulong)stream.Length || size is < 48 or > 16_777_216
            || offset < 128 || size > (ulong)stream.Length || offset > (ulong)stream.Length - size) return false;
        stream.Position = (long)offset;
        var toc = new byte[(int)size]; stream.ReadExactly(toc);
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(toc), header[24..56]);
    }
    static LocalGameRegistration Validate(LocalGameRegistration item)
    {
        if (!Path.IsPathFullyQualified(item.Executable) || !Path.IsPathFullyQualified(item.WorkingDirectory)) throw new InvalidDataException("Informe caminhos absolutos para a instalação local.");
        var executable = Path.GetFullPath(item.Executable);
        var folder = Path.GetFullPath(item.WorkingDirectory).TrimEnd(Path.DirectorySeparatorChar);
        if (!File.Exists(executable) || !Directory.Exists(folder) || !executable.Equals(GameLibrary.SafeInside(folder, Path.GetRelativePath(folder, executable)), StringComparison.OrdinalIgnoreCase)
            || !Path.GetExtension(executable).Equals(".exe", StringComparison.OrdinalIgnoreCase) || GameLibrary.IsHelper(Path.GetFileName(executable)))
            throw new InvalidDataException("Executável jogável local não encontrado dentro da instalação.");
        if (item.Metadata == null || string.IsNullOrWhiteSpace(item.Metadata.Title) || item.Metadata.Title.Length > 120 || item.Metadata.Title.Any(char.IsControl)
            || item.Metadata.Source != "local" || item.Metadata.StoreId != null || item.Metadata.Artwork == null)
            throw new InvalidDataException("A ficha local precisa de título e artes próprios, sem identidade Steam.");
        if (!string.IsNullOrEmpty(item.Arguments)) throw new InvalidDataException("Este cadastro local aceita executáveis diretos sem argumentos.");
        if (item.Id != null && !System.Text.RegularExpressions.Regex.IsMatch(item.Id, "^local-[a-f0-9]{12}$"))
            throw new InvalidDataException("Identidade local inválida.");
        if (item.ContentPath != null && (!Path.GetFileName(executable).Equals("eglantine.exe", StringComparison.OrdinalIgnoreCase)
            || !ValidRuntimeContent(item.ContentPath)))
            throw new InvalidDataException("Informe um ExeFS ou contêiner NUP válido existente para o Eglantine.");
        return item with { Executable = executable, WorkingDirectory = folder, Arguments = null };
    }
    List<LocalGameRegistration> Load() => File.Exists(FilePath) ? JsonSerializer.Deserialize<List<LocalGameRegistration>>(File.ReadAllText(FilePath), Json) ?? [] : [];
    public IEnumerable<Game> Read(List<string> notes)
    {
        var games = new List<Game>();
        try {
            foreach (var entry in Load()) {
                try {
                    var item = Validate(entry);
                    games.Add(new(Identity(item), item.Metadata.Title, item.Executable, item.Executable,
                        Folder: item.WorkingDirectory, ProcessArgument: item.ContentPath, LaunchDirectory: item.WorkingDirectory, LocalMetadata: item.Metadata,
                        LaunchArguments: item.ContentPath == null ? null : ["-f", "-g", item.ContentPath]));
                } catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NullReferenceException) { notes.Add("Uma instalação local cadastrada está indisponível."); }
            }
        } catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { notes.Add("Cadastro local temporariamente indisponível; biblioteca existente preservada."); }
        return games;
    }
    public string Import(string manifest, string? replaceId = null)
    {
        var item = Validate(JsonSerializer.Deserialize<LocalGameRegistration>(File.ReadAllText(manifest), Json) ?? throw new InvalidDataException("Ficha local vazia."));
        if (item.Id != null) throw new InvalidDataException("A identidade é mantida pelo cadastro; use a substituição explícita para atualizar uma instalação.");
        var entries = Load(); // Erro de leitura não autoriza sobrescrever um cadastro existente.
        var sameInstallation = entries.Where(e => string.Equals(Path.GetFullPath(e.WorkingDirectory).TrimEnd(Path.DirectorySeparatorChar), item.WorkingDirectory, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFullPath(e.Executable), item.Executable, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (sameInstallation.Length > 1) throw new InvalidDataException("A instalação corresponde a mais de um cadastro local.");
        var id = sameInstallation.SingleOrDefault() is { } current ? Identity(current) : GameLibrary.LocalId(item.WorkingDirectory);
        if (replaceId != null) {
            var previous = entries.Where(e => Identity(e) == replaceId).ToArray();
            if (previous.Length != 1 || !string.Equals(previous[0].Metadata.Title, item.Metadata.Title, StringComparison.OrdinalIgnoreCase)
                || sameInstallation.Any(e => Identity(e) != replaceId))
                throw new InvalidDataException("A substituição precisa identificar exatamente o mesmo jogo local, sem conflitar com outra instalação.");
            id = replaceId;
        }
        if (id != GameLibrary.LocalId(item.WorkingDirectory)) item = item with { Id = id };
        var art = item.Metadata.Artwork!;
        var sourceRoot = Path.GetDirectoryName(Path.GetFullPath(manifest))!;
        var sources = new[] { art.Cover, art.Backdrop }.Select(p => Path.GetFullPath(p, sourceRoot)).ToArray();
        foreach (var source in sources) if (new FileInfo(source).Length > 8_000_000) throw new InvalidDataException("Arte local muito grande.");
        var content = sources.SelectMany(p => System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p))).ToArray();
        var revision = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content))[..16];
        var relative = Path.Combine("imports", id, revision);
        var output = Path.Combine(directory, "library", relative);
        Directory.CreateDirectory(output);
        foreach (var (kind, path) in new[] { ("cover", art.Cover), ("backdrop", art.Backdrop) }) {
            var source = Path.GetFullPath(path, sourceRoot);
            using var input = File.OpenRead(source);
            ArtworkStore.OptimizeLocal(input, Path.Combine(output, kind + ".jpg"), kind);
        }
        item = item with { Metadata = item.Metadata with { Artwork = art with { Cover = Path.Combine(relative, "cover.jpg"), Backdrop = Path.Combine(relative, "backdrop.jpg") } } };
        entries.RemoveAll(e => Identity(e) == id || string.Equals(Path.GetFullPath(e.WorkingDirectory).TrimEnd(Path.DirectorySeparatorChar), item.WorkingDirectory, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFullPath(e.Executable), item.Executable, StringComparison.OrdinalIgnoreCase));
        entries.Add(item);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(entries, Json)); File.Move(FilePath + ".tmp", FilePath, true);
        return id;
    }
}
