using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FlacBox
{
    /// <summary>
    /// Interface for all predictors.
    /// </summary>
    interface IPredictor 
    {
        int Next(int x);
        long NextLong(int x);
    }

    /// <summary>
    /// Predictor factory.
    /// </summary>
    static class PredictorFactory
    {
        internal static IPredictor CreateFixedPredictor(int order, int[] samples)
        {
            int[] coefficients;
            switch (order)
            {
                case 0:
                    return new LpcZeroOrderPredictor();
                case 1:
                    return new LpcFirstOrderPredictor(1);
                case 2:
                    return new LpcSecondOrderPredictor(2, -1, samples[0]);
                case 3: coefficients = new int[] { 3, -3, 1 }; break;
                case 4: coefficients = new int[] { 4, -6, 4, -1 }; break;
                default:
                    throw new NotSupportedException();
            }
            return new LpcPredictor(coefficients, samples);
        }

        internal static IPredictor CreateLpcPredictor(int[] coefficients, int shift, int[] samples)
        {
            IPredictor basePredictor;
            switch (coefficients.Length)
            {
                case 0:
                    throw new NotSupportedException();
                case 1:
                    basePredictor = new LpcFirstOrderPredictor(coefficients[0]);
                    break;
                case 2:
                    basePredictor = new LpcSecondOrderPredictor(coefficients[0], coefficients[1], samples[0]);
                    break;
                default:
                    basePredictor = new LpcPredictor(coefficients, samples);
                    break;
            }

            if (shift == 0)
                return basePredictor;
            else if (shift > 0)
                return new RightShiftPedictor(basePredictor, shift);
            else
                return new LeftShiftPedictor(basePredictor, shift);
        }

        class LeftShiftPedictor : IPredictor
        {
            int shift;
            IPredictor basePredictor;

            public LeftShiftPedictor(IPredictor basePredictor, int shift)
            {
                this.basePredictor = basePredictor;
                this.shift = shift;
            }

            public int Next(int x)
            {
                return basePredictor.Next(x) << shift;
            }

            public long NextLong(int x)
            {
                return basePredictor.NextLong(x) << shift;
            }
        }

        class RightShiftPedictor : IPredictor
        {
            int shift;
            IPredictor basePredictor;

            public RightShiftPedictor(IPredictor basePredictor, int shift)
            {
                this.basePredictor = basePredictor;
                this.shift = shift;
            }

            public int Next(int x)
            {
                return (int)NextLong(x);
            }

            public long NextLong(int x)
            {
                return basePredictor.NextLong(x) >> shift;
            }
        }

        class LpcZeroOrderPredictor : IPredictor
        {
            public int Next(int x)
            {
                return 0;
            }

            public long NextLong(int x)
            {
                return 0;
            }
        }

        class LpcFirstOrderPredictor : IPredictor
        {
            int coefficient;

            public LpcFirstOrderPredictor(int coefficient)
            {
                this.coefficient = coefficient;
            }

            public int Next(int x)
            {
                return x * coefficient;
            }

            public long NextLong(int x)
            {
                return x * (long)coefficient;
            }
        }

        class LpcSecondOrderPredictor : IPredictor
        {
            int coefficient1;
            int coefficient2;
            int lastX;

            public LpcSecondOrderPredictor(int coefficient1, int coefficient2, int lastX)
            {
                this.coefficient1 = coefficient1;
                this.coefficient2 = coefficient2;
                this.lastX = lastX;
            }

            public int Next(int x)
            {
                int result = x * coefficient1 + lastX * coefficient2;
                lastX = x;
                return result;
            }

            public long NextLong(int x)
            {
                long result = x * (long)coefficient1 + lastX * (long)coefficient2;
                lastX = x;
                return result;
            }
        }

        class LpcPredictor : IPredictor
        {
            int[] samples;
            int order;
            int[] coefficients;
            int currentSample;

            public LpcPredictor(int[] coefficients, int[] samples)
            {
                System.Diagnostics.Debug.Assert(coefficients.Length == samples.Length + 1);

                this.coefficients = coefficients;
                this.order = coefficients.Length;

                this.samples = new int[this.order - 1];
                Array.Copy(samples, this.samples, this.samples.Length);
                this.currentSample = 0;
            }

            public int Next(int x)
            {
                return (int)NextLong(x);
            }

            public long NextLong(int x)
            {
                long sum = x * (long)coefficients[0];
                for (int i = 1; i < order; i++)
                {
                    if (--currentSample < 0) currentSample = samples.Length - 1;
                    sum += samples[currentSample] * (long)coefficients[i];
                }
                samples[currentSample] = x;
                if (++currentSample >= samples.Length) currentSample = 0;

                return sum;
            }
        }
    }
}
