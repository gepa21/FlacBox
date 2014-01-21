using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FlacBox
{
    /// <summary>
    /// Contains estimation algorithms for FlacWriter.
    /// 
    /// TODO add more mathematical analysis to improve performance.
    /// </summary>
    abstract class FlacWriterEstimator
    {
        internal abstract FlacMethod FindBestMethod(int[] channelSamples, int bitsPerSample);
        internal abstract FlacMethodAndDataPair[] FindBestMethods(int[][] samples, int bitsPerSamples);
        internal abstract SoundChannelAssignmentType FindBestMethods(int[] leftSamples, int[] rightSamples, int bitsPerSample, out FlacMethodAndDataPair[] methods);
    }

    sealed class FlacWriterEstimatorImpl : FlacWriterEstimator
    {
        ParallelExecution parallel;
        FlacEncodingPolicy policy;

        internal FlacWriterEstimatorImpl(FlacEncodingPolicy policy)
        {
            this.policy = policy;
            this.parallel = ParallelExecutionFactory.CreateParallelExecution(policy.UseParallelExtensions);
        }

        internal override FlacMethod FindBestMethod(int[] channelSamples, int bitsPerSample)
        {
            int i = 1;
            int firstSample = channelSamples[0];
            while (i < channelSamples.Length && channelSamples[i] == firstSample)
                i++;

            if (i == channelSamples.Length)
            {
                // constant method will be better than other methods
                return new FlacConstantMethod(bitsPerSample);
            }

            FlacMethod verbatimMethod;
            verbatimMethod = new FlacVerbatimMethod(
                channelSamples.Length, bitsPerSample);

            FlacMethod fixedMethod = null, lpcMethod = null;
            
            parallel.Invoke(
                delegate
                {
                    fixedMethod = FindBestFixedMethod(channelSamples, bitsPerSample, policy);
                },
                delegate
                {
                    lpcMethod = FindBestLpcMethod(channelSamples, bitsPerSample, policy);
                }
            );

            return FindBestMethod(new FlacMethod[] { 
                verbatimMethod, fixedMethod, lpcMethod });
        }

        private FlacMethod FindBestLpcMethod(int[] channelSamples, int bitsPerSample, FlacEncodingPolicy policy)
        {
            if (!policy.LpcOrder.HasValue) return null;

            int minLpcOrder = policy.LpcOrder.Value.MinValue;
            int maxLpcOrder = Math.Min(policy.LpcOrder.Value.MaxValue, 
                channelSamples.Length - 1);

            double[] r = new double[maxLpcOrder + 1];
            parallel.For(0, r.Length, i =>
            {
                double sum = 0;
                for (int j = 0, q = channelSamples.Length - i; j < i; j++, q++)
                    sum += (double)channelSamples[j] * channelSamples[q];
                for (int j = i, q = 0; j < channelSamples.Length; j++, q++)
                    sum += (double)channelSamples[j] * channelSamples[q];
                r[i] = sum;
            });

            FlacMethod[] methods = new FlacMethod[maxLpcOrder];            
            parallel.For(minLpcOrder, maxLpcOrder + 1, order =>
            {
                double[] coef = SolveLpc(r, order);

                int[] integerCoefficients;
                int shift;
                int precision;
                ConvertLpcCoeficientsToIntegers(coef, out integerCoefficients, out precision, out shift);

                IPredictor predictor = PredictorFactory.CreateLpcPredictor(integerCoefficients, shift, ArrayUtils.CutArray(channelSamples, 0, order - 1));
                FlacResidualCoefficeints residual = FindBestResidual(channelSamples, order, predictor, policy);
                FlacLpcMethodCoefficeints lpcCoefficients = new FlacLpcMethodCoefficeints(
                    precision, integerCoefficients, shift);

                FlacMethod method = new FlacLpcMethod(bitsPerSample, lpcCoefficients, residual);
                methods[order - 1] = method;
            });

            return FindBestMethod(methods);
        }

        private int GetPrecisionForSigned(int[] array)
        {
            int min, max;
            min = max = array[0];
            for (int i = 1; i < array.Length; i++)
            {
                if (min > array[i]) min = array[i];
                if (max < array[i]) max = array[i];
            }
            return GetBitsForSignedNumbersInRange(min, max);
        }

        private void ConvertLpcCoeficientsToIntegers(double[] coef, out int[] integerCoef, out int precision, out int shift)
        {
            double maxOrder = 0;
            for (int i = 0; i < coef.Length; i++)
            {
                double order = Math.Log(Math.Abs(coef[i]), 2);
                if (maxOrder < order) maxOrder = order;
            }

            const int ActualBitsInCoeficients = 14;
            shift = ActualBitsInCoeficients - (int)Math.Ceiling(maxOrder);

            integerCoef = new int[coef.Length];

            do
            {
                double coeficientMultiplier = Math.Pow(2, shift);
                for (int i = 0; i < coef.Length; i++)
                {
                    integerCoef[i] = (int)Math.Round(coef[i] * coeficientMultiplier);
                }

                precision = GetPrecisionForSigned(integerCoef);

                if (precision < 16) break;

                shift--;
            } while (precision >= 16);
        }

        private double[] SolveLpc(double[] r, int p)
        {
            double[] y = new double[p];
            double[] t = new double[p * 2 - 1];
            for (int i = 0; i < p; i++)
            {
                y[i] = r[i + 1];
            }

            t[p - 1] = r[0];
            for (int i = 1; i < p; i++)
            {
                t[p + i - 1] = r[i];
                t[p - i - 1] = r[i];
            }

            return LevinsonRecursion.Solve(p, t, y);
        }

        private FlacMethod FindBestFixedMethod(int[] channelSamples, int bitsPerSample, FlacEncodingPolicy policy)
        {
            if (!policy.FixedOrder.HasValue) return null;

            int minFixedOrder = policy.FixedOrder.Value.MinValue;
            int maxFixedOrder = Math.Min(channelSamples.Length - 1, 
                policy.FixedOrder.Value.MaxValue);

            FlacMethod[] methods = new FlacMethod[maxFixedOrder + 1];
            parallel.For(minFixedOrder, maxFixedOrder + 1, order =>
            {
                IPredictor predictor = PredictorFactory.CreateFixedPredictor(order, ArrayUtils.CutArray(channelSamples, 0, order - 1));
                FlacResidualCoefficeints residual = FindBestResidual(channelSamples, order, predictor, policy);

                FlacMethod method = new FlacFixedMethod(bitsPerSample, order, residual);
                methods[order] = method;
            });

            return FindBestMethod(methods);
        }

        private FlacMethod FindBestMethod(IList<FlacMethod> methods)
        {
            int bestMethodIndex = 0;
            while (bestMethodIndex < methods.Count && methods[bestMethodIndex] == null)
                bestMethodIndex++;

            if (bestMethodIndex == methods.Count) throw new FlacException("No valid method");

            for (int i = bestMethodIndex + 1; i < methods.Count; i++)
            {                
                if (methods[i] != null && methods[i].EstimatedSize < methods[bestMethodIndex].EstimatedSize)
                    bestMethodIndex = i;
            }
            return methods[bestMethodIndex];
        }

        private FlacResidualCoefficeints FindBestResidual(int[] channelSamples, int order, IPredictor predictor, FlacEncodingPolicy policy)
        {
            int[] residual;
            if (order > 0)
            {
                residual = new int[channelSamples.Length];
                int lastSample = channelSamples[order - 1];
                for (int i = order; i < residual.Length; i++)
                {
                    int nextSample = channelSamples[i];
                    residual[i] = nextSample - predictor.Next(lastSample);
                    lastSample = nextSample;
                }
            }
            else
                residual = channelSamples;

            int minRiceOrder = policy.RicePartionOrder.MinValue;
            int maxRiceOrder = policy.RicePartionOrder.MaxValue;
            List<FlacResidualCoefficeints> rices = new List<FlacResidualCoefficeints>();
            int samplesPerPartition = channelSamples.Length >> minRiceOrder;

            if (samplesPerPartition << minRiceOrder != channelSamples.Length)
            {
                minRiceOrder = maxRiceOrder = 0; // reset minRiceOrder to zero;
            }

            for (int riceOrder = minRiceOrder; riceOrder <= maxRiceOrder; riceOrder++)
            {
                if (samplesPerPartition <= order) break;

                int partitionCount = 1 << riceOrder;

                int[] parameters = new int[partitionCount];
                int totalPartitionDataSize = 0;
                int j = order;
                for (int i = 0; i < partitionCount; i++)
                {
                    int skipAmount = i == 0 ? order : 0;
                    int estimatedPartitionSize;
                    int riceParameter;
                    FindBestResidual(residual, samplesPerPartition * i + skipAmount, samplesPerPartition - skipAmount,
                        out estimatedPartitionSize, out riceParameter);
                    totalPartitionDataSize += estimatedPartitionSize;
                    parameters[i] = riceParameter;
			    }

                const int NormalPrecision = 4;
                const int ExtendedPrecision = 5;
                const int MinValueForExtendedParameters = 15;

                bool isExtended = Array.Exists(parameters, delegate(int x) {
                    return x >= MinValueForExtendedParameters;
                });

                int totalSize = 4 + totalPartitionDataSize +
                    partitionCount * (isExtended ? ExtendedPrecision : NormalPrecision);

                FlacResidualCoefficeints rice = new FlacResidualCoefficeints();
                rice.EstimatedSize = totalSize;
                rice.IsExtended = isExtended;
                rice.RiceParameters = parameters;
                rice.Order = riceOrder;

                rices.Add(rice);

                if ((samplesPerPartition & 1) != 0) break;
                samplesPerPartition >>= 1;
            }

            int bestRicePartition = 0;
            for (int i = 1; i < rices.Count; i++)
            {
                if (rices[bestRicePartition].EstimatedSize > rices[i].EstimatedSize)
                    bestRicePartition = i;
            }
            return rices[bestRicePartition];
        }

        private void FindBestResidual(int[] residual, int offset, int count, out int estimatedSize, out int riceParameter)
        {
            int bitsForSignedNumber = GetPrecisionForSigned(residual);

            int escapedVersionSize = bitsForSignedNumber * count + 5;

            if (bitsForSignedNumber < 2)
            {
                estimatedSize = escapedVersionSize;
                riceParameter = ~bitsForSignedNumber;
                return;
            }

            int maxRiceParameter = bitsForSignedNumber - 2;
            int[] riceVersionSizes = new int[maxRiceParameter + 1];
            for (int m = 0; m <= maxRiceParameter; m++)
            {
                riceVersionSizes[m] = (m + 1) * count;
            }

            for (int i = 0; i < count; i++)
            {
                int sample = residual[offset + i];
                if (sample < 0)
                {
                    riceVersionSizes[0]++;
                    sample = ~sample;
                }
                riceVersionSizes[0] += sample << 1;

                for (int m = 1; m <= maxRiceParameter; m++)
                {
                    riceVersionSizes[m] += sample;
                    sample >>= 1;
                }
            }

            int bestRiceIndex = 0;
            for (int m = 1; m <= maxRiceParameter; m++)
            {
                if (riceVersionSizes[bestRiceIndex] > riceVersionSizes[m])
                    bestRiceIndex = m;
            }

            if (riceVersionSizes[bestRiceIndex] <= escapedVersionSize)
            {
                estimatedSize = riceVersionSizes[bestRiceIndex];
                riceParameter = bestRiceIndex;
            }
            else
            {
                estimatedSize = escapedVersionSize;
                riceParameter = ~bitsForSignedNumber;
            }
        }

        // Tried Math.Log(Log2 * residualAbsDataSum / residual.Length, 2);
        // Performance is sampe as full search

        private int GetBitsForSignedNumbersInRange(int min, int max)
        {
            System.Diagnostics.Debug.Assert(min <= max);

            if (min >= 0 || max >= ~min)
                return GetBitsForNumber((uint)max) + 1;
            else 
                return GetBitsForNumber((uint)~min) + 1;
        }

        private int GetBitsForNumber(uint n)
        {
            int bits = 0;
            uint mask = 1;
            if (n >= 0x100)
            {
                bits += 8;
                if (n >= 0x10000)
                {
                    bits += 8;
                    if (n >= 0x1000000)
                        bits += 8;
                }
                mask <<= bits;
            }
            while (mask <= n)
            {
                mask <<= 1;
                bits++;
            }
            return bits;
        }

        internal override FlacMethodAndDataPair[] FindBestMethods(int[][] samples, int bitsPerSamples)
        {
            FlacMethodAndDataPair[] samplesAndMethods = new FlacMethodAndDataPair[samples.Length];
            parallel.For(0, samples.Length, i =>
            {
                samplesAndMethods[i] = new FlacMethodAndDataPair(
                    FindBestMethod(samples[i], bitsPerSamples),
                    bitsPerSamples, 
                    samples[i]);
            });
            return samplesAndMethods;
        }

        internal override SoundChannelAssignmentType FindBestMethods(int[] leftSamples, int[] rightSamples, int bitsPerSample, out FlacMethodAndDataPair[] methods)
        {
            if (policy.StereoEncoding == StereoEncodingPolicy.AsIs)
            {
                methods = FindBestMethods(new int[2][] { leftSamples, rightSamples }, bitsPerSample);
                return SoundChannelAssignmentType.LeftRight;
            }

            int samplesPerChannel = leftSamples.Length;
            int[] differenceLeftMinusRight = null, average = null;

            FlacMethod methodForLeft = null, methodForRight = null, methodForSide = null, methodForAverage = null;

            parallel.Invoke(
                delegate
                {
                    methodForLeft = FindBestMethod(leftSamples, bitsPerSample);
                },
                delegate
                {
                    methodForRight = FindBestMethod(rightSamples, bitsPerSample);
                },
                delegate
                {
                    differenceLeftMinusRight = new int[samplesPerChannel];
                    for (int i = 0; i < samplesPerChannel; i++)
                    {
                        differenceLeftMinusRight[i] = leftSamples[i] - rightSamples[i];
                    }
                    methodForSide = FindBestMethod(differenceLeftMinusRight, bitsPerSample + 1);
                },
                delegate
                {
                    if (policy.StereoEncoding == StereoEncodingPolicy.TrySidesAndAverage)
                    {
                        average = new int[samplesPerChannel];
                        for (int i = 0; i < samplesPerChannel; i++)
                        {
                            average[i] = (leftSamples[i] + rightSamples[i]) >> 1;
                        }
                        methodForAverage = FindBestMethod(average, bitsPerSample);
                    }
                }
            );

            int independentEstimation = methodForLeft.EstimatedSize + methodForRight.EstimatedSize;
            int leftSideEstimation = methodForLeft.EstimatedSize + methodForSide.EstimatedSize;
            int rightSideEstimation = methodForSide.EstimatedSize + methodForRight.EstimatedSize;
            int averageEstimation = average == null ? int.MaxValue :
                methodForAverage.EstimatedSize + methodForSide.EstimatedSize;

            SoundChannelAssignmentType type;
            if (Math.Min(independentEstimation, leftSideEstimation) < Math.Min(rightSideEstimation, averageEstimation))
            {
                if (independentEstimation <= leftSideEstimation)
                {
                    type = SoundChannelAssignmentType.LeftRight;
                    methods = new FlacMethodAndDataPair[] {
                        new FlacMethodAndDataPair(methodForLeft, bitsPerSample, leftSamples), 
                        new FlacMethodAndDataPair(methodForRight, bitsPerSample, rightSamples)
                    };
                }
                else
                {
                    type = SoundChannelAssignmentType.LeftSide;
                    methods = new FlacMethodAndDataPair[] {
                        new FlacMethodAndDataPair(methodForLeft, bitsPerSample, leftSamples), 
                        new FlacMethodAndDataPair(methodForSide, bitsPerSample + 1, differenceLeftMinusRight)
                    };
                }
            }
            else
            {
                if (rightSideEstimation <= averageEstimation)
                {
                    type = SoundChannelAssignmentType.RightSide;
                    methods = new FlacMethodAndDataPair[] {
                        new FlacMethodAndDataPair(methodForSide, bitsPerSample + 1, differenceLeftMinusRight), 
                        new FlacMethodAndDataPair(methodForRight, bitsPerSample, rightSamples)
                    };
                }
                else
                {
                    type = SoundChannelAssignmentType.MidSide;
                    methods = new FlacMethodAndDataPair[] {
                        new FlacMethodAndDataPair(methodForAverage, bitsPerSample, average), 
                        new FlacMethodAndDataPair(methodForSide, bitsPerSample + 1, differenceLeftMinusRight)
                    };
                }
            }
            return type;
        }
    }
}
