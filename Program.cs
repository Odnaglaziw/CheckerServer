using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CheckerServer;

public class Program
{
    public static void Main(string[] args)
    {
        var server = new Server();

        Task.Run(async () => await server.StartAsync("http://+:65000/"));

        Console.WriteLine("Нажмите Enter для завершения работы.");
        Console.ReadLine();
    }

    public static void Log(string text)
    {
        Console.WriteLine($"{DateTime.Now}: {text}");
    }

    public static void LogError(string text)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"{DateTime.Now}: {text}");
        Console.ResetColor();
    }
}
