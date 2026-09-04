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
    public async Task Advertised_PSD_is_preserved_but_cannot_advance_without_explicit_preparation()
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
            new WorkflowCommand.ConfirmOriginal(), "qa", CancellationToken.None)).IsFailure.ShouldBeTrue();
        var refused = await harness.Sessions.ExecuteAsync(imported.Id,
            new WorkflowCommand.StartStep(StepKind.OriginalConfirmation), "qa", CancellationToken.None);
        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(FailureCode.PsdUnsupported);
        stored = (await harness.Inner.Repository.LoadAsync(imported.Id, CancellationToken.None)).Value!;
        stored.ToSnapshot().CurrentStep!.Step.ShouldBe(StepKind.OriginalConfirmation);
        stored.ToSnapshot().CurrentStep!.State.ShouldBe(StepState.Failed);
        stored.Revisions.Count.ShouldBe(1);
        var result = await harness.Sessions.ExecuteAsync(imported.Id,
            new WorkflowCommand.SetPrintDimensions(PrintDimensions.FromMillimetres(50, 50, SizePreset.Custom)),
            "qa", CancellationToken.None);
        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.PsdPreparationFailed);
    }

    // Adobe PSD v1, RGB/8, 4x3, uncompressed planar composite; resource 1057 positively
    // identifies real merged data. No customer artwork and no third-party rendering engine.
    internal static byte[] RgbCompositePsd(bool transparency = false, bool spot = false)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true);
        void U16(ushort value) { writer.Write((byte)(value >> 8)); writer.Write((byte)value); }
        void U32(uint value) { U16((ushort)(value >> 16)); U16((ushort)value); }
        writer.Write("8BPS"u8); U16(1); writer.Write(new byte[6]); U16(transparency || spot ? (ushort)4 : (ushort)3);
        U32(3); U32(4); U16(8); U16(3); U32(0);
        U32(spot ? 72u : 30u); writer.Write("8BIM"u8); U16(1057); U16(0); U32(17);
        U32(1); writer.Write((byte)1); U32(0); U32(0); U32(1); writer.Write((byte)0);
        if (spot)
        {
            writer.Write("8BIM"u8); U16(1006); U16(0); U32(3); writer.Write(new byte[] { 2, 87, 49, 0 });
            writer.Write("8BIM"u8); U16(1007); U16(0); U32(14);
            U16(0); U16(65535); U16(0); U16(0); U16(0); U16(100); writer.Write(new byte[] { 2, 0 });
        }
        if (transparency)
        {
            // One real RGB layer, with an alpha plane. A negative layer count declares
            // that the merged image's fourth plane is transparency, per Adobe's spec.
            U32(136); U32(128); U16(unchecked((ushort)-1));
            U32(0); U32(0); U32(3); U32(4); U16(4);
            U16(0); U32(14); U16(1); U32(14); U16(2); U32(14); U16(65535); U32(14);
            writer.Write("8BIMnorm"u8); writer.Write(new byte[] { 255, 0, 0, 0 });
            U32(12); U32(0); U32(0); writer.Write(new byte[] { 1, 65, 0, 0 });
            foreach (byte colour in new byte[] { 200, 80, 40 }) { U16(0); writer.Write(Enumerable.Repeat(colour, 12).ToArray()); }
            U16(0); writer.Write(new byte[] { 0, 0, 0, 0, 0, 255, 128, 0, 0, 0, 0, 0 });
            U32(0);
        }
        else U32(0);
        U16(0);
        writer.Write(Enumerable.Repeat((byte)200, 12).ToArray());
        writer.Write(Enumerable.Repeat((byte)80, 12).ToArray());
        writer.Write(Enumerable.Repeat((byte)40, 12).ToArray());
        if (transparency) writer.Write(new byte[] { 0, 0, 0, 0, 0, 255, 128, 0, 0, 0, 0, 0 });
        else if (spot) writer.Write(Enumerable.Repeat((byte)255, 12).ToArray());
        return stream.ToArray();
    }
}
