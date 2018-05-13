using System;
using System.Threading.Tasks;

namespace QuicDotNet.Test.Console
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var conn = new QuicClient();
            await conn.ConnectAsync("localhost", 5000);

            System.Console.WriteLine("Press ENTER to quit");
            System.Console.ReadLine();
        }
    }
}
