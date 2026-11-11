using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
namespace DummyWbs.Core
{
 [DataContract] public sealed class PropertyValue
 {
  [DataMember] public string Category { get; set; } = "";
  [DataMember] public string CategoryInternal { get; set; } = "";
  [DataMember] public string Name { get; set; } = "";
  [DataMember] public string NameInternal { get; set; } = "";
  [DataMember] public string Value { get; set; } = "";
  [DataMember] public string ValueType { get; set; } = "";
  [DataMember] public string Error { get; set; } = "";
 }
 [DataContract] public sealed class Node
 {
  [DataMember] public string Key { get; set; } = "";
  [DataMember] public string ParentKey { get; set; } = "";
  [DataMember] public string SourceModel { get; set; } = "";
  [DataMember] public string SourceKey { get; set; } = "";
  [DataMember] public string TreePath { get; set; } = "";
  [DataMember] public string Name { get; set; } = "";
  [DataMember] public bool HasGeometry { get; set; }
  [DataMember] public bool IsTerminalGeometry { get; set; }
  [DataMember] public List<PropertyValue> Properties { get; set; } = new List<PropertyValue>();
 }
 [DataContract] public sealed class Mapping
 {
  [DataMember] public string Field { get; set; } = "";
  [DataMember] public string SourceContains { get; set; } = "";
  [DataMember] public string Category { get; set; } = "*";
  [DataMember] public string Property { get; set; } = "";
 }
 [DataContract] public sealed class Settings
 {
  [DataMember] public int MaximumNodes { get; set; } = 250000;
  [DataMember] public string[] WbsLevels { get; set; } = new[] { "SourceModel", "Discipline", "LineId", "PipeRun" };
  [DataMember] public List<Mapping> Mappings { get; set; } = new List<Mapping>();
  public static readonly string[] Fields = { "Area", "Discipline", "System", "LineId", "PipeRun", "Tag", "ItemId", "ComponentType" };
  public static Settings CreateDefault()
  {
   var s = new Settings();
   Add(s,"Area","Area","Area Code");
   Add(s,"Discipline","Discipline");
   Add(s,"System","System","System Name");
   Add(s,"LineId","Line ID","Line Number","Line No");
   Add(s,"PipeRun","PipeRun","Pipe Run");
   Add(s,"Tag","Equipment Tag","Tag Number","Tag");
   Add(s,"ItemId","GUID","GlobalId","Element ID");
   Add(s,"ComponentType","Name_Pip","Component Type");
   return s;
  }
  private static void Add(Settings s,string field,params string[] names)
  { foreach(var name in names) s.Mappings.Add(new Mapping { Field=field, Property=name }); }
  public void Validate()
  {
   if(MaximumNodes<1) throw new ArgumentException("MaximumNodes must be positive.");
   if(WbsLevels==null || WbsLevels.Length==0) throw new ArgumentException("WbsLevels is required.");
   var seen=new HashSet<string>(StringComparer.Ordinal);
   foreach(var level in WbsLevels)
   { if(level!="SourceModel" && Array.IndexOf(Fields,level)<0) throw new ArgumentException("Unknown WBS field: "+level);
     if(!seen.Add(level)) throw new ArgumentException("Repeated WBS field: "+level); }
   if(Mappings==null) throw new ArgumentException("Mappings is required.");
   foreach(var m in Mappings)
    if(m==null || Array.IndexOf(Fields,m.Field)<0 || string.IsNullOrWhiteSpace(m.Category) || string.IsNullOrWhiteSpace(m.Property))
     throw new ArgumentException("Each mapping needs a known Field, Category and Property.");
  }
 }
 public sealed class ResolvedField
 {
  public string Value="", OriginNode="", OriginProperty="", State="Missing";
 }
 public sealed class Component
 {
  public Node Node;
  public int GeometryLeaves;
  public string WbsCode="";
  public Dictionary<string,ResolvedField> Fields = new Dictionary<string,ResolvedField>(StringComparer.Ordinal);
 }
 public sealed class WbsRow
 {
  public string Code="", ParentCode="", Kind="", Name="", ComponentKey="";
  public int Level, ComponentCount, GeometryLeaves;
 }
 public sealed class Issue
 {
  public string NodeKey="", Code="", Detail="";
 }
 public sealed class Result
 {
  public List<Component> Components=new List<Component>();
  public List<WbsRow> Wbs=new List<WbsRow>();
  public List<Issue> Issues=new List<Issue>();
 }
}
