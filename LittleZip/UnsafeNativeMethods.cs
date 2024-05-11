using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;

[assembly: DisableRuntimeMarshalling]

namespace System.IO.Compression;

/// <summary>
/// Zopfli Options
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ZopfliOptions
{
    // Whether to print output
    public int verbose;

    // Whether to print more detailed output
    public int verbose_more;

    // Maximum amount of times to rerun forward and backward pass to optimize LZ77
    // compression cost. Good values: 10, 15 for small files, 5 for files over
    // several MB in size or it will be too slow.
    public int numiterations;

    // If true, splits the data in multiple deflate blocks with optimal choice
    // for the block boundaries. Block splitting gives better compression. Default:
    // true (1).
    public int blocksplitting;

    // If true, chooses the optimal block split points only after doing the iterative
    // LZ77 compression. If false, chooses the block split points first, then does
    // iterative LZ77 on each individual block. Depending on the file, either first
    // or last gives the best compression. Default: false (0).
    public int blocksplittinglast;

    // Maximum amount of blocks to split into (0 for unlimited, but this can give
    // extreme results that hurt compression on some files). Default value: 15.
    public int blocksplittingmax;

    /// <summary>
    /// Initializes options used throughout the program with default values.
    /// </summary>
    public ZopfliOptions()
    {
        verbose = 0;
        verbose_more = 0;
        numiterations = 15;
        blocksplitting = 1;
        blocksplittinglast = 0;
        blocksplittingmax = 15;
    }
}

/// <summary>
/// Zopfli format options
/// </summary>
public enum ZopfliFormat
{
    Zopfli_Format_Gzip,
    Zopfli_Format_Zlib,
    Zopfli_Format_Deflate
}

internal readonly ref struct Handle(IntPtr data, uint len, bool zopfli = false)
{
    public Handle(IntPtr data, uint len) : this(data, len, true)
    {
    }

    public unsafe Span<byte> Data => new((void*)data, (int)len);

    public void Dispose()
    {
        if (zopfli)
        {
            if (data != IntPtr.Zero)
            {
                UnsafeNativeMethods.ZopfliFree(data);
            }
        }
        else
        {
            if (data != IntPtr.Zero)
            {
                unsafe
                {
                    NativeMemory.Free((void*)data);
                }
            }
        }
    }
}

[SuppressUnmanagedCodeSecurity]
internal sealed partial class UnsafeNativeMethods
{
    /// <summary>
    /// Deflate array with Zopfli
    /// </summary>
    /// <param name="inBuffer">Data to deflate</param>
    /// <param name="compressionLevel">Maximum amount of times to rerun forward and backward pass to optimize LZ77 (high = more compression and more slow)</param>
    /// <param name="outBuffer">Data deflated</param>
    /// <param name="deflatedSize">Size of deflated data</param>
    /// <param name="crc32">CRC of deflated data</param>
    public static void ZopfliDeflate(
        ReadOnlySpan<byte> inBuffer, int compressionLevel, out Handle outBuffer, out uint deflatedSize, out uint crc32
    )
    {
        const ZopfliFormat type = ZopfliFormat.Zopfli_Format_Deflate;
        var options = new ZopfliOptions();

        //Get CRC32
        crc32 = GetCrc32(inBuffer);

        // Compress the data via native methods
        options.numiterations = compressionLevel - 5;
        ZopfliCompress(options, type, inBuffer, inBuffer.Length, out var result, out var resultSize);

        // if compresed size is less that uncompresed size, copy data back to managed memory and return
        if ((int)resultSize < inBuffer.Length)
        {
            deflatedSize = (uint)resultSize;
            outBuffer = new Handle(result, (uint)resultSize);
        }
        else
        {
            deflatedSize = 0;
            outBuffer = default;
        }
    }

