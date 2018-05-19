using Kestrel.Transport.Quic.Internal.Headers;

namespace Kestrel.Transport.Quic.Internal.Packets
{
    public class InitialPacket : AbstractPacketBase
    {
        public InitialPacket(BaseHeader header)
            : base(header)
        {

        }

        public override string ToString()
        {
            return $"{this.Header} Initial(0x7f)";
        }
    }
}