namespace SigeSiseEjecutivo.Models;

public sealed record TrackingRecord(
    long RowId,
    string System,
    int ItemNumber,
    string Session,
    string Areas,
    string Organ,
    string Contact,
    string Circuit,
    string Topic,
    string Type,
    string Request,
    string Agreements,
    string Followup,
    string Status);

public sealed record ImportRow(
    string System,
    int ItemNumber,
    string Session,
    string Areas,
    string Organ,
    string Contact,
    string Circuit,
    string Topic,
    string Type,
    string Request,
    string Agreements,
    string Followup,
    string Status);

public sealed record ImportResult(IReadOnlyList<ImportRow> Rows, IReadOnlyDictionary<string,int> Counts, IReadOnlyList<string> SheetNames);
