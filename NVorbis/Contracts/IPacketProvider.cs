using System;
using NVorbis.Ogg;

namespace NVorbis.Contracts
{
    /// <summary>
    /// Encapsulates a method that calculates the number of granules decodable from the specified packet.
    /// </summary>
    /// <param name="packet">The <see cref="VorbisPacket"/> to calculate.</param>
    /// <param name="isLastInPage"><see langword="true"/> if the packet is the last in the page, otherise <see langword="false"/>.</param>
    /// <returns>The calculated number of granules.</returns>
    public delegate int GetPacketGranuleCount(ref VorbisPacket packet, bool isLastInPage);

    /// <summary>
    /// Describes an interface for a packet stream reader.
    /// </summary>
    public interface IPacketProvider : IDisposable
    {
        /// <summary>
        /// Gets whether the provider supports seeking.
        /// </summary>
        bool CanSeek { get; }

        /// <summary>
        /// Gets the serial number of this provider's data stream.
        /// </summary>
        int StreamSerial { get; }

        /// <summary>
        /// Gets the next packet in the stream and advances to the next packet position.
        /// </summary>
        /// <returns>The <see cref="VorbisPacket"/> for the next packet if available.</returns>
        VorbisPacket GetNextPacket();

        /// <summary>
        /// Seeks the stream to the packet that is prior to the requested granule position by the specified preroll number of packets.
        /// </summary>
        /// <param name="granulePos">The granule position to seek to.</param>
        /// <param name="preRoll">The number of packets to seek backward prior to the granule position.</param>
        /// <param name="getPacketGranuleCount">
        /// A <see cref="GetPacketGranuleCount"/> delegate that returns the number of granules in the specified packet.
        /// </param>
        /// <returns>The granule position at the start of the packet containing the requested position.</returns>
        long SeekTo(long granulePos, uint preRoll, GetPacketGranuleCount getPacketGranuleCount);

        /// <summary>
        /// Gets the total number of granule available in the stream.
        /// </summary>
        long GetGranuleCount();

        /// <summary>
        /// Gets packet data for the requested position.
        /// </summary>
        /// <param name="dataPart">The packet data position.</param>
        /// <returns>The packet data segment.</returns>
        ArraySegment<byte> GetPacketData(PacketDataPart dataPart);

        /// <summary>
        /// Used to finish a packet. Using a finished packet is undefined behavior.
        /// </summary>
        /// <param name="packet">The packet to finish.</param>
        void FinishPacket(in VorbisPacket packet);
    }
}
