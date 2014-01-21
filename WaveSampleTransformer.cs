using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FlacBox
{
    /// <summary>
    /// Helps to pack/unpack binary data from/into integers.
    /// </summary>
    abstract class WaveSampleTransformer
    {
        internal abstract int PackData(IEnumerable<int> data, byte[] outputBuffer);
        internal abstract void UnpackData(byte[] data, int[] outputBuffer);
    }

    static class WaveSampleTransformerFactory
    {
        internal static WaveSampleTransformer CreateWaveSampleTransformer(int bitsInSample)
        {
            switch (bitsInSample)
            {
                case 8:
                    return new Pack8();
                case 16:
                    return new Pack16();
                case 24:
                    return new Pack24();
                case 32:
                    return new Pack32();
                default:
                    throw new NotSupportedException();
            }
        }

        internal static WaveSampleTransformer CreateWave16SampleTransformer(int bitsInSample)
        {
            switch (bitsInSample)
            {
                case 8:
                    return new Pack8To16();
                case 16:
                    return new Pack16();
                case 24:
                    return new Pack24To16();
                case 32:
                    return new Pack32To16();
                default:
                    throw new NotSupportedException();
            }
        }

        private class Pack8 : WaveSampleTransformer
        {
            internal override int PackData(IEnumerable<int> data, byte[] outputBuffer)
            {
                int i = 0;
                foreach (int value in data)
                {
                    outputBuffer[i++] = (byte)(value + 0x80);
                }
                return i;
            }

            internal override void UnpackData(byte[] data, int[] outputBuffer)
            {
                for (int i = 0; i < outputBuffer.Length; i++)
                {
                    outputBuffer[i] = data[i] - 0x80;
                }
            }
        }

        private class Pack8To16 : WaveSampleTransformer
        {
            internal override int PackData(IEnumerable<int> data, byte[] outputBuffer)
            {
                int i = 0;
                foreach (int value in data)
                {
                    outputBuffer[i++] = 0;
                    outputBuffer[i++] = (byte)(value);
                }
                return i;
            }

            internal override void UnpackData(byte[] data, int[] outputBuffer)
            {
                for (int i = 0; i < outputBuffer.Length; i++)
                {
                    outputBuffer[i] = data[i * 2 + 1];
                }
            }
        }

        private class Pack16 : WaveSampleTransformer
        {
            internal override int PackData(IEnumerable<int> data, byte[] outputBuffer)
            {
                int i = 0;
                foreach (int value in data)
                {
                    outputBuffer[i++] = (byte)(value & 0xFF);
                    outputBuffer[i++] = (byte)((value >> 8) & 0xFF);
                }
                return i;
            }

            internal override void UnpackData(byte[] data, int[] outputBuffer)
            {
                for (int i = 0; i < outputBuffer.Length; i++)
                {
                    outputBuffer[i] = (short)(data[2 * i] | (data[2 * i + 1] << 8));
                }
            }
        }

        private class Pack24 : WaveSampleTransformer
        {
            internal override int PackData(IEnumerable<int> data, byte[] outputBuffer)
            {
                int i = 0;
                foreach (int value in data)
                {
                    outputBuffer[i++] = (byte)(value & 0xFF);
                    outputBuffer[i++] = (byte)((value >> 8) & 0xFF);
                    outputBuffer[i++] = (byte)((value >> 16) & 0xFF);
                }
                return i;
            }

            internal override void UnpackData(byte[] data, int[] outputBuffer)
            {
                for (int i = 0; i < outputBuffer.Length; i++)
                {
                    outputBuffer[i] = (data[3 * i] | (data[3 * i + 1] << 8)) |
                        ((sbyte)data[3 * i + 2] << 16);
                }
            }
        }

        private class Pack24To16 : WaveSampleTransformer
        {
            internal override int PackData(IEnumerable<int> data, byte[] outputBuffer)
            {
                int i = 0;
                foreach (int value in data)
                {
                    outputBuffer[i++] = (byte)((value >> 8) & 0xFF);
                    outputBuffer[i++] = (byte)((value >> 16) & 0xFF);
                }
                return i;
            }

            internal override void UnpackData(byte[] data, int[] outputBuffer)
            {
                for (int i = 0; i < outputBuffer.Length; i++)
                {
                    outputBuffer[i] = (data[2 * i] << 8) |
                        ((sbyte)data[2 * i + 1] << 16);
                }
            }
        }

        private class Pack32 : WaveSampleTransformer
        {
            internal override int PackData(IEnumerable<int> data, byte[] outputBuffer)
            {
                int i = 0;
                foreach (int value in data)
                {
                    outputBuffer[i++] = (byte)(value & 0xFF);
                    outputBuffer[i++] = (byte)((value >> 8) & 0xFF);
                    outputBuffer[i++] = (byte)((value >> 16) & 0xFF);
                    outputBuffer[i++] = (byte)((value >> 24) & 0xFF);
                }
                return i;
            }

            internal override void UnpackData(byte[] data, int[] outputBuffer)
            {
                for (int i = 0; i < outputBuffer.Length; i++)
                {
                    outputBuffer[i] = (int)(data[4 * i] | (data[4 * i + 1] << 8)
                        | (data[4 * i + 2] << 16) | (data[4 * i + 3] << 24));                    
                }
            }
        }

        private class Pack32To16 : WaveSampleTransformer
        {
            internal override int PackData(IEnumerable<int> data, byte[] outputBuffer)
            {
                int i = 0;
                foreach (int value in data)
                {
                    outputBuffer[i++] = (byte)((value >> 16) & 0xFF);
                    outputBuffer[i++] = (byte)((value >> 24) & 0xFF);
                }
                return i;
            }

            internal override void UnpackData(byte[] data, int[] outputBuffer)
            {
                for (int i = 0; i < outputBuffer.Length; i++)
                {
                    outputBuffer[i] = (data[2 * i] << 16) | (data[2 * i + 1] << 24);
                }
            }
        }
    }
}
