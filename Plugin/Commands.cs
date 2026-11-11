using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using DummyWbs.Core;
using NavisApp=Autodesk.Navisworks.Api.Application;
using Cursor=System.Windows.Forms.Cursor;
namespace DummyWbs.Navisworks2027
{
 [Plugin("DummyWbs.Export","EDPW",DisplayName="Dummy WBS - Export Property Inventory",ToolTip="Read model properties and export a proposed WBS without changing the document")]
 [AddInPlugin(AddInLocation.AddIn)]
 public sealed class ExportCommand:AddInPlugin
 {
  public override int Execute(params string[] parameters)
  {
   try
   {
    var document=NavisApp.ActiveDocument;
    if(document==null || !document.Models.RootItems.Any()) {MessageBox.Show("Open a model first.","Dummy WBS");return 1;}
    var config=Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),"wbs-settings.json");
    var settings=File.Exists(config)?Exporter.ReadJson<Settings>(config):Settings.CreateDefault();
    settings.Validate();
    if(MessageBox.Show("Read ALL loaded hierarchy nodes and properties, including hidden items, up to "+settings.MaximumNodes+" nodes?\n\n"+
      "Navisworks may be busy while scanning. The tool writes inventory files and a PROPOSED WBS to a new local folder. It does not alter geometry, sets, viewpoints or save the document.\n\n"+
      "Vendor identity and component boundaries require review; no SmartPlant-only schema is assumed.",
      "Dummy WBS - Confirm whole-model scan",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return 0;
    using(var folder=new FolderBrowserDialog {Description="Choose a local output folder (keep real model exports out of GitHub)",ShowNewFolderButton=true})
    {
     if(folder.ShowDialog()!=DialogResult.OK)return 0;
     Cursor.Current=Cursors.WaitCursor;
     try
     {
      var nodes=ReadModel(document,settings.MaximumNodes);
      var result=WbsBuilder.Build(nodes,settings);
      var target=Exporter.Write(folder.SelectedPath,nodes,settings,result);
      MessageBox.Show("Export completed.\nNodes: "+nodes.Count+"\nProvisional components: "+result.Components.Count+
       "\nReview issues: "+result.Issues.Count+"\n\n"+target+"\n\nOpen summary.txt, property-profile.csv and dummy-wbs.csv. Review mappings before relying on the WBS.",
       "Dummy WBS",MessageBoxButtons.OK,MessageBoxIcon.Information);
     }finally {Cursor.Current=Cursors.Default;}
    }
    return 0;
   }
   catch(Exception ex){MessageBox.Show("Export failed; do not treat any partial output folder as complete.\n\n"+ex.Message,"Dummy WBS",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
  }
  private sealed class Pending {public ModelItem Item;public string Parent="",Key="",Source="",SourceKey="",Path="";}
  private static List<Node> ReadModel(Document document,int limit)
  {
   var output=new List<Node>();var stack=new Stack<Pending>();
   var roots=document.Models.RootItems.ToArray();
   for(int i=roots.Length-1;i>=0;i--) stack.Push(new Pending {Item=roots[i],Key="R"+(i+1),Source=roots[i].DisplayName??"Unnamed source",SourceKey="R"+(i+1)});
   while(stack.Count>0)
   {
    if(output.Count>=limit)throw new InvalidOperationException("MaximumNodes reached. No complete inventory was exported. Increase MaximumNodes in wbs-settings.json only after considering memory and scan time.");
    var current=stack.Pop();var item=current.Item;
    var name=item.DisplayName??"";
    var source=current.Source;var sourceKey=current.SourceKey;
    if(LooksLikeSource(name)){source=name;sourceKey=current.Key;}
    var children=item.Children.ToArray();
    var node=new Node {Key=current.Key,ParentKey=current.Parent,SourceModel=source,SourceKey=sourceKey,Name=name,
     TreePath=current.Path.Length==0?name:current.Path+" / "+name,HasGeometry=item.HasGeometry,IsTerminalGeometry=item.HasGeometry&&children.Length==0};
    try
    {
     foreach(var category in item.PropertyCategories)
     {
      try
      {
       foreach(var property in category.Properties)
       {
        var p=new PropertyValue {Category=category.DisplayName??"",CategoryInternal=category.Name??"",Name=property.DisplayName??"",NameInternal=property.Name??""};
        try{p.Value=property.Value==null?"":property.Value.ToDisplayString();}
        catch(Exception ex){p.Error=ex.Message;}
        node.Properties.Add(p);
       }
      }catch(Exception ex){node.Properties.Add(new PropertyValue{Category=category.DisplayName??"",CategoryInternal=category.Name??"",Error="Category enumeration: "+ex.Message});}
     }
    }catch(Exception ex){node.Properties.Add(new PropertyValue{Error="Property-category enumeration: "+ex.Message});}
    output.Add(node);
    for(int i=children.Length-1;i>=0;i--) stack.Push(new Pending {Item=children[i],Key=current.Key+"."+(i+1),Parent=current.Key,Source=source,SourceKey=sourceKey,Path=node.TreePath});
   }
   return output;
  }
  private static bool LooksLikeSource(string name)
  {
   return new[]{".nwd",".nwf",".nwc",".ifc",".vue",".rvt",".dwg",".dgn",".stp",".step"}
    .Any(suffix=>name.TrimEnd().EndsWith(suffix,StringComparison.OrdinalIgnoreCase));
  }
 }
}
