using System;

namespace MyApp
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine(new MyFunction.MyFunction().Execute("Hello, World!"));
            Console.ReadKey();
        }
    }
}
