using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace FlacBox
{
    /// <summary>
    /// Stream for encoding/decoding WAVE data into FLAC container.
    /// </summary>
    public class WaveOverFlacStream : Stream
    {        
        IEnumerator<ArraySegment<byte>> dataSource;
        IEnumerator<WriteRequest> dataConsumer;
        WaveOverFlacStreamMode mode;

        public WaveOverFlacStream(Stream stream, WaveOverFlacStreamMode mode)
            : this(stream, mode, false, FlacWriter.DefaultEncodingPolicyLevel)
        {
        }

        public WaveOverFlacStream(Stream stream, WaveOverFlacStreamMode mode, bool leaveOpen)
            : this(stream, mode, leaveOpen, FlacWriter.DefaultEncodingPolicyLevel)
        {
        }

        public WaveOverFlacStream(Stream stream, WaveOverFlacStreamMode mode, bool leaveOpen, int compressionLevel)
        {
            if (stream == null) throw new ArgumentNullException("stream");
            ValidateCompressionLevel(compressionLevel);

            this.mode = mode;
            switch (mode)
            {
                case WaveOverFlacStreamMode.Decode:
                    this.dataSource = ReadFlac(new FlacReader(stream, leaveOpen)); break;
                case WaveOverFlacStreamMode.Encode:
                    this.dataConsumer = WriteFlac(new FlacWriter(stream, leaveOpen), compressionLevel);
                    this.dataConsumer.MoveNext();
                    break;
            }
        }

        private static void ValidateCompressionLevel(int compressionLevel)
        {
            if (compressionLevel < FlacWriter.MinEncodingPolicyLevel ||
                compressionLevel > FlacWriter.MaxEncodingPolicyLevel)
                throw new ArgumentOutOfRangeException("compressionLevel");
        }

        public WaveOverFlacStream(FlacReader reader)
        {
            if (reader == null) throw new ArgumentNullException("reader");

            this.dataSource = ReadFlac(reader);
            this.mode = WaveOverFlacStreamMode.Decode;
            EnsureHeaderRead();
        }

        public WaveOverFlacStream(FlacWriter writer)
            : this(writer, FlacWriter.DefaultEncodingPolicyLevel)
        {
        }

        public WaveOverFlacStream(FlacWriter writer, int compressionLevel)
        {
            if (writer == null) throw new ArgumentNullException("writer");
            ValidateCompressionLevel(compressionLevel);

            this.dataConsumer = WriteFlac(writer, compressionLevel);
            this.dataConsumer.MoveNext();
            this.mode = WaveOverFlacStreamMode.Encode;
        }

        public override bool CanRead
        {
            get { return mode == WaveOverFlacStreamMode.Decode; }
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public override bool CanWrite
        {
            get { return mode == WaveOverFlacStreamMode.Encode; }
        }

        public override void Flush()
        {
            if(mode != WaveOverFlacStreamMode.Encode)
                throw new NotImplementedException();
        }

        public override long Length
        {
            get 
            {
                if (mode != WaveOverFlacStreamMode.Decode)
                    throw new NotImplementedException();

                EnsureHeaderRead();

                return totalStreamLength; 
            }
        }

        public override long Position
        {
            get
            {
                throw new NotImplementedException();
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (count < 0) throw new IndexOutOfRangeException();

            if (!CanRead) throw new NotSupportedException();

            EnsureHeaderRead();

            if (currentData.Count >= count)
            {
                Array.Copy(currentData.Array, currentData.Offset, buffer, offset, count);
                currentData = new ArraySegment<byte>(currentData.Array, currentData.Offset + count, currentData.Count - count);
                return count;
            }
            else
            {
                int read = currentData.Count;
                Array.Copy(currentData.Array, currentData.Offset, buffer, offset, currentData.Count);
                currentData = NoCurrentData;

                while (dataSource.MoveNext())
                {
                    int rest = count - read;
                    if (dataSource.Current.Count >= rest)
                    {
                        Array.Copy(dataSource.Current.Array, 0, buffer, offset + read, rest);
                        read += rest;
                        currentData = new ArraySegment<byte>(dataSource.Current.Array, rest, dataSource.Current.Count - rest);
                        break;
                    }
                    else
                    {
                        Array.Copy(dataSource.Current.Array, 0, buffer, offset + read, dataSource.Current.Count);
                        read += dataSource.Current.Count;
                    }
                }
                return read;
            }
        }

        private static ArraySegment<byte> NoCurrentData = new ArraySegment<byte>(new byte[0]);

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException("count");

            if (!CanWrite) throw new NotSupportedException();

            dataConsumer.Current.Data = buffer;
            dataConsumer.Current.Offset = offset;
            dataConsumer.Current.Count = count;

            dataConsumer.MoveNext();
        }

        bool disposed = false;

        protected override void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (dataSource != null)
                {
                    dataSource.Dispose();
                    dataSource = null;
                }
                if (dataConsumer != null)
                {
                    dataConsumer.Dispose();
                    dataConsumer = null;
                }

                disposed = true;
            }

            base.Dispose(disposing);
        }

        bool headerRead = false;

        private void EnsureHeaderRead()
        {
            if (!headerRead)
            {
                if (!dataSource.MoveNext())
                    throw new FlacException("Stream is empty");

                currentData = dataSource.Current;
                headerRead = true;
            }
        }

        ArraySegment<byte> currentData;
        FlacStreaminfo streaminfo;
        long totalStreamLength;

        private IEnumerator<ArraySegment<byte>> ReadFlac(FlacReader reader)
        {
            try
            {
                while (reader.Read())
                {
                    if (reader.RecordType == FlacRecordType.MetadataBlock &&
                        reader.MetadataBlockType == FlacMetadataBlockType.Streaminfo)
                    {
                        streaminfo = reader.Streaminfo;
                        ValidateBitPerSample(streaminfo.BitsPerSample);
                        ValidateTotalSamples(streaminfo.TotalSampleCount);
                        InitializeHelpers(reader);

                        byte[] waveHeader = CreateWaveHeader();

                        yield return new ArraySegment<byte>( waveHeader );
                    }
                    else if (reader.RecordType == FlacRecordType.Frame)
                    {
                        yield return ReadFrame(reader);
                    }
                }
            }
            finally
            {
                reader.Close();
            }
        }

        WaveSampleTransformer sampleTransform;
        byte[] pcmBuffer;

        private void InitializeHelpers(FlacReader reader)
        {
            sampleTransform = WaveSampleTransformerFactory.CreateWaveSampleTransformer(reader.Streaminfo.BitsPerSample);

            int bufferSize = 
                reader.Streaminfo.MaxBlockSize * ((reader.Streaminfo.ChannelsCount * reader.Streaminfo.BitsPerSample) >> 3);
            pcmBuffer = new byte[bufferSize];
        }

        private ArraySegment<byte> ReadFrame(FlacReader reader)
        {
            int read = sampleTransform.PackData(reader.GetValues(), pcmBuffer);           
            return new ArraySegment<byte>(pcmBuffer, 0, read);
        }

        private void ValidateTotalSamples(long totalSamples)
        {
            if (totalSamples < 1)
                throw new FlacException("Totals samples cannot be unknown");
        }

        private byte[] CreateWaveHeader()
        {
            const int FmtChunkOffset = 12;
            const int DataChunkOffset = 36;
            const int WaveHeaderSize = DataChunkOffset + 8;

            int bytesPerInterChannelSample = (streaminfo.ChannelsCount * streaminfo.BitsPerSample) >> 3;
            long dataLength = streaminfo.TotalSampleCount * bytesPerInterChannelSample;
            this.totalStreamLength = dataLength + WaveHeaderSize;

            byte[] waveHeader = new byte[WaveHeaderSize];
            waveHeader[0] = (byte)'R'; waveHeader[1] = (byte)'I'; waveHeader[2] = (byte)'F'; waveHeader[3] = (byte)'F';
            Array.Copy(BitConverter.GetBytes((uint)totalStreamLength - 8), 0, waveHeader, 4, 4);
            waveHeader[8] = (byte)'W'; waveHeader[9] = (byte)'A'; waveHeader[10] = (byte)'V'; waveHeader[11] = (byte)'E';

            waveHeader[FmtChunkOffset + 0] = (byte)'f'; waveHeader[FmtChunkOffset + 1] = (byte)'m'; waveHeader[FmtChunkOffset + 2] = (byte)'t'; waveHeader[FmtChunkOffset + 3] = (byte)' ';
            waveHeader[FmtChunkOffset + 4] = 16; // + 5 - 7 zeros
            waveHeader[FmtChunkOffset + 8] = 1; // padding, + 9 zero
            waveHeader[FmtChunkOffset + 10] = (byte)streaminfo.ChannelsCount; // + 11 zero
            Array.Copy(BitConverter.GetBytes(streaminfo.SampleRate), 0, waveHeader, FmtChunkOffset + 12, 4);
            int bytesPerSecond = streaminfo.SampleRate * bytesPerInterChannelSample;
            Array.Copy(BitConverter.GetBytes(bytesPerSecond), 0, waveHeader, FmtChunkOffset + 16, 4);
            Array.Copy(BitConverter.GetBytes((ushort)bytesPerInterChannelSample), 0, waveHeader, FmtChunkOffset + 20, 2);
            Array.Copy(BitConverter.GetBytes((ushort)streaminfo.BitsPerSample), 0, waveHeader, FmtChunkOffset + 22, 2);

            waveHeader[DataChunkOffset + 0] = (byte)'d'; waveHeader[DataChunkOffset + 1] = (byte)'a'; waveHeader[DataChunkOffset + 2] = (byte)'t'; waveHeader[DataChunkOffset + 3] = (byte)'a';
            Array.Copy(BitConverter.GetBytes((uint)dataLength), 0, waveHeader, DataChunkOffset + 4, 4);

            return waveHeader;
        }

        private static void ValidateBitPerSample(int bitPerSample)
        {
            if (bitPerSample != 8 && bitPerSample != 16 && bitPerSample != 24 && bitPerSample != 32)
            {
                throw new FlacException("Unsupported bit per sample");
            }
        }

        private IEnumerator<WriteRequest> WriteFlac(FlacWriter writer, int compressionLevel)
        {
            try
            {
                const int RiffHeaderLength = 12;
                const int RiffBlockHeaderLength = 8;
                const int MinFormatLength = 16;
                const int MaxFormatLength = 128;


                WriteRequest request = new WriteRequest();

                byte[] waveHeader = new byte[RiffHeaderLength];
                ArraySegment<byte> buffer = new ArraySegment<byte>(waveHeader);
                do
                {
                    yield return request;
                    if (!request.IsDataPresent) 
                        throw new FlacException("RIFF header is expected");

                } while (FillBuffer(ref buffer, request));

                if (waveHeader[0] != 'R' || waveHeader[1] != 'I' || waveHeader[2] != 'F' || waveHeader[3] != 'F' ||
                    waveHeader[8] != 'W' || waveHeader[9] != 'A' || waveHeader[10] != 'V' || waveHeader[11] != 'E')
                    throw new FlacException("RIFF and WAVE header are expected");

                long totalStreamLength = BitConverter.ToUInt32(waveHeader, 4);
                
                do
                {
                    byte[] blockHeader = new byte[RiffBlockHeaderLength];
                    buffer = new ArraySegment<byte>(blockHeader);
                    while (FillBuffer(ref buffer, request))
                    {
                        yield return request;
                        if (!request.IsDataPresent)
                            throw new FlacException("RIFF block expected");
                    }

                    if (blockHeader[0] == 'f' && blockHeader[1] == 'm' && blockHeader[2] == 't' && blockHeader[3] == ' ')
                    {
                        int formatBlockSize = BitConverter.ToInt32(blockHeader, 4);
                        if (formatBlockSize < MinFormatLength || formatBlockSize > MaxFormatLength)
                            throw new FlacException("Invalid format block size");

                        byte[] formatBlock = new byte[formatBlockSize];
                        buffer = new ArraySegment<byte>(formatBlock);

                        while (FillBuffer(ref buffer, request))
                        {
                            yield return request;
                            if (!request.IsDataPresent)
                                throw new FlacException("format block expected");
                        }

                        if(BitConverter.ToUInt16(formatBlock, 0) != 1)
                                throw new FlacException("Unsupported alignment in WAVE");

                        FlacStreaminfo streaminfo = new FlacStreaminfo();
                        streaminfo.ChannelsCount = BitConverter.ToUInt16(formatBlock, 2);
                        streaminfo.SampleRate = BitConverter.ToInt32(formatBlock, 4);
                        streaminfo.BitsPerSample = BitConverter.ToUInt16(formatBlock, 14);
                        streaminfo.MinBlockSize = FlacCommons.DefaultBlockSize;
                        streaminfo.MaxBlockSize = FlacCommons.DefaultBlockSize;

                        EstimateMinAndMaxFrameSize(streaminfo);

                        this.streaminfo = streaminfo;
                    }
                    else if (blockHeader[0] == 'd' && blockHeader[1] == 'a' && blockHeader[2] == 't' && blockHeader[3] == 'a')
                    {
                        uint dataBlockSize = BitConverter.ToUInt32(blockHeader, 4);
                        if (streaminfo == null)
                            throw new FlacException("Format block was not found");

                        int bytesPerInterChannelSample = (streaminfo.ChannelsCount * streaminfo.BitsPerSample) >> 3;

                        long totalSamples = dataBlockSize / bytesPerInterChannelSample;
                        streaminfo.TotalSampleCount = totalSamples;

                        sampleTransform = WaveSampleTransformerFactory.CreateWaveSampleTransformer(streaminfo.BitsPerSample);

                        try
                        {
                            writer.StartStream(streaminfo);

                            int samplesInBuffer = streaminfo.MaxBlockSize;
                            pcmBuffer = new byte[bytesPerInterChannelSample * samplesInBuffer];
                            long currentSample = 0;
                            int[] samples = new int[streaminfo.ChannelsCount * samplesInBuffer];
                            while (currentSample + samplesInBuffer <= totalSamples)
                            {
                                buffer = new ArraySegment<byte>(pcmBuffer);
                                while (FillBuffer(ref buffer, request))
                                {
                                    yield return request;
                                    if (!request.IsDataPresent)
                                        throw new FlacException("data block expected");
                                }
                                sampleTransform.UnpackData(pcmBuffer, samples);
                                writer.WriteSamples(samples);
                                currentSample += samplesInBuffer;
                            }

                            if (currentSample < totalSamples)
                            {
                                int samplesLeft = (int)(totalSamples - currentSample);
                                buffer = new ArraySegment<byte>(pcmBuffer, 0, bytesPerInterChannelSample * samplesLeft);
                                while (FillBuffer(ref buffer, request))
                                {
                                    yield return request;
                                    if (!request.IsDataPresent)
                                        throw new FlacException("data block expected");
                                }
                                samples = new int[streaminfo.ChannelsCount * samplesLeft];
                                sampleTransform.UnpackData(pcmBuffer, samples);
                                writer.WriteSamples(samples);
                            }
                        }
                        finally
                        {
                            writer.EndStream();
                        }
                        break;
                    }
                    else // otherwise skip
                    {
                        uint dataBlockSize = BitConverter.ToUInt32(blockHeader, 4);
                        byte[] extraData = new byte[(int)dataBlockSize];
                        buffer = new ArraySegment<byte>(extraData);
                        while (FillBuffer(ref buffer, request))
                        {
                            yield return request;
                            if (!request.IsDataPresent)
                                throw new FlacException("extra data is expected");
                        }
                    }
                } while (request.IsDataPresent);
            }
            finally
            {
                writer.Close();
            }
        }

        private static void EstimateMinAndMaxFrameSize(FlacStreaminfo streaminfo)
        {
            // unknown
            streaminfo.MinFrameSize = 0;
            streaminfo.MaxFrameSize = 0;
        }

        private bool FillBuffer(ref ArraySegment<byte> buffer, WriteRequest currentRequest)
        {
            if (buffer.Count > currentRequest.Count)
            {
                Array.Copy(currentRequest.Data, currentRequest.Offset, buffer.Array, buffer.Offset, currentRequest.Count);
                buffer = new ArraySegment<byte>(buffer.Array, buffer.Offset + currentRequest.Count, buffer.Count - currentRequest.Count);
                currentRequest.Data = null;
                currentRequest.Count = 0;
                return true;
            }
            else
            {
                Array.Copy(currentRequest.Data, currentRequest.Offset, buffer.Array, buffer.Offset, buffer.Count);
                currentRequest.Offset += buffer.Count;
                currentRequest.Count -= buffer.Count;
                buffer = new ArraySegment<byte>(buffer.Array, buffer.Offset + buffer.Count, 0);
                return false;
            }
        }

        class WriteRequest
        {
            public byte[] Data;
            public int Offset;
            public int Count = 0;

            public bool IsDataPresent
            {
                get { return Count > 0; }
            }
        }
    }

    public enum WaveOverFlacStreamMode
    {
        Decode,
        Encode
    }
}


