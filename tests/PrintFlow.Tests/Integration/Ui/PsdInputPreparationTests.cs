using System.IO;
using System.Text;
using System.Resources;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class PsdInputPreparationTests
{
    [Fact]
    public async Task Advertised_PSD_is_imported_but_reaches_dimensions_without_raster_pixels_reproduction()
    {
        using HomeScreenHarness harness = new();
        byte[] bytes = RgbCompositePsd();
        string source = harness.Inner.Workspace.CreateSourceFile("synthetic.psd", bytes);
        ResourceManager resources = new("PrintFlow.App.Resources.Strings", typeof(HomeViewModel).Assembly);
        resources.GetString("Home_ImportFilter")!.ShouldContain("*.psd");
        harness.FilePicker.Path = source;
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);
        SessionView imported = harness.Navigation.WorkflowSelectionFor.ShouldNotBeNull();
        SessionAggregate stored = (await harness.Inner.Repository.LoadAsync(imported.Id, CancellationToken.None)).Value!;
        stored.Snapshot.ShouldNotBeNull();
        var root = stored.Revisions.Single();
        root.Facts.Format.ShouldBe(ImageFormat.Psd);
        resources.GetString("Format_Psd").ShouldBe("PSD");
        root.Facts.HasPixelDimensions.ShouldBeFalse();
        File.ReadAllBytes(harness.Inner.FileWorkspace.ResolveAbsolute(root.File)).ShouldBe(bytes);
        File.ReadAllBytes(source).ShouldBe(bytes);
        (await harness.Sessions.ExecuteAsync(imported.Id,
            new WorkflowCommand.SelectWorkflow(WorkflowType.GeneratePrintTiff), "qa", CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await harness.Sessions.ExecuteAsync(imported.Id,
            new WorkflowCommand.ConfirmOriginal(), "qa", CancellationToken.None)).IsSuccess.ShouldBeTrue();
        stored = (await harness.Inner.Repository.LoadAsync(imported.Id, CancellationToken.None)).Value!;
        stored.ToSnapshot().CurrentStep!.Step.ShouldBe(StepKind.PrintDimensions);
        var result = await harness.Sessions.ExecuteAsync(imported.Id,
            new WorkflowCommand.SetPrintDimensions(PrintDimensions.FromMillimetres(50, 50, SizePreset.Custom)),
            "qa", CancellationToken.None);
        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
    }

    // Adobe PSD v1, RGB/8, 4x3, uncompressed planar composite; resource 1057 positively
    // identifies real merged data. No customer artwork and no third-party rendering engine.
    internal static byte[] RgbCompositePsd()
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true);
        void U16(ushort value) { writer.Write((byte)(value >> 8)); writer.Write((byte)value); }
        void U32(uint value) { U16((ushort)(value >> 16)); U16((ushort)value); }
        writer.Write("8BPS"u8); U16(1); writer.Write(new byte[6]); U16(3);
        U32(3); U32(4); U16(8); U16(3); U32(0);
        U32(30); writer.Write("8BIM"u8); U16(1057); U16(0); U32(17);
        U32(1); writer.Write((byte)1); U32(0); U32(0); U32(1); writer.Write((byte)0);
        U32(0); U16(0);
        writer.Write(Enumerable.Repeat((byte)200, 12).ToArray());
        writer.Write(Enumerable.Repeat((byte)80, 12).ToArray());
        writer.Write(Enumerable.Repeat((byte)40, 12).ToArray());
        return stream.ToArray();
    }
}
