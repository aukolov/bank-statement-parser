using System;
using System.IO;
using System.Linq;
using CommandLine;

namespace BankStatementParser
{
    static class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Bank of Cyprus Statement Parser");
            Console.WriteLine();

            Parser.Default.ParseArguments<Options>(args)
                .WithParsed(o =>
                {
                    var fileProcessor = new BocFileProcessor();
                    var statements = fileProcessor.Process(o.Path);

                    var transactionSerializer = new TransactionSerializer();
                    foreach (var g in statements.GroupBy(x => x.AccountNumber))
                    {
                        var accountNumber = g.Key;
                        PrintInColor($"Processing account {accountNumber}...", ConsoleColor.Green);
                        var accountStatements = g.OrderBy(s => s.FromDate).ToArray();

                        foreach (var error in StatementContinuityValidator.GetErrors(accountStatements))
                        {
                            PrintInColor(error, ConsoleColor.Red);
                        }

                        var transactions = accountStatements
                            .SelectMany(s => s.Transactions).ToArray();
                        var serializedTransactions = transactionSerializer.Serialize(
                            transactions);
                        var fileName = $"statement_{accountNumber}_gen{DateTime.Now:yyyyMMdd-HHmmss}.csv";

                        var transactionsWord = transactions.Length != 1 ? "transactions" : "transaction";
                        Console.WriteLine($"Writing {transactions.Length} {transactionsWord} to {Path.GetFullPath(fileName)}...");
                        File.WriteAllText(fileName, serializedTransactions);
                    }

                    Console.WriteLine();
                    PrintInColor("Done!", ConsoleColor.Green);
                });
        }

        private static void PrintInColor(string text, ConsoleColor foregroundColor)
        {
            var oldColor = Console.ForegroundColor;
            try
            {
                Console.ForegroundColor = foregroundColor;
                Console.WriteLine(text);
            }
            finally
            {
                Console.ForegroundColor = oldColor;
            }
        }
    }
}
