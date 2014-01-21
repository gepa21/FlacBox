using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace FlacBox
{
    /// <summary>
    /// Stream for decoding WAVE s16 data from FLAC container. 
    /// Files with 32 bits samples cannot be read by Windows, this class
    /// down/up convert to 16 bit WAVE files. Decoding only.
    /// </summary>
    public class Wave16OverFlacStream : Stream
    {
        const int BitsPerSample = 16;
        IEnumerator<ArraySegment<byte>> dataSource;

        public Wave16OverFlacStream(Stream stream)
            : this(stream, false)
        {
        }

        public Wave16OverFlacStream(Stream stream, bool leaveOpen)
        {
            if (stream == null) throw new ArgumentNullException("stream");
            this.dataSource = ReadFlac(new FlacReader(stream, leaveOpen)); 
        }

        public Wave16OverFlacStream(FlacReader reader)
        {
            if (reader == null) throw new ArgumentNullException("reader");

            this.dataSource = ReadFlac(reader);
        }

        public override bool CanRead
        {
            get { return true; }
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public override bool CanWrite
        {
            get { return false; }
        }

        public override void Flush()
        {
            throw new NotImplementedException();
        }

        public override long Length
        {
            get 
            {
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
            throw new NotSupportedException();
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
            sampleTransform = WaveSampleTransformerFactory.CreateWave16SampleTransformer(reader.Streaminfo.BitsPerSample);

            int bufferSize = 
                reader.Streaminfo.MaxBlockSize * ((reader.Streaminfo.ChannelsCount * BitsPerSample) >> 3);
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

            int bytesPerInterChannelSample = (streaminfo.ChannelsCount * BitsPerSample) >> 3;
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
            Array.Copy(BitConverter.GetBytes((ushort)BitsPerSample), 0, waveHeader, FmtChunkOffset + 22, 2);

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

    }
}

