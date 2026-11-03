using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using PrintFlow.App.Controls;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Integration.AiDesign.Authoring;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class AiPromptEditorTests
{
    private static readonly Guid TextId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid PersonId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid LayoutId = Guid.Parse("20000000-0000-0000-0000-000000000002");


    [Fact]
    public void Typing_and_paste_preserve_text_around_reference_nodes() => WpfRendering.OnStaThread(() =>
    {
        AiPromptEditor control = new() { Document = Sample() };
        RichTextBox box = Box(control);
        var person = Tokens(box).First();
        box.CaretPosition = person.ElementStart;
        box.CaretPosition.InsertTextInRun("图3");
        box.Selection.Select(person.ElementEnd, person.ElementEnd);
        const string literal = "  ʻĀna\r\n陈 {{姓名}}  ";
        Paste(box, literal);

        Text(control.Document).ShouldBe("before图3" + literal + "after");
        control.Document.Parts.OfType<AiTextPart>().ShouldContain(x => x.Text.Contains("图3"));
        control.Document.Parts.OfType<AiImagePart>().Select(x => (x.PartId, x.SlotId))
            .ShouldBe(new[] { (PersonId, "person-photo"), (LayoutId, "layout-reference") });
        box.Selection.Text.ShouldBeEmpty();
    });

    [Fact]
    public void Removing_token_emits_reference_removal_without_choosing_another_image() => WpfRendering.OnStaThread(() =>
    {
        AiPromptEditor control = new() { Document = Sample() };
        AiPromptDocument? edited = null;
        int invoked = 0;
        control.DocumentEdited += (_, e) => edited = e.Document;
        control.ImageReferenceInvoked += (_, _) => invoked++;
        var token = Tokens(Box(control)).First();
        new TextRange(token.ElementStart, token.ElementEnd).Text = "";

        edited.ShouldNotBeNull();
        edited.Parts.OfType<AiImagePart>().Single().ShouldBe(new AiImagePart(LayoutId, "layout-reference"));
        Text(edited).ShouldBe("beforeafter");
        invoked.ShouldBe(0);
    });

    [Fact]
    public void Keyboard_can_activate_both_photo_slots() => WpfRendering.OnStaThread(() =>
    {
        AiPromptEditor control = new() { Document = Sample() };
        List<string> invoked = [];
        control.ImageReferenceInvoked += (_, e) => invoked.Add(e.SlotId);
        foreach (var token in Tokens(Box(control)))
        {
            Button button = (Button)token.Child;
            button.Focusable.ShouldBeTrue();
            KeyboardNavigation.GetIsTabStop(button).ShouldBeTrue();
            AutomationProperties.GetName(button).ShouldNotBeNullOrEmpty();
            button.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new TestPresentationSource(), 0, Key.Enter)
                { RoutedEvent = Keyboard.KeyDownEvent });
        }
        invoked.ShouldBe(new[] { "person-photo", "layout-reference" });
    });

    [Theory]
    [InlineData("")]
    [InlineData("\r\n")]
    [InlineData("  ʻĀna\r\n陈 e\u0301 {{姓名}}  \nlast\r")]
    public void Projection_and_unrelated_edits_preserve_exact_newlines_without_terminal_paragraph(string literal) => WpfRendering.OnStaThread(() =>
    {
        AiPromptEditor control = new() { Document = new([new AiTextPart(TextId, literal), new AiImagePart(PersonId, "person-photo")]) };
        var box = Box(control);
        box.CaretPosition = Tokens(box).Single().ElementEnd;
        box.CaretPosition.InsertTextInRun("!");
        Text(control.Document).ShouldBe(literal + "!");
        control.Document.Parts.Select(x => x.PartId).Distinct().Count().ShouldBe(control.Document.Parts.Count);
    });

    [Fact]
    public void Undo_redo_restores_deleted_reference_and_pasted_text() => WpfRendering.OnStaThread(() =>
    {
        AiPromptEditor control = new() { Document = Sample() };
        var box = Box(control);
        var token = Tokens(box).First();
        box.Selection.Select(token.ElementStart, token.ElementEnd);
        Paste(box, "replacement\r\ne\u0301");
        control.Document.Parts.OfType<AiImagePart>().Count().ShouldBe(1);
        box.Undo();
        Text(control.Document).ShouldBe("beforeafter");
        control.Document.Parts.OfType<AiImagePart>().First().ShouldBe(new AiImagePart(PersonId, "person-photo"));
        string? invoked = null;
        control.ImageReferenceInvoked += (_, e) => invoked = e.SlotId;
        ((Button)Tokens(box).First().Child).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        invoked.ShouldBe("person-photo", "undo must restore an actionable reference");
        box.Redo();
        Text(control.Document).ShouldBe("beforereplacement\r\ne\u0301after");
        control.Document.Parts.OfType<AiImagePart>().Single().PartId.ShouldBe(LayoutId);
    });

    [Fact]
    public void Two_way_echo_preserves_caret_undo_and_does_not_emit_programmatic_edits() => WpfRendering.OnStaThread(() =>
    {
        DocumentHost host = new() { Value = Sample() };
        AiPromptEditor control = new();
        BindingOperations.SetBinding(control, AiPromptEditor.DocumentProperty,
            new Binding(nameof(DocumentHost.Value)) { Source = host, Mode = BindingMode.TwoWay });
        int edits = 0;
        control.DocumentEdited += (_, e) =>
        {
            edits++;
            // A5 may echo a frozen copy after applying the edit. It must not reset caret/history.
            host.Value = new(Array.AsReadOnly(e.Document.Parts.ToArray()));
        };
        var box = Box(control);
        box.CaretPosition = ((Run)((Paragraph)box.Document.Blocks.FirstBlock).Inlines.FirstInline).ContentStart.GetPositionAtOffset(3)!;
        Type(box, "X");
        edits.ShouldBe(1);
        host.Value.Parts.OfType<AiTextPart>().First().Text.ShouldBe("befXore");
        BindingOperations.IsDataBound(control, AiPromptEditor.DocumentProperty).ShouldBeTrue();
        new TextRange(box.Document.ContentStart, box.CaretPosition).Text.ShouldBe("befX");
        box.CanUndo.ShouldBeTrue();
        box.Undo();
        Text(control.Document).ShouldBe("beforeafter");
        edits.ShouldBe(2);
        host.Value = new([new AiTextPart(TextId, "external")]);
        edits.ShouldBe(2);
        box.CanUndo.ShouldBeFalse("external documents start a new editing history");
    });

    [Fact]
    public void Editor_renders_offscreen_without_binding_errors()
    {
        static AiPromptEditor Preview() => new()
        {
            Document = new([new AiTextPart(TextId, "姓名：  ʻĀna e\u0301 陈 {{姓名}}  \r\n人物采用 "),
                new AiImagePart(PersonId, "person-photo"), new AiTextPart(Guid.NewGuid(), "，版式参考 "),
                new AiImagePart(LayoutId, "layout-reference"), new AiTextPart(Guid.NewGuid(), "。\r\n全文可直接编辑，普通文字图3不会自动绑定照片。")]),
            ReferenceLabels = new Dictionary<string, string>
            { ["person-photo"] = "人物 · 选择照片", ["layout-reference"] = "版式 · attachment-02.png" }
        };
        WpfRendering.RenderExpectingNoBindingErrors(Preview, new Size(560, 360));
        if (Environment.GetEnvironmentVariable("PRINTFLOW_A4_EDITOR_CAPTURE") is { Length: > 0 } path)
            WpfRendering.CapturePng(Preview, new Size(560, 360), path);
    }

    [Fact]
    public void Paste_preserves_mixed_line_endings_and_cannot_create_a_reference() => WpfRendering.OnStaThread(() =>
    {
        AiPromptEditor control = new() { Document = Sample() };
        var box = Box(control);
        box.CaretPosition = Tokens(box).First().ElementStart;
        Paste(box, "\nLF\rCR\r\nCRLF {{姓名}} 图3");
        Text(control.Document).ShouldBe("before\nLF\rCR\r\nCRLF {{姓名}} 图3after");
        control.Document.Parts.OfType<AiImagePart>().Count().ShouldBe(2);
    });

    [Fact]
    public void Typed_input_and_paragraph_break_export_the_actual_final_document() => WpfRendering.OnStaThread(() =>
    {
        using TempWorkspace workspace = new();
        var module = AuthoringPersistenceTests.Module(workspace.Root);
        AiPromptEditor control = new() { Document = Sample() };
        var box = Box(control);
        var run = (Run)((Paragraph)box.Document.Blocks.FirstBlock).Inlines.FirstInline;
        box.CaretPosition = run.ContentStart.GetPositionAtOffset(3)!;
        Type(box, "e\u0301");
        // The native Enter command requires real keyboard focus. Exercise its document edit
        // primitive offscreen; physical key dispatch remains a separate validation boundary.
        using (box.DeclareChangeBlock()) box.CaretPosition = box.CaretPosition.InsertParagraphBreak();
        Type(box, "图3");
        AiAuthoringDraft draft = new()
        {
            Id = Guid.NewGuid(), Template = new("test", 1),
            TemplateSnapshot = new("test", AiProductKind.MemorialTshirt, "unused", []), Fields = new Dictionary<string, string>(),
            InitialDocument = Sample(), FinalDocument = control.Document,
            Slots = [new("person-photo", "人物", true), new("layout-reference", "版式", false)]
        };
        var export = module.Export(draft);
        export.Text.ShouldBe("befe\u0301\r\n图3ore[人物：待选择图片][版式：待选择图片]after");
        export.RequiredAttachments.ShouldBeEmpty();
        export.Issues.Count.ShouldBe(2);
        box.Undo();
        Text(control.Document).ShouldBe("befe\u0301\r\noreafter");
    });

    [Fact]
    public void Photo_label_updates_preserve_text_caret_and_undo() => WpfRendering.OnStaThread(() =>
    {
        AiPromptEditor control = new() { Document = Sample(), ReferenceLabels = new Dictionary<string, string>
            { ["person-photo"] = "人物 · 选择照片", ["layout-reference"] = "版式 · 选择照片" } };
        var box = Box(control);
        box.CaretPosition = ((Run)((Paragraph)box.Document.Blocks.FirstBlock).Inlines.FirstInline).ContentEnd;
        Type(box, "!");
        int caret = box.Document.ContentStart.GetOffsetToPosition(box.CaretPosition);
        int edits = 0; control.DocumentEdited += (_, _) => edits++;
        control.ReferenceLabels = new Dictionary<string, string> { ["person-photo"] = "人物 · attachment-02.png" };
        ((Button)Tokens(box).First().Child).Content.ShouldBe("人物 · attachment-02.png");
        box.Document.ContentStart.GetOffsetToPosition(box.CaretPosition).ShouldBe(caret);
        edits.ShouldBe(0);
        box.CanUndo.ShouldBeTrue();
        box.Undo();
        Text(control.Document).ShouldBe("beforeafter");
    });

    [Fact]
    public void Native_bullet_formatting_cannot_drop_visible_text_or_references() => WpfRendering.OnStaThread(() =>
    {
        AiPromptEditor control = new() { Document = Sample() };
        var box = Box(control);
        box.SelectAll();
        EditingCommands.ToggleBullets.CanExecute(null, box).ShouldBeTrue();
        EditingCommands.ToggleBullets.Execute(null, box);
        box.Document.Blocks.FirstBlock.ShouldBeOfType<System.Windows.Documents.List>();
        Text(control.Document).ShouldBe("beforeafter");
        control.Document.Parts.OfType<AiImagePart>().Select(x => x.PartId).ShouldBe(new[] { PersonId, LayoutId });
        var item = ((System.Windows.Documents.List)box.Document.Blocks.FirstBlock).ListItems.FirstListItem;
        var paragraph = (Paragraph)item.Blocks.FirstBlock;
        box.CaretPosition = ((Run)paragraph.Inlines.FirstInline).ContentEnd;
        Type(box, "!");
        Text(control.Document).ShouldBe("before!after");
        control.ReferenceLabels = new Dictionary<string, string> { ["person-photo"] = "人物 · 更换照片" };
        ((Button)paragraph.Inlines.OfType<InlineUIContainer>().First().Child).Content.ShouldBe("人物 · 更换照片");
        box.Undo();
        Text(control.Document).ShouldBe("beforeafter");
        control.Document.Parts.OfType<AiImagePart>().Count().ShouldBe(2);
    });

    private static void Type(RichTextBox box, string value)
    {
        box.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice,
            new TextComposition(InputManager.Current, box, value)) { RoutedEvent = TextCompositionManager.TextInputEvent });
        // WPF queues rich-text input at Background priority; drain it without a native window.
        box.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private static AiPromptDocument Sample() => new(Array.AsReadOnly<AiPromptPart>([
        new AiTextPart(TextId, "before"), new AiImagePart(PersonId, "person-photo"),
        new AiImagePart(LayoutId, "layout-reference"), new AiTextPart(Guid.NewGuid(), "after")]));
    private static string Text(AiPromptDocument document) => string.Concat(document.Parts.OfType<AiTextPart>().Select(x => x.Text));
    private static RichTextBox Box(AiPromptEditor control)
    {
        // WPF enables its text undo manager when the editing template is connected by layout.
        control.Measure(new Size(560, 360));
        control.Arrange(new Rect(0, 0, 560, 360));
        control.UpdateLayout();
        return (RichTextBox)control.FindName("Editor");
    }
    private static IEnumerable<InlineUIContainer> Tokens(RichTextBox box) => box.Document.Blocks.OfType<Paragraph>()
        .SelectMany(x => x.Inlines).OfType<InlineUIContainer>();
    private static void Paste(RichTextBox box, string value)
    {
        DataObject data = new(); data.SetText(value, TextDataFormat.UnicodeText);
        box.RaiseEvent(new DataObjectPastingEventArgs(data, false, DataFormats.UnicodeText));
    }
    private sealed class TestPresentationSource : PresentationSource
    {
        public override System.Windows.Media.Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override System.Windows.Media.CompositionTarget GetCompositionTargetCore() => null!;
    }
    private sealed class DocumentHost : DependencyObject
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(AiPromptDocument), typeof(DocumentHost));
        public AiPromptDocument Value { get => (AiPromptDocument)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    }
}
