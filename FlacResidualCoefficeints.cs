using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FlacBox
{
    /// <summary>
    /// Helps to encode residual data. Used in FlacWriter.
    /// </summary>
    public class FlacResidualCoefficeints
    {
        public int Order;
        public int[] RiceParameters;
        public int EstimatedSize;
        public bool IsExtended;

        internal void WriteResidual(FlacBitStreamWriter writer, IPredictor predictor, int predictorOrder, int[] samples)
        {
            int totalPartitions = 1 << Order;
            System.Diagnostics.Debug.Assert((samples.Length % totalPartitions) == 0
                && (samples.Length / totalPartitions) > predictorOrder && RiceParameters.Length == totalPartitions);

            if (!IsExtended)
                writer.WriteUnsigned(0, 2);
            else
                writer.WriteUnsigned(1, 2);

            writer.WriteUnsigned((uint)Order, 4);

            int j = predictorOrder;
            int samplePerPartition = samples.Length / totalPartitions;
            int encodingParameterPrecision = IsExtended ? 5 : 4;
            IEnumerator<int> residualData = GetResidual(samples, predictorOrder, predictor);

            for (int i = 0; i < totalPartitions; i++)
            {
                if (RiceParameters[i] >= 0)
                {
                    int riceParameter = RiceParameters[i];
                    writer.WriteUnsigned((uint)riceParameter, encodingParameterPrecision);
                    while (j++ < samplePerPartition)
                    {
                        if (!residualData.MoveNext())
                            throw new FlacException("Invalid amount of residual data");

                        writer.WriteRice(residualData.Current, riceParameter);
                    }    
                }
                else // escaped
                {
                    writer.WriteUnsigned(~0U, encodingParameterPrecision);
                    int samplePrecision = ~RiceParameters[i];
                    writer.WriteUnsigned((uint)samplePrecision, 5);
                    while(j++ < samplePerPartition)
                    {
                        if (!residualData.MoveNext())
                            throw new FlacException("Invalid amount of residual data");

                        writer.WriteSigned(residualData.Current, samplePrecision);
                    }
                }
                j = 0;
            }
        }

        private IEnumerator<int> GetResidual(int[] samples, int predictorOrder, IPredictor predictor)
        {
            int lastSample = predictorOrder > 0 ? samples[predictorOrder - 1] : 0;
            for (int i = predictorOrder; i < samples.Length; i++)
            {
                int nextSample = samples[i];
                yield return nextSample - predictor.Next(lastSample);
                lastSample = nextSample;
            }
        }

    }
}
