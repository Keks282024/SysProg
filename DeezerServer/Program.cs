using System;
using System.Threading;

namespace DeezerServer
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Logger.Clear();

            Server server = new Server("http://localhost:5050/");

            Thread listenerThread = new Thread(server.Start);
            listenerThread.Start();

            Logger.Info("Server je pokrenut.");
            Logger.Info("Probaj u browseru: http://localhost:5050/search?q=coldplay");
            Logger.Info("Pritisni ENTER za zaustavljanje servera...");

            Console.ReadLine();

            Logger.Info("Zaustavljanje servera...");

            server.Stop();
            listenerThread.Join();

            Logger.Info("Server je zaustavljen.");
        }
    }
}