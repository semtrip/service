using System;
using System.Collections.Generic;
using TwitchViewerBot.Core.Models;

namespace TwitchViewerBot.ConsoleUI.Helpers
{
    public static class ConsoleHelper
    {
        public static void PrintProxyValidationResults(List<ProxyValidationResult> results)
        {
            foreach (var result in results)
            {
                Console.ForegroundColor = result.IsValid ? ConsoleColor.Green : ConsoleColor.Red;
                Console.WriteLine($"{result.Proxy.Address} - {(result.IsValid ? "OK" : "DEAD")}");
                Console.ResetColor();
            }
        }
    }
}