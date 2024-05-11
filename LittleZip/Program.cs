using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;

if (args.Length == 0)
{
    Console.WriteLine(
        """
        MaxineZip
        
        Usage:
            MaxineZip <output zip> <input file name> [-level <number>] [-fname <file name in zip>]
            MaxineZip -rezip <zip file>
        Levels 0-12: libdeflate (fast)
        Levels 13+: Zopfli (very slow)
        Default level: 6
        
        Examples:
            MaxineZip file.zip inputfile.png -level 12
            MaxineZip file.zip inputfile.png -fname filenameinzip.png -level 12
            MaxineZip -rezip file.zip
        """);
    return 0;
}

var watch = Stopwatch.StartNew();

var goodArgs = new List<string>(args.Length);

var rezipMode = false;
var level = 6;
string? fname = null;
for (var i = 0; i < args.Length; i++)
{
    var arg = args[i];
    if (arg == "-rezip")
    {
        rezipMode = true;
    }
    else if (arg == "-level")
    {
        level = int.Parse(args[++i]);
    }
    else if (arg == "-fname")
    {
        fname = args[++i];
    }
    else
    {
        goodArgs.Add(arg);
    }
}

var inputName = goodArgs[0];

if (rezipMode)
{
    var startSize = new FileInfo(inputName).Length;
    using (var lzip = new LittleZip(inputName, allowExisting: true))
    {
        if (!lzip.Recompress(level, true))
        {
            Console.WriteLine("Failed to repack!");
            return 1;
        }
    }

    var endSize = new FileInfo(inputName).Length;
    
    Console.WriteLine($"Repacked {inputName} in {watch.Elapsed} with savings of {BytesToString(startSize - endSize)} ({(endSize / startSize)*100:0.00}%)");
}
else
{
    var outputName = goodArgs[1];

    var startSize = new FileInfo(outputName).Length;

    using (var lzip = new LittleZip(inputName, allowExisting: false))
    {
        lzip.AddFile(outputName, fname ?? Path.GetFileName(outputName), level);
    }
    
    var endSize = new FileInfo(inputName).Length;
    
    Console.WriteLine($"Created {outputName} in {watch.Elapsed} with savings of {BytesToString(startSize - endSize)} ({(endSize / startSize)*100:0.00}%)");
}

return 0;

//https://stackoverflow.com/a/11124118
static string BytesToString(long value)
{
    string suffix;
    double readable;
    switch (Math.Abs(value))
    {
        case >= 0x1000000000000000:
            suffix = "EiB";
            readable = value >> 50;
            break;
        case >= 0x4000000000000:
            suffix = "PiB";
            readable = value >> 40;
            break;
        case >= 0x10000000000:
            suffix = "TiB";
            readable = value >> 30;
            break;
        case >= 0x40000000:
            suffix = "GiB";
            readable = value >> 20;
            break;
        case >= 0x100000:
            suffix = "MiB";
            readable = value >> 10;
            break;
        case >= 0x400:
            suffix = "KiB";
            readable = value;
            break;
        default:
            return value.ToString("0 B");
    }

    return (readable / 1024).ToString("0.## ", CultureInfo.InvariantCulture) + suffix;
}