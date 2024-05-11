using System.Collections.Generic;
using System.Text;

namespace System.IO.Compression;

    /// <summary>Unique class for decompression file. Represents a Zip file.</summary>
    public sealed class LittleUnZip : IDisposable
    {
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

        /// <summary>
        /// Represents an entry in Zip file directory
        /// </summary>
        public struct ZipFileEntry
        {
            /// <summary>Compression method</summary>
            public Compression method;
            /// <summary>Full path and filename as stored in Zip</summary>
            public string filename;
            /// <summary>Original file size</summary>
            public uint fileSize;
            /// <summary>Compressed file size</summary>
            public uint compressedSize;
            /// <summary>Offset of header information inside Zip storage</summary>
            public uint headerOffset;
            /// <summary>Offset of file inside Zip storage</summary>
            public int headerSize;
            /// <summary>32-bit checksum of entire file</summary>
            private uint _crc32;
            /// <summary>Last modification time of file</summary>
            public DateTime modifyTime;
            /// <summary>User comment for file</summary>
            public string comment;
            /// <summary>True if UTF8 encoding for filename and comments, false if default (CP 437)</summary>
            public bool encodeUTF8;

            public uint Crc32
            {
                get { return _crc32; }
                set { _crc32 = value; }
            }

            /// <summary>Overriden method</summary>
            /// <returns>Filename in Zip</returns>
            public override string ToString()
            {
                return filename;
            }
        }

        #region Public fields
        public List<ZipFileEntry> zipFileEntrys = new List<ZipFileEntry>();         // List of files data
        public string zipComment;                                                   // Coment of ZIP file
        #endregion

        #region Private fields
        private Stream _zipFileStream;           // Stream object of storage file
        private string _zipFileName;             // Stream object of storage file
        private ushort _zipFiles;            // number of files in zip
        #endregion

        #region | Constructors |
        /// <summary>
        /// Create zip object
        /// </summary>
        /// <param name="filename">Full path of Zip file to open</param>
        public LittleUnZip(string filename)
        {
            _zipFileName = filename;
            using (Stream zipStream = new FileStream(filename, FileMode.Open, FileAccess.Read))
            {
                if (!ReadZipInfo(zipStream))
                {
                    throw new InvalidDataException();
                }
            }
        }

        /// <summary>
        /// Create zip object
        /// </summary>
        /// <param name="stream">Already opened stream with zip contents</param>
        public LittleUnZip(Stream stream)
        {
            if (!stream.CanSeek)
            {
                throw new InvalidOperationException("Stream cannot seek");
            }

            _zipFileStream = stream;
            if (!ReadZipInfo(stream))
            {
                throw new InvalidDataException();
            }
        }
        #endregion

        #region IDisposable Members
        /// <summary>
        /// Closes the Zip file stream
        /// </summary>
        public void Dispose()
        {
            if (_zipFileStream != null)
            {
                _zipFileStream.Close();
                _zipFileStream.Dispose();
                _zipFileStream = null;
            }
        }
        #endregion

        #region Public methods
        /// <summary>Text zip integrity</summary>
        /// <returns>True if zip file is ok</returns>
        public bool Test()
        {
            try
            {
                for (var f = 0; f < zipFileEntrys.Count; f++)
                {
                    if (!TestFile(zipFileEntrys[f]))
                    {
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex) { throw new Exception(ex.Message + "\r\nIn ZipExtract.Test"); }
        }

        /// <summary>
        /// Copy the contents of all stored files into a folder
        /// </summary>
        /// <param name="outputFolder">Destination folder</param>
        /// <param name="dirs">if TRUE, recreate struct directories</param>
        /// <param name="progressBar">Progress bar of extract process</param>
        /// <remarks>Unique decompression methods are Store and Deflate</remarks>
        public void Extract(string outputFolder, bool dirs)
        {
            try
            {
                string path;

                for (var f = 0; f < zipFileEntrys.Count; f++)
                {
                    if (dirs)
                    {
                        path = outputFolder + Path.DirectorySeparatorChar + zipFileEntrys[f].filename;
                        path = path.Replace('/', Path.DirectorySeparatorChar);
                    }
                    else
                    {
                        path = Path.Combine(outputFolder, Path.GetFileName(zipFileEntrys[f].filename));
                    }

                    if (!ExtractFile(zipFileEntrys[f], path))
                    {
                        throw new Exception("Can´t extract " + zipFileEntrys[f]);
                    }

                    //Update progress bar
                    // if (progressBar != null)
                    // {
                    //     progressBar.Value = (int)(progressBar.Maximum * (this.zipFileEntrys.IndexOf(this.zipFileEntrys[f]) + 1) / this.zipFileEntrys.Count);
                    //     MediaTypeNames.Application.DoEvents();
                    // }
                }
            }
            catch (InvalidDataException) { throw new InvalidDataException("Bad zip file."); }
            catch (Exception ex) { throw new Exception(ex.Message + "\r\nIn ZipExtract.ExtractAll"); }
        }

        /// <summary>
        /// Copy the contents of a stored file into a physical file
        /// </summary>
        /// <param name="zfe">Entry information of file to extract</param>
        /// <param name="outPathFilename">Name of file to store uncompressed data</param>
        /// <returns>True if success, false if not.</returns>
        /// <remarks>Unique decompression methods are Store and Deflate</remarks>
        public bool ExtractFile(ZipFileEntry zfe, string outPathFilename)
        {
            Stream output = null;

            try
            {
                // Make sure the parent directory exist
                var path = Path.GetDirectoryName(outPathFilename);
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }

                // Check it is directory. If so, do nothing
                if (Directory.Exists(outPathFilename))
                {
                    return true;
                }

                //Delete file to create
                if (File.Exists(outPathFilename))
                {
                    try
                    {
                        File.Delete(outPathFilename);
                    }
                    catch
                    {
                        throw new InvalidOperationException("File '" + outPathFilename + "' cannot be written");
                    }
                }

                output = new FileStream(outPathFilename, FileMode.Create, FileAccess.Write);
                if (!ExtractFile(zfe, output))
                {
                    return false;
                }

                //Change file datetimes
                output.Close();
                // while (IsFileLocked(outPathFilename))
                // {
                //     MediaTypeNames.Application.DoEvents();
                // }
                File.SetCreationTime(outPathFilename, zfe.modifyTime);
                File.SetLastWriteTime(outPathFilename, zfe.modifyTime);

                return true;
            }
            catch (Exception ex) { throw new Exception(ex.Message + "\r\nIn ZipExtract.ExtractFile"); }
            finally
            {
                if (output != null)
                {
                    output.Close();
                    output.Dispose();
                }
            }
        }

        /// <summary>
        /// Copy the contents of a stored file into an opened stream
        /// </summary>
        /// <param name="zfe">Entry information of file to extract</param>
        /// <param name="outStream">Stream to store the uncompressed data</param>
        /// <returns>True if success, false if not.</returns>
        /// <remarks>Unique decompression methods are Store and Deflate</remarks>
        public bool ExtractFile(ZipFileEntry zfe, Stream outStream)
        {
            Stream zipStream;

            try
            {
                if (_zipFileStream != null)
                {
                    zipStream = _zipFileStream;
                }
                else
                {
                    zipStream = new FileStream(_zipFileName, FileMode.Open, FileAccess.Read);
                }

                if (!outStream.CanWrite)
                {
                    throw new InvalidOperationException("Stream cannot be written");
                }

                // check signature
                var signature = new byte[4];
                zipStream.Seek(zfe.headerOffset, SeekOrigin.Begin);
                zipStream.Read(signature, 0, 4);
                if (BitConverter.ToUInt32(signature, 0) != 0x04034b50)
                {
                    return false;
                }

                //Seek to begin of compress data
                var br = new BinaryReader(zipStream);
                zipStream.Seek(zfe.headerOffset + 26, SeekOrigin.Begin);
                var fileNameLength = br.ReadUInt16();
                var extraFieldLength = br.ReadUInt16();
                zipStream.Seek(fileNameLength + extraFieldLength, SeekOrigin.Current);

                // Select input stream for inflating or just reading
                Stream deflatedStream;
                if (zfe.method == Compression.Store)
                {
                    deflatedStream = zipStream;
                }
                else if (zfe.method == Compression.Deflate)
                {
                    deflatedStream = new DeflateStream(zipStream, CompressionMode.Decompress, true);
                }
                else
                {
                    return false;
                }

                //Inicialize CRC
                var crc32 = 0xffffffff;

                // Buffered copy
                var buffer = new byte[16384];
                var bytesPending = zfe.fileSize;
                while (bytesPending > 0)
                {
                    var bytesRead = deflatedStream.Read(buffer, 0, (int)Math.Min(bytesPending, buffer.Length));
                    crc32 = Crc32(crc32, ref buffer, bytesRead);
                    bytesPending -= (uint)bytesRead;
                    outStream.Write(buffer, 0, bytesRead);
                }
                outStream.Flush();

                //Close streams
                if (zfe.method == Compression.Deflate)
                {
                    deflatedStream.Dispose();
                }
                if (_zipFileStream == null)
                {
                    zipStream.Dispose();
                }

                //Verify data integrity
                crc32 ^= 0xffffffff;
                if (zfe.Crc32 != crc32)
                {
                    return false;
                }
                return true;
            }
            catch (Exception ex) { throw new Exception(ex.Message + "\r\nIn ZipExtract.ExtractFile"); }
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
            try
            {
                var entry = zipFileEntrys.FindAll(name => name.filename == filename);
                var zfe = entry[0];

                return ExtractFile(zfe, outStream);
            }
            catch (Exception ex) { throw new Exception(ex.Message + "\r\nIn ZipExtract.ExtractFile"); }
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
            try
            {
                var entry = zipFileEntrys.FindAll(name => name.filename == zipFilename);
                var zfe = entry[0];

                return ExtractFile(zfe, outPathFilename);
            }
            catch (Exception ex) { throw new Exception(ex.Message + "\r\nIn ZipExtract.ExtractFile"); }
        }

        /// <summary>
        /// Test the contents of a stored file
        /// </summary>
        /// <param name="zfe">Entry information of file to extract</param>
        /// <returns>True if success, false if not.</returns>
        /// <remarks>Unique decompression methods are Store and Deflate</remarks>
        public bool TestFile(ZipFileEntry zfe)
        {
            try
            {
                if (!ExtractFile(zfe, Stream.Null))
                {
                    return false;
                }
                return true;
            }
            catch (Exception ex) { throw new Exception(ex.Message + "\r\nIn ZipExtract.TestFile"); }
        }
        #endregion

        #region Private methods
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

        // Reads the end-of-central-directory record and entrys
        private bool ReadZipInfo(Stream zipStream)
        {
            byte[] centralDirImage = null;                  // Central dir image
            try
            {
                if (zipStream.Length < 22)
                {
                    return false;
                }

                //Find begin of End of central directory record (EOCD)
                zipStream.Seek(-17, SeekOrigin.End);
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
                var centralSize = br.ReadInt32();
                var centralDirOffset = br.ReadUInt32();
                var commentSize = br.ReadUInt16();
                var zipComment = br.ReadChars(commentSize);
                this.zipComment = new string(zipComment);

                // Copy entire central directory to a memory buffer
                _zipFiles = entries;
                centralDirImage = new byte[centralSize];
                zipStream.Seek(centralDirOffset, SeekOrigin.Begin);
                zipStream.Read(centralDirImage, 0, centralSize);
                br.Close();

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
                while (pointer < centralDirImage.Length)
                {
                    var signature = BitConverter.ToUInt32(centralDirImage, pointer);
                    if (signature != 0x02014b50)
                    {
                        break;
                    }

                    var zfe = new ZipFileEntry();

                    var encodeUTF8 = (BitConverter.ToUInt16(centralDirImage, pointer + 8) & 0x0800) != 0; //True if UTF8 encoding for filename and comments, false if default (CP 437)
                    var encoder = encodeUTF8 ? Encoding.UTF8 : Encoding.GetEncoding(437);
                    zfe.method = (Compression)BitConverter.ToUInt16(centralDirImage, pointer + 10);
                    zfe.modifyTime = DosTimeToDateTime(BitConverter.ToUInt32(centralDirImage, pointer + 12));
                    zfe.Crc32 = BitConverter.ToUInt32(centralDirImage, pointer + 16);
                    zfe.compressedSize = BitConverter.ToUInt32(centralDirImage, pointer + 20);
                    zfe.fileSize = BitConverter.ToUInt32(centralDirImage, pointer + 24);
                    var filenameSize = BitConverter.ToUInt16(centralDirImage, pointer + 28);
                    var extraSize = BitConverter.ToUInt16(centralDirImage, pointer + 30);
                    var fileCommentSize = BitConverter.ToUInt16(centralDirImage, pointer + 32);
                    zfe.headerOffset = BitConverter.ToUInt32(centralDirImage, pointer + 42);
                    zfe.headerSize = 46 + filenameSize + extraSize + fileCommentSize;
                    zfe.filename = encoder.GetString(centralDirImage, pointer + 46, filenameSize);

                    if (fileCommentSize > 0)
                    {
                        zfe.comment = encoder.GetString(centralDirImage, pointer + 46 + filenameSize + extraSize, fileCommentSize);
                    }

                    zipFileEntrys.Add(zfe);
                    pointer += zfe.headerSize;
                }

                return true;
            }
            catch (Exception ex) { throw new Exception(ex.Message + "\r\nEn ZipExtract.ReadZipInfo"); }
        }

        /// <summary>Check if the file is locked</summary>
        /// <param name="pathFileName">File to check</param>
        /// <returns>True if the file is locked</returns>
        private static bool IsFileLocked(string pathFileName)
        {
            try
            {
                //File exist?
                if (!File.Exists(pathFileName))
                {
                    return false;
                }

                //if can open, not is blocked
                var myFile = File.Open(pathFileName, FileMode.Open, FileAccess.Write, FileShare.None);
                myFile.Close();

                return false;
            }
            catch
            {
                return true;
            }
        }
        #endregion

        #region CRC32 methods
        private static ReadOnlySpan<uint> CrcTable => Polynomials.CrcTable;

        /// <summary>
        /// Make CRC 32
        /// </summary>
        /// <param name="crc32">Actual CRC32 value</param>
        /// <param name="data">Data to process</param>
        /// <param name="length">Length of data</param>
        /// <returns>New CRC32 value</returns>
        private static uint Crc32(uint crc32, ref byte[] data, int length)
        {
            var offset = 0;
            var table = CrcTable;

            //Process block of 16 bytes
            while (length >= 16)
            {
                var a = table[(3 * 256) + data[offset + 12]]
                    ^ table[(2 * 256) + data[offset + 13]]
                    ^ table[(1 * 256) + data[offset + 14]]
                    ^ table[(0 * 256) + data[offset + 15]];

                var b = table[(7 * 256) + data[offset + 8]]
                    ^ table[(6 * 256) + data[offset + 9]]
                    ^ table[(5 * 256) + data[offset + 10]]
                    ^ table[(4 * 256) + data[offset + 11]];

                var c = table[(11 * 256) + data[offset + 4]]
                    ^ table[(10 * 256) + data[offset + 5]]
                    ^ table[(9 * 256) + data[offset + 6]]
                    ^ table[(8 * 256) + data[offset + 7]];
                var d = table[(15 * 256) + ((byte)crc32 ^ data[offset])]
                    ^ table[(14 * 256) + ((byte)(crc32 >> 8) ^ data[offset + 1])]
                    ^ table[(13 * 256) + ((byte)(crc32 >> 16) ^ data[offset + 2])]
                    ^ table[(int)((12 * 256) + ((crc32 >> 24) ^ data[offset + 3]))];

                crc32 = d ^ c ^ b ^ a;
                offset += 16;
                length -= 16;
            }

            //Process remain bytes
            while (--length >= 0)
            {
                crc32 = table[(int)((crc32 ^ data[offset++]) & 0xff)] ^ crc32 >> 8;
            }

            //return result
            return crc32;
        }
        #endregion
    }