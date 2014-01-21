using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FlacBox
{
    /// <summary>
    /// Helps to mix/interleave samples.
    /// </summary>
    abstract class WaveSampleMixer
    {
        internal abstract IEnumerable<int> MixSamples(FlacReader reader);        
    }

    class AsIsWaveSampleMixer : WaveSampleMixer
    {
        internal override IEnumerable<int> MixSamples(FlacReader reader)
        {
            int blockSize = reader.BlockSize;
            int channelCount = reader.Streaminfo.ChannelsCount;

            List<int[]> channelData = new List<int[]>(channelCount);
            while (reader.Read() && reader.RecordType == FlacRecordType.Subframe)
            {
                int[] data = reader.ReadSubframeValues();
                channelData.Add(data);
            }

            for (int i = 0; i < blockSize; i++)
            {
                for (int j = 0; j < channelCount; j++)
                {
                    yield return channelData[j][i];
                }
            }
        }
    }

    class RightSideWaveSampleMixer : WaveSampleMixer
    {
        internal override IEnumerable<int> MixSamples(FlacReader reader)
        {
            if (!reader.Read() || reader.RecordType != FlacRecordType.Subframe)
                throw new FlacException("Side channel expected");
            int[] side = reader.ReadSubframeValues();

            if (!reader.Read() || reader.RecordType != FlacRecordType.Subframe)
                throw new FlacException("Right channel expected");
            int[] right = reader.ReadSubframeValues();

            for (int i = 0; i < right.Length; i++)
            {
                yield return right[i] + side[i];
                yield return right[i];
            }

            reader.Read();
        }
    }


    class LeftSideWaveSampleMixer : WaveSampleMixer
    {
        internal override IEnumerable<int> MixSamples(FlacReader reader)
        {
            if (!reader.Read() || reader.RecordType != FlacRecordType.Subframe)
                throw new FlacException("Right channel expected");
            int[] left = reader.ReadSubframeValues();

            if (!reader.Read() || reader.RecordType != FlacRecordType.Subframe)
                throw new FlacException("Side channel expected");
            int[] side = reader.ReadSubframeValues();

            for (int i = 0; i < left.Length; i++)
            {
                yield return left[i];
                yield return left[i] - side[i];
            }

            reader.Read();
        }
    }

    class AverageWaveSampleMixer : WaveSampleMixer
    {
        internal override IEnumerable<int> MixSamples(FlacReader reader)
        {
            if (!reader.Read() || reader.RecordType != FlacRecordType.Subframe)
                throw new FlacException("Mid channel expected");
            int[] mid = reader.ReadSubframeValues();

            if (!reader.Read() || reader.RecordType != FlacRecordType.Subframe)
                throw new FlacException("Side channel expected");
            int[] side = reader.ReadSubframeValues();

            for (int i = 0; i < mid.Length; i++)
            {
                int right = mid[i] - (side[i] >> 1);
                yield return right + side[i];
                yield return right;
            }

            reader.Read();
        }
    }
}
