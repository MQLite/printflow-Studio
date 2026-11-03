using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using PrintFlow.Domain.AiDesign.Authoring;

namespace PrintFlow.App.Controls;

/// <summary>Maps known nodes, never interpreting typed labels or template-like text.</summary>
internal static class AiPromptEditorProjection
{
    private static readonly DependencyProperty PartIdProperty = DependencyProperty.RegisterAttached(
        "PartId", typeof(Guid), typeof(AiPromptEditorProjection), new FrameworkPropertyMetadata(Guid.Empty));

    public static FlowDocument Create(AiPromptDocument document)
    {
        Paragraph paragraph = new() { Margin = new Thickness(0) };
        foreach (var part in document.Parts)
        {
            Inline inline;
            if (part is AiTextPart text) inline = new Run(text.Text);
            else if (part is AiImagePart image)
            {
                // WPF serializes embedded controls during undo. A string identity survives;
                // a record in Tag and per-button delegates do not.
                Button button = new() { Content = image.SlotId, Tag = image.PartId.ToString("D"), Focusable = true, IsTabStop = true,
                    Padding = new Thickness(5, 1, 5, 1), Margin = new Thickness(2, 0, 2, 0) };
                AutomationProperties.SetName(button, image.SlotId);
                inline = new InlineUIContainer(button) { BaselineAlignment = BaselineAlignment.Center };
            }
            else throw new ArgumentException("Unsupported prompt node.", nameof(document));
            inline.SetValue(PartIdProperty, part.PartId);
            paragraph.Inlines.Add(inline);
        }
        return new FlowDocument(paragraph);
    }

    public static AiPromptDocument Read(FlowDocument document, IReadOnlyDictionary<Guid, AiImagePart> references)
    {
        List<AiPromptPart> parts = [];
        HashSet<Guid> used = [];
        void Text(string value, Inline? inline = null)
        {
            if (value.Length == 0) return;
            Guid id = inline is null ? Guid.Empty : (Guid)inline.GetValue(PartIdProperty);
            if (parts.LastOrDefault() is AiTextPart previous && (id == Guid.Empty || id == previous.PartId))
            { parts[^1] = previous with { Text = previous.Text + value }; return; }
            if (id == Guid.Empty || used.Contains(id)) id = Guid.NewGuid();
            used.Add(id);
            inline?.SetValue(PartIdProperty, id);
            parts.Add(new AiTextPart(id, value));
        }
        void Visit(IEnumerable<Inline> inlines)
        {
            foreach (var inline in inlines)
            {
                switch (inline)
                {
                    case Run run: Text(run.Text, run); break;
                    case LineBreak line: Text("\r\n", line); break;
                    case Span span: Visit(span.Inlines); break;
                    case InlineUIContainer { Child: Button { Tag: string identity } } when
                        Guid.TryParse(identity, out var imageId) && references.TryGetValue(imageId, out var image):
                        used.Add(image.PartId); parts.Add(image); break;
                }
            }
        }
        bool first = true;
        foreach (var paragraph in Paragraphs(document.Blocks))
        {
            if (!first) Text("\r\n");
            first = false;
            Visit(paragraph.Inlines);
        }
        return new(Array.AsReadOnly(parts.ToArray()));
    }

    // Native editing commands can wrap paragraphs in lists/sections/tables even though
    // authoring persists only text and image nodes. Do not lose their visible content.
    public static IEnumerable<Paragraph> Paragraphs(BlockCollection blocks)
    {
        foreach (var block in blocks)
        {
            if (block is Paragraph paragraph) yield return paragraph;
            else
            {
                IEnumerable<BlockCollection> children = block switch
                {
                    Section section => [section.Blocks],
                    System.Windows.Documents.List list => list.ListItems.Select(x => x.Blocks),
                    Table table => table.RowGroups.SelectMany(x => x.Rows).SelectMany(x => x.Cells).Select(x => x.Blocks),
                    _ => []
                };
                foreach (var child in children)
                    foreach (var nested in Paragraphs(child)) yield return nested;
            }
        }
    }
}
