using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using DummyWbs.Core;
using NavisApp=Autodesk.Navisworks.Api.Application;
namespace DummyWbs.Navisworks2027
{
 [Plugin("DummyWbs.Export","EDPW",DisplayName="Dummy WBS - Export Property Inventory",ToolTip="Cancellable property inventory; start with a 1,000-node sample")]
 [AddInPlugin(AddInLocation.AddIn)]
 public sealed class ExportCommand:AddInPlugin
 {
  private static bool running;
  public override int Execute(params string[] parameters)
  {
   if(running)return 1;running=true;
   try
   {
    var doc=NavisApp.ActiveDocument;
    if(doc==null||!doc.Models.RootItems.Any()){MessageBox.Show("Open a model first.","Dummy WBS");return 1;}
    var path=Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),"wbs-settings.json");
    var settings=File.Exists(path)?Exporter.ReadJson<Settings>(path):Settings.CreateDefault();settings.Validate();
    bool sample;
    using(var options=new Form{Text="Dummy WBS 0.1.1 - Scan scope",Width=570,Height=280,StartPosition=FormStartPosition.CenterScreen,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false})
    {
     var small=new RadioButton{Left=18,Top=90,Width=520,Text="Sample: at most 1,000 nodes (recommended)",Checked=true};
     var full=new RadioButton{Left=18,Top=122,Width=520,Text="Whole model: maximum "+settings.MaximumNodes+" nodes"};
     var ok=new Button{Text="Continue",Left=320,Top=194,Width=100,DialogResult=DialogResult.OK};
     var cancel=new Button{Text="Cancel",Left=430,Top=194,Width=100,DialogResult=DialogResult.Cancel};
     options.Controls.AddRange(new Control[]{
      new Label{Left=18,Top=18,Width=520,Height=64,Text="Start small. Select ONE branch in the Selection Tree to sample it with ancestor context. With no selection, sample from model roots. Includes hidden items; does not change the model."},
      small,full,new Label{Left=18,Top=156,Width=520,Height=26,Text="Cancel supported; two-minute limit between API calls."},ok,cancel});
     options.AcceptButton=ok;options.CancelButton=cancel;
     if(options.ShowDialog()!=DialogResult.OK)return 0;sample=small.Checked;
    }
    var selected=sample?doc.CurrentSelection.SelectedItems.ToArray():new ModelItem[0];
    if(selected.Length>1){MessageBox.Show("Select one branch, or clear selection, and run again.","Dummy WBS");return 1;}
    using(var folder=new FolderBrowserDialog{Description="Choose local output folder; keep model exports out of GitHub",ShowNewFolderButton=true})
    {
     if(folder.ShowDialog()!=DialogResult.OK)return 0;
     string message;
     try{message=Run(doc,settings,sample,selected.FirstOrDefault(),folder.SelectedPath);}
     catch(OperationCanceledException){message="Cancelled. Any unfinished export is not a complete result. Check the scan log in your chosen output folder.";}
     catch(TimeoutException ex){message=ex.Message+"\n\nChoose a smaller branch next time. Check the scan log.";}
     MessageBox.Show(message,"Dummy WBS");
    }
    return 0;
   }
   catch(Exception ex){MessageBox.Show("Export failed. Ignore unfinished output.\n\n"+ex.Message,"Dummy WBS");return 1;}
   finally{running=false;}
  }
  private static string Run(Document doc,Settings settings,bool sample,ModelItem selected,string folder)
  {
   string log=Path.Combine(folder,"dummy-wbs-scan-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6)+".log");
   int limit=sample?Math.Min(1000,settings.MaximumNodes):settings.MaximumNodes;
   var watch=Stopwatch.StartNew();double lastUi=-1,lastLog=-1;string lastStage="";bool completed=false;
   var progress=NavisApp.BeginProgress("Dummy WBS 0.1.1","Node budget, not percent of full model. Cancel to stop.");
   try
   {
    Action<string,int> checkpoint=(stage,count)=>{
     double elapsed=watch.Elapsed.TotalSeconds;bool changed=stage!=lastStage;
     if(elapsed-lastLog>=2||changed)
     {File.AppendAllText(log,DateTime.UtcNow.ToString("O")+" | "+stage+" | "+count+" processed | "+elapsed.ToString("0.0")+"s"+Environment.NewLine);lastLog=elapsed;}
     if(elapsed-lastUi>=0.2||changed)
     {
      progress.WorkingFileName=stage+" - "+count+" processed ("+elapsed.ToString("0")+"s)";
      double fraction=stage=="Reading model"?0.65*Math.Min(1,(double)count/limit):stage=="Writing files"?0.9:0.75;
      if(!progress.Update(fraction)||progress.IsCanceled)throw new OperationCanceledException();
      lastUi=elapsed;lastStage=stage;
     }
     if(elapsed>=120)throw new TimeoutException("Two-minute cooperative time limit reached.");
    };
    bool truncated;checkpoint("Reading model",0);
    var nodes=ReadModel(doc,limit,sample,selected,checkpoint,out truncated);
    checkpoint("Building WBS",0);
    var result=WbsBuilder.Build(nodes,settings,checkpoint);
    string scope=sample?"SAMPLE ONLY - "+(selected!=null?"selected branch plus ancestors":"model-root traversal")+
     "; "+(truncated?"node budget reached; descendants may be incomplete":"selected scope traversal finished")+
     ". This is NOT whole-model coverage.":"Whole loaded-model traversal completed.";
    var target=Exporter.Write(folder,nodes,settings,result,checkpoint,scope);
    File.AppendAllText(log,"COMPLETE - "+scope+Environment.NewLine);completed=true;progress.Update(1);
    return "Export completed.\n"+scope+"\n\nNodes: "+nodes.Count+"\nProvisional components: "+result.Components.Count+
     "\nReview issues: "+result.Issues.Count+"\n\n"+target+"\n\nLog: "+log;
   }
   finally{NavisApp.EndProgress();if(!completed)File.AppendAllText(log,"STOPPED WITHOUT COMPLETE EXPORT"+Environment.NewLine);}
  }
  private sealed class Pending{public ModelItem Item;public string Key="",Parent="",Source="",SourceKey="",Path="";}
  private static List<Node> ReadModel(Document doc,int limit,bool sample,ModelItem selected,Action<string,int> checkpoint,out bool truncated)
  {
   var nodes=new List<Node>();var stack=new Stack<Pending>();
   if(selected!=null)
   {
    var ancestors=new List<ModelItem>();for(var p=selected.Parent;p!=null;p=p.Parent)ancestors.Add(p);ancestors.Reverse();
    string parent="",source=selected.DisplayName??"Selected source",sourceKey="S",path="";
    foreach(var ancestor in ancestors)
    {
     checkpoint("Reading model",nodes.Count);
     if(nodes.Count>=limit)throw new InvalidOperationException("Ancestor depth exceeds sample budget.");
     string key="C"+(nodes.Count+1);
     var context=ReadOne(ancestor,new Pending{Key=key,Parent=parent,Source=source,SourceKey=sourceKey,Path=path},false,false,checkpoint,nodes.Count);
     nodes.Add(context);parent=key;source=context.SourceModel;sourceKey=context.SourceKey;path=context.TreePath;
    }
    stack.Push(new Pending{Item=selected,Key="S",Parent=parent,Source=source,SourceKey=sourceKey,Path=path});
   }
   else
   {
    var roots=doc.Models.RootItems.ToArray();
    for(int i=roots.Length-1;i>=0;i--)stack.Push(new Pending{Item=roots[i],Key="R"+(i+1),Source=roots[i].DisplayName??"Unnamed source",SourceKey="R"+(i+1)});
   }
   while(stack.Count>0)
   {
    checkpoint("Reading model",nodes.Count);
    if(nodes.Count>=limit){if(sample)break;throw new InvalidOperationException("MaximumNodes reached; whole-model scan is incomplete.");}
    var p=stack.Pop();var children=p.Item.Children.ToArray();
    var node=ReadOne(p.Item,p,p.Item.HasGeometry,children.Length==0,checkpoint,nodes.Count);nodes.Add(node);
    for(int i=children.Length-1;i>=0;i--)stack.Push(new Pending{Item=children[i],Key=p.Key+"."+(i+1),Parent=p.Key,Source=node.SourceModel,SourceKey=node.SourceKey,Path=node.TreePath});
   }
   truncated=stack.Count>0;return nodes;
  }
  private static Node ReadOne(ModelItem item,Pending p,bool geometry,bool terminal,Action<string,int> checkpoint,int count)
  {
   string name=item.DisplayName??"",source=p.Source,sourceKey=p.SourceKey;
   if(new[]{".nwd",".nwf",".nwc",".ifc",".vue",".rvt",".dwg",".dgn",".stp",".step"}.Any(s=>name.TrimEnd().EndsWith(s,StringComparison.OrdinalIgnoreCase))){source=name;sourceKey=p.Key;}
   var node=new Node{Key=p.Key,ParentKey=p.Parent,SourceModel=source,SourceKey=sourceKey,Name=name,TreePath=p.Path.Length==0?name:p.Path+" / "+name,HasGeometry=geometry,IsTerminalGeometry=geometry&&terminal};
   try
   {
    foreach(var category in item.PropertyCategories)
    {
     checkpoint("Reading model",count);
     try
     {
      foreach(var property in category.Properties)
      {
       checkpoint("Reading model",count);
       var value=new PropertyValue{Category=category.DisplayName??"",CategoryInternal=category.Name??"",Name=property.DisplayName??"",NameInternal=property.Name??""};
       try{value.Value=property.Value==null?"":property.Value.ToDisplayString();}catch(Exception ex){value.Error=ex.Message;}
       node.Properties.Add(value);
      }
     }catch(Exception ex) when (!(ex is OperationCanceledException)&&!(ex is TimeoutException))
     {node.Properties.Add(new PropertyValue{Category=category.DisplayName??"",Error="Category enumeration: "+ex.Message});}
    }
   }catch(Exception ex) when (!(ex is OperationCanceledException)&&!(ex is TimeoutException))
   {node.Properties.Add(new PropertyValue{Error="Category enumeration: "+ex.Message});}
   return node;
  }
 }
}
