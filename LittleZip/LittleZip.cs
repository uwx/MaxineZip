////////////////////////////////////////////////////////////////////////////////////////////////////////////
// LittleZip C#. (GPL) Jose M. Piñeiro
// Version: 1.0.0.3 (May 20, 2018)
////////////////////////////////////////////////////////////////////////////////////////////////////////////
// 
// This file is Test for libdeflate wrapper
// Little can:
// - Compress several files in a very little zip
// - Compress in very little time
// - Use very little code
// - Very little learning for use
// - Use Storage and Deflate methods. Another methods are not implemented.
// 
// LittleZip can not:
// - Create a large zip ( > 2.147.483.647 bytes)
// - Store a large file ( > 2.147.483.647 bytes)
// - Use little memory ( need two times the compressed file )
// - Decompress one ZIP file. Use LittleUnZip program.
//
// 
// Use code from: http://github.com/jaime-olivares/zipstorer
// Use library from: https://github.com/ebiggers/libdeflate
// Use library from: https://github.com/drivehappy/libzopfli-sharp
//////////////////////////////////////////////////////////////////////////////////////////////////////////// 
// Compress Functions:
// LittleZip(string _filename, string _comment)
// - Open an existing ZIP file for append files. Create a new ZIP file if not exit. Optionally you can put a general comment.
// 
// LittleZip Create(Stream _stream, string _comment)
// - Open an existing ZIP stream for append files. Create a new ZIP stream if not exit. Optionally you can put a general comment.
// 
// AddFile(string pathFilename, string filenameInZip, int compressionLevel, string comment)
// - Add full contents of a file into the Zip storage. Optionally you can put a file comment.
// 
// AddBuffer(byte[] inBuffer, string filenameInZip, DateTime modifyTime, int compressionLevel, string comment = "")
// - Add full contents of a array into the Zip storage. Optionally you can put a file comment.
// 
// Close()
// - Updates central directory and close the Zip storage. Automatic call in dispose
// 
// Important note: Compression levels above 12, use ZOPLI library. It is very slow.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace MaxineZip
{
    /// <summary>
    /// Unique class for compression/decompression file. Represents a Zip file.
    /// </summary>
    public sealed class LittleZip : IDisposable
    {
        #region Private structs
        /// <summary>
        /// Compression method enumeration
        /// </summary>
        public enum Compression : ushort
        {
            /// <summary>Uncompressed storage</summary> 
            Store = 0,
            /// <summary>Deflate compression method</summary>
            Deflate = 8
        }
        #endregion

        /// <summary>
        /// Represents an entry in Zip file directory
        /// </summary>
        public struct ZipFileEntry
        {
            /// <summary>Compression method</summary>
            public Compression Method;
            /// <summary>Full path and filename as stored in Zip</summary>
            public string FilenameInZip;
            /// <summary>Original file size</summary>
            public uint FileSize;
            /// <summary>Compressed file size</summary>
            public uint CompressedSize;
            /// <summary>Offset of header information inside Zip storage</summary>
            public uint HeaderOffset;
            /// <summary>Size of header information</summary>
            public uint HeaderSize;
            /// <summary>32-bit checksum of entire file</summary>
            public uint Crc32;
            /// <summary>Last modification time of file</summary>
            public DateTime ModifyTime;
            /// <summary>User comment for file</summary>
            public string Comment;
            /// <summary>True if UTF8 encoding for filename and comments, false if default (CP 437)</summary>
            public bool EncodeUTF8;

            /// <summary>Overriden method</summary>
            /// <returns>Filename in Zip</returns>
            public override string ToString()
            {
                return FilenameInZip;
            }
        }

        #region Public fields
        /// <summary>True if UTF8 encoding for filename and comments, false if default (CP 437)</summary>
        public bool EncodeUTF8 { get; init; } = false;
        #endregion

        #region Private fields
        // List of files to store
        public List<ZipFileEntry> Files { get; } = [];
        // Filename of storage file
        private string _fileName;
        // Stream object of storage file
        private Stream _zipFileStream;
        // General comment
        private string _comment = string.Empty;
        // Central dir image
        private byte[] _centralDirImage;
        // Existing files in zip
        private ushort _existingFiles;
        // Default filename encoder
        private static Encoding DefaultEncoding { get; }
        #endregion

        public long Size => _zipFileStream.Length;

        #region Public methods
        /// <summary>
        /// Open exist ZIP file. If not exist create a new ZIP file.
        /// </summary>
        /// <param name="pathFilename">Full path of Zip file to create</param>
        /// <param name="zipComment">General comment for Zip file</param>
        /// <returns>LittleZip object</returns>
        public LittleZip(string pathFilename, string zipComment = "", bool allowExisting = true)
        {
            Stream zipStream;

            if (zipComment != "")
                _comment = zipComment;

            if (allowExisting && File.Exists(pathFilename))
            {
                zipStream = new FileStream(pathFilename, FileMode.Open, FileAccess.ReadWrite);
                ReadFileInfo(zipStream);
            }
            else
            {
                zipStream = new FileStream(pathFilename, FileMode.Create, FileAccess.ReadWrite);
            }

            _fileName = pathFilename;
            _zipFileStream = zipStream;
        }

        /// <summary>
        /// Open an existing storage from stream or Create a new zip storage in a stream
        /// </summary>
        /// <param name="zipStream">Stream Zip to create</param>
        /// <param name="zipComment">General comment for Zip file</param>
        /// <returns>LittleZip object</returns>
        public LittleZip(Stream zipStream, string zipComment = "", bool allowExisting = true)
        {
            if (!zipStream.CanSeek)
                throw new InvalidOperationException("Stream cannot seek");

            if (zipComment != string.Empty)
                _comment = zipComment;

            if (allowExisting && zipStream.Length > 0)
            {
                _zipFileStream = zipStream;
                ReadFileInfo(zipStream);
            }
            else
            {
                _zipFileStream = zipStream;
            }
        }

        static LittleZip()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            
            DefaultEncoding = Encoding.GetEncoding("IBM437");
        }

        /// <summary>
        /// Add full contents of a file into the Zip storage
        /// </summary>
        /// <param name="pathFilename">Full path of file to add to Zip storage</param>
        /// <param name="filenameInZip">Filename and path as desired in Zip directory</param>
        /// <param name="compressionLevel">Level os compression. 0 = store, 6 = medium/default, 13 = high</param>
        /// <param name="fileComment">Comment for stored file</param>   
        public void AddFile(string pathFilename, string filenameInZip, int compressionLevel, string fileComment = "")
        {
            //Check the maximun file size
            if (pathFilename.Length > int.MaxValue - 56)
                throw new Exception("File is too large to be processed by this program. Maximum size " + (int.MaxValue - 56));

            //Read the imput file
            var inBuffer = new FileInfo(pathFilename).Length > 0
                ? File.ReadAllBytes(pathFilename)
                : [];

            var modifyTime = File.GetLastWriteTime(pathFilename);

            //Add inBuffer to Zip
            AddBuffer(inBuffer, filenameInZip, modifyTime, compressionLevel, fileComment);
        }

        /// <summary>
        /// Add full contents of a array into the Zip storage
        /// </summary>
        /// <param name="inBuffer">Data to store in Zip</param>
        /// <param name="filenameInZip">Filename and path as desired in Zip directory</param>>
        /// <param name="modifyTime">Modify time for stored file</param>>
        /// <param name="compressionLevel">Level os compression. 0 = store, 6 = medium/default, 13 = high</param>
        /// <param name="fileComment">Comment for stored file</param>   
        public void AddBuffer(ReadOnlySpan<byte> inBuffer, string filenameInZip, DateTime modifyTime, int compressionLevel = 6, string fileComment = "")
        {
            Handle outBuffer = default;

            try
            {
                // Prepare the ZipFileEntry
                var zfe = new ZipFileEntry
                {
                    FileSize = (uint)inBuffer.Length,
                    EncodeUTF8 = EncodeUTF8,
                    FilenameInZip = NormalizedFilename(filenameInZip),
                    Comment = fileComment,
                    ModifyTime = modifyTime,
                    Method = Compression.Deflate
                };

                if (compressionLevel == 0 || inBuffer.Length == 0)
                {
                    zfe.Crc32 = UnsafeNativeMethods.GetCrc32(inBuffer);
                    zfe.Method = Compression.Store;
                }
                else
                {
                    // Deflate the Source and get ZipFileEntry data
                    if (compressionLevel > 12)
                    {
                        UnsafeNativeMethods.ZopfliDeflate(
                            inBuffer,
                            compressionLevel,
                            out outBuffer,
                            out zfe.CompressedSize,
                            out zfe.Crc32
                        );
                    }
                    else
                    {
                        UnsafeNativeMethods.Libdeflate(
                            inBuffer,
                            compressionLevel,
                            true,
                            out outBuffer,
                            out zfe.CompressedSize,
                            out zfe.Crc32
                        );
                    }

                    // If not reduced the size, use the original data.
                    if (zfe.CompressedSize == 0)
                    {
                        zfe.Method = Compression.Store;
                        zfe.CompressedSize = zfe.FileSize;
                    }
                }

                // //Wait for idle ZipFile stream
                // while (Blocked != filenameInZip)
                // {
                //     if (Blocked == null)
                //         Blocked = filenameInZip;
                //     else
                //     {
                //         Thread.Sleep(5);
                //     }
                // }

                // Write local header
                zfe.HeaderOffset = (uint)_zipFileStream.Position; // offset within file of the start of this local record
                WriteLocalHeader(ref zfe);

                // Write deflate data (or original data if can´t deflate) to zip
                _zipFileStream.Write(zfe.Method == Compression.Deflate ? outBuffer.Data : inBuffer);

                //Add file in the Zip Directory struct
                Files.Add(zfe);

                // //unblock zip file
                // Blocked = null;
            }
            finally
            {
                outBuffer.Dispose();
            }
        }

        /// <summary>
        /// Updates central directory and close the Zip storage
        /// </summary>
        /// <remarks>This is a required step, unless automatic dispose is used</remarks>
        public void Close()
        {
            var centralOffset = (uint)_zipFileStream.Position;
            uint centralSize = 0;

            if (_centralDirImage != null!)
                _zipFileStream.Write(_centralDirImage, 0, _centralDirImage.Length);

            foreach (var t in Files)
            {
                var pos = _zipFileStream.Position;
                WriteCentralDirRecord(t);
                centralSize += (uint)(_zipFileStream.Position - pos);
            }

            if (_centralDirImage != null)
                WriteEndRecord(centralSize + (uint)_centralDirImage.Length, centralOffset);
            else
                WriteEndRecord(centralSize, centralOffset);

            if (_zipFileStream != null!)
            {
                _zipFileStream.Flush();
                _zipFileStream.Dispose();
                _zipFileStream = null!;
            }
        }
        #endregion

        #region Private methods
        /* Local file header:
            local file header signature     4 bytes  (0x504b0304)
            version needed to extract       2 bytes  (20 in this implementation)
            general purpose bit flag        2 bytes  (Only implement encodin: 437 page or UTF8)  
            compression method              2 bytes
            last mod file time              2 bytes
            last mod file date              2 bytes
            crc-32                          4 bytes
            compressed size                 4 bytes
            uncompressed size               4 bytes
            filename length                 2 bytes
            extra field length              2 bytes  (Ever 0)

            filename (variable size)
            extra field (variable size). This implementation not use extra field for minimize ZIP size.
        */
        private void WriteLocalHeader(ref ZipFileEntry zfe)
        {
            //Encode filename
            var encoder = zfe.EncodeUTF8 ? Encoding.UTF8 : DefaultEncoding;
            var encodedFilename = encoder.GetBytes(zfe.FilenameInZip);

            //Write local header
            _zipFileStream.Write([ 0x50, 0x4b, 0x03, 0x04, 0x14, 0x00 ]);              // local file header signature + version needed to extract
            Write((ushort)(zfe.EncodeUTF8 ? 0x0800 : 0));  // filename encoding 
            Write((ushort)zfe.Method);                     // zipping method
            Write(DateTimeToDosTime(zfe.ModifyTime));      // zipping date and time
            Write(zfe.Crc32);                              // CRC32
            Write(zfe.CompressedSize);                     // Compressed size
            Write(zfe.FileSize);                           // Uncompressed size
            Write((ushort)encodedFilename.Length);          // filename length
            Write((ushort)0);                               // extra length = 0
            _zipFileStream.Write(encodedFilename, 0, encodedFilename.Length);

            // Add header size to the Zip Directory struct
            zfe.HeaderSize = (uint)(30 + encodedFilename.Length);
        }

        /* Central directory's File header:
            central file header signature   4 bytes  (0x504b0102)
            version made by                 2 bytes  
            version needed to extract       2 bytes  (Version 20 for maximun compatibility)
            general purpose bit flag        2 bytes
            compression method              2 bytes
            last mod file time              2 bytes
            last mod file date              2 bytes
            crc-32                          4 bytes
            compressed size                 4 bytes
            uncompressed size               4 bytes
            filename length                 2 bytes
            extra field length              2 bytes
            file comment length             2 bytes
            disk number start               2 bytes
            internal file attributes        2 bytes
            external file attributes        4 bytes
            relative offset of local header 4 bytes

            filename (variable size)
            extra field (variable size)
            file comment (variable size)
        */
        private void WriteCentralDirRecord(ZipFileEntry zfe)
        {
            var encoder = zfe.EncodeUTF8 ? Encoding.UTF8 : DefaultEncoding;
            var encodedFilename = encoder.GetBytes(zfe.FilenameInZip);
            var encodedComment = encoder.GetBytes(zfe.Comment);

            _zipFileStream.Write([ 0x50, 0x4b, 0x01, 0x02, 0x17, 0x0B, 0x14, 0x00 ]);  // central file header signature + version made by + version needed to extract
            
            Write((ushort)(zfe.EncodeUTF8 ? 0x0800 : 0));  // filename and comment encoding 
            Write((ushort)zfe.Method);                     // zipping method
            Write(DateTimeToDosTime(zfe.ModifyTime));      // zipping date and time
            Write(zfe.Crc32);                              // file CRC
            Write(zfe.CompressedSize);                     // compressed file size
            Write(zfe.FileSize);                           // uncompressed file size
            Write((ushort)encodedFilename.Length);         // length of Filename in zip
            Write((ushort)0);                              // extra length = 0
            Write((ushort)encodedComment.Length);          // file comment length
            Write((ushort)0);                              // disk=0
            Write((ushort)0);                              // file type: binary
            Write((ushort)0);                              // Internal file attributes
            Write((ushort)0x8100);                         // External file attributes (normal/readable)
            Write(zfe.HeaderOffset);                       // Offset of header
            _zipFileStream.Write(encodedFilename, 0, encodedFilename.Length); // Filename in zip
            _zipFileStream.Write(encodedComment, 0, encodedComment.Length);   // Comment of file
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Write<T>(T value) where T : unmanaged
        {
            _zipFileStream.Write(MemoryMarshal.Cast<T, byte>(MemoryMarshal.CreateReadOnlySpan(ref value, 1)));
        }

        /* End of central dir record:
            end of central dir signature    4 bytes  (0x06054b50)
            number of this disk             2 bytes
            number of the disk with the start of the central directory  2 bytes
            total number of entries in the central dir on this disk    2 bytes
            total number of entries in the central dir                 2 bytes
            size of the central directory   4 bytes
            offset of start of central directory with respect to the starting disk number        4 bytes
            zipfile comment length          2 bytes
            zipfile comment (variable size)
        */
        private void WriteEndRecord(uint size, uint offset)
        {
            _zipFileStream.Write([ 0x50, 0x4B, 0x05, 0x06, 0x00, 0x00, 0x00, 0x00 ]); // End of central directory signature = 0x06054b50 + Number of this disk + Disk where central directory starts

            var fileCount = (ushort)(Files.Count + _existingFiles);
            Span<ushort> temp = stackalloc ushort[2];
            temp[0] = fileCount; // Number of central directory records on this disk (or 0xffff for ZIP64)
            temp[1] = fileCount; // Total number of central directory records (or 0xffff for ZIP64)
            _zipFileStream.Write(MemoryMarshal.Cast<ushort, byte>(temp)); 

            Write(size); // Size of central directory (bytes) (or 0xffffffff for ZIP64)

            Write(offset); // Offset of start of central directory, relative to start of archive (or 0xffffffff for ZIP64)
            
            if (_comment.Length > 0)
            {
                WriteLengthPrefixedStringFast(_comment);
            }
            else
            {
                Write((ushort)0);
            }
        }

        private void WriteLengthPrefixedStringFast(string str)
        {
            var encoder = EncodeUTF8 ? Encoding.UTF8 : DefaultEncoding;
            var useArray = str.Length > 128;
            var encodedComment = useArray ? encoder.GetBytes(_comment) : stackalloc byte[encoder.GetByteCount(_comment)];
            var bytesWritten = encodedComment.Length;
            if (!useArray)
            {
                if (!encoder.TryGetBytes(str, encodedComment, out bytesWritten))
                {
                    encodedComment = encoder.GetBytes(str);
                    bytesWritten = encodedComment.Length;
                }
            }

            Write((ushort)bytesWritten);
            _zipFileStream.Write(encodedComment[..bytesWritten]);
        }

        /* DOS Date and time:
            MS-DOS date. The date is a packed value with the following format. Bits Description 
                0-4 Day of the month (131) 
                5-8 Month (1 = January, 2 = February, and so on) 
                9-15 Year offset from 1980 (add 1980 to get actual year) 
            MS-DOS time. The time is a packed value with the following format. Bits Description 
                0-4 Second divided by 2 
                5-10 Minute (059) 
                11-15 Hour (023 on a 24-hour clock) 
        */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint DateTimeToDosTime(DateTime dt)
        {
            return (uint)(
                (dt.Second / 2) | (dt.Minute << 5) | (dt.Hour << 11) |
                (dt.Day << 16) | (dt.Month << 21) | ((dt.Year - 1980) << 25));
        }
        
        /* DOS Date and time:
            MS-DOS date. The date is a packed value with the following format. Bits Description
                0-4 Day of the month (131)
                5-8 Month (1 = January, 2 = February, and so on)
                9-15 Year offset from 1980 (add 1980 to get actual year)
            MS-DOS time. The time is a packed value with the following format. Bits Description
                0-4 Second divided by 2
                5-10 Minute (059)
                11-15 Hour (023 on a 24-hour clock)
        */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static DateTime DosTimeToDateTime(uint dt)
        {
            return new DateTime(
                (int)(dt >> 25) + 1980,
                (int)(dt >> 21) & 15,
                (int)(dt >> 16) & 31,
                (int)(dt >> 11) & 31,
                (int)(dt >> 5) & 63,
                (int)(dt & 31) * 2);
        }

        // Replaces backslashes with slashes to store in zip header. Remove unit letter
        private static string NormalizedFilename(string filename)
        {
            Span<char> filenameSpan = stackalloc char[filename.Length];
            filename.AsSpan().Replace(filenameSpan, '\\', '/');

            var pos = filenameSpan.IndexOf(':');
            if (pos >= 0)
                filenameSpan = filenameSpan[(pos + 1)..];

            return new string(filenameSpan.Trim('/'));
        }

        /// <summary>
        /// Reads the end-of-central-directory record
        /// </summary>
        /// <returns></returns>
        #if REPAIR
        private void ReadFileInfo(Stream zipFileStream)
        {
            if (zipFileStream.Length < 22)
                throw new InvalidDataException("Invalid ZIP file");

            try
            {
                zipFileStream.Seek(-15, SeekOrigin.End);
                var br = new BinaryReader(zipFileStream);
                do
                {
                    zipFileStream.Seek(-5, SeekOrigin.Current);
                    var sig = br.ReadUInt32();
                    if (sig == 0x06054b50)
                    {
                        zipFileStream.Seek(6, SeekOrigin.Current);

                        var entries = br.ReadUInt16();
                        var centralSize = br.ReadInt32();
                        var centralDirOffset = br.ReadUInt32();
                        
                        if (zipFileStream.Position != zipFileStream.Length)
                            throw new InvalidDataException("ZIP not corrupted!");
                        
                        // var commentSize = br.ReadUInt16();

                        // check if comment field is the very last data in file
                        // if (zipFileStream.Position + commentSize != zipFileStream.Length)
                        //    throw new InvalidDataException("Invalid ZIP file");

                        // Copy entire central directory to a memory buffer
                        _existingFiles = entries;
                        _centralDirImage = new byte[centralSize];
                        zipFileStream.Seek(centralDirOffset, SeekOrigin.Begin);
                        zipFileStream.ReadExactly(_centralDirImage);

                        // Leave the pointer at the begining of central dir, to append new files
                        zipFileStream.Seek(centralDirOffset, SeekOrigin.Begin);
                        return;
                    }
                } while (zipFileStream.Position > 0);

                throw new InvalidDataException("Invalid ZIP file");
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("Invalid ZIP file", ex);
            }
        }
        #else
        private void ReadFileInfo(Stream zipFileStream, bool readWholeZip = true)
        {
            if (zipFileStream.Length < 22)
                throw new InvalidDataException("Invalid ZIP file");

            try
            {
                if (readWholeZip)
                {
                    if (!ReadZipInfo(zipFileStream))
                    {
                        throw new InvalidDataException("Invalid ZIP file");
                    }
                }
                else
                {
                    zipFileStream.Seek(-17, SeekOrigin.End);
                    var br = new BinaryReader(zipFileStream);
                    do
                    {
                        zipFileStream.Seek(-5, SeekOrigin.Current);
                        var sig = br.ReadUInt32();
                        if (sig == 0x06054b50)
                        {
                            zipFileStream.Seek(6, SeekOrigin.Current);

                            var entries = br.ReadUInt16();
                            var centralSize = br.ReadInt32();
                            var centralDirOffset = br.ReadUInt32();
                            var commentSize = br.ReadUInt16();

                            // check if comment field is the very last data in file
                            if (zipFileStream.Position + commentSize != zipFileStream.Length)
                                throw new InvalidDataException("Invalid ZIP file");

                            // Copy entire central directory to a memory buffer
                            _existingFiles = entries;
                            _centralDirImage = new byte[centralSize];
                            zipFileStream.Seek(centralDirOffset, SeekOrigin.Begin);
                            zipFileStream.ReadExactly(_centralDirImage);

                            // Leave the pointer at the begining of central dir, to append new files
                            zipFileStream.Seek(centralDirOffset, SeekOrigin.Begin);
                            return;
                        }
                    } while (zipFileStream.Position > 0);
                }
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("Invalid ZIP file", ex);
            }
        }
        
        private bool ReadZipInfo(Stream zipStream)
        {
            byte[]? centralDirArray = null;                  // Central dir image
            try
            {
                //Find begin of End of central directory record (EOCD)
                zipStream.Seek(-17, SeekOrigin.End);

                int centralSize;
                uint centralDirOffset;
                {
                    var br = new BinaryReader(zipStream);
                    
                    uint sig;
                    do
                    {
                        zipStream.Seek(-5, SeekOrigin.Current);
                        sig = br.ReadUInt32();
                    } while (zipStream.Position > 0 && sig != 0x06054b50);

                    //Check that found End of central directory signature = 0x06054b50 
                    if (sig != 0x06054b50)
                    {
                        return false;
                    }

                    zipStream.Seek(6, SeekOrigin.Current);
                    var entries = br.ReadUInt16();
                    centralSize = br.ReadInt32();
                    centralDirOffset = br.ReadUInt32();
                    var commentSize = br.ReadUInt16();
                    var zipComment = br.ReadChars(commentSize);
                    _comment = new string(zipComment);

                    // _existingFiles = entries;
                }

                // Copy entire central directory to a memory buffer
                centralDirArray = centralSize >= 1024 ? ArrayPool<byte>.Shared.Rent(centralSize) : null;
                var centralDirImage = centralSize < 1024 ? stackalloc byte[centralSize] : centralDirArray;
                zipStream.Seek(centralDirOffset, SeekOrigin.Begin);
                zipStream.ReadExactly(centralDirImage);

                //Read ZipEntrys
                /* Central directory's File header:
                   central file header signature   4 bytes  (0x02014b50)
                   version made by                 2 bytes
                   version needed to extract       2 bytes
                   general purpose bit flag        2 bytes
                   compression method              2 bytes
                   last mod file time              2 bytes
                   last mod file date              2 bytes
                   crc-32                          4 bytes
                   compressed size                 4 bytes
                   uncompressed size               4 bytes
                   filename length                 2 bytes
                   extra field length              2 bytes
                   file comment length             2 bytes
                   disk number start               2 bytes
                   internal file attributes        2 bytes
                   external file attributes        4 bytes
                   relative offset of local header 4 bytes
                   filename (variable size)
                   extra field (variable size)
                   file comment (variable size)
                */
                var pointer = 0;
                {
                    var br = new SpanReader(centralDirImage);
                    while (pointer < centralDirImage.Length)
                    {
                        var signature = br.ReadUInt32();
                        if (signature != 0x02014b50)
                        {
                            break;
                        }

                        br.Position += 4; // skip version made by, version needed to extract

                        var zfe = new ZipFileEntry();

                        var encodeUTF8 = (br.ReadUInt16() & 0x0800) != 0; //True if UTF8 encoding for filename and comments, false if default (CP 437)
                        var encoder = encodeUTF8 ? Encoding.UTF8 : DefaultEncoding;
                        zfe.Method = (Compression)br.ReadUInt16();
                        zfe.ModifyTime = DosTimeToDateTime(br.ReadUInt32());
                        zfe.Crc32 = br.ReadUInt32();
                        zfe.CompressedSize = br.ReadUInt32();
                        zfe.FileSize = br.ReadUInt32();
                        var filenameSize = br.ReadUInt16();
                        var extraSize = br.ReadUInt16();
                        var fileCommentSize = br.ReadUInt16();
                        br.Position += 8; // skip disk number start, internal file attributes, external file attributes
                        zfe.HeaderOffset = br.ReadUInt32();
                        zfe.HeaderSize = (uint)(46 + filenameSize + extraSize + fileCommentSize);
                        zfe.FilenameInZip = encoder.GetString(centralDirImage.Slice(pointer + 46, filenameSize));

                        if (fileCommentSize > 0)
                        {
                            zfe.Comment = encoder.GetString(
                                centralDirImage.Slice(
                                    pointer + 46 + filenameSize + extraSize,
                                    fileCommentSize
                                )
                            );
                        }
                        else
                        {
                            zfe.Comment = string.Empty;
                        }

                        Files.Add(zfe);
                        pointer += (int)zfe.HeaderSize;
                    }
                }

                // Leave the pointer at the begining of central dir, to append new files
                zipStream.Seek(centralDirOffset, SeekOrigin.Begin);

                return true;
            }
            finally
            {
                if (centralDirArray != null) ArrayPool<byte>.Shared.Return(centralDirArray);
            }
        }
        #endif
        #endregion

        #region Unzip

        public Stream? OpenFile(ZipFileEntry zfe)
        {
            // check signature
            var signature = 0;
            _zipFileStream.Seek(zfe.HeaderOffset, SeekOrigin.Begin);
            _zipFileStream.ReadExactly(MemoryMarshal.Cast<int, byte>(MemoryMarshal.CreateSpan(ref signature, 1)));
            if (signature != 0x04034b50)
            {
                return null;
            }

            //Seek to begin of compress data
            var br = new BinaryReader(_zipFileStream);
            _zipFileStream.Seek(zfe.HeaderOffset + 26, SeekOrigin.Begin);
            var fileNameLength = br.ReadUInt16();
            var extraFieldLength = br.ReadUInt16();
            _zipFileStream.Seek(fileNameLength + extraFieldLength, SeekOrigin.Current);

            // Select input stream for inflating or just reading
            return zfe.Method switch
            {
                Compression.Store => new NoDisposeStream(_zipFileStream),
                Compression.Deflate => new DeflateStream(_zipFileStream, CompressionMode.Decompress, true),
                _ => null
            };
        }

        /// <summary>
        /// Copy the contents of a stored file into an opened stream
        /// </summary>
        /// <param name="zfe">Entry information of file to extract</param>
        /// <param name="outStream">Stream to store the uncompressed data</param>
        /// <param name="bufferSize">Buffer size to use when extracting</param>
        /// <returns>True if success, false if not.</returns>
        /// <remarks>Unique decompression methods are Store and Deflate</remarks>
        public bool ExtractFile(ZipFileEntry zfe, Stream outStream, int bufferSize = 1024*1024)
        {
            if (!outStream.CanWrite)
            {
                throw new InvalidOperationException("Stream cannot be written");
            }

            var deflatedStream = OpenFile(zfe);

            if (deflatedStream == null) return false;

            byte[]? buffer = null;
            try
            {
                //Inicialize CRC
                var crc32 = 0xffffffff;

                // Buffered copy
                buffer = ArrayPool<byte>.Shared.Rent(bufferSize);

                var bspan = buffer.AsSpan();
                var bytesPending = zfe.FileSize;
                while (bytesPending > 0)
                {
                    var bytesRead = deflatedStream.Read(buffer, 0, (int)Math.Min(bytesPending, buffer.Length));
                    crc32 = Polynomials.Crc32(crc32, bspan[..bytesRead]);
                    bytesPending -= (uint)bytesRead;
                    outStream.Write(buffer, 0, bytesRead);
                }

                outStream.Flush();

                //Verify data integrity
                crc32 ^= 0xffffffff;
                return zfe.Crc32 == crc32;
            }
            finally
            {
                if (zfe.Method == Compression.Deflate)
                {
                    deflatedStream.Dispose();
                }

                if (buffer != null)
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }
        
        /// <summary>
        /// Copy the contents of a stored file into an opened stream
        /// </summary>
        /// <param name="filename">Filename to extract</param>
        /// <param name="outStream">Stream to store the uncompressed data</param>
        /// <returns>True if success, false if not.</returns>
        /// <remarks>Unique decompression methods are Store and Deflate</remarks>
        public bool ExtractFile(string filename, Stream outStream)
        {
            var entry = Files.FindAll(name => name.FilenameInZip == filename);
            var zfe = entry[0];

            return ExtractFile(zfe, outStream);
        }

        /// <summary>
        /// Copy the contents of a stored file into an opened stream
        /// </summary>
        /// <param name="zipFilename">Name in ZIP to extract</param>
        /// <param name="outPathFilename">Name of file to store uncompressed data</param>
        /// <returns>True if success, false if not.</returns>
        /// <remarks>Unique decompression methods are Store and Deflate</remarks>
        public bool ExtractFile(string zipFilename, string outPathFilename)
        {
            var entry = Files.FindAll(name => name.FilenameInZip == zipFilename);
            var zfe = entry[0];

            return ExtractFile(zfe, outPathFilename);
        }

        /// <summary>
        /// Copy the contents of a stored file into a physical file
        /// </summary>
        /// <param name="zfe">Entry information of file to extract</param>
        /// <param name="outPathFilename">Name of file to store uncompressed data</param>
        /// <param name="createDirectory">Create the parent directory if not exists</param>
        /// <returns>True if success, false if not.</returns>
        /// <remarks>Unique decompression methods are Store and Deflate</remarks>
        public bool ExtractFile(ZipFileEntry zfe, string outPathFilename, bool createDirectory = true)
        {
            // Make sure the parent directory exist
            if (createDirectory)
            {
                var path = Path.GetDirectoryName(outPathFilename);
                if (path != null && !Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
            }

            // Check it is directory. If so, do nothing
            if (Directory.Exists(outPathFilename))
            {
                return true;
            }

            using (var output = new FileStream(outPathFilename, FileMode.Create, FileAccess.Write))
            {
                if (!ExtractFile(zfe, output))
                {
                    return false;
                }
            }

            //Change file datetimes
            // while (IsFileLocked(outPathFilename))
            // {
            //     MediaTypeNames.Application.DoEvents();
            // }
            File.SetCreationTime(outPathFilename, zfe.ModifyTime);
            File.SetLastWriteTime(outPathFilename, zfe.ModifyTime);

            return true;
        }

        /// <summary>
        /// Test the contents of a stored file
        /// </summary>
        /// <param name="zfe">Entry information of file to extract</param>
        /// <returns>True if success, false if not.</returns>
        /// <remarks>Unique decompression methods are Store and Deflate</remarks>
        public bool TestFile(ZipFileEntry zfe) => ExtractFile(zfe, Stream.Null);

        public unsafe bool Recompress(int compressionLevel = 6, bool throwOnFailure = false)
        {
            foreach (var zipFileEntry in Files)
            {
                Console.WriteLine(zipFileEntry.FilenameInZip);
            }
            
            var files = new (ZipFileEntry Entry, nuint Data)[Files.Count];
            try
            {
                var i = 0;
                foreach (var zfe in Files)
                {
                    if (!ExtractToMemory(zfe, out var mem, throwOnFailure))
                    {
                        return false;
                    }

                    files[i++] = (zfe, (nuint)mem);
                }

                _zipFileStream.Seek(0, SeekOrigin.Begin);
                Files.Clear();
                
                // Truncate the rest of the file
                _zipFileStream.SetLength(0);
                _zipFileStream.Flush();

                foreach (var (zfe, data) in files)
                {
                    AddBuffer(
                        new ReadOnlySpan<byte>((void*)data, (int)zfe.FileSize),
                        zfe.FilenameInZip,
                        zfe.ModifyTime,
                        compressionLevel,
                        zfe.Comment
                    );
                }
                return true;
            }
            finally
            {
                foreach (var (_, data) in files)
                {
                    NativeMemory.Free((void*)data);
                }
            }
        }

        private unsafe bool ExtractToMemory(ZipFileEntry zfe, out byte* mem, bool throwOnFailure = false)
        {
            mem = (byte*)NativeMemory.Alloc(zfe.FileSize);
            try
            {
                var stream = new UnmanagedMemoryStream(mem, 0, zfe.FileSize, FileAccess.Write);

                using (var deflatedStream = OpenFile(zfe))
                {
                    if (deflatedStream == null) return throwOnFailure
                        ? throw new InvalidOperationException($"{nameof(OpenFile)} failed! Either mismatching signature or unsupported compression method")
                        : false;
                    deflatedStream.CopyTo(stream);
                }

                var crc32 = UnsafeNativeMethods.GetCrc32(new ReadOnlySpan<byte>(mem, (int)zfe.FileSize));
                var crcMatches = zfe.Crc32 == crc32;
                return throwOnFailure
                    ? crcMatches ? true : throw new InvalidOperationException("CRC mismatch")
                    : crcMatches;
            }
            catch
            {
                NativeMemory.Free(mem);
                mem = null;
                throw;
            }
        }

        #endregion

        #region IDisposable Members
        /// <summary>
        /// Closes the Zip file stream
        /// </summary>
        public void Dispose()
        {
            Close();
        }
        #endregion
    }
}