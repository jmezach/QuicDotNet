using System;
using System.Text;
using Kestrel.Transport.Quic.Internal.Packets;
using Kestrel.Transport.Quic.Internal.Utilities;

namespace Kestrel.Transport.Quic.Internal.Headers
{
    public class LongHeader : BaseHeader
    {
        /// <remarks>
        ///     0                   1                   2                   3
        ///    0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
        ///   +-+-+-+-+-+-+-+-+
        ///   |1|   Type (7)  |
        ///   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        ///   |                         Version (32)                          |
        ///   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        ///   |DCIL(4)|SCIL(4)|
        ///   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        ///   |               Destination Connection ID (0/32..144)         ...
        ///   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        ///   |                 Source Connection ID (0/32..144)            ...
        ///   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        ///   |                       Payload Length (i)                    ...
        ///   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        ///   |                       Packet Number (32)                      |
        ///   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        ///   |                          Payload (*)                        ...
        ///   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        /// </remarks>
        /// <param name="buffer">Buffer to parse the long header from.</param>
        /// <param name="offset">Current offset.</param>
        public LongHeader(Span<byte> buffer, ref int offset)
        {
            // Read version (32-bit)
            this.Version = BitConverter.ToString(buffer.Slice(offset, 4).ToArray(), 0, 4);
            offset += 4;

            // Read length of destination and source connection ID's
            byte dcilscil = buffer.Slice(offset++, 1).ToArray()[0];
            int dcil = dcilscil >> 4;
            int scil = dcilscil & 0x0F;

            if (dcil != 0) { dcil += 3; }
            if (scil != 0) { scil += 3; }

            // Read destination and source connection ID's
            this.DestinationConnectionID = "0x" + BitConverter.ToString(buffer.Slice(offset, dcil).ToArray()).ToLower().Replace("-", string.Empty);
            offset += dcil;

            this.SourceConnectionID = "0x" + BitConverter.ToString(buffer.Slice(offset, scil).ToArray()).ToLower().Replace("-", string.Empty);
            offset += scil;

            // Read payload length (Variable length Integer)
            this.PayloadLength = VariableLengthInteger.Parse(buffer, ref offset);

            // Read packet number
            byte[] packetNumberBytes = buffer.Slice(offset, 4).ToArray();
            if (BitConverter.IsLittleEndian) {
                Array.Reverse(packetNumberBytes);
            }

            this.PacketNumber = BitConverter.ToUInt32(packetNumberBytes, 0);
            offset += 4;
        }

        public string Version { get; }
        public string SourceConnectionID { get; }
        public long PayloadLength { get; }

        public override string ToString()
        {
            return $"{this.SourceConnectionID} {this.PacketNumber} {this.PayloadLength}";
        }
    }
}