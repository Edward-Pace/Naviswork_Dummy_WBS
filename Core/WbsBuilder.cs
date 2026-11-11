using System;
using System.Collections.Generic;
using System.Linq;
namespace DummyWbs.Core
{
 public static class WbsBuilder
 {
  private static bool Same(string a,string b) => string.Equals(a?.Trim(),b?.Trim(),StringComparison.OrdinalIgnoreCase);
  private static bool Generic(string name) => new[]{"","Geometry","Mesh","Group","Item"}.Any(x=>Same(x,name));
  private static IEnumerable<PropertyValue> Matches(Node n,string field,Settings s)
  {
   var applicable=s.Mappings.Where(m=>m.Field==field &&
    (string.IsNullOrEmpty(m.SourceContains) || n.SourceModel.IndexOf(m.SourceContains,StringComparison.OrdinalIgnoreCase)>=0)).ToArray();
   // Source-specific rules replace the generic rules for this field on that source.
   if(applicable.Any(m=>!string.IsNullOrEmpty(m.SourceContains)))
    applicable=applicable.Where(m=>!string.IsNullOrEmpty(m.SourceContains)).ToArray();
   return n.Properties.Where(p=>string.IsNullOrEmpty(p.Error) && !string.IsNullOrWhiteSpace(p.Value) &&
    applicable.Any(m=>(m.Category=="*" || Same(m.Category,p.Category) || Same(m.Category,p.CategoryInternal)) &&
      (Same(m.Property,p.Name) || Same(m.Property,p.NameInternal))));
  }
  private static bool Candidate(Node n,Settings s) =>
   !Generic(n.Name) && new[]{"Tag","ItemId","ComponentType","LineId"}.Any(f=>Matches(n,f,s).Any());
  private static IEnumerable<Node> Ancestors(Node start,Dictionary<string,Node> nodes)
  {
   var seen=new HashSet<string>(StringComparer.Ordinal);
   for(var n=start;n!=null;)
   {
    if(!seen.Add(n.Key)) throw new ArgumentException("Hierarchy cycle at "+n.Key);
    yield return n;
    Node parent;
    if(string.IsNullOrEmpty(n.ParentKey) || !nodes.TryGetValue(n.ParentKey,out parent) || parent.SourceKey!=start.SourceKey) break;
    n=parent;
   }
  }
  private static ResolvedField Resolve(Node n,string field,Settings s,Dictionary<string,Node> index)
  {
   foreach(var ancestor in Ancestors(n,index))
   {
    var matches=Matches(ancestor,field,s).ToArray();
    var distinct=matches.Select(p=>p.Value.Trim()).Distinct(StringComparer.Ordinal).ToArray();
    if(distinct.Length>1) return new ResolvedField {State="Conflict",OriginNode=ancestor.Key,
     OriginProperty=string.Join(" | ",matches.Select(p=>p.Category+" > "+p.Name+" = "+p.Value))};
    if(distinct.Length==1) return new ResolvedField {Value=distinct[0],OriginNode=ancestor.Key,
     OriginProperty=matches[0].Category+" > "+matches[0].Name,State=ancestor.Key==n.Key ? "Direct" : "Ancestor"};
   }
   return new ResolvedField();
  }
  public static Result Build(IReadOnlyList<Node> nodes,Settings settings,Action<string,int> checkpoint=null)
  {
   settings.Validate();
   if(nodes.Count>settings.MaximumNodes) throw new ArgumentException("Node limit exceeded; no complete WBS can be claimed.");
   var index=nodes.ToDictionary(n=>n.Key,StringComparer.Ordinal);
   foreach(var n in nodes)
    if(string.IsNullOrEmpty(n.SourceKey) || string.IsNullOrEmpty(n.SourceModel)) throw new ArgumentException("Source identity required: "+n.Key);
   var result=new Result();
   var components=new Dictionary<string,Component>(StringComparer.Ordinal);
   foreach(var leaf in nodes.Where(n=>n.IsTerminalGeometry))
   {
    checkpoint?.Invoke("Building WBS",components.Count);
    var chain=Ancestors(leaf,index).ToArray();
    // Prefer the nearest logical node with a configured identifying property.
    var owner=chain.FirstOrDefault(n=>Candidate(n,settings)) ?? leaf;
    Component c;
    if(!components.TryGetValue(owner.Key,out c))
    {
     c=new Component {Node=owner};
     foreach(var field in Settings.Fields)
     {
      var value=Resolve(owner,field,settings,index); c.Fields.Add(field,value);
      if(value.State=="Conflict") result.Issues.Add(new Issue {NodeKey=owner.Key,Code="PROPERTY_CONFLICT",Detail=field+": "+value.OriginProperty});
     }
     if(owner==leaf && Generic(leaf.Name)) result.Issues.Add(new Issue {NodeKey=owner.Key,Code="UNRESOLVED_COMPONENT",Detail="No logical ancestor with configured identifying metadata; geometry leaf retained as a provisional component."});
     components.Add(owner.Key,c);
    }
    c.GeometryLeaves++;
   }
   foreach(var n in nodes)
   {
    checkpoint?.Invoke("Reviewing properties",components.Count);
    foreach(var p in n.Properties.Where(p=>!string.IsNullOrEmpty(p.Error)))
     result.Issues.Add(new Issue {NodeKey=n.Key,Code="PROPERTY_READ_ERROR",Detail=p.Category+" > "+p.Name+": "+p.Error});
    if(n.HasGeometry && !n.IsTerminalGeometry)
     result.Issues.Add(new Issue {NodeKey=n.Key,Code="NONTERMINAL_GEOMETRY",Detail="Geometry-bearing parent retained in inventory; component WBS uses terminal geometry only. Review coverage."});
   }
   result.Components=components.Values.OrderBy(c=>c.Node.SourceModel,StringComparer.Ordinal).ThenBy(c=>c.Node.Key,StringComparer.Ordinal).ToList();
   var tree=new Group {Name="Model WBS"};
   foreach(var c in result.Components)
   {
    checkpoint?.Invoke("Assembling WBS",components.Count);
    var group=tree;
    foreach(var level in settings.WbsLevels)
    {
     var value=level=="SourceModel" ? c.Node.SourceModel : c.Fields[level].Value;
     var missing=string.IsNullOrWhiteSpace(value);
     var label=missing ? "Unclassified ("+level+")" : value;
     // Separate real values from missing-value buckets and duplicate named sources.
     var key=(missing?"missing:":"value:")+label+(level=="SourceModel" ? "\u001f"+c.Node.SourceKey : "");
     Group next;
     if(!group.Children.TryGetValue(key,out next)) group.Children.Add(key,next=new Group {Name=label});
     group=next;
    }
    group.Components.Add(c);
   }
   Emit(tree,"","",0,result);
   return result;
  }
  private sealed class Group
  {
   public string Name;
   public SortedDictionary<string,Group> Children=new SortedDictionary<string,Group>(StringComparer.Ordinal);
   public List<Component> Components=new List<Component>();
  }
  private static void Emit(Group group,string code,string parent,int level,Result result)
  {
   WbsRow row=null;
   if(level>0) {row=new WbsRow {Code=code,ParentCode=parent,Level=level,Kind="Group",Name=group.Name};result.Wbs.Add(row);}
   int child=0, count=0, geometry=0;
   foreach(var entry in group.Children.Values)
   {
    string next=code.Length==0 ? (++child).ToString() : code+"."+(++child);
    int before=result.Wbs.Count; Emit(entry,next,code,level+1,result);
    count+=result.Wbs[before].ComponentCount; geometry+=result.Wbs[before].GeometryLeaves;
   }
   foreach(var c in group.Components.OrderBy(x=>x.Node.Key,StringComparer.Ordinal))
   {
    var label=c.Fields["Tag"].Value;
    if(string.IsNullOrWhiteSpace(label)) label=c.Fields["ItemId"].Value;
    if(string.IsNullOrWhiteSpace(label)) label=c.Node.Name;
    c.WbsCode=code+"."+(++child);
    result.Wbs.Add(new WbsRow {Code=c.WbsCode,ParentCode=code,Level=level+1,Kind="Component",Name=label,ComponentKey=c.Node.Key,ComponentCount=1,GeometryLeaves=c.GeometryLeaves});
    count++; geometry+=c.GeometryLeaves;
   }
   if(row!=null) {row.ComponentCount=count;row.GeometryLeaves=geometry;}
  }
 }
}
