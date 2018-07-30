using System;
using CakeLib;

namespace CakeConsoleApp
{
    class Program
    {
        static void Main(string[] args)
        {
            var c = new Cake {name = "Bakewell tart"};
            Console.WriteLine(c.name);
        }
    }
}