    /// <summary>
    /// Internal convert method to convert byte array to compressed byte array
    /// </summary>
    /// <param name="options">Compression options</param>
    /// <param name="output_type">Format type, DEFLATE, GZIP, ZLIB</param>
    /// <param name="inBuffer">Uncompressed data array</param>
    /// 
    public static unsafe void ZopfliCompress(
        ZopfliOptions options, ZopfliFormat output_type, ReadOnlySpan<byte> inBuffer, int dataInSize, out IntPtr dataOut,
        out UIntPtr dataOutSize
    )
    {
        fixed (byte* inPtr = inBuffer)
        {
            switch (IntPtr.Size)
            {
                case 4:
                    zopfli_x86(options, output_type, (IntPtr)inPtr, dataInSize, out dataOut, out dataOutSize);
                    break;
                case 8:
                    zopfli_x64(options, output_type, (IntPtr)inPtr, dataInSize, out dataOut, out dataOutSize);
                    break;
                default:
                    throw new InvalidOperationException("Invalid platform. Can not find proper function");
            }
        }
    }

    [LibraryImport("zopfli_x86.dll", EntryPoint = "ZopfliCompress")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void zopfli_x86(
        ZopfliOptions options, ZopfliFormat output_type, IntPtr inBuffer, int dataInSize, out IntPtr dataOut,
        out UIntPtr dataOutSize
    );

    [LibraryImport("zopfli_x64.dll", EntryPoint = "ZopfliCompress")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void zopfli_x64(
        ZopfliOptions options, ZopfliFormat output_type, IntPtr inBuffer, int dataInSize, out IntPtr dataOut,
        out UIntPtr dataOutSize
    );


    /// <summary>
    /// Frees memory allocated by the native Zopfli library.
    /// </summary>
    /// <param name="mem">Pointer to the unmanaged memory to free</param>
    internal static void ZopfliFree(IntPtr mem)
    {
        switch (IntPtr.Size)
        {
            case 4:
                ZopfliFree_x86(mem);
                break;
            case 8:
                ZopfliFree_x64(mem);
                break;
            default:
                throw new InvalidOperationException("Invalid platform. Can not find proper function");
        }
    }

    [LibraryImport("zopfli_x86.dll", EntryPoint = "ZopfliFree")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void ZopfliFree_x86(IntPtr mem);

    [LibraryImport("zopfli_x64.dll", EntryPoint = "ZopfliFree")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void ZopfliFree_x64(IntPtr mem);
    
// }
//
//
// [SuppressUnmanagedCodeSecurity]
// internal sealed partial class UnsafeNativeMethods
// {

    /// <summary>
    /// Make CRC-32 checksum
    /// </summary>
    /// <param name="buffer">Data to checksum</param>
    /// <returns>The updated checksum</returns>
    public static unsafe uint GetCrc32(ReadOnlySpan<byte> buffer)
    {
        fixed (byte* ptrBuffer = buffer)
        {
            return LibdeflateCrc32(0, (IntPtr)ptrBuffer, buffer.Length);
        }
    }


    /// <summary>
    /// Deflate array 
    /// </summary>
    /// <param name="inBuffer">Data to deflate</param>
    /// <param name="compressionLevel">The compression level on a zlib-like scale but with a higher maximum value (1 = fastest, 6 = medium/default, 9 = slow, 13 = slowest)</param>
    /// <param name="force"></param>
    /// <param name="outBuffer">Data deflated</param>
    /// <param name="deflatedSize">Size of deflated data</param>
    /// <param name="crc32">CRC of deflated data</param>
    public static unsafe void Libdeflate(ReadOnlySpan<byte> inBuffer, int compressionLevel, bool force, out Handle outBuffer, out uint deflatedSize, out uint crc32)
    {
        //Get ptrInBuffer
        fixed (byte* ptrInBuffer = inBuffer)
        {
            //Allocate compressor
            var compressor = LibdeflateAllocCompressor(compressionLevel);
            if (compressor == IntPtr.Zero)
                throw new Exception("Out of memory");

            //Get CRC32
            crc32 = LibdeflateCrc32(0, (IntPtr)ptrInBuffer, inBuffer.Length);

            //Allocate output buffer
            var maxCompresedSize = force
                ? LibdeflateDeflateCompressBound(compressor, inBuffer.Length)
                : inBuffer.Length;

            var outBufferBuffer = NativeMemory.Alloc((UIntPtr)maxCompresedSize);

            //compress
            deflatedSize = (uint)LibdeflateDeflateCompress(compressor, (IntPtr)ptrInBuffer, inBuffer.Length, (IntPtr)outBufferBuffer, maxCompresedSize);

            outBuffer = new Handle((IntPtr)outBufferBuffer, deflatedSize, false);
            
            //Free resources
            LibdeflateFreeCompressor(compressor);
        }

    }

    /* ========================================================================== */
    /*                             Compression                                    */
    /* ========================================================================== */

    /// <summary>
    /// Allocates a new compressor that supports DEFLATE, zlib, and gzip compression.
    ///
    /// Note: for compression, the sliding window size is defined at compilation time
    /// to 32768, the largest size permissible in the DEFLATE format. It cannot be
    /// changed at runtime.
    /// 
    /// A single compressor is not safe to use by multiple threads concurrently.
    /// However, different threads may use different compressors concurrently.
    /// </summary>
    /// <param name="compression_level">The compression level on a zlib-like scale but with a higher maximum value (1 = fastest, 6 = medium/default, 9 = slow, 13 = slowest)</param>
    /// <returns>Pointer to the new compressor, or NULL if out of memory.</returns>
    public static IntPtr LibdeflateAllocCompressor(int compression_level)
    {
        return IntPtr.Size switch
        {
            4 => libdeflate_alloc_compressor_x86(compression_level),
            8 => libdeflate_alloc_compressor_x64(compression_level),
            _ => throw new InvalidOperationException("Invalid platform. Can not find proper function")
        };
    }
    [LibraryImport("libdeflate_x86.dll", EntryPoint = "libdeflate_alloc_compressor")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial IntPtr libdeflate_alloc_compressor_x86(int compression_level);
    [LibraryImport("libdeflate_x64.dll", EntryPoint = "libdeflate_alloc_compressor")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial IntPtr libdeflate_alloc_compressor_x64(int compression_level);

    /// <summary>Performs raw DEFLATE in the ZLIB format compression on a buffer of data.</summary>
    /// <param name="compressor">Pointer to the compressor</param>
    /// <param name="inData">Data to compress</param>
    /// <param name="in_nbytes">Length of data to compress</param>
    /// <param name="outBuffer">Data compresed</param>
    /// <param name="out_nbytes_avail">Leght of buffer for data compresed</param>
    /// <returns>Compressed size in bytes, or 0 if the data could not be compressed to 'out_nbytes_avail' bytes or fewer.</returns>
    public static long LibdeflateZlibCompress(IntPtr compressor, IntPtr inBuffer, int in_nbytes, IntPtr outBuffer, int out_nbytes_avail)
    {
        return IntPtr.Size switch
        {
            4 => (long)(libdeflate_zlib_compress_x86(
                compressor,
                inBuffer,
                (UIntPtr)in_nbytes,
                outBuffer,
                (UIntPtr)out_nbytes_avail
            )),
            8 => (long)(libdeflate_zlib_compress_x64(
                compressor,
                inBuffer,
                (UIntPtr)in_nbytes,
                outBuffer,
                (UIntPtr)out_nbytes_avail
            )),
            _ => throw new InvalidOperationException("Invalid platform. Can not find proper function")
        };
    }
    [LibraryImport("libdeflate_x86.dll", EntryPoint = "libdeflate_zlib_compress")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial UIntPtr libdeflate_zlib_compress_x86(IntPtr compressor, IntPtr inBuffer, UIntPtr in_nbytes, IntPtr outBuffer, UIntPtr out_nbytes_avail);
    [LibraryImport("libdeflate_x64.dll", EntryPoint = "libdeflate_zlib_compress")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial UIntPtr libdeflate_zlib_compress_x64(IntPtr compressor, IntPtr inBuffer, UIntPtr in_nbytes, IntPtr outBuffer, UIntPtr out_nbytes_avail);

    /// <summary>Performs raw DEFLATE compression on a buffer of data.</summary>
    /// <param name="compressor">Pointer to the compressor</param>
    /// <param name="inData">Data to compress</param>
    /// <param name="in_nbytes">Length of data to compress</param>
    /// <param name="outData">Data compresed</param>
    /// <param name="out_nbytes_avail">Leght of buffer for data compresed</param>
    /// <returns>Compressed size in bytes, or 0 if the data could not be compressed to 'out_nbytes_avail' bytes or fewer.</returns>
    public static long LibdeflateDeflateCompress(IntPtr compressor, IntPtr inBuffer, int in_nbytes, IntPtr outBuffer, int out_nbytes_avail)
    {
        return IntPtr.Size switch
        {
            4 => (long)(libdeflate_deflate_compress_x86(
                compressor,
                inBuffer,
                (UIntPtr)in_nbytes,
                outBuffer,
                (UIntPtr)out_nbytes_avail
            )),
            8 => (long)(libdeflate_deflate_compress_x64(
                compressor,
                inBuffer,
                (UIntPtr)in_nbytes,
                outBuffer,
                (UIntPtr)out_nbytes_avail
            )),
            _ => throw new InvalidOperationException("Invalid platform. Can not find proper function")
        };
    }
    [LibraryImport("libdeflate_x86.dll", EntryPoint = "libdeflate_deflate_compress")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial UIntPtr libdeflate_deflate_compress_x86(IntPtr compressor, IntPtr inBuffer, UIntPtr in_nbytes, IntPtr outBuffer, UIntPtr out_nbytes_avail);
    [LibraryImport("libdeflate_x64.dll", EntryPoint = "libdeflate_deflate_compress")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial UIntPtr libdeflate_deflate_compress_x64(IntPtr compressor, IntPtr inBuffer, UIntPtr in_nbytes, IntPtr outBuffer, UIntPtr out_nbytes_avail);

    /// <summary>Performs raw GZIP compression on a buffer of data.</summary>
    /// <param name="compressor">Pointer to the compressor</param>
    /// <param name="inData">Data to compress</param>
    /// <param name="in_nbytes">Length of data to compress</param>
    /// <param name="outData">Data compresed</param>
    /// <param name="out_nbytes_avail">Leght of buffer for data compresed</param>
    /// <returns>Compressed size in bytes, or 0 if the data could not be compressed to 'out_nbytes_avail' bytes or fewer.</returns>
    public static long LibdeflateGzipCompress(IntPtr compressor, IntPtr inBuffer, int in_nbytes, IntPtr outBuffer, int out_nbytes_avail)
    {
        return IntPtr.Size switch
        {
            4 => (long)(libdeflate_gzip_compress_x86(
                compressor,
                inBuffer,
                (UIntPtr)in_nbytes,
                outBuffer,
                (UIntPtr)out_nbytes_avail
            )),
            8 => (long)(libdeflate_gzip_compress_x64(
                compressor,
                inBuffer,
                (UIntPtr)in_nbytes,
                outBuffer,
                (UIntPtr)out_nbytes_avail
            )),
            _ => throw new InvalidOperationException("Invalid platform. Can not find proper function")
        };
    }
    [LibraryImport("libdeflate_x86.dll", EntryPoint = "libdeflate_gzip_compress")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial UIntPtr libdeflate_gzip_compress_x86(IntPtr compressor, IntPtr inBuffer, UIntPtr in_nbytes, IntPtr outBuffer, UIntPtr out_nbytes_avail);
    [LibraryImport("libdeflate_x64.dll", EntryPoint = "libdeflate_gzip_compress")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial UIntPtr libdeflate_gzip_compress_x64(IntPtr compressor, IntPtr inBuffer, UIntPtr in_nbytes, IntPtr outBuffer, UIntPtr out_nbytes_avail);

    /// <summary>Get the worst-case upper bound on the number of bytes of compressed data that may be produced
    /// by compressing any buffer of length less than or equal to 'in_nbytes'.
    /// Mathematically, this bound will necessarily be a number greater than or equal to 'in_nbytes'.
    /// It may be an overestimate of the true upper bound.  
    /// As a special case, 'compressor' may be NULL.  This causes the bound to be taken across *any*
    /// libdeflate_compressor that could ever be allocated with this build of the library, with any options.
    /// 
    /// With block-based compression, it is usually preferable to separately store the uncompressed size of each
    /// block and to store any blocks that did not compress to less than their original size uncompressed.  In that
    /// scenario, there is no need to know the worst-case compressed size, since the maximum number of bytes of
    /// compressed data that may be used would always be one less than the input length.  You can just pass a
    /// buffer of that size to libdeflate_deflate_compress() and store the data uncompressed if libdeflate_deflate_compress()
    /// returns 0, indicating that the compressed data did not fit into the provided output buffer.</summary>
    /// <param name="compressor">Pointer to the compressor</param>
    /// <param name="in_nbytes">Length of data to compress</param>
    /// <returns>Worst-case upper bound on the number of bytes of compressed data that may be produced by compressing any buffer of length less than or equal to 'in_nbytes'.</returns>
    public static int LibdeflateZlibCompressBound(IntPtr compressor, int in_nbytes)
    {
        return IntPtr.Size switch
        {
            4 => (int)libdeflate_zlib_compress_bound_x86(compressor, (UIntPtr)in_nbytes),
            8 => (int)libdeflate_zlib_compress_bound_x64(compressor, (UIntPtr)in_nbytes),
            _ => throw new InvalidOperationException("Invalid platform. Can not find proper function")
        };
    }
    [LibraryImport("libdeflate_x86.dll", EntryPoint = "libdeflate_zlib_compress_bound")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial UIntPtr libdeflate_zlib_compress_bound_x86(IntPtr compressor, UIntPtr in_nbytes);
    [LibraryImport("libdeflate_x64.dll", EntryPoint = "libdeflate_zlib_compress_bound")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial UIntPtr libdeflate_zlib_compress_bound_x64(IntPtr compressor, UIntPtr in_nbytes);

    /// <summary>Returns a worst-case upper bound on the number of bytes of compressed data that may be produced
    /// by compressing any buffer of length less than or equal to 'in_nbytes'.
    /// Mathematically, this bound will necessarily be a number greater than or equal to 'in_nbytes'.
    /// It may be an overestimate of the true upper bound.  
    /// As a special case, 'compressor' may be NULL.  This causes the bound to be taken across *any*
    /// libdeflate_compressor that could ever be allocated with this build of the library, with any options.
    /// </summary>
    /// <param name="compressor">Pointer to the compressor</param>
    /// <param name="in_nbytes">Length of data to compress</param>
    /// <returns>Worst-case upper bound on the number of bytes of compressed data that may be produced by compressing any buffer of length less than or equal to 'in_nbytes'.</returns>
    public static int LibdeflateDeflateCompressBound(IntPtr compressor, int in_nbytes)
    {
        return IntPtr.Size switch
        {
            4 => (int)libdeflate_deflate_compress_bound_x86(compressor, (UIntPtr)in_nbytes),
            8 => (int)libdeflate_deflate_compress_bound_x64(compressor, (UIntPtr)in_nbytes),
            _ => throw new InvalidOperationException("Invalid platform. Can not find proper function")
        };
    }
    [LibraryImport("libdeflate_x86.dll", EntryPoint = "libdeflate_deflate_compress_bound")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial UIntPtr libdeflate_deflate_compress_bound_x86(IntPtr compressor, UIntPtr in_nbytes);
    [LibraryImport("libdeflate_x64.dll", EntryPoint = "libdeflate_deflate_compress_bound")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial UIntPtr libdeflate_deflate_compress_bound_x64(IntPtr compressor, UIntPtr in_nbytes);

    /// <summary>Returns a worst-case upper bound on the number of bytes of compressed data that may be produced
    /// by compressing any buffer of length less than or equal to 'in_nbytes'.
    /// Mathematically, this bound will necessarily be a number greater than or equal to 'in_nbytes'.
    /// It may be an overestimate of the true upper bound.  
    /// As a special case, 'compressor' may be NULL.  This causes the bound to be taken across *any*
    /// libdeflate_compressor that could ever be allocated with this build of the library, with any options.
    /// 
    /// With block-based compression, it is usually preferable to separately store the uncompressed size of each
    /// block and to store any blocks that did not compress to less than their original size uncompressed.  In that
    /// scenario, there is no need to know the worst-case compressed size, since the maximum number of bytes of
    /// compressed data that may be used would always be one less than the input length.  You can just pass a
    /// buffer of that size to libdeflate_deflate_compress() and store the data uncompressed if libdeflate_deflate_compress()
    /// returns 0, indicating that the compressed data did not fit into the provided output buffer.</summary>
    /// <param name="compressor">Pointer to the compressor</param>
    /// <param name="in_nbytes">Length of data to compress</param>
    /// <returns>Worst-case upper bound on the number of bytes of compressed data that may be produced by compressing any buffer of length less than or equal to 'in_nbytes'.</returns>
    public static int LibdeflateGzipCompressBound(IntPtr compressor, int in_nbytes)
    {
        return IntPtr.Size switch
        {
            4 => (int)libdeflate_gzip_compress_bound_x86(compressor, (UIntPtr)in_nbytes),
            8 => (int)libdeflate_gzip_compress_bound_x64(compressor, (UIntPtr)in_nbytes),
            _ => throw new InvalidOperationException("Invalid platform. Can not find proper function")
        };
    }
    [LibraryImport("libdeflate_x86.dll", EntryPoint = "libdeflate_gzip_compress_bound")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial UIntPtr libdeflate_gzip_compress_bound_x86(IntPtr compressor, UIntPtr in_nbytes);
    [LibraryImport("libdeflate_x64.dll", EntryPoint = "libdeflate_gzip_compress_bound")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial UIntPtr libdeflate_gzip_compress_bound_x64(IntPtr compressor, UIntPtr in_nbytes);

    /// <summary>Frees a compressor that was allocated with libdeflate_alloc_compressor()</summary>
    /// <param name="compressor">Pointer to the compressor</param>
    public static void LibdeflateFreeCompressor(IntPtr compressor)
    {
        switch (IntPtr.Size)
        {
            case 4:
                libdeflate_free_compressor_x86(compressor);
                break;
            case 8:
                libdeflate_free_compressor_x64(compressor);
                break;
            default:
                throw new InvalidOperationException("Invalid platform. Can not find proper function");
        }
    }
    [LibraryImport("libdeflate_x86.dll", EntryPoint = "libdeflate_free_compressor")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void libdeflate_free_compressor_x86(IntPtr compressor);
    [LibraryImport("libdeflate_x64.dll", EntryPoint = "libdeflate_free_compressor")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void libdeflate_free_compressor_x64(IntPtr compressor);


    /* ========================================================================== */
    /*                                Checksums                                   */
    /* ========================================================================== */

    /// <summary>Updates a running CRC-32 checksum</summary>
    /// <param name="crc">Inial value of checksum. When starting a new checksum will be 0</param>
    /// <param name="inBuffer">Data to checksum</param>
    /// <param name="len">Length of data</param>
    /// <returns>The updated checksum</returns>
    public static uint LibdeflateCrc32(uint crc, IntPtr inBuffer, int len)
    {
        return IntPtr.Size switch
        {
            4 => libdeflate_crc32_x86(crc, inBuffer, (UIntPtr)len),
            8 => libdeflate_crc32_x64(crc, inBuffer, (UIntPtr)len),
            _ => throw new InvalidOperationException("Invalid platform. Can not find proper function")
        };
    }
    [LibraryImport("libdeflate_x86.dll", EntryPoint = "libdeflate_crc32")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial uint libdeflate_crc32_x86(uint crc, IntPtr inBuffer, UIntPtr len);
    [LibraryImport("libdeflate_x64.dll", EntryPoint = "libdeflate_crc32")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial uint libdeflate_crc32_x64(uint crc, IntPtr inBuffer, UIntPtr len);

    /// <summary>Updates a running Adler-32 checksum</summary>
    /// <param name="crc">Inial value of checksum. When starting a new checksum will be 1</param>
    /// <param name="inBuffer">Data to checksum</param>
    /// <param name="len">Length of data</param>
    /// <returns>The updated checksum</returns>
    public static uint LibdeflateAdler32(uint crc, IntPtr inBuffer, int len)
    {
        return IntPtr.Size switch
        {
            4 => libdeflate_adler32_x86(crc, inBuffer, (UIntPtr)len),
            8 => libdeflate_adler32_x64(crc, inBuffer, (UIntPtr)len),
            _ => throw new InvalidOperationException("Invalid platform. Can not find proper function")
        };
    }
    
    [LibraryImport("libdeflate_x86.dll", EntryPoint = "libdeflate_adler32")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial uint libdeflate_adler32_x86(uint crc, IntPtr inBuffer, UIntPtr len);
    [LibraryImport("libdeflate_x64.dll", EntryPoint = "libdeflate_adler32")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial uint libdeflate_adler32_x64(uint crc, IntPtr inBuffer, UIntPtr len);
}