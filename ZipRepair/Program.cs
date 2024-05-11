// See https://aka.ms/new-console-template for more information

using System.IO.Compression;
using MaxineZip;

var root = @"root";

foreach (var file in Directory.EnumerateFiles(root, "*.zip", SearchOption.AllDirectories))
{
    Console.WriteLine(file);
    try
    {
        using var lzip = new LittleZip(file);
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex);
        // throw new InvalidOperationException($"While operating on {file}", ex);
    }
}

Console.WriteLine("Hello, World!");