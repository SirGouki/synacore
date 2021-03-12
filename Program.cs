using System;

namespace synacore.NET
{
    class Program
    {
        static void Main(string[] args)
        {
            Emu emu = new Emu();

            emu.LoadROM();
            emu.Emulate();

            Console.WriteLine("Program terminated.  Press any key to exit . . .");
            Console.ReadKey(true);
        }
    }
}
