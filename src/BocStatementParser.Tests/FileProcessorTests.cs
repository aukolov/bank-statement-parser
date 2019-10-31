using System.IO;
using NUnit.Framework;
using Shouldly;

namespace BocStatementParser.Tests
{
    public class Tests
    {
        private FileProcessor _instance;

        [SetUp]
        public void Setup()
        {
            _instance = new FileProcessor();
        }

        private static string[] GetPdfFiles() => 
            Directory.GetFiles("./test-data", "*.pdf");

        [TestCaseSource(nameof(GetPdfFiles))]
        public void ProcessesFiles(string pdfPath)
        {
            var csvFile = Path.Combine(
                Path.GetDirectoryName(pdfPath),
                Path.GetFileNameWithoutExtension(pdfPath) + ".csv");
            var expectedResult = File.ReadAllText(csvFile);

            var actualResult = _instance.Process(pdfPath);

            actualResult.ShouldBe(expectedResult);
        }

        [Test]
        public void ProcessesFolder()
        {
            var csvFile = "test-data/all.csv";
            var expectedResult = File.ReadAllText(csvFile);

            var actualResult = _instance.Process("test-data");

            actualResult.ShouldBe(expectedResult);
        }
    }
}