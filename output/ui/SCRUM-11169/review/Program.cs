using System.Reflection;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Outputs;
using PrintFlow.Workflow.Services;

static object Get(object o, string p) => o.GetType().GetProperty(p)!.GetValue(o)!;
static object Call(object o, string m, params object?[] args) {
 var method=o.GetType().GetMethods().Single(x=>x.Name==m);
 var all=method.GetParameters().Select((p,i)=>i<args.Length?args[i]:p.DefaultValue).ToArray();
 return method.Invoke(o,all)!;
}
var ass=Assembly.Load("PrintFlow.Tests");
using var h=(IDisposable)Activator.CreateInstance(ass.GetType("PrintFlow.Tests.Fixtures.HomeScreenHarness")!, new object?[]{null,null})!;
var inner=Get(h,"Inner");
var sessions=(ISessionService)Get(h,"Sessions");
async Task<SessionViewModel> Open(WorkflowType type,string name) {
 var file=(string)Call(inner,"WriteBorderedSourcePng",name+".png");
 var s=(await sessions.ImportAsync(type,file,name,"reviewer",default)).Value;
 var m=(SessionViewModel)Call(h,"Session",Get(h,"Navigation")); m.Open(s); await m.PreviewsLoaded; return m;
}
var m=await Open(WorkflowType.GeneratePrintTiff,"white");
await m.ConfirmOriginalCommand.ExecuteAsync(null); m.WidthMmText="200"; m.HeightMmText="150";
await m.SetMaximumBoundsCommand.ExecuteAsync(null);
m.SelectedWhiteUnderbaseChoice=m.WhiteUnderbaseChoices.Single(x=>x.Branch==WhiteUnderbaseBranch.W1_1px);
await m.SelectWhiteUnderbaseCommand.ExecuteAsync(null);
Console.WriteLine($"WHITE_INITIAL page={m.GuidedPage} runVisible={m.ShowsGuidedRunStep}");
m.ChangeGuidedWhiteInkCommand.Execute(null);
m.SelectedWhiteUnderbaseChoice=m.WhiteUnderbaseChoices.Single(x=>x.Branch==WhiteUnderbaseBranch.W1_1px);
await m.SelectWhiteUnderbaseCommand.ExecuteAsync(null);
Console.WriteLine($"WHITE_RECONFIRM page={m.GuidedPage} runVisible={m.ShowsGuidedRunStep} persisted={(await sessions.LoadAsync(m.Id,default)).Value.WhiteUnderbaseBranch} notice={m.Notice}");
var trim=await Open(WorkflowType.PrepareAsset,"trim");
await trim.ConfirmOriginalCommand.ExecuteAsync(null); await trim.SkipCommand.ExecuteAsync(null); await trim.SkipCommand.ExecuteAsync(null);
Console.WriteLine($"TRIM_BEFORE page={trim.GuidedPage} uniform={trim.UniformMarginText} mode={trim.SelectedTrimMode.Mode}");
trim.SelectedTrimMode=trim.TrimModes.Single(x=>x.Mode.ToString()=="UniformMargin"); trim.UniformMarginText="2";
await trim.ApplyTrimMarginCommand.ExecuteAsync(null);
Console.WriteLine($"TRIM_APPLIED uniform={trim.UniformMarginText} summary={trim.TrimParametersSummary} notice={trim.Notice}");
trim.ViewHistoryCommand.Execute(trim.Steps.First());
Console.WriteLine($"TRIM_HISTORY prompt={trim.IsConfirmingDraftLeave} history={trim.IsViewingHistory}");
await trim.DiscardDraftAndLeaveCommand.ExecuteAsync(null); trim.CloseHistoryCommand.Execute(null);
Console.WriteLine($"TRIM_AFTER_DISCARD uniform={trim.UniformMarginText} mode={trim.SelectedTrimMode.Mode} summary={trim.TrimParametersSummary}");

