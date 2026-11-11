using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DummyWbs.Core;
namespace DummyWbs.Demo
{
 internal static class Program
 {
  private static int Main(string[] args)
  {
   try
   {
    if(args.Length==1 && args[0]=="--test"){Test();return 0;}
    if(args.Length==2 && args[0]=="--write-settings"){Exporter.WriteJson(args[1],Settings.CreateDefault());return 0;}
    if(args.Length==2 && args[0]=="--demo")
    {
     var settings=DemoSettings();var nodes=Fixture();
     Console.WriteLine(Exporter.Write(args[1],nodes,settings,WbsBuilder.Build(nodes,settings)));return 0;
    }
    if(args.Length==4 && args[0]=="--replay")
    {
     var nodes=Exporter.ReadJson<List<Node>>(args[1]);var settings=Exporter.ReadJson<Settings>(args[2]);
     Console.WriteLine(Exporter.Write(args[3],nodes,settings,WbsBuilder.Build(nodes,settings)));return 0;
    }
    Console.WriteLine("Usage:\n  DummyWbs.Demo --test\n  DummyWbs.Demo --demo <output-folder>\n  DummyWbs.Demo --replay <model-snapshot.json> <wbs-settings.json> <output-folder>\n  DummyWbs.Demo --write-settings <new-json-path>");
    return 2;
   }
   catch(Exception ex){Console.Error.WriteLine(ex.ToString());return 1;}
  }
  private static Node Make(string key,string parent,string sourceKey,string source,string name,bool geometry=false)
  {return new Node{Key=key,ParentKey=parent,SourceKey=sourceKey,SourceModel=source,Name=name,TreePath=source+" / "+key+" / "+name,HasGeometry=geometry,IsTerminalGeometry=geometry};}
  private static void Prop(Node n,string category,string name,string value){n.Properties.Add(new PropertyValue {Category=category,Name=name,Value=value});}
  private static Settings DemoSettings()
  {
   var s=Settings.CreateDefault();
   s.Mappings.Add(new Mapping {Field="LineId",SourceContains=".ifc",Category="VendorPset",Property="PipelineRef"});
   s.Mappings.Add(new Mapping {Field="ComponentType",SourceContains=".ifc",Category="VendorPset",Property="ObjectClass"});
   return s;
  }
  private static List<Node> Fixture()
  {
   var a=Make("A","","A","Vendor_A.vue","Vendor_A.vue");Prop(a,"Project","Discipline","Piping");
   var component=Make("A.1","A","A","Vendor_A.vue","Elbow A");Prop(component,"SmartPlant 3D","Line ID","DEMO-L100");Prop(component,"SmartPlant 3D","Name_Pip","Elbow");
   var b=Make("B","","B","Vendor_B.ifc","Vendor_B.ifc");Prop(b,"Project","Discipline","Piping");
   var componentB=Make("B.1","B","B","Vendor_B.ifc","Valve B");Prop(componentB,"VendorPset","PipelineRef","DEMO-L200");Prop(componentB,"VendorPset","ObjectClass","Valve");
   var c=Make("C","","C","Vendor_C.dgn","Vendor_C.dgn");
   return new List<Node>{a,component,Make("A.1.1","A.1","A","Vendor_A.vue","Geometry",true),Make("A.1.2","A.1","A","Vendor_A.vue","Geometry",true),
    b,componentB,Make("B.1.1","B.1","B","Vendor_B.ifc","Geometry",true),c,Make("C.1","C","C","Vendor_C.dgn","Mesh",true)};
  }
  private static void Assert(bool condition,string message){if(!condition)throw new InvalidOperationException("FAILED: "+message);}
  private static void Test()
  {
   int count=0;
   Action<string,Action> run=(name,action)=>{action();count++;Console.WriteLine("PASS "+name);};
   run("Mixed-vendor alias mapping",()=>{
    var r=WbsBuilder.Build(Fixture(),DemoSettings());
    Assert(r.Components.Single(c=>c.Node.Key=="B.1").Fields["LineId"].Value=="DEMO-L200","IFC vendor property was not mapped");});
   run("Multiple geometry leaves deduplicate at logical component",()=>{
    var r=WbsBuilder.Build(Fixture(),DemoSettings());Assert(r.Components.Count==3,"component count");Assert(r.Components.Single(c=>c.Node.Key=="A.1").GeometryLeaves==2,"geometry count");
    Assert(r.Components.Sum(c=>c.GeometryLeaves)==4,"every terminal leaf assigned exactly once");});
   run("Ancestor provenance is explicit",()=>{
    var r=WbsBuilder.Build(Fixture(),DemoSettings());var f=r.Components.Single(c=>c.Node.Key=="A.1").Fields["Discipline"];
    Assert(f.Value=="Piping"&&f.State=="Ancestor"&&f.OriginNode=="A","provenance");});
   run("Missing vendor metadata retained as unclassified",()=>{
    var r=WbsBuilder.Build(Fixture(),DemoSettings());Assert(r.Components.Any(c=>c.Node.Key=="C.1"),"unknown geometry dropped");
    Assert(r.Wbs.Any(w=>w.Name=="Unclassified (LineId)"),"unclassified bucket missing");});
   run("Conflicting aliases are not silently selected",()=>{
    var nodes=Fixture();Prop(nodes.Single(n=>n.Key=="A.1"),"Other","Line Number","DIFFERENT");
    var r=WbsBuilder.Build(nodes,DemoSettings());var f=r.Components.Single(c=>c.Node.Key=="A.1").Fields["LineId"];
    Assert(f.State=="Conflict"&&f.Value=="","ambiguous value selected");Assert(r.Issues.Any(i=>i.Code=="PROPERTY_CONFLICT"),"conflict not logged");});
   run("Inheritance never crosses a source boundary",()=>{
    var nodes=Fixture();nodes.Single(n=>n.Key=="C").ParentKey="A.1";
    var r=WbsBuilder.Build(nodes,DemoSettings());Assert(r.Components.Single(c=>c.Node.Key=="C.1").Fields["LineId"].State=="Missing","cross-source inheritance");});
   run("Source-specific mapping overrides generic aliases",()=>{
    var nodes=Fixture();Prop(nodes.Single(n=>n.Key=="B.1"),"Other","Line ID","WRONG");
    Assert(WbsBuilder.Build(nodes,DemoSettings()).Components.Single(c=>c.Node.Key=="B.1").Fields["LineId"].Value=="DEMO-L200","source override");});
   run("Property read failures are visible",()=>{
    var nodes=Fixture();nodes[0].Properties.Add(new PropertyValue{Category="Bad",Name="Field",Error="Synthetic read failure"});
    Assert(WbsBuilder.Build(nodes,DemoSettings()).Issues.Any(i=>i.Code=="PROPERTY_READ_ERROR"),"read error lost");});
   run("Parent aggregate counts reconcile",()=>{
    var r=WbsBuilder.Build(Fixture(),DemoSettings());var roots=r.Wbs.Where(w=>w.ParentCode=="").ToArray();
    Assert(roots.Sum(w=>w.ComponentCount)==3 && roots.Sum(w=>w.GeometryLeaves)==4,"WBS counts mismatch");});
   run("WBS stable for reordered snapshot records",()=>{
    var a=WbsBuilder.Build(Fixture(),DemoSettings());var nodes=Fixture();nodes.Reverse();var b=WbsBuilder.Build(nodes,DemoSettings());
    Assert(a.Components.Select(c=>c.WbsCode).SequenceEqual(b.Components.Select(c=>c.WbsCode)),"order changed");});
   run("Snapshot node limit stops incomplete run",()=>{
    var s=DemoSettings();s.MaximumNodes=1;bool failed=false;try{WbsBuilder.Build(Fixture(),s);}catch(ArgumentException){failed=true;}
    Assert(failed,"limit ignored");});
   run("CSV formula prefixes and quotes are escaped",()=>{
    Assert(Exporter.Cell("=1+1").StartsWith("\"'="),"formula not escaped");
    Assert(Exporter.Cell(" \t@SUM(1)").Contains("'"),"leading whitespace not protected");
    Assert(Exporter.Cell("a\"b")=="\"a\"\"b\"","quotes not escaped");});
   Console.WriteLine(count+" tests passed; 0 failed.");
  }
 }
}
