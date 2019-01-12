using System;

namespace GenerateBlock
{
    class Program
    {
        static void Main(string[] args)
        {
            BlockWorker worker = new BlockWorker(1000000);
            worker.StartRunner().GetAwaiter().GetResult();
            Console.WriteLine("\nPlease any key to close...");
            Console.ReadKey();
        }
    }
}