Console.WriteLine($"TRIM_PERSISTED={(await sessions.LoadAsync(trim.Id,default)).Value.TrimMargin}");
var cropfile=(string)Call(inner,"WriteOpaqueSourcePng","crop.png");
var cropstate=(await sessions.ImportAsync(WorkflowType.PrepareAsset,cropfile,"crop","reviewer",default)).Value;
var crop=(SessionViewModel)Call(h,"Session",Get(h,"Navigation")); crop.Open(cropstate);
await crop.ConfirmOriginalCommand.ExecuteAsync(null); await crop.SkipCommand.ExecuteAsync(null); await crop.SkipCommand.ExecuteAsync(null); await crop.RunStepCommand.ExecuteAsync(null); await crop.PreviewsLoaded;
crop.BeginManualCropCommand.Execute(null);
crop.TrySetCropSelection(new CropSurfaceLayout(12,10,12,10,12,10,crop.IsFitToViewport,crop.ZoomScale),3,2,9,7);
await crop.BackToHomeCommand.ExecuteAsync(null);
Console.WriteLine($"CROP_MODAL prompt={crop.IsConfirmingDraftLeave} canManual={crop.CanManualCrop} selection={crop.CropSelection}");
Exception? failure=null;
var thread=new Thread(()=>{ try {
 var view=new PrintFlow.App.Views.SessionScreenView {DataContext=crop};
 var size=new System.Windows.Size(1000,700); view.Measure(size); view.Arrange(new System.Windows.Rect(size)); view.UpdateLayout();
 IEnumerable<System.Windows.DependencyObject> Walk(System.Windows.DependencyObject root) {yield return root; for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++) foreach(var child in Walk(System.Windows.Media.VisualTreeHelper.GetChild(root,i))) yield return child;}
 var button=Walk(view).OfType<System.Windows.Controls.Button>().Single(x=>System.Windows.Automation.AutomationProperties.GetAutomationId(x)=="Session.BeginManualCrop");
 bool shown=true; for(System.Windows.DependencyObject? e=button;e!=null;e=System.Windows.Media.VisualTreeHelper.GetParent(e)) if(e is System.Windows.UIElement u&&u.Visibility!=System.Windows.Visibility.Visible)shown=false;
 Console.WriteLine($"CROP_MODAL_BUTTON enabled={button.IsEnabled} shown={shown}");
 var peer=System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(button)!;
 ((System.Windows.Automation.Provider.IInvokeProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)!).Invoke();
 System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.SystemIdle);
 crop.KeepDraftCommand.Execute(null);
 Console.WriteLine($"CROP_AFTER_KEEP selection={crop.CropSelection} cropping={crop.IsCropping}");
 view.DataContext=null;view.UpdateLayout();
 }catch(Exception ex){failure=ex;}}); thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join(); if(failure!=null)throw failure;
