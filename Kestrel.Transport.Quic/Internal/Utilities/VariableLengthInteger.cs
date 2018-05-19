using System;

namespace Kestrel.Transport.Quic.Internal.Utilities
{
    public static class VariableLengthInteger
    {
        public static long Parse(Span<byte> buffer, ref int offset)
        {
            byte lengthIndicator = buffer.Slice(offset++, 1)[0];
            int count = 0;
            if ((lengthIndicator & 0x40) != 0) {
                count += 1;
                lengthIndicator -= 0x40;
            }
            if ((lengthIndicator & 0x80) != 0) {
                count += 2;
                lengthIndicator -= 0x80;
            }

            long result = lengthIndicator;
            for (var i = 1; i < Math.Pow(2, count); i++) {
                result = result << 8;
                result += buffer.Slice(offset++, 1)[0];
            }

            return result;
        }
    }
}