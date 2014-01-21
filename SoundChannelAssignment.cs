using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FlacBox
{
    public enum SoundChannelAssignment
    {
        Left, 
        Right, 
        Center, 
        LFE, 
        BackLeft, 
        BackRight,
        Average,
        Difference,
        Unknown
    }

    public enum SoundChannelAssignmentType
    {
        Auto = -1,
        None = -1,
        Mono = 0,
        LeftRight = 1,
        LeftRightCenter = 2,
        LeftRightBackLeftBackRight = 3,
        LeftRightCenterBackLeftBackRight = 4,
        LeftRightCenterLFEBackLeftBackRight = 5,
        LeftSide = 8,
        RightSide = 9,
        MidSide = 10
    }
}
