using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Automation;
using PrintFlow.App.Resources;
using PrintFlow.Domain.AiDesign.Authoring;

namespace PrintFlow.App.Controls;

public sealed class AiPromptDocumentEditedEventArgs(AiPromptDocument document) : EventArgs
{
    public AiPromptDocument Document { get; } = document;
}

public sealed class AiImageReferenceInvokedEventArgs(string slotId) : EventArgs
{
    public string SlotId { get; } = slotId;
}

/// <summary>A document projection only. The host owns picking, binding, export and persistence.</summary>
public partial class AiPromptEditor : UserControl
{
    public static readonly DependencyProperty DocumentProperty = DependencyProperty.Register(
        nameof(Document), typeof(AiPromptDocument), typeof(AiPromptEditor),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnDocumentChanged));

    public AiPromptDocument Document
    {
        get => (AiPromptDocument?)GetValue(DocumentProperty) ?? projected;
        set => SetValue(DocumentProperty, value);
    }

    public event EventHandler<AiPromptDocumentEditedEventArgs>? DocumentEdited;
    public event EventHandler<AiImageReferenceInvokedEventArgs>? ImageReferenceInvoked;
    public static readonly DependencyProperty ReferenceLabelsProperty = DependencyProperty.Register(
        nameof(ReferenceLabels), typeof(IReadOnlyDictionary<string, string>), typeof(AiPromptEditor),
        new PropertyMetadata(null, (sender, _) => ((AiPromptEditor)sender).RefreshLabels()));

    /// <summary>Host-provided role/file labels; presentation changes never edit the document.</summary>
    public IReadOnlyDictionary<string, string>? ReferenceLabels
    {
        get => (IReadOnlyDictionary<string, string>?)GetValue(ReferenceLabelsProperty);
        set => SetValue(ReferenceLabelsProperty, value);
    }
    private bool projecting;
    private AiPromptDocument projected = new([]);
    private IReadOnlyDictionary<Guid, AiImagePart> references = new Dictionary<Guid, AiImagePart>();

    public AiPromptEditor()
    {
        InitializeComponent();
        Project(new([]));
        Editor.TextChanged += OnTextChanged;
        DataObject.AddPastingHandler(Editor, OnPaste);
        Editor.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnReferenceClick));
    }

    private static void OnDocumentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var control = (AiPromptEditor)sender;
        var document = (AiPromptDocument?)e.NewValue ?? new([]);
        if (!control.projecting && !control.projected.Parts.SequenceEqual(document.Parts)) control.Project(document);
    }

    private void Project(AiPromptDocument document)
    {
        projecting = true;
        try
        {
            projected = new(Array.AsReadOnly(document.Parts.ToArray()));
            references = projected.Parts.OfType<AiImagePart>().ToDictionary(x => x.PartId);
            Editor.Document = AiPromptEditorProjection.Create(projected);
            RefreshLabels();
        }
        finally { projecting = false; }
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (projecting) return;
        var edited = AiPromptEditorProjection.Read(Editor.Document, references);
        RefreshLabels();
        if (projected.Parts.SequenceEqual(edited.Parts)) return;
        projected = edited;
        SetCurrentValue(DocumentProperty, edited);
        DocumentEdited?.Invoke(this, new(edited));
    }

    private void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        // Accept text only: rich clipboard objects cannot create or rebind image references.
        e.CancelCommand();
        if ((e.DataObject.GetData(DataFormats.UnicodeText) ?? e.DataObject.GetData(DataFormats.Text)) is not string text) return;
        using (Editor.DeclareChangeBlock())
        {
            Editor.Selection.Text = "";
            var insertion = Editor.Selection.Start;
            // TextRange.Text normalizes LF to CRLF. Insert literal text into a Run instead.
            var end = insertion.GetPositionAtOffset(0, LogicalDirection.Forward)!;
            insertion.InsertTextInRun(text);
            Editor.CaretPosition = end;
        }
    }

    private void OnReferenceClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is Button { Tag: string identity } && Guid.TryParse(identity, out var id) &&
            projected.Parts.OfType<AiImagePart>().FirstOrDefault(x => x.PartId == id) is { } image)
        {
            e.Handled = true;
            ImageReferenceInvoked?.Invoke(this, new(image.SlotId));
        }
    }

    private void RefreshLabels()
    {
        if (Editor is null) return;
        foreach (var paragraph in AiPromptEditorProjection.Paragraphs(Editor.Document.Blocks)) Update(paragraph.Inlines);
        void Update(IEnumerable<Inline> inlines)
        {
            foreach (var inline in inlines)
            {
                if (inline is Span span) Update(span.Inlines);
                else if (inline is InlineUIContainer { Child: Button { Tag: string identity } button } &&
                    Guid.TryParse(identity, out var id) && references.TryGetValue(id, out var image))
                {
                    string label = ReferenceLabels?.GetValueOrDefault(image.SlotId) ?? Strings.AiPrompt_ChoosePhoto;
                    if (!Equals(button.Content, label)) button.Content = label;
                    AutomationProperties.SetName(button, label);
                }
            }
        }
    }
}
