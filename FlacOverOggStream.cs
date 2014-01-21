using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FlacBox
{
    /// <summary>
    /// Stream for encoding/decoding FLAC data into OGG container.
    /// </summary>
    public class FlacOverOggStream : Stream, IPageble
    {
        private static ArraySegment<byte> NoCurrentData = new ArraySegment<byte>(new byte[0]);

        private FlacOverOggStreamMode mode;
        Stream baseStream;
        IEnumerator<ArraySegment<byte>> dataSource;
        ArraySegment<byte> currentData;

        private uint streamId;

        public uint StreamId
        {
            get { return streamId; }
        }

        public FlacOverOggStream(Stream stream, FlacOverOggStreamMode mode)
        {
            if (stream == null) throw new ArgumentNullException("stream");

            this.mode = mode;

            this.baseStream = stream;

            switch (mode)
            {
                case FlacOverOggStreamMode.Decode:
                    this.dataSource = ReadFlacStream(stream);

                    if (!dataSource.MoveNext())
                        throw new OggStreamException("FLAC data was not found");
                    currentData = dataSource.Current;
                    break;
                case FlacOverOggStreamMode.Encode:
                    InitializeWriteMode();
                    break;
            }
        }

        const int MaxFrameDataLength = 65307;
        byte[] frameData;
        bool writeOggFlacHeader = false;

        private void InitializeWriteMode()
        {
            byte[] randomBytes = new byte[4]; 
            new Random().NextBytes(randomBytes);
            this.streamId = BitConverter.ToUInt32(randomBytes, 0);

            frameData = new byte[MaxFrameDataLength];            
            writeOggFlacHeader = true;
        }

        public override bool CanRead
        {
            get { return mode == FlacOverOggStreamMode.Decode; }
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public override bool CanWrite
        {
            get { return mode == FlacOverOggStreamMode.Encode; }
        }

        public override void Flush()
        {
            if (mode != FlacOverOggStreamMode.Encode)
                throw new NotSupportedException();

            ((IPageble)this).EndOfPage(false);
        }

        void IPageble.EndOfPage(bool last)
        {
            if (currentData.Count > 0)
            {
                WriteFullFrame(currentData.Array, currentData.Offset, currentData.Count, last, false);
                currentData = new ArraySegment<byte>(frameData, 0, 0);
            }
        }

        public override long Length
        {
            get { throw new NotImplementedException(); }
        }

        public override long Position
        {
            get { return (long)position; }
            set
            {
                throw new NotImplementedException();
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!CanRead) throw new NotSupportedException();

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
            if (!CanWrite) throw new NotSupportedException();

            if (count < 0) throw new ArgumentOutOfRangeException("count");

            if (writeOggFlacHeader)
            {
                BuildWriteOggFlacHeader();
                writeOggFlacHeader = false;
            }

            if (currentData.Count + count > MaxFrameDataLength)
            {
                int tail = MaxFrameDataLength - currentData.Count;
                Array.Copy(buffer, offset, currentData.Array,
                    currentData.Count, tail);
                WriteFullFrame(frameData, 0, MaxFrameDataLength, false, true);
                offset += tail;
                count -= tail;

                currentData = new ArraySegment<byte>(frameData, 0, 0);
                while (count > MaxFrameDataLength)
                {
                    WriteFullFrame(buffer, offset, count, false, true);
                    offset += MaxFrameDataLength;
                    count -= MaxFrameDataLength;
                }               
            }

            System.Diagnostics.Debug.Assert(count <= MaxFrameDataLength);
            Array.Copy(buffer, offset, currentData.Array, currentData.Count, count);
            currentData = new ArraySegment<byte>(currentData.Array, 0, currentData.Count + count);
        }

#pragma warning disable 0414 // TODO remove

        bool isFirstFrameWitten = false;
        bool isLastFrameWritten = false;
        uint pageNumber = 0;
        ulong position = 0;

        private void WriteFullFrame(byte[] buffer, int offset, int count, bool lastFrame, bool overflow)
        {
            // TODO add logic for "overflow" pages

            int segments = (count + 254) / 255;
            byte[] frameHeader = new byte[27 + segments];
            Array.Copy(OggHeader, frameHeader, OggHeader.Length);
            frameHeader[4] = 0x00;
            int frameType = 0;
            if (!isFirstFrameWitten)
            {
                frameType |= FirstPageFlag;
                isFirstFrameWitten = true;
            }
            if (lastFrame)
            {
                frameType |= LastPageFlag;
                isLastFrameWritten = true;
            }
            frameHeader[5] = (byte)frameType;
            Array.Copy(BitConverter.GetBytes(position), 0, frameHeader, 6, 8);
            Array.Copy(BitConverter.GetBytes(streamId), 0, frameHeader, 14, 4);
            Array.Copy(BitConverter.GetBytes(pageNumber), 0, frameHeader, 18, 4);
            // 22 - 26 crc32
            frameHeader[26] = (byte)segments;
            for (int i = 0; i < segments - 1; i++)
            {
                frameHeader[27 + i] = 255;
            }
            frameHeader[27 + segments - 1] = (byte)( count - (segments - 1) * 255 );

            uint crc32 = CrcUtils.Crc32(0, frameHeader);
            crc32 = CrcUtils.Crc32(crc32, buffer, offset, count);

            Array.Copy(BitConverter.GetBytes(crc32), 0, frameHeader, 22, 4);

            baseStream.Write(frameHeader, 0, frameHeader.Length);
            baseStream.Write(buffer, offset, count);

            pageNumber++;
            position += (ulong)count;
        }

        private void BuildWriteOggFlacHeader()
        {
            frameData[0] = 0x7F;
            frameData[1] = 0x46; frameData[2] = 0x4C; frameData[3] = 0x41; frameData[4] = 0x43; // FLAC
            frameData[5] = 1; frameData[6] = 0; // version
            frameData[7] = 0; frameData[8] = 0; // unknown number of other non-audio frames

            currentData = new ArraySegment<byte>(frameData, 0, 9);
        }

        bool disposed = false;

        protected override void Dispose(bool disposing)
        {
            if(!disposed)
            {
                if (mode == FlacOverOggStreamMode.Encode)
                {
                    ((IPageble)this).EndOfPage(true);
                }

                if (baseStream != null)
                {
                    baseStream.Dispose();
                    baseStream = null;
                }
                disposed = true;
            }

            base.Dispose(disposing);
        }

        static byte[] OggHeader = { 0x4f, 0x67, 0x67, 0x53 };
        const int FlacFirstPageLength = 79 - 27 - 1;

        const int FirstPageFlag = 0x02;
        const int LastPageFlag = 0x04;
        const int ContunedPacketFlag = 0x01;

        private IEnumerator<ArraySegment<byte>> ReadFlacStream(Stream stream)
        {
            const byte SupportedStreamStructure = 0;
            const int PageHeaderSize = 27;

            byte[] pageHeader = new byte[PageHeaderSize];
            int read = stream.Read(pageHeader, 0, PageHeaderSize);

            bool streamFound = false;
            this.streamId = 0;

            while (read > 0)
            {
                if (read != PageHeaderSize)
                    throw new OggStreamException("Unexpected end of stream: Ogg page header is too short");
                
                if(pageHeader[0] != OggHeader[0] ||  pageHeader[1] != OggHeader[1] || 
                    pageHeader[2] != OggHeader[2] || pageHeader[3] != OggHeader[3])
                    throw new OggStreamException("Invalid Ogg page signature");

                if(pageHeader[4] != SupportedStreamStructure)
                    throw new OggStreamException("Unsupported Ogg page structure");

                int headerTypeFlags = pageHeader[5];
                ulong absolutePosition = BitConverter.ToUInt64(pageHeader, 6);
                uint currentStreamId = BitConverter.ToUInt32(pageHeader, 14);
                uint pageNo = BitConverter.ToUInt32(pageHeader, 18);
                uint pageCrc = BitConverter.ToUInt32(pageHeader, 22);
                int segmentsCount = pageHeader[26];

                byte[] segmentSizes = new byte[segmentsCount];
                read = stream.Read(segmentSizes, 0, segmentsCount);
                if(read != segmentsCount)
                    throw new OggStreamException(String.Format("Incomplete Ogg page: expected {0} segment sizes", segmentsCount));

                int dataSize = 0;
                for (int i = 0; i < segmentsCount; i++)
			    {
                        dataSize += segmentSizes[i];
			    }

                bool skipPage = true;
                if((headerTypeFlags & FirstPageFlag) != 0)
                {
                    skipPage = streamFound;
                }
                else if(streamFound)
                {
                    skipPage = streamId != currentStreamId;
                }

                if(skipPage)
                {
                    stream.Seek(dataSize, SeekOrigin.Current);
                }
                else
                {
                    pageHeader[22] = 0; pageHeader[23] = 0; // setting CRC field to zero
                    pageHeader[24] = 0; pageHeader[25] = 0;

                    uint crc = Crc32(Crc32(0, pageHeader), segmentSizes);

                    byte[] data = new byte[dataSize];
                    read = stream.Read(data, 0, dataSize);
                    if(read != dataSize)
                        throw new OggStreamException(String.Format("Incomplete Ogg page: expected {0} bytes of data", dataSize));

                    crc = Crc32(crc, data);                    
                    if(crc != pageCrc)
                        throw new OggStreamException("Invalid CRC for Ogg page");

                    if(streamFound)
                        yield return new ArraySegment<byte>(data);
                    else
                    {
                        // check if it's FLAC
                        bool isFlac = data.Length == FlacFirstPageLength &&
                            data[0] == 0x7F && data[1] == 'F' && data[2] == 'L' && data[3] == 'A' && data[4] == 'C';
                        bool isDummyFlac = data.Length >= 4 && data[0] == 'f' && data[1] == 'L' && data[2] == 'a' && data[3] == 'C';
                        if (isFlac)
                        {
                            const int FlacCodecHeaderSize = 9;
                            streamFound = true;
                            this.streamId = currentStreamId;

                            byte majorVersion = data[5];
                            byte minorVersion = data[6];
                            int numberOfNonAudioPackets = data[7] << 8 | data[8];

                            yield return new ArraySegment<byte>(data, FlacCodecHeaderSize, dataSize - FlacCodecHeaderSize);
                        }
                        else if (isDummyFlac)
                        {
                            // some files has this encoding :(
                            streamFound = true;
                            streamId = currentStreamId;

                            yield return new ArraySegment<byte>(data);
                        }
                    }

                    if (streamFound && ((headerTypeFlags & LastPageFlag) != 0))
                    {
                        yield break;
                    }
                }

                read = stream.Read(pageHeader, 0, PageHeaderSize);
            }
        }

        private static uint Crc32(uint seed, byte[] data)
        {
            return CrcUtils.Crc32(seed, data);
        }
    }

    public class OggStreamException : Exception
    {
        public OggStreamException(string message)
            : base(message)
        {
        }
    }

    public enum FlacOverOggStreamMode
    {
        Decode,
        Encode
    }
}
