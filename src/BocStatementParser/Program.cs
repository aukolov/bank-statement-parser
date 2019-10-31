using System;
using System.IO;
using System.Linq;
using CommandLine;

namespace BocStatementParser
{
    class Program
    {
        static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args)
                .WithParsed(o =>
                {
                    var fileProcessor = new FileProcessor();
                    var statements = fileProcessor.Process(o.Path);

                    var transactionSerializer = new TransactionSerializer();
                    foreach (var g in statements.GroupBy(x => x.AccountNumber))
                    {
                        var transactions = g.SelectMany(s => s.Transactions).ToArray();
                        var serializedTransactions = transactionSerializer.Serialize(
                            transactions);
                        var fileName = $"statement_{g.Key}_gen{DateTime.Now:yyyyMMdd-HHmmss}.csv";

                        var transactionsWord = transactions.Length != 1 ? "transactions" : "transaction";
                        Console.WriteLine($"Writing {transactions.Length} {transactionsWord} to {Path.GetFullPath(fileName)}...");
                        File.WriteAllText(fileName, serializedTransactions);
                    }
                    Console.WriteLine("Done!");
                });
        }
    }
}
