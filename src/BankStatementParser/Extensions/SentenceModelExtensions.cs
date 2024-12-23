using Pdf2Text;

namespace BankStatementParser.Extensions;

public static class SentenceModelExtensions
{
    public static double HorizontalCenter(this SentenceModel sentenceModel)
    {
        return sentenceModel.Left + sentenceModel.Width / 2;
    }
}