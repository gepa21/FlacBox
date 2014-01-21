using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FlacBox
{
    static class LevinsonRecursion
    {
        internal static double[] Solve(int n, double[] t, double[] y)
        {
            System.Diagnostics.Debug.Assert(t.Length == n * 2 - 1 && y.Length == n);

            double[] fPrev = new double[] { 1 / t[n - 1] };
            double[] bPrev = new double[] { 1 / t[n - 1] };
            double[] x = new double[n];

            x[0] = y[0] * bPrev[0];

            for (int k = 2; k <= n; k++)
            {
                double ef = 0, eb = 0, ex = 0;
                for (int i = 1; i < k; i++)
                {
                    ef += t[(n - 1) + k - i] * fPrev[i - 1];
                    eb += t[(n - 1) - i] * bPrev[i - 1];
                    ex += t[(n - 1) + k - i] * x[i - 1];
                }

                double[] f = new double[k];
                double[] b = new double[k];

                double coef = 1 / (1 - ef * eb);
                f[0] = coef * fPrev[0];
                for (int i = 1; i < k - 1; i++)
                {
                    f[i] = coef * fPrev[i] - ef * coef * bPrev[i - 1];
                }
                f[k - 1] = -ef * coef * bPrev[k - 2];

                b[0] = -eb * coef * fPrev[0];
                for (int i = 1; i < k - 1; i++)
                {
                    b[i] = coef * bPrev[i - 1] - eb * coef * fPrev[i];
                }
                b[k - 1] = coef * bPrev[k - 2];

                double[] newX = new double[k];
                for (int i = 0; i < k - 1; i++)
                {
                    newX[i] = x[i] + (y[k - 1] - ex) * b[i];
                }
                newX[k - 1] = (y[k - 1] - ex) * b[k - 1];

                fPrev = f;
                bPrev = b;
                x = newX;
            }

            return x;
        }

        //[System.Diagnostics.Conditional("DEBUG")]
        //private static void Check(int n, double[] t, double[] y, double[] x)
        //{
        //    for (int i = 0; i < n; i++)
        //    {
        //        double res = 0;
        //        for (int j = 0; j < n; j++)
        //        {
        //            res += x[j] * t[n - 1 + i - j];
        //        }

        //        System.Diagnostics.Debug.Assert(Math.Abs((res - y[i]) / res) < 1e-3);
        //    }
        //}
    }
}
