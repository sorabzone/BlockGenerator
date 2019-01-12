namespace GenerateBlock
{
    public class Transaction
    {
        public int ID;
        public int Size;
        public short Fee;
    }

    public class Combination
    {
        public int TotalSize;
        public int TotalFee;
    }

    public class Answer
    {
        public int TotalSize;
        public int TotalFee;
        public string Detail;
    }
}
