using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using SigeSiseEjecutivo.Models;

namespace SigeSiseEjecutivo.Services;

public sealed class XlsxImporter
{
    private static readonly string[] Required = ["#","sesion","areas","organo jurisdiccional","contacto","cto","tema","tipo","detalle tema","acuerdos","seguimiento","estatus"];

    public ImportResult Parse(Stream input)
    {
        using var ms = new MemoryStream(); input.CopyTo(ms); ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen:false);
        var shared = ReadSharedStrings(zip);
        var workbook = Load(zip,"xl/workbook.xml") ?? throw new InvalidDataException("El archivo no contiene xl/workbook.xml.");
        var rels = Load(zip,"xl/_rels/workbook.xml.rels") ?? throw new InvalidDataException("El archivo no contiene relaciones de hojas.");
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace pkg = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relMap = rels.Descendants(pkg+"Relationship").ToDictionary(x=>(string)x.Attribute("Id")!,x=>(string)x.Attribute("Target")!);
        var sheetNames = workbook.Descendants(main+"sheet").Select(x=>(string?)x.Attribute("name") ?? "").Where(x=>x.Length>0).ToList();
        var all = new List<ImportRow>(); var counts = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in workbook.Descendants(main+"sheet"))
        {
            var name=((string?)s.Attribute("name") ?? "").Trim(); var system=Normalize(name).ToUpperInvariant();
            if (system is not ("SISE" or "SIGE")) continue;
            var id=(string?)s.Attribute(relNs+"id"); if (id is null || !relMap.TryGetValue(id,out var target)) continue;
            var path = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target.Replace("../","");
            var doc=Load(zip,path); if (doc is null) continue;
            var matrix=ReadRows(doc,shared).Where(r=>r.Any(v=>!string.IsNullOrWhiteSpace(v))).ToList();
            var headerAt=matrix.FindIndex(r=>Required.Count(req=>r.Any(v=>Normalize(v)==req))>=6);
            if (headerAt<0) throw new InvalidDataException($"La hoja {system} no contiene los encabezados esperados.");
            var headers=matrix[headerAt].Select(Normalize).ToList();
            string Get(List<string> row,string key){var i=headers.IndexOf(key); return i>=0 && i<row.Count ? row[i] : "";}
            var n=0;
            foreach (var r in matrix.Skip(headerAt+1))
            {
                if (!r.Any(v=>!string.IsNullOrWhiteSpace(v))) continue;
                n++;
                var item = int.TryParse(Get(r,"#"),NumberStyles.Any,CultureInfo.InvariantCulture,out var parsed) ? parsed : n;
                var status=Get(r,"estatus").Trim(); if (status.Length==0) status="Sin estatus";
                all.Add(new ImportRow(system,item,NormalizeDate(Get(r,"sesion")),Get(r,"areas").Trim(),Get(r,"organo jurisdiccional").Trim(),Get(r,"contacto").Trim(),TrimNumber(Get(r,"cto")),Get(r,"tema").Trim(),Get(r,"tipo").Trim(),Get(r,"detalle tema").Trim(),Get(r,"acuerdos").Trim(),Get(r,"seguimiento").Trim(),status));
            }
            counts[system]=n;
        }
        if (!counts.ContainsKey("SISE") || !counts.ContainsKey("SIGE")) throw new InvalidDataException("El Excel debe contener las hojas SISE y SIGE.");
        return new ImportResult(all,counts,sheetNames);
    }

    static XDocument? Load(ZipArchive z,string p){var e=z.GetEntry(p); if(e is null)return null; using var s=e.Open(); return XDocument.Load(s);}
    static List<string> ReadSharedStrings(ZipArchive z){var d=Load(z,"xl/sharedStrings.xml"); if(d is null)return []; XNamespace n="http://schemas.openxmlformats.org/spreadsheetml/2006/main"; return d.Descendants(n+"si").Select(si=>string.Concat(si.Descendants(n+"t").Select(t=>t.Value))).ToList();}
    static List<List<string>> ReadRows(XDocument d,List<string> shared){XNamespace n="http://schemas.openxmlformats.org/spreadsheetml/2006/main"; var output=new List<List<string>>(); foreach(var row in d.Descendants(n+"row")){var vals=new List<string>(); foreach(var c in row.Elements(n+"c")){var idx=ColumnIndex((string?)c.Attribute("r")??"A1"); while(vals.Count<=idx) vals.Add(""); var t=(string?)c.Attribute("t"); var v=c.Element(n+"v")?.Value??""; string x=v; if(t=="s"&&int.TryParse(v,out var si)&&si>=0&&si<shared.Count)x=shared[si]; else if(t=="inlineStr")x=string.Concat(c.Descendants(n+"t").Select(q=>q.Value)); else if(t=="b")x=v=="1"?"TRUE":"FALSE"; vals[idx]=x;} output.Add(vals);} return output;}
    static int ColumnIndex(string cell){var n=0; foreach(var ch in cell.TakeWhile(char.IsLetter)) n=n*26+(char.ToUpperInvariant(ch)-'A'+1); return Math.Max(0,n-1);}
    static string Normalize(string? v)
    {
        var chars=(v??"").Normalize(NormalizationForm.FormD).Where(c=>CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark).ToArray();
        var noMarks=new string(chars);
        return string.Join(" ",noMarks.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries)).Trim().ToLowerInvariant();
    }
    static string TrimNumber(string v)=>v.EndsWith(".0",StringComparison.Ordinal)?v[..^2].Trim():v.Trim();
    static string NormalizeDate(string v){if(double.TryParse(v,NumberStyles.Float,CultureInfo.InvariantCulture,out var serial)&&serial>1)try{return DateTime.FromOADate(serial).ToString("yyyy-MM-dd");}catch{} if(DateTime.TryParse(v,CultureInfo.GetCultureInfo("es-MX"),DateTimeStyles.None,out var d))return d.ToString("yyyy-MM-dd"); return v.Length>=10?v[..10]:v;}
}