var dimA=await Open(WorkflowType.GeneratePrintTiff,"dimA"); await dimA.ConfirmOriginalCommand.ExecuteAsync(null);dimA.ChooseCustomSizeCommand.Execute(null);
var dimB=await Open(WorkflowType.GeneratePrintTiff,"dimB"); await dimB.ConfirmOriginalCommand.ExecuteAsync(null);dimB.ChooseCustomSizeCommand.Execute(null);
var sourceB=await Open(WorkflowType.GeneratePrintTiff,"sourceB");
failure=null;
thread=new Thread(()=>{try {
 var view=new PrintFlow.App.Views.SessionScreenView {DataContext=dimA};
 var size=new System.Windows.Size(1000,700); view.Measure(size);view.Arrange(new System.Windows.Rect(size));view.UpdateLayout();
 IEnumerable<System.Windows.DependencyObject> Walk(System.Windows.DependencyObject root){yield return root;for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++) foreach(var child in Walk(System.Windows.Media.VisualTreeHelper.GetChild(root,i)))yield return child;}
 var input=Walk(view).OfType<System.Windows.Controls.TextBox>().Single(x=>System.Windows.Automation.AutomationProperties.GetAutomationId(x)=="Session.PrintDimensions.TargetMillimetres");
 var provider=(System.Windows.Automation.Provider.IValueProvider)System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(input)!.GetPattern(System.Windows.Automation.Peers.PatternInterface.Value)!;
 view.DataContext=dimB;view.UpdateLayout(); provider.SetValue("old-task-input");
 Console.WriteLine($"STALE_TEXTBOX taskB={dimB.CustomMillimetresText}");
 view.DataContext=sourceB;view.UpdateLayout();provider.SetValue("hidden-old-task-input");
 Console.WriteLine($"HIDDEN_TEXTBOX page={sourceB.GuidedPage} target={sourceB.CustomMillimetresText} ownEnabled={input.IsEnabled}");
 view.DataContext=null;view.UpdateLayout();
}catch(Exception ex){failure=ex;}});thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(failure!=null)throw failure;
var fixtures=ass.GetType("PrintFlow.Tests.Fixtures.FinalSaveFixtures")!;
var finalReview=await (Task<SessionView>)fixtures.GetMethod("PngAtFinalReviewAsync")!.Invoke(null,new object?[]{inner,sessions})!;
var delivery=(PrintFlow.Workflow.Delivery.IApprovedArtifactDeliveryService)fixtures.GetMethod("Delivery")!.Invoke(null,new object?[]{inner,null})!;
var folder=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"printflow-e2-readonly-review-20261019","saved-"+Guid.NewGuid().ToString("N"));System.IO.Directory.CreateDirectory(folder);
var saved=new SessionViewModel(sessions,(PrintFlow.Workflow.Services.IArtefactPreviewService)Get(h,"Previews"),(IProductionTiffReviewService)Get(h,"TiffReviews"),(PrintFlow.App.Navigation.INavigationService)Get(h,"Navigation"),finalSave:new PrintFlow.Workflow.Delivery.FinalSaveCoordinator(sessions,delivery,"reviewer"),folderPicker:new TempFolder(folder));
saved.Open(finalReview);await saved.PreviewsLoaded;await saved.FinalSaveFactsLoaded;
string originalSaveName=saved.SaveFileNameText;
saved.SaveFileNameText="confirmed-output.png";saved.ChangeLocationCommand.Execute(null);
await saved.ConfirmAndSaveCommand.ExecuteAsync(null);await saved.FinalSaveFactsLoaded;
Console.WriteLine($"SAVE_CONFIRMED name={saved.SaveFileNameText} folder={saved.SaveFolderText} path={saved.FinalSaveSavedPath}");
saved.SaveFileNameText="unfinished-second-name.png";
saved.ViewHistoryCommand.Execute(saved.Steps.First());
Console.WriteLine($"SAVE_SECOND_EDIT prompt={saved.IsConfirmingDraftLeave}");
await saved.DiscardDraftAndLeaveCommand.ExecuteAsync(null);saved.CloseHistoryCommand.Execute(null);
Console.WriteLine($"SAVE_AFTER_DISCARD original={originalSaveName} name={saved.SaveFileNameText} folder={saved.SaveFolderText} path={saved.FinalSaveSavedPath}");
var observed=(PrintFlow.Workflow.Delivery.IApprovedArtifactDeliveryService)Activator.CreateInstance(ass.GetType("PrintFlow.Tests.Fixtures.ObservedDeliveryService")!,new object?[]{delivery})!;
var saveHold=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);observed.GetType().GetProperty("HoldDelivery")!.SetValue(observed,saveHold);
var delayed=new SessionViewModel(sessions,(IArtefactPreviewService)Get(h,"Previews"),(IProductionTiffReviewService)Get(h,"TiffReviews"),(PrintFlow.App.Navigation.INavigationService)Get(h,"Navigation"),finalSave:new PrintFlow.Workflow.Delivery.FinalSaveCoordinator(sessions,observed,"reviewer"),folderPicker:new TempFolder(folder));
delayed.Open((await sessions.LoadAsync(finalReview.Id,default)).Value);await delayed.PreviewsLoaded;await delayed.FinalSaveFactsLoaded;
delayed.SaveFileNameText="delayed-"+Guid.NewGuid().ToString("N")+".png";delayed.ChangeLocationCommand.Execute(null);
var pendingSave=delayed.SaveApprovedCommand.ExecuteAsync(null);await ((TaskCompletionSource)Get(observed,"DeliveryEntered")).Task.WaitAsync(TimeSpan.FromSeconds(10));
try {
 delayed.Open((await sessions.LoadAsync(sourceB.Id,default)).Value);await delayed.PreviewsLoaded;
 Console.WriteLine($"OLD_SAVE_OPEN_B idB={delayed.Id==sourceB.Id} page={delayed.GuidedPage} busy={delayed.IsBusy}");
 await delayed.ConfirmOriginalCommand.ExecuteAsync(null);
 Console.WriteLine($"OLD_SAVE_B_CONFIRM page={delayed.GuidedPage} persistedStep={(await sessions.LoadAsync(sourceB.Id,default)).Value.CurrentStep?.Step}");
} finally {saveHold.SetResult();await pendingSave;}
Console.WriteLine($"OLD_SAVE_END_B idB={delayed.Id==sourceB.Id} page={delayed.GuidedPage} busy={delayed.IsBusy}");
public class TempFolder(string path):PrintFlow.App.Navigation.IDeliveryFolderPicker { public string? PickFolder(string title,string? initial)=>path; }


