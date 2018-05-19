namespace Kestrel.Transport.Quic.Internal.Headers
{
    public abstract class BaseHeader
    {
        public string DestinationConnectionID { get; protected set; }
        public long PacketNumber { get; protected set; }
    }
}