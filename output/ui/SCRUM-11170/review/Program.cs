using System.Reflection;
using System.Text.Json;
using PrintFlow.App.ViewModels;
using PrintFlow.Workflow.Services;
using PrintFlow.Tests.Integration.Ui;
using PrintFlow.App.Navigation;
using PrintFlow.App.Startup;

static object P(object o, string n) => o.GetType().GetProperty(n)!.GetValue(o)!;
static object M(object o, string n, params object?[] a) => o.GetType().GetMethod(n)!.Invoke(o,a)!;
static object NewHarness() => Activator.CreateInstance(typeof(GuidedImportHomeTests).Assembly.GetType("PrintFlow.Tests.Fixtures.HomeScreenHarness")!, new object?[]{null,null})!;
static void Retain(object h) { var inner=P(h,"Inner"); foreach(var n in new[]{"Workspace","Database"}) {var f=P(inner,n); f.GetType().GetProperty("RetainForInspection")!.SetValue(f,true); Console.WriteLine(n+": "+P(f,n=="Workspace"?"Root":"Path"));} }
static HomeViewModel Fresh(object h, ISessionService? service=null) => new(service??(ISessionService)P(h,"Sessions"),(IArtefactPreviewService)P(h,"Previews"),(INavigationService)P(h,"Navigation"),(IFilePicker)P(h,"FilePicker"),(StartupStatusAccessor)P(h,"StartupStatus"),(ReadinessObservationAccessor)P(h,"Readiness"));

var h=NewHarness(); Retain(h);
var home=(HomeViewModel)P(h,"Home"); var nav=P(h,"Navigation");
var path=(string)M(h,"WriteSourceFile","never-chose-a-route.png");
await home.DropFilesCommand.ExecuteAsync(new[]{path});
var imported=(SessionView)P(nav,"WorkflowSelectionFor");
var route=(WorkflowSelectionViewModel)M(h,"WorkflowSelection",nav); route.Open(imported);
await route.BackToHomeCommand.ExecuteAsync(null);
var resumedHome=Fresh(h); await resumedHome.RefreshCommand.ExecuteAsync(null);
await resumedHome.ResumeCommand.ExecuteAsync(resumedHome.ContinueSessions.Single());
var resumed=(SessionView)P(nav,"SessionFor");
Console.WriteLine(JsonSerializer.Serialize(new {Probe="BackWithoutRouteThenContinue", WasExplicitSelectInvoked=false, ResumedRoute=resumed.WorkflowType.ToString(), ResumedStep=resumed.CurrentStep!.Step.ToString(), Destination="Session", CanConfirmOriginal=resumed.AvailableCommands.Any(x=>x.ToString()=="ConfirmOriginal")}));

var c=NewHarness(); Retain(c);
var service=DispatchProxy.Create<ISessionService,GuidedSessionInputTests.DeferredService>();
var proxy=(GuidedSessionInputTests.DeferredService)service; proxy.Inner=(ISessionService)P(c,"Sessions");
var cancelHome=Fresh(c,service); await cancelHome.RefreshCommand.ExecuteAsync(null);
var gate=proxy.HoldNext(nameof(ISessionService.ImportAsync));
var importing=cancelHome.DropFilesCommand.ExecuteAsync(new[]{(string)M(c,"WriteSourceFile","cancelled-wait.png")});
await gate.Started.Task; cancelHome.CancelImportCommand.Execute(null); gate.Release.SetResult(); await importing;
Console.WriteLine(JsonSerializer.Serialize(new {Probe="CancelCommittedImport", Persisted=(await proxy.Inner.ListRecentAsync(default)).Value.Count, ContinueRows=cancelHome.ContinueSessions.Count(), RecentRows=cancelHome.RecentSessions.Count, cancelHome.Notice}));
var draftRoute=(WorkflowSelectionViewModel)M(h,"WorkflowSelection",nav); draftRoute.Open(imported);
draftRoute.OutputName="operator-new-output-name";
var homesBefore=(int)P(nav,"GoHomeCount"); await draftRoute.BackToHomeCommand.ExecuteAsync(null);
var storedAfterBack=(await ((ISessionService)P(h,"Sessions")).LoadAsync(imported.Id,default)).Value;
Console.WriteLine(JsonSerializer.Serialize(new {Probe="BackDiscardsRouteNameDraft", NavigatedImmediately=(int)P(nav,"GoHomeCount")==homesBefore+1, Typed=draftRoute.OutputName, Persisted=storedAfterBack.OutputName.Value}));
