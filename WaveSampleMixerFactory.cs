using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FlacBox
{
    static class WaveSampleMixerFactory
    {
        internal static WaveSampleMixer CreateWaveSampleMixer(SoundChannelAssignment[] assignments)
        {
            if (assignments.Length < 2 ||
                (assignments[0] == SoundChannelAssignment.Left && assignments[1] == SoundChannelAssignment.Right))
                return new AsIsWaveSampleMixer();
            else if(assignments.Length == 2)
            {
                if (assignments[0] == SoundChannelAssignment.Difference && assignments[1] == SoundChannelAssignment.Right)
                    return new RightSideWaveSampleMixer();
                else if (assignments[0] == SoundChannelAssignment.Left && assignments[1] == SoundChannelAssignment.Difference)
                    return new LeftSideWaveSampleMixer();
                else if (assignments[0] == SoundChannelAssignment.Average && assignments[1] == SoundChannelAssignment.Difference)
                    return new AverageWaveSampleMixer();
            }

            throw new NotSupportedException();
        }
    }
}
