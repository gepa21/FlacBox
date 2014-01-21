using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FlacBox
{
    /// <summary>
    /// Contains STREAMINFO data for FLAC stream.
    /// </summary>
    public class FlacStreaminfo
    {
        public int MinBlockSize;
        public int MaxBlockSize;
        public int MinFrameSize;
        public int MaxFrameSize;
        public int SampleRate;
        public int ChannelsCount;
        public int BitsPerSample;
        public long TotalSampleCount;
        public byte[] MD5;
    }
}
