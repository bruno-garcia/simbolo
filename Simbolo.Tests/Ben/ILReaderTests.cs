using System.Diagnostics.Internal;

using Xunit;

namespace Ben.Demystifier.Test
{
    public class ILReaderTests
    {
        public static TheoryData<byte[]> InlineCilSamples => 
            new TheoryData<byte[]>
            {
                //  https://github.com/benaadams/Ben.Demystifier/issues/56#issuecomment-366490463
                { new byte[] {
                    2, 123, 209, 5, 0, 4, 20, 254, 1, 114, 193, 103, 1, 112, 40, 160, 22, 0, 6,
                    2, 115, 183, 10, 0, 10, 125, 210, 5, 0, 4, 2, 123, 212, 5, 0, 4, 2, 123, 211,
                    5, 0, 4, 40, 221, 15, 0, 6, 44, 68, 2, 123, 212, 5, 0, 4, 111, 103, 17, 0, 6, 2,
                    111, 222, 9, 0, 6, 2, 40, 184, 10, 0, 10, 2, 254, 6, 249, 15, 0, 6, 115, 185, 10,
                    0, 10, 2, 123, 210, 5, 0, 4, 111, 186, 10, 0, 10, 22, 40, 101, 6, 0, 10, 111, 221,
                    0, 0, 43, 40, 188, 10, 0, 10, 125, 209, 5, 0, 4, 42, 2, 123, 212, 5, 0, 4, 111,
                    103, 17, 0, 6, 111, 216, 9, 0, 6, 2, 123, 211, 5, 0, 4, 111, 166, 14, 0, 6, 111,
                    125, 16, 0, 6, 254, 1, 22, 254, 1, 114, 235, 103, 1, 112, 40, 160, 22, 0, 6, 114,
                    160, 104, 1, 112, 40, 210, 0, 0, 10, 114, 194, 5, 0, 112, 40, 221, 0, 0, 10, 44, 51,
                    2, 40, 184, 10, 0, 10, 2, 254, 6, 250, 15, 0, 6, 115, 185, 10, 0, 10, 2, 123, 210,
                    5, 0, 4, 111, 186, 10, 0, 10, 22, 40, 196, 21, 0, 6, 111, 221, 0, 0, 43, 40, 188,
                    10, 0, 10, 125, 209, 5, 0, 4, 42, 2, 40, 184, 10, 0, 10, 2, 254, 6, 251, 15, 0, 6,
                    115, 185, 10, 0, 10, 2, 123, 210, 5, 0, 4, 111, 186, 10, 0, 10, 24, 40, 101, 6, 0,
                    10, 111, 221, 0, 0, 43, 40, 188, 10, 0, 10, 125, 209, 5, 0, 4, 42
                } },

                //  https://github.com/benaadams/Ben.Demystifier/issues/56#issuecomment-390654651
                { new byte[] {
                    115, 31, 5, 0, 6, 37, 2, 125, 94, 1, 0, 4, 37, 3, 125, 91, 1, 0, 4, 37, 4, 125, 92,
                    1, 0, 4, 37, 5, 125, 93, 1, 0, 4, 37, 123, 91, 1, 0, 4, 40, 61, 0, 0, 10, 44, 16,
                    40, 160, 4, 0, 6, 114, 253, 15, 0, 112, 115, 90, 0, 0, 10, 122, 254, 6, 32, 5, 0,
                    6, 115, 137, 2, 0, 10, 115, 61, 2, 0, 6, 42
                } },

                { new byte[] {
                    31, 254, 115, 42, 2, 0, 6, 37, 2, 125, 159, 0, 0, 4, 37, 3, 125, 158, 0, 0, 4, 42
                } },
            };

        //  https://github.com/benaadams/Ben.Demystifier/issues/56
        [Theory, MemberData(nameof(InlineCilSamples))]
        public void ReadsInlinedOpcodes(byte[] cil)
        {
            var sut = new ILReader(cil);
            while (sut.Read(GetType().GetMethod(nameof(ReadsInlinedOpcodes))))
            {
            }
        }
    }
}
