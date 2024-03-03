using System;
using System.IO;
using System.Linq;
using BankStatementParser.Banks;
using NUnit.Framework;
using Shouldly;

namespace BankStatementParser.Tests
{
    public class RevolutFileProcessorTests
    {
        private RevolutFileProcessor _fileProcessor;
        private TransactionSerializer _transactionSerializer;

        [SetUp]
        public void Setup()
        {
            _fileProcessor = new RevolutFileProcessor();
            _transactionSerializer = new TransactionSerializer();
        }

        private static string[] GetPdfFiles() =>
            Directory.GetFiles("./test-data/revolut", "*.pdf");

        [TestCaseSource(nameof(GetPdfFiles))]
        public void ProcessesFiles(string pdfPath)
        {
            var statements = _fileProcessor.Process(pdfPath);

            foreach (var grouping in statements.GroupBy(x => x.AccountNumber))
            {
                var csvFile = Path.Combine(
                    Path.GetDirectoryName(pdfPath),
                    Path.GetFileNameWithoutExtension(pdfPath) + "_" + grouping.Key + ".csv");
                var expectedResult = File.ReadAllText(csvFile);
                var actualResult = _transactionSerializer
                    .Serialize(grouping.SelectMany(x => x.Transactions).ToArray());
                actualResult.ShouldBe(expectedResult);
            }
        }
        
        [Test]
        public void ExtractsAccountNumber()
        {
            var statements = _fileProcessor.Process("test-data/revolut/1.pdf");

            foreach (var statement in statements)
            {
                statement.AccountNumber.ShouldStartWith("Main-LT");
                statement.AccountNumber.ShouldEndWith("11");
                statement.AccountNumber.Length.ShouldBe(25);
            }
        }

        [Test]
        public void ExtractsDifferentAccountNumbers()
        {
            var statements = _fileProcessor.Process("test-data/revolut/2.pdf");

            statements.Length.ShouldBe(4);
            statements[0].AccountNumber.ShouldStartWith("Euro-LT");
            statements[0].AccountNumber.ShouldEndWith("07");
            statements[1].AccountNumber.ShouldStartWith("Vault-LT");
            statements[1].AccountNumber.ShouldEndWith("99");
            statements[2].AccountNumber.ShouldStartWith("British_Pound-LT");
            statements[2].AccountNumber.ShouldEndWith("07");
            statements[3].AccountNumber.ShouldStartWith("United_States_Dollar-LT");
            statements[3].AccountNumber.ShouldEndWith("07");
        }
    }
}