using System;
using System.IO;
using System.Linq;
using BankStatementParser;
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
            var csvFile = Path.Combine(
                Path.GetDirectoryName(pdfPath),
                Path.GetFileNameWithoutExtension(pdfPath) + ".csv");
            var expectedResult = File.ReadAllText(csvFile);

            var statement = _fileProcessor.Process(pdfPath)
                .ShouldHaveSingleItem();

            var actualResult = _transactionSerializer
                .Serialize(statement.Transactions.ToArray());
            actualResult.ShouldBe(expectedResult);
        }

        [Test]
        public void ProcessesFolder()
        {
            var csvFile = "test-data/revolut/all.csv";
            var expectedResult = File.ReadAllText(csvFile);

            var statements = _fileProcessor.Process("test-data/revolut");
            statements.Length.ShouldBe(3);
            var actualResult = _transactionSerializer.Serialize(
                statements.SelectMany(x => x.Transactions).ToArray());

            actualResult.ShouldBe(expectedResult);
        }

        [Test]
        public void ExtractsFromAndToTimestamp()
        {
            var statement = _fileProcessor.Process("test-data/revolut/1.pdf")
                .ShouldHaveSingleItem();

            statement.FromDate.ShouldBe(new DateTime(2021, 9, 1));
            statement.ToDate.ShouldBe(new DateTime(2021, 11, 30));
        }

        [Test]
        public void ExtractsAccountNumber()
        {
            var statement = _fileProcessor.Process("test-data/revolut/1.pdf")
                .ShouldHaveSingleItem();

            statement.AccountNumber.ShouldStartWith("LT");
            statement.AccountNumber.Length.ShouldBe(20);
        }
    }
}