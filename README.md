# boc-statement-parser
Command line tool for extracting transactions from Bank of Cyprus, Hellenic Bank, Revolut and other popular banks PDF statement.

## Command line options
```
  -p, --path    Required. Path to a PDF BoC statement file or folder containing statement files.
  -b, --bank    Required. One of the supported banks: BoC (Bank of Cyprus), Revolut, Hellenic
  --help        Display this help screen.
  --version     Display version information.
```

## Development
Build release for Windows:
```
dotnet publish -c Release -p:PublishSingleFile=true -p:UseAppHost=true --self-contained false -r win-x64
```