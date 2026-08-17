namespace FuaPay.Web.Modules.Reporting.Application;

public sealed record CsvExportFile(
    string FileName,
    byte[] Content);
