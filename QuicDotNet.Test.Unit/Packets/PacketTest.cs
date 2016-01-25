﻿namespace QuicDotNet.Test.Unit.Packets
{
    using System.Diagnostics;
    using System.Linq;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    using QuicDotNet.Frames;
    using QuicDotNet.Packets;
    using QuicDotNet.Test.Unit.Messages;

    [TestClass]
    public class PacketTest
    {
        [TestMethod]
        public void FreshHello()
        {
            var connectionId = 15690248817103694251U;

            var packet = new RegularPacket(connectionId, 1, null);
            Assert.IsNotNull(packet.ConnectionId);
            packet.AddFrame(new StreamFrame(ClientHandshakeMessageTest.ClientInchoateGoogleFreshParametersClientMessageFactory.Value, false, 1, 0));
            var packetBytes = packet.PadAndNullEncrypt();
            Assert.IsNotNull(packet.MessageAuthenticationHash);

            Debug.WriteLine("Message authentication hash: " + packet.MessageAuthenticationHash.Select(b => b.ToString("x2")).Aggregate((c, n) => c + " " + n));
            Debug.WriteLine(packetBytes.GenerateHexDumpWithASCII());

            Assert.AreEqual(PacketLibrary.ClientInchoateGoogleFresh.Length, packetBytes.Length);

            // Soft warn
            for (var i = 0; i < packetBytes.Length; i++)
            {
                if (packetBytes[i] != PacketLibrary.ClientInchoateGoogleFresh[i])
                    Debug.WriteLine($"Byte difference at position {i}: generated byte is {packetBytes[i]:x2} but reference byte was {PacketLibrary.ClientInchoateGoogleFresh[i]:x2}");
            }

            // Hard test fail
            for (var i = 0; i < packetBytes.Length; i++)
                Assert.AreEqual(PacketLibrary.ClientInchoateGoogleFresh[i], packetBytes[i], $"Byte difference at position {i}: generated byte is {packetBytes[i]:x2} but reference byte was {PacketLibrary.ClientInchoateGoogleFresh[i]:x2}");

            // Now try to reverse engineer it to ensure packet parsing works.
            AbstractPacketBase rePacket;
            AbstractPacketBase.TryParse(packetBytes, out rePacket);

            Assert.IsNotNull(rePacket);
            Assert.IsInstanceOfType(rePacket, typeof(RegularPacket));
            var rp = (RegularPacket)rePacket;
            Assert.AreEqual(rp.ConnectionId, packet.ConnectionId.Value);
            Assert.AreEqual(rp.Entropy, packet.Entropy);
            Assert.AreEqual(rp.FecGroup, packet.FecGroup);
            Assert.IsNotNull(rp.MessageAuthenticationHash);
            Assert.AreEqual(rp.MessageAuthenticationHash.Sum(b => (int)b), packet.MessageAuthenticationHash.Sum(b => (int)b));
            Assert.AreEqual(rp.PacketNumber, packet.PacketNumber);

            var rpBytes = rp.ToByteArray();
            //Debug.WriteLine(rpBytes.GenerateHexDumpWithASCII());
            Assert.AreEqual(packetBytes.Length, rpBytes.Length);

            // Soft reverse engineering warn
            for (var i = 0; i < packetBytes.Length; i++)
            {
                if (packetBytes[i] != rpBytes[i])
                    Debug.WriteLine($"Byte difference at position {i}: generated byte is {rpBytes[i]:x2} but reference byte was {packetBytes[i]:x2}");
            }
            
            // Hard test fail
            for (var i = 0; i < packetBytes.Length; i++)
                Assert.AreEqual(packetBytes[i], rpBytes[i], $"Byte difference at position {i}: generated byte is {rpBytes[i]:x2} but reference byte was {packetBytes[i]:x2}");
        }
    }
}
