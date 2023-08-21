using System;
using System.IO;
using System.Linq;
using BankStatementParser.Banks;
using NUnit.Framework;
using Shouldly;

namespace BankStatementParser.Tests
{
    public class BocFileProcessorTests
    {
        private BocFileProcessor _bocFileProcessor;
        private TransactionSerializer _transactionSerializer;

        [SetUp]
        public void Setup()
        {
            _bocFileProcessor = new BocFileProcessor();
            _transactionSerializer = new TransactionSerializer();
        }

        private static string[] GetPdfFiles() =>
            Directory.GetFiles("./test-data/boc", "*.pdf")
                .Concat(Directory.GetFiles("./test-data/boc/1", "*.pdf")).ToArray();

        [TestCaseSource(nameof(GetPdfFiles))]
        public void ProcessesFiles(string pdfPath)
        {
            var csvFile = Path.Combine(
                Path.GetDirectoryName(pdfPath),
                Path.GetFileNameWithoutExtension(pdfPath) + ".csv");
            var expectedResult = File.ReadAllText(csvFile);

            var statement = _bocFileProcessor.Process(pdfPath)
                .ShouldHaveSingleItem();

            var actualResult = _transactionSerializer
                .Serialize(statement.Transactions.ToArray());
            actualResult.ShouldBe(expectedResult);
        }

        [Test]
        public void ProcessesFolder()
        {
            var csvFile = "test-data/boc/all.csv";
            var expectedResult = File.ReadAllText(csvFile);

            var statements = _bocFileProcessor.Process("test-data/boc");
            statements.Length.ShouldBe(3);
            var actualResult = _transactionSerializer.Serialize(
                statements.SelectMany(x => x.Transactions).ToArray());

            actualResult.ShouldBe(expectedResult);
        }

        [Test]
        public void ExtractsFromAndToTimestamp()
        {
            var statement = _bocFileProcessor.Process("test-data/boc/1.pdf")
                .ShouldHaveSingleItem();

            statement.FromDate.ShouldBe(new DateTime(2018, 11, 1));
            statement.ToDate.ShouldBe(new DateTime(2018, 11, 30));
        }

        [Test]
        public void ExtractsAccountNumber()
        {
            var statement = _bocFileProcessor.Process("test-data/boc/1.pdf")
                .ShouldHaveSingleItem();

            statement.AccountNumber.ShouldStartWith("35");
            statement.AccountNumber.Length.ShouldBe(12);
        }
    }
}