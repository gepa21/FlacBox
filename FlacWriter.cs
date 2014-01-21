using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace FlacBox
{
    /// <summary>
    /// Writes basic FLAC data.
    /// </summary>
    public class FlacWriter : IDisposable
    {
        public const int MinEncodingPolicyLevel = 0;
        public const int MaxEncodingPolicyLevel = 9;
        public const int DefaultEncodingPolicyLevel = 5;

        Stream baseStream;
        IPageble pageble;
        bool leaveOpen;
        IFrameSink sink;

        int[] buffer;
        int bufferSize;
        FlacWriterEstimator estimator;
        int frameNumber = 0;
        long streamPosition = 0;
        long currentSample = 0;


        public Stream BaseStream
        {
            get { return baseStream; }
        }

        FlacStreaminfo streaminfo = null;

        public FlacStreaminfo Streaminfo
        {
            get { return streaminfo; }
        }

        MetadataBlockWriter metaWriter = null;

        public FlacWriter(Stream baseStream)
            : this(baseStream, false)
        {
        }

        public FlacWriter(Stream baseStream, bool leaveOpen)
        {
            if (baseStream == null) throw new ArgumentNullException("baseStream");

            this.baseStream = baseStream;
            if (baseStream is IPageble)
                this.pageble = (IPageble)baseStream;
            else
                this.pageble = NullForIPageble.Instance;
            this.leaveOpen = leaveOpen;
            this.sink = NullFrameSink.Instance;
        }

        ~FlacWriter()
        {
            Dispose(false);
        }

        public void StartStream(FlacStreaminfo streaminfo)
        {
            StartStream(streaminfo, FlacEncodingPolicy.CreateFromLevel(DefaultEncodingPolicyLevel));
        }

        public void StartStream(FlacStreaminfo streaminfo, int level)
        {
            StartStream(streaminfo, FlacEncodingPolicy.CreateFromLevel(level));
        }

        public void StartStream(FlacStreaminfo streaminfo, FlacEncodingPolicy policy)
        {
            if (policy == null) throw new ArgumentNullException("policy");
            policy.Validate();
            if (streaminfo == null) throw new ArgumentNullException("baseStream");

            this.streaminfo = streaminfo;

            InitializeEstimation(policy);
            InitializeBuffer();

            WriteFlacHeader();

            metaWriter = new StreaminfoWriter(streaminfo);
        }

        private void InitializeEstimation(FlacEncodingPolicy policy)
        {
            estimator = new FlacWriterEstimatorImpl(policy);
        }

        private void FlushMetaWriter(bool last)
        {
            System.Diagnostics.Debug.Assert(metaWriter != null);

            byte type = metaWriter.Type;
            if(last) type |= 0x80;
            BaseStream.WriteByte(type);

            int length = metaWriter.Length;
            BaseStream.WriteByte((byte)(length >> 16)); BaseStream.WriteByte((byte)(length >> 8)); BaseStream.WriteByte((byte)(length));

            metaWriter.Write(BaseStream);

            metaWriter = null;

            pageble.EndOfPage(false);
        }

        private void WriteFlacHeader()
        {
            BaseStream.Write(FlacCommons.StreamMarker, 0, FlacCommons.StreamMarker.Length);
        }

        private void InitializeBuffer()
        {
            buffer = new int[Streaminfo.MaxBlockSize * Streaminfo.ChannelsCount];
            bufferSize = 0;
        }

        public void EndStream()
        {
            if (bufferSize > 0) FlushBuffer();
            buffer = null;

            this.streaminfo = null;
        }

        public void Close()
        {
            Dispose(true);
        }

        void IDisposable.Dispose()
        {
            Dispose(true);
        }

        bool disposed = false;
        private void Dispose(bool disposing)
        {
            if(!disposed)
            {
                FlushBuffer();

                if(!leaveOpen)
                    baseStream.Close();
                baseStream = null;
                disposed = true;
            }

            GC.SuppressFinalize(this);
        }


        public void MetadataBlock(FlacMetadataBlockType type, byte[] metadata)
        {
            if (metadata == null)
                throw new ArgumentNullException("metadata");

            FlushMetaWriter(false);

            metaWriter = new ArrayMetadataBlockWriter(type, metadata);
        }


        private void AppendToBuffer(int[] samples)
        {
            int tail = buffer.Length - bufferSize;
            if (tail <= samples.Length)
            {
                Array.Copy(samples, 0, buffer, bufferSize, tail);
                WriteFrameFromBuffer(buffer.Length, false);
                int position = tail;
                while (position + buffer.Length <= samples.Length)
                {
                    Array.Copy(samples, position, buffer, 0, buffer.Length);
                    WriteFrameFromBuffer(buffer.Length, false);
                    position += buffer.Length;
                }
                bufferSize = samples.Length - position;
                Array.Copy(samples, position, buffer, 0, bufferSize);
            }
            else
            {
                Array.Copy(samples, 0, buffer, bufferSize, samples.Length);
                bufferSize += samples.Length;
            }
        }

        private void FlushBuffer()
        {
            if (bufferSize > 0)
            {
                WriteFrameFromBuffer(bufferSize, true);
                bufferSize = 0;
            }
        }

        private void WriteFrameFromBuffer(int count, bool last)
        {
            int channelsCount = Streaminfo.ChannelsCount;
            int samplesPerChannel = count / channelsCount;

            int[][] nonInterleavedSamples = new int[channelsCount][];
            for (int i = 0; i < channelsCount; i++)
            {
                nonInterleavedSamples[i] = new int[samplesPerChannel];
            }

            int k = 0;
            for (int j = 0; j < samplesPerChannel; j++)
            {
                for (int i = 0; i < channelsCount; i++)
                {
                    nonInterleavedSamples[i][j] = buffer[k++];
                }
            }

            WriteFrame(nonInterleavedSamples, SoundChannelAssignmentType.Auto);

            pageble.EndOfPage(last);
        }

        public void WriteSamples(int[] samples)
        {
            if (samples == null) throw new ArgumentNullException("samples");
            int samplesCount = samples.Length;
            int channelsCount = Streaminfo.ChannelsCount;
            if ((samplesCount % channelsCount) != 0)
                throw new ArgumentException("samples count has to be multiple by amount of channels");

            AppendToBuffer(samples);
        }

        public void WriteFrame(int[][] samples, SoundChannelAssignmentType channelAssigment)
        {
            if(samples == null) throw new ArgumentNullException("samples");
            int channelsCount = Streaminfo.ChannelsCount;
            if (samples.Length != channelsCount)
                throw new ArgumentException("Expected same amount of channels as in Streaminfo");
            int samplesPerChannel = samples[0].Length;
            if( // samplesPerChannel < Streaminfo.MinBlockSize || 
                Streaminfo.MaxBlockSize < samplesPerChannel)
                throw new ArgumentException("Amount of samples more or less than specified in Streaminfo");

            for (int i = 1; i < channelsCount; i++)
            {
                if(samplesPerChannel != samples[i].Length)
                throw new ArgumentException("Expected same amount of samples on all the channels");
            }

            FlacMethod[] methods;

            if(channelsCount == 2 && channelAssigment == SoundChannelAssignmentType.Auto)
            {
                FlacMethodAndDataPair[] methodsForLeftAndRight;

                SoundChannelAssignmentType assigmentType =
                    FindBestMethod(samples[0], samples[1], out methodsForLeftAndRight);

                WriteFrame(methodsForLeftAndRight, assigmentType);
            }
            else
            {
                methods = new FlacMethod[channelsCount];

                SoundChannelAssignment[] assignments;
                SoundChannelAssignmentType definedChannelAssigmentType;
                if (channelAssigment == SoundChannelAssignmentType.Auto)
                {
                    assignments = FlacCommons.StaticChannelAssignments[channelsCount - 1];
                    definedChannelAssigmentType = (SoundChannelAssignmentType)(channelsCount - 1);
                }
                else
                {
                    assignments = FlacCommons.StaticChannelAssignments[(int)channelAssigment];
                    definedChannelAssigmentType = channelAssigment;
                }

                FlacMethodAndDataPair[] samplesAndMethods = estimator.FindBestMethods(
                    samples, Streaminfo.BitsPerSample);

                WriteFrame(samplesAndMethods, definedChannelAssigmentType);
            }
        }

        public void WriteFrame(FlacMethodAndDataPair[] methods, SoundChannelAssignmentType channelAssignment)
        {
            EnsureFramesMode();

            sink.StartFrame(streamPosition, currentSample);

            if (methods == null) throw new ArgumentNullException("methods");
            if(methods.Length != Streaminfo.ChannelsCount)
                throw new ArgumentNullException("Methods items does not correspond to amount of channels");

            int samplesCount = methods[0].Data.Length;

            const byte Blocking = 0x00; // fixed

            MemoryStream frameHeader = new MemoryStream();
            // sync code + reserved = 0 + blocking
            frameHeader.WriteByte(0xFF); frameHeader.WriteByte(0xF8 | Blocking);

            int interChannelSamplesTypeIndex = Array.IndexOf(FlacCommons.StaticBlockSizeSamples, samplesCount);
            int interChannelSamplesType;
            if (interChannelSamplesTypeIndex > 0)
                interChannelSamplesType = interChannelSamplesTypeIndex;
            else if (samplesCount > 256)
                interChannelSamplesType = FlacCommons.Bit16BlockSizeSamplesType;
            else
                interChannelSamplesType = FlacCommons.Bit8BlockSizeSamplesType;

            int sampleRateTypeIndex = Array.IndexOf(FlacCommons.StaticSampleRates, Streaminfo.SampleRate);
            int sampleRateType = sampleRateTypeIndex > 0 
                ? sampleRateTypeIndex : FlacCommons.StreaminfoSampleRateType;
            frameHeader.WriteByte((byte)(interChannelSamplesType << 4 | sampleRateType));

            int channelAssignmetType = (int)channelAssignment;

            int sampleSizeInBitsTypeIndex = Array.IndexOf(FlacCommons.StaticSampleSizeInBits, Streaminfo.BitsPerSample);
            int sampleSizeInBitsType = sampleSizeInBitsTypeIndex > 0
                ? sampleSizeInBitsTypeIndex : FlacCommons.StreaminfoSizeInBitsType;
            frameHeader.WriteByte((byte)(channelAssignmetType << 4 | sampleSizeInBitsType << 1));

            WriteUtf8Number(frameHeader, frameNumber);
            switch (interChannelSamplesType)
            {
                case FlacCommons.Bit8BlockSizeSamplesType:
                    frameHeader.WriteByte((byte)(samplesCount - 1)); break;
                case FlacCommons.Bit16BlockSizeSamplesType:
                    frameHeader.WriteByte((byte)((samplesCount - 1) >> 8));
                    frameHeader.WriteByte((byte)(samplesCount - 1));
                    break;
            }
            frameHeader.Close();

            byte[] frameHeaderData = frameHeader.ToArray();

            byte crc8 = CrcUtils.Crc8(0, frameHeaderData);

            BaseStream.Write(frameHeaderData, 0, frameHeaderData.Length);
            BaseStream.WriteByte(crc8);

            ++frameNumber;

            ushort crc16Seed = CrcUtils.Crc16(CrcUtils.Crc16(0, frameHeaderData), crc8);

            // write channels
            FlacBitStreamWriter bitWriter = new FlacBitStreamWriter(BaseStream, crc16Seed);

            for (int i = 0; i < methods.Length; i++)
            {
                WriteSubframe(bitWriter, methods[i]);
            }

            int subframesLength;
            ushort crc16;
            bitWriter.Complete(out crc16, out subframesLength);

            // write footer
            BaseStream.WriteByte((byte)(crc16 >> 8)); BaseStream.WriteByte((byte)crc16);


            int frameSize = frameHeaderData.Length + 1 + subframesLength + 2;
            streamPosition += frameSize;
            currentSample += samplesCount;

            sink.EndFrame(streamPosition, currentSample);
        }

        private void WriteSubframe(FlacBitStreamWriter bitWriter, FlacMethodAndDataPair flacMethodAndDataPair)
        {
            bitWriter.WriteUnsigned(0, 1);

            int subframeType = flacMethodAndDataPair.Method.SubframeTypeCode;
            bitWriter.WriteUnsigned((uint)subframeType, 6);

            bitWriter.WriteUnsigned(0, 1);

            flacMethodAndDataPair.WriteData(bitWriter);
        }

        private static void WriteUtf8Number(Stream stream, int number)
        {
            if (number < 128)
                stream.WriteByte((byte)number);
            else
            {
                int mask = 0x40;
                Stack<byte> parts = new Stack<byte>(4);
                do
                {
                    int part = (number & 0x3F) | 0x80;
                    number >>= 6;
                    parts.Push((byte)part);
                    mask >>= 1;
                } while (number >= mask);
                int firstByte = (0x100 - (mask << 1)) | number;
                stream.WriteByte((byte)firstByte);
                while (parts.Count > 0)
                    stream.WriteByte(parts.Pop());
            }
        }

        private void EnsureFramesMode()
        {
            if (metaWriter != null)
            {
                FlushMetaWriter(true);
            }
        }

        public FlacMethodAndDataPair FindBestMethod(int[] channelSamples, SoundChannelAssignment channelAssigment)
        {
            if (channelSamples == null) throw new ArgumentNullException("channelSamples");
            if (channelSamples.Length == 0) throw new ArgumentException("channelSamples has to have at least one item");

            int channelBitsPerSample = Streaminfo.BitsPerSample;
            if (channelAssigment == SoundChannelAssignment.Difference)
                channelBitsPerSample++;

            return new FlacMethodAndDataPair(
                estimator.FindBestMethod(channelSamples, channelBitsPerSample),
                channelBitsPerSample, channelSamples);
        }

        [Obsolete]
        FlacEncodingPolicy policy = null;

        [Obsolete]
        private FlacEncodingPolicy GetPolicy()
        {
            return policy;
        }

        public SoundChannelAssignmentType FindBestMethod(int[] leftSamples, int[] rightSamples, out FlacMethodAndDataPair[] methods)
        {
            if (leftSamples == null) throw new ArgumentNullException("leftSamples");
            if (rightSamples == null) throw new ArgumentNullException("rightSamples");
            if (leftSamples.Length != rightSamples.Length)
                throw new ArgumentException("leftSamples and rightSamples has to have same amount of samples");
            if(leftSamples.Length == 0)
                throw new ArgumentException("channels has to have at least one item");

            return estimator.FindBestMethods(leftSamples, rightSamples, Streaminfo.BitsPerSample,
                out methods);
        }

        private abstract class MetadataBlockWriter
        {
            public abstract byte Type { get; }
            public abstract int Length { get; }
            public abstract void Write(Stream s);
        }

        private class ArrayMetadataBlockWriter : MetadataBlockWriter
        {
            FlacMetadataBlockType type;
            byte[] data;

            public ArrayMetadataBlockWriter(FlacMetadataBlockType type, byte[] data)
            {
                this.type = type;
                this.data = data;
            }

            public override byte Type
            {
                get { return (byte)type; }
            }

            public override int Length
            {
                get { return data.Length; }
            }

            public override void Write(Stream s)
            {
                s.Write(data, 0, data.Length);
            }
        }

        private class StreaminfoWriter : MetadataBlockWriter
        {
            FlacStreaminfo streaminfo;

            public StreaminfoWriter(FlacStreaminfo streaminfo)
            {
                this.streaminfo = streaminfo;
            }

            public override byte Type
            {
                get { return (int)FlacMetadataBlockType.Streaminfo; }
            }

            public override int Length
            {
                get { return FlacCommons.StreaminfoMetadataBlockLengh; }
            }

            public override void Write(Stream s)
            {
                s.WriteByte((byte)(streaminfo.MinBlockSize >> 8)); s.WriteByte((byte)(streaminfo.MinBlockSize));
                s.WriteByte((byte)(streaminfo.MaxBlockSize >> 8)); s.WriteByte((byte)(streaminfo.MaxBlockSize));
                s.WriteByte((byte)(streaminfo.MinFrameSize >> 16)); s.WriteByte((byte)(streaminfo.MinFrameSize >> 8)); s.WriteByte((byte)(streaminfo.MinFrameSize));
                s.WriteByte((byte)(streaminfo.MaxFrameSize >> 16)); s.WriteByte((byte)(streaminfo.MaxFrameSize >> 8)); s.WriteByte((byte)(streaminfo.MaxFrameSize));
                uint ratePlusChannelsAndBits = (uint)(streaminfo.SampleRate << 12 |
                    (streaminfo.ChannelsCount - 1) << 9 |
                    (streaminfo.BitsPerSample - 1) << 4 |
                    (byte)(streaminfo.TotalSampleCount >> 32));

                s.WriteByte((byte)(ratePlusChannelsAndBits >> 24)); s.WriteByte((byte)(ratePlusChannelsAndBits >> 16));
                s.WriteByte((byte)(ratePlusChannelsAndBits >> 8)); s.WriteByte((byte)(ratePlusChannelsAndBits));

                s.WriteByte((byte)(streaminfo.TotalSampleCount >> 24)); s.WriteByte((byte)(streaminfo.TotalSampleCount >> 16));
                s.WriteByte((byte)(streaminfo.TotalSampleCount >> 8)); s.WriteByte((byte)(streaminfo.TotalSampleCount));

                byte[] md5 = streaminfo.MD5;
                if(md5 == null)
                {
                    md5 = new byte[FlacCommons.Md5Length]; // unknown
                }
                s.Write(md5, 0, FlacCommons.Md5Length);
            }
        }
    }

}
