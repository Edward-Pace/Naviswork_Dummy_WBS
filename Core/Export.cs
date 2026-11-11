using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
namespace DummyWbs.Core
{
 public static class Exporter
 {
  public static T ReadJson<T>(string path)
  {using(var stream=File.OpenRead(path)) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);}
  public static void WriteJson<T>(string path,T value)
  {using(var stream=new FileStream(path,FileMode.CreateNew)) new DataContractJsonSerializer(typeof(T)).WriteObject(stream,value);}
  // CSV opened in Excel must not execute model-supplied strings as formulas.
  public static string Cell(string value)
  {
   value=value??"";
   if(value.Length>0 && ("=+-@\t\r\n".IndexOf(value[0])>=0 || (value.TrimStart().Length>0 && "=+-@".IndexOf(value.TrimStart()[0])>=0))) value="'"+value;
   return "\"" + value.Replace("\"","\"\"") + "\"";
  }
  private static void Row(StreamWriter w,params string[] cells) => w.WriteLine(string.Join(",",cells.Select(Cell)));
  private static string N(int n)=>n.ToString(CultureInfo.InvariantCulture);
  private static StreamWriter Csv(string directory,string file) => new StreamWriter(new FileStream(Path.Combine(directory,file),FileMode.CreateNew),new UTF8Encoding(true));
  public static string Write(string parent,IReadOnlyList<Node> nodes,Settings settings,Result result,Action<string,int> checkpoint=null,string scope="Complete supplied snapshot")
  {
   var directory=Path.Combine(parent,"dummy-wbs-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss",CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N").Substring(0,8));
   Directory.CreateDirectory(directory);
   checkpoint?.Invoke("Writing files",0);
   File.WriteAllText(Path.Combine(directory,"RUN-STATUS.txt"),"Run not completed. Ignore output until summary.txt is successfully written.");
   // JSON preserves literal model strings; CSV escapes potential spreadsheet formula prefixes.
   WriteJson(Path.Combine(directory,"model-snapshot.json"),nodes.ToList());
   checkpoint?.Invoke("Writing files",1);
   WriteJson(Path.Combine(directory,"wbs-settings.json"),settings);
   using(var w=Csv(directory,"nodes.csv"))
   {Row(w,"NodeKey","ParentKey","SourceKey","SourceModel","Name","TreePath","HasGeometry","TerminalGeometry","DirectPropertyCount");
    foreach(var n in nodes) {checkpoint?.Invoke("Writing files",2);Row(w,n.Key,n.ParentKey,n.SourceKey,n.SourceModel,n.Name,n.TreePath,n.HasGeometry.ToString(),n.IsTerminalGeometry.ToString(),N(n.Properties.Count));}}
   using(var w=Csv(directory,"properties.csv"))
   {Row(w,"NodeKey","SourceModel","TreePath","Category","CategoryInternal","Property","PropertyInternal","ValueType","Value","ReadError");
    foreach(var n in nodes) {checkpoint?.Invoke("Writing files",3);foreach(var p in n.Properties) Row(w,n.Key,n.SourceModel,n.TreePath,p.Category,p.CategoryInternal,p.Name,p.NameInternal,p.ValueType,p.Value,p.Error);}}
   using(var w=Csv(directory,"property-profile.csv"))
   {Row(w,"SourceKey","SourceModel","Category","CategoryInternal","Property","PropertyInternal","NodesWithProperty","NonblankReadableValues","ReadErrors");
    var flat=nodes.SelectMany(n=>n.Properties.Select(p=>new{Node=n,Property=p}));
    foreach(var g in flat.GroupBy(x=>new{x.Node.SourceKey,x.Node.SourceModel,x.Property.Category,x.Property.CategoryInternal,x.Property.Name,x.Property.NameInternal})
      .OrderBy(x=>x.Key.SourceKey,StringComparer.Ordinal).ThenBy(x=>x.Key.Category,StringComparer.Ordinal).ThenBy(x=>x.Key.Name,StringComparer.Ordinal))
     Row(w,g.Key.SourceKey,g.Key.SourceModel,g.Key.Category,g.Key.CategoryInternal,g.Key.Name,g.Key.NameInternal,N(g.Select(x=>x.Node.Key).Distinct().Count()),
       N(g.Count(x=>string.IsNullOrEmpty(x.Property.Error)&&!string.IsNullOrWhiteSpace(x.Property.Value))),N(g.Count(x=>!string.IsNullOrEmpty(x.Property.Error))));}
   using(var w=Csv(directory,"dummy-wbs.csv"))
   {Row(w,"WbsCode","ParentWbsCode","Level","Kind","Name","ComponentKey","ProvisionalComponentCount","TerminalGeometryCount");
    foreach(var r in result.Wbs) {checkpoint?.Invoke("Writing files",4);Row(w,r.Code,r.ParentCode,N(r.Level),r.Kind,r.Name,r.ComponentKey,N(r.ComponentCount),N(r.GeometryLeaves));}}
   using(var w=Csv(directory,"components.csv"))
   {
    var header=new List<string>{"WbsCode","ComponentKey","SourceModel","TreePath","ComponentName","TerminalGeometryCount"};
    foreach(var field in Settings.Fields) header.AddRange(new[]{field,field+"State",field+"OriginNode",field+"OriginProperty"});
    Row(w,header.ToArray());
    foreach(var c in result.Components)
    {checkpoint?.Invoke("Writing files",5);var values=new List<string>{c.WbsCode,c.Node.Key,c.Node.SourceModel,c.Node.TreePath,c.Node.Name,N(c.GeometryLeaves)};
     foreach(var field in Settings.Fields){var p=c.Fields[field];values.AddRange(new[]{p.Value,p.State,p.OriginNode,p.OriginProperty});}
     Row(w,values.ToArray());}
   }
   using(var w=Csv(directory,"issues.csv"))
   {Row(w,"NodeKey","Code","Detail");foreach(var issue in result.Issues)Row(w,issue.NodeKey,issue.Code,issue.Detail);}
   checkpoint?.Invoke("Writing files",6);
   File.WriteAllText(Path.Combine(directory,"summary.txt"),
    "DUMMY WBS - REVIEW REQUIRED\r\nCreated UTC: "+DateTime.UtcNow.ToString("O")+"\r\n"+
    "Nodes inventoried: "+nodes.Count+"\r\nProperties inventoried: "+nodes.Sum(n=>n.Properties.Count)+"\r\n"+
    "Terminal geometry nodes: "+nodes.Count(n=>n.IsTerminalGeometry)+"\r\nProvisional components: "+result.Components.Count+"\r\n"+
    "Geometry leaves assigned: "+result.Components.Sum(c=>c.GeometryLeaves)+"\r\nIssues: "+result.Issues.Count+"\r\n"+
    "Component boundaries and mappings are heuristic, not an approved project WBS. Review issues and unclassified groups.\r\n"+
    "Node keys identify this hierarchy snapshot only; WBS codes can change when grouping/data changes.\r\n"+
    "Source model names are inferred from filename-like hierarchy nodes, not verified vendor identities.\r\n"+
    "Only terminal geometry contributes to the proposed component WBS; all hierarchy properties remain inventoried.\r\n"+
    "Exports contain model data. Keep them in local-data and out of GitHub.\r\n",Encoding.UTF8);
   File.WriteAllText(Path.Combine(directory,"scan-scope.txt"),scope);
   File.WriteAllText(Path.Combine(directory,"RUN-STATUS.txt"),"COMPLETE EXPORT - "+scope);
   return directory;
  }
 }
}