/**************************************************************************
    From some MSDN sample:	 
    A wave RIFF file is put together like this:
	 
    The 12 byte RIFF chunk is constructed like this:
    Bytes 0 - 3 :	'R' 'I' 'F' 'F'
    Bytes 4 - 7 :	Length of file, minus the first 8 bytes of the RIFF description.
                    (4 bytes for "WAVE" + 24 bytes for format chunk length +
                    8 bytes for data chunk description + actual sample data size.)
    Bytes 8 - 11:	'W' 'A' 'V' 'E'
	
    The 24 byte FORMAT chunk is constructed like this:
    Bytes 0 - 3 :	'f' 'm' 't' ' '
    Bytes 4 - 7 :	The format chunk length. This is always 16.
    Bytes 8 - 9 :	File padding. Always 1.
    Bytes 10- 11:	Number of channels. Either 1 for mono,  or 2 for stereo.
    Bytes 12- 15:	Sample rate.
    Bytes 16- 19:	Number of bytes per second.
    Bytes 20- 21:	Bytes per sample. 1 for 8 bit mono, 2 for 8 bit stereo or
                    16 bit mono, 4 for 16 bit stereo.
    Bytes 22- 23:	Number of bits per sample.
	
    The DATA chunk is constructed like this:
    Bytes 0 - 3 :	'd' 'a' 't' 'a'
    Bytes 4 - 7 :	Length of data, in bytes.
    Bytes 8 -...:	Actual sample data.
	
***************************************************************************/
