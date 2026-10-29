using System.Text;
using System.Text.RegularExpressions;

namespace LazyGaming;

public sealed class VdfNode : Dictionary<string, object>
{
    public VdfNode() : base(StringComparer.OrdinalIgnoreCase) { }
    public VdfNode Child(string name) => TryGetValue(name, out var value) && value is VdfNode node ? node : new();
    public string Text(string name) => TryGetValue(name, out var value) ? value.ToString() ?? "" : "";
    public static VdfNode TextFile(string text)
    {
        var tokens = Regex.Matches(text, "\"((?:\\\\.|[^\"\\\\])*)\"|[{}]").Select(m => m.Value).ToArray();
        int index = 0;
        string Decode(string token) => token[1..^1].Replace("\\\\", "\\").Replace("\\\"", "\"");
        VdfNode Read()
        {
            var node = new VdfNode();
            while (index < tokens.Length)
            {
                var token = tokens[index++]; if (token == "}") break;
                if (!token.StartsWith('"') || index >= tokens.Length) throw new InvalidDataException("VDF inválido.");
                var key = Decode(token); var value = tokens[index++];
                node[key] = value == "{" ? Read() : Decode(value);
            }
            return node;
        }
        return Read();
    }
}

// Leitura de appinfo v39–41. Tokens/chaves privadas do cabeçalho são ignorados.
public static class SteamData
{
    public static Dictionary<uint, VdfNode> Read(string path, HashSet<uint> wanted)
    {
        var result = new Dictionary<uint, VdfNode>();
        if (!File.Exists(path)) return result;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        uint magic = reader.ReadUInt32(); _ = reader.ReadUInt32();
        if (magic is not (0x07564427 or 0x07564428 or 0x07564429)) return result;
        string[]? strings = null;
        string CString()
        {
            using var bytes = new MemoryStream(); byte b;
            while ((b = reader.ReadByte()) != 0) { bytes.WriteByte(b); if (bytes.Length > 2_000_000) throw new InvalidDataException(); }
            return Encoding.UTF8.GetString(bytes.ToArray());
        }
        if (magic == 0x07564429)
        {
            long table = reader.ReadInt64(), current = stream.Position; stream.Position = table;
            uint count = reader.ReadUInt32(); if (count > 1_000_000) return result;
            strings = new string[count]; for (int i = 0; i < count; i++) strings[i] = CString();
            stream.Position = current;
        }
        VdfNode ReadNode(int depth = 0)
        {
            if (depth > 50) throw new InvalidDataException();
            var node = new VdfNode();
            while (true)
            {
                byte type = reader.ReadByte(); if (type == 8) return node;
                string key = strings == null ? CString() : strings[reader.ReadUInt32()];
                node[key] = type switch {
                    0 => ReadNode(depth + 1), 1 => CString(), 2 => reader.ReadInt32(), 3 => reader.ReadSingle(),
                    4 or 6 => reader.ReadUInt32(), 7 => reader.ReadUInt64(), 10 => reader.ReadInt64(),
                    _ => throw new InvalidDataException("Tipo binário VDF não suportado.")
                };
            }
        }
        while (stream.Position + 8 < stream.Length)
        {
            uint id = reader.ReadUInt32(); if (id == 0) break;
            uint size = reader.ReadUInt32(); long end = stream.Position + size;
            if (end > stream.Length || size < 40) break;
            if (wanted.Contains(id))
            {
                stream.Position += magic >= 0x07564428 ? 60 : 40;
                try { result[id] = ReadNode().Child("appinfo"); }
                catch (Exception e) when (e is InvalidDataException or EndOfStreamException or IndexOutOfRangeException) { }
            }
            stream.Position = end;
        }
        return result;
    }
}
