using System;
using System.IO;
using System.Linq;
using BankStatementParser.Banks;
using NUnit.Framework;
using Shouldly;

namespace BankStatementParser.Tests
{
    public class HellenicFileProcessorTests
    {
        private IFileProcessor _fileProcessor;
        private TransactionSerializer _transactionSerializer;

        [SetUp]
        public void Setup()
        {
            _fileProcessor = new HellenicFileProcessor();
            _transactionSerializer = new TransactionSerializer();
        }

        private static string[] GetPdfFiles() =>
            Directory.GetFiles("./test-data/hellenic", "*.pdf");

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
        public void ExtractsFromAndToTimestamp()
        {
            var statement = _fileProcessor.Process("test-data/hellenic/1.pdf")
                .ShouldHaveSingleItem();

            statement.FromDate.ShouldBe(new DateTime(2020, 3, 1));
            statement.ToDate.ShouldBe(new DateTime(2020, 5, 31));
        }

        [Test]
        public void ExtractsAccountNumber()
        {
            var statement = _fileProcessor.Process("test-data/hellenic/1.pdf")
                .ShouldHaveSingleItem();

            statement.AccountNumber.ShouldStartWith("243");
            statement.AccountNumber.Length.ShouldBe(16);
        }
    }
}