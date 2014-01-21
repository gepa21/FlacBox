using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FlacBox
{
    /// <summary>
    /// Common constants used during FLAC encoding/decoding.
    /// </summary>
    static class FlacCommons
    {
        internal static int[] StaticBlockSizeSamples = { 0, 192, 576, 1152, 2304, 4608,
            0, 0, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768 };

        internal static int[] StaticSampleRates = {0, 88200, 176400, 192000, 8000, 16000,
            22050, 24000, 32000, 44100, 48000, 96000, 0, 0, 0 };

        internal static int[] StaticSampleSizeInBits = { 0, 8, 12, 0, 16, 20, 24, 0 };


        internal static SoundChannelAssignment[][] StaticChannelAssignments = {
                new SoundChannelAssignment[] { SoundChannelAssignment.Center },
                new SoundChannelAssignment[] { SoundChannelAssignment.Left, SoundChannelAssignment.Right },
                new SoundChannelAssignment[] { SoundChannelAssignment.Left, SoundChannelAssignment.Right, SoundChannelAssignment.Center },
                new SoundChannelAssignment[] { SoundChannelAssignment.Left, SoundChannelAssignment.Right, SoundChannelAssignment.BackLeft, SoundChannelAssignment.BackRight },
                new SoundChannelAssignment[] { SoundChannelAssignment.Left, SoundChannelAssignment.Right, SoundChannelAssignment.Center, SoundChannelAssignment.BackLeft, SoundChannelAssignment.BackRight  },
                new SoundChannelAssignment[] { SoundChannelAssignment.Left, SoundChannelAssignment.Right, SoundChannelAssignment.Center, SoundChannelAssignment.LFE, SoundChannelAssignment.BackLeft, SoundChannelAssignment.BackRight  },
                null,
                null,
                new SoundChannelAssignment[] { SoundChannelAssignment.Left, SoundChannelAssignment.Difference },
                new SoundChannelAssignment[] { SoundChannelAssignment.Difference, SoundChannelAssignment.Right },
                new SoundChannelAssignment[] { SoundChannelAssignment.Average, SoundChannelAssignment.Difference },
            };

        internal static byte[] StreamMarker = { 0x66, 0x4C, 0x61, 0x43 };


        internal const int Bit8SampleRateType = 12;
        internal const int Bit16SampleRateType = 13;
        internal const int Bit16Mult10SampleRateType = 14;
        internal const int StreaminfoSizeInBitsType = 0;
        internal const int Bit8BlockSizeSamplesType = 6;
        internal const int Bit16BlockSizeSamplesType = 7;
        internal const int StreaminfoSampleRateType = 0;

        internal const int ConstantSubframeType = 0;
        internal const int VerbatimSubframeType = 1;
        internal const int FixedSubframeTypeStart = 8;
        internal const int FixedSubframeTypeEnd = 12;
        internal const int LpcSubframeTypeStart = 32;
        internal const int LpcSubframeTypeEnd = 63;

        internal const int StreaminfoMetadataBlockLengh = 34;
        internal const int Md5Length = 16;

        internal const int DefaultBlockSize = 4608;
    }

    public enum SubframeType
    {
        Reserved,
        SubframeConstant,
        SubframeVerbatim,
        SubframeFixed,
        SubframeLpc
    }
}
