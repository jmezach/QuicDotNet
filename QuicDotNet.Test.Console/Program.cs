using System;
using System.Threading.Tasks;

namespace QuicDotNet.Test.Console
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var conn = new QuicClient();
            await conn.ConnectAsync("www.google.com", 443);

            System.Console.WriteLine("Press ENTER to quit");
            System.Console.ReadLine();
        }
    }
}
