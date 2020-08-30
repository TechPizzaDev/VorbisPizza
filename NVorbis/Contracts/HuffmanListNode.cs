
namespace NVorbis.Contracts
{
    internal class HuffmanListNode
    {
        public int Value;
        public int Length;
        public int Bits;
        public int Mask;

        public HuffmanListNode(int value, int length, int bits, int mask)
        {
            Value = value;
            Length = length;
            Bits = bits;
            Mask = mask;
        }
    }
}
