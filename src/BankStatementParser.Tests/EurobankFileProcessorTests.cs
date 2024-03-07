using System;
using System.IO;
using System.Linq;
using BankStatementParser.Banks;
using NUnit.Framework;
using Shouldly;

namespace BankStatementParser.Tests
{
    public class EurobankFileProcessorTests
    {
        private EurobankFileProcessor _fileProcessor;
        private TransactionSerializer _transactionSerializer;

        [SetUp]
        public void Setup()
        {
            _fileProcessor = new EurobankFileProcessor();
            _transactionSerializer = new TransactionSerializer();
        }

        private static string[] GetPdfFiles() =>
            Directory.GetFiles("./test-data/eurobank", "*.pdf");

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
            var statements = _fileProcessor.Process("test-data/eurobank/1.pdf");

            statements.Length.ShouldBe(1);
            var statement = statements.Single();
            statement.AccountNumber.ShouldStartWith("CY860");
            statement.AccountNumber.ShouldEndWith("36");
            statement.AccountNumber.Length.ShouldBe(28);
        }
        
        [Test]
        public void ExtractsFromAndToTimestamp()
        {
            var statement = _fileProcessor.Process("test-data/eurobank/1.pdf")
                .ShouldHaveSingleItem();

            statement.FromDate.ShouldBe(new DateTime(2024, 02, 1));
            statement.ToDate.ShouldBe(new DateTime(2024, 02, 29));
        }
    }
}