using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FlacBox
{
    /// <summary>
    /// FLAC encoding method. Used in FlacWriter.
    /// </summary>
    public abstract class FlacMethod
    {
        SubframeType subframeType;
        int estimatedSize;

        public SubframeType SubframeType
        {
            get { return subframeType; }
        }

        public int EstimatedSize
        {
            get { return estimatedSize; }
        }

        internal abstract int SubframeTypeCode { get; }

        protected FlacMethod(SubframeType subframeType, int estimatedSize)
        {
            this.subframeType = subframeType;
            this.estimatedSize = estimatedSize;
        }

        internal abstract void WriteData(FlacBitStreamWriter writer, int bitsPerSample, int[] data);
    }

    public abstract class FlacMethodFirBased : FlacMethod
    {
        int order;

        public int Order
        {
            get { return order; }
        }

        FlacResidualCoefficeints residual;

        public FlacResidualCoefficeints Residual
        {
            get { return residual; }
        }

        protected FlacMethodFirBased(SubframeType subframeType, int estimatedSize, int order, FlacResidualCoefficeints residual)
            : base(subframeType, estimatedSize)
        {
            this.order = order;
            this.residual = residual;
        }

        internal override void WriteData(FlacBitStreamWriter writer, int bitsPerSample, int[] data)
        {
            WriteHeaderData(writer, bitsPerSample, data);

            IPredictor predictor = CreatePredictor(ArrayUtils.CutArray(data, 0, Order - 1));

            residual.WriteResidual(writer, predictor, Order, data);
        }

        internal abstract IPredictor CreatePredictor(int[] samples);

        internal abstract void WriteHeaderData(FlacBitStreamWriter writer, int bitsPerSample, int[] data);
    }

    public sealed class FlacConstantMethod : FlacMethod
    {
        internal override int SubframeTypeCode
        {
            get { return FlacCommons.ConstantSubframeType; }
        }

        public FlacConstantMethod(int bitsPerSample)
            : base(SubframeType.SubframeConstant, bitsPerSample)
        {
        }

        internal override void WriteData(FlacBitStreamWriter writer, int bitsPerSample, int[] data)
        {
            writer.WriteSigned(data[0], bitsPerSample);
        }
    }

    public sealed class FlacVerbatimMethod : FlacMethod
    {
        internal override int SubframeTypeCode
        {
            get { return FlacCommons.VerbatimSubframeType; }
        }

        public FlacVerbatimMethod(int bitsPerSample, int samplesCount)
            : base(SubframeType.SubframeVerbatim, bitsPerSample * samplesCount)
        {
        }

        internal override void WriteData(FlacBitStreamWriter writer, int bitsPerSample, int[] data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                writer.WriteSigned(data[i], bitsPerSample);
            }
        }
    }

    public sealed class FlacFixedMethod : FlacMethodFirBased
    {
        internal override int SubframeTypeCode
        {
            get { return FlacCommons.FixedSubframeTypeStart + Order; }
        }

        public FlacFixedMethod(int bitsPerSample, int order, FlacResidualCoefficeints residual)
            : base(SubframeType.SubframeFixed, bitsPerSample * order + residual.EstimatedSize, order, residual)
        {
        }

        internal override void WriteHeaderData(FlacBitStreamWriter writer, int bitsPerSample, int[] data)
        {
            // samples
            for (int i = 0; i < Order; i++)
            {
                writer.WriteSigned(data[i], bitsPerSample);
            }
        }

        internal override IPredictor CreatePredictor(int[] samples)
        {
            return PredictorFactory.CreateFixedPredictor(Order, samples);
        }
    }

    public sealed class FlacLpcMethod : FlacMethodFirBased
    {
        internal override int SubframeTypeCode
        {
            get { return FlacCommons.LpcSubframeTypeStart + Order - 1; }
        }

        FlacLpcMethodCoefficeints coefficients;

        FlacLpcMethodCoefficeints Coefficients
        {
            get { return coefficients; }
        }

        public FlacLpcMethod(int bitsPerSample, FlacLpcMethodCoefficeints coefficients, FlacResidualCoefficeints residual)
            : base(SubframeType.SubframeLpc, coefficients.EstimateStorage(bitsPerSample) + residual.EstimatedSize, coefficients.Coefficients.Length, residual)
        {
            this.coefficients = coefficients;
        }

        internal override void WriteHeaderData(FlacBitStreamWriter writer, int bitsPerSample, int[] data)
        {
            System.Diagnostics.Debug.Assert(0 < Coefficients.CoefficientsPrecision &&
                Coefficients.CoefficientsPrecision < 16);

            // samples
            for (int i = 0; i < Order; i++)
            {
                writer.WriteSigned(data[i], bitsPerSample);
            }

            writer.WriteUnsigned((uint)(Coefficients.CoefficientsPrecision - 1), 4);

            writer.WriteSigned(Coefficients.ResultShift, 5);

            for (int i = 0; i < Order; i++)
            {
                writer.WriteSigned(Coefficients.Coefficients[i], Coefficients.CoefficientsPrecision);
            }
        }

        internal override IPredictor CreatePredictor(int[] samples)
        {
            return Coefficients.CreatePredictor(samples);
        }
    }

    public class FlacLpcMethodCoefficeints
    {
        int coefficientsPrecision;

        public int CoefficientsPrecision
        {
            get { return coefficientsPrecision; }
        }

        int[] coefficients;

        public int[] Coefficients
        {
            get { return coefficients; }
        }

        int resultShift;

        public int ResultShift
        {
            get { return resultShift; }
        }

        public FlacLpcMethodCoefficeints(int coefficientsPrecision, int[] coefficients, int resultShift)
        {
            this.coefficientsPrecision = coefficientsPrecision;
            this.coefficients = coefficients;
            this.resultShift = resultShift;
        }

        internal int EstimateStorage(int bitsPerSample)
        {
            return bitsPerSample * Coefficients.Length +
                4 + 5 + CoefficientsPrecision * Coefficients.Length;
        }

        internal IPredictor CreatePredictor(int[] samples)
        {
            return PredictorFactory.CreateLpcPredictor(Coefficients, ResultShift, samples);
        }
    }
}
