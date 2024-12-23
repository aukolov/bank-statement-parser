using System;
using System.IO;
using System.Linq;
using BankStatementParser.Banks;
using NUnit.Framework;
using Shouldly;

namespace BankStatementParser.Tests
{
    public class FibankFileProcessorTests
    {
        private FibankFileProcessor _fibankFileProcessor;
        private TransactionSerializer _transactionSerializer;

        [SetUp]
        public void Setup()
        {
            _fibankFileProcessor = new FibankFileProcessor();
            _transactionSerializer = new TransactionSerializer();
        }

        private static string[] GetPdfFiles() =>
            Directory.GetFiles("./test-data/fibank", "*.pdf").ToArray();

        [TestCaseSource(nameof(GetPdfFiles))]
        public void ProcessesFiles(string pdfPath)
        {
            var csvFile = Path.Combine(
                Path.GetDirectoryName(pdfPath),
                Path.GetFileNameWithoutExtension(pdfPath) + ".csv");
            var expectedResult = File.ReadAllText(csvFile);

            var statement = _fibankFileProcessor.Process(pdfPath)
                .ShouldHaveSingleItem();

            var actualResult = _transactionSerializer
                .Serialize(statement.Transactions.ToArray());
            actualResult.ShouldBe(expectedResult);
        }

        [Test]
        public void ProcessesFolder()
        {
            var csvFile = "test-data/fibank/all.csv";
            var expectedResult = File.ReadAllText(csvFile);

            var statements = _fibankFileProcessor.Process("test-data/fibank");
            statements.Length.ShouldBe(6);
            var actualResult = _transactionSerializer.Serialize(
                statements.SelectMany(x => x.Transactions).ToArray());

            actualResult.ShouldBe(expectedResult);
        }

        [Test]
        public void ExtractsFromAndToTimestamp()
        {
            var statement = _fibankFileProcessor.Process("test-data/fibank/1.pdf")
                .ShouldHaveSingleItem();

            statement.FromDate.ShouldBe(new DateTime(2024, 9, 1));
            statement.ToDate.ShouldBe(new DateTime(2024, 9, 30));
        }

        [Test]
        public void ExtractsAccountNumber()
        {
            var statement = _fibankFileProcessor.Process("test-data/fibank/1.pdf")
                .ShouldHaveSingleItem();

            statement.AccountNumber.ShouldStartWith("CY");
            statement.AccountNumber.ShouldEndWith("281");
        }
    }
}