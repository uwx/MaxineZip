using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System.IO.Compression;

public ref struct SpanReader
{
#if NET6_0_OR_GREATER && DISABLED
	delegate decimal DecimalToDecimal(ReadOnlySpan<byte> span);
	private static readonly DecimalToDecimal _decimalToDecimal = (DecimalToDecimal)typeof(decimal)
			  .GetMethod("ToDecimal", BindingFlags.Static | BindingFlags.NonPublic, new[] { typeof(ReadOnlySpan<byte>) })
			  .CreateDelegate(typeof(DecimalToDecimal));
	
	delegate decimal CreateDecimal(int lo, int mid, int hi, int flags);
	private static CreateDecimal _createDecimal;

	private static void InitializeCreateDecimal()
	{
		var paramLo = Expression.Parameter(typeof(int), "lo");
		var paramMid = Expression.Parameter(typeof(int), "mi");
		var paramHi = Expression.Parameter(typeof(int), "hi");
		var paramFlags = Expression.Parameter(typeof(int), "flags");

		var ctor = typeof(Decimal).GetConstructor(
			BindingFlags.Instance | BindingFlags.NonPublic,
			null,
			new[] { typeof(int), typeof(int), typeof(int), typeof(int) },
			null);

		var lambda = Expression.Lambda<CreateDecimal>(
			Expression.New(ctor, paramLo, paramMid, paramHi, paramFlags), paramLo, paramMid, paramHi, paramFlags);

		_createDecimal = lambda.Compile();
	}
#endif

	/// <summary>
	/// Reads a boolean value from the current binary stream and advances the current position within the stream by one byte.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool ReadBoolean() => InternalReadByte() != 0;

	/// <summary>
	/// Reads the next byte from the current binary stream and advances the current position within the stream by one byte.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public byte ReadByte() => InternalReadByte();

	/// <summary>
	/// Reads a decimal value from the current binary stream and advances the current position within the stream by sixteen bytes.
	/// </summary>
	public unsafe decimal ReadDecimal()
	{
		var span = InternalReadSpan(16);
		try
		{
			if (BitConverter.IsLittleEndian)
			{
				return new decimal(
#if NET6_0_OR_GREATER
				stackalloc
#else
				new
#endif
				[]
				{
					Unsafe.ReadUnaligned<int>(ref MemoryMarshal.GetReference(span)),          // lo
					Unsafe.ReadUnaligned<int>(ref MemoryMarshal.GetReference(span[4..])), // mid
					Unsafe.ReadUnaligned<int>(ref MemoryMarshal.GetReference(span[8..])), // hi
					Unsafe.ReadUnaligned<int>(ref MemoryMarshal.GetReference(span[12..])) // flags
				});
			}
			else
			{
				return new decimal(
#if NET6_0_OR_GREATER
				stackalloc
#else
				new
#endif
				[]
				{
					BinaryPrimitives.ReverseEndianness(Unsafe.ReadUnaligned<int>(ref MemoryMarshal.GetReference(span))),          // lo
					BinaryPrimitives.ReverseEndianness(Unsafe.ReadUnaligned<int>(ref MemoryMarshal.GetReference(span[4..]))), // mid
					BinaryPrimitives.ReverseEndianness(Unsafe.ReadUnaligned<int>(ref MemoryMarshal.GetReference(span[8..]))), // hi
					BinaryPrimitives.ReverseEndianness(Unsafe.ReadUnaligned<int>(ref MemoryMarshal.GetReference(span[12..]))) // flags
				});
			}
		}
		catch (ArgumentException e)
		{
			// ReadDecimal cannot leak out ArgumentException
			throw ExceptionHelper.DecimalReadingException(e);
		}
	}

	/// <summary>
	/// Reads single-precision floating-point number from the current binary stream and advances the current position within the stream by four bytes.
	/// </summary>
	public float ReadSingle()
	{
		var span = InternalReadSpan(4);
		return Unsafe.ReadUnaligned<float>(ref MemoryMarshal.GetReference(span));
	}

	/// <summary>
	/// Reads a double-precision floating-point number from the current binary stream and advances the current position within the stream by eight bytes.
	/// </summary>
	public double ReadDouble()
	{
		var span = InternalReadSpan(8);
		return Unsafe.ReadUnaligned<double>(ref MemoryMarshal.GetReference(span));
	}

	/// <summary>
	/// Reads a 16-bit signed integer from the current binary stream and advances the current position within the stream by two bytes.
	/// </summary>
	public short ReadInt16()
	{
		var span = InternalReadSpan(2);
		return Unsafe.ReadUnaligned<short>(ref MemoryMarshal.GetReference(span));
	}

	/// <summary>
	/// Reads a 32-bit signed integer from the current binary stream and advances the current position within the stream by four bytes.
	/// </summary>
	public int ReadInt32()
	{
		var span = InternalReadSpan(4);
		return Unsafe.ReadUnaligned<int>(ref MemoryMarshal.GetReference(span));
	}

	/// <summary>
	/// Reads a 64-bit signed integer from the current binary stream and advances the current position within the stream by eight bytes.
	/// </summary>
	public long ReadInt64()
	{
		var span = InternalReadSpan(8);
		return Unsafe.ReadUnaligned<long>(ref MemoryMarshal.GetReference(span));
	}
	
	/// <summary>
	/// Reads a signed byte from the current binary stream and advances the current position within the stream by one byte.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public sbyte ReadSByte() => (sbyte)InternalReadByte();

	/// <summary>
	/// Reads a 16-bit unsigned integer from the current binary stream and advances the current position within the stream by two bytes.
	/// </summary>
	public ushort ReadUInt16()
	{
		var span = InternalReadSpan(2);
		return Unsafe.ReadUnaligned<ushort>(ref MemoryMarshal.GetReference(span));
	}

	/// <summary>
	/// Reads a 32-bit unsigned integer from the current binary stream and advances the current position within the stream by four bytes.
	/// </summary>
	public uint ReadUInt32()
	{
		var span = InternalReadSpan(4);
		return Unsafe.ReadUnaligned<uint>(ref MemoryMarshal.GetReference(span));
	}

	/// <summary>
	/// Reads 64-bit unsigned integer from the current binary stream and advances the current position within the stream by eight bytes.
	/// </summary>
	public ulong ReadUInt64()
	{
		var span = InternalReadSpan(8);
		return Unsafe.ReadUnaligned<ulong>(ref MemoryMarshal.GetReference(span));
	}

	private readonly ReadOnlySpan<byte> _data;
	private readonly int _length;
	private int _position;

	/// <summary>
	/// Gets the offset into the underlying <see cref="ReadOnlySpan{T}"/> to start reading from.
	/// </summary>
	public int Offset => 0;

	/// <summary>
	/// Gets the effective length of the readable region of the underlying <see cref="ReadOnlySpan{T}"/>.
	/// </summary>
	public int Length => _length;

	/// <summary>
	/// Gets or sets the current reading position within the underlying <see cref="ReadOnlySpan{T}"/>.
	/// </summary>
	public int Position
	{
		get => _position;
		set
		{
			if (value < 0) throw ExceptionHelper.PositionLessThanZeroException(nameof(value));
			if (value > _length) throw ExceptionHelper.PositionGreaterThanLengthOfReadOnlyMemoryException(nameof(value));

			_position = value;
		}
	}

	/// <summary>
	/// Gets the amount of bytes remaining until end of the buffer.
	/// </summary>
	public int Remaining => _length - _position;

	/// <summary>
	/// Initializes a new instance of <see cref="BinaryBufferReader"/> based on the specified <see cref="ReadOnlyMemory{T}"/>.
	/// </summary>
	/// <param name="data">The input <see cref="ReadOnlyMemory{T}"/>.</param>
	/// 
	public SpanReader(in ReadOnlySpan<byte> data)
	{
		_data = data;
		_position = 0;
		_length = data.Length;
	}

	/// <summary>
	/// Reads the specified number of bytes from the current binary stream into a byte array and advances the current position within the stream by that number of bytes.
	/// </summary>
	/// <param name="count">The number of bytes to read.</param>
	public byte[] ReadBytes(int count) => InternalReadSpan(count).ToArray();

	/// <summary>
	/// Reads a span of bytes from the current binary stream and advances the current position within the stream by the number of bytes read.
	/// </summary>
	/// <param name="count">The number of bytes to read.</param>
	public ReadOnlySpan<byte> ReadSpan(int count) => InternalReadSpan(count);

	/// <summary>
	/// Reads the specified number of bytes from the current binary stream, starting from a specified point in the byte array.
	/// </summary>
	/// <returns>
	/// The number of bytes read into buffer. This might be less than the number of bytes requested if that many bytes are not available, or it might be zero if the end of the stream is reached.
	/// </returns>
	public int Read(byte[] buffer, int index, int count)
	{
		if (count <= 0)
			return 0;

		var relPos = _position + count;

		if ((uint)relPos > (uint)_length)
		{
			count = relPos - _length;
		}
		if (count <= 0)
			return 0;

		var span = InternalReadSpan(count);
		span.CopyTo(buffer.AsSpan(index, count));

		return count;
	}


	/// <summary>
	/// Reads the next byte from the underlying <see cref="ReadOnlyMemory{T}"/> and advances the current position by one byte.
	/// </summary>
	private byte InternalReadByte()
	{
		var curPos = _position;
		var newPos = curPos + 1;

		if ((uint)newPos > (uint)_length)
		{
			_position = _length;
			throw ExceptionHelper.EndOfDataException();
		}

		_position = newPos;

		return _data[curPos];
	}

	/// <summary>
	/// Returns a read-only span over the specified number of bytes from the underlying <see cref="ReadOnlyMemory{T}"/> and advances the current position by that number of bytes.
	/// </summary>
	/// <param name="count">The size of the read-only span to return.</param>
	private ReadOnlySpan<byte> InternalReadSpan(int count)
	{
		if (count <= 0)
			return ReadOnlySpan<byte>.Empty;

		var curPos = _position;
		var newPos = curPos + count;

		if ((uint)newPos > (uint)_length)
		{
			_position = _length;
			throw ExceptionHelper.EndOfDataException();
		}

		_position = newPos;

		return _data.Slice(curPos, count);
	}
}

file static class ExceptionHelper
{
	public static ArgumentOutOfRangeException PositionLessThanZeroException(string positionParameterName, string positionWord = "Position (zero-based)")
	{
		return new ArgumentOutOfRangeException(positionParameterName, $"{positionWord} must be greater than or equal to zero.");
	}

	public static ArgumentOutOfRangeException PositionGreaterThanLengthOfReadOnlyMemoryException(string positionParameterName)
	{
		return PositionGreaterThanDataStreamLengthException(positionParameterName, "Position (zero-based)", "read-only memory");
	}

	private static ArgumentOutOfRangeException PositionGreaterThanDataStreamLengthException(string positionParameterName, string positionWord, string dataStreamType)
	{
		return new ArgumentOutOfRangeException(positionParameterName, $"{positionWord} must be equal to or less than the size of the underlying {dataStreamType}.");
	}
	

	public static EndOfStreamException EndOfDataException()
	{
		return new EndOfStreamException("Reached to end of data");
	}

	public static IOException DecimalReadingException(ArgumentException argumentException)
	{
		return new IOException("Failed to read decimal value", argumentException);
	}
}