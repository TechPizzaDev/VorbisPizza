
namespace NVorbis.Ogg
{
    /// <summary>
    /// Encapsulates a method that calculates the number of granules in a packet.
    /// </summary>
    public interface IPacketGranuleCountProvider
    {
        /// <summary>
        /// Calculates the number of granules decodable from the specified packet.
        /// </summary>
        /// <param name="packet">The <see cref="VorbisPacket"/> to calculate.</param>
        /// <param name="isLastInPage"><see langword="true"/> if the packet is the last in the page, otherise <see langword="false"/>.</param>
        /// <returns>The calculated number of granules.</returns>
        int GetPacketGranuleCount(ref VorbisPacket packet, bool isLastInPage);
    }
}
