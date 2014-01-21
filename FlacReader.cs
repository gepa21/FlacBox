using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace FlacBox
{
    /// <summary>
    /// Reads basic FLAC data.
    /// </summary>
    public class FlacReader : IDisposable
    {
        bool leaveOpen;
        Stream baseStream;

        public Stream BaseStream
        {
            get { return baseStream; }
        }

        FlacRecordType recordType = FlacRecordType.None;

        public FlacRecordType RecordType
        {
            get { return recordType; }
        }

        public int FrameNumber
        {
            get { return frameNumber; }
        }

        public int CurrentChannel
        {
            get { return subframeIndex; }
        }

        bool isLastMetadataBlock = false;
        FlacBitStreamReader bitReader = null;
        int wastedBitsPerSample = 0;

        FlacMetadataBlockType metadataBlockType = FlacMetadataBlockType.Invalid;

        public FlacMetadataBlockType MetadataBlockType
        {
            get { return metadataBlockType; }
        }

        FlacStreaminfo streaminfo;

        public FlacStreaminfo Streaminfo
        {
            get { return streaminfo; }
        }

        int sampleSizeInBits;

        public int FrameBitsPerSample
        {
            get { return sampleSizeInBits; }
        }

        int sampleRate;

        public int FrameSampleRate
        {
            get { return sampleRate; }
        }

        int channelCount;

        public int FrameChannelCount
        {
            get { return channelCount; }
        }

        int blockSize;

        public int BlockSize
        {
            get { return blockSize; }
        }

        SoundChannelAssignment[] channelAssignment = null;

        public SoundChannelAssignment[] ChannelAssignment
        {
            get { return channelAssignment; }
        }

        SoundChannelAssignmentType channelAssignmentType = SoundChannelAssignmentType.None;

        public SoundChannelAssignmentType ChannelAssignmentType
        {
            get { return channelAssignmentType; }
        }

        public FlacReader(Stream baseStream, bool leaveOpen)
        {
            this.baseStream = baseStream;
            this.leaveOpen = leaveOpen;
        }

        public bool Read()
        {
            if (recordType == FlacRecordType.Eof)
                return false;

            try
            {
                switch (recordType)
                {
                    case FlacRecordType.None:
                        ReadStream();
                        break;
                    case FlacRecordType.Stream:
                        ReadMetadataBlock();
                        break;
                    case FlacRecordType.MetadataBlock:
                        if (isLastMetadataBlock)
                            ReadFrame();
                        else
                            ReadMetadataBlock();
                        break;
                    case FlacRecordType.Frame:
                        subframeIndex = 0;
                        ReadSubframe();
                        break;
                    case FlacRecordType.Subframe:
                        if (!dataRead)
                        {
                            SkipSampleValues();
                        }

                        if (++subframeIndex < FrameChannelCount)
                            ReadSubframe();
                        else
                            ReadFrameFooter();
                        break;
                    case FlacRecordType.FrameFooter:
                        ReadFrame();
                        break;
                    case FlacRecordType.Error:
                        throw new FlacException("Reader in error state");
                    case FlacRecordType.Sync:
                        ReadFrame();
                        break;
                    default:
                        throw new NotImplementedException();
                }
                return recordType != FlacRecordType.Eof;
            }
            catch
            {
                recordType = FlacRecordType.Error;
                throw;
            }
        }

        byte secondSyncByte = 2; // set to invalid reserved value

        public bool FindSync()
        {
            const byte FirstSyncByte = 0xFF;
            const byte SecondSyncByte = 0xF8;
            const byte SecondSyncByteMask = 0xFC;

            int b = BaseStream.ReadByte();
            bool found = false;
            while (b >= 0)
            {
                if (b == FirstSyncByte)
                {
                    b = BaseStream.ReadByte();
                    if (b >= 0 && (b & SecondSyncByteMask) == SecondSyncByte)
                    {
                        // sync found
                        secondSyncByte = (byte)b;
                        recordType = FlacRecordType.Sync;
                        found = true;
                        break;
                    }
                }
                else
                {
                    b = BaseStream.ReadByte();
                }
            }
            return found;
        }

        private void SkipSampleValues()
        {
            if(!dataRead)
            {
                foreach (int value in dataSource) ;                

                dataRead = true;
            }
        }

        public IEnumerable<int> GetValues()
        {
            if (RecordType == FlacRecordType.Subframe)
                return ReadSubframeValues();
            else if (RecordType == FlacRecordType.Frame)
            {
                WaveSampleMixer mixer = WaveSampleMixerFactory.CreateWaveSampleMixer(ChannelAssignment);
                return mixer.MixSamples(this);
            }
            else
                throw new FlacException("Reader shall be pointing to frame or subframe");
        }

        public int[] ReadSubframeValues()
        {
            if (dataRead) 
                throw new FlacException("Cannot read twice");

            int[] values = new int[BlockSize];
            int i = 0;
            foreach(int value in dataSource)
            {
                values[i++] = value << wastedBitsPerSample;
            }

            System.Diagnostics.Debug.Assert(i == values.Length);

            dataRead = true;

            return values;
        }

        IEnumerable<int> dataSource = null;

        private void ReadFrameFooter()
        {
            ushort crc16 = bitReader.Complete();
            bitReader = null;

            byte[] data = ReadExactly(2);
            int footerCrc16 = data[0] << 8 | data[1];

            if (crc16 != footerCrc16)
                throw new FlacException("Invalid frame footer CRC16");

            subframeIndex = 0;

            this.recordType = FlacRecordType.FrameFooter;
        }

        private void ReadSubframe()
        {
            uint zeroPadding = bitReader.ReadBits(1);
            if (zeroPadding != 0)
                throw new FlacException("Subframe zero padding is not zero");
            int subframeType = (int)bitReader.ReadBits(6);

            SubframeType type;
            int order = 0;
            if (subframeType == FlacCommons.ConstantSubframeType)
                type = SubframeType.SubframeConstant;
            else if (subframeType == FlacCommons.VerbatimSubframeType)
                type = SubframeType.SubframeVerbatim;
            else if (FlacCommons.FixedSubframeTypeStart <= subframeType &&
                subframeType <= FlacCommons.FixedSubframeTypeEnd)
            {
                type = SubframeType.SubframeFixed;
                order = subframeType - FlacCommons.FixedSubframeTypeStart;
            }
            else if (subframeType >= FlacCommons.LpcSubframeTypeStart)
            {
                type = SubframeType.SubframeLpc;
                order = subframeType - FlacCommons.LpcSubframeTypeStart + 1;
            }
            else
                throw new FlacException("Subframe type is set to reserved");

            uint wastedBitsPerSampleFlag = bitReader.ReadBits(1);
            if (wastedBitsPerSampleFlag > 0)
            {
                this.wastedBitsPerSample = 1 + (int)bitReader.ReadUnary();
            }
            else
                this.wastedBitsPerSample = 0;

            this.subframeType = type;

            int subframeBitsPerSample = FrameBitsPerSample;
            if (ChannelAssignment[subframeIndex] == SoundChannelAssignment.Difference)
            {
                subframeBitsPerSample++; // undocumented
            }

            switch (type)
            {
                case SubframeType.SubframeConstant:
                    PrepareConstantSubframe(subframeBitsPerSample);
                    break;
                case SubframeType.SubframeVerbatim:
                    PrepareVerbatimSubframe(subframeBitsPerSample);
                    break;
                case SubframeType.SubframeLpc:
                    PrepareLpcSubframe(order, subframeBitsPerSample);
                    break;
                case SubframeType.SubframeFixed:
                    PrepareFixedSubframe(order, subframeBitsPerSample);
                    break;
            }

            this.recordType = FlacRecordType.Subframe;
            this.dataRead = false;
        }

        private void PrepareConstantSubframe(int subframeBitsPerSample)
        {
            int sample = bitReader.ReadSignedBits(subframeBitsPerSample);
            dataSource = GetNValues(sample, BlockSize);
        }

        private IEnumerable<int> GetNValues(int value, int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return value;
            }
        }

        private void PrepareVerbatimSubframe(int subframeBitsPerSample)
        {
            dataSource = ReadValuesVerbatim(bitReader, BlockSize, subframeBitsPerSample);
        }

        private IEnumerable<int> ReadValuesVerbatim(FlacBitStreamReader bitReader, int blockSize, int bitsPerSample)
        {
            for (int i = 0; i < blockSize; i++)
            {
                int value = bitReader.ReadSignedBits(bitsPerSample);
                yield return value;
            }
        }

        private void PrepareFixedSubframe(int order, int subframeBitsPerSample)
        {
            int[] warmupSamples = new int[order];
            for (int i = 0; i < order; i++)
            {
                warmupSamples[i] = bitReader.ReadSignedBits(subframeBitsPerSample - this.wastedBitsPerSample);
            }

            IEnumerator<int> residual = ReadResidualData(bitReader, BlockSize, order);
            IPredictor predictor = PredictorFactory.CreateFixedPredictor(order, RemoveLastItem(warmupSamples));
            dataSource = GetPredictorSamples(BlockSize, warmupSamples, predictor, residual);
        }

        private void PrepareLpcSubframe(int order, int subframeBitsPerSample)
        {
            const int InvalidLpcPrecision = 15;
            
            int[] warmupSamples = new int[order];
            for (int i = 0; i < order; i++)
            {
                warmupSamples[i] = bitReader.ReadSignedBits(subframeBitsPerSample - this.wastedBitsPerSample);
            }
            uint precisionCode = bitReader.ReadBits(4);
            if (precisionCode == InvalidLpcPrecision)
                throw new FlacException("Invalid subframe coefficient precision");

            int precision = (int)(precisionCode + 1);

            int shift = bitReader.ReadSignedBits(5);

            int[] coefficients = new int[order];
            for (int i = 0; i < order; i++)
            {
                coefficients[i] = bitReader.ReadSignedBits(precision);
            }

            IEnumerator<int> residual = ReadResidualData(bitReader, BlockSize, order);
            IPredictor predictor = PredictorFactory.CreateLpcPredictor(coefficients, shift, RemoveLastItem(warmupSamples));
            dataSource = GetPredictorSamples(BlockSize, warmupSamples, predictor, residual);
        }

        private IEnumerator<int> ReadResidualData(FlacBitStreamReader bitReader, int blockSize, int predictorOrder)
        {
            const byte RiceCodingWith4BitParameter = 0;
            const byte RiceCodingWith5BitParameter = 1;

            uint residualRiceParamSizeType = bitReader.ReadBits(2);

            int residualRiceParamSize;
            if (residualRiceParamSizeType == RiceCodingWith4BitParameter)
                residualRiceParamSize = 4;
            else if (residualRiceParamSizeType == RiceCodingWith5BitParameter)
                residualRiceParamSize = 5;
            else
                throw new FlacException("Reserved residual coding method");

            // rice and rice2 almost the same
            // read rice partitined method
            int partitionOrder = (int)bitReader.ReadBits(4);
            int partitionCount = 1 << partitionOrder;
            int sampleCount = blockSize >> partitionOrder;

            if (sampleCount < predictorOrder || sampleCount < 1 || 
                (sampleCount << partitionOrder) != blockSize )
                throw new FlacException("Invalid partition order");

            for (int i = 0; i < partitionCount; i++)
            {
                int skipSamples = i == 0 ? predictorOrder : 0;

                int riceParameter = (int)bitReader.ReadBits(residualRiceParamSize);
                if (riceParameter + 1 == 1 << residualRiceParamSize)
                {
                    // escape mode
                    int bitsPerSample = (int)bitReader.ReadBits(5);

                    for (int j = skipSamples; j < sampleCount; j++)
                    {
                        yield return bitReader.ReadSignedBits(bitsPerSample);                        
                    }
                }
                else
                {
                    int maxRiceK = int.MaxValue >> riceParameter;
                    for (int j = skipSamples; j < sampleCount; j++)
                    {
                        yield return bitReader.ReadRice(riceParameter);
                    }
                }
            }
        }

        private static int[] RemoveLastItem(int[] array)
        {
            return ArrayUtils.CutArray(array, 0, array.Length - 1);
        }

        private IEnumerable<int> GetPredictorSamples(int blockSize, int[] warmupSamples, IPredictor predictor, IEnumerator<int> residualData)
        {
            int lastSample = 0;
            if (warmupSamples.Length > 0)
            {
                for (int i = 0; i < warmupSamples.Length; i++)
                {
                    yield return warmupSamples[i];
                }
                lastSample = warmupSamples[warmupSamples.Length - 1];
            }

            for (int i = warmupSamples.Length; i < blockSize; i++)
            {
                if (!residualData.MoveNext())
                    throw new FlacException("Not enough residual data");
                int x = predictor.Next(lastSample);
                int e = residualData.Current;
                int nextSample = x + e;
                yield return nextSample;

                lastSample = nextSample;
            }

            if (residualData.MoveNext())
                throw new FlacException("Not all residual data is decoded");
        }

        private void ReadFrame()
        {
            const int FrameHeaderLength = 2 + 1 + 1;
            const int FrameSync = 0xFFF8;
            const int FrameSyncMask = 0xFFFC;
            const int ReservedBlockSizeSamplesType = 0;
            const int InvalidSampleRateType = 15;

            int read;
            byte[] data = new byte[FrameHeaderLength];
            if (recordType != FlacRecordType.Sync)
            {
                read = BaseStream.Read(data, 0, FrameHeaderLength);
                if (read < FrameHeaderLength)
                {
                    if (read <= 0)
                    {
                        recordType = FlacRecordType.Eof;
                        return;
                    }
                    throw new FlacException("Unexpected eof of stream: invalid frame header length");
                }

                if (((data[0] << 8 | data[1]) & FrameSyncMask) != FrameSync)
                    throw new FlacException("Frame sync is expected");
            }
            else
            {
                const int SyncReadLength = 2;
                read = BaseStream.Read(data, SyncReadLength, FrameHeaderLength - SyncReadLength);
                if(read + SyncReadLength < FrameHeaderLength)
                    throw new FlacException("Unexpected eof of stream: invalid frame header length");

                data[0] = (byte)(FrameSync >> 8); data[1] = secondSyncByte;
            }

            if((data[1] & 0x02) != 0)
                throw new FlacException("Frame header reserved bit (15) shall not be 1");

            this.variableBlockSize = (data[1] & 0x01) != 0;

            int blockSizeSamplesType = data[2] >> 4;
            if(blockSizeSamplesType == ReservedBlockSizeSamplesType)
                throw new FlacException("Frame header block size samples shall not be set to reserved");

            int previousBlockSize = this.blockSize;

            this.blockSize = FlacCommons.StaticBlockSizeSamples[blockSizeSamplesType];

            int sampleRateType = data[2] & 0x0F;
            if(sampleRateType == InvalidSampleRateType)
                throw new FlacException("Frame header sample rate type is invalid");

            this.sampleRate = FlacCommons.StaticSampleRates[sampleRateType];

            int channelAssignmentType = data[3] >> 4;

            if (channelAssignmentType >= FlacCommons.StaticChannelAssignments.Length)
                throw new FlacException("Frame header channel assignments are defined as reserved");

            this.channelAssignmentType = (SoundChannelAssignmentType)channelAssignmentType;
            this.channelAssignment = FlacCommons.StaticChannelAssignments[channelAssignmentType];
            if (channelAssignment == null)
                throw new FlacException("Frame header channel assignment are not defined");

            this.channelCount = channelAssignment.Length;

            int sampleSizeInBitsType = (data[3] >> 1) & 0x07;
            if (sampleSizeInBitsType == FlacCommons.StreaminfoSizeInBitsType)
                this.sampleSizeInBits = Streaminfo.BitsPerSample;
            else if (FlacCommons.StaticSampleSizeInBits[sampleSizeInBitsType] > 0)
                this.sampleSizeInBits = FlacCommons.StaticSampleSizeInBits[sampleSizeInBitsType];
            else
                throw new FlacException("Frame header sample size is defined as reserved");

            if((data[3] & 1) != 0)
                throw new FlacException("Frame header reserved bit (31) shall not be 1");

            MemoryStream ms = new MemoryStream(20);
            ms.Write(data, 0, FrameHeaderLength);

            byte[] numberData;
            if (variableBlockSize)
            {
                ReadUtf8Number(out this.sampleNumber, out numberData);
                if (numberData.Length > 7)
                    throw new FlacException("Invalid variable block size");
            }
            else
            {
                ReadUtf8Number(out this.frameNumber, out numberData);
                if (numberData.Length > 6)
                    throw new FlacException("Invalid frame number");
                this.sampleNumber = this.frameNumber == 0 ? 0 : 
                    previousBlockSize * this.frameNumber;                
            }
            ms.Write(numberData, 0, numberData.Length);

            byte[] blockSizeData = null;
            switch (blockSizeSamplesType)
            {
                case FlacCommons.Bit8BlockSizeSamplesType:
                    blockSizeData = ReadExactly(1);
                    this.blockSize = (int)blockSizeData[0] + 1;
                    break;
                case FlacCommons.Bit16BlockSizeSamplesType:
                    blockSizeData = ReadExactly(2);
                    this.blockSize = (blockSizeData[0] << 8 | blockSizeData[1]) + 1;
                    break;
            }
            if(blockSizeData != null)
                ms.Write(blockSizeData, 0, blockSizeData.Length);

            byte[] sampleRateData = null;
            switch (sampleRateType)
            {
                case FlacCommons.StreaminfoSampleRateType:
                    this.sampleRate = Streaminfo.SampleRate;
                    break;
                case FlacCommons.Bit8SampleRateType:
                    sampleRateData = ReadExactly(1);
                    this.sampleRate = sampleRateData[0];
                    break;
                case FlacCommons.Bit16SampleRateType:
                    sampleRateData = ReadExactly(2);
                    this.sampleRate = sampleRateData[0] << 8 | sampleRateData[1];
                    break;
                case FlacCommons.Bit16Mult10SampleRateType:
                    sampleRateData = ReadExactly(2);
                    this.sampleRate = (sampleRateData[0] << 8 | sampleRateData[1]) * 10;
                    break;
            }
            if (sampleRateData != null)
                ms.Write(sampleRateData, 0, sampleRateData.Length);

            byte[] readData = ms.ToArray();
            byte crc8 = CrcUtils.Crc8(0, readData);
            int headerCrc8 = BaseStream.ReadByte();
            if (headerCrc8 < 0)
                throw new FlacException("Unexpected end of stream: frame CRC8 expected");
            else if(crc8 != headerCrc8)
                throw new FlacException("Invalid frame CRC");

            ushort currentCrc16 = CrcUtils.Crc16(
                CrcUtils.Crc16(0, readData), (byte)headerCrc8);

            bitReader = new FlacBitStreamReader(BaseStream, currentCrc16);

            lastFrameHeaderData = new byte[readData.Length + 1];
            Array.Copy(readData, lastFrameHeaderData, readData.Length);
            lastFrameHeaderData[readData.Length] = crc8;

            recordType = FlacRecordType.Frame;
        }

        public byte[] ReadFrameRawData()
        {
            if(recordType != FlacRecordType.Frame)
                throw new FlacException("Cannot read non-frame data");

            MemoryStream ms = new MemoryStream();
            ms.Write(lastFrameHeaderData, 0, lastFrameHeaderData.Length);

            // replace bitReader
            ushort currentCrc16 = bitReader.Complete();
            bitReader = new FlacBitStreamReader(
                new SinkedStream(BaseStream, ms), currentCrc16);

            // read subframes
            for (int i = 0; i < FrameChannelCount; i++)
            {
                ReadSubframe();
                SkipSampleValues();
            }
            bitReader.Complete();
            bitReader = null;

            // read footer
            byte[] crc16 = ReadExactly(2);
            recordType = FlacRecordType.FrameFooter;

            int frameDataLength = checked((int)ms.Length);
            ms.Write(crc16, 0, crc16.Length);

            byte[] frameData = ms.ToArray();

            // check CRC16
            ushort dataCrc16 = CrcUtils.Crc16(0, frameData, 0, frameDataLength);
            int footerCrc = (crc16[0] << 8) | crc16[1];
            if(dataCrc16 != footerCrc)
                throw new FlacException("Invalid frame footer CRC16");

            return frameData;
        }

        byte[] lastFrameHeaderData = null;

        private void ReadUtf8Number(out int number, out byte[] numberData)
        {
            int firstByte = BaseStream.ReadByte();
            if(firstByte < 0)
                throw new FlacException("Unexpected end of stream: UTF8 number expected");
            if (firstByte < 0x80)
            {
                number = firstByte;
                numberData = new byte[] { (byte)firstByte };
            }
            else if (firstByte >= 0xC0) 
            {
                int mask = 0x20;
                int satelitesCount = 1;
                while (mask > 0 && (firstByte & mask) != 0)
                {
                    satelitesCount++;
                    mask >>= 1;
                }
                if(mask == 0)
                    throw new FlacException("Invalid UTF8 number size");
                number = (firstByte & (mask - 1));
                numberData = new byte[satelitesCount + 1];
                numberData[0] = (byte)firstByte;
                for (int i = 0; i < satelitesCount; i++)
                {
                    int nextByte = BaseStream.ReadByte();
                    if (nextByte < 0)
                        throw new FlacException("Unexpected end of stream: UTF8 number satelite expected");
                    if((nextByte & 0xC0) != 0x80)
                        throw new FlacException("Invalid UTF8 number satelite");
                    number = (number << 6) | (nextByte & 0x3F);
                    numberData[i + 1] = (byte)nextByte;
                }
            }
            else
                throw new FlacException("Invalid UTF8 number start");
        }

        bool variableBlockSize;
        int frameNumber;
        int sampleNumber;
        int subframeIndex;
        SubframeType subframeType;
        bool dataRead;

        private void ReadMetadataBlock()
        {
            // read header
            int headerType = BaseStream.ReadByte();
            if (headerType < 0)
                throw new FlacException("Unexepted end of stream: metadata block expected");

            isLastMetadataBlock = (headerType & 0x80) != 0;

            metadataBlockType = (FlacMetadataBlockType)(headerType & 0x7F);

            byte[] metadataBlockLengthBytes = ReadExactly(3);
            int metadataBlockLength = metadataBlockLengthBytes[0] << 16 |
                metadataBlockLengthBytes[1] << 8 |
                metadataBlockLengthBytes[2];

            recordType = FlacRecordType.MetadataBlock;

            if (metadataBlockType == FlacMetadataBlockType.Streaminfo)
            {
                ReadStreaminfo(metadataBlockLength);
            }
            else
            {
                // TODO read other block types
                SkipData(metadataBlockLength);
            }
        }

        private void SkipData(int bytes)
        {
            if (BaseStream.CanSeek)
                BaseStream.Seek(bytes, SeekOrigin.Current);
            else
            {
                const int MaxSkip = 1024;
                byte[] buffer = new byte[MaxSkip];
                while (bytes > MaxSkip)
                {
                    BaseStream.Read(buffer, 0, MaxSkip);
                    bytes -= MaxSkip;
                }
                BaseStream.Read(buffer, 0, bytes);
            }
        }

        private void ReadStreaminfo(int metadataBlockLength)
        {
            const int Md5Length = FlacCommons.Md5Length;
            const int Md5Offset = FlacCommons.StreaminfoMetadataBlockLengh - Md5Length;

            if (metadataBlockLength < FlacCommons.StreaminfoMetadataBlockLengh)
                throw new FlacException("Invalid STREAMINFO block size");

            byte[] data = ReadExactly(metadataBlockLength);

            FlacStreaminfo streaminfo = new FlacStreaminfo();
            streaminfo.MinBlockSize = data[0] << 8 | data[1];
            streaminfo.MaxBlockSize = data[2] << 8 | data[3];
            streaminfo.MinFrameSize = data[4] << 16 | data[5] << 8 | data[6];
            streaminfo.MaxFrameSize = data[7] << 16 | data[8] << 8 | data[9];

            streaminfo.SampleRate = (data[10] << 16 | data[11] << 8 | data[12]) >> 4;
            streaminfo.ChannelsCount = ((data[12] >> 1) & 0x07) + 1;

            streaminfo.BitsPerSample = ((data[12] & 0x01) << 4 | (data[13] >> 4)) + 1;

            streaminfo.TotalSampleCount = ((long)(data[13] & 0x0F)) << 32 |
                (long)data[14] << 24 | (uint)(data[15] << 16 | data[16] << 8 | data[17]);

            streaminfo.MD5 = new byte[Md5Length];
            Array.Copy(data, Md5Offset, streaminfo.MD5, 0, Md5Length);

            this.streaminfo = streaminfo;
        }

        private byte[] ReadExactly(int bytes)
        {
            byte[] data = new byte[bytes];
            int read = BaseStream.Read(data, 0, data.Length);
            if (read != data.Length)
                throw new FlacException(String.Format("Unexpected end of stream: expected {0} bytes", bytes));
            return data;
        }

        private void ReadStream()
        {
            byte[] marker = new byte[FlacCommons.StreamMarker.Length];
            int read = BaseStream.Read(marker, 0, marker.Length);
            if (read != marker.Length)
                throw new FlacException("Unexpected end of stream: fLaC is expected");

            for (int i = 0; i < marker.Length; i++)
            {
                if (marker[i] != FlacCommons.StreamMarker[i])
                    throw new FlacException("Invalid stream marker: fLaC is expected");
            }

            recordType = FlacRecordType.Stream;
        }

        void IDisposable.Dispose()
        {
            Dispose(true);
        }

        public void Close()
        {
            Dispose(true);
        }

        bool disposed = false;

        private void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (!leaveOpen) BaseStream.Close();
                baseStream = null;
                disposed = true;
            }

            GC.SuppressFinalize(this);
        }

        ~FlacReader()
        {
            Dispose(false);
        }

        private class SinkedStream : Stream
        {
            Stream baseStream;
            Stream sinkStream;

            internal SinkedStream(Stream baseStream, Stream sinkStream)
            {
                this.baseStream = baseStream;
                this.sinkStream = sinkStream;
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
                get { throw new NotImplementedException(); }
            }

            public override long Position
            {
                get { throw new NotImplementedException(); }
                set { throw new NotImplementedException(); }
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                int read = baseStream.Read(buffer, offset, count);
                if (read > 0)
                    sinkStream.Write(buffer, offset, read);
                return read;
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
                throw new NotImplementedException();
            }
        }
    }

    public enum FlacMetadataBlockType
    {
        Streaminfo, 
        Padding, 
        Application,
        Seektable, 
        VorbisComment, 
        Cuesheet, 
        Picture,
        Invalid = 127
    }

    public enum FlacRecordType
    {
        None,
        Eof,
        Stream, 
        MetadataBlock,
        Frame, 
        FrameFooter, 
        Subframe, 
        Sync,
        Error
    }

}
