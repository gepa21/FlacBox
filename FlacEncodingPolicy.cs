using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FlacBox
{
    /// <summary>
    /// Defines restrictions for estimation algorithm.
    /// </summary>
    [Serializable]
    public sealed class FlacEncodingPolicy
    {
        public IntRange? FixedOrder;
        public IntRange? LpcOrder;
        public IntRange RicePartionOrder;
        public StereoEncodingPolicy StereoEncoding;
        public bool UseParallelExtensions;

        public FlacEncodingPolicy()
        {
            FixedOrder = null;
            LpcOrder = new IntRange(8);
            RicePartionOrder = new IntRange(0, 5);
            StereoEncoding = StereoEncodingPolicy.TrySidesAndAverage;
#if PARALLEL
            UseParallelExtensions = true;
#else
            UseParallelExtensions = false;
#endif
        }

        public static FlacEncodingPolicy CreateFromLevel(int level)
        {
            if (level < 0 || level > 9) throw new ArgumentOutOfRangeException("level");

            switch (level)
            {
                case 0:
                    return new FlacEncodingPolicy() { FixedOrder = null, LpcOrder = null, RicePartionOrder = new IntRange(0), StereoEncoding = StereoEncodingPolicy.AsIs };
                case 1:
                    return new FlacEncodingPolicy() { FixedOrder = new IntRange(0, 2), LpcOrder = null, RicePartionOrder = new IntRange(0, 3), StereoEncoding = StereoEncodingPolicy.TrySides };
                case 2:
                    return new FlacEncodingPolicy() { FixedOrder = new IntRange(0, 3), LpcOrder = null, RicePartionOrder = new IntRange(0, 3), StereoEncoding = StereoEncodingPolicy.TrySidesAndAverage };
                case 3:
                    return new FlacEncodingPolicy() { FixedOrder = new IntRange(0), LpcOrder = new IntRange(6), RicePartionOrder = new IntRange(0, 4), StereoEncoding = StereoEncodingPolicy.AsIs };
                case 4:
                    return new FlacEncodingPolicy() { FixedOrder = new IntRange(0), LpcOrder = new IntRange(8), RicePartionOrder = new IntRange(0, 4), StereoEncoding = StereoEncodingPolicy.TrySides };
                case 5:
                    return new FlacEncodingPolicy() { FixedOrder = new IntRange(0), LpcOrder = new IntRange(8), RicePartionOrder = new IntRange(0, 5), StereoEncoding = StereoEncodingPolicy.TrySidesAndAverage };
                case 6:
                    return new FlacEncodingPolicy() { FixedOrder = new IntRange(0), LpcOrder = new IntRange(8), RicePartionOrder = new IntRange(0, 6), StereoEncoding = StereoEncodingPolicy.TrySidesAndAverage };
                case 7:
                    return new FlacEncodingPolicy() { FixedOrder = new IntRange(0, 3), LpcOrder = new IntRange(8), RicePartionOrder = new IntRange(0, 6), StereoEncoding = StereoEncodingPolicy.TrySidesAndAverage };
                case 8:
                    return new FlacEncodingPolicy() { FixedOrder = new IntRange(0, 3), LpcOrder = new IntRange(12), RicePartionOrder = new IntRange(0, 6), StereoEncoding = StereoEncodingPolicy.TrySidesAndAverage };
                case 9:
                    return new FlacEncodingPolicy() { FixedOrder = new IntRange(0, 4), LpcOrder = new IntRange(1, 32), RicePartionOrder = new IntRange(0, 15), StereoEncoding = StereoEncodingPolicy.TrySidesAndAverage };
                default:
                    throw new NotSupportedException();
            }
        }

        internal FlacEncodingPolicy(FlacEncodingPolicy cloneFrom)
        {
            this.FixedOrder = cloneFrom.FixedOrder;
            this.LpcOrder = cloneFrom.LpcOrder;
            this.RicePartionOrder = cloneFrom.RicePartionOrder;
            this.StereoEncoding = cloneFrom.StereoEncoding;
            this.UseParallelExtensions = cloneFrom.UseParallelExtensions;
        }

        internal void Validate()
        {
            if (FixedOrder.HasValue)
            {
                if (FixedOrder.Value.MinValue > FixedOrder.Value.MaxValue) throw new FlacException("MinFixedOrder > MaxFixedOrder");
                if (FixedOrder.Value.MinValue < 0) throw new FlacException("MinFixedOrder < 0");
                if (FixedOrder.Value.MaxValue > 4) throw new FlacException("MaxFixedOrder > 4");
            }

            if (LpcOrder.HasValue)
            {
                if (LpcOrder.Value.MinValue > LpcOrder.Value.MaxValue) throw new FlacException("MinLpcOrder > MaxLpcOrder");
                if (LpcOrder.Value.MinValue < 1) throw new FlacException("MinLpcOrder < 1");
                if (LpcOrder.Value.MaxValue > 32) throw new FlacException("MaxLpcOrder > 32");
            }

            if (RicePartionOrder.MinValue > RicePartionOrder.MaxValue) throw new FlacException("MinRicePartionOrder > MaxRicePartionOrder");
            if (RicePartionOrder.MinValue < 0) throw new FlacException("MinRicePartionOrder < 0");
            if (RicePartionOrder.MaxValue > 15) throw new FlacException("MaxRicePartionOrder > 15");
        }
    }

    public enum StereoEncodingPolicy
    {
        AsIs,
        TrySides,
        TrySidesAndAverage
    }

    [Serializable]
    public struct IntRange
    {
        public int MinValue;
        public int MaxValue;

        public IntRange(int singleValue)
        {
            this.MinValue = singleValue;
            this.MaxValue = singleValue;
        }

        public IntRange(int minValue, int maxValue)
        {
            this.MinValue = minValue;
            this.MaxValue = maxValue;
        }
    }
}
