namespace Kestrel.Transport.Quic.Internal.Packets
{
    public enum PacketType : byte
    {
        Initial = 0x7F,
        Retry = 0x7E,
        Handshake = 0x7D,
        ZeroRTTProtected = 0x7C,
    }
}