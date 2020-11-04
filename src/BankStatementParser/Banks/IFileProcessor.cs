namespace BankStatementParser.Banks
{
    public interface IFileProcessor
    {
        Statement[] Process(string path);
    }
}