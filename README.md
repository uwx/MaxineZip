# MaxineZip

A simple CLI application for creating or repacking zip files using libdeflate or Zopfli.

### Limitations
The entire source file is loaded into memory, so it must be <2GB in size. It will use twice the file size in RAM.

MaxineZip is designed to compress a single file. If compressing multiple files is desired, create the zip file with any
other tool in "Store" mode, then use the `-rezip` option.

Based on [JosePineiro/LittleZip](https://github.com/JosePineiro/LittleZip).

Usage:

    MaxineZip <output zip> <input file name> [-level <number>] [-fname <file name in zip>]
    MaxineZip -rezip <zip file>

Levels 0-12 will use libdeflate (fast)
Levels 13 and above will use Zopfli (extremely slow)
Default level: 6

Examples:

**Compress a single file into a zip**

    MaxineZip file.zip inputfile.png -level 12

**Compress a single file into a zip and rename it**

    MaxineZip file.zip inputfile.png -fname filenameinzip.png -level 12

**Repack**

    MaxineZip -rezip file.zip

## Credit
- [JosePineiro/LittleZip](https://github.com/JosePineiro/LittleZip)
- [ebiggers/libdeflate](https://github.com/ebiggers/libdeflate)
- [drivehappy/libzopfli-sharp](https://github.com/drivehappy/libzopfli-sharp)
