using System;
using Kestrel.Transport.Quic.Internal.Headers;

namespace Kestrel.Transport.Quic.Internal.Packets
{
    public abstract class AbstractPacketBase
    {
        protected AbstractPacketBase(BaseHeader header)
        {
            this.Header = header;
        }

        public BaseHeader Header { get; }

        public static AbstractPacketBase Parse(Span<byte> buffer)
        {
            byte publicFlags = buffer[0];

            int offset = 1;
            bool longHeader = (publicFlags & 0x08) != 0;
            BaseHeader header = longHeader ? new LongHeader(buffer, ref offset) : null;

            // Read packet type
            AbstractPacketBase result = null;
            if ((publicFlags & (byte)PacketType.Initial) != 0) {
                result = new InitialPacket(header);
            } else if ((publicFlags & 0x7E) != 0) {
                // TODO
            } else if ((publicFlags & 0x7D) != 0) {
                // TODO
            } else if ((publicFlags & 0x7c) != 0) {
                // TODO
            }

            return result;
        }
    }
}