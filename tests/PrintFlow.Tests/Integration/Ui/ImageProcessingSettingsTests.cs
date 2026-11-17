using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Settings;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class ImageProcessingSettingsTests
{
    [Fact]
    public async Task Home_can_import_for_print_only_while_damaged_settings_stop_an_image_processing_route()
    {
        using HomeScreenHarness harness = new();
        await harness.Inner.Settings.UpsertAsync(
            [SettingEntry.Text(SettingKey.ImageProcessingDefaults, "{}")], default);

        await harness.Home.DropFilesCommand.ExecuteAsync(new[] { harness.WriteSourceFile("print-only.png") });
        var imported = harness.Navigation.WorkflowSelectionFor.ShouldNotBeNull(harness.Home.Notice);
        imported.WorkflowType.ShouldBe(WorkflowType.GeneratePrintTiff);

        var denied = await harness.Sessions.ExecuteAsync(imported.Id,
            new WorkflowCommand.SelectWorkflow(WorkflowType.PrepareCustomerDesign), "tester", default);
        denied.IsFailure.ShouldBeTrue();
        denied.Failure.MessageKey.ShouldBe("ImageProcessing_InvalidDefaults");
        (await harness.Sessions.LoadAsync(imported.Id, default)).Value.WorkflowType.ShouldBe(WorkflowType.GeneratePrintTiff);

        await harness.Inner.Settings.UpsertAsync(
            [ImageProcessingPolicy.CompatibleDefault.ToSetting(), SettingEntry.Integer(SettingKey.TrimSafetyMarginPixels, 9)],
            default);
        var repaired = await harness.Sessions.ExecuteAsync(imported.Id,
            new WorkflowCommand.SelectWorkflow(WorkflowType.PrepareCustomerDesign), "tester", default);
        repaired.IsSuccess.ShouldBeTrue(repaired.IsFailure ? repaired.Failure.ToString() : null);
        repaired.Value.TrimMargin.Top.ShouldBe(9);
        repaired.Value.ImageProcessingSelection.ShouldBe(SessionImageProcessingSelection.Compatible);
    }

    [Fact]
    public async Task The_WPF_page_renders_three_method_selectors_and_the_save_actions()
    {
        using SettingsScreenHarness harness = new();
        SettingsViewModel screen = await harness.OpenAsync();
        await screen.OpenImageProcessingCommand.ExecuteAsync(null);
        WpfRendering.RenderExpectingNoBindingErrors(() => new SettingsView { DataContext = screen },
            WpfRendering.ReviewViewport, tree =>
            {
                ComboBox ById(string id) => tree.OfType<ComboBox>()
                    .Single(control => AutomationProperties.GetAutomationId(control) == id);

                ComboBox enhancement = ById("ImageProcessing.Enhancement");
                ComboBox background = ById("ImageProcessing.BackgroundRemoval");
                ComboBox trim = ById("ImageProcessing.Crop");
                enhancement.SelectedValue.ShouldBe(ImageProcessingMethod.MeituEnhancement);
                background.SelectedValue.ShouldBe(ImageProcessingMethod.MeituBackgroundRemoval);
                trim.SelectedValue.ShouldBe(ImageProcessingMethod.AlphaTrim);
                enhancement.Items.Cast<SettingsViewModel.MethodOption>().Count(option => option.IsAvailable).ShouldBe(1);
                foreach (string id in new[] { "ImageProcessing.Back", "ImageProcessing.RestoreDefaults", "ImageProcessing.Save" })
                {
                    Button action = tree.OfType<Button>().Single(control => AutomationProperties.GetAutomationId(control) == id);
                    action.Visibility.ShouldBe(Visibility.Visible);
                    action.ActualHeight.ShouldBeGreaterThan(0);
                }
                return 0;
            });

        if (Environment.GetEnvironmentVariable("PRINTFLOW_IMAGE_PROCESSING_CAPTURE") is { Length: > 0 } folder)
        {
            WpfRendering.CapturePng(() => new SettingsView { DataContext = screen },
                WpfRendering.ReviewViewport, Path.Combine(folder, "image-processing-settings-wpf.png"));
            WpfRendering.CapturePng(() => new SettingsView { DataContext = screen },
                new Size(720, 500), Path.Combine(folder, "image-processing-settings-wpf-compact.png"));
        }
    }

    [Fact]
    public async Task Saving_the_page_persists_margin_and_noise_defaults_together()
    {
        using SettingsScreenHarness harness = new();
        SettingsViewModel screen = await harness.OpenAsync();
        await screen.OpenImageProcessingCommand.ExecuteAsync(null);
        screen.IsEditingImageProcessing.ShouldBeTrue();
        screen.TrimSafetyMarginPixels = "12";
        screen.IgnoreSmallTrimNoise = false;
        await screen.SaveImageProcessingCommand.ExecuteAsync(null);

        var entries = (await harness.Settings.ReadAllAsync(default)).Value;
        entries.Select(e => e.Key).ShouldBe([SettingKey.TrimSafetyMarginPixels, SettingKey.ImageProcessingDefaults], ignoreOrder: true);
        ImageProcessingPolicy.FromSettings(entries).Value.IgnoreSmallTrimNoise.ShouldBeFalse();
        SettingsViewModel reopened = await harness.RestartAndOpenAsync();
        await reopened.OpenImageProcessingCommand.ExecuteAsync(null);
        reopened.TrimSafetyMarginPixels.ShouldBe("12");
        reopened.IgnoreSmallTrimNoise.ShouldBeFalse();
        reopened.SelectedCropMethod.ShouldBe(ImageProcessingMethod.AlphaTrim);
    }

    [Fact]
    public async Task Discarding_the_page_does_not_write_settings()
    {
        using SettingsScreenHarness harness = new();
        SettingsViewModel screen = await harness.OpenAsync();
        await screen.OpenImageProcessingCommand.ExecuteAsync(null);
        screen.TrimSafetyMarginPixels = "15";
        Task navigation = screen.BackFromImageProcessingCommand.ExecuteAsync(null);
        screen.IsImageProcessingLeavePending.ShouldBeTrue();
        screen.KeepEditingImageProcessingCommand.Execute(null);
        await navigation;
        screen.IsEditingImageProcessing.ShouldBeTrue();
        screen.TrimSafetyMarginPixels.ShouldBe("15");

        navigation = screen.BackFromImageProcessingCommand.ExecuteAsync(null);
        screen.DiscardImageProcessingCommand.Execute(null);
        await navigation;
        screen.IsEditingImageProcessing.ShouldBeFalse();
        (await harness.Settings.ReadAllAsync(default)).Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Damaged_defaults_require_an_explicit_reset_before_save()
    {
        using SettingsScreenHarness harness = new();
        await harness.Settings.UpsertAsync([SettingEntry.Text(SettingKey.ImageProcessingDefaults, "{}")], default);
        SettingsViewModel screen = await harness.OpenAsync();
        await screen.OpenImageProcessingCommand.ExecuteAsync(null);
        screen.ImageProcessingNotice.ShouldNotBeNullOrWhiteSpace();
        screen.SaveImageProcessingCommand.CanExecute(null).ShouldBeFalse();
        screen.RestoreImageProcessingDefaultsCommand.Execute(null);
        screen.SaveImageProcessingCommand.CanExecute(null).ShouldBeTrue();
        await screen.SaveImageProcessingCommand.ExecuteAsync(null);
        var entries = (await harness.Settings.ReadAllAsync(default)).Value;
        ImageProcessingPolicy.FromSettings(entries).Value.ShouldBe(ImageProcessingPolicy.CompatibleDefault);
    }
}
