// See https://aka.ms/new-console-template for more information

using System.IO.Compression;

var root = @"root";

var hashes = new Dictionary<uint, string>();

foreach (var file in Directory.EnumerateFiles(root, "*.zip", SearchOption.AllDirectories))
{
    try
    {
        using var zip = ZipFile.OpenRead(file);
        if (zip.Entries.Count == 1 && zip.Entries.FirstOrDefault(e => Path.GetExtension(e.FullName.AsSpan()) is ".sfc" or ".smc" or ".nes" or ".gb" or ".gbc" or ".gba" or ".iso" or ".bin" or ".nds" or ".dsi" or ".n64" or ".z64" or ".v64") is { } entry)
        {
            if (!hashes.TryAdd(entry.Crc32, file))
            {
                Console.WriteLine($"Duplicate: \"{file}\" to \"{hashes[entry.Crc32]}\"");
            }
        }
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException($"While operating on {file}", ex);
    }
}

Console.WriteLine("Hello, World!");